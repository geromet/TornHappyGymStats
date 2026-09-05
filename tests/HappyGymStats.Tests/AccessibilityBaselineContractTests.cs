using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class AccessibilityBaselineContractTests
{
    private const string AppCssPath = "src/HappyGymStats.Blazor/HappyGymStats.Blazor/wwwroot/app.css";

    [Fact]
    public void Global_styles_keep_keyboard_focus_visibly_distinct()
    {
        var css = ReadRepoFile(AppCssPath);

        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("a[href]", css, StringComparison.Ordinal);
        Assert.Contains("button", css, StringComparison.Ordinal);
        Assert.Contains("input", css, StringComparison.Ordinal);
        Assert.Contains("[tabindex]:not([tabindex=\"-1\"])", css, StringComparison.Ordinal);
        Assert.Contains("outline: 3px solid", css, StringComparison.Ordinal);
        Assert.Contains("outline-offset: 3px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_styles_respect_reduced_motion_preference()
    {
        var css = ReadRepoFile(AppCssPath);

        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("animation-duration: 0.01ms !important", css, StringComparison.Ordinal);
        Assert.Contains("animation-iteration-count: 1 !important", css, StringComparison.Ordinal);
        Assert.Contains("transition-duration: 0.01ms !important", css, StringComparison.Ordinal);
        Assert.Contains("transition-delay: 0ms !important", css, StringComparison.Ordinal);
        Assert.Contains("scroll-behavior: auto !important", css, StringComparison.Ordinal);
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
