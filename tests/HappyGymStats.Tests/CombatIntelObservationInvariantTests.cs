using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class CombatIntelObservationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 17, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1d)]
    [InlineData(-0.01d)]
    public void Create_rejects_negative_exact_values(double value)
    {
        Assert.Throws<ArgumentException>(() => CombatIntelObservation.Create(
            "negative-exact",
            42,
            "provider",
            Now,
            Now,
            CombatIntelClassification.Exact,
            value: (decimal)value));
    }

    [Theory]
    [InlineData(-1d, 100d)]
    [InlineData(0d, -1d)]
    [InlineData(-2d, -1d)]
    public void Create_rejects_negative_estimate_bounds(double lower, double upper)
    {
        Assert.Throws<ArgumentException>(() => CombatIntelObservation.Create(
            "negative-estimate",
            42,
            "provider",
            Now,
            Now,
            CombatIntelClassification.Estimated,
            lowerBound: (decimal)lower,
            upperBound: (decimal)upper));
    }

    [Fact]
    public void Create_allows_zero_values_and_bounds()
    {
        var exact = CombatIntelObservation.Create(
            "zero-exact",
            42,
            "provider",
            Now,
            Now,
            CombatIntelClassification.Exact,
            value: 0m);
        var estimate = CombatIntelObservation.Create(
            "zero-estimate",
            42,
            "provider",
            Now,
            Now,
            CombatIntelClassification.Estimated,
            lowerBound: 0m,
            upperBound: 0m);

        Assert.Equal(0m, exact.Value);
        Assert.Equal(0m, estimate.LowerBound);
        Assert.Equal(0m, estimate.UpperBound);
    }

    [Fact]
    public void Create_rejects_self_supersession()
    {
        Assert.Throws<ArgumentException>(() => CombatIntelObservation.Create(
            "same-id",
            42,
            "provider",
            Now,
            Now,
            CombatIntelClassification.Estimated,
            lowerBound: 100m,
            upperBound: 200m,
            supersedesObservationId: "same-id"));
    }
}
