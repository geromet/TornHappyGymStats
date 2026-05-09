extern alias blazor;

using ChainEngine = blazor::HappyGymStats.Blazor.Chain.ChainEngine;
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
    [InlineData(new[] { 250, 250 }, 11, 7453.80272517504)]
    [InlineData(new[] { 300, 100, 100 }, 11, 7293.670978471314)]
    [InlineData(new[] { 500 }, 11, 7873.737623471623)]
    [InlineData(new[] { 10_000 }, 11, 190793.49925493213)]
    public void ComboRespect_matches_reference_values(int[] legs, double baseResp, double expected)
    {
        var engine = new ChainEngine();

        Assert.Equal(expected, engine.ComboRespect(legs, baseResp), precision: 6);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(5_000)]
    [InlineData(50_000)]
    public void SigmaMult_prefix_sum_agrees_with_closed_form(int length)
    {
        var engine = new ChainEngine();
        var closedForm = 0.25 * LGamma(length + 1) / Math.Log(10) + 0.75 * length;

        Assert.Equal(closedForm, engine.SigmaMult(length), precision: 9);
    }

    [Fact]
    public void EnumerateSplits_ranks_best_combination_first_and_matches_ComboRespect()
    {
        var engine = new ChainEngine();

        var splits = ChainEngine.EnumerateSplits(engine, budget: 1000, baseResp: 11, minLeg: 10);

        Assert.NotEmpty(splits);
        for (var i = 1; i < splits.Count; i++)
            Assert.True(splits[i - 1].Respect >= splits[i].Respect);

        var top = splits[0];
        Assert.Equal(1, top.Rank);
        Assert.Equal(engine.ComboRespect(top.Legs, 11), top.Respect, precision: 6);
    }

    private static double LGamma(double n)
    {
        if (n <= 1) return 0;
        return (n - 0.5) * Math.Log(n) - n + 0.5 * Math.Log(2 * Math.PI)
               + 1.0 / (12 * n) - 1.0 / (360 * n * n * n);
    }
}
