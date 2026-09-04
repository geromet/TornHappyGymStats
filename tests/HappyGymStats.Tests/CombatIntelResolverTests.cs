using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class CombatIntelResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Resolve_prefers_fresher_observation_and_keeps_stale_alternatives()
    {
        var staleExact = Observation(
            "exact-old",
            provider: "private-spy",
            observedMinutesAgo: 30,
            classification: CombatIntelClassification.Exact,
            value: 900m);
        var freshEstimate = Observation(
            "estimate-new",
            provider: "estimate-provider",
            observedMinutesAgo: 2,
            classification: CombatIntelClassification.Estimated,
            lower: 1000m,
            upper: 1500m);

        var result = CombatIntelResolver.Resolve(42, [staleExact, freshEstimate], new(), Now);

        Assert.Equal("estimate-new", result.Winner?.ObservationId);
        Assert.Single(result.Alternatives);
        Assert.Equal("exact-old", result.Alternatives[0].ObservationId);
        Assert.Equal(Now, result.ResolvedAtUtc);
    }

    [Fact]
    public void Resolve_prefers_exact_when_observation_and_fetch_times_are_equal()
    {
        var estimated = Observation(
            "estimated",
            provider: "a-provider",
            observedMinutesAgo: 1,
            classification: CombatIntelClassification.Estimated,
            lower: 500m,
            upper: 1000m);
        var exact = Observation(
            "exact",
            provider: "z-provider",
            observedMinutesAgo: 1,
            classification: CombatIntelClassification.Exact,
            value: 750m);

        var result = CombatIntelResolver.Resolve(42, [estimated, exact], new(), Now);

        Assert.Equal("exact", result.Winner?.ObservationId);
    }

    [Fact]
    public void Resolve_is_stable_across_input_order()
    {
        var first = Observation("b", provider: "provider-b", observedMinutesAgo: 1);
        var second = Observation("a", provider: "provider-a", observedMinutesAgo: 1);

        var forward = CombatIntelResolver.Resolve(42, [first, second], new(), Now);
        var reverse = CombatIntelResolver.Resolve(42, [second, first], new(), Now);

        Assert.Equal("a", forward.Winner?.ObservationId);
        Assert.Equal(forward.Winner, reverse.Winner);
        Assert.Equal(forward.Alternatives, reverse.Alternatives);
    }

    [Fact]
    public void Resolve_rejects_private_faction_intel_from_another_faction()
    {
        var publicObservation = Observation("public", provider: "public", observedMinutesAgo: 5);
        var ownFaction = Observation(
            "own",
            provider: "private",
            observedMinutesAgo: 1,
            visibilityScope: CombatIntelVisibilityScope.Faction,
            visibilityOwner: "faction-10");
        var otherFaction = Observation(
            "other",
            provider: "private",
            observedMinutesAgo: 0,
            visibilityScope: CombatIntelVisibilityScope.Faction,
            visibilityOwner: "faction-20");

        var result = CombatIntelResolver.Resolve(
            42,
            [publicObservation, otherFaction, ownFaction],
            new CombatIntelAccessContext { FactionId = "faction-10" },
            Now);

        Assert.Equal("own", result.Winner?.ObservationId);
        Assert.Single(result.Alternatives);
        Assert.Equal("public", result.Alternatives[0].ObservationId);
        Assert.DoesNotContain(result.Alternatives, observation => observation.ObservationId == "other");
    }

    [Fact]
    public void Resolve_rejects_member_private_intel_for_another_member()
    {
        var privateObservation = Observation(
            "private",
            provider: "linked",
            observedMinutesAgo: 0,
            visibilityScope: CombatIntelVisibilityScope.Member,
            visibilityOwner: "member-a");

        var denied = CombatIntelResolver.Resolve(
            42,
            [privateObservation],
            new CombatIntelAccessContext { MemberId = "member-b" },
            Now);
        var allowed = CombatIntelResolver.Resolve(
            42,
            [privateObservation],
            new CombatIntelAccessContext { MemberId = "member-a" },
            Now);

        Assert.Null(denied.Winner);
        Assert.Empty(denied.Alternatives);
        Assert.Equal("private", allowed.Winner?.ObservationId);
    }

    [Fact]
    public void Resolve_never_mixes_different_players()
    {
        var target = Observation("target", provider: "provider", observedMinutesAgo: 10);
        var newerOtherPlayer = Observation("other-player", provider: "provider", observedMinutesAgo: 0, playerId: 99);

        var result = CombatIntelResolver.Resolve(42, [newerOtherPlayer, target], new(), Now);

        Assert.Equal("target", result.Winner?.ObservationId);
        Assert.Empty(result.Alternatives);
    }

    [Fact]
    public void Resolution_preserves_range_provider_metadata_and_supersession_provenance()
    {
        var observation = Observation(
            "estimate-new",
            provider: "provider-neutral-name",
            observedMinutesAgo: 1,
            lower: 1_000m,
            upper: 2_000m,
            providerMetadata: "opaque-provider-metadata",
            supersedesObservationId: "estimate-old");

        var result = CombatIntelResolver.Resolve(42, [observation], new(), Now);

        Assert.Equal(1_000m, result.Winner?.LowerBound);
        Assert.Equal(2_000m, result.Winner?.UpperBound);
        Assert.Equal("provider-neutral-name", result.Winner?.Provider);
        Assert.Equal("opaque-provider-metadata", result.Winner?.ProviderMetadata);
        Assert.Equal("estimate-old", result.Winner?.SupersedesObservationId);
    }

    [Theory]
    [InlineData(CombatIntelClassification.Exact, null, null, null)]
    [InlineData(CombatIntelClassification.Exact, 100d, 90d, null)]
    [InlineData(CombatIntelClassification.Estimated, 100d, 90d, 110d)]
    [InlineData(CombatIntelClassification.Estimated, null, null, 110d)]
    [InlineData(CombatIntelClassification.Estimated, null, 120d, 110d)]
    public void Observation_creation_rejects_invalid_value_shapes(
        CombatIntelClassification classification,
        double? value,
        double? lower,
        double? upper)
    {
        Assert.Throws<ArgumentException>(() => CombatIntelObservation.Create(
            "bad-shape",
            42,
            "provider",
            Now,
            Now,
            classification,
            value is null ? null : (decimal)value.Value,
            lower is null ? null : (decimal)lower.Value,
            upper is null ? null : (decimal)upper.Value));
    }

    [Theory]
    [InlineData("", 42, "provider")]
    [InlineData("id", 0, "provider")]
    [InlineData("id", -1, "provider")]
    [InlineData("id", 42, "")]
    [InlineData("id", 42, "   ")]
    public void Observation_creation_rejects_invalid_identity_and_provenance(string id, long playerId, string provider)
    {
        Assert.ThrowsAny<ArgumentException>(() => CombatIntelObservation.Create(
            id,
            playerId,
            provider,
            Now,
            Now,
            CombatIntelClassification.Estimated,
            lowerBound: 100m,
            upperBound: 200m));
    }

    [Fact]
    public void Observation_creation_rejects_observed_time_after_fetch_time()
    {
        Assert.Throws<ArgumentException>(() => CombatIntelObservation.Create(
            "bad-time",
            42,
            "provider",
            Now,
            Now.AddSeconds(1),
            CombatIntelClassification.Estimated,
            lowerBound: 100m,
            upperBound: 200m));
    }

    [Theory]
    [InlineData(CombatIntelVisibilityScope.Faction)]
    [InlineData(CombatIntelVisibilityScope.Member)]
    public void Observation_creation_rejects_private_scope_without_owner(CombatIntelVisibilityScope scope)
    {
        Assert.Throws<ArgumentException>(() => CombatIntelObservation.Create(
            "private-without-owner",
            42,
            "provider",
            Now,
            Now,
            CombatIntelClassification.Estimated,
            lowerBound: 100m,
            upperBound: 200m,
            visibilityScope: scope));
    }

    private static CombatIntelObservation Observation(
        string id,
        string provider,
        int observedMinutesAgo,
        CombatIntelClassification classification = CombatIntelClassification.Estimated,
        decimal? value = null,
        decimal? lower = 100m,
        decimal? upper = 200m,
        long playerId = 42,
        CombatIntelVisibilityScope visibilityScope = CombatIntelVisibilityScope.Public,
        string? visibilityOwner = null,
        string? providerMetadata = null,
        string? supersedesObservationId = null)
    {
        var observedAt = Now.AddMinutes(-observedMinutesAgo);
        return CombatIntelObservation.Create(
            id,
            playerId,
            provider,
            observedAt,
            observedAt,
            classification,
            value,
            classification == CombatIntelClassification.Exact ? null : lower,
            classification == CombatIntelClassification.Exact ? null : upper,
            visibilityScope,
            visibilityOwner,
            providerMetadata,
            supersedesObservationId);
    }
}
