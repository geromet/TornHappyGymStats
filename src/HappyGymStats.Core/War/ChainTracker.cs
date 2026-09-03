namespace HappyGymStats.Core.War;

/// <summary>
/// How the board should treat outside (non-war) targets right now. This is an <b>eligibility</b>
/// decision only - which specific target to hit is M010's job
/// (<c>data/V2/handoff/06-milestone-3-chain-command.md</c>, "Out of scope").
/// </summary>
public enum ChainBoardMode
{
    /// <summary>War targets and outside targets are both eligible; the board should still prefer
    /// war targets.</summary>
    OutsideTargetsAllowed,

    /// <summary>Inside the milestone reservation window with at least one attackable war target -
    /// outside targets are locked so the crossing hit lands on a war target.</summary>
    WarTargetsOnly,

    /// <summary>Inside the reservation window with <b>no</b> attackable war target. Filler on an
    /// outside target would carry the chain across the milestone and forfeit its bonus, so the
    /// advice is to wait or revive rather than hit. Counter-intuitive by design - the forfeited
    /// value is in <see cref="ChainTrackerState.Reason"/>.</summary>
    HoldForWarTarget,

    /// <summary>Outside the reservation window with no attackable war target. Outside filler is the
    /// only way to keep the chain alive; it scores roughly half (<c>war = 1</c>, not <c>2</c>).</summary>
    SustainWithFiller,
}

/// <summary>
/// The loudest single chain signal for a faction, in ascending urgency. A distinct <c>ChainAlert</c>
/// <b>event</b> pushed only to assigned watchers (<c>data/V2/handoff/06</c>, task 4) is deferred —
/// <see cref="WarHub"/> has no per-war groups or per-user targeting (M1 task 10 listed them; the
/// board shipped broadcasting full state to all). Until watchers exist (M008 S06, itself blocked on
/// those groups) this level rides in the broadcast state and the board renders a banner from it.
/// </summary>
public enum ChainAlertLevel
{
    /// <summary>Nothing to flag.</summary>
    None,

    /// <summary>Inside the milestone reservation window — the crossing hit is reserved for a war
    /// target (or the chain should hold if none is attackable).</summary>
    ReservationWindow,

    /// <summary>The inferred lapse timer is under <see cref="ChainTracker.AlertTimerLowSeconds"/> —
    /// the chain is close to dropping. Outranks <see cref="ReservationWindow"/>.</summary>
    TimerRunningLow,
}

/// <summary>
/// A pure snapshot of chain state for the board: the current multiplier, the next milestone and
/// distance to it, the reservation state, and what the crossing hit is worth. No timer concept -
/// the lapse timer arrives in a later slice once its data source is confirmed.
/// </summary>
public sealed record ChainTrackerState(
    int ChainLength,
    double CurrentMultiplier,
    int? NextMilestone,
    int? HitsToNextMilestone,
    int NextMilestoneBonus,
    bool IsInReservationWindow,
    int ForfeitedValueIfCrossedOutside,
    int AttackableWarTargetCount,
    ChainBoardMode Mode,
    string Reason);

/// <summary>
/// Pure chain-command logic (<c>data/V2/handoff/06</c>, task 1): given a chain length and how many
/// war targets are attackable, produce the multiplier, next milestone, hits remaining, reservation
/// state and forfeited value. Multiplier and milestones come from <see cref="ChainEngine"/> so the
/// two models cannot disagree.
/// </summary>
public static class ChainTracker
{
    /// <summary>Inside this many hits of a milestone, the crossing hit is reserved for a war target.
    /// <c>data/V2/handoff/06</c> starts it at 5; a planner can override per war.</summary>
    public const int DefaultReservationWindowHits = 5;

    /// <summary>At or below this many seconds to lapse, the board raises
    /// <see cref="ChainAlertLevel.TimerRunningLow"/>. Sized for the INFERRED path: one score-poll
    /// spacing (~30 s) is noise, so this sits well above it to survive the ± error bar. An exact
    /// deadline needs no such margin, but it uses the same threshold deliberately — two thresholds
    /// would mean the alert moved when the data source changed, which reads as a bug to anyone
    /// watching the board.</summary>
    public const int AlertTimerLowSeconds = 90;

    /// <summary>The loudest chain signal for the board: timer-about-to-lapse beats
    /// reservation-window beats nothing. Pure — no I/O, no clock.</summary>
    public static ChainAlertLevel AlertLevel(ChainTrackerState state, ChainLapseEstimate? timer)
    {
        ArgumentNullException.ThrowIfNull(state);

        // Exact as well as Inferred. This gate was written when Inferred was the only
        // confidence that carried a number, so adding ChainLapseConfidence.Exact would
        // otherwise have made the BETTER signal silent — a real Torn deadline ticking down
        // to zero with no alert, while a guess at the same number raised one.
        if (timer is { SecondsUntilLapse: int left }
            && timer.Confidence is ChainLapseConfidence.Inferred or ChainLapseConfidence.Exact
            && left <= AlertTimerLowSeconds)
        {
            return ChainAlertLevel.TimerRunningLow;
        }

        return state.IsInReservationWindow ? ChainAlertLevel.ReservationWindow : ChainAlertLevel.None;
    }

    public static ChainTrackerState Evaluate(
        int chainLength,
        int attackableWarTargetCount,
        int reservationWindowHits = DefaultReservationWindowHits)
    {
        if (attackableWarTargetCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attackableWarTargetCount), attackableWarTargetCount, "Attackable war-target count cannot be negative.");
        }

        if (reservationWindowHits < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservationWindowHits), reservationWindowHits, "Reservation window must be at least 1 hit.");
        }

        var length = Math.Max(0, chainLength);
        var multiplier = CurrentMultiplier(length);

        var nextMilestone = NextMilestoneAfter(length);
        int? hitsToNext = nextMilestone is int milestone ? milestone - length : null;
        var nextBonus = nextMilestone is int m ? BonusForMilestone(m) : 0;

        var inWindow = hitsToNext is int hits && hits <= reservationWindowHits;
        var hasWarTarget = attackableWarTargetCount > 0;

        var mode = (inWindow, hasWarTarget) switch
        {
            (true, true) => ChainBoardMode.WarTargetsOnly,
            (true, false) => ChainBoardMode.HoldForWarTarget,
            (false, false) => ChainBoardMode.SustainWithFiller,
            (false, true) => ChainBoardMode.OutsideTargetsAllowed,
        };

        var reason = mode switch
        {
            ChainBoardMode.WarTargetsOnly =>
                $"Chain {length}: {hitsToNext} hit(s) to chain {nextMilestone}. Outside targets locked — landing the crossing hit outside forfeits {nextBonus} points.",
            ChainBoardMode.HoldForWarTarget =>
                $"Chain {length}: {hitsToNext} hit(s) to chain {nextMilestone} and no attackable war target. Wait or revive — filler would carry the chain across and forfeit {nextBonus} points.",
            ChainBoardMode.SustainWithFiller =>
                $"Chain {length}: no attackable war target. Outside filler keeps the chain alive but scores roughly half.",
            _ => nextMilestone is int next
                ? $"Chain {length}: {hitsToNext} hit(s) to chain {next} (worth {nextBonus}). Outside targets allowed."
                : $"Chain {length}: past the final milestone. Outside targets allowed.",
        };

        return new ChainTrackerState(
            ChainLength: length,
            CurrentMultiplier: multiplier,
            NextMilestone: nextMilestone,
            HitsToNextMilestone: hitsToNext,
            NextMilestoneBonus: nextBonus,
            IsInReservationWindow: inWindow,
            ForfeitedValueIfCrossedOutside: nextBonus,
            AttackableWarTargetCount: attackableWarTargetCount,
            Mode: mode,
            Reason: reason);
    }

    /// <summary>
    /// The chain multiplier in effect at <paramref name="chainLength"/>:
    /// <c>max(1, ChainEngine.DefaultA · log10(n) + ChainEngine.DefaultB)</c>, which crosses 1.0 at
    /// chain 10. Returns 1.0 for lengths below 1 (the <c>log10</c> domain edge) rather than a NaN.
    /// </summary>
    public static double CurrentMultiplier(int chainLength)
    {
        if (chainLength < 1)
        {
            return 1.0;
        }

        return Math.Max(1.0, ChainEngine.DefaultA * Math.Log10(chainLength) + ChainEngine.DefaultB);
    }

    private static int? NextMilestoneAfter(int chainLength)
    {
        foreach (var milestone in ChainEngine.Milestones)
        {
            if (milestone > chainLength)
            {
                return milestone;
            }
        }

        return null;
    }

    private static int BonusForMilestone(int milestone)
    {
        var index = Array.IndexOf(ChainEngine.Milestones, milestone);
        return index >= 0 ? ChainEngine.MilestoneBonuses[index] : 0;
    }
}
