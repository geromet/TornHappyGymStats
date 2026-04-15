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
    // Torn happiness does not passively regenerate on a quarter-hour cadence the way energy does.
    // We still count quarter-hour ticks between events for observability, but do not apply a regen delta.
    public const int HappyRegenPerTick = 0;

    public sealed record BackwardsReconstructionStats(
        int GymTrainsDerived,
        long RegenTicksApplied,
        int ClampAppliedCount,
        int WarningCount);

    public sealed record BackwardsReconstructionResult(
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        BackwardsReconstructionStats Stats);

    public static BackwardsReconstructionResult RunBackwards(
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

        var derived = new List<DerivedGymTrain>();

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

            // Apply inverse regen between this event time and the cursor time (later).
            var ticks = QuarterHourTicks.CountTicksBetweenUtc(ev.OccurredAtUtc, cursorTime);
            regenTicksTotal += ticks;

            var regenGain = SafeMultiplyTicks(ticks, HappyRegenPerTick);
            cursorHappy -= regenGain;
            if (cursorHappy < 0)
            {
                // Reconstruction should not go negative; clamp and warn.
                cursorHappy = 0;
                warningCount++;
            }

            cursorTime = ev.OccurredAtUtc;

            // Update effective ceiling based on max-happy at this time.
            // We use the max-happy value known at (or before) cursorTime, allowing the ceiling to
            // increase again when earlier max-happy was higher.
            var actualMaxAtTime = maxTimeline.MaxHappyAtUtc(cursorTime);
            if (actualMaxAtTime is not null)
            {
                effectiveMaxCeiling = actualMaxAtTime;
            }

            // Clamp to effective max even if the current event isn't a gym train.
            cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);

            if (ev is HappyDeltaEvent delta)
            {
                // cursorHappy is our best estimate of happy immediately AFTER this delta event.
                // To go backwards, invert the delta: before = after - delta.
                var beforeLong = (long)cursorHappy - delta.Delta;

                if (beforeLong < 0)
                {
                    cursorHappy = 0;
                    warningCount++;
                }
                else if (beforeLong > int.MaxValue)
                {
                    cursorHappy = int.MaxValue;
                    warningCount++;
                }
                else
                {
                    cursorHappy = (int)beforeLong;
                }

                cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);
                continue;
            }

            if (ev is not GymTrainEvent gym)
                continue;

            // At this point cursorHappy is our best estimate of happy immediately AFTER the train.
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

            derived.Add(new DerivedGymTrain(
                LogId: gym.LogId,
                OccurredAtUtc: gym.OccurredAtUtc,
                HappyBeforeTrain: happyBeforeTrain,
                HappyUsed: gym.HappyUsed,
                HappyAfterTrain: consistentAfter,
                RegenTicksApplied: ticks,
                RegenHappyGained: regenGain,
                MaxHappyAtTimeUtc: effectiveMaxCeiling,
                ClampedToMax: clampedToMax));

            // Move the cursor to the state before the train.
            cursorHappy = happyBeforeTrain;
            cursorHappy = ClampToEffectiveMax(cursorHappy, effectiveMaxCeiling, ref clampAppliedCount, ref warningCount);
        }

        // Return in chronological order for consumers/UI.
        derived.Sort(static (a, b) =>
        {
            var cmp = a.OccurredAtUtc.CompareTo(b.OccurredAtUtc);
            return cmp != 0 ? cmp : a.LogId.CompareTo(b.LogId);
        });

        return new BackwardsReconstructionResult(
            DerivedGymTrains: derived,
            Stats: new BackwardsReconstructionStats(
                GymTrainsDerived: derived.Count,
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
