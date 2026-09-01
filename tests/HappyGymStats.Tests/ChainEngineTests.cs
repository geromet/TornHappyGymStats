using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ChainEngineTests
{
    // Fixed values cross-checked against chain.py's `selftest` command.
    [Theory]
    [InlineData(250, 310.6273965988654)]
    [InlineData(1000, 1391.9011610555333)]
    public void SigmaMult_matches_reference_values(int length, double expected)
    {
        var engine = new ChainEngine();

        Assert.Equal(expected, engine.SigmaMult(length), precision: 6);
    }

    [Theory]
    [InlineData(250, 310)]
    [InlineData(2500, 2550)]
    public void CumBonus_matches_reference_values(int length, int expected)
    {
        var engine = new ChainEngine();

        Assert.Equal(expected, engine.CumBonus(length));
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 0.75)]
    public void SigmaMult_handles_zero_and_low_chain_lengths(int length, double expected)
    {
        var engine = new ChainEngine();

        Assert.Equal(expected, engine.SigmaMult(length), precision: 6);
    }

    [Theory]
    [InlineData(0, 11.0, 0.0)]
    [InlineData(10, 11.0, 110.53934834041118)]
    public void ChainRespect_matches_expected_values(int length, double baseRespect, double expected)
    {
        var engine = new ChainEngine();

        Assert.Equal(expected, engine.ChainRespect(length, baseRespect), precision: 6);
    }

    [Fact]
    public void CumulativeCurve_ends_at_combo_respect()
    {
        var engine = new ChainEngine();
        var legs = new[] { 25, 10, 10 };

        var curve = engine.CumulativeCurve(legs, ChainEngine.DefaultBase);

        Assert.NotEmpty(curve);
        Assert.Equal(engine.ComboRespect(legs, ChainEngine.DefaultBase), curve[^1], precision: 6);
    }

    [Fact]
    public void EnumerateSplits_orders_ranked_combinations_by_respect()
    {
        var engine = new ChainEngine();

        var splits = ChainEngine.EnumerateSplits(engine, budget: 1000, baseResp: 11, minLeg: 10);

        Assert.NotEmpty(splits);
        Assert.True(splits.Zip(splits.Skip(1)).All(pair => pair.First.Respect >= pair.Second.Respect));
    }

    [Fact]
    public void EnumerateSplits_returns_empty_for_zero_budget()
    {
        var engine = new ChainEngine();

        Assert.Empty(ChainEngine.EnumerateSplits(engine, budget: 0, baseResp: 11));
    }

    [Fact]
    public void Implementation_is_core_owned_and_chain_tests_no_longer_alias_blazor()
    {
        var repoRoot = FindRepoRoot();
        var corePath = Path.Combine(repoRoot, "src", "HappyGymStats.Core", "War", "ChainEngine.cs");
        var legacyPath = Path.Combine(repoRoot, "src", "HappyGymStats.Blazor", "HappyGymStats.Blazor", "Chain", "ChainEngine.cs");
        var testSourcePath = Path.Combine(repoRoot, "tests", "HappyGymStats.Tests", "ChainEngineTests.cs");

        Assert.True(File.Exists(corePath));
        Assert.False(File.Exists(legacyPath));
        Assert.Equal("HappyGymStats.Core.War", typeof(ChainEngine).Namespace);

        var testSourceLines = File.ReadAllLines(testSourcePath);
        Assert.NotEqual("extern alias blazor;", testSourceLines[0].Trim());
        Assert.Contains("using HappyGymStats.Core.War;", testSourceLines);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
