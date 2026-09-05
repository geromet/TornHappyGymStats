using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class WarScoutStaticContractTests
{
    [Fact]
    public void WarScout_page_declares_authorized_interactive_route_and_evidence_first_labels()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/WarScout.razor");

        Assert.Contains("@page \"/war/scout/{FactionId:long}\"", content, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", content, StringComparison.Ordinal);
        Assert.Contains("@rendermode InteractiveServer", content, StringComparison.Ordinal);
        Assert.Contains("Pre-war briefing", content, StringComparison.Ordinal);
        Assert.Contains("Evidence coverage", content, StringComparison.Ordinal);
        Assert.Contains("Historical conclusions", content, StringComparison.Ordinal);
        Assert.Contains("Threat roster", content, StringComparison.Ordinal);
        Assert.Contains("Lump-adjusted score", content, StringComparison.Ordinal);
        Assert.Contains("<details class=\"scout-member\">", content, StringComparison.Ordinal);
        Assert.DoesNotContain("lockdown", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("what you must beat", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarScout_responsive_css_has_mobile_tablet_and_desktop_layout_contracts()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/WarScout.razor.css");

        Assert.Contains("@media (max-width: 767px)", content, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 768px) and (max-width: 1099px)", content, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(12rem, 1.8fr) repeat(4, minmax(7rem, 1fr));", content, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 420px)", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WarScoutLookup_page_declares_authorized_interactive_route_at_scout_index()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/WarScoutLookup.razor");

        Assert.Contains("@page \"/war/scout\"", content, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WarScout_service_calls_the_scout_api_endpoint()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarScoutService.cs");

        Assert.Contains("/api/v1/war/scout/", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WarScout_layout_exposes_a_navigation_entry()
    {
        var content = ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor");

        Assert.Contains("Href=\"/war/scout\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WarScout_api_controller_requires_the_user_role_and_uses_a_positive_faction_id_route()
    {
        var content = ReadRepoFile("src/HappyGymStats.Api/Controllers/WarScoutController.cs");

        Assert.Contains("[Authorize(Roles = Roles.User)]", content, StringComparison.Ordinal);
        Assert.Contains("[Route(\"api/v1/war/scout\")]", content, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"{factionId:long}\")]", content, StringComparison.Ordinal);
    }

    [Fact]
    public void WarScout_sources_do_not_reference_forbidden_collectors()
    {
        var combined = string.Join(
            "\n",
            ReadRepoFile("src/HappyGymStats.Contracts/Models/WarScoutDtos.cs"),
            ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/WarScoutService.cs"),
            ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/WarScout.razor"));

        var forbidden = new[] { "TornApiClient", "api.torn.com", "centrifugo", "Centrifugo" };

        foreach (var token in forbidden)
        {
            Assert.DoesNotContain(token, combined, StringComparison.OrdinalIgnoreCase);
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
