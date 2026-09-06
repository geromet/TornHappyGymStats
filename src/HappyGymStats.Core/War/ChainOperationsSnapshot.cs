namespace HappyGymStats.Core.War;

public sealed class ChainOperationsSnapshot
{
    public ChainOperationsSnapshot(
        long factionId,
        long warId,
        long revision,
        IEnumerable<WatcherShift>? shifts = null,
        IEnumerable<WatcherCheckIn>? checkIns = null,
        IEnumerable<WatcherCheckOut>? checkOuts = null,
        IEnumerable<WatcherHandoff>? handoffs = null,
        IEnumerable<ChainAttackSlot>? attackSlots = null,
        IEnumerable<ChainOperationalEvent>? operationalEvents = null)
    {
        if (factionId <= 0) throw new ArgumentOutOfRangeException(nameof(factionId));
        if (warId <= 0) throw new ArgumentOutOfRangeException(nameof(warId));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));

        FactionId = factionId;
        WarId = warId;
        Revision = revision;
        Shifts = (shifts ?? []).OrderBy(item => item.StartsAtUtc).ThenBy(item => item.Id).ToArray();
        CheckIns = (checkIns ?? [])
            .Select(item => new WatcherCheckIn(item.ShiftId, item.OccurredAtUtc.ToUniversalTime()))
            .OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.ShiftId).ToArray();
        CheckOuts = (checkOuts ?? [])
            .Select(item => new WatcherCheckOut(item.ShiftId, item.OccurredAtUtc.ToUniversalTime()))
            .OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.ShiftId).ToArray();
        Handoffs = (handoffs ?? [])
            .Select(item => new WatcherHandoff(item.FromShiftId, item.ToShiftId, item.OccurredAtUtc.ToUniversalTime()))
            .OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.FromShiftId).ThenBy(item => item.ToShiftId).ToArray();
        AttackSlots = (attackSlots ?? []).OrderBy(item => item.StartsAtUtc).ThenBy(item => item.Id).ToArray();
        OperationalEvents = (operationalEvents ?? [])
            .Select(item => new ChainOperationalEvent(
                item.Kind,
                item.Key,
                item.OccurredAtUtc,
                item.SourceId,
                item.MemberId,
                item.EndsAtUtc))
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();

        ValidateUniqueIds(Shifts.Select(item => item.Id), "Watcher shift ids must be unique.");
        ValidateUniqueIds(AttackSlots.Select(item => item.Id), "Attack slot ids must be unique.");

        var shiftIds = Shifts.Select(item => item.Id).ToHashSet();
        if (CheckIns.Any(item => !shiftIds.Contains(item.ShiftId))
            || CheckOuts.Any(item => !shiftIds.Contains(item.ShiftId))
            || Handoffs.Any(item => !shiftIds.Contains(item.FromShiftId) || !shiftIds.Contains(item.ToShiftId)))
        {
            throw new ArgumentException("Watcher events must reference shifts in the same operational snapshot.");
        }

        if (CheckIns.Distinct().Count() != CheckIns.Count
            || CheckOuts.Distinct().Count() != CheckOuts.Count
            || Handoffs.Distinct().Count() != Handoffs.Count)
        {
            throw new ArgumentException("Watcher events must be unique so restart replay cannot duplicate them.");
        }

        var eventKeys = OperationalEvents.Select(item => item.Key).ToArray();
        if (eventKeys.Distinct(StringComparer.Ordinal).Count() != eventKeys.Length)
        {
            throw new ArgumentException("Operational event keys must be unique so restart replay cannot duplicate emissions.");
        }
    }

    public long FactionId { get; }
    public long WarId { get; }
    public long Revision { get; }
    public IReadOnlyList<WatcherShift> Shifts { get; }
    public IReadOnlyList<WatcherCheckIn> CheckIns { get; }
    public IReadOnlyList<WatcherCheckOut> CheckOuts { get; }
    public IReadOnlyList<WatcherHandoff> Handoffs { get; }
    public IReadOnlyList<ChainAttackSlot> AttackSlots { get; }
    public IReadOnlyList<ChainOperationalEvent> OperationalEvents { get; }

    private static void ValidateUniqueIds(IEnumerable<Guid> ids, string message)
    {
        var values = ids.ToArray();
        if (values.Distinct().Count() != values.Length)
            throw new ArgumentException(message);
    }
}

public interface IChainOperationsRepository
{
    Task<ChainOperationsSnapshot?> GetAsync(long factionId, long warId, CancellationToken ct);

    Task SaveAsync(
        ChainOperationsSnapshot snapshot,
        long expectedRevision,
        CancellationToken ct);
}
