using Bunit;
using HappyGymStats.Blazor.Components.War;
using HappyGymStats.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace HappyGymStats.Tests;

public sealed class WarBoardComponentRenderedTests
{
    private const string RawTimerDiagnostic = "operator-only raw timer spacing diagnostic";

    [Fact]
    public async Task Exact_chain_timer_renders_one_measured_countdown_without_inferred_label()
    {
        await using var context = CreateContext();
        var cut = context.Render<WarChainCommandPanel>(parameters => parameters
            .Add(component => component.Chain, CreateChain("Exact", timerIsInferred: false, secondsSinceLastHit: null)));

        Assert.Contains("Chain lapses in", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("00:42 left, from Torn's own deadline.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Last hit", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".hgs-figure-marker-inferred"));

        var alert = cut.Find(".mud-alert");
        Assert.DoesNotContain(RawTimerDiagnostic, alert.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inferred_chain_timer_uses_curated_alert_copy_and_single_provenance_label()
    {
        await using var context = CreateContext();
        var cut = context.Render<WarChainCommandPanel>(parameters => parameters
            .Add(component => component.Chain, CreateChain("Inferred", timerIsInferred: true, secondsSinceLastHit: 280)));

        var timerFigures = cut.FindAll(".hgs-figure")
            .Where(element => element.TextContent.Contains("Last hit", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(timerFigures);
        Assert.Single(timerFigures[0].QuerySelectorAll(".hgs-figure-marker-inferred"));
        Assert.DoesNotContain("Last hit (inferred)", cut.Markup, StringComparison.Ordinal);

        var alert = cut.Find(".mud-alert");
        Assert.Contains("Estimated from score polls", alert.TextContent, StringComparison.Ordinal);
        Assert.Contains("Hit now if you can", alert.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(RawTimerDiagnostic, alert.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hole_panel_distinguishes_empty_and_actionable_states()
    {
        await using var context = CreateContext();
        var empty = context.Render<WarHoleAlerts>(parameters => parameters
            .Add(component => component.Holes, Array.Empty<WarHoleDto>()));
        Assert.Contains("No current hole alerts.", empty.Markup, StringComparison.Ordinal);
        Assert.Empty(empty.FindAll(".war-hole"));

        var populated = context.Render<WarHoleAlerts>(parameters => parameters
            .Add(component => component.Holes, new[]
            {
                new WarHoleDto(
                    Kind: "IdleAttacker",
                    Severity: "Critical",
                    FactionId: 1,
                    FactionName: "Our Faction",
                    OpponentFactionId: 2,
                    MemberId: 42,
                    MemberName: "Alice",
                    Reason: "Idle too long while targets are open")
            }));

        Assert.DoesNotContain("No current hole alerts.", populated.Markup, StringComparison.Ordinal);
        Assert.Contains("Alice", populated.Markup, StringComparison.Ordinal);
        Assert.Single(populated.FindAll(".war-hole-critical"));
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        return context;
    }

    private static WarChainCommandDto CreateChain(
        string confidence,
        bool timerIsInferred,
        int? secondsSinceLastHit) => new(
        ChainLength: 23,
        CurrentMultiplier: 1.2,
        NextMilestone: 25,
        HitsToNextMilestone: 2,
        NextMilestoneBonus: 10,
        IsInReservationWindow: false,
        ForfeitedValueIfCrossedOutside: 0,
        AttackableWarTargetCount: 3,
        Mode: "WarTargetsOnly",
        Advice: "Keep pressure on war targets.",
        Alert: "TimerRunningLow",
        TimerIsInferred: timerIsInferred,
        SecondsSinceLastHit: secondsSinceLastHit,
        SecondsUntilLapse: 42,
        TimerSpacingSeconds: 30,
        TimerDiagnostic: RawTimerDiagnostic,
        TimerConfidence: confidence,
        LapsesAtUtc: confidence == "Exact" ? DateTimeOffset.UtcNow.AddSeconds(42) : null);
}
