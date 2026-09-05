using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class WarPollerSimplificationContractTests
{
    [Fact]
    public void War_poller_uses_framework_timeprovider_without_reintroducing_custom_clock_or_dead_dependency()
    {
        var root = ResolveRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src/HappyGymStats.WarPoller/WarPollerService.cs"));
        var program = File.ReadAllText(Path.Combine(root, "src/HappyGymStats.WarPoller/Program.cs"));
        var historyWorker = File.ReadAllText(Path.Combine(root, "src/HappyGymStats.WarPoller/RankedWarHistoryBackfillWorker.cs"));
        var historyHost = File.ReadAllText(Path.Combine(root, "src/HappyGymStats.WarPoller/RankedWarHistoryBackfillHostedService.cs"));

        Assert.False(File.Exists(Path.Combine(root, "src/HappyGymStats.WarPoller/WarPollerClock.cs")));

        Assert.Contains("TimeProvider timeProvider", service, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(backoff, _timeProvider, cancellationToken)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IWarPollerClock", service, StringComparison.Ordinal);
        Assert.DoesNotContain("WarPollerClock", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IImportRunRepository importRunRepository", service, StringComparison.Ordinal);

        Assert.Contains("AddSingleton(TimeProvider.System)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("IWarPollerClock", program, StringComparison.Ordinal);
        Assert.DoesNotContain("WarPollerClock", program, StringComparison.Ordinal);

        Assert.Contains("TimeProvider timeProvider", historyWorker, StringComparison.Ordinal);
        Assert.Contains("TimeProvider timeProvider", historyHost, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(result.DelayBeforeNextIteration, timeProvider, stoppingToken)", historyHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IWarPollerClock", historyWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("IWarPollerClock", historyHost, StringComparison.Ordinal);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "HappyGymStats.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
