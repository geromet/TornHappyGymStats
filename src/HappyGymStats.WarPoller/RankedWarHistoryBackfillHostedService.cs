using HappyGymStats.Core.War;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.WarPoller;

/// <summary>
/// Disabled-by-default hosted wrapper around <see cref="RankedWarHistoryBackfillWorker"/>. When
/// <see cref="WarPollerOptions.RankedWarHistoryBackfillEnabled"/> is false it logs once and does
/// no Torn HTTP calls or database writes, so it is safe to deploy alongside the live-war poller
/// without consuming extra Torn quota. Exceptions from the worker are caught and turned into
/// persisted retry state rather than allowed to crash the shared WarPoller host, since a backfill
/// bug should not take down live-war polling.
/// </summary>
public sealed class RankedWarHistoryBackfillHostedService(
    IServiceScopeFactory scopeFactory,
    WarPollerOptions options,
    TimeProvider timeProvider,
    ILogger<RankedWarHistoryBackfillHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.RankedWarHistoryBackfillEnabled)
        {
            logger.LogInformation("Ranked-war history backfill is disabled; no Torn calls or database writes will be made.");
            return;
        }

        logger.LogInformation(
            "Ranked-war history backfill worker started for scope {ScopeKey}.",
            options.RankedWarHistoryBackfillScopeKey);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = scopeFactory.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<RankedWarHistoryBackfillWorker>();

                var result = await worker.RunIterationAsync(stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (result.DelayBeforeNextIteration > TimeSpan.Zero)
                {
                    await Task.Delay(result.DelayBeforeNextIteration, timeProvider, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Ranked-war history backfill worker stopping due to cancellation.");
        }
        finally
        {
            logger.LogInformation("Ranked-war history backfill worker stopped.");
        }
    }
}