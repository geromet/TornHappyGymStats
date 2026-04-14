using HappyGymStats.Export;
using HappyGymStats.Reconstruction;
using HappyGymStats.Storage;

namespace HappyGymStats.Tests.Integration;

/// <summary>
/// End-to-end integration test exercising the full pipeline:
/// fixture JSONL → ReconstructionRunner → derived sidecar → CsvExportRunner → CSV output.
/// </summary>
public sealed class E2ePipelineTests
{
    /// <summary>
    /// Full pipeline: copy fixture → reconstruct → export CSV → verify output.
    /// </summary>
    [Fact]
    public void FullPipeline_FromFixtures_ProducesExpectedCsv()
    {
        var tempDir = CreateTempDir();
        try
        {
            // --- Arrange: set up paths and copy fixture ---
            var paths = BuildAppPaths(tempDir);
            Directory.CreateDirectory(paths.DataDirectory);

            var fixtureSource = FixturePath("userlogs-small.jsonl");
            File.Copy(fixtureSource, paths.LogsJsonlPath, overwrite: true);

            // --- Act 1: Run reconstruction ---
            var runner = new ReconstructionRunner(paths);
            var reconResult = runner.Run(
                currentHappy: 1000,
                anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000));

            // --- Assert reconstruction succeeded ---
            Assert.True(reconResult.Success, $"Reconstruction failed: {reconResult.ErrorMessage}");
            Assert.True(File.Exists(paths.DerivedGymTrainsJsonlPath),
                "Derived sidecar file should exist after reconstruction.");

            var derivedLines = File.ReadAllLines(paths.DerivedGymTrainsJsonlPath);
            Assert.NotEmpty(derivedLines);
            // The fixture has 1 gym-train event (id=1001) and 1 malformed line.
            Assert.Single(derivedLines);
            Assert.NotNull(reconResult.Stats);
            Assert.Equal(1, reconResult.Stats.GymTrainsDerived);

            // --- Act 2: Run CSV export from raw JSONL + derived sidecar ---
            var csvPath = paths.LogsCsvPath;
            var exportResult = CsvExportRunner.Run(
                paths.LogsJsonlPath,
                csvPath,
                derivedJsonlPath: paths.DerivedGymTrainsJsonlPath);

            // --- Assert export succeeded ---
            Assert.True(exportResult.Success, $"CSV export failed: {exportResult.ErrorMessage}");
            Assert.True(File.Exists(csvPath), "CSV output file should exist.");

            // --- Verify CSV content ---
            var csvLines = File.ReadAllLines(csvPath);

            // Header + at least 1 data row (fixture has 3 valid + 1 malformed that gets skipped).
            // Valid records: id=1001 (gym), id=1002 (misc), id=1003 (travel) → 3 data rows.
            Assert.True(csvLines.Length >= 2, "CSV should have a header row and at least one data row.");
            Assert.Equal(4, csvLines.Length); // 1 header + 3 data rows

            // Parse header
            var headers = CsvSplit(csvLines[0]);

            // Header must include canonical columns
            Assert.Contains("id", headers);
            Assert.Contains("timestamp", headers);

            // Header must include all derived columns
            Assert.Contains("happy_before_train", headers);
            Assert.Contains("happy_after_train", headers);
            Assert.Contains("regen_ticks_applied", headers);
            Assert.Contains("regen_happy_gained", headers);
            Assert.Contains("max_happy_at_time_utc", headers);
            Assert.Contains("clamped_to_max", headers);

            // Verify the row count matches export result
            Assert.Equal(3, exportResult.RowsWritten);

            // Find the row for id=1001 and verify derived columns are populated
            var idIdx = Array.IndexOf(headers, "id");
            var happyBeforeIdx = Array.IndexOf(headers, "happy_before_train");
            var clampedIdx = Array.IndexOf(headers, "clamped_to_max");

            Assert.True(idIdx >= 0, "id column must exist in header");
            Assert.True(happyBeforeIdx >= 0, "happy_before_train column must exist in header");
            Assert.True(clampedIdx >= 0, "clamped_to_max column must exist in header");

            string? row1001 = null;
            foreach (var line in csvLines.Skip(1))
            {
                var fields = CsvSplit(line);
                if (fields.Length > idIdx && fields[idIdx] == "1001")
                {
                    row1001 = line;
                    break;
                }
            }

            Assert.NotNull(row1001);
            var rowFields = CsvSplit(row1001);
            // id=1001 used happy_used=25, currentHappy=1000 at anchor, reconstruction should compute values
            Assert.NotEqual(string.Empty, rowFields[happyBeforeIdx]);
            Assert.True(rowFields[clampedIdx] == "true" || rowFields[clampedIdx] == "false",
                $"clamped_to_max should be 'true' or 'false', got '{rowFields[clampedIdx]}'");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Pipeline with no gym-train events produces empty derived sidecar and CSV with blank derived columns.
    /// </summary>
    [Fact]
    public void FullPipeline_NoGymTrains_ProducesCsvWithBlankDerivedColumns()
    {
        var tempDir = CreateTempDir();
        try
        {
            var paths = BuildAppPaths(tempDir);
            Directory.CreateDirectory(paths.DataDirectory);

            // Write a JSONL with only non-gym events
            File.WriteAllText(paths.LogsJsonlPath,
                "{\"id\":\"2001\",\"timestamp\":1700000000,\"details\":{\"id\":2322,\"title\":\"Travel\",\"category\":\"Travel\"},\"data\":{\"destination\":\"Mexico\"},\"params\":{\"color\":\"blue\"}}\n");

            var runner = new ReconstructionRunner(paths);
            var reconResult = runner.Run(
                currentHappy: 100,
                anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000));

            Assert.True(reconResult.Success, reconResult.ErrorMessage);
            Assert.Equal(0, reconResult.Stats!.GymTrainsDerived);

            var csvPath = paths.LogsCsvPath;
            var exportResult = CsvExportRunner.Run(
                paths.LogsJsonlPath,
                csvPath,
                derivedJsonlPath: paths.DerivedGymTrainsJsonlPath);

            Assert.True(exportResult.Success, $"Export failed: {exportResult.ErrorMessage}");

            var csvLines = File.ReadAllLines(csvPath);
            Assert.Equal(2, csvLines.Length); // header + 1 data row

            var headers = CsvSplit(csvLines[0]);
            Assert.Contains("happy_before_train", headers);
            Assert.Contains("clamped_to_max", headers);

            // The single data row should have blank derived columns
            var dataFields = CsvSplit(csvLines[1]);
            var happyBeforeIdx = Array.IndexOf(headers, "happy_before_train");
            Assert.Equal(string.Empty, dataFields[happyBeforeIdx]);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Reconstruction failure (missing input file) propagates correctly and export is not attempted.
    /// </summary>
    [Fact]
    public void FullPipeline_MissingInput_ReconstructionFails()
    {
        var tempDir = CreateTempDir();
        try
        {
            var paths = BuildAppPaths(tempDir);
            Directory.CreateDirectory(paths.DataDirectory);

            // Do NOT copy fixture — input file does not exist.
            var runner = new ReconstructionRunner(paths);
            var reconResult = runner.Run(
                currentHappy: 100,
                anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000));

            Assert.False(reconResult.Success);
            Assert.NotNull(reconResult.ErrorMessage);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // --- Helpers ---

    private static AppPaths BuildAppPaths(string tempDir)
    {
        return new AppPaths(
            DataDirectory: Path.Combine(tempDir, "data"),
            QuarantineDirectory: Path.Combine(tempDir, "quarantine"),
            CheckpointPath: Path.Combine(tempDir, "data", "checkpoint.json"),
            LogsJsonlPath: Path.Combine(tempDir, "data", "userlogs.jsonl"));
    }

    private static string CreateTempDir()
        => Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests.E2e", Guid.NewGuid().ToString("N"));

    private static string FixturePath(string fileName)
    {
        var root = FindRepoRoot();
        return Path.Combine(root, "tests", "HappyGymStats.Tests", "Fixtures", fileName);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root (HappyGymStats.sln not found).");
    }

    private static void Cleanup(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Simple CSV-aware splitter: respects double-quoted fields containing commas.
    /// </summary>
    private static string[] CsvSplit(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }
}
