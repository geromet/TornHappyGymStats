using HappyGymStats.Reconstruction;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Tests.Reconstruction;

public sealed class HappyReconstructorTests
{
    [Fact]
    public void RunBackwards_WhenNoEvents_ReturnsEmpty()
    {
        var result = HappyReconstructor.RunBackwards(
            events: Array.Empty<ReconstructionEvent>(),
            currentHappy: 123,
            anchorTimeUtc: Utc("2024-01-01T12:00:00Z"));

        Assert.Empty(result.DerivedGymTrains);
        Assert.Equal(0, result.Stats.GymTrainsDerived);
    }

    [Fact]
    public void RunBackwards_WhenCurrentHappyNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HappyReconstructor.RunBackwards(
                events: Array.Empty<ReconstructionEvent>(),
                currentHappy: -1,
                anchorTimeUtc: Utc("2024-01-01T12:00:00Z")));
    }

    [Fact]
    public void RunBackwards_InvertsQuarterHourRegenTicks()
    {
        // Anchor at 12:30 with happy=100, then reconstruct one gym event at 12:00.
        // Forward regen ticks between 12:00 and 12:30 are at 12:15 and 12:30 => 2 ticks => +10 happy.
        var anchor = Utc("2024-01-01T12:30:00Z");
        var gymTime = Utc("2024-01-01T12:00:00Z");

        var events = new ReconstructionEvent[]
        {
            new GymTrainEvent(LogId: "1", OccurredAtUtc: gymTime, HappyUsed: 0),
        };

        var result = HappyReconstructor.RunBackwards(
            events: events,
            currentHappy: 100,
            anchorTimeUtc: anchor);

        var derived = Assert.Single(result.DerivedGymTrains);
        Assert.Equal(2, derived.RegenTicksApplied);
        Assert.Equal(10, derived.RegenHappyGained);
        Assert.Equal(90, derived.HappyAfterTrain);
        Assert.Equal(90, derived.HappyBeforeTrain);
    }

    [Fact]
    public void RunBackwards_DerivesHappyBeforeTrain_FromHappyUsed()
    {
        var t = Utc("2024-01-01T12:10:00Z");

        var events = new ReconstructionEvent[]
        {
            // Same timestamp as anchor => regen ticks == 0.
            new GymTrainEvent(LogId: "1", OccurredAtUtc: t, HappyUsed: 25),
        };

        var result = HappyReconstructor.RunBackwards(
            events: events,
            currentHappy: 75,
            anchorTimeUtc: t);

        var derived = Assert.Single(result.DerivedGymTrains);
        Assert.Equal(0, derived.RegenTicksApplied);
        Assert.Equal(0, derived.RegenHappyGained);

        Assert.Equal(100, derived.HappyBeforeTrain);
        Assert.Equal(25, derived.HappyUsed);
        Assert.Equal(75, derived.HappyAfterTrain);
    }

    [Fact]
    public void RunBackwards_DoesNotUnclamp_WhenMaxHappyIncreasesGoingBackwards()
    {
        // Forward timeline:
        // 10:00 max=100
        // 11:00 max=80 (decrease)
        // Backwards reconstruction from 11:30 with currentHappy=80 should NOT raise the effective max back to 100.
        var tMax100 = Utc("2024-01-01T10:00:00Z");
        var tMax80 = Utc("2024-01-01T11:00:00Z");
        var anchor = Utc("2024-01-01T11:30:00Z");

        var gym1031 = Utc("2024-01-01T10:31:00Z");
        var gym1030 = Utc("2024-01-01T10:30:00Z");

        var events = new ReconstructionEvent[]
        {
            new MaxHappyEvent(LogId: "10", OccurredAtUtc: tMax100, MaxHappy: 100),
            new MaxHappyEvent(LogId: "11", OccurredAtUtc: tMax80, MaxHappy: 80),

            // Two trains between quarter-hour ticks: no regen should be applied between 10:31 and 10:30.
            new GymTrainEvent(LogId: "21", OccurredAtUtc: gym1031, HappyUsed: 0),
            new GymTrainEvent(LogId: "22", OccurredAtUtc: gym1030, HappyUsed: 30),
        };

        var result = HappyReconstructor.RunBackwards(
            events: events,
            currentHappy: 80,
            anchorTimeUtc: anchor);

        Assert.Equal(2, result.DerivedGymTrains.Count);

        var first = result.DerivedGymTrains[0]; // 10:30
        var second = result.DerivedGymTrains[1]; // 10:31

        Assert.Equal(gym1030, first.OccurredAtUtc);
        Assert.Equal(0, first.RegenTicksApplied); // from 10:30 -> 10:31
        Assert.Equal(80, first.MaxHappyAtTimeUtc); // effective max (do-not-unclamp)
        Assert.True(first.ClampedToMax);
        Assert.Equal(80, first.HappyBeforeTrain);

        Assert.Equal(gym1031, second.OccurredAtUtc);
        Assert.Equal(2, second.RegenTicksApplied); // 10:31 -> 11:00 (max-happy event boundary) includes 10:45, 11:00
        Assert.Equal(80, second.MaxHappyAtTimeUtc);
        Assert.False(second.ClampedToMax);

        Assert.True(result.Stats.ClampAppliedCount >= 1);
        Assert.True(result.Stats.WarningCount >= 1);
    }

    private static DateTimeOffset Utc(string iso)
        => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
}
