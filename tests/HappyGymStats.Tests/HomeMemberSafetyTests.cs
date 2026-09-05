using System;
using System.IO;

namespace HappyGymStats.Tests;

public sealed class HomeMemberSafetyTests
{
    [Fact]
    public void Home_does_not_render_implementation_or_raw_error_detail_to_members()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("Could not load training data. Please try again.", content, StringComparison.Ordinal);
        Assert.Contains("Import failed. Please try again.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("status.ErrorMessage", content, StringComparison.Ordinal);
        Assert.DoesNotContain("backend rejected", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API response format", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure.SafeMessage", content, StringComparison.Ordinal);
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
