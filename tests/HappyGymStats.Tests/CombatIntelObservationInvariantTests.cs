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

    [Fact]
    public void CreateFromProvider_rejects_timestamps_beyond_allowed_future_skew()
    {
        var tooFarFuture = Now + CombatIntelObservation.MaxProviderFutureSkew + TimeSpan.FromSeconds(1);

        Assert.Throws<ArgumentException>(() => CombatIntelObservation.CreateFromProvider(
            "future-provider",
            42,
            "provider",
            tooFarFuture,
            tooFarFuture,
            Now,
            CombatIntelClassification.Exact,
            value: 100m));
    }

    [Fact]
    public void CreateFromProvider_allows_timestamp_at_future_skew_boundary()
    {
        var allowedFuture = Now + CombatIntelObservation.MaxProviderFutureSkew;

        var observation = CombatIntelObservation.CreateFromProvider(
            "small-skew",
            42,
            "provider",
            allowedFuture,
            allowedFuture,
            Now,
            CombatIntelClassification.Exact,
            value: 100m);

        Assert.Equal(allowedFuture, observation.FetchedAtUtc);
        Assert.Equal(allowedFuture, observation.ObservedAtUtc);
    }

    [Fact]
    public void Rejected_future_provider_payload_cannot_dominate_resolver_freshness()
    {
        var legitimate = CombatIntelObservation.CreateFromProvider(
            "legitimate",
            42,
            "provider-a",
            Now,
            Now,
            Now,
            CombatIntelClassification.Exact,
            value: 100m);
        var farFuture = Now + TimeSpan.FromDays(30);

        Assert.Throws<ArgumentException>(() => CombatIntelObservation.CreateFromProvider(
            "malicious-future",
            42,
            "provider-b",
            farFuture,
            farFuture,
            Now,
            CombatIntelClassification.Exact,
            value: 999m));

        var resolution = CombatIntelResolver.Resolve(
            42,
            [legitimate],
            new CombatIntelAccessContext(),
            Now);

        Assert.Same(legitimate, resolution.Winner);
        Assert.Empty(resolution.Alternatives);
    }
}
