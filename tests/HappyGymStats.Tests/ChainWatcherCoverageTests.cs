using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ChainWatcherCoverageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Shift_uses_half_open_utc_window()
    {
        var shift = NewShift(1, T0, T0.AddHours(1));

        Assert.True(shift.Covers(T0));
        Assert.True(shift.Covers(T0.AddMinutes(59)));
        Assert.False(shift.Covers(T0.AddHours(1)));
    }

    [Fact]
    public void Shift_normalizes_non_utc_offsets()
    {
        var start = new DateTimeOffset(2026, 9, 6, 2, 0, 0, TimeSpan.FromHours(2));
        var end = start.AddHours(1);

        var shift = NewShift(1, start, end);

        Assert.Equal(T0, shift.StartsAtUtc);
        Assert.Equal(T0.AddHours(1), shift.EndsAtUtc);
    }

    [Fact]
    public void Adjacent_shifts_do_not_create_a_gap()
    {
        var shifts = new[]
        {
            NewShift(1, T0, T0.AddHours(1)),
            NewShift(2, T0.AddHours(1), T0.AddHours(2)),
        };

        var gaps = ChainWatcherCoverage.FindGaps(shifts, T0, T0.AddHours(2));

        Assert.Empty(gaps);
    }

    [Fact]
    public void Overlapping_shifts_are_unioned_before_gap_detection()
    {
        var shifts = new[]
        {
            NewShift(1, T0.AddMinutes(10), T0.AddMinutes(50)),
            NewShift(2, T0.AddMinutes(40), T0.AddMinutes(80)),
        };

        var gaps = ChainWatcherCoverage.FindGaps(shifts, T0, T0.AddMinutes(100));

        Assert.Equal(2, gaps.Count);
        Assert.Equal(new WatcherCoverageGap(T0, T0.AddMinutes(10)), gaps[0]);
        Assert.Equal(new WatcherCoverageGap(T0.AddMinutes(80), T0.AddMinutes(100)), gaps[1]);
    }

    [Fact]
    public void No_shifts_returns_the_entire_window_as_uncovered()
    {
        var gaps = ChainWatcherCoverage.FindGaps([], T0, T0.AddMinutes(30));

        var gap = Assert.Single(gaps);
        Assert.Equal(TimeSpan.FromMinutes(30), gap.Duration);
    }

    [Fact]
    public void ActiveAt_switches_watchers_exactly_at_adjacent_boundary()
    {
        var first = NewShift(1, T0, T0.AddHours(1));
        var second = NewShift(2, T0.AddHours(1), T0.AddHours(2));

        var active = ChainWatcherCoverage.ActiveAt([first, second], T0.AddHours(1));

        Assert.Equal([second.Id], active.Select(shift => shift.Id).ToArray());
    }

    [Fact]
    public void State_is_awaiting_checkin_during_grace_then_no_show()
    {
        var shift = NewShift(1, T0, T0.AddHours(1));
        var grace = TimeSpan.FromMinutes(5);

        Assert.Equal(
            WatcherShiftState.AwaitingCheckIn,
            ChainWatcherCoverage.GetState(shift, [], [], T0.AddMinutes(4), grace));
        Assert.Equal(
            WatcherShiftState.NoShow,
            ChainWatcherCoverage.GetState(shift, [], [], T0.AddMinutes(5), grace));
    }

    [Fact]
    public void Valid_checkin_keeps_shift_active_until_checkout_or_end()
    {
        var shift = NewShift(1, T0, T0.AddHours(1));
        var checkIns = new[] { new WatcherCheckIn(shift.Id, T0.AddMinutes(2)) };

        Assert.Equal(
            WatcherShiftState.Active,
            ChainWatcherCoverage.GetState(shift, checkIns, [], T0.AddMinutes(20), TimeSpan.FromMinutes(5)));

        var checkOuts = new[] { new WatcherCheckOut(shift.Id, T0.AddMinutes(30)) };
        Assert.Equal(
            WatcherShiftState.Completed,
            ChainWatcherCoverage.GetState(shift, checkIns, checkOuts, T0.AddMinutes(30), TimeSpan.FromMinutes(5)));
        Assert.Equal(
            WatcherShiftState.Completed,
            ChainWatcherCoverage.GetState(shift, checkIns, [], T0.AddHours(1), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Checkin_outside_shift_does_not_hide_no_show()
    {
        var shift = NewShift(1, T0, T0.AddHours(1));
        var checkIns = new[] { new WatcherCheckIn(shift.Id, T0.AddHours(1)) };

        var state = ChainWatcherCoverage.GetState(
            shift,
            checkIns,
            [],
            T0.AddMinutes(10),
            TimeSpan.FromMinutes(5));

        Assert.Equal(WatcherShiftState.NoShow, state);
    }

    [Fact]
    public void Adjacent_handoff_requires_exact_shared_boundary()
    {
        var first = NewShift(1, T0, T0.AddHours(1));
        var second = NewShift(2, T0.AddHours(1), T0.AddHours(2));
        var shifts = new Dictionary<Guid, WatcherShift>
        {
            [first.Id] = first,
            [second.Id] = second,
        };

        Assert.True(ChainWatcherCoverage.IsValidAdjacentHandoff(
            new WatcherHandoff(first.Id, second.Id, T0.AddHours(1)),
            shifts));
        Assert.False(ChainWatcherCoverage.IsValidAdjacentHandoff(
            new WatcherHandoff(first.Id, second.Id, T0.AddHours(1).AddSeconds(1)),
            shifts));
    }

    [Fact]
    public void Invalid_shift_and_window_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => NewShift(1, T0, T0));
        Assert.Throws<ArgumentException>(() => ChainWatcherCoverage.FindGaps([], T0, T0));
    }

    private static WatcherShift NewShift(long watcherId, DateTimeOffset start, DateTimeOffset end)
        => new(Guid.NewGuid(), watcherId, start, end);
}
