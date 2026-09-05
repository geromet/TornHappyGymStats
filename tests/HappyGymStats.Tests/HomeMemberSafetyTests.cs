using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class HomeMemberSafetyTests
{
    [Fact]
    public void Home_does_not_collect_raw_credentials_or_render_the_point_cloud()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("Train smarter. Scout faster.", content, StringComparison.Ordinal);
        Assert.Contains("Gym Explorer", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Torn API Key", content, StringComparison.Ordinal);
        Assert.DoesNotContain("StartImportAsync", content, StringComparison.Ordinal);
        Assert.DoesNotContain("gym-cloud-chart", content, StringComparison.Ordinal);
        Assert.DoesNotContain("plotlyInterop", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Gym_explorer_owns_the_public_point_cloud_without_collecting_an_api_key()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/GymExplorer.razor");

        Assert.Contains("@page \"/gym-explorer\"", content, StringComparison.Ordinal);
        Assert.Contains("gym-cloud-chart", content, StringComparison.Ordinal);
        Assert.Contains("plotlyInterop.render", content, StringComparison.Ordinal);
        Assert.Contains("does not claim an optimal training window", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Torn API Key", content, StringComparison.Ordinal);
        Assert.DoesNotContain("StartImportAsync", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Plotly_is_loaded_on_demand_instead_of_from_the_app_shell()
    {
        var app = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/App.razor");
        var interop = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/wwwroot/plotly-interop.js");

        Assert.DoesNotContain("cdn.plot.ly/plotly", app, StringComparison.Ordinal);
        Assert.Contains("ensureLoaded", interop, StringComparison.Ordinal);
        Assert.Contains("cdn.plot.ly/plotly-2.27.1.min.js", interop, StringComparison.Ordinal);
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
