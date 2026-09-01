using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarBoardStaticContractTests
{
    [Fact]
    public void War_page_declares_authorized_interactive_route_and_required_labels()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor");

        Assert.Contains("@page \"/war\"", content, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", content, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", content, StringComparison.Ordinal);
        Assert.Contains("Coverage ratio", content, StringComparison.Ordinal);
        Assert.Contains("Hole alerts", content, StringComparison.Ordinal);
        Assert.Contains("Stale data.", content, StringComparison.Ordinal);
        Assert.Contains("Hub connection", content, StringComparison.Ordinal);
        Assert.Contains("Attacks to finish", content, StringComparison.Ordinal);
        Assert.Contains("Hospital countdown", content, StringComparison.Ordinal);
        Assert.Contains("ETA", content, StringComparison.Ordinal);
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
        Assert.Contains("GetTokenAsync(\"access_token\")", content, StringComparison.Ordinal);
    }

    [Fact]
    public void War_layout_exposes_navigation_entry()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor");

        Assert.Contains("Href=\"/war\"", content, StringComparison.Ordinal);
        Assert.Contains(">War board<", content, StringComparison.Ordinal);
    }

    [Fact]
    public void War_page_styles_define_all_hole_severities()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor.css");

        Assert.Contains(".war-hole-critical", content, StringComparison.Ordinal);
        Assert.Contains(".war-hole-high", content, StringComparison.Ordinal);
        Assert.Contains(".war-hole-medium", content, StringComparison.Ordinal);
        Assert.Contains(".war-hole-low", content, StringComparison.Ordinal);
    }

    [Fact]
    public void War_board_sources_do_not_reference_forbidden_collectors()
    {
        var combined = string.Join(
            "\n",
            ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/WarDtos.cs"),
            ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarBoardService.cs"),
            ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor"));

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
