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

    [Fact]
    public void War_poller_keeps_orchestration_linear_and_policy_helpers_effect_free()
    {
        var root = ResolveRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "src/HappyGymStats.WarPoller/WarPollerService.cs"));

        var runOnce = ExtractMethodBody(service, "public async Task<WarPollerTickResult> RunOnceAsync(");
        Assert.Contains("ResolveActiveWarAsync", runOnce, StringComparison.Ordinal);
        Assert.Contains("CompleteNoActiveWarAsync", runOnce, StringComparison.Ordinal);
        Assert.Contains("CompleteActiveWarAsync", runOnce, StringComparison.Ordinal);
        Assert.Contains("CompleteCancellationAsync", runOnce, StringComparison.Ordinal);
        Assert.Contains("CompleteRetryableFailureAsync", runOnce, StringComparison.Ordinal);
        Assert.Contains("RecordFatalFailureAsync", runOnce, StringComparison.Ordinal);
        Assert.Contains("BuildHeartbeat", runOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRankedWarReportAsync", runOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaceRosterSnapshotAsync", runOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(", runOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("LogWarning(", runOnce, StringComparison.Ordinal);
        Assert.DoesNotContain("LogError(", runOnce, StringComparison.Ordinal);

        var activeCompletion = ExtractMethodBody(service, "private async Task<WarPollerTickResult> CompleteActiveWarAsync(");
        Assert.Contains("GetRankedWarReportAsync", activeCompletion, StringComparison.Ordinal);
        Assert.Contains("TryGetOurChainDeadlineAsync", activeCompletion, StringComparison.Ordinal);
        Assert.Contains("BuildPersistedState", activeCompletion, StringComparison.Ordinal);
        Assert.Contains("ReplaceRosterSnapshotAsync", activeCompletion, StringComparison.Ordinal);
        Assert.Contains("BuildHeartbeat", activeCompletion, StringComparison.Ordinal);
        Assert.Contains("TryNotifyHubAsync", activeCompletion, StringComparison.Ordinal);

        var noWarCompletion = ExtractMethodBody(service, "private async Task<WarPollerTickResult> CompleteNoActiveWarAsync(");
        Assert.Contains("UpsertCurrentAsync", noWarCompletion, StringComparison.Ordinal);
        Assert.Contains("BuildHeartbeat", noWarCompletion, StringComparison.Ordinal);
        Assert.Contains("TryNotifyHubAsync", noWarCompletion, StringComparison.Ordinal);
        Assert.DoesNotContain("_tornApiClient", noWarCompletion, StringComparison.Ordinal);

        var cancellation = ExtractMethodBody(service, "private async Task<WarPollerTickResult> CompleteCancellationAsync(");
        Assert.Contains("BuildHeartbeat", cancellation, StringComparison.Ordinal);
        Assert.Contains("phase: \"cancelled\"", cancellation, StringComparison.Ordinal);

        var retryableFailure = ExtractMethodBody(service, "private async Task<WarPollerTickResult> CompleteRetryableFailureAsync(");
        Assert.Contains("ComputeFailureBackoff", retryableFailure, StringComparison.Ordinal);
        Assert.Contains("BuildSanitizedErrorMessage", retryableFailure, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(backoff, _timeProvider, cancellationToken)", retryableFailure, StringComparison.Ordinal);
        Assert.Contains("phase: \"retryable-failure\"", retryableFailure, StringComparison.Ordinal);
        Assert.Contains("phase: \"cancelled\"", retryableFailure, StringComparison.Ordinal);

        var fatalFailure = ExtractMethodBody(service, "private async Task RecordFatalFailureAsync(");
        Assert.Contains("BuildSanitizedErrorMessage", fatalFailure, StringComparison.Ordinal);
        Assert.Contains("phase: \"failed\"", fatalFailure, StringComparison.Ordinal);
        Assert.Contains("LogError(", fatalFailure, StringComparison.Ordinal);

        var projection = ExtractMethodBody(service, "private PersistedWarState BuildPersistedState(");
        AssertEffectFree(projection);
        Assert.DoesNotContain("_timeProvider", projection, StringComparison.Ordinal);

        var heartbeat = ExtractMethodBody(service, "private WarPollerHeartbeatEntity BuildHeartbeat(");
        AssertEffectFree(heartbeat);
        Assert.DoesNotContain("_timeProvider", heartbeat, StringComparison.Ordinal);

        var backoff = ExtractMethodBody(service, "private TimeSpan ComputeFailureBackoff(");
        AssertEffectFree(backoff);
        Assert.DoesNotContain("_timeProvider", backoff, StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(service, "interface IWarPoller"));
    }

    private static void AssertEffectFree(string methodBody)
    {
        Assert.DoesNotContain("await ", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_tornApiClient", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_warStateRepository", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_unitOfWork", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_warPollerNotifier", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_logger", methodBody, StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Method signature not found: {signature}");

        var openingBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openingBrace >= 0, $"Opening brace not found for: {signature}");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return source[openingBrace..(index + 1)];
                    }
                    break;
            }
        }

        throw new InvalidOperationException($"Closing brace not found for: {signature}");
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
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
