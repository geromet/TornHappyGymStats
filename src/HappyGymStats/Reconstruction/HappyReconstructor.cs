using System.Diagnostics;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Reconstruction;

/// <summary>
/// Deterministic backwards reconstruction of happy values for gym train logs.
/// </summary>
/// <remarks>
/// The algorithm traverses events backwards from a caller-provided anchor (time + current happy).
/// It inverts quarter-hour regen ticks (5 happy per tick) and inverts gym trains
/// (<c>happy_before = happy_after + happy_used</c>).
/// 
/// Max-happy is applied as an upper-bound constraint.
///
/// Current policy: as we move backwards we always use the max-happy value known at that time
/// (i.e., we DO allow the ceiling to increase again when earlier max-happy was higher).
/// This is not perfectly invertible across max-happy decreases, but it produces more intuitive
/// reconstructions when users know they previously trained at higher max-happy values.
/// </remarks>
public static class HappyReconstructor
{
    // Torn happiness regenerates passively at a fixed cadence: +5 happy every 15 minutes.
    // Ticks are counted by QuarterHourTicks using UTC quarter-hour boundaries.
    public const int HappyRegenPerTick = 5;

    public sealed record BackwardsReconstructionStats(
        int GymTrainsDerived,
        long RegenTicksApplied,
        int ClampAppliedCount,
        int WarningCount);

    public sealed record BackwardsReconstructionResult(
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        BackwardsReconstructionStats Stats);

    public sealed record BackwardsReconstructionDetailedResult(
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        IReadOnlyList<DerivedHappyEvent> DerivedHappyEvents,
        BackwardsReconstructionStats Stats);

    public static BackwardsReconstructionResult RunBackwards(
        IEnumerable<ReconstructionEvent> events,
        int currentHappy,
        DateTimeOffset anchorTimeUtc)
    {
        var detailed = RunBackwardsDetailed(events, currentHappy, anchorTimeUtc);
        return new BackwardsReconstructionResult(
            DerivedGymTrains: detailed.DerivedGymTrains,
            Stats: detailed.Stats);
    }

    public static BackwardsReconstructionDetailedResult RunBackwardsDetailed(
        IEnumerable<ReconstructionEvent> events,
        int currentHappy,
        DateTimeOffset anchorTimeUtc)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));
        if (currentHappy < 0) throw new ArgumentOutOfRangeException(nameof(currentHappy), currentHappy, "currentHappy must be >= 0.");
        EnsureUtc(anchorTimeUtc, nameof(anchorTimeUtc));

        var eventArray = events
            .Where(e => e.OccurredAtUtc <= anchorTimeUtc)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.LogId)
            .ToArray();

        var maxTimeline = MaxHappyTimeline.FromEvents(eventArray.OfType<MaxHappyEvent>());

        var derivedTrains = new List<DerivedGymTrain>();
        var derivedEvents = new List<DerivedHappyEvent>();

        var cursorTime = anchorTimeUtc;
        var cursorHappy = currentHappy;

        // Effective max ceiling used for clamping. As we move backwards, this follows the max-happy
        // known at that earlier time (i.e., it may increase or decrease).
        int? effectiveMaxCeiling = maxTimeline.MaxHappyAtUtc(anchorTimeUtc);

        var clampAppliedCount = 0;
        var warningCount = 0;
        var regenTicksTotal = 0L;

        // Clamp the anchor happy if needed.
        cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);

        foreach (var ev in eventArray)
        {
            EnsureUtc(ev.OccurredAtUtc, nameof(events));

            // Expand regen ticks between this event time and the cursor time (later).
            // We represent each tick as a synthetic event at the tick instant.
            var tickInstants = QuarterHourTicks.EnumerateTickInstantsBetweenUtc(ev.OccurredAtUtc, cursorTime)
                .OrderByDescending(t => t)
                .ToArray();

            regenTicksTotal += tickInstants.Length;

            foreach (var tick in tickInstants)
            {
                // cursorHappy is the happy immediately AFTER the tick (later side).
                var afterTick = cursorHappy;
                var beforeTickLong = (long)afterTick - HappyRegenPerTick;

                var beforeTick = beforeTickLong < 0 ? 0 : (int)beforeTickLong;
                if (beforeTickLong < 0)
                    warningCount++;

                derivedEvents.Add(new DerivedHappyEvent(
                    EventId: $"regen@{tick.ToUnixTimeSeconds()}",
                    SourceLogId: null,
                    OccurredAtUtc: tick,
                    EventType: "regen_tick",
                    HappyBeforeEvent: beforeTick,
                    HappyAfterEvent: afterTick,
                    Delta: HappyRegenPerTick,
                    HappyUsed: null,
                    MaxHappyAtTimeUtc: effectiveMaxCeiling,
                    ClampedToMax: false));

                cursorHappy = beforeTick;
                cursorTime = tick;

                // Clamp to effective max after moving back across the tick.
                cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);
            }

            // Now cursorTime is still >= ev.OccurredAtUtc; move to the event time.
            cursorTime = ev.OccurredAtUtc;

            // Update effective ceiling based on max-happy at this time.
            var actualMaxAtTime = maxTimeline.MaxHappyAtUtc(cursorTime);
            if (actualMaxAtTime is not null)
            {
                effectiveMaxCeiling = actualMaxAtTime;
            }

            // Clamp to effective max at the event time.
            cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);

            if (ev is MaxHappyEvent max)
            {
                // Max-happy change itself doesn't directly add/subtract happy, but it can force a clamp.
                derivedEvents.Add(new DerivedHappyEvent(
                    EventId: max.LogId,
                    SourceLogId: max.LogId,
                    OccurredAtUtc: max.OccurredAtUtc,
                    EventType: "max_happy",
                    HappyBeforeEvent: cursorHappy,
                    HappyAfterEvent: cursorHappy,
                    Delta: null,
                    HappyUsed: null,
                    MaxHappyAtTimeUtc: effectiveMaxCeiling,
                    ClampedToMax: false));

                continue;
            }

            if (ev is HappyDeltaEvent delta)
            {
                var after = cursorHappy;

                // To go backwards, invert the delta: before = after - delta.
                var beforeLong = (long)after - delta.Delta;

                int before;
                if (beforeLong < 0)
                {
                    before = 0;
                    warningCount++;
                }
                else if (beforeLong > int.MaxValue)
                {
                    before = int.MaxValue;
                    warningCount++;
                }
                else
                {
                    before = (int)beforeLong;
                }

                var beforeClamped = ClampToEffectiveMax(before, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);

                derivedEvents.Add(new DerivedHappyEvent(
                    EventId: delta.LogId,
                    SourceLogId: delta.LogId,
                    OccurredAtUtc: delta.OccurredAtUtc,
                    EventType: "happy_delta",
                    HappyBeforeEvent: beforeClamped,
                    HappyAfterEvent: after,
                    Delta: delta.Delta,
                    HappyUsed: null,
                    MaxHappyAtTimeUtc: effectiveMaxCeiling,
                    ClampedToMax: beforeClamped != before));

                cursorHappy = beforeClamped;
                continue;
            }

            if (ev is not GymTrainEvent gym)
                continue;

            // cursorHappy is our best estimate of happy immediately AFTER the train.
            var happyAfterTrain = cursorHappy;

            var beforeCandidate = happyAfterTrain + gym.HappyUsed;

            var happyBeforeTrain = beforeCandidate;
            var clampedToMax = false;

            if (effectiveMaxCeiling is not null && happyBeforeTrain > effectiveMaxCeiling.Value)
            {
                happyBeforeTrain = effectiveMaxCeiling.Value;
                clampedToMax = true;
                clampAppliedCount++;
                warningCount++; // clamping implies likely info loss.
            }

            if (happyBeforeTrain < 0)
            {
                happyBeforeTrain = 0;
                warningCount++;
            }

            // Keep the derived fields internally consistent: after = before - used.
            var consistentAfter = happyBeforeTrain - gym.HappyUsed;
            if (consistentAfter < 0)
            {
                // This can happen if we clamped before below happyUsed.
                consistentAfter = 0;
                warningCount++;
            }

            derivedTrains.Add(new DerivedGymTrain(
                LogId: gym.LogId,
                OccurredAtUtc: gym.OccurredAtUtc,
                HappyBeforeTrain: happyBeforeTrain,
                HappyUsed: gym.HappyUsed,
                HappyAfterTrain: consistentAfter,
                RegenTicksApplied: tickInstants.Length,
                RegenHappyGained: SafeMultiplyTicks(tickInstants.Length, HappyRegenPerTick),
                MaxHappyAtTimeUtc: effectiveMaxCeiling,
                ClampedToMax: clampedToMax));

            derivedEvents.Add(new DerivedHappyEvent(
                EventId: gym.LogId,
                SourceLogId: gym.LogId,
                OccurredAtUtc: gym.OccurredAtUtc,
                EventType: "gym_train",
                HappyBeforeEvent: happyBeforeTrain,
                HappyAfterEvent: consistentAfter,
                Delta: -gym.HappyUsed,
                HappyUsed: gym.HappyUsed,
                MaxHappyAtTimeUtc: effectiveMaxCeiling,
                ClampedToMax: clampedToMax));

            cursorHappy = happyBeforeTrain;
            cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);
        }

        // Return in chronological order with deterministic tiebreaking.
        static int TypeOrder(string t) => t switch
        {
            "regen_tick" => 0,
            "max_happy" => 1,
            "happy_delta" => 2,
            "gym_train" => 3,
            _ => 9,
        };

        derivedTrains.Sort(static (a, b) =>
        {
            var cmp = a.OccurredAtUtc.CompareTo(b.OccurredAtUtc);
            return cmp != 0 ? cmp : a.LogId.CompareTo(b.LogId);
        });

        derivedEvents.Sort((a, b) =>
        {
            var cmp = a.OccurredAtUtc.CompareTo(b.OccurredAtUtc);
            if (cmp != 0) return cmp;

            cmp = TypeOrder(a.EventType).CompareTo(TypeOrder(b.EventType));
            if (cmp != 0) return cmp;

            return string.CompareOrdinal(a.EventId, b.EventId);
        });

        return new BackwardsReconstructionDetailedResult(
            DerivedGymTrains: derivedTrains,
            DerivedHappyEvents: derivedEvents,
            Stats: new BackwardsReconstructionStats(
                GymTrainsDerived: derivedTrains.Count,
                RegenTicksApplied: regenTicksTotal,
                ClampAppliedCount: clampAppliedCount,
                WarningCount: warningCount));
    }

    private static int ClampToEffectiveMax(int happy, int? effectiveMax, ref int clampAppliedCount, ref int warningCount)
    {
        if (effectiveMax is null)
            return happy;

        if (happy <= effectiveMax.Value)
            return happy;

        clampAppliedCount++;
        warningCount++;
        return effectiveMax.Value;
    }

    private static int SafeMultiplyTicks(long ticks, int perTick)
    {
        Debug.Assert(ticks >= 0);
        if (ticks == 0)
            return 0;

        // Happy values fit within int; if the multiplication overflows, saturate.
        try
        {
            checked
            {
                return (int)(ticks * perTick);
            }
        }
        catch (OverflowException)
        {
            return int.MaxValue;
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Value must be in UTC (offset +00:00).", paramName);
    }
}
