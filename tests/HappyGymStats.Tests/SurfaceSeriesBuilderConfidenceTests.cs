using HappyGymStats.Core.Reconstruction;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SurfaceSeriesBuilderConfidenceTests
{
    [Fact]
    public void Build_projects_confidence_and_sorted_reason_codes_from_provenance()
    {
        var raws = new[]
        {
            new SurfaceSeriesBuilder.RawGymLog("log-1", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                "{\"data\":{\"energy_used\":10,\"strength_before\":100,\"strength_increased\":5}}")
        };

        var derived = new Dictionary<string, SurfaceSeriesBuilder.DerivedGym>
        {
            ["log-1"] = new("log-1", 4500)
        };

        var events = Array.Empty<SurfaceSeriesBuilder.DerivedHappyEvent>();

        var provenance = new Dictionary<string, IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>>
        {
            ["log-1"] = new List<SurfaceSeriesBuilder.ModifierProvenance>
            {
                new("personal", "verified", "source-log"),
                new("faction", "unresolved", "missing-faction-record"),
                new("company", "unresolved", "missing-company-record")
            }
        };

        var payload = SurfaceSeriesBuilder.Build(raws, derived, events, provenance);

        Assert.Single(payload.GymConfidence);
        Assert.Equal(0.5625, payload.GymConfidence[0], 4);
        Assert.Equal(
            ["missing-company-record", "missing-faction-record", "source-log"],
            payload.GymConfidenceReasons[0]);
    }

    [Fact]
    public void Build_uses_fallback_reason_when_provenance_missing()
    {
        var raws = new[]
        {
            new SurfaceSeriesBuilder.RawGymLog("log-2", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                "{\"data\":{\"energy_used\":10,\"defense_before\":200,\"defense_increased\":8}}")
        };

        var derived = new Dictionary<string, SurfaceSeriesBuilder.DerivedGym>
        {
            ["log-2"] = new("log-2", 5200)
        };

        var payload = SurfaceSeriesBuilder.Build(
            raws,
            derived,
            Array.Empty<SurfaceSeriesBuilder.DerivedHappyEvent>(),
            new Dictionary<string, IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>>());

        Assert.Single(payload.GymConfidence);
        Assert.Equal(0.2, payload.GymConfidence[0], 4);
        Assert.Equal(["missing-provenance-record"], payload.GymConfidenceReasons[0]);
    }
}
