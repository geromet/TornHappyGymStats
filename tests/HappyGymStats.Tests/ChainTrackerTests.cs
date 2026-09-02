using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ChainTrackerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(25)]
    [InlineData(250)]
    [InlineData(999)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void CurrentMultiplier_never_disagrees_with_ChainEngine(int length)
    {
        var engine = new ChainEngine();
        // ChainEngine's cumulative sigma prefix increments by a*log10(n)+b per hit; the displayed
        // chain multiplier clamps that at 1.0 (it crosses 1.0 at chain 10).
        var expected = System.Math.Max(1.0, engine.SigmaMult(length) - engine.SigmaMult(length - 1));

        Assert.Equal(expected, ChainTracker.CurrentMultiplier(length), precision: 9);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(-50, 1.0)]
    [InlineData(1, 1.0)]
    [InlineData(9, 1.0)]      // still clamped just below the crossing point
    [InlineData(10, 1.0)]     // 0.25*1 + 0.75
    [InlineData(100, 1.25)]   // 0.25*2 + 0.75
    [InlineData(1000, 1.5)]   // 0.25*3 + 0.75
    [InlineData(100_000, 2.0)] // 0.25*5 + 0.75 - the stated 2x ceiling
    public void CurrentMultiplier_matches_the_scoring_formula(int length, double expected)
    {
        Assert.Equal(expected, ChainTracker.CurrentMultiplier(length), precision: 9);
    }

    [Fact]
    public void Next_milestone_is_the_smallest_milestone_strictly_above_the_current_length()
    {
        var engine = new ChainEngine();

        // At exactly 250 the 250 bonus is already banked - ChainEngine.CumBonus(250) includes it -
        // so the next milestone to chase is 500, not 250.
        Assert.Equal(310, engine.CumBonus(250));
        var state = ChainTracker.Evaluate(chainLength: 250, attackableWarTargetCount: 3);
        Assert.Equal(500, state.NextMilestone);
        Assert.Equal(250, state.HitsToNextMilestone);

        Assert.Equal(10, ChainTracker.Evaluate(0, 3).NextMilestone);
        Assert.Equal(1000, ChainTracker.Evaluate(999, 3).NextMilestone);
    }

    [Fact]
    public void Past_the_final_milestone_there_is_no_next_milestone_and_no_forfeit()
    {
        var state = ChainTracker.Evaluate(chainLength: 100_001, attackableWarTargetCount: 3);

        Assert.Null(state.NextMilestone);
        Assert.Null(state.HitsToNextMilestone);
        Assert.Equal(0, state.NextMilestoneBonus);
        Assert.Equal(0, state.ForfeitedValueIfCrossedOutside);
        Assert.False(state.IsInReservationWindow);
        Assert.Equal(ChainBoardMode.OutsideTargetsAllowed, state.Mode);
    }

    [Theory]
    [InlineData(994, false)] // 6 hits out - outside the window
    [InlineData(995, true)]  // crossing hit is the 5th - inside (window is inclusive)
    [InlineData(996, true)]
    [InlineData(999, true)]
    public void Reservation_window_is_the_last_five_hits_before_a_milestone(int length, bool expectedInWindow)
    {
        var state = ChainTracker.Evaluate(length, attackableWarTargetCount: 3);

        Assert.Equal(expectedInWindow, state.IsInReservationWindow);
        Assert.Equal(1000, state.NextMilestone);
    }

    [Fact]
    public void Reservation_window_size_is_configurable()
    {
        Assert.False(ChainTracker.Evaluate(996, 3, reservationWindowHits: 3).IsInReservationWindow);
        Assert.True(ChainTracker.Evaluate(996, 3, reservationWindowHits: 4).IsInReservationWindow);
    }

    [Fact]
    public void In_window_with_a_war_target_locks_outside_targets()
    {
        var state = ChainTracker.Evaluate(chainLength: 998, attackableWarTargetCount: 4);

        Assert.Equal(ChainBoardMode.WarTargetsOnly, state.Mode);
        Assert.Equal(640, state.NextMilestoneBonus);
        Assert.Equal(640, state.ForfeitedValueIfCrossedOutside);
        Assert.Contains("640", state.Reason);
        Assert.Contains("locked", state.Reason);
    }

    [Fact]
    public void In_window_with_no_war_target_advises_waiting_and_names_the_forfeit()
    {
        var state = ChainTracker.Evaluate(chainLength: 997, attackableWarTargetCount: 0);

        Assert.Equal(ChainBoardMode.HoldForWarTarget, state.Mode);
        Assert.Equal(640, state.ForfeitedValueIfCrossedOutside);
        Assert.Contains("640", state.Reason);
        Assert.Contains("Wait or revive", state.Reason);
    }

    [Fact]
    public void Out_of_window_with_no_war_target_permits_filler_to_sustain_the_chain()
    {
        var state = ChainTracker.Evaluate(chainLength: 300, attackableWarTargetCount: 0);

        Assert.Equal(ChainBoardMode.SustainWithFiller, state.Mode);
        Assert.False(state.IsInReservationWindow);
        Assert.Contains("half", state.Reason);
    }

    [Fact]
    public void Out_of_window_with_war_targets_is_the_normal_mode()
    {
        var state = ChainTracker.Evaluate(chainLength: 300, attackableWarTargetCount: 5);

        Assert.Equal(ChainBoardMode.OutsideTargetsAllowed, state.Mode);
        Assert.Equal(500, state.NextMilestone);
        Assert.Equal(200, state.HitsToNextMilestone);
    }

    [Fact]
    public void Negative_chain_length_is_treated_as_zero()
    {
        var state = ChainTracker.Evaluate(chainLength: -5, attackableWarTargetCount: 2);

        Assert.Equal(0, state.ChainLength);
        Assert.Equal(1.0, state.CurrentMultiplier);
        Assert.Equal(10, state.NextMilestone);
    }

    [Theory]
    [InlineData(-1)]
    public void Negative_target_count_is_rejected(int targets)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChainTracker.Evaluate(500, targets));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_reservation_window_is_rejected(int window)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChainTracker.Evaluate(500, 3, window));
    }

    [Fact]
    public void AlertLevel_is_none_outside_the_window_with_a_healthy_timer()
    {
        var state = ChainTracker.Evaluate(chainLength: 300, attackableWarTargetCount: 4);
        var timer = new ChainLapseEstimate(DateTimeOffset.UnixEpoch, 30, 270, 30, true, ChainLapseConfidence.Inferred, "");

        Assert.Equal(ChainAlertLevel.None, ChainTracker.AlertLevel(state, timer));
    }

    [Fact]
    public void AlertLevel_raises_reservation_window_when_inside_it()
    {
        var state = ChainTracker.Evaluate(chainLength: 997, attackableWarTargetCount: 4);

        Assert.Equal(ChainAlertLevel.ReservationWindow, ChainTracker.AlertLevel(state, timer: null));
    }

    [Fact]
    public void AlertLevel_timer_running_low_outranks_the_reservation_window()
    {
        var state = ChainTracker.Evaluate(chainLength: 997, attackableWarTargetCount: 4);
        var timer = new ChainLapseEstimate(
            DateTimeOffset.UnixEpoch, 260, ChainTracker.AlertTimerLowSeconds - 1, 30, true, ChainLapseConfidence.Inferred, "");

        Assert.Equal(ChainAlertLevel.TimerRunningLow, ChainTracker.AlertLevel(state, timer));
    }

    [Fact]
    public void AlertLevel_ignores_a_low_timer_that_is_only_a_None_confidence_guess()
    {
        var state = ChainTracker.Evaluate(chainLength: 300, attackableWarTargetCount: 4);
        var timer = ChainLapseEstimate.Unknown("last hit older than the window");

        Assert.Equal(ChainAlertLevel.None, ChainTracker.AlertLevel(state, timer));
    }

    [Fact]
    public void At_995_with_no_war_target_the_advice_is_wait_not_filler_and_names_the_cost()
    {
        // data/V2/handoff/06 S07 acceptance: chain in the window, nothing attackable -> hold.
        // Filler would carry the chain across chain 1000 and forfeit the 640 bonus.
        var state = ChainTracker.Evaluate(chainLength: 995, attackableWarTargetCount: 0);

        Assert.Equal(ChainBoardMode.HoldForWarTarget, state.Mode);
        Assert.Contains("Wait or revive", state.Reason);
        Assert.Contains("640", state.Reason);
    }
}
