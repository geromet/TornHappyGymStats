using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class AppShellContractTests
{
    private const string LayoutPath = "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor";
    private const string LayoutCssPath = "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor.css";
    private const string WeatherPath = "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Weather.razor";

    [Fact]
    public void Desktop_shell_groups_navigation_by_player_task()
    {
        var layout = ReadRepoFile(LayoutPath);

        Assert.Contains(">Command<", layout, StringComparison.Ordinal);
        Assert.Contains(">Intelligence<", layout, StringComparison.Ordinal);
        Assert.Contains(">Planning<", layout, StringComparison.Ordinal);
        Assert.Contains(">Training<", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/war\"", layout, StringComparison.Ordinal);
        Assert.Contains(">Live War<", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/war/scout\"", layout, StringComparison.Ordinal);
        Assert.Contains(">Opponent Scout<", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/chain-calculator\"", layout, StringComparison.Ordinal);
        Assert.Contains(">Chain Planner<", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/my-stats\"", layout, StringComparison.Ordinal);
        Assert.Contains(">My Training<", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/gym-explorer\"", layout, StringComparison.Ordinal);
        Assert.Contains(">Gym Explorer<", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_gym_explorer_stays_outside_the_private_training_gate()
    {
        var layout = ReadRepoFile(LayoutPath);
        var trainingStart = layout.IndexOf(">Training<", StringComparison.Ordinal);
        var trainingEnd = layout.IndexOf("</div>", trainingStart, StringComparison.Ordinal);
        var trainingNavigation = layout[trainingStart..trainingEnd];
        var privateTraining = trainingNavigation.IndexOf("Href=\"/my-stats\"", StringComparison.Ordinal);
        var authClose = trainingNavigation.IndexOf("</AuthorizeView>", privateTraining, StringComparison.Ordinal);
        var publicExplorer = trainingNavigation.IndexOf("Href=\"/gym-explorer\"", StringComparison.Ordinal);

        Assert.True(trainingStart >= 0);
        Assert.True(trainingEnd > trainingStart);
        Assert.True(privateTraining >= 0);
        Assert.True(authClose > privateTraining);
        Assert.True(publicExplorer > authClose);

        var mobileStart = layout.IndexOf("class=\"mobile-navigation\"", StringComparison.Ordinal);
        var mobileEnd = layout.IndexOf("</nav>", mobileStart, StringComparison.Ordinal);
        var mobileNavigation = layout[mobileStart..mobileEnd];
        var mobilePrivateTraining = mobileNavigation.IndexOf("Href=\"/my-stats\"", StringComparison.Ordinal);
        var mobileAuthClose = mobileNavigation.IndexOf("</AuthorizeView>", mobilePrivateTraining, StringComparison.Ordinal);
        var mobilePublicExplorer = mobileNavigation.IndexOf("Href=\"/gym-explorer\"", StringComparison.Ordinal);

        Assert.True(mobilePrivateTraining >= 0);
        Assert.True(mobileAuthClose > mobilePrivateTraining);
        Assert.True(mobilePublicExplorer > mobileAuthClose);
    }

    [Fact]
    public void Account_and_legal_routes_are_secondary_not_primary_debug_navigation()
    {
        var layout = ReadRepoFile(LayoutPath);

        Assert.Contains("class=\"rail-secondary\"", layout, StringComparison.Ordinal);
        Assert.Contains("Account &amp; Connections", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/privacy\"", layout, StringComparison.Ordinal);
        Assert.Contains("Href=\"/terms\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("Href=\"/login\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("Href=\"/settings\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("https://www.torn.com", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("Icons.Material.Filled.Lock", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Mobile_navigation_has_a_separate_task_priority_order()
    {
        var layout = ReadRepoFile(LayoutPath);
        var mobileStart = layout.IndexOf("class=\"mobile-navigation\"", StringComparison.Ordinal);
        var mobileEnd = layout.IndexOf("</nav>", mobileStart, StringComparison.Ordinal);
        var mobileNavigation = layout[mobileStart..mobileEnd];

        Assert.True(mobileStart >= 0);
        Assert.True(mobileEnd > mobileStart);
        Assert.True(mobileNavigation.IndexOf(">Live War<", StringComparison.Ordinal)
                    < mobileNavigation.IndexOf(">Opponent Scout<", StringComparison.Ordinal));
        Assert.True(mobileNavigation.IndexOf(">Opponent Scout<", StringComparison.Ordinal)
                    < mobileNavigation.IndexOf(">Chain Planner<", StringComparison.Ordinal));
        Assert.True(mobileNavigation.IndexOf(">Chain Planner<", StringComparison.Ordinal)
                    < mobileNavigation.IndexOf(">My Training<", StringComparison.Ordinal));
        Assert.True(mobileNavigation.IndexOf(">My Training<", StringComparison.Ordinal)
                    < mobileNavigation.IndexOf(">Overview<", StringComparison.Ordinal));
    }

    [Fact]
    public void Responsive_shell_keeps_active_route_visible_without_color_only_state()
    {
        var layout = ReadRepoFile(LayoutPath);
        var css = ReadRepoFile(LayoutCssPath);

        Assert.Contains("desktop-rail--collapsed", layout, StringComparison.Ordinal);
        Assert.Contains("DrawerVariant.Temporary", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Mobile primary navigation\"", layout, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 959.98px)", css, StringComparison.Ordinal);
        Assert.Contains(".mud-nav-link.active", css, StringComparison.Ordinal);
        Assert.Contains("border-left-color: var(--mud-palette-primary)", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: 700", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Removed_weather_demo_surface_stays_removed()
    {
        var layout = ReadRepoFile(LayoutPath);

        Assert.DoesNotContain("/weather", layout, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(GetRepoPath(WeatherPath)), "Weather demo route must not return to the production shell.");
    }

    private static string ReadRepoFile(string relativePath) => File.ReadAllText(GetRepoPath(relativePath));

    private static string GetRepoPath(string relativePath)
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

        return Path.Combine(directory.FullName, relativePath);
    }
}
