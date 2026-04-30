using HappyGymStats.Storage;

namespace HappyGymStats.Storage;

public static class SqlitePaths
{
    public const string DatabaseOverrideEnvironmentVariable = "HAPPYGYMSTATS_DATABASE";

    public static string ResolveDatabasePath(AppPaths paths, string? explicitPath = null)
    {
        if (paths is null)
            throw new ArgumentNullException(nameof(paths));

        var candidate = explicitPath;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = Environment.GetEnvironmentVariable(DatabaseOverrideEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(candidate))
            return Path.GetFullPath(candidate);

        return Path.Combine(paths.DataDirectory, "happygymstats.db");
    }
}
