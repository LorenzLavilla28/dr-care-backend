using System.Text.Json;
using DrCare.Application;
using DrCare.Domain;
using DrCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Email;

public sealed class EmailQueueService(
    ApplicationDbContext db,
    IOptions<EmailOptions> options) : IEmailQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> EnqueueAsync(QueueEmailRequest request, CancellationToken cancellationToken)
        => (await EnqueueManyAsync([request], cancellationToken))[0];

    public async Task<IReadOnlyList<Guid>> EnqueueManyAsync(IReadOnlyList<QueueEmailRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0) return [];
        var result = new List<Guid>(requests.Count);
        var added = new List<(EmailOutboxMessage Message, string? Key)>();
        foreach (var request in requests)
        {
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (idempotencyKey?.Length > 200) throw new DomainRuleException("The email idempotency key cannot exceed 200 characters.");

        if (idempotencyKey is not null)
        {
            var existingId = await db.EmailOutboxMessages
                .AsNoTracking()
                .Where(x => x.OrganizationId == request.OrganizationId && x.IdempotencyKey == idempotencyKey)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingId.HasValue) { result.Add(existingId.Value); continue; }
        }

        var attachments = request.Attachments ?? [];
        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName) || attachment.FileName.Length > 200)
                throw new DomainRuleException("Every email attachment needs a file name of 200 characters or fewer.");
            if (string.IsNullOrWhiteSpace(attachment.ContentType) || attachment.ContentType.Length > 120)
                throw new DomainRuleException("Every email attachment needs a valid content type.");
            if (string.IsNullOrWhiteSpace(attachment.ObjectKey) || attachment.ObjectKey.Length > 512)
                throw new DomainRuleException("Every email attachment needs a valid private object key.");
        }

        var message = new EmailOutboxMessage(
            request.OrganizationId,
            request.RecipientEmail,
            request.RecipientName,
            request.Subject,
            request.HtmlBody,
            request.TextBody,
            request.Category,
            JsonSerializer.Serialize(attachments, JsonOptions),
            idempotencyKey,
            options.Value.Queue.MaxAttempts,
            request.NotBefore);
        db.EmailOutboxMessages.Add(message);
        added.Add((message, idempotencyKey));
        result.Add(message.Id);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException) when (added.Any(x => x.Key is not null))
        {
            foreach (var item in added) db.Entry(item.Message).State = EntityState.Detached;
            var resolved = new List<Guid>(requests.Count);
            foreach (var request in requests)
            {
                var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
                if (key is null) throw;
                resolved.Add(await db.EmailOutboxMessages.AsNoTracking()
                    .Where(x => x.OrganizationId == request.OrganizationId && x.IdempotencyKey == key)
                    .Select(x => x.Id).SingleAsync(cancellationToken));
            }
            return resolved;
        }
    }

    public async Task CancelAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.EmailOutboxMessages.SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        if (message is null) return;
        message.Cancel();
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<QueuedEmailState?> FindAsync(Guid organizationId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.EmailOutboxMessages.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.IdempotencyKey == idempotencyKey)
            .Select(x => new QueuedEmailState(x.Status, x.CreatedAt, x.SentAt))
            .SingleOrDefaultAsync(cancellationToken);
}
