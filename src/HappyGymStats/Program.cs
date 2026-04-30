using System.Linq;
using HappyGymStats.Data;
using HappyGymStats.Export;
using HappyGymStats.Fetch;
using HappyGymStats.Reconstruction;
using HappyGymStats.Storage;
using HappyGymStats.Ui;
using HappyGymStats.Torn;
using HappyGymStats.Visualizer;
using HappyGymStats.Verification;
using Microsoft.EntityFrameworkCore;

var ui = new ConsoleUi();

var paths = AppPaths.Default();
var databasePath = SqlitePaths.ResolveDatabasePath(paths.DataDirectory);
Directory.CreateDirectory(paths.DataDirectory);
Directory.CreateDirectory(paths.QuarantineDirectory);
Directory.CreateDirectory(paths.DerivedDirectory);
Directory.CreateDirectory(paths.ExportDirectory);

// Non-interactive CLI modes (useful for regenerating artifacts without the menu UI)
//
// Usage:
//   HappyGymStats visualize [--csv <path>] [--out <dir>]
//   HappyGymStats visualize-happy [--timeline <path>] [--out <dir>]
//   HappyGymStats reconstruct-happy --current <int> [--anchor <iso-utc>]
if (args.Length > 0)
{
    string? GetArg(string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    if (args[0].Equals("visualize", StringComparison.OrdinalIgnoreCase))
    {
        var csvPath = GetArg("--csv") ?? paths.LogsCsvPath;
        var outDir = GetArg("--out") ?? paths.ExportDirectory;

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"CSV not found: {csvPath}");
            return;
        }

        Directory.CreateDirectory(outDir);

        var result = CsvStatReader.readStatRecords(csvPath);
        var recordCount = result.Records.Count();

        if (recordCount == 0)
        {
            Console.WriteLine("No gym train records found in CSV.");
            return;
        }

        var generatedPaths = SurfacePlotter.generateStackedPlots(result.Records, outDir).ToList();

        Console.WriteLine($"Records processed: {recordCount}");
        Console.WriteLine($"Parse errors: {result.ParseErrors.Count()}");
        foreach (var p in generatedPaths)
            Console.WriteLine(p);

        return;
    }

    if (args[0].Equals("visualize-happy", StringComparison.OrdinalIgnoreCase))
    {
        var timelinePath = GetArg("--timeline") ?? paths.HappyTimelineCsvPath;
        var outDir = GetArg("--out") ?? paths.ExportDirectory;

        if (!File.Exists(timelinePath))
        {
            Console.Error.WriteLine($"Happy timeline CSV not found: {timelinePath}");
            return;
        }

        Directory.CreateDirectory(outDir);

        var outPath = Path.Combine(outDir, "HappyTimeline.html");
        var generated = HappyTimelinePlotter.generateHappyTimelinePlot(timelinePath, outPath);

        Console.WriteLine(generated);
        return;
    }

    if (args[0].Equals("reconstruct-happy", StringComparison.OrdinalIgnoreCase))
    {
        var currentText = GetArg("--current");
        if (string.IsNullOrWhiteSpace(currentText) || !int.TryParse(currentText, out var currentHappy) || currentHappy < 0)
        {
            Console.Error.WriteLine("Missing/invalid --current <int> (must be >= 0)");
            return;
        }

        var anchorText = GetArg("--anchor");
        var anchorTimeUtc = string.IsNullOrWhiteSpace(anchorText)
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Parse(anchorText, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);

        var runner = new ReconstructionRunner(paths);
        var result = runner.Run(currentHappy: currentHappy, anchorTimeUtc: anchorTimeUtc, ct: CancellationToken.None);

        if (!result.Success)
        {
            Console.Error.WriteLine(result.ErrorMessage ?? "Reconstruction failed.");
            return;
        }

        Console.WriteLine($"Derived output: {result.DerivedOutputPath}");
        Console.WriteLine($"Gym trains derived: {result.DerivedGymTrains.Count}");
        if (result.Stats is not null)
        {
            Console.WriteLine($"Clamp applied: {result.Stats.ClampAppliedCount}");
            Console.WriteLine($"Warnings: {result.Stats.WarningCount}");
            Console.WriteLine($"Max-happy events extracted: {result.Stats.MaxHappyEventsExtracted}");
            Console.WriteLine($"Happy delta events extracted: {result.Stats.HappyDeltaEventsExtracted}");
        }

        return;
    }

    if (args[0].Equals("migrate-legacy-db", StringComparison.OrdinalIgnoreCase))
    {
        var dbPath = GetArg("--db") ?? databasePath;
        var result = await LegacySqliteMigrator.RunAsync(paths, dbPath, CancellationToken.None);

        if (!result.Success)
        {
            Console.Error.WriteLine(result.ErrorMessage ?? "Legacy-to-SQLite migration failed.");
            return;
        }

        Console.WriteLine($"SQLite database: {result.DatabasePath}");
        Console.WriteLine($"Raw logs imported: {result.RawLogsImported}");
        Console.WriteLine($"Derived gym trains imported: {result.DerivedGymTrainsImported}");
        Console.WriteLine($"Derived happy events imported: {result.DerivedHappyEventsImported}");
        Console.WriteLine($"Checkpoint imported: {(result.CheckpointImported ? "yes" : "no")}");
        return;
    }

    if (args[0].Equals("export-csv", StringComparison.OrdinalIgnoreCase))
    {
        var source = (GetArg("--source") ?? "auto").Trim().ToLowerInvariant();
        var dbPath = GetArg("--db") ?? databasePath;
        var jsonlPath = GetArg("--jsonl") ?? paths.LogsJsonlPath;
        var derivedPath = GetArg("--derived") ?? paths.DerivedGymTrainsJsonlPath;
        var derivedHappyPath = GetArg("--derived-happy") ?? paths.DerivedHappyEventsJsonlPath;
        var csvPath = GetArg("--csv") ?? paths.LogsCsvPath;
        var debugCsvPath = GetArg("--debug-csv") ?? paths.LogsDebugCsvPath;
        var timelinePath = GetArg("--timeline") ?? paths.HappyTimelineCsvPath;

        var useDatabase = source switch
        {
            "db" => true,
            "legacy" => false,
            "auto" => File.Exists(dbPath) && new FileInfo(dbPath).Length > 0,
            _ => throw new ArgumentException("--source must be one of: auto, db, legacy")
        };

        if (useDatabase)
        {
            var dbExportResult = await DbCsvExportRunner.RunAsync(dbPath, csvPath, CancellationToken.None);
            if (!dbExportResult.Success)
            {
                Console.Error.WriteLine(dbExportResult.ErrorMessage ?? "DB-backed CSV export failed.");
                return;
            }

            Console.WriteLine($"CSV output: {dbExportResult.OutputPath} (rows={dbExportResult.RowsWritten}, source=db)");

            var dbDebugResult = await DbCsvExportRunner.RunDebugAsync(dbPath, debugCsvPath, CancellationToken.None);
            if (!dbDebugResult.Success)
            {
                Console.Error.WriteLine(dbDebugResult.ErrorMessage ?? "DB-backed debug CSV export failed.");
                return;
            }

            Console.WriteLine($"Debug CSV output: {dbDebugResult.OutputPath} (rows={dbDebugResult.RowsWritten}, source=db)");

            var dbTimelineResult = await DbHappyTimelineCsvWriter.WriteAsync(dbPath, timelinePath, CancellationToken.None);
            if (!dbTimelineResult.Success)
            {
                Console.Error.WriteLine(dbTimelineResult.ErrorMessage ?? "DB-backed happy timeline CSV export failed.");
                return;
            }

            Console.WriteLine($"Happy timeline CSV output: {dbTimelineResult.OutputPath} (rows={dbTimelineResult.RowsWritten}, source=db)");
            return;
        }

        var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedPath);
        if (!result.Success)
        {
            Console.Error.WriteLine(result.ErrorMessage ?? "CSV export failed.");
            return;
        }

        Console.WriteLine($"CSV output: {result.OutputPath} (rows={result.RowsWritten}, source=legacy)");

        var debugResult = CsvExportRunner.RunDebug(jsonlPath, debugCsvPath, derivedPath, derivedHappyPath);
        if (!debugResult.Success)
        {
            Console.Error.WriteLine(debugResult.ErrorMessage ?? "Debug CSV export failed.");
            return;
        }

        Console.WriteLine($"Debug CSV output: {debugResult.OutputPath} (rows={debugResult.RowsWritten}, source=legacy)");

        var timelineResult = HappyTimelineCsvWriter.Write(derivedHappyPath, timelinePath);
        if (!timelineResult.Success)
        {
            Console.Error.WriteLine(timelineResult.ErrorMessage ?? "Happy timeline CSV export failed.");
            return;
        }

        Console.WriteLine($"Happy timeline CSV output: {timelineResult.OutputPath} (rows={timelineResult.RowsWritten}, source=legacy)");
        return;
    }

    // Verification / debugging mode.
    // Usage:
    //   HappyGymStats verify-export [--csv <path>] [--jsonl <path>] [--derived <path>] [--html <path>] [--top <n>]
    if (args[0].Equals("verify-export", StringComparison.OrdinalIgnoreCase))
    {
        var csvPath = GetArg("--csv") ?? paths.LogsCsvPath;
        var jsonlPath = GetArg("--jsonl") ?? paths.LogsJsonlPath;
        var derivedPath = GetArg("--derived") ?? paths.DerivedGymTrainsJsonlPath;
        var htmlPath = GetArg("--html");

        var topText = GetArg("--top");
        var top = 20;
        if (!string.IsNullOrWhiteSpace(topText) && int.TryParse(topText, out var parsedTop) && parsedTop >= 0)
            top = parsedTop;

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"CSV not found: {csvPath}");
            return;
        }

        var report = ExportVerifier.Verify(new ExportVerifier.VerifyOptions(
            CsvPath: csvPath,
            LogsJsonlPath: File.Exists(jsonlPath) ? jsonlPath : null,
            DerivedGymTrainsJsonlPath: File.Exists(derivedPath) ? derivedPath : null,
            SurfacesHtmlPath: (htmlPath is not null && File.Exists(htmlPath)) ? htmlPath : null,
            TopOutliers: top));

        Console.WriteLine($"Stat rows (gym trains): {report.StatRows}");
        if (report.JsonlRowsFound > 0)
            Console.WriteLine($"JSONL rows matched: {report.JsonlRowsFound}");
        if (report.DerivedRowsFound > 0)
            Console.WriteLine($"Derived gym-train rows matched: {report.DerivedRowsFound}");

        Console.WriteLine($"Mismatches: {report.Mismatches}");
        foreach (var m in report.SampleMismatches)
            Console.WriteLine($"MISMATCH id={m.LogId} field={m.Field} expected={m.Expected} actual={m.Actual}");

        if (report.TopOutlierLines.Count > 0)
        {
            Console.WriteLine($"Top {report.TopOutlierLines.Count} outliers (stat gained / energy):");
            foreach (var line in report.TopOutlierLines)
                Console.WriteLine(line);
        }

        if (report.VisualizationHtmlMatchesCsv)
            Console.WriteLine("Surfaces.html check: PASS (raw point cloud matches CSV-derived stat rows)");
        else if (htmlPath is not null)
            Console.WriteLine("Surfaces.html check: FAIL (raw point cloud could not be parsed or did not match)");

        return;
    }
}

using var appCts = new CancellationTokenSource();
CancellationTokenSource? operationCts = null;

var cancelPressCount = 0;
Console.CancelKeyPress += (_, e) =>
{
    cancelPressCount++;

    // First Ctrl+C: request graceful cancellation and keep the process alive.
    if (cancelPressCount == 1)
    {
        e.Cancel = true;

        if (operationCts is not null)
        {
            operationCts.Cancel();
            ui.RenderInfo("Cancellation requested (will stop after the current request; press Ctrl+C again to force quit)." );
        }
        else
        {
            appCts.Cancel();
            ui.RenderInfo("Cancellation requested (press Ctrl+C again to force quit)." );
        }

        return;
    }

    // Second Ctrl+C: allow the runtime to terminate immediately.
    e.Cancel = false;
};

var state = new AppState(
    ApiKey: null,
    ThrottleDelay: TimeSpan.FromMilliseconds(1100));

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30),
};

var torn = new TornApiClient(httpClient);
var fetcher = new LogFetcher(paths, torn);

// Project scope: gym/happy related logs only.
// Torn v2 /user/log supports category filtering via `cat`.
var freshStartUrl = new Uri("https://api.torn.com/v2/user/log?cat=25");

ui.RenderPrivacyWarning();

while (true)
{
    if (appCts.IsCancellationRequested)
    {
        ui.RenderInfo("Shutting down...");
        break;
    }

    var action = ui.PromptMainMenu(
        hasCheckpoint: CheckpointStore.TryRead(paths.CheckpointPath) is not null,
        hasJsonlData: File.Exists(paths.LogsJsonlPath) && new FileInfo(paths.LogsJsonlPath).Length > 0);

    if (action == MainMenuAction.Exit)
        break;

    try
    {
        switch (action)
        {
            case MainMenuAction.ShowStatus:
            {
                var checkpoint = CheckpointStore.TryRead(paths.CheckpointPath);
                ui.RenderStatus(paths, databasePath, checkpoint);
                break;
            }

            case MainMenuAction.ReconstructHappy:
            {
                var currentHappy = ui.PromptCurrentHappy();
                var runner = new ReconstructionRunner(paths);

                cancelPressCount = 0;
                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(appCts.Token);
                operationCts = opCts;

                try
                {
                    ui.RenderInfo("Starting happy reconstruction...");

                    var result = runner.Run(
                        currentHappy: currentHappy,
                        anchorTimeUtc: DateTimeOffset.UtcNow,
                        ct: opCts.Token);

                    if (!result.Success)
                    {
                        ui.RenderError(result.ErrorMessage ?? "Reconstruction failed.");
                    }
                    else
                    {
                        ui.RenderReconstructionSummary(result);
                    }
                }
                finally
                {
                    operationCts = null;
                    cancelPressCount = 0;
                }

                break;
            }

            case MainMenuAction.ExportCsv:
            {
                cancelPressCount = 0;
                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(appCts.Token);
                operationCts = opCts;

                try
                {
                    ui.RenderPrivacyWarning();

                    var preferDatabase = SqlitePaths.ShouldPreferDatabase(
                        databasePath,
                        paths.LogsJsonlPath,
                        paths.DerivedGymTrainsJsonlPath,
                        paths.DerivedHappyEventsJsonlPath);
                    ui.RenderInfo(preferDatabase
                        ? "Starting DB-backed CSV export..."
                        : "Starting CSV export...");

                    CsvExportRunner.ExportResult result;
                    CsvExportRunner.ExportResult debugResult;
                    CsvExportRunner.ExportResult timelineResult;

                    if (preferDatabase)
                    {
                        result = await DbCsvExportRunner.RunAsync(databasePath, paths.LogsCsvPath, opCts.Token);
                        debugResult = await DbCsvExportRunner.RunDebugAsync(databasePath, paths.LogsDebugCsvPath, opCts.Token);
                        timelineResult = await DbHappyTimelineCsvWriter.WriteAsync(databasePath, paths.HappyTimelineCsvPath, opCts.Token);
                    }
                    else
                    {
                        result = CsvExportRunner.Run(
                            paths.LogsJsonlPath,
                            paths.LogsCsvPath,
                            paths.DerivedGymTrainsJsonlPath);

                        debugResult = CsvExportRunner.RunDebug(
                            paths.LogsJsonlPath,
                            paths.LogsDebugCsvPath,
                            paths.DerivedGymTrainsJsonlPath,
                            paths.DerivedHappyEventsJsonlPath);

                        timelineResult = HappyTimelineCsvWriter.Write(
                            paths.DerivedHappyEventsJsonlPath,
                            paths.HappyTimelineCsvPath);
                    }

                    ui.RenderCsvExportSummary(result);

                    if (debugResult.Success)
                    {
                        ui.RenderInfo($"Debug CSV output: {debugResult.OutputPath} (rows={debugResult.RowsWritten})");

                        if (timelineResult.Success)
                        {
                            ui.RenderInfo($"Happy timeline CSV output: {timelineResult.OutputPath} (rows={timelineResult.RowsWritten})");
                        }
                        else
                        {
                            ui.RenderError(timelineResult.ErrorMessage ?? "Happy timeline CSV export failed.");
                        }
                    }
                    else
                    {
                        ui.RenderError(debugResult.ErrorMessage ?? "Debug CSV export failed.");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // let outer catch handle it
                }
                finally
                {
                    operationCts = null;
                    cancelPressCount = 0;
                }

                break;
            }

            case MainMenuAction.ConfigureThrottle:
            {
                state = state with { ThrottleDelay = ui.PromptThrottleDelay(state.ThrottleDelay) };
                ui.RenderInfo($"Throttle delay set to {state.ThrottleDelay.TotalMilliseconds:0} ms.");
                break;
            }

            case MainMenuAction.Fetch:
            case MainMenuAction.Resume:
            {
                if (string.IsNullOrWhiteSpace(state.ApiKey))
                    state = state with { ApiKey = ui.PromptApiKey() };

                var mode = action == MainMenuAction.Fetch ? FetchMode.Fresh : FetchMode.Resume;
                var options = FetchOptions.Default(freshStartUrl, state.ThrottleDelay);

                cancelPressCount = 0;
                using var opCts = CancellationTokenSource.CreateLinkedTokenSource(appCts.Token);
                operationCts = opCts;

                try
                {
                    ui.RenderInfo(mode == FetchMode.Fresh ? "Starting fetch (new)..." : "Resuming fetch...");

                    var result = await fetcher.RunAsync(
                        apiKey: state.ApiKey!,
                        mode: mode,
                        options: options,
                        ct: opCts.Token,
                        log: ui.RenderInfo);

                    ui.RenderInfo($"Fetch finished. pages={result.PagesFetched}, fetched={result.LogsFetched}, appended={result.LogsAppended}.");
                }
                finally
                {
                    operationCts = null;
                    cancelPressCount = 0;
                }

                break;
            }

            case MainMenuAction.Visualize:
            {
                if (!File.Exists(paths.LogsCsvPath))
                {
                    ui.RenderError("No CSV export found. Run 'Export CSV' first.");
                    break;
                }

                try
                {
                    var result = CsvStatReader.readStatRecords(paths.LogsCsvPath);

                    var recordCount = result.Records.Count();
                    if (recordCount == 0)
                    {
                        ui.RenderInfo("CSV file is empty or contains no gym train records.");
                        break;
                    }

                    var generatedPaths = SurfacePlotter.generateStackedPlots(result.Records, paths.ExportDirectory).ToList();

                    ui.RenderVisualizeSummary(generatedPaths, recordCount, result.ParseErrors.Count());
                }
                catch (FileNotFoundException ex)
                {
                    ui.RenderError("CSV file not found. Run 'Export CSV' first.", ex);
                }
                catch (Exception ex)
                {
                    ui.RenderError("Visualization failed.", ex);
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    catch (OperationCanceledException)
    {
        // Intended cancellation path.
        ui.RenderInfo("Operation cancelled.");
    }
    catch (Exception ex)
    {
        // Never print secrets (API key).
        ui.RenderError("Unexpected error.", ex, state.ApiKey);
    }

    ui.PromptContinue();
}

// ReSharper disable once RedundantRecordBody
internal readonly record struct AppState(string? ApiKey, TimeSpan ThrottleDelay);
