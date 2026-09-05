using HappyGymStats.Blazor.Components.Pages;
using HappyGymStats.Blazor.Components.Shared;

namespace HappyGymStats.Tests;

public sealed class WarScoutProvenanceTests
{
    public static TheoryData<WarScoutMetric> MeasuredMetrics => new()
    {
        WarScoutMetric.TotalWarsObserved,
        WarScoutMetric.ObservedDateRange,
        WarScoutMetric.BackfillStatus,
        WarScoutMetric.BackfillProgress,
        WarScoutMetric.BackfillUpdated,
        WarScoutMetric.MembersSeen,
        WarScoutMetric.OutcomeSample,
        WarScoutMetric.WarsParticipated,
        WarScoutMetric.ScoreRange,
        WarScoutMetric.LastSeen
    };

    [Theory]
    [MemberData(nameof(MeasuredMetrics))]
    public void Direct_observations_are_measured(WarScoutMetric metric)
    {
        Assert.Equal(FigureKind.Measured, WarScoutProvenance.For(metric));
    }

    [Fact]
    public void Every_other_current_scout_metric_is_inferred_and_none_are_projected()
    {
        var measured = MeasuredMetrics.Select(row => (WarScoutMetric)row[0]).ToHashSet();

        foreach (var metric in Enum.GetValues<WarScoutMetric>())
        {
            var kind = WarScoutProvenance.For(metric);
            Assert.NotEqual(FigureKind.Projected, kind);

            if (!measured.Contains(metric))
            {
                Assert.Equal(FigureKind.Inferred, kind);
            }
        }
    }

    [Fact]
    public void Scout_page_routes_visible_profile_values_through_shared_figure_mapping()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/WarScout.razor");

        Assert.DoesNotContain("FigureKind.Projected", content, StringComparison.Ordinal);
        Assert.Contains("WarScoutMetric.BackfillStatus", content, StringComparison.Ordinal);
        Assert.Contains("WarScoutMetric.SampleSufficiency", content, StringComparison.Ordinal);
        Assert.Contains("WarScoutMetric.PointsPerHour", content, StringComparison.Ordinal);
        Assert.Contains("WarScoutMetric.ThreatTier", content, StringComparison.Ordinal);
        Assert.Contains("WarScoutMetric.LumpAdjustedScorePerAttack", content, StringComparison.Ordinal);
        Assert.Contains("WarScoutMetric.LastSeen", content, StringComparison.Ordinal);
        Assert.True(CountOccurrences(content, "<Figure ") >= 20, "Scout should route its visible summary/detail values through Figure.");
    }

    private static int CountOccurrences(string content, string token)
    {
        var count = 0;
        var offset = 0;

        while ((offset = content.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root from test output directory.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }
}
