using HappyGymStats.Core.Reconstruction;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SurfaceSeriesBuilderConfidenceTests
{
    [Fact]
    public void Build_projects_confidence_and_sorted_reason_codes_from_provenance()
    {
        var gymLogs = new[]
        {
            new GymLogEntry(
                "log-1", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                HappyBeforeTrain: 4500,
                EnergyUsed: 10,
                StrengthBefore: 100, StrengthIncreased: 5,
                DefenseBefore: null, DefenseIncreased: null,
                SpeedBefore: null, SpeedIncreased: null,
                DexterityBefore: null, DexterityIncreased: null)
        };

        var provenance = new Dictionary<string, IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>>
        {
            ["log-1"] = new List<SurfaceSeriesBuilder.ModifierProvenance>
            {
                new("personal", "verified", "source-log"),
                new("faction", "unresolved", "missing-faction-record"),
                new("company", "unresolved", "missing-company-record")
            }
        };

        var payload = SurfaceSeriesBuilder.Build(gymLogs, provenance);

        Assert.Single(payload.GymConfidence);
        Assert.Equal(0.5625, payload.GymConfidence[0], 4);
        Assert.Equal(
            ["company-unresolved", "faction-unresolved", "personal-verified"],
            payload.GymConfidenceReasons[0]);
    }

    [Fact]
    public void Build_uses_fallback_reason_when_provenance_missing()
    {
        var gymLogs = new[]
        {
            new GymLogEntry(
                "log-2", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                HappyBeforeTrain: 5200,
                EnergyUsed: 10,
                StrengthBefore: null, StrengthIncreased: null,
                DefenseBefore: 200, DefenseIncreased: 8,
                SpeedBefore: null, SpeedIncreased: null,
                DexterityBefore: null, DexterityIncreased: null)
        };

        var payload = SurfaceSeriesBuilder.Build(
            gymLogs,
            new Dictionary<string, IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>>());

        Assert.Single(payload.GymConfidence);
        Assert.Equal(0.2, payload.GymConfidence[0], 4);
        Assert.Equal(["missing-provenance-record"], payload.GymConfidenceReasons[0]);
    }
}
