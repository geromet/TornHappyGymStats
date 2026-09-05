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
    public void MyStats_and_Home_share_the_same_loading_and_failure_vocabulary()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");
        var myStats = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");

        Assert.Contains("<LoadingState", home, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", home, StringComparison.Ordinal);
        Assert.Contains("<LoadingState", myStats, StringComparison.Ordinal);
        Assert.Contains("<ErrorState", myStats, StringComparison.Ordinal);
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
