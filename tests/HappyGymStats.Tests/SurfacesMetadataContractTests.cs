using System;
using System.IO;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SurfacesMetadataContractTests
{
    [Fact]
    public void Caller_scoped_surface_projects_metadata_from_the_same_owner_filtered_series()
    {
        var controller = ReadRepoFile(
            "src/HappyGymStats.Api/Controllers/SurfacesController.cs");

        Assert.Contains("GetGymLogEntriesAsync(callerAnonymousId, ct)", controller, StringComparison.Ordinal);
        Assert.Contains("text = surfaces.GymText", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_surface_sanitizer_still_removes_point_metadata()
    {
        var controller = ReadRepoFile(
            "src/HappyGymStats.Api/Controllers/SurfacesController.cs");

        Assert.Contains("gymCloud.Remove(\"text\")", controller, StringComparison.Ordinal);
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
