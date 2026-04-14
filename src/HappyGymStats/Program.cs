using System.Linq;
using HappyGymStats.Export;
using HappyGymStats.Fetch;
using HappyGymStats.Reconstruction;
using HappyGymStats.Storage;
using HappyGymStats.Ui;
using HappyGymStats.Torn;
using HappyGymStats.Visualizer;

var ui = new ConsoleUi();

var paths = AppPaths.Default();
Directory.CreateDirectory(paths.DataDirectory);
Directory.CreateDirectory(paths.QuarantineDirectory);
Directory.CreateDirectory(paths.DerivedDirectory);
Directory.CreateDirectory(paths.ExportDirectory);

// Non-interactive CLI modes (useful for regenerating artifacts without the menu UI)
//
// Usage:
//   HappyGymStats visualize [--csv <path>] [--out <dir>]
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
        }

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
                ui.RenderStatus(paths, checkpoint);
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

                    ui.RenderInfo("Starting CSV export...");

                    var result = CsvExportRunner.Run(
                        paths.LogsJsonlPath,
                        paths.LogsCsvPath,
                        paths.DerivedGymTrainsJsonlPath);

                    ui.RenderCsvExportSummary(result);
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
