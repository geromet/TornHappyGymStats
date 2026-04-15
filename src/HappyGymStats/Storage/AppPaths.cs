namespace HappyGymStats.Storage;

public sealed record AppPaths(
    string DataDirectory,
    string QuarantineDirectory,
    string CheckpointPath,
    string LogsJsonlPath)
{
    public string DerivedDirectory => Path.Combine(DataDirectory, "derived");

    // Derived sidecar file for gym-train reconstruction output.
    public string DerivedGymTrainsJsonlPath => Path.Combine(DerivedDirectory, "derived-gymtrains.jsonl");

    // Derived sidecar file containing per-event happy timeline (includes synthetic regen tick events).
    public string DerivedHappyEventsJsonlPath => Path.Combine(DerivedDirectory, "derived-happy-events.jsonl");

    public string ExportDirectory => Path.Combine(DataDirectory, "export");

    public string LogsCsvPath => Path.Combine(ExportDirectory, "userlogs.csv");

    public string LogsDebugCsvPath => Path.Combine(ExportDirectory, "userlogs.debug.csv");

    public string HappyTimelineCsvPath => Path.Combine(ExportDirectory, "happy-timeline.csv");

    public static AppPaths Default(string appName = "HappyGymStats")
    {
        var dataDir = HappyGymStats.Storage.DataDirectory.ResolveBasePath(appName);
        var quarantineDir = Path.Combine(dataDir, "quarantine");

        return new AppPaths(
            DataDirectory: dataDir,
            QuarantineDirectory: quarantineDir,
            CheckpointPath: Path.Combine(dataDir, "checkpoint.json"),
            LogsJsonlPath: Path.Combine(dataDir, "userlogs.jsonl"));
    }
}
