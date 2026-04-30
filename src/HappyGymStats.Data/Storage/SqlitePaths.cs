namespace HappyGymStats.Storage;

public static class SqlitePaths
{
    public const string DatabaseOverrideEnvironmentVariable = "HAPPYGYMSTATS_DATABASE";

    public static string ResolveDatabasePath(string fallbackDataDirectory, string? explicitPath = null)
    {
        if (string.IsNullOrWhiteSpace(fallbackDataDirectory))
            throw new ArgumentException("Fallback data directory must be provided.", nameof(fallbackDataDirectory));

        var candidate = explicitPath;
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = Environment.GetEnvironmentVariable(DatabaseOverrideEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(candidate))
            return Path.GetFullPath(candidate);

        return Path.Combine(Path.GetFullPath(fallbackDataDirectory), "happygymstats.db");
    }

    public static bool ShouldPreferDatabase(string databasePath, params string[] legacyInputs)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return false;

        var dbInfo = new FileInfo(databasePath);
        if (dbInfo.Length <= 0)
            return false;

        var dbWriteUtc = dbInfo.LastWriteTimeUtc;

        foreach (var path in legacyInputs)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            var legacyWriteUtc = new FileInfo(path).LastWriteTimeUtc;
            if (legacyWriteUtc > dbWriteUtc)
                return false;
        }

        return true;
    }
}
