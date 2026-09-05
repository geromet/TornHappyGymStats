using Bunit;
using HappyGymStats.Blazor.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ChainPlannerRenderedTests : BunitContext
{
    public ChainPlannerRenderedTests()
    {
        Services.AddLogging();
        Services.AddMudServices();
    }

    [Fact]
    public void Planner_renders_decisions_then_recommendation_before_advanced_model_controls()
    {
        var cut = Render<ChainCalculator>();
        var markup = cut.Markup;

        Assert.Contains("Chain planner", markup, StringComparison.Ordinal);
        Assert.Contains("Hits available", markup, StringComparison.Ordinal);
        Assert.Contains("Maximize total respect", markup, StringComparison.Ordinal);
        Assert.Contains("Recommended plan", markup, StringComparison.Ordinal);
        Assert.Contains("Alternatives", markup, StringComparison.Ordinal);
        Assert.Contains("Use as Plan A", markup, StringComparison.Ordinal);
        Assert.Contains("Use as Plan B", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Use the A / B buttons", markup, StringComparison.Ordinal);

        var recommendationIndex = markup.IndexOf("Recommended plan", StringComparison.Ordinal);
        var alternativesIndex = markup.IndexOf("Alternatives", StringComparison.Ordinal);
        var advancedIndex = markup.IndexOf("Advanced model settings", StringComparison.Ordinal);

        Assert.True(recommendationIndex >= 0 && alternativesIndex > recommendationIndex);
        Assert.True(advancedIndex > alternativesIndex);

        var advanced = cut.Find("[data-testid=advanced-model-settings]");
        Assert.False(advanced.HasAttribute("open"));
    }

    [Fact]
    public void Exhaustive_combinations_stay_secondary_until_requested()
    {
        var cut = Render<ChainCalculator>();

        Assert.Empty(cut.FindAll("[data-testid=chain-all-combinations]"));

        var showAll = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Show all", StringComparison.Ordinal));
        showAll.Click();

        Assert.Single(cut.FindAll("[data-testid=chain-all-combinations]"));
        Assert.Contains("Hide all combinations", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Budget_presets_recalculate_the_recommendation_without_changing_the_engine_contract()
    {
        var cut = Render<ChainCalculator>();

        cut.Find("button[aria-label='Use 250 hits']").Click();

        Assert.Contains("250 hits available", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Recommended plan", cut.Markup, StringComparison.Ordinal);
    }
}
