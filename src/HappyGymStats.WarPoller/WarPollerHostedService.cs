using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.WarPoller;

public sealed class WarPollerHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<WarPollerHostedService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<WarPollerHostedService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WarPoller worker started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var poller = scope.ServiceProvider.GetRequiredService<WarPollerService>();
                var result = await poller.RunOnceAsync(stoppingToken);

                if (stoppingToken.IsCancellationRequested || string.Equals(result.Phase, "cancelled", StringComparison.Ordinal))
                {
                    break;
                }

                if (string.Equals(result.Phase, "retryable-failure", StringComparison.Ordinal))
                {
                    continue;
                }

                if (result.DelayBeforeNextTick > TimeSpan.Zero)
                {
                    await Task.Delay(result.DelayBeforeNextTick, _timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("WarPoller worker stopping due to cancellation.");
        }
        finally
        {
            _logger.LogInformation("WarPoller worker stopped.");
        }
    }
}
