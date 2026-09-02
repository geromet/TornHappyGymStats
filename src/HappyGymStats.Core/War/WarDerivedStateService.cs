using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

public sealed class WarDerivedStateService(
    IWarStateRepository repository,
    TimeProvider? timeProvider = null,
    WarStateDerivationEngine? derivationEngine = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly WarStateDerivationEngine _derivationEngine = derivationEngine ?? new();

    public async Task<WarDerivedState> GetCurrentAsync(
        string scopeKey,
        IReadOnlyCollection<long>? idleAttackerIds = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ct.ThrowIfCancellationRequested();

        var asOfUtc = _timeProvider.GetUtcNow();
        var heartbeat = await repository.GetHeartbeatAsync(scopeKey, ct);
        var current = await repository.GetCurrentAsync(scopeKey, ct);

        if (current?.WarId is not long warId)
        {
            return BuildDerivedState(
                new WarDerivedState { AsOfUtc = asOfUtc, CoverageRatio = 1m },
                heartbeat,
                ["No current war is available for the requested scope."]);
        }

        var rosterRows = await repository.GetRosterSnapshotAsync(warId, ct);
        var scoreSamples = await repository.GetScoreSamplesAsync(warId, ct);
        var derived = _derivationEngine.Derive(rosterRows, scoreSamples, asOfUtc, idleAttackerIds);

        return BuildDerivedState(derived with { WarId = warId }, heartbeat, []);
    }

    private WarDerivedState BuildDerivedState(
        WarDerivedState derived,
        WarPollerHeartbeatEntity? heartbeat,
        IReadOnlyCollection<string> extraWarnings)
    {
        var warnings = derived.Warnings.Concat(extraWarnings).ToList();
        var isHeartbeatStale = heartbeat?.StaleAfterUtc is DateTimeOffset staleAfterUtc && staleAfterUtc <= derived.AsOfUtc;

        if (heartbeat is null)
        {
            warnings.Add("No war poller heartbeat is available for the requested scope.");
        }
        else if (isHeartbeatStale)
        {
            warnings.Add($"Heartbeat phase '{heartbeat.Phase}' is stale as of {derived.AsOfUtc:O}.");
        }

        return derived with
        {
            HeartbeatPhase = heartbeat?.Phase,
            HeartbeatUpdatedAtUtc = heartbeat?.UpdatedAtUtc,
            HeartbeatPollStartedAtUtc = heartbeat?.PollStartedAtUtc,
            HeartbeatPollCompletedAtUtc = heartbeat?.PollCompletedAtUtc,
            HeartbeatStaleAfterUtc = heartbeat?.StaleAfterUtc,
            IsHeartbeatStale = isHeartbeatStale,
            HeartbeatLastError = heartbeat?.LastError,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }
}
