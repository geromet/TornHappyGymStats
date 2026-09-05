namespace HappyGymStats.Core.War;

/// <summary>
/// One provider-neutral status sample assembled from an already-existing faction/member observation.
/// Missing observations are represented by the absence of a sample, not by zero-valued activity.
/// </summary>
public sealed record OpponentActivitySample
{
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required int FactionMemberCount { get; init; }
    public required int ObservedMemberCount { get; init; }
    public required int ActiveMemberCount { get; init; }
    public required int AttackableMemberCount { get; init; }
}

/// <summary>
/// Aggregated historical activity for one UTC hour-of-week bucket. Sunday 00:00 UTC is bucket 0;
/// Saturday 23:00 UTC is bucket 167. Null shares mean no observed-member denominator existed for
/// that bucket and must not be interpreted as zero activity.
/// </summary>
public sealed record OpponentHourlyBaselineBucket
{
    public required int UtcHourOfWeek { get; init; }
    public required DayOfWeek UtcDayOfWeek { get; init; }
    public required int UtcHour { get; init; }
    public required int SampleCount { get; init; }
    public required int FactionMemberTotal { get; init; }
    public required int ObservedMemberTotal { get; init; }
    public required decimal? Coverage { get; init; }
    public required decimal? ActiveShare { get; init; }
    public required decimal? AttackableShare { get; init; }
    public DateTimeOffset? LatestObservationAtUtc { get; init; }
}

/// <summary>
/// Pure aggregation over preassembled sampled observations. This type deliberately owns no
/// polling, persistence, retention cleanup, provider client, timer, or background-service seam.
/// Callers remain responsible for supplying the bounded historical window they intend to model.
/// </summary>
public static class OpponentPressureBaselineBuilder
{
    public const int UtcHourBucketCount = 7 * 24;

    public static IReadOnlyList<OpponentHourlyBaselineBucket> Build(
        IReadOnlyCollection<OpponentActivitySample> samples,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var asOf = asOfUtc.ToUniversalTime();
        var accumulators = new Accumulator[UtcHourBucketCount];

        foreach (var sample in samples)
        {
            ArgumentNullException.ThrowIfNull(sample);
            Validate(sample, asOf);

            // No denominator means no usable observation. Do not convert missing coverage into
            // a zero-activity sample or let it dilute the historical baseline.
            if (sample.ObservedMemberCount == 0)
            {
                continue;
            }

            var observedAt = sample.ObservedAtUtc.ToUniversalTime();
            var bucketIndex = GetUtcHourOfWeek(observedAt);
            ref var accumulator = ref accumulators[bucketIndex];
            accumulator.SampleCount++;
            accumulator.FactionMemberTotal += sample.FactionMemberCount;
            accumulator.ObservedMemberTotal += sample.ObservedMemberCount;
            accumulator.ActiveMemberTotal += sample.ActiveMemberCount;
            accumulator.AttackableMemberTotal += sample.AttackableMemberCount;
            if (accumulator.LatestObservationAtUtc is null || observedAt > accumulator.LatestObservationAtUtc)
            {
                accumulator.LatestObservationAtUtc = observedAt;
            }
        }

        var result = new OpponentHourlyBaselineBucket[UtcHourBucketCount];
        for (var bucketIndex = 0; bucketIndex < result.Length; bucketIndex++)
        {
            var accumulator = accumulators[bucketIndex];
            var observedTotal = accumulator.ObservedMemberTotal;
            var factionTotal = accumulator.FactionMemberTotal;
            result[bucketIndex] = new OpponentHourlyBaselineBucket
            {
                UtcHourOfWeek = bucketIndex,
                UtcDayOfWeek = (DayOfWeek)(bucketIndex / 24),
                UtcHour = bucketIndex % 24,
                SampleCount = accumulator.SampleCount,
                FactionMemberTotal = factionTotal,
                ObservedMemberTotal = observedTotal,
                Coverage = factionTotal == 0 ? null : (decimal)observedTotal / factionTotal,
                ActiveShare = observedTotal == 0 ? null : (decimal)accumulator.ActiveMemberTotal / observedTotal,
                AttackableShare = observedTotal == 0 ? null : (decimal)accumulator.AttackableMemberTotal / observedTotal,
                LatestObservationAtUtc = accumulator.LatestObservationAtUtc,
            };
        }

        return result;
    }

    public static OpponentHourlyBaselineBucket ForEvaluation(
        IReadOnlyList<OpponentHourlyBaselineBucket> baseline,
        DateTimeOffset evaluationAtUtc)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.Count != UtcHourBucketCount)
        {
            throw new ArgumentException($"Expected exactly {UtcHourBucketCount} UTC hour-of-week buckets.", nameof(baseline));
        }

        return baseline[GetUtcHourOfWeek(evaluationAtUtc.ToUniversalTime())];
    }

    public static int GetUtcHourOfWeek(DateTimeOffset observedAtUtc)
    {
        var utc = observedAtUtc.ToUniversalTime();
        return (int)utc.DayOfWeek * 24 + utc.Hour;
    }

    private static void Validate(OpponentActivitySample sample, DateTimeOffset asOf)
    {
        if (sample.FactionMemberCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sample), "Faction member count must be positive.");
        }

        if (sample.ObservedMemberCount < 0 || sample.ObservedMemberCount > sample.FactionMemberCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sample), "Observed member count must be within the faction member count.");
        }

        if (sample.ActiveMemberCount < 0 || sample.ActiveMemberCount > sample.ObservedMemberCount ||
            sample.AttackableMemberCount < 0 || sample.AttackableMemberCount > sample.ObservedMemberCount)
        {
            throw new ArgumentOutOfRangeException(nameof(sample), "Active and attackable counts must be within the observed member count.");
        }

        if (sample.ObservedAtUtc.ToUniversalTime() > asOf)
        {
            throw new ArgumentException("Historical baseline samples cannot occur after the evaluation time.", nameof(sample));
        }
    }

    private struct Accumulator
    {
        public int SampleCount;
        public int FactionMemberTotal;
        public int ObservedMemberTotal;
        public int ActiveMemberTotal;
        public int AttackableMemberTotal;
        public DateTimeOffset? LatestObservationAtUtc;
    }
}
