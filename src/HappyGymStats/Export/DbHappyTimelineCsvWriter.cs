using System.Text;
using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Export;

public static class DbHappyTimelineCsvWriter
{
    public static async Task<CsvExportRunner.ExportResult> WriteAsync(
        string databasePath,
        string outputCsvPath,
        CancellationToken ct = default)
    {
        try
        {
            var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new HappyGymStatsDbContext(options);
            var rows = await db.DerivedHappyEvents
                .AsNoTracking()
                .OrderBy(row => row.SortOrder)
                .ThenBy(row => row.EventId)
                .ToListAsync(ct);

            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath) ?? ".");

            using var writer = new StreamWriter(
                outputCsvPath,
                append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            CsvWriter.WriteHeader(writer, HappyTimelineCsvWriter.Columns);

            foreach (var ev in rows)
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["event_id"] = ev.EventId,
                    ["source_log_id"] = ev.SourceLogId ?? string.Empty,
                    ["timestamp"] = ev.OccurredAtUtc.ToUnixTimeSeconds().ToString(),
                    ["event_type"] = ev.EventType,
                    ["happy_before_event"] = ev.HappyBeforeEvent?.ToString() ?? string.Empty,
                    ["happy_after_event"] = ev.HappyAfterEvent?.ToString() ?? string.Empty,
                    ["delta"] = ev.Delta?.ToString() ?? string.Empty,
                    ["happy_used"] = ev.HappyUsed?.ToString() ?? string.Empty,
                    ["max_happy_at_time_utc"] = ev.MaxHappyAtTimeUtc?.ToString() ?? string.Empty,
                    ["clamped_to_max"] = ev.ClampedToMax ? "true" : "false",
                };

                CsvWriter.WriteRow(writer, HappyTimelineCsvWriter.Columns, row);
            }

            return new CsvExportRunner.ExportResult
            {
                Success = true,
                OutputPath = outputCsvPath,
                HeaderColumns = HappyTimelineCsvWriter.Columns,
                RowsWritten = rows.Count,
                DerivedFileMissing = false,
            };
        }
        catch (Exception ex)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = $"Failed to write DB-backed happy timeline CSV file '{outputCsvPath}': {ex.Message}",
                HeaderColumns = HappyTimelineCsvWriter.Columns,
            };
        }
    }
}
