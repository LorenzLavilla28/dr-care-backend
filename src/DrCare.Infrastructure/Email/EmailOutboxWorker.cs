using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DrCare.Infrastructure.Email;

public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailOptions> options,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    private readonly EmailOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            logger.LogInformation("Email delivery is disabled. Messages may still be queued and will remain pending until Email:Enabled is true.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled)
                {
                    for (var processed = 0; processed < _options.Queue.BatchSize; processed++)
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
                        if (!await processor.ProcessNextAsync(stoppingToken)) break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The email outbox worker encountered an unexpected error and will retry.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.Queue.PollIntervalSeconds), stoppingToken);
        }
    }
}
