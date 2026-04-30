using System.Diagnostics;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Reconstruction;

/// <summary>
/// Forward (oldest-to-newest) reconstruction of per-event happy values.
/// </summary>
/// <remarks>
/// The forward model is intentionally anchor-driven because max-happy changes are not invertible when reconstructing backwards.
/// When an anchor is encountered (currently: recognized overdose events), we snap the happy value to the inferred value.
/// </remarks>
public static class HappyTimelineReconstructor
{
    // Torn happy passively regenerates +5 every 15 minutes.
    public const int HappyRegenPerTick = 5;

    public sealed record ForwardStats(
        int EventsProcessed,
        int RegenTicksEmitted,
        int GymTrainsDerived,
        int ClampAppliedCount,
        int AnchorAppliedCount,
        int WarningCount);

    public sealed record ForwardResult(
        IReadOnlyList<DerivedHappyEvent> DerivedHappyEvents,
        IReadOnlyList<DerivedGymTrain> DerivedGymTrains,
        ForwardStats Stats);

    public static ForwardResult RunForward(
        IEnumerable<ReconstructionEvent> events)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));

        var ordered = events
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.LogId)
            .ToArray();

        var maxTimeline = MaxHappyTimeline.FromEvents(ordered.OfType<MaxHappyEvent>());

        // Decide whether we can safely assume early-game initial state (Shack max=100, happy=100):
        // Only when there are gym trains before any max-happy event.
        var firstTrain = ordered.OfType<GymTrainEvent>().FirstOrDefault();
        var firstMax = ordered.OfType<MaxHappyEvent>().FirstOrDefault();

        int? currentHappy;
        if (firstTrain is not null && (firstMax is null || firstTrain.OccurredAtUtc < firstMax.OccurredAtUtc))
        {
            currentHappy = 100;
        }
        else
        {
            currentHappy = null;
        }

        DateTimeOffset? cursorTime = ordered.Length > 0 ? ordered[0].OccurredAtUtc : null;

        var derivedEvents = new List<DerivedHappyEvent>(capacity: Math.Max(64, ordered.Length * 2));
        var derivedTrains = new List<DerivedGymTrain>();

        var clampApplied = 0;
        var warnings = 0;
        var anchorApplied = 0;
        var regenEmitted = 0;

        // When the immediately-previous real event was a gym train, we attribute any regen ticks
        // between that train and the next real event to that train row.
        int? pendingTrainIndex = null;

        foreach (var ev in ordered)
        {
            EnsureUtc(ev.OccurredAtUtc, nameof(events));

            // Emit regen tick events between cursorTime and this event.
            // These ticks represent regeneration AFTER the previous real event and BEFORE this real event.
            if (cursorTime is not null)
            {
                long regenTicksBetween = 0;
                var regenHappyGainedBetween = 0;

                foreach (var tick in QuarterHourTicks.EnumerateTickInstantsBetweenUtc(cursorTime.Value, ev.OccurredAtUtc))
                {
                    // Determine max ceiling at this tick (if known).
                    var maxAtTick = maxTimeline.MaxHappyAtUtc(tick);

                    var before = currentHappy;
                    var clamped = false;
                    int? after = before is null
                        ? null
                        : ApplyDeltaClamp(before.Value, +HappyRegenPerTick, maxAtTick, ref clampApplied, ref warnings, out clamped);

                    // For regen ticks, the *actual* delta can be < +5 if we were clamped at max.
                    int? actualDelta = (before is not null && after is not null) ? after.Value - before.Value : null;

                    derivedEvents.Add(new DerivedHappyEvent(
                        EventId: $"regen@{tick.ToUnixTimeSeconds()}",
                        SourceLogId: null,
                        OccurredAtUtc: tick,
                        EventType: "regen_tick",
                        HappyBeforeEvent: before,
                        HappyAfterEvent: after,
                        Delta: actualDelta,
                        HappyUsed: null,
                        MaxHappyAtTimeUtc: maxAtTick,
                        ClampedToMax: clamped));

                    currentHappy = after;
                    regenEmitted++;

                    if (pendingTrainIndex is not null)
                    {
                        regenTicksBetween++;
                        if (actualDelta is not null)
                            regenHappyGainedBetween += actualDelta.Value;
                    }
                }

                // Finalize the previous train row (if any): it owns the regen between it and this event.
                if (pendingTrainIndex is not null)
                {
                    var idx = pendingTrainIndex.Value;
                    var prior = derivedTrains[idx];

                    derivedTrains[idx] = prior with
                    {
                        RegenTicksApplied = regenTicksBetween,
                        RegenHappyGained = regenHappyGainedBetween,
                    };

                    pendingTrainIndex = null;
                }
            }

            cursorTime = ev.OccurredAtUtc;

            // Process the actual event.
            var maxAtEvent = maxTimeline.MaxHappyAtUtc(ev.OccurredAtUtc);

            switch (ev)
            {
                case MaxHappyEvent max:
                {
                    // Max-happy changes only affect the passive regen ceiling.
                    // They do not immediately clamp current happy; Torn applies that on quarter-hour ticks.
                    var before = currentHappy;
                    var after = before;

                    derivedEvents.Add(new DerivedHappyEvent(
                        EventId: max.LogId,
                        SourceLogId: max.LogId,
                        OccurredAtUtc: max.OccurredAtUtc,
                        EventType: "max_happy",
                        HappyBeforeEvent: before,
                        HappyAfterEvent: after,
                        Delta: before is not null && after is not null ? after.Value - before.Value : null,
                        HappyUsed: null,
                        MaxHappyAtTimeUtc: maxAtEvent,
                        ClampedToMax: false));

                    currentHappy = after;
                    break;
                }

                case OverdoseEvent od:
                {
                    // Overdose anchors: infer exact before/after based on drug percent loss.
                    // This overrides currentHappy.
                    int? inferredBefore;
                    int? inferredAfter;

                    if (od.PercentLoss >= 1.0)
                    {
                        inferredBefore = od.HappyDecreased;
                        inferredAfter = 0;
                    }
                    else if (Math.Abs(od.PercentLoss - 0.5) < 0.0001)
                    {
                        inferredAfter = od.HappyDecreased;
                        inferredBefore = od.HappyDecreased * 2;
                    }
                    else
                    {
                        // Unknown factor: fall back to delta behavior.
                        inferredBefore = currentHappy;
                        inferredAfter = inferredBefore is null
                            ? null
                            : (int?)ApplyDeltaFloorOnly(inferredBefore.Value, -od.HappyDecreased, ref warnings);
                        warnings++;
                    }

                    derivedEvents.Add(new DerivedHappyEvent(
                        EventId: od.LogId,
                        SourceLogId: od.LogId,
                        OccurredAtUtc: od.OccurredAtUtc,
                        EventType: "overdose",
                        HappyBeforeEvent: inferredBefore,
                        HappyAfterEvent: inferredAfter,
                        Delta: inferredBefore is not null && inferredAfter is not null ? inferredAfter.Value - inferredBefore.Value : null,
                        HappyUsed: null,
                        MaxHappyAtTimeUtc: maxAtEvent,
                        ClampedToMax: false));

                    currentHappy = inferredAfter;
                    anchorApplied++;
                    break;
                }

                case HappyDeltaEvent delta:
                {
                    var before = currentHappy;
                    int? after = before is null
                        ? null
                        : ApplyDeltaFloorOnly(before.Value, delta.Delta, ref warnings);

                    derivedEvents.Add(new DerivedHappyEvent(
                        EventId: delta.LogId,
                        SourceLogId: delta.LogId,
                        OccurredAtUtc: delta.OccurredAtUtc,
                        EventType: "happy_delta",
                        HappyBeforeEvent: before,
                        HappyAfterEvent: after,
                        Delta: before is not null && after is not null ? after.Value - before.Value : null,
                        HappyUsed: null,
                        MaxHappyAtTimeUtc: maxAtEvent,
                        ClampedToMax: false));

                    currentHappy = after;
                    break;
                }

                case GymTrainEvent gym:
                {
                    var before = currentHappy;
                    int? after = null;
                    if (before is not null)
                    {
                        after = before.Value - gym.HappyUsed;
                        if (after < 0)
                        {
                            after = 0;
                            warnings++;
                        }
                    }

                    derivedEvents.Add(new DerivedHappyEvent(
                        EventId: gym.LogId,
                        SourceLogId: gym.LogId,
                        OccurredAtUtc: gym.OccurredAtUtc,
                        EventType: "gym_train",
                        HappyBeforeEvent: before,
                        HappyAfterEvent: after,
                        Delta: before is not null && after is not null ? after.Value - before.Value : null,
                        HappyUsed: gym.HappyUsed,
                        MaxHappyAtTimeUtc: maxAtEvent,
                        ClampedToMax: false));

                    if (before is not null && after is not null)
                    {
                        derivedTrains.Add(new DerivedGymTrain(
                            LogId: gym.LogId,
                            OccurredAtUtc: gym.OccurredAtUtc,
                            HappyBeforeTrain: before.Value,
                            HappyUsed: gym.HappyUsed,
                            HappyAfterTrain: after.Value,
                            RegenTicksApplied: 0,
                            RegenHappyGained: 0,
                            MaxHappyAtTimeUtc: maxAtEvent,
                            ClampedToMax: false));

                        // Regen between this train and the next real event will be attributed to this row.
                        pendingTrainIndex = derivedTrains.Count - 1;
                    }

                    currentHappy = after;
                    break;
                }
            }
        }

        return new ForwardResult(
            DerivedHappyEvents: derivedEvents,
            DerivedGymTrains: derivedTrains,
            Stats: new ForwardStats(
                EventsProcessed: ordered.Length,
                RegenTicksEmitted: regenEmitted,
                GymTrainsDerived: derivedTrains.Count,
                ClampAppliedCount: clampApplied,
                AnchorAppliedCount: anchorApplied,
                WarningCount: warnings));
    }

    private static int ApplyDeltaFloorOnly(
        int before,
        int delta,
        ref int warnings)
    {
        long afterLong = (long)before + delta;
        if (afterLong < 0)
        {
            warnings++;
            return 0;
        }

        if (afterLong > int.MaxValue)
        {
            warnings++;
            return int.MaxValue;
        }

        return (int)afterLong;
    }

    private static int ApplyDeltaClamp(
        int before,
        int delta,
        int? max,
        ref int clampApplied,
        ref int warnings,
        out bool clampedToMax)
    {
        clampedToMax = false;

        long afterLong = (long)before + delta;
        if (afterLong < 0)
        {
            warnings++;
            return 0;
        }

        if (afterLong > int.MaxValue)
        {
            warnings++;
            afterLong = int.MaxValue;
        }

        var after = (int)afterLong;

        if (max is not null && after > max.Value)
        {
            clampedToMax = true;
            clampApplied++;
            return max.Value;
        }

        return after;
    }

    private static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Value must be in UTC (offset +00:00).", paramName);
    }
}
