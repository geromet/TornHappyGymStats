using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ChainAttackSlotTests
{
    private static readonly DateTimeOffset SlotTime = new(2026, 9, 6, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Candidate_order_is_primary_then_backups()
    {
        var slot = NewSlot(10, 20, 30);

        Assert.Equal([10L, 20L, 30L], slot.CandidateOrder);
    }

    [Fact]
    public void SelectCandidate_keeps_primary_when_available()
    {
        var slot = NewSlot(10, 20, 30);

        var selected = slot.SelectCandidate(new HashSet<long> { 20 });

        Assert.Equal(10, selected);
    }

    [Fact]
    public void SelectCandidate_promotes_backups_in_declared_order()
    {
        var slot = NewSlot(10, 20, 30, 40);

        Assert.Equal(20, slot.SelectCandidate(new HashSet<long> { 10 }));
        Assert.Equal(30, slot.SelectCandidate(new HashSet<long> { 10, 20 }));
        Assert.Equal(40, slot.SelectCandidate(new HashSet<long> { 10, 20, 30 }));
    }

    [Fact]
    public void SelectCandidate_returns_null_when_every_candidate_is_unavailable()
    {
        var slot = NewSlot(10, 20, 30);

        var selected = slot.SelectCandidate(new HashSet<long> { 10, 20, 30 });

        Assert.Null(selected);
    }

    [Fact]
    public void Slot_normalizes_time_to_utc()
    {
        var local = new DateTimeOffset(2026, 9, 6, 5, 0, 0, TimeSpan.FromHours(2));
        var slot = new ChainAttackSlot(Guid.NewGuid(), local, 10, [20]);

        Assert.Equal(SlotTime, slot.StartsAtUtc);
    }

    [Fact]
    public void Slot_rejects_duplicate_or_primary_backup_members()
    {
        Assert.Throws<ArgumentException>(() => new ChainAttackSlot(Guid.NewGuid(), SlotTime, 10, [10]));
        Assert.Throws<ArgumentException>(() => new ChainAttackSlot(Guid.NewGuid(), SlotTime, 10, [20, 20]));
    }

    [Fact]
    public void Slot_rejects_invalid_member_ids()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainAttackSlot(Guid.NewGuid(), SlotTime, 0, [20]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChainAttackSlot(Guid.NewGuid(), SlotTime, 10, [0]));
    }

    private static ChainAttackSlot NewSlot(long primary, params long[] backups)
        => new(Guid.NewGuid(), SlotTime, primary, backups);
}
