using System.Globalization;
using System.Text;
using System.Text.Json;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Legacy.Cli.Export;

public static class DbCsvExportRunner
{
    private static readonly (string Type, string Key)[] KnownStatTypes =
    {
        ("strength", "strength"),
        ("defense", "defense"),
        ("speed", "speed"),
        ("dexterity", "dexterity")
    };

    public static async Task<CsvExportRunner.ExportResult> RunAsync(
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

            var rawRows = await db.RawUserLogs
                .AsNoTracking()
                .OrderBy(row => row.Id)
                .ToListAsync(ct);

            var derivedByLogId = await db.DerivedGymTrains
                .AsNoTracking()
                .ToDictionaryAsync(row => row.LogId, StringComparer.Ordinal, ct);

            var headerSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rawRows)
                try
                {
                    var keys = JsonFlattener.DiscoverKeys(row.RawJson);
                    foreach (var key in keys)
                        headerSet.Add(key);
                }
                catch (JsonException)
                {
                    // Skip malformed payloads for key discovery.
                }

            foreach (var col in CsvExportRunner.NormalizedColumns)
                headerSet.Add(col);

            var headerColumns = CsvExportRunner.BuildHeaderOrder(headerSet, true);

            GymModifierNormalizer.GymModifierTable? gymTable = null;
            {
                var ok = GymModifierNormalizer.GymModifierTable.TryLoadFromDefaultLocations(out gymTable,
                    out var loadError);
                if (!ok)
                    return new CsvExportRunner.ExportResult
                    {
                        Success = false,
                        ErrorMessage = loadError
                    };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath) ?? ".");

            using var writer = new StreamWriter(
                outputCsvPath,
                false,
                new UTF8Encoding(false));

            CsvWriter.WriteHeader(writer, headerColumns);

            var rowsWritten = 0;
            foreach (var row in rawRows)
                try
                {
                    var flat = JsonFlattener.Flatten(row.RawJson);
                    if (derivedByLogId.TryGetValue(row.LogId, out var derived))
                        EnrichWithDerived(flat, derived);

                    GymModifierNormalizer.ApplyNormalization(flat, gymTable);
                    CsvWriter.WriteRow(writer, headerColumns, flat);
                    rowsWritten++;
                }
                catch (JsonException)
                {
                    // Skip malformed payloads while preserving best-effort export.
                }

            return new CsvExportRunner.ExportResult
            {
                Success = true,
                OutputPath = outputCsvPath,
                HeaderColumns = headerColumns,
                RowsWritten = rowsWritten
            };
        }
        catch (Exception ex)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = $"Failed to write DB-backed CSV file '{outputCsvPath}': {ex.Message}"
            };
        }
    }

    public static async Task<CsvExportRunner.ExportResult> RunDebugAsync(
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

            var rawRows = await db.RawUserLogs
                .AsNoTracking()
                .OrderBy(row => row.Id)
                .ToListAsync(ct);

            var derivedByLogId = await db.DerivedGymTrains
                .AsNoTracking()
                .ToDictionaryAsync(row => row.LogId, StringComparer.Ordinal, ct);

            var happyEventsBySourceLogId = await db.DerivedHappyEvents
                .AsNoTracking()
                .Where(row => row.SourceLogId != null)
                .ToDictionaryAsync(row => row.SourceLogId!, StringComparer.Ordinal, ct);

            GymModifierNormalizer.GymModifierTable? gymTable = null;
            {
                var ok = GymModifierNormalizer.GymModifierTable.TryLoadFromDefaultLocations(out gymTable,
                    out var loadError);
                if (!ok)
                    return new CsvExportRunner.ExportResult
                    {
                        Success = false,
                        ErrorMessage = loadError
                    };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath) ?? ".");

            using var writer = new StreamWriter(
                outputCsvPath,
                false,
                new UTF8Encoding(false));

            CsvWriter.WriteHeader(writer, CsvExportRunner.DebugColumns);

            var rowsWritten = 0;
            foreach (var record in rawRows)
                try
                {
                    using var doc = JsonDocument.Parse(record.RawJson);
                    var root = doc.RootElement;

                    var row = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["id"] = record.LogId,
                        ["timestamp"] = record.OccurredAtUtc.ToUnixTimeSeconds().ToString()
                    };

                    if (TryGetPropertyIgnoreCase(root, "data", out var dataEl) &&
                        dataEl.ValueKind == JsonValueKind.Object)
                    {
                        row["data"] = JsonSerializer.Serialize(dataEl);

                        if (TryGetPropertyIgnoreCase(dataEl, "energy_used", out var energyEl) &&
                            TryGetValueAsString(energyEl, out var energyText))
                            row["data.energy_used"] = energyText;

                        if (TryGetPropertyIgnoreCase(dataEl, "gym", out var gymEl) &&
                            TryGetValueAsString(gymEl, out var gymText))
                            row["data.gym"] = gymText;

                        if (TryGetPropertyIgnoreCase(dataEl, "maximum_happy_after", out var maxAfterEl) &&
                            TryGetValueAsString(maxAfterEl, out var maxAfterText))
                            row["data.maximum_happy_after"] = maxAfterText;

                        if (TryGetPropertyIgnoreCase(dataEl, "maximum_happy_before", out var maxBeforeEl) &&
                            TryGetValueAsString(maxBeforeEl, out var maxBeforeText))
                            row["data.maximum_happy_before"] = maxBeforeText;

                        var hasAny = false;
                        var delta = 0;
                        if (TryGetPropertyIgnoreCase(dataEl, "happy_increased", out var incEl) &&
                            TryParseInt32(incEl, out var inc))
                        {
                            hasAny = true;
                            delta += inc;
                        }

                        if (TryGetPropertyIgnoreCase(dataEl, "happy_decreased", out var decEl) &&
                            TryParseInt32(decEl, out var dec))
                        {
                            hasAny = true;
                            delta -= dec;
                        }

                        if (hasAny)
                            row["data.happy_delta"] = delta.ToString();

                        ExtractStatFields(dataEl, gymTable, row);
                    }

                    if (happyEventsBySourceLogId.TryGetValue(record.LogId, out var happyEvent))
                    {
                        row["happy_before_event"] = happyEvent.HappyBeforeEvent?.ToString() ?? string.Empty;
                        row["happy_after_event"] = happyEvent.HappyAfterEvent?.ToString() ?? string.Empty;
                        row["event_type"] = happyEvent.EventType;
                    }

                    if (derivedByLogId.TryGetValue(record.LogId, out var derived))
                    {
                        row["regen_ticks_applied"] = derived.RegenTicksApplied.ToString();

                        if (!row.ContainsKey("happy_before_event"))
                            row["happy_before_event"] = derived.HappyBeforeTrain.ToString();
                        if (!row.ContainsKey("happy_after_event"))
                            row["happy_after_event"] = derived.HappyAfterTrain.ToString();
                        if (!row.ContainsKey("event_type"))
                            row["event_type"] = "gym_train";
                    }

                    CsvWriter.WriteRow(writer, CsvExportRunner.DebugColumns, row);
                    rowsWritten++;
                }
                catch (JsonException)
                {
                    // Skip malformed payloads.
                }

            return new CsvExportRunner.ExportResult
            {
                Success = true,
                OutputPath = outputCsvPath,
                HeaderColumns = CsvExportRunner.DebugColumns,
                RowsWritten = rowsWritten
            };
        }
        catch (Exception ex)
        {
            return new CsvExportRunner.ExportResult
            {
                Success = false,
                ErrorMessage = $"Failed to write DB-backed debug CSV file '{outputCsvPath}': {ex.Message}",
                HeaderColumns = CsvExportRunner.DebugColumns
            };
        }
    }

    private static void EnrichWithDerived(Dictionary<string, string> flat, DerivedGymTrainEntity derived)
    {
        flat["happy_before_train"] = derived.HappyBeforeTrain.ToString();
        flat["happy_after_train"] = derived.HappyAfterTrain.ToString();
        flat["regen_ticks_applied"] = derived.RegenTicksApplied.ToString();
        flat["regen_happy_gained"] = derived.RegenHappyGained.ToString();
        flat["max_happy_at_time_utc"] = derived.MaxHappyAtTimeUtc?.ToString() ?? string.Empty;
        flat["clamped_to_max"] = derived.ClampedToMax ? "true" : "false";
    }

    private static void ExtractStatFields(
        JsonElement dataEl,
        GymModifierNormalizer.GymModifierTable? gymTable,
        Dictionary<string, string> row)
    {
        foreach (var (type, key) in KnownStatTypes)
        {
            var hasAny = false;

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_before", out var beforeEl) &&
                TryGetValueAsString(beforeEl, out var beforeText))
            {
                row["stat_before"] = beforeText;
                hasAny = true;
            }

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_after", out var afterEl) &&
                TryGetValueAsString(afterEl, out var afterText))
            {
                row["stat_after"] = afterText;
                hasAny = true;
            }

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_increased", out var incEl) &&
                TryGetValueAsString(incEl, out var incText))
            {
                if (gymTable is not null &&
                    row.TryGetValue("data.gym", out var gymText) &&
                    int.TryParse(gymText, out var gymId) &&
                    double.TryParse(incText, NumberStyles.Float, CultureInfo.InvariantCulture, out var incRaw) &&
                    gymTable.TryGetMultiplier(gymId, key, out var mult) &&
                    mult > 0)
                    row["stat_increased"] = (incRaw / mult).ToString("0.###############", CultureInfo.InvariantCulture);
                else
                    row["stat_increased"] = incText;

                hasAny = true;
            }

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_decreased", out var decEl) &&
                TryGetValueAsString(decEl, out var decText))
            {
                row["stat_decreased"] = decText;
                hasAny = true;
            }

            if (hasAny)
            {
                row["stat_type"] = type;
                return;
            }
        }
    }

    private static bool TryParseInt32(JsonElement value, out int n)
    {
        n = 0;
        long? raw = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var i64) => i64,
            JsonValueKind.Number when value.TryGetDouble(out var d) => (long)d,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };

        if (raw is null || raw.Value is < int.MinValue or > int.MaxValue)
            return false;

        n = (int)raw.Value;
        return true;
    }

    private static bool TryGetValueAsString(JsonElement element, out string value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };

        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            if (obj.TryGetProperty(name, out value))
                return true;

            foreach (var prop in obj.EnumerateObject())
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
        }

        value = default;
        return false;
    }
}