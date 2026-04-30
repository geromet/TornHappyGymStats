using HappyGymStats.Storage;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SqlitePathsTests
{
    [Fact]
    public void ResolveDatabasePath_uses_fallback_directory_when_no_override_is_set()
    {
        var originalDb = Environment.GetEnvironmentVariable(SqlitePaths.DatabaseOverrideEnvironmentVariable);
        var tempRoot = Path.Combine(Path.GetTempPath(), "happygymstats-sqlite-path-tests", Guid.NewGuid().ToString("N"));

        try
        {
            Environment.SetEnvironmentVariable(SqlitePaths.DatabaseOverrideEnvironmentVariable, null);

            var resolved = SqlitePaths.ResolveDatabasePath(tempRoot);

            Assert.Equal(Path.Combine(Path.GetFullPath(tempRoot), "happygymstats.db"), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SqlitePaths.DatabaseOverrideEnvironmentVariable, originalDb);
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ShouldPreferDatabase_returns_false_when_legacy_input_is_newer_than_database()
    {
        var tempRoot = CreateTempDirectory();
        var dbPath = Path.Combine(tempRoot, "happygymstats.db");
        var legacyPath = Path.Combine(tempRoot, "userlogs.jsonl");

        File.WriteAllText(dbPath, "db");
        File.WriteAllText(legacyPath, "legacy");

        File.SetLastWriteTimeUtc(dbPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(legacyPath, new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));

        Assert.False(SqlitePaths.ShouldPreferDatabase(dbPath, legacyPath));
    }

    [Fact]
    public void ShouldPreferDatabase_returns_true_when_database_is_present_and_not_older_than_legacy_inputs()
    {
        var tempRoot = CreateTempDirectory();
        var dbPath = Path.Combine(tempRoot, "happygymstats.db");
        var legacyPath = Path.Combine(tempRoot, "userlogs.jsonl");

        File.WriteAllText(dbPath, "db");
        File.WriteAllText(legacyPath, "legacy");

        File.SetLastWriteTimeUtc(legacyPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(dbPath, new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));

        Assert.True(SqlitePaths.ShouldPreferDatabase(dbPath, legacyPath));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "happygymstats-sqlite-path-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
