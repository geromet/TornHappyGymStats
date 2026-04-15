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
    public void RunBackwards_CountsQuarterHourTicks_AndAppliesHappyRegen()
    {
        // Anchor at 12:30 with happy=100, then reconstruct one gym event at 12:00.
        // Quarter-hour ticks between 12:00 and 12:30 are 12:15 and 12:30 => 2 ticks.
        // Happy regenerates +5 per tick, so going backwards we subtract 10.
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
    public void RunBackwards_AllowsUnclamp_WhenMaxHappyIsHigherEarlier()
    {
        // Forward timeline:
        // 10:00 max=100
        // 11:00 max=80 (decrease)
        // Backwards reconstruction from 11:30 with currentHappy=80 SHOULD use the higher max (100)
        // when reconstructing earlier times.
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
            // Use a large happy_used so the reconstructed before-train value exceeds max and clamps.
            new GymTrainEvent(LogId: "22", OccurredAtUtc: gym1030, HappyUsed: 50),
        };

        var result = HappyReconstructor.RunBackwards(
            events: events,
            currentHappy: 80,
            anchorTimeUtc: anchor);

        Assert.Equal(2, result.DerivedGymTrains.Count);

        var first = result.DerivedGymTrains[0]; // 10:30
        var second = result.DerivedGymTrains[1]; // 10:31

        Assert.Equal(gym1030, first.OccurredAtUtc);
        Assert.Equal(0, first.RegenTicksApplied);
        Assert.Equal(100, first.MaxHappyAtTimeUtc);
        Assert.True(first.ClampedToMax);
        Assert.Equal(100, first.HappyBeforeTrain); // derived after at 10:30 + 50 used exceeds 100, so it clamps

        Assert.Equal(gym1031, second.OccurredAtUtc);
        Assert.Equal(2, second.RegenTicksApplied); // 10:31 -> 11:00 includes 10:45, 11:00
        Assert.Equal(100, second.MaxHappyAtTimeUtc);
        Assert.False(second.ClampedToMax);

        Assert.Equal(1, result.Stats.ClampAppliedCount);
    }

    [Fact]
    public void RunBackwards_AppliesHappyDeltaEvents_WhenReconstructing()
    {
        // Timeline (forward):
        // 12:00 gym train uses 10 happy
        // 12:05 item use increases happy by +25
        // 12:10 anchor, current happy = 50
        //
        // Therefore at 12:00 after-train happy must be 25, and before-train must be 35.
        var anchor = Utc("2024-01-01T12:10:00Z");
        var deltaTime = Utc("2024-01-01T12:05:00Z");
        var gymTime = Utc("2024-01-01T12:00:00Z");

        var events = new ReconstructionEvent[]
        {
            new HappyDeltaEvent(LogId: "d1", OccurredAtUtc: deltaTime, Delta: 25),
            new GymTrainEvent(LogId: "g1", OccurredAtUtc: gymTime, HappyUsed: 10),
        };

        var result = HappyReconstructor.RunBackwards(
            events: events,
            currentHappy: 50,
            anchorTimeUtc: anchor);

        var derived = Assert.Single(result.DerivedGymTrains);
        Assert.Equal(gymTime, derived.OccurredAtUtc);
        Assert.Equal(35, derived.HappyBeforeTrain);
        Assert.Equal(10, derived.HappyUsed);
        Assert.Equal(25, derived.HappyAfterTrain);
    }

    private static DateTimeOffset Utc(string iso)
        => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
}
