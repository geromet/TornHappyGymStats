using System.Globalization;
using HappyGymStats.Export;
using HappyGymStats.Reconstruction;
using HappyGymStats.Storage;
using HappyGymStats.Verification;
using HappyGymStats.Visualizer;
using Microsoft.FSharp.Collections;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ExportedDatasetConsistencyTests
{
    private static readonly DatasetPaths Paths = DatasetPaths.Discover();

    private static readonly string[] KnownMaxHappyRegressionIds =
    {
        "1hG4UEPP4u65xsvmyVqO",
        "sYzEXeNqsx830B9RDaz6",
        "xYb46POoCDV3VGL9EQEi",
        "4SbkaCWhaHyxwJ4co7lW",
        "FijoBMBKCWBL1lCREWDq",
        "1d02tEE5mAm4Qo7CBrgs",
        "lBgYRhYBLHmAI6OCAbXW",
        "buO12TzeF4K1FlJI41PB",
        "OZFOkzCPDSSBAo6YV3HA",
        "nls7uxjv7xzSEDyaINMQ",
        "79C2lVzkDWvRi0pCA6bE",
        "EcATSWOoMERZeVgVWJXO",
        "8f9V4ECP9gjcz0zS5CCF",
        "aHnX0QShub1Z8KcYVHro",
    };

    [Fact]
    public void Derived_jsonl_matches_reconstruction_from_userlogs()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();

        var reconstructed = Reconstruct();

        var expectedTrains = DerivedGymTrainReader.Read(Paths.DerivedGymTrainsJsonlPath);
        Assert.Null(expectedTrains.ErrorMessage);
        Assert.False(expectedTrains.FileMissing);

        var expectedEvents = DerivedHappyEventReader.Read(Paths.DerivedHappyEventsJsonlPath);
        Assert.Null(expectedEvents.ErrorMessage);
        Assert.False(expectedEvents.FileMissing);

        Assert.Equal(
            OrderTrains(expectedTrains.Records.Values),
            OrderTrains(reconstructed.DerivedGymTrains));

        Assert.Equal(
            OrderEvents(expectedEvents.AllEvents),
            OrderEvents(reconstructed.DerivedHappyEvents));
    }

    [Fact]
    public void Userlogs_csv_matches_a_fresh_export()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();
        var tempOutput = Path.Combine(CreateTempDirectory(), "userlogs.csv");

        var result = CsvExportRunner.Run(
            Paths.UserLogsJsonlPath,
            tempOutput,
            Paths.DerivedGymTrainsJsonlPath);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(File.ReadAllText(Paths.UserLogsCsvPath), File.ReadAllText(tempOutput));
    }

    [Fact]
    public void Debug_csv_matches_a_fresh_export()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();
        var tempOutput = Path.Combine(CreateTempDirectory(), "userlogs.debug.csv");

        var result = CsvExportRunner.RunDebug(
            Paths.UserLogsJsonlPath,
            tempOutput,
            Paths.DerivedGymTrainsJsonlPath,
            Paths.DerivedHappyEventsJsonlPath);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(File.ReadAllText(Paths.UserLogsDebugCsvPath), File.ReadAllText(tempOutput));
    }

    [Fact]
    public void Happy_timeline_csv_matches_a_fresh_export()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();
        var tempOutput = Path.Combine(CreateTempDirectory(), "happy-timeline.csv");

        var result = HappyTimelineCsvWriter.Write(
            Paths.DerivedHappyEventsJsonlPath,
            tempOutput);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(File.ReadAllText(Paths.HappyTimelineCsvPath), File.ReadAllText(tempOutput));
    }

    [Fact]
    public void Surfaces_html_point_cloud_matches_csv_and_jsonl()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();

        var report = ExportVerifier.Verify(new ExportVerifier.VerifyOptions(
            CsvPath: Paths.UserLogsCsvPath,
            LogsJsonlPath: Paths.UserLogsJsonlPath,
            DerivedGymTrainsJsonlPath: Paths.DerivedGymTrainsJsonlPath,
            SurfacesHtmlPath: Paths.SurfacesHtmlPath,
            TopOutliers: 0));

        Assert.True(report.VisualizationHtmlMatchesCsv);
        Assert.Equal(0, report.Mismatches);
    }

    [Fact]
    public void Fresh_surfaces_html_point_cloud_matches_csv_and_jsonl()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();
        var tempDir = CreateTempDirectory();

        var parse = CsvStatReader.readStatRecords(Paths.UserLogsCsvPath);
        var generated = SurfacePlotter.generateStackedPlots(ListModule.OfSeq(parse.Records), tempDir).ToArray();

        Assert.Single(generated);

        var report = ExportVerifier.Verify(new ExportVerifier.VerifyOptions(
            CsvPath: Paths.UserLogsCsvPath,
            LogsJsonlPath: Paths.UserLogsJsonlPath,
            DerivedGymTrainsJsonlPath: Paths.DerivedGymTrainsJsonlPath,
            SurfacesHtmlPath: generated[0],
            TopOutliers: 0));

        Assert.True(report.VisualizationHtmlMatchesCsv);
        Assert.Equal(0, report.Mismatches);
    }

    [Fact]
    public async Task Legacy_dataset_can_be_migrated_to_sqlite_and_reexported_without_drift()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();
        var tempDir = CreateTempDirectory();
        var dbPath = Path.Combine(tempDir, "happygymstats.db");
        var csvOutput = Path.Combine(tempDir, "userlogs.csv");
        var debugOutput = Path.Combine(tempDir, "userlogs.debug.csv");
        var timelineOutput = Path.Combine(tempDir, "happy-timeline.csv");

        var appPaths = new AppPaths(
            DataDirectory: Path.GetDirectoryName(Paths.UserLogsJsonlPath)!,
            QuarantineDirectory: Path.Combine(Path.GetDirectoryName(Paths.UserLogsJsonlPath)!, "quarantine"),
            CheckpointPath: Path.Combine(Path.GetDirectoryName(Paths.UserLogsJsonlPath)!, "checkpoint.json"),
            LogsJsonlPath: Paths.UserLogsJsonlPath);

        var migrate = await LegacySqliteMigrator.RunAsync(appPaths, dbPath, CancellationToken.None);
        Assert.True(migrate.Success, migrate.ErrorMessage);

        var csv = await DbCsvExportRunner.RunAsync(dbPath, csvOutput, CancellationToken.None);
        Assert.True(csv.Success, csv.ErrorMessage);
        Assert.Equal(File.ReadAllText(Paths.UserLogsCsvPath), File.ReadAllText(csvOutput));

        var debug = await DbCsvExportRunner.RunDebugAsync(dbPath, debugOutput, CancellationToken.None);
        Assert.True(debug.Success, debug.ErrorMessage);
        Assert.Equal(File.ReadAllText(Paths.UserLogsDebugCsvPath), File.ReadAllText(debugOutput));

        var timeline = await DbHappyTimelineCsvWriter.WriteAsync(dbPath, timelineOutput, CancellationToken.None);
        Assert.True(timeline.Success, timeline.ErrorMessage);
        Assert.Equal(File.ReadAllText(Paths.HappyTimelineCsvPath), File.ReadAllText(timelineOutput));
    }

    [Fact]
    public void Max_happy_events_use_actual_happy_delta()
    {
        using var _ = Paths.UseRepoRootAsCurrentDirectory();

        var reconstructed = Reconstruct();
        var eventsBySourceId = reconstructed.DerivedHappyEvents
            .Where(e => e.SourceLogId is not null)
            .ToDictionary(e => e.SourceLogId!, StringComparer.Ordinal);

        foreach (var id in KnownMaxHappyRegressionIds)
        {
            Assert.True(eventsBySourceId.TryGetValue(id, out var ev), $"Missing reconstructed event for {id}");
            Assert.Equal("max_happy", ev.EventType);
            Assert.Equal(ExpectedDelta(ev), ev.Delta);
        }

        foreach (var ev in reconstructed.DerivedHappyEvents.Where(e => e.EventType == "max_happy"))
            Assert.Equal(ExpectedDelta(ev), ev.Delta);
    }

    private static HappyTimelineReconstructor.ForwardResult Reconstruct()
    {
        var read = JsonlLogReader.Read(Paths.UserLogsJsonlPath);
        Assert.True(read.Success, read.ErrorMessage);

        var extract = LogEventExtractor.Extract(read.Records.Select(record => new ReconstructionLogRecord(
            LogId: record.LogId,
            OccurredAtUtc: record.OccurredAtUtc,
            Title: record.Title,
            Category: record.Category,
            RawJson: record.RawJson)));
        var events = extract.Events.ToArray();

        return HappyTimelineReconstructor.RunForward(events);
    }

    private static int? ExpectedDelta(DerivedHappyEvent ev)
        => ev.HappyBeforeEvent is not null && ev.HappyAfterEvent is not null
            ? ev.HappyAfterEvent.Value - ev.HappyBeforeEvent.Value
            : null;

    private static DerivedGymTrain[] OrderTrains(IEnumerable<DerivedGymTrain> trains)
        => trains
            .OrderBy(t => t.OccurredAtUtc)
            .ThenBy(t => t.LogId, StringComparer.Ordinal)
            .ToArray();

    private static DerivedHappyEvent[] OrderEvents(IEnumerable<DerivedHappyEvent> events)
        => events
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.EventType, StringComparer.Ordinal)
            .ThenBy(e => e.EventId, StringComparer.Ordinal)
            .ToArray();

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "happygymstats-tests", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record DatasetPaths(
        string RepoRoot,
        string UserLogsJsonlPath,
        string DerivedGymTrainsJsonlPath,
        string DerivedHappyEventsJsonlPath,
        string UserLogsCsvPath,
        string UserLogsDebugCsvPath,
        string HappyTimelineCsvPath,
        string SurfacesHtmlPath)
    {
        public static DatasetPaths Discover()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
                dir = dir.Parent;

            if (dir is null)
                throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");

            var dataRoot = Path.Combine(dir.FullName, "src", "HappyGymStats.Cli", "bin", "Debug", "net8.0", "data");
            return new DatasetPaths(
                RepoRoot: dir.FullName,
                UserLogsJsonlPath: Path.Combine(dataRoot, "userlogs.jsonl"),
                DerivedGymTrainsJsonlPath: Path.Combine(dataRoot, "derived", "derived-gymtrains.jsonl"),
                DerivedHappyEventsJsonlPath: Path.Combine(dataRoot, "derived", "derived-happy-events.jsonl"),
                UserLogsCsvPath: Path.Combine(dataRoot, "export", "userlogs.csv"),
                UserLogsDebugCsvPath: Path.Combine(dataRoot, "export", "userlogs.debug.csv"),
                HappyTimelineCsvPath: Path.Combine(dataRoot, "export", "happy-timeline.csv"),
                SurfacesHtmlPath: Path.Combine(dataRoot, "export", "Surfaces.html"));
        }

        public IDisposable UseRepoRootAsCurrentDirectory() => new CurrentDirectoryScope(RepoRoot);
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _originalDirectory;

        public CurrentDirectoryScope(string newDirectory)
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(newDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
        }
    }
}
