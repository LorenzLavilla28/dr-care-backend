using System.Text.Json;
using DrCare.Application;
using DrCare.Domain;
using DrCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Email;

public sealed class EmailOutboxProcessor(
    ApplicationDbContext db,
    IPrivateObjectStorage storage,
    IEmailDeliverySender sender,
    IOptions<EmailOptions> options,
    ILogger<EmailOutboxProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EmailQueueOptions _queueOptions = options.Value.Queue;

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var message = await ClaimNextAsync(cancellationToken);
        if (message is null) return false;

        try
        {
            var attachmentReferences = JsonSerializer.Deserialize<IReadOnlyList<QueuedEmailAttachment>>(message.AttachmentsJson, JsonOptions) ?? [];
            var attachments = new List<EmailDeliveryAttachment>(attachmentReferences.Count);
            var totalBytes = 0L;
            foreach (var attachment in attachmentReferences)
            {
                await using var source = await storage.OpenReadAsync(attachment.ObjectKey, cancellationToken);
                using var buffer = new MemoryStream();
                await source.CopyToAsync(buffer, cancellationToken);
                totalBytes += buffer.Length;
                if (totalBytes > _queueOptions.MaxTotalAttachmentBytes)
                    throw new InvalidOperationException($"Email attachments exceed the configured {_queueOptions.MaxTotalAttachmentBytes:N0}-byte limit.");
                attachments.Add(new EmailDeliveryAttachment(attachment.FileName, attachment.ContentType, buffer.ToArray()));
            }

            await sender.SendAsync(new EmailDeliveryMessage(
                message.RecipientEmail,
                message.RecipientName,
                message.Subject,
                message.HtmlBody,
                message.TextBody,
                attachments), cancellationToken);
            message.MarkSent(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Email outbox message {EmailId} was sent. Category: {Category}; attempt: {Attempt}.", message.Id, message.Category, message.AttemptCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var now = DateTimeOffset.UtcNow;
            var delaySeconds = Math.Min(3600, _queueOptions.BaseRetrySeconds * Math.Pow(2, Math.Max(0, message.AttemptCount - 1)));
            message.RecordFailure(ex.Message, now, now.AddSeconds(delaySeconds));
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Email outbox message {EmailId} failed on attempt {Attempt} of {MaxAttempts}. Status: {Status}.", message.Id, message.AttemptCount, message.MaxAttempts, message.Status);
        }

        return true;
    }

    private async Task<EmailOutboxMessage?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now.AddMinutes(-_queueOptions.ProcessingTimeoutMinutes);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var message = await db.EmailOutboxMessages
            .FromSqlInterpolated($$"""
                SELECT * FROM "EmailOutboxMessages"
                WHERE ("Status" = 'Pending' AND "AvailableAt" <= {{now}})
                   OR ("Status" = 'Processing' AND "ProcessingStartedAt" < {{staleBefore}})
                ORDER BY "AvailableAt", "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        message.StartAttempt(now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message;
    }
}
