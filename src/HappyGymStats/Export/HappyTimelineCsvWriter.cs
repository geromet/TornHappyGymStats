using System.Text;
using HappyGymStats.Export;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Export;

/// <summary>
/// Writes a fixed-schema happy timeline CSV from derived-happy-events.jsonl.
/// </summary>
public static class HappyTimelineCsvWriter
{
    public static readonly string[] Columns =
    {
        "event_id",
        "source_log_id",
        "timestamp",
        "event_type",
        "happy_before_event",
        "happy_after_event",
        "delta",
        "happy_used",
        "max_happy_at_time_utc",
        "clamped_to_max",
    };

    public static CsvExportRunner.ExportResult Write(
        string derivedHappyEventsJsonlPath,
        string outputCsvPath)
    {
        var read = DerivedHappyEventReader.Read(derivedHappyEventsJsonlPath);
        if (read.ErrorMessage is not null)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = read.ErrorMessage,
            };
        }

        if (read.FileMissing)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = $"Derived happy events not found: {derivedHappyEventsJsonlPath} (run reconstruction first)",
            };
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath)!);
        }
        catch (Exception ex)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = $"Unable to create output directory for '{outputCsvPath}': {ex.Message}",
            };
        }

        try
        {
            using var writer = new StreamWriter(
                outputCsvPath,
                append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            CsvWriter.WriteHeader(writer, Columns);

            var rowsWritten = 0;
            foreach (var ev in read.AllEvents.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.EventType).ThenBy(e => e.EventId))
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["event_id"] = ev.EventId,
                    ["source_log_id"] = ev.SourceLogId ?? string.Empty,
                    ["timestamp"] = ev.OccurredAtUtc.ToUnixTimeSeconds().ToString(),
                    ["event_type"] = ev.EventType,
                    ["happy_before_event"] = ev.HappyBeforeEvent.ToString(),
                    ["happy_after_event"] = ev.HappyAfterEvent.ToString(),
                    ["delta"] = ev.Delta?.ToString() ?? string.Empty,
                    ["happy_used"] = ev.HappyUsed?.ToString() ?? string.Empty,
                    ["max_happy_at_time_utc"] = ev.MaxHappyAtTimeUtc?.ToString() ?? string.Empty,
                    ["clamped_to_max"] = ev.ClampedToMax ? "true" : "false",
                };

                CsvWriter.WriteRow(writer, Columns, row);
                rowsWritten++;
            }

            return new CsvExportRunner.ExportResult
            {
                Success = true,
                OutputPath = outputCsvPath,
                HeaderColumns = Columns,
                RowsWritten = rowsWritten,
                DerivedFileMissing = false,
            };
        }
        catch (Exception ex)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = $"Failed to write CSV file '{outputCsvPath}': {ex.Message}",
                HeaderColumns = Columns,
            };
        }
    }
}
