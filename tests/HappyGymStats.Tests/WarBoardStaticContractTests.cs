using System;
using System.IO;
using System.Text.RegularExpressions;

namespace HappyGymStats.Tests;

public sealed class WarBoardStaticContractTests
{
    private static readonly string[] WarComponentFiles =
    [
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarBoardHeader.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarBoardStateBanners.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarBoardSummary.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarFactionList.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarFactionCard.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarChainCommandPanel.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarRosterTable.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarHoleAlerts.razor",
        "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarDiagnosticsPanel.razor"
    ];

    [Fact]
    public void War_page_declares_authorized_interactive_route_and_composes_required_sections()
    {
        var page = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor");
        var presentation = string.Join("\n", WarComponentFiles.Select(ReadRepoFile));

        Assert.Contains("@page \"/war\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", page, StringComparison.Ordinal);
        Assert.Contains("<WarBoardHeader", page, StringComparison.Ordinal);
        Assert.Contains("<WarBoardStateBanners", page, StringComparison.Ordinal);
        Assert.Contains("<WarBoardSummary", page, StringComparison.Ordinal);
        Assert.Contains("<WarFactionList", page, StringComparison.Ordinal);
        Assert.Contains("<WarHoleAlerts", page, StringComparison.Ordinal);
        Assert.Contains("<WarDiagnosticsPanel", page, StringComparison.Ordinal);
        Assert.True(page.Split('\n').Length < 80, "War.razor should remain a small composition surface.");

        Assert.Contains("Coverage ratio", presentation, StringComparison.Ordinal);
        Assert.Contains("Hole alerts", presentation, StringComparison.Ordinal);
        Assert.Contains("StaleDataBanner", presentation, StringComparison.Ordinal);
        Assert.Contains("Review heartbeat, warnings, and hub connection status before acting on roster gaps.", presentation, StringComparison.Ordinal);
        Assert.Contains("Hub connection", presentation, StringComparison.Ordinal);
        Assert.Contains("Attacks to finish", presentation, StringComparison.Ordinal);
        Assert.Contains("Hospital countdown", presentation, StringComparison.Ordinal);
        Assert.Contains("ETA", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void War_board_service_bootstraps_from_rest_and_subscribes_to_hub_deltas()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarBoardService.cs");

        Assert.Contains("/api/v1/war/current", content, StringComparison.Ordinal);
        Assert.Contains("/api/hub/war", content, StringComparison.Ordinal);
        Assert.Contains("WarStateUpdated", content, StringComparison.Ordinal);
        Assert.Contains("RequestCurrentState", content, StringComparison.Ordinal);
        Assert.Contains("WithAutomaticReconnect", content, StringComparison.Ordinal);
        Assert.Contains("IServerAccessTokenProvider", content, StringComparison.Ordinal);

        var tokenProvider = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ServerAccessTokenProvider.cs");
        Assert.Contains("GetTokenAsync(\"access_token\")", tokenProvider, StringComparison.Ordinal);
        Assert.Contains("PersistAsJson", tokenProvider, StringComparison.Ordinal);
        Assert.Contains("TryTakeFromJson", tokenProvider, StringComparison.Ordinal);
    }

    [Fact]
    public void War_layout_exposes_navigation_entry()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor");

        Assert.Contains("Href=\"/war\"", content, StringComparison.Ordinal);
        Assert.Contains(">War board<", content, StringComparison.Ordinal);
    }

    [Fact]
    public void War_component_styles_define_all_hole_severities()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarHoleAlerts.razor.css");

        Assert.Contains(".war-hole-critical", content, StringComparison.Ordinal);
        Assert.Contains(".war-hole-high", content, StringComparison.Ordinal);
        Assert.Contains(".war-hole-medium", content, StringComparison.Ordinal);
        Assert.Contains(".war-hole-low", content, StringComparison.Ordinal);
    }

    [Fact]
    public void War_components_keep_responsive_composition_contracts()
    {
        var page = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor");
        var factions = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarFactionList.razor");
        var roster = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarRosterTable.razor");

        Assert.Contains("xs=\"12\" lg=\"8\"", page, StringComparison.Ordinal);
        Assert.Contains("xs=\"12\" lg=\"4\"", page, StringComparison.Ordinal);
        Assert.Contains("xs=\"12\" xl=\"6\"", factions, StringComparison.Ordinal);
        Assert.Contains("Breakpoint=\"Breakpoint.Md\"", roster, StringComparison.Ordinal);
    }

    [Fact]
    public void War_board_sources_do_not_reference_forbidden_collectors()
    {
        var combined = string.Join(
            "\n",
            new[]
            {
                ReadRepoFile("src/HappyGymStats.Contracts/Models/WarDtos.cs"),
                ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarBoardService.cs"),
                ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor")
            }.Concat(WarComponentFiles.Select(ReadRepoFile)));

        var forbidden = new[]
        {
            "TornApiClient",
            "api.torn.com",
            "centrifugo",
            "Centrifugo",
            "ajax",
            "scrap",
            "PersonalLane",
            "personal-lane",
            "repository-backed personal lane"
        };

        foreach (var token in forbidden)
        {
            Assert.DoesNotMatch(new Regex(Regex.Escape(token), RegexOptions.IgnoreCase), combined);
        }
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
