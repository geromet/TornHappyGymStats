using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class OpponentPressureBaselineTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_returns_all_168_utc_hour_of_week_buckets()
    {
        var baseline = OpponentPressureBaselineBuilder.Build([], AsOf);

        Assert.Equal(168, baseline.Count);
        Assert.Equal(0, baseline[0].UtcHourOfWeek);
        Assert.Equal(DayOfWeek.Sunday, baseline[0].UtcDayOfWeek);
        Assert.Equal(0, baseline[0].UtcHour);
        Assert.Equal(167, baseline[^1].UtcHourOfWeek);
        Assert.Equal(DayOfWeek.Saturday, baseline[^1].UtcDayOfWeek);
        Assert.Equal(23, baseline[^1].UtcHour);
        Assert.All(baseline, bucket =>
        {
            Assert.Null(bucket.ActiveShare);
            Assert.Null(bucket.AttackableShare);
            Assert.Null(bucket.Coverage);
            Assert.Equal(0, bucket.SampleCount);
        });
    }

    [Fact]
    public void Build_uses_weighted_member_counts_instead_of_averaging_sample_percentages()
    {
        var samples = new[]
        {
            Sample(new DateTimeOffset(2026, 8, 30, 12, 5, 0, TimeSpan.Zero), faction: 20, observed: 20, active: 10, attackable: 4),
            Sample(new DateTimeOffset(2026, 8, 23, 12, 20, 0, TimeSpan.Zero), faction: 20, observed: 10, active: 1, attackable: 1),
        };

        var bucket = OpponentPressureBaselineBuilder.ForEvaluation(
            OpponentPressureBaselineBuilder.Build(samples, AsOf),
            new DateTimeOffset(2026, 9, 6, 12, 45, 0, TimeSpan.Zero));

        Assert.Equal(DayOfWeek.Sunday, bucket.UtcDayOfWeek);
        Assert.Equal(12, bucket.UtcHour);
        Assert.Equal(2, bucket.SampleCount);
        Assert.Equal(40, bucket.FactionMemberTotal);
        Assert.Equal(30, bucket.ObservedMemberTotal);
        Assert.Equal(0.75m, bucket.Coverage);
        Assert.Equal(11m / 30m, bucket.ActiveShare);
        Assert.Equal(5m / 30m, bucket.AttackableShare);
    }

    [Fact]
    public void Offset_samples_are_bucketed_by_utc_not_local_clock_hour()
    {
        var localTimestamp = new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2));
        var baseline = OpponentPressureBaselineBuilder.Build(
            [Sample(localTimestamp, faction: 20, observed: 10, active: 3, attackable: 2)],
            AsOf);

        var utcBucket = baseline[OpponentPressureBaselineBuilder.GetUtcHourOfWeek(localTimestamp)];

        Assert.Equal(DayOfWeek.Sunday, utcBucket.UtcDayOfWeek);
        Assert.Equal(1, utcBucket.UtcHour);
        Assert.Equal(1, utcBucket.SampleCount);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), utcBucket.LatestObservationAtUtc);
    }

    [Fact]
    public void Missing_denominator_is_not_interpreted_as_zero_activity()
    {
        var observedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var baseline = OpponentPressureBaselineBuilder.Build(
            [Sample(observedAt, faction: 20, observed: 0, active: 0, attackable: 0)],
            AsOf);
        var bucket = baseline[OpponentPressureBaselineBuilder.GetUtcHourOfWeek(observedAt)];

        Assert.Equal(0, bucket.SampleCount);
        Assert.Equal(0, bucket.ObservedMemberTotal);
        Assert.Null(bucket.Coverage);
        Assert.Null(bucket.ActiveShare);
        Assert.Null(bucket.AttackableShare);
    }

    [Fact]
    public void ForEvaluation_selects_the_matching_utc_hour_of_week_bucket()
    {
        var sampleAt = new DateTimeOffset(2026, 8, 28, 22, 0, 0, TimeSpan.Zero);
        var baseline = OpponentPressureBaselineBuilder.Build(
            [Sample(sampleAt, faction: 20, observed: 16, active: 5, attackable: 4)],
            AsOf);

        var bucket = OpponentPressureBaselineBuilder.ForEvaluation(
            baseline,
            new DateTimeOffset(2026, 9, 4, 22, 59, 0, TimeSpan.Zero));

        Assert.Equal(DayOfWeek.Friday, bucket.UtcDayOfWeek);
        Assert.Equal(22, bucket.UtcHour);
        Assert.Equal(1, bucket.SampleCount);
        Assert.Equal(5m / 16m, bucket.ActiveShare);
    }

    [Fact]
    public void Future_history_sample_fails_closed()
    {
        var sample = Sample(AsOf.AddSeconds(1), faction: 20, observed: 10, active: 2, attackable: 1);

        Assert.Throws<ArgumentException>(() => OpponentPressureBaselineBuilder.Build([sample], AsOf));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(20, 21, 0, 0)]
    [InlineData(20, 10, 11, 0)]
    [InlineData(20, 10, 0, 11)]
    public void Invalid_sample_counts_fail_closed(int faction, int observed, int active, int attackable)
    {
        var sample = Sample(AsOf.AddHours(-1), faction, observed, active, attackable);

        Assert.ThrowsAny<ArgumentException>(() => OpponentPressureBaselineBuilder.Build([sample], AsOf));
    }

    private static OpponentActivitySample Sample(
        DateTimeOffset observedAt,
        int faction,
        int observed,
        int active,
        int attackable)
    {
        return new OpponentActivitySample
        {
            ObservedAtUtc = observedAt,
            FactionMemberCount = faction,
            ObservedMemberCount = observed,
            ActiveMemberCount = active,
            AttackableMemberCount = attackable,
        };
    }
}
