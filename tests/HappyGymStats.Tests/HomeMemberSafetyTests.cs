using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class HomeMemberSafetyTests
{
    [Fact]
    public void Home_is_product_navigation_without_raw_credentials_or_point_cloud()
    {
        var home = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("Train smarter. Scout faster.", home, StringComparison.Ordinal);
        Assert.Contains("Live War", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/my-stats\"", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/gym-explorer\"", home, StringComparison.Ordinal);
        Assert.Contains("Account &amp; connections", home, StringComparison.Ordinal);
        Assert.DoesNotContain("War board", home, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Torn API Key", home, StringComparison.Ordinal);
        Assert.DoesNotContain("_apiKey", home, StringComparison.Ordinal);
        Assert.DoesNotContain("StartImportAsync", home, StringComparison.Ordinal);
        Assert.DoesNotContain("gym-cloud-chart", home, StringComparison.Ordinal);
        Assert.DoesNotContain("plotlyInterop", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Gym_explorer_owns_public_research_surface_without_collecting_credentials()
    {
        var explorer = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/GymExplorer.razor");

        Assert.Contains("@page \"/gym-explorer\"", explorer, StringComparison.Ordinal);
        Assert.Contains("gym-cloud-chart", explorer, StringComparison.Ordinal);
        Assert.Contains("plotlyInterop.render", explorer, StringComparison.Ordinal);
        Assert.Contains("does not claim an optimal training window", explorer, StringComparison.Ordinal);
        Assert.DoesNotContain("Torn API Key", explorer, StringComparison.Ordinal);
    }

    [Fact]
    public void Plotly_is_loaded_on_demand_instead_of_from_app_shell()
    {
        var app = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/App.razor");
        var interop = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/wwwroot/plotly-interop.js");

        Assert.DoesNotContain("cdn.plot.ly", app, StringComparison.Ordinal);
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
