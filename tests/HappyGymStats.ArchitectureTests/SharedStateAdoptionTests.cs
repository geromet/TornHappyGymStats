using System;
using System.IO;

namespace HappyGymStats.ArchitectureTests;

public sealed class SharedStateAdoptionTests
{
    [Fact]
    public void Home_uses_shared_components_for_loading_empty_and_failed_data_states()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("<LoadingState Message=\"Loading surfaces data…\" />", home, StringComparison.Ordinal);
        Assert.Contains("<ErrorState Message=\"@_loadError\" OnRetry=\"LoadSurfacesAsync\" />", home, StringComparison.Ordinal);
        Assert.Contains("<EmptyState Message=\"No surfaces data found. Run an import first.\" />", home, StringComparison.Ordinal);

        Assert.DoesNotContain("MudProgressCircular", home, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudAlert Severity=\"Severity.Warning\" Class=\"mb-4\">@_loadError</MudAlert>", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Home_treats_missing_dataset_as_empty_instead_of_failed()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains(
            "catch (ApiFailure failure) when (failure.Category == ApiFailureCategory.NotFound)",
            home,
            StringComparison.Ordinal);
        Assert.Contains("_surfaces = null;", home, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_loadError = \"No surfaces data found. Run an import first.\";",
            home,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Home_keeps_transient_import_feedback_separate_from_page_state_components()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("@if (!string.IsNullOrEmpty(_statusMessage))", home, StringComparison.Ordinal);
        Assert.Contains("<MudAlert Severity=\"@_statusSeverity\"", home, StringComparison.Ordinal);
        Assert.Contains("_statusMessage = \"Import failed. Please try again.\";", home, StringComparison.Ordinal);
    }

    [Fact]
    public void War_uses_shared_components_for_page_state_without_replacing_operational_alerts()
    {
        var war = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor");

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
        var war = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor");

        Assert.DoesNotContain("WarBoard.CurrentFailure.SafeMessage", war, StringComparison.Ordinal);
        Assert.DoesNotContain("WarBoard.ConnectionError", ExtractRenderedConnectionFailureBlock(war), StringComparison.Ordinal);
    }

    [Fact]
    public void Home_MyStats_and_War_share_the_same_state_vocabulary()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");
        var myStats = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");
        var war = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/War.razor");

        Assert.Contains("<LoadingState", home, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", home, StringComparison.Ordinal);
        Assert.Contains("<LoadingState", myStats, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", myStats, StringComparison.Ordinal);
        Assert.Contains("<LoadingState", war, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", war, StringComparison.Ordinal);
        Assert.Contains("<EmptyState", war, StringComparison.Ordinal);
        Assert.Contains("<StaleDataBanner", war, StringComparison.Ordinal);
    }

    private static string ExtractRenderedConnectionFailureBlock(string war)
    {
        const string marker = "The war board encountered a connection problem. Refresh to retry.";
        var markerIndex = war.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, "Expected bounded connection-failure copy was not found.");

        var start = Math.Max(0, markerIndex - 200);
        var length = Math.Min(war.Length - start, 400);
        return war.Substring(start, length);
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
