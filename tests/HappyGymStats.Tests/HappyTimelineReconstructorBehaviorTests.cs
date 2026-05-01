using HappyGymStats.Core.Reconstruction;
using Xunit;
using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Tests;

public sealed class HappyTimelineReconstructorBehaviorTests
{
    [Fact]
    public void Max_happy_drop_and_restore_before_next_tick_does_not_clamp_current_happy()
    {
        var result = HappyTimelineReconstructor.RunForward(new ReconstructionEvent[]
        {
            new GymTrainEvent("seed", Utc(2026, 1, 1, 10, 59, 59), 0),
            new MaxHappyEvent("max-up", Utc(2026, 1, 1, 11, 0, 1), 100, 2000),
            new HappyDeltaEvent("gain", Utc(2026, 1, 1, 11, 0, 2), 1700),
            new MaxHappyEvent("max-down", Utc(2026, 1, 1, 11, 2, 0), 2000, 1200),
            new MaxHappyEvent("max-restore", Utc(2026, 1, 1, 11, 3, 0), 1200, 2000),
            new GymTrainEvent("train", Utc(2026, 1, 1, 11, 4, 0), 100),
        });

        var eventsById = result.DerivedHappyEvents.ToDictionary(e => e.EventId, StringComparer.Ordinal);
        var train = Assert.Single(result.DerivedGymTrains.Where(t => t.LogId == "train"));

        Assert.Equal(1800, train.HappyBeforeTrain);
        Assert.Equal(1700, train.HappyAfterTrain);

        Assert.Equal(0, eventsById["max-down"].Delta);
        Assert.Equal(1800, eventsById["max-down"].HappyBeforeEvent);
        Assert.Equal(1800, eventsById["max-down"].HappyAfterEvent);

        Assert.Equal(0, eventsById["max-restore"].Delta);
        Assert.Equal(1800, eventsById["max-restore"].HappyBeforeEvent);
        Assert.Equal(1800, eventsById["max-restore"].HappyAfterEvent);
    }

    [Fact]
    public void Max_happy_drop_clamps_on_the_next_quarter_hour_tick()
    {
        var result = HappyTimelineReconstructor.RunForward(new ReconstructionEvent[]
        {
            new GymTrainEvent("seed", Utc(2026, 1, 1, 10, 59, 59), 0),
            new MaxHappyEvent("max-up", Utc(2026, 1, 1, 11, 0, 1), 100, 2000),
            new HappyDeltaEvent("gain", Utc(2026, 1, 1, 11, 0, 2), 1700),
            new MaxHappyEvent("max-down", Utc(2026, 1, 1, 11, 2, 0), 2000, 1200),
            new GymTrainEvent("after-tick", Utc(2026, 1, 1, 11, 16, 0), 50),
        });

        var regenTick = Assert.Single(result.DerivedHappyEvents.Where(e => e.EventId == $"regen@{Utc(2026, 1, 1, 11, 15, 0).ToUnixTimeSeconds()}"));
        Assert.Equal(1800, regenTick.HappyBeforeEvent);
        Assert.Equal(1200, regenTick.HappyAfterEvent);
        Assert.Equal(-600, regenTick.Delta);
        Assert.True(regenTick.ClampedToMax);

        var train = Assert.Single(result.DerivedGymTrains.Where(t => t.LogId == "after-tick"));
        Assert.Equal(1200, train.HappyBeforeTrain);
        Assert.Equal(1150, train.HappyAfterTrain);
    }

    [Fact]
    public void Provenance_reason_codes_for_unresolved_dependencies_are_deterministic()
    {
        Assert.Equal("missing-faction-record", ModifierProvenanceReasonCodes.MissingFactionRecord);
        Assert.Equal("missing-company-record", ModifierProvenanceReasonCodes.MissingCompanyRecord);

        Assert.NotEqual(
            ModifierProvenanceReasonCodes.MissingFactionRecord,
            ModifierProvenanceReasonCodes.MissingCompanyRecord);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second)
        => new(year, month, day, hour, minute, second, TimeSpan.Zero);
}
