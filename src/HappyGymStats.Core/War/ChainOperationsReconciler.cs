namespace HappyGymStats.Core.War;

public enum ChainOperationalEventKind
{
    UpcomingShift,
    MissedCheckIn,
    Handoff,
    UncoveredPeriod,
    CriticalSaveSlot,
}

public sealed class ChainOperationalEvent
{
    public ChainOperationalEvent(
        ChainOperationalEventKind kind,
        string key,
        DateTimeOffset occurredAtUtc,
        Guid? sourceId = null,
        long? memberId = null,
        DateTimeOffset? endsAtUtc = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Operational event key must be non-empty.", nameof(key));
        if (memberId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(memberId), "Member id must be positive when supplied.");

        var occurred = occurredAtUtc.ToUniversalTime();
        var ends = endsAtUtc?.ToUniversalTime();
        if (ends is not null && ends <= occurred)
            throw new ArgumentException("Operational event end must be after its start.", nameof(endsAtUtc));

        Kind = kind;
        Key = key;
        OccurredAtUtc = occurred;
        SourceId = sourceId;
        MemberId = memberId;
        EndsAtUtc = ends;
    }

    public ChainOperationalEventKind Kind { get; }
    public string Key { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public Guid? SourceId { get; }
    public long? MemberId { get; }
    public DateTimeOffset? EndsAtUtc { get; }
}

public sealed record ChainOperationsReconciliationResult(
    ChainOperationsSnapshot Snapshot,
    IReadOnlyList<ChainOperationalEvent> NewEvents)
{
    public bool HasChanges => NewEvents.Count > 0;
}

public static class ChainOperationsReconciler
{
    public static ChainOperationsReconciliationResult Reconcile(
        ChainOperationsSnapshot snapshot,
        DateTimeOffset nowUtc,
        TimeSpan checkInGrace,
        TimeSpan upcomingLead,
        TimeSpan criticalSlotLead,
        DateTimeOffset coverageWindowStartsAtUtc,
        DateTimeOffset coverageWindowEndsAtUtc,
        IReadOnlySet<long> unavailableOrReservedMemberIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(unavailableOrReservedMemberIds);
        if (checkInGrace < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(checkInGrace));
        if (upcomingLead < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(upcomingLead));
        if (criticalSlotLead < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(criticalSlotLead));

        nowUtc = nowUtc.ToUniversalTime();
        coverageWindowStartsAtUtc = coverageWindowStartsAtUtc.ToUniversalTime();
        coverageWindowEndsAtUtc = coverageWindowEndsAtUtc.ToUniversalTime();
        if (coverageWindowEndsAtUtc <= coverageWindowStartsAtUtc)
            throw new ArgumentException("Coverage window end must be after its start.", nameof(coverageWindowEndsAtUtc));

        var existingKeys = snapshot.OperationalEvents
            .Select(item => item.Key)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = new List<ChainOperationalEvent>();

        var upcomingThrough = nowUtc + upcomingLead;
        foreach (var shift in snapshot.Shifts)
        {
            if (shift.StartsAtUtc > nowUtc && shift.StartsAtUtc <= upcomingThrough)
            {
                candidates.Add(new ChainOperationalEvent(
                    ChainOperationalEventKind.UpcomingShift,
                    $"upcoming-shift:{shift.Id:N}",
                    shift.StartsAtUtc,
                    shift.Id,
                    shift.WatcherId));
            }

            if (ChainWatcherCoverage.GetState(
                    shift,
                    snapshot.CheckIns,
                    snapshot.CheckOuts,
                    nowUtc,
                    checkInGrace) == WatcherShiftState.NoShow)
            {
                var missedAt = shift.StartsAtUtc + checkInGrace;
                if (missedAt > shift.EndsAtUtc)
                    missedAt = shift.EndsAtUtc;
                candidates.Add(new ChainOperationalEvent(
                    ChainOperationalEventKind.MissedCheckIn,
                    $"missed-check-in:{shift.Id:N}",
                    missedAt,
                    shift.Id,
                    shift.WatcherId));
            }
        }

        var shiftsById = snapshot.Shifts.ToDictionary(item => item.Id);
        foreach (var handoff in snapshot.Handoffs)
        {
            var occurredAt = handoff.OccurredAtUtc.ToUniversalTime();
            if (occurredAt <= nowUtc && ChainWatcherCoverage.IsValidAdjacentHandoff(handoff, shiftsById))
            {
                candidates.Add(new ChainOperationalEvent(
                    ChainOperationalEventKind.Handoff,
                    $"handoff:{handoff.FromShiftId:N}:{handoff.ToShiftId:N}:{occurredAt.UtcDateTime.Ticks}",
                    occurredAt,
                    handoff.ToShiftId));
            }
        }

        foreach (var gap in ChainWatcherCoverage.FindGaps(
                     snapshot.Shifts,
                     coverageWindowStartsAtUtc,
                     coverageWindowEndsAtUtc))
        {
            candidates.Add(new ChainOperationalEvent(
                ChainOperationalEventKind.UncoveredPeriod,
                $"uncovered:{gap.StartsAtUtc.UtcDateTime.Ticks}:{gap.EndsAtUtc.UtcDateTime.Ticks}",
                gap.StartsAtUtc,
                endsAtUtc: gap.EndsAtUtc));
        }

        var criticalThrough = nowUtc + criticalSlotLead;
        foreach (var slot in snapshot.AttackSlots)
        {
            if (slot.StartsAtUtc < nowUtc || slot.StartsAtUtc > criticalThrough)
                continue;

            var selectedMemberId = slot.SelectCandidate(unavailableOrReservedMemberIds);
            var selectedToken = selectedMemberId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            candidates.Add(new ChainOperationalEvent(
                ChainOperationalEventKind.CriticalSaveSlot,
                $"critical-save-slot:{slot.Id:N}:{selectedToken}",
                slot.StartsAtUtc,
                slot.Id,
                selectedMemberId));
        }

        var newEvents = candidates
            .Where(item => existingKeys.Add(item.Key))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

        if (newEvents.Length == 0)
            return new ChainOperationsReconciliationResult(snapshot, newEvents);

        var updated = new ChainOperationsSnapshot(
            snapshot.FactionId,
            snapshot.WarId,
            checked(snapshot.Revision + 1),
            snapshot.Shifts,
            snapshot.CheckIns,
            snapshot.CheckOuts,
            snapshot.Handoffs,
            snapshot.AttackSlots,
            snapshot.OperationalEvents.Concat(newEvents));

        return new ChainOperationsReconciliationResult(updated, newEvents);
    }
}
