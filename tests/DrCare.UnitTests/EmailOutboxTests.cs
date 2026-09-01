using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DrCare.Domain;
using DrCare.Application;
using DrCare.Infrastructure.Email;
using DrCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DrCare.UnitTests;

public sealed class EmailOutboxTests
{
    [Fact]
    public async Task Queue_persists_once_for_the_same_idempotency_key()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var service = new EmailQueueService(db, Options.Create(new EmailOptions()));
        var organizationId = Guid.NewGuid();
        var request = new QueueEmailRequest(
            organizationId,
            "lead@example.com",
            "Alex Santos",
            "Welcome",
            "<p>Hello</p>",
            IdempotencyKey: "welcome:lead-123");

        var first = await service.EnqueueAsync(request, CancellationToken.None);
        var second = await service.EnqueueAsync(request, CancellationToken.None);

        Assert.Equal(first, second);
        var queued = Assert.Single(await db.EmailOutboxMessages.ToListAsync());
        Assert.Equal(EmailDeliveryStatus.Pending, queued.Status);
        Assert.Equal("welcome:lead-123", queued.IdempotencyKey);
    }

    [Fact]
    public void Delivery_failure_retries_then_becomes_terminal()
    {
        var message = CreateMessage(maxAttempts: 2);
        var now = DateTimeOffset.UtcNow;

        message.StartAttempt(now);
        message.RecordFailure("temporary", now, now.AddMinutes(1));

        Assert.Equal(EmailDeliveryStatus.Pending, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("temporary", message.LastError);

        message.StartAttempt(now.AddMinutes(1));
        message.RecordFailure("permanent", now.AddMinutes(1), now.AddMinutes(2));

        Assert.Equal(EmailDeliveryStatus.Failed, message.Status);
        Assert.Equal(2, message.AttemptCount);
    }

    [Fact]
    public void Successful_delivery_records_sent_time()
    {
        var message = CreateMessage(maxAttempts: 3);
        var now = DateTimeOffset.UtcNow;

        message.StartAttempt(now);
        message.MarkSent(now.AddSeconds(1));

        Assert.Equal(EmailDeliveryStatus.Sent, message.Status);
        Assert.NotNull(message.SentAt);
        Assert.Null(message.LastError);
    }

    [Fact]
    public async Task Microsoft_graph_sender_gets_app_token_and_posts_html_with_attachment()
    {
        var handler = new GraphHandler(HttpStatusCode.Accepted);
        var sender = new MicrosoftGraphEmailSender(
            new TestHttpClientFactory(handler),
            Options.Create(EnabledOptions()));

        await sender.SendAsync(new EmailDeliveryMessage(
            "lead@example.com",
            "Alex Santos",
            "Welcome",
            "<p>Hello</p>",
            "Hello",
            [new EmailDeliveryAttachment("invoice.pdf", "application/pdf", [1, 2, 3])]),
            CancellationToken.None);

        Assert.Contains("client_id=test-client", handler.TokenRequestBody);
        Assert.Equal("Bearer", handler.GraphAuthorization?.Scheme);
        Assert.Equal("test-token", handler.GraphAuthorization?.Parameter);
        Assert.Contains("lead@example.com", handler.GraphRequestBody);
        Assert.Contains("#microsoft.graph.fileAttachment", handler.GraphRequestBody);
        Assert.Contains("AQID", handler.GraphRequestBody);
    }

    [Fact]
    public async Task Microsoft_graph_failure_is_reported_to_the_outbox_processor()
    {
        var sender = new MicrosoftGraphEmailSender(
            new TestHttpClientFactory(new GraphHandler(HttpStatusCode.Forbidden)),
            Options.Create(EnabledOptions()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(
            new EmailDeliveryMessage("lead@example.com", null, "Subject", "<p>Body</p>", null, []),
            CancellationToken.None));

        Assert.Contains("sendMail failed (403)", error.Message);
    }

    [Fact]
    public async Task Microsoft_graph_sender_omits_empty_attachments_instead_of_sending_null()
    {
        var handler = new GraphHandler(HttpStatusCode.Accepted);
        var sender = new MicrosoftGraphEmailSender(new TestHttpClientFactory(handler), Options.Create(EnabledOptions()));

        await sender.SendAsync(new EmailDeliveryMessage("lead@example.com", null, "Subject", "<p>Body</p>", null, []), CancellationToken.None);

        Assert.DoesNotContain("\"attachments\"", handler.GraphRequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Queue_can_cancel_a_scheduled_message()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var service = new EmailQueueService(db, Options.Create(new EmailOptions()));
        var id = await service.EnqueueAsync(new QueueEmailRequest(Guid.NewGuid(), "lead@example.com", null, "Reminder", "<p>Reminder</p>",
            IdempotencyKey: "signing:1:reminder", NotBefore: DateTimeOffset.UtcNow.AddDays(1)), CancellationToken.None);

        await service.CancelAsync(id, CancellationToken.None);

        Assert.Equal(EmailDeliveryStatus.Cancelled, (await db.EmailOutboxMessages.SingleAsync()).Status);
    }

    [Fact]
    public async Task Queue_exposes_the_authoritative_sent_timestamp_by_business_key()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new ApplicationDbContext(dbOptions);
        var service = new EmailQueueService(db, Options.Create(new EmailOptions()));
        var organizationId = Guid.NewGuid();
        await service.EnqueueAsync(new QueueEmailRequest(organizationId, "lead@example.com", null, "Welcome", "<p>Welcome</p>", IdempotencyKey: "lead:123:welcome"), CancellationToken.None);
        var message = await db.EmailOutboxMessages.SingleAsync();
        var sentAt = DateTimeOffset.UtcNow;
        message.StartAttempt(sentAt.AddSeconds(-1)); message.MarkSent(sentAt); await db.SaveChangesAsync();

        var state = await service.FindAsync(organizationId, "lead:123:welcome", CancellationToken.None);

        Assert.NotNull(state); Assert.Equal(EmailDeliveryStatus.Sent, state!.Status); Assert.Equal(sentAt, state.SentAt);
    }

    private static EmailOutboxMessage CreateMessage(int maxAttempts) => new(
        Guid.NewGuid(),
        "lead@example.com",
        "Alex Santos",
        "Subject",
        "<p>Body</p>",
        "Body",
        "test",
        "[]",
        "test-key",
        maxAttempts);

    private static EmailOptions EnabledOptions() => new()
    {
        Enabled = true,
        TenantId = "test-tenant",
        ClientId = "test-client",
        ClientSecret = "test-secret",
        FromAddress = "mailbox@example.com",
        FromName = "Dr. Care"
    };

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler, disposeHandler: false);
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class GraphHandler(HttpStatusCode sendStatus) : HttpMessageHandler
    {
        public string TokenRequestBody { get; private set; } = string.Empty;
        public string GraphRequestBody { get; private set; } = string.Empty;
        public AuthenticationHeaderValue? GraphAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host.Equals("login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
            {
                TokenRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"test-token\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                };
            }

            GraphAuthorization = request.Headers.Authorization;
            GraphRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(sendStatus)
            {
                Content = new StringContent(sendStatus == HttpStatusCode.Accepted ? string.Empty : "denied")
            };
        }
    }
}
