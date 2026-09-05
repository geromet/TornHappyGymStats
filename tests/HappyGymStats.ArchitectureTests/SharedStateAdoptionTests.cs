using System;
using System.IO;

namespace HappyGymStats.ArchitectureTests;

public sealed class SharedStateAdoptionTests
{
    [Fact]
    public void Home_is_a_navigation_surface_and_does_not_own_surfaces_lifecycle()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("Train smarter. Scout faster.", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/my-stats\"", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/war/scout\"", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/gym-explorer\"", home, StringComparison.Ordinal);

        Assert.DoesNotContain("SurfacesService", home, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSurfacesAsync", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<LoadingState", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<ErrorState", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<EmptyState", home, StringComparison.Ordinal);
        Assert.DoesNotContain("MudProgressCircular", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_keeps_account_setup_out_of_the_landing_page()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("Account &amp; connections", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/player-account\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Torn API Key", home, StringComparison.Ordinal);
        Assert.DoesNotContain("_apiKey", home, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMyStatsImportAsync", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_keeps_the_3d_research_surface_secondary()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("The public 3D gym point cloud now lives in", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/gym-explorer\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("plotlyInterop", home, StringComparison.Ordinal);
        Assert.DoesNotContain("scatter3d", home, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void War_uses_shared_components_for_page_state_without_replacing_operational_alerts()
    {
        var war = ReadWarPresentation();

        Assert.Contains("<LoadingState Message=\"Loading current war state…\"", war, StringComparison.Ordinal);
        Assert.Contains("<ErrorState Message=\"War board unavailable. Refresh to retry.\"", war, StringComparison.Ordinal);
        Assert.Contains("<StaleDataBanner Message=\"Review heartbeat, warnings, and hub connection status before acting on roster gaps.\"", war, StringComparison.Ordinal);
        Assert.Contains("<EmptyState Message=\"No war in progress.", war, StringComparison.Ordinal);
        Assert.Contains("<EmptyState Message=\"No current hole alerts.\"", war, StringComparison.Ordinal);
        Assert.Contains("<ErrorState Message=\"The war board encountered a connection problem. Refresh to retry.\"", war, StringComparison.Ordinal);

        // Chain command alerts are operational instructions, not page lifecycle state.
        Assert.Contains("<strong>Chain about to lapse.</strong>", war, StringComparison.Ordinal);
        Assert.Contains("<strong>Reservation window.</strong>", war, StringComparison.Ordinal);
    }

    [Fact]
    public void War_failure_copy_does_not_render_service_failure_detail()
    {
        var war = ReadWarPresentation();
        var diagnostics = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarDiagnosticsPanel.razor");

        Assert.DoesNotContain("WarBoard.CurrentFailure.SafeMessage", war, StringComparison.Ordinal);
        // Passing ConnectionError into the diagnostics component is allowed so it can decide
        // whether a generic error state exists. The raw value itself must never be rendered.
        Assert.DoesNotContain("@ConnectionError", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_driven_MyStats_and_War_share_the_same_state_vocabulary()
    {
        var myStats = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");
        var war = ReadWarPresentation();

        Assert.Contains("<LoadingState", myStats, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", myStats, StringComparison.Ordinal);
        Assert.Contains("<LoadingState", war, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", war, StringComparison.Ordinal);
        Assert.Contains("<EmptyState", war, StringComparison.Ordinal);
        Assert.Contains("<StaleDataBanner", war, StringComparison.Ordinal);
    }

    private static string ReadWarPresentation() => string.Join(
        '\n',
        ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor"),
        ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarBoardStateBanners.razor"),
        ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarChainCommandPanel.razor"),
        ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarHoleAlerts.razor"),
        ReadRepoFile("src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/War/WarDiagnosticsPanel.razor"));

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
