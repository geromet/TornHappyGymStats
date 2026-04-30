using HappyGymStats.Storage;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class DataDirectoryTests
{
    [Fact]
    public void ResolveBasePath_prefers_explicit_environment_override_when_set()
    {
        var original = Environment.GetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable);
        var tempRoot = Path.Combine(Path.GetTempPath(), "happygymstats-data-dir-tests", Guid.NewGuid().ToString("N"));
        var overridePath = Path.Combine(tempRoot, "repo-data");

        try
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, overridePath);

            var resolved = DataDirectory.ResolveBasePath("HappyGymStatsTest");

            Assert.Equal(Path.GetFullPath(overridePath), resolved);
            Assert.True(Directory.Exists(overridePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, original);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveBasePath_preserves_app_base_directory_default_when_override_is_unset()
    {
        var original = Environment.GetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, null);

            var resolved = DataDirectory.ResolveBasePath("HappyGymStatsTest");
            var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));

            Assert.Equal(expected, resolved);
            Assert.True(Directory.Exists(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, original);
        }
    }
}
