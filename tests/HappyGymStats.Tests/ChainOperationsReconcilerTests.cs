using HappyGymStats.Core.War;

namespace HappyGymStats.Tests;

public sealed class ChainOperationsReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 10, 10, 0, TimeSpan.Zero);
    private static readonly Guid FirstShiftId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondShiftId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid UpcomingShiftId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid SlotId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    [Fact]
    public void Reconcile_emits_deterministic_operational_events_without_owning_reservations()
    {
        var snapshot = Snapshot();

        var result = Reconcile(snapshot, new HashSet<long> { 41 });

        Assert.True(result.HasChanges);
        Assert.Equal(2, result.Snapshot.Revision);
        Assert.Contains(result.NewEvents, item =>
            item.Kind == ChainOperationalEventKind.UpcomingShift
            && item.SourceId == UpcomingShiftId
            && item.MemberId == 13);
        Assert.Contains(result.NewEvents, item =>
            item.Kind == ChainOperationalEventKind.MissedCheckIn
            && item.SourceId == SecondShiftId);
        Assert.Contains(result.NewEvents, item => item.Kind == ChainOperationalEventKind.Handoff);

        var gap = Assert.Single(result.NewEvents.Where(item =>
            item.Kind == ChainOperationalEventKind.UncoveredPeriod));
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero), gap.OccurredAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 10, 30, 0, TimeSpan.Zero), gap.EndsAtUtc);

        var critical = Assert.Single(result.NewEvents.Where(item =>
            item.Kind == ChainOperationalEventKind.CriticalSaveSlot));
        Assert.Equal(SlotId, critical.SourceId);
        Assert.Equal(42, critical.MemberId);
        Assert.False(critical.Key.Contains("energy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Restart_reconciliation_is_idempotent_when_emitted_keys_were_persisted()
    {
        var first = Reconcile(Snapshot(), new HashSet<long> { 41 });

        var restarted = Reconcile(first.Snapshot, new HashSet<long> { 41 });

        Assert.False(restarted.HasChanges);
        Assert.Empty(restarted.NewEvents);
        Assert.Same(first.Snapshot, restarted.Snapshot);
        Assert.Equal(2, restarted.Snapshot.Revision);
    }

    [Fact]
    public void Candidate_change_emits_one_new_critical_slot_event_in_backup_order()
    {
        var first = Reconcile(Snapshot(), new HashSet<long> { 41 });

        var second = Reconcile(first.Snapshot, new HashSet<long> { 41, 42 });

        var promoted = Assert.Single(second.NewEvents);
        Assert.Equal(ChainOperationalEventKind.CriticalSaveSlot, promoted.Kind);
        Assert.Equal(43, promoted.MemberId);
        Assert.Equal(3, second.Snapshot.Revision);
    }

    [Fact]
    public void Snapshot_rejects_duplicate_operational_event_keys()
    {
        var duplicateA = new ChainOperationalEvent(
            ChainOperationalEventKind.UpcomingShift,
            "same-key",
            Now,
            UpcomingShiftId,
            13);
        var duplicateB = new ChainOperationalEvent(
            ChainOperationalEventKind.MissedCheckIn,
            "same-key",
            Now.AddMinutes(1),
            SecondShiftId,
            12);

        Assert.Throws<ArgumentException>(() => new ChainOperationsSnapshot(
            100,
            200,
            1,
            operationalEvents: [duplicateA, duplicateB]));
    }

    [Fact]
    public void Reconcile_validates_windows_and_lead_times_before_changing_revision()
    {
        var snapshot = Snapshot();

        Assert.Throws<ArgumentOutOfRangeException>(() => ChainOperationsReconciler.Reconcile(
            snapshot,
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(-1),
            TimeSpan.FromMinutes(20),
            Now,
            Now.AddHours(1),
            new HashSet<long>()));

        Assert.Throws<ArgumentException>(() => ChainOperationsReconciler.Reconcile(
            snapshot,
            Now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(20),
            Now,
            Now,
            new HashSet<long>()));
        Assert.Equal(1, snapshot.Revision);
    }

    private static ChainOperationsReconciliationResult Reconcile(
        ChainOperationsSnapshot snapshot,
        IReadOnlySet<long> unavailableOrReserved) =>
        ChainOperationsReconciler.Reconcile(
            snapshot,
            Now,
            checkInGrace: TimeSpan.FromMinutes(5),
            upcomingLead: TimeSpan.FromMinutes(30),
            criticalSlotLead: TimeSpan.FromMinutes(20),
            coverageWindowStartsAtUtc: new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero),
            coverageWindowEndsAtUtc: new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.Zero),
            unavailableOrReservedMemberIds: unavailableOrReserved);

    private static ChainOperationsSnapshot Snapshot()
    {
        var first = new WatcherShift(
            FirstShiftId,
            11,
            new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero));
        var second = new WatcherShift(
            SecondShiftId,
            12,
            new DateTimeOffset(2026, 9, 6, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero));
        var upcoming = new WatcherShift(
            UpcomingShiftId,
            13,
            new DateTimeOffset(2026, 9, 6, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.Zero));

        return new ChainOperationsSnapshot(
            factionId: 100,
            warId: 200,
            revision: 1,
            shifts: [first, second, upcoming],
            checkIns: null,
            checkOuts: null,
            handoffs: [new WatcherHandoff(FirstShiftId, SecondShiftId, first.EndsAtUtc)],
            attackSlots: [new ChainAttackSlot(SlotId, Now.AddMinutes(10), 41, [42, 43])]);
    }
}
