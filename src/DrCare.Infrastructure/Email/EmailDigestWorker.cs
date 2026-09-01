using DrCare.Application;
using DrCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Email;

public sealed class EmailDigestWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> options, ILogger<EmailDigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await QueueDueDigestsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Email digest scheduling failed."); }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task QueueDueDigestsAsync(CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled || !config.Digests.Enabled) return;
        var localNow = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        if (localNow.Hour != config.Digests.HourLocal) return;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IEmailQueue>();
        var composer = scope.ServiceProvider.GetRequiredService<IEmailComposer>();
        var unread = await db.Notifications.AsNoTracking().Where(x => !x.Read)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        foreach (var group in unread.GroupBy(x => x.RecipientId).Where(x => x.Count() >= config.Digests.MinimumUnread))
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == group.Key && x.IsActive, ct);
            if (user is null) continue;
            var items = group.Take(10).ToArray();
            var content = composer.Compose(new BrandedEmailRequest(null, $"You have {group.Count()} unread Dr. Care notifications.", "Your daily work digest", $"Hello {user.DisplayName},",
                [$"You have {group.Count()} unread notifications in the Dr. Care workspace.", "Open the workspace to review and act on the items assigned to you."], "Open workspace", scope.ServiceProvider.GetRequiredService<IApplicationLinks>().Login(),
                items.Select(x => new KeyValuePair<string, string>(x.Title, x.Message)).ToArray()));
            await queue.EnqueueAsync(new QueueEmailRequest(user.OrganizationId, user.Email, user.DisplayName, $"Dr. Care daily digest — {localNow:MMM d}", content.Html, content.Text,
                "internal-digest", IdempotencyKey: $"digest:{user.Id}:{localNow:yyyy-MM-dd}"), ct);
        }
    }
}
