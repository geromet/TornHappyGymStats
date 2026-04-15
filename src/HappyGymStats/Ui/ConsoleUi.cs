using System.Text;
using HappyGymStats.Reconstruction;
using HappyGymStats.Storage;
using HappyGymStats.Storage.Models;
using Spectre.Console;

namespace HappyGymStats.Ui;

public enum MainMenuAction
{
    Fetch,
    Resume,
    ShowStatus,
    ReconstructHappy,
    ExportCsv,
    Visualize,
    ConfigureThrottle,
    Exit,
}

public sealed class ConsoleUi
{
    public MainMenuAction PromptMainMenu(bool hasCheckpoint, bool hasJsonlData)
    {
        AnsiConsole.Clear();

        var title = new StringBuilder();
        title.AppendLine();
        title.AppendLine("  [bold aqua]HappyGymStats[/] — [grey]Torn gym log analysis[/]");
        title.AppendLine();

        var titlePanel = new Panel(title.ToString().Trim()) { Border = BoxBorder.None };
        AnsiConsole.Write(titlePanel);

        var choices = new List<MainMenuAction>();

        if (hasCheckpoint)
        {
            choices.Add(MainMenuAction.Resume);
            choices.Add(MainMenuAction.Fetch);
        }
        else
        {
            choices.Add(MainMenuAction.Fetch);
        }

        choices.Add(MainMenuAction.ShowStatus);

        if (hasJsonlData)
        {
            choices.Add(MainMenuAction.ReconstructHappy);
            choices.Add(MainMenuAction.ExportCsv);
        }

        choices.Add(MainMenuAction.Visualize);
        choices.Add(MainMenuAction.ConfigureThrottle);
        choices.Add(MainMenuAction.Exit);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<MainMenuAction>()
                .Title("[bold]Select an action[/]")
                .AddChoices(choices)
                .UseConverter(a => ToMenuLabel(a, hasCheckpoint, hasJsonlData)));

        return choice;
    }

    public string PromptApiKey()
    {
        RenderInfo("The API key is only kept in memory for this session.");

        var prompt = new TextPrompt<string>("Enter Torn API key ([grey]hidden[/]):")
            .Secret()
            .Validate(value =>
            {
                if (string.IsNullOrWhiteSpace(value))
                    return ValidationResult.Error("API key cannot be empty.");

                return ValidationResult.Success();
            });

        return AnsiConsole.Prompt(prompt).Trim();
    }

    public TimeSpan PromptThrottleDelay(TimeSpan current)
    {
        var prompt = new TextPrompt<int>($"Throttle delay in milliseconds ([grey]current: {current.TotalMilliseconds:0}[/])")
            .DefaultValue((int)Math.Round(current.TotalMilliseconds));
        prompt.ShowDefaultValue = true;

        prompt.Validate(ms =>
        {
            if (ms < 0)
                return ValidationResult.Error("Throttle delay must be >= 0.");

            // Guard rails: default Torn limits are low; we want a safe minimum.
            if (ms is > 0 and < 250)
                return ValidationResult.Error("Throttle delay should be >= 250 ms to avoid rate limit spikes.");

            return ValidationResult.Success();
        });

        var millis = AnsiConsole.Prompt(prompt);
        return TimeSpan.FromMilliseconds(millis);
    }

    public int PromptCurrentHappy()
    {
        var prompt = new TextPrompt<int>("Enter current happy ([grey]>= 0[/]):")
            .Validate(happy => happy < 0
                ? ValidationResult.Error("Happy must be >= 0.")
                : ValidationResult.Success());

        return AnsiConsole.Prompt(prompt);
    }

    public void RenderReconstructionSummary(ReconstructionRunner.RunResult result, int previewCount = 5)
    {
        if (!result.Success)
        {
            RenderError(result.ErrorMessage ?? "Reconstruction failed.");
            return;
        }

        if (result.Stats is null)
        {
            RenderError("Reconstruction succeeded but did not produce stats (unexpected).");
            return;
        }

        var s = result.Stats;

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Item")
            .AddColumn("Value");

        summary.AddRow("Derived output", Markup.Escape(result.DerivedOutputPath));
        summary.AddRow("Anchor time (UTC)", Markup.Escape(result.AnchorTimeUtc.ToString("u")));
        summary.AddRow("Lines read", s.LinesRead.ToString());
        summary.AddRow("Malformed lines", s.MalformedLines.ToString());
        summary.AddRow("Gym-train events", s.GymTrainEventsExtracted.ToString());
        summary.AddRow("Max-happy events", s.MaxHappyEventsExtracted.ToString());
        summary.AddRow("Happy delta events", s.HappyDeltaEventsExtracted.ToString());
        summary.AddRow("Gym trains derived", s.GymTrainsDerived.ToString());
        summary.AddRow("Clamp applied", s.ClampAppliedCount.ToString());
        summary.AddRow("Warnings", s.WarningCount.ToString());

        AnsiConsole.Write(new Panel(summary).Header("[bold]Reconstruction summary[/]", Justify.Left));

        if (result.DerivedGymTrains.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No gym trains were derived.[/]");
            return;
        }

        var preview = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("log_id")
            .AddColumn("time")
            .AddColumn("before")
            .AddColumn("used")
            .AddColumn("after")
            .AddColumn("max")
            .AddColumn("clamped");

        foreach (var row in result.DerivedGymTrains.TakeLast(Math.Max(1, previewCount)))
        {
            preview.AddRow(
                row.LogId.ToString(),
                Markup.Escape(row.OccurredAtUtc.ToString("u")),
                row.HappyBeforeTrain.ToString(),
                row.HappyUsed.ToString(),
                row.HappyAfterTrain.ToString(),
                row.MaxHappyAtTimeUtc?.ToString() ?? "(none)",
                row.ClampedToMax ? "yes" : "no");
        }

        AnsiConsole.Write(new Panel(preview).Header($"[bold]Preview (last {Math.Min(previewCount, result.DerivedGymTrains.Count)})[/]", Justify.Left));
    }

    public void RenderCsvExportSummary(Export.CsvExportRunner.ExportResult result)
    {
        if (!result.Success)
        {
            RenderError(result.ErrorMessage ?? "CSV export failed.");
            return;
        }

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Item")
            .AddColumn("Value");

        summary.AddRow("Output path", Markup.Escape(result.OutputPath ?? "(unknown)"));
        summary.AddRow("Header columns", result.HeaderColumns.Count.ToString());
        summary.AddRow("Rows written", result.RowsWritten.ToString());

        if (result.ReaderStats is not null)
        {
            summary.AddRow("Lines read", result.ReaderStats.LinesRead.ToString());
            summary.AddRow("Malformed lines", result.ReaderStats.MalformedLines.ToString());
        }

        if (result.DerivedFileMissing)
        {
            summary.AddRow("Derived sidecar", "[yellow]Not found (derived columns left blank)[/]");
        }
        else
        {
            summary.AddRow("Derived sidecar", result.DerivedMalformedLines > 0
                ? $"[yellow]{result.DerivedMalformedLines} malformed line(s) skipped[/]"
                : "[green]Loaded[/]");
        }

        AnsiConsole.Write(new Panel(summary).Header("[bold]CSV export summary[/]", Justify.Left));
    }

    public void RenderVisualizeSummary(IReadOnlyList<string> generatedPaths, int recordCount, int parseErrors)
    {
        if (generatedPaths.Count == 0)
        {
            RenderInfo("No surface plots generated — no gym train data found.");
            return;
        }

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Item")
            .AddColumn("Value");

        summary.AddRow("Output files", generatedPaths.Count.ToString());
        summary.AddRow("Records processed", recordCount.ToString());

        foreach (var path in generatedPaths)
        {
            summary.AddRow("  →", Markup.Escape(path));
        }

        if (parseErrors > 0)
        {
            summary.AddRow("[yellow]Parse errors[/]", $"[yellow]{parseErrors} row(s) skipped[/]");
        }

        AnsiConsole.Write(new Panel(summary).Header("[bold]Visualization summary[/]", Justify.Left));
    }

    public void RenderStatus(AppPaths paths, Checkpoint? checkpoint)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Item")
            .AddColumn("Value");

        table.AddRow("Data directory", Markup.Escape(paths.DataDirectory));
        table.AddRow("Quarantine", Markup.Escape(paths.QuarantineDirectory));
        table.AddRow("Checkpoint", Markup.Escape(paths.CheckpointPath));
        table.AddRow("JSONL store", Markup.Escape(paths.LogsJsonlPath));
        table.AddRow("Derived directory", Markup.Escape(paths.DerivedDirectory));
        table.AddRow("Derived gym trains", Markup.Escape(paths.DerivedGymTrainsJsonlPath));
        table.AddRow("Export directory", Markup.Escape(paths.ExportDirectory));
        table.AddRow("CSV output", Markup.Escape(paths.LogsCsvPath));
        table.AddRow("Debug CSV output", Markup.Escape(paths.LogsDebugCsvPath));

        if (checkpoint is null)
        {
            table.AddRow("Last log", "[grey](none yet)[/]");
            table.AddRow("Next cursor", "[grey](none)[/]");
            table.AddRow("Last run", "[grey](none yet)[/]");
            table.AddRow("Last error", "[grey](none)[/]");
        }
        else
        {
            var last = $"id={checkpoint.LastLogId?.ToString() ?? "(unknown)"}, " +
                       $"ts={checkpoint.LastLogTimestamp?.ToString("u") ?? "(unknown)"}, " +
                       $"cat={checkpoint.LastLogCategory ?? "(unknown)"}, " +
                       $"title={checkpoint.LastLogTitle ?? "(unknown)"}";
            table.AddRow("Last log", Markup.Escape(last));

            table.AddRow("Next cursor", string.IsNullOrWhiteSpace(checkpoint.NextUrl)
                ? "[grey](none)[/]"
                : Markup.Escape(checkpoint.NextUrl));

            var run = $"outcome={checkpoint.LastRunOutcome ?? "(unknown)"}, " +
                      $"started={checkpoint.LastRunStartedAt?.ToString("u") ?? "(unknown)"}, " +
                      $"completed={checkpoint.LastRunCompletedAt?.ToString("u") ?? "(unknown)"}";
            table.AddRow("Last run", Markup.Escape(run));

            if (string.IsNullOrWhiteSpace(checkpoint.LastErrorMessage))
            {
                table.AddRow("Last error", "[grey](none)[/]");
            }
            else
            {
                var err = $"at={checkpoint.LastErrorAt?.ToString("u") ?? "(unknown)"}, msg={checkpoint.LastErrorMessage}";
                table.AddRow("Last error", Markup.Escape(err));
            }
        }

        AnsiConsole.Write(new Panel(table).Header("[bold]Status[/]", Justify.Left));
        AnsiConsole.MarkupLine("[grey]Note: the API key is never written to disk by the UI.[/]");
    }

    public void RenderError(string message, Exception? ex = null, params string?[] secretsToRedact)
    {
        var safeMessage = RedactSecrets(message, secretsToRedact);
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(safeMessage)}");

        if (ex is not null)
        {
            // Show full exception chain for debugging.
            var current = ex;
            var depth = 0;
            while (current is not null)
            {
                var prefix = depth == 0 ? "Exception" : $"Inner exception ({depth})";
                var typeLine = $"{prefix}: {current.GetType().FullName}";
                var msgLine = RedactSecrets(current.Message, secretsToRedact);

                AnsiConsole.MarkupLine($"[grey]{Markup.Escape(typeLine)}[/]");
                if (!string.IsNullOrWhiteSpace(msgLine))
                    AnsiConsole.MarkupLine($"[grey]  {Markup.Escape(msgLine)}[/]");

                if (current.StackTrace is not null)
                {
                    var safeStack = RedactSecrets(current.StackTrace, secretsToRedact);
                    foreach (var line in safeStack.Split('\n').Take(8))
                    {
                        AnsiConsole.MarkupLine($"[dim grey]  {Markup.Escape(line.TrimEnd('\r'))}[/]");
                    }
                    var remaining = safeStack.Split('\n').Length - 8;
                    if (remaining > 0)
                        AnsiConsole.MarkupLine($"[dim grey]  ... ({remaining} more frame(s))[/]");
                }

                current = current.InnerException;
                depth++;
            }
        }
    }

    public void RenderInfo(string message)
    {
        AnsiConsole.MarkupLine($"[blue]Info:[/] {Markup.Escape(message)}");
    }

    public void RenderPrivacyWarning()
    {
        var panel = new Panel(
            new Markup(
                "[yellow bold]⚠  Privacy warning[/]\n\n" +
                "The data handled by this tool — gym logs, happy values, and any " +
                "exported CSV files — can reveal your in-game activity patterns.\n\n" +
                "[red]Do not share exported files or screenshots with anyone you do not fully trust.[/]\n" +
                "Malicious actors could use this information to infer your online schedule, " +
                "energy/gym habits, or other sensitive gameplay details."))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("yellow"),
            Padding = new Padding(1, 1),
        };

        AnsiConsole.Write(panel);
    }

    public void PromptContinue()
    {
        AnsiConsole.MarkupLine("\n[grey]Press Enter to continue...[/]");
        _ = Console.ReadLine();
    }

    private static string ToMenuLabel(MainMenuAction action, bool hasCheckpoint, bool hasJsonlData) => action switch
    {
        MainMenuAction.Fetch => hasCheckpoint ? "Fetch logs (start over)" : "Fetch logs (new)",
        MainMenuAction.Resume => "Resume fetch",
        MainMenuAction.ShowStatus => "Show status",
        MainMenuAction.ReconstructHappy => "Reconstruct happy",
        MainMenuAction.ExportCsv => "Export CSV",
        MainMenuAction.Visualize => "Visualize",
        MainMenuAction.ConfigureThrottle => "Configure throttle",
        MainMenuAction.Exit => "Exit",
        _ => action.ToString(),
    };

    private static string RedactSecrets(string value, params string?[] secrets)
    {
        var redacted = value;
        foreach (var secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret))
                continue;

            redacted = redacted.Replace(secret, "***", StringComparison.Ordinal);
        }

        return redacted;
    }
}
