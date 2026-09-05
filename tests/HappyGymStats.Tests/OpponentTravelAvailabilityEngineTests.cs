using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class OpponentTravelAvailabilityEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fresh_torn_observation_supersedes_older_travel_estimate()
    {
        var result = OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now.AddMinutes(-10), OpponentLocationCategory.Travelling) with
            {
                EstimatedRemainingTravel = TimeSpan.FromMinutes(30),
                EstimatedUncertainty = TimeSpan.FromMinutes(5),
            },
            Observation(Now.AddMinutes(-1), OpponentLocationCategory.Torn),
        ], Now);

        Assert.True(result.IsAttackableNow);
        Assert.Equal(TravelWindowPrecision.ObservedNow, result.Precision);
        Assert.Equal(Now.AddMinutes(-1), result.SourceObservedAtUtc);
        Assert.Contains("superseded", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Estimated_window_retains_uncertainty_and_provenance()
    {
        var result = OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now.AddMinutes(-2), OpponentLocationCategory.Travelling) with
            {
                Destination = "Mexico",
                EstimatedRemainingTravel = TimeSpan.FromMinutes(20),
                EstimatedUncertainty = TimeSpan.FromMinutes(4),
                Provenance = ["faction-members:sample-42"],
            },
        ], Now);

        Assert.Equal(TravelWindowPrecision.Estimated, result.Precision);
        Assert.False(result.IsAttackableNow);
        Assert.Equal(Now.AddMinutes(14), result.AttackableFromUtc);
        Assert.Equal(Now.AddMinutes(22), result.AttackableUntilUtc);
        Assert.Equal("Mexico", result.Destination);
        Assert.Equal("faction-members:sample-42", Assert.Single(result.Provenance));
        Assert.Contains("not exact", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_timing_never_invents_eta()
    {
        var result = OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now.AddMinutes(-1), OpponentLocationCategory.Travelling) with { Destination = "Canada" },
        ], Now);

        Assert.Equal(TravelWindowPrecision.Unknown, result.Precision);
        Assert.Null(result.AttackableFromUtc);
        Assert.Null(result.AttackableUntilUtc);
        Assert.Contains("no return or attackable ETA is invented", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_observation_drops_old_estimate_instead_of_outliving_source()
    {
        var result = OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now.AddMinutes(-16), OpponentLocationCategory.Travelling) with
            {
                EstimatedRemainingTravel = TimeSpan.FromMinutes(30),
                EstimatedUncertainty = TimeSpan.FromMinutes(5),
            },
        ], Now);

        Assert.False(result.IsFresh);
        Assert.Equal(TravelWindowPrecision.Unknown, result.Precision);
        Assert.Null(result.AttackableFromUtc);
        Assert.Null(result.AttackableUntilUtc);
    }

    [Fact]
    public void Authoritative_return_timestamp_is_not_downgraded_to_estimate()
    {
        var authoritative = Now.AddMinutes(12);
        var result = OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now.AddMinutes(-1), OpponentLocationCategory.Travelling) with
            {
                AuthoritativeReturnAtUtc = authoritative,
                EstimatedRemainingTravel = TimeSpan.FromMinutes(40),
                EstimatedUncertainty = TimeSpan.FromMinutes(10),
            },
        ], Now);

        Assert.Equal(TravelWindowPrecision.Authoritative, result.Precision);
        Assert.Equal(authoritative, result.AttackableFromUtc);
        Assert.Null(result.AttackableUntilUtc);
    }

    [Fact]
    public void Undefined_location_and_impossible_times_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now, (OpponentLocationCategory)999),
        ], Now));

        Assert.Throws<ArgumentException>(() => OpponentTravelAvailabilityEngine.EvaluateLatest([
            Observation(Now, OpponentLocationCategory.Travelling) with
            {
                AuthoritativeReturnAtUtc = Now.AddMinutes(-1),
            },
        ], Now));
    }

    private static OpponentTravelObservation Observation(DateTimeOffset observedAt, OpponentLocationCategory location)
        => new()
        {
            ObservedAtUtc = observedAt,
            Location = location,
        };
}
