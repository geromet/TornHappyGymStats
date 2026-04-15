using System.Globalization;
using System.Text;
using System.Text.Json;
using HappyGymStats.Reconstruction;

using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Export;

/// <summary>
/// 2-pass CSV export runner:
/// <list type="number">
///   <item>Pass 1: stream JSONL to discover the union-of-keys header set.</item>
///   <item>Pass 2: stream JSONL again, flatten each record, and write matching CSV rows.</item>
/// </list>
/// Only the header set is held in memory; rows are not buffered.
/// Header order is deterministic: <c>id, timestamp, title, category</c> first (when present),
/// then remaining columns sorted ordinally, then derived columns in a fixed order.
/// Derived gym-train sidecar is optional — missing file produces blank derived columns.
/// </summary>
public static class CsvExportRunner
{
    /// <summary>
    /// Canonical column order for well-known top-level fields.
    /// In the real Torn API response, title and category are nested inside "details",
    /// so only id and timestamp appear as top-level keys.
    /// </summary>
    private static readonly string[] CanonicalPrefix = { "id", "timestamp" };

    /// <summary>
    /// Fixed-order derived column names appended at the end of every CSV header.
    /// </summary>
    public static readonly string[] DerivedColumns =
    {
        "happy_before_train",
        "happy_after_train",
        "regen_ticks_applied",
        "regen_happy_gained",
        "max_happy_at_time_utc",
        "clamped_to_max"
    };

    /// <summary>
    /// Computed columns that do not exist in the raw Torn log payload but are added during export.
    /// </summary>
    public static readonly string[] NormalizedColumns =
    {
        "data.strength_increased_normalized",
        "data.defense_increased_normalized",
        "data.speed_increased_normalized",
        "data.dexterity_increased_normalized",
    };

    /// <summary>
    /// Minimal, fixed-schema CSV intended for debugging reconstruction/surfaces.
    /// </summary>
    public static readonly string[] DebugColumns =
    {
        "id",
        "timestamp",
        "data",
        "data.energy_used",
        "data.gym",
        "data.maximum_happy_after",
        "data.maximum_happy_before",
        "data.happy_delta",
        "stat_type",
        "stat_before",
        "stat_after",
        "stat_increased",
        "stat_decreased",
        "happy_before_train",
        "happy_after_train",
        "regen_ticks_applied",
    };

    public sealed class ExportResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? OutputPath { get; init; }
        public IReadOnlyList<string> HeaderColumns { get; init; } = Array.Empty<string>();
        public int RowsWritten { get; init; }
        public JsonlLogReader.ReadStats? ReaderStats { get; init; }

        /// <summary>
        /// When true, the derived sidecar file was not found. Export succeeded with blank derived columns.
        /// </summary>
        public bool DerivedFileMissing { get; init; }

        /// <summary>
        /// Number of malformed lines skipped in the derived sidecar file.
        /// </summary>
        public int DerivedMalformedLines { get; init; }
    }

    /// <summary>
    /// Run the 2-pass CSV export without a derived sidecar.
    /// </summary>
    /// <param name="logsJsonlPath">Path to the source JSONL file.</param>
    /// <param name="outputCsvPath">Path for the output CSV file.</param>
    public static ExportResult Run(string logsJsonlPath, string outputCsvPath)
        => Run(logsJsonlPath, outputCsvPath, derivedJsonlPath: null);

    /// <summary>
    /// Run the 2-pass CSV export with an optional derived gym-train sidecar.
    /// </summary>
    /// <param name="logsJsonlPath">Path to the source JSONL file.</param>
    /// <param name="outputCsvPath">Path for the output CSV file.</param>
    /// <param name="derivedJsonlPath">Optional path to the derived gym-train JSONL sidecar.</param>
    public static ExportResult Run(string logsJsonlPath, string outputCsvPath, string? derivedJsonlPath)
    {
        // --- Load derived sidecar (optional) ---
        var derivedRead = derivedJsonlPath is not null
            ? DerivedGymTrainReader.Read(derivedJsonlPath)
            : null;

        if (derivedRead?.ErrorMessage is not null)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = derivedRead.ErrorMessage,
            };
        }

        // --- Pass 1: discover union-of-keys ---
        var firstRead = JsonlLogReader.Read(logsJsonlPath);
        if (!firstRead.Success)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = firstRead.ErrorMessage
            };
        }

        var headerSet = new HashSet<string>(StringComparer.Ordinal);
        var pass1Records = firstRead.Records.ToList(); // materialize once for pass 1

        foreach (var record in pass1Records)
        {
            try
            {
                var keys = JsonFlattener.DiscoverKeys(record.RawJson);
                foreach (var key in keys)
                {
                    headerSet.Add(key);
                }
            }
            catch (JsonException)
            {
                // Skip records whose raw JSON cannot be parsed for key discovery.
            }
        }

        // Add computed columns that don't exist in the raw payload.
        foreach (var col in NormalizedColumns)
        {
            headerSet.Add(col);
        }

        // Deterministic header order: canonical prefix first, then remaining sorted ordinally,
        // then derived columns in fixed order (always appended).
        var headerColumns = BuildHeaderOrder(headerSet, includeDerived: true);

        // --- Gym modifier normalization (optional) ---
        // If gyms.json is present (either in the working directory or alongside the executable),
        // we normalize stat increased values by dividing out the gym multiplier.
        // This makes cross-gym comparisons meaningful (the gym modifier is just a multiplicative factor).
        GymModifierNormalizer.GymModifierTable? gymTable = null;
        {
            var ok = GymModifierNormalizer.GymModifierTable.TryLoadFromDefaultLocations(out gymTable, out var loadError);
            if (!ok)
            {
                return new ExportResult
                {
                    Success = false,
                    ErrorMessage = loadError,
                };
            }
        }

        // --- Pass 2: write CSV rows ---
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath)!);
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = $"Unable to create output directory for '{outputCsvPath}': {ex.Message}"
            };
        }

        int rowsWritten;
        try
        {
            using var writer = new StreamWriter(
                outputCsvPath,
                append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            CsvWriter.WriteHeader(writer, headerColumns);

            rowsWritten = 0;
            foreach (var record in pass1Records)
            {
                try
                {
                    var flat = JsonFlattener.Flatten(record.RawJson);

                    // Enrich with derived columns if a matching derived record exists.
                    if (derivedRead is not null &&
                        flat.TryGetValue("id", out var idText) &&
                        !string.IsNullOrEmpty(idText) &&
                        derivedRead.Records.TryGetValue(idText, out var derived))
                    {
                        EnrichWithDerived(flat, derived);
                    }

                    // Normalize stat increased values by gym multiplier (if gyms.json is available).
                    GymModifierNormalizer.ApplyNormalization(flat, gymTable);

                    CsvWriter.WriteRow(writer, headerColumns, flat);
                    rowsWritten++;
                }
                catch (JsonException)
                {
                    // Skip records that fail flattening (already counted as malformed by reader).
                }
            }
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = $"Failed to write CSV file '{outputCsvPath}': {ex.Message}",
                HeaderColumns = headerColumns,
                ReaderStats = firstRead.Stats
            };
        }

        return new ExportResult
        {
            Success = true,
            ErrorMessage = null,
            OutputPath = outputCsvPath,
            HeaderColumns = headerColumns,
            RowsWritten = rowsWritten,
            ReaderStats = firstRead.Stats,
            DerivedFileMissing = derivedRead?.FileMissing ?? true,
            DerivedMalformedLines = derivedRead?.MalformedLines ?? 0,
        };
    }

    /// <summary>
    /// Run a fixed-schema "debug" CSV export intended for easier inspection.
    /// </summary>
    public static ExportResult RunDebug(string logsJsonlPath, string outputCsvPath, string? derivedJsonlPath)
    {
        // --- Load derived sidecar (optional) ---
        var derivedRead = derivedJsonlPath is not null
            ? DerivedGymTrainReader.Read(derivedJsonlPath)
            : null;

        if (derivedRead?.ErrorMessage is not null)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = derivedRead.ErrorMessage,
            };
        }

        var read = JsonlLogReader.Read(logsJsonlPath);
        if (!read.Success)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = read.ErrorMessage,
            };
        }

        // --- Gym modifier normalization (optional) ---
        GymModifierNormalizer.GymModifierTable? gymTable = null;
        {
            var ok = GymModifierNormalizer.GymModifierTable.TryLoadFromDefaultLocations(out gymTable, out var loadError);
            if (!ok)
            {
                return new ExportResult
                {
                    Success = false,
                    ErrorMessage = loadError,
                };
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputCsvPath)!);
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = $"Unable to create output directory for '{outputCsvPath}': {ex.Message}",
            };
        }

        int rowsWritten;
        try
        {
            using var writer = new StreamWriter(
                outputCsvPath,
                append: false,
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            CsvWriter.WriteHeader(writer, DebugColumns);

            rowsWritten = 0;

            foreach (var record in read.Records)
            {
                try
                {
                    using var doc = JsonDocument.Parse(record.RawJson);
                    var root = doc.RootElement;

                    var row = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["id"] = record.LogId,
                        ["timestamp"] = record.OccurredAtUtc.ToUnixTimeSeconds().ToString(),
                    };

                    if (TryGetPropertyIgnoreCase(root, "data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
                    {
                        row["data"] = JsonSerializer.Serialize(dataEl);

                        if (TryGetPropertyIgnoreCase(dataEl, "energy_used", out var energyEl) && TryGetValueAsString(energyEl, out var energyText))
                            row["data.energy_used"] = energyText;

                        if (TryGetPropertyIgnoreCase(dataEl, "gym", out var gymEl) && TryGetValueAsString(gymEl, out var gymText))
                            row["data.gym"] = gymText;

                        if (TryGetPropertyIgnoreCase(dataEl, "maximum_happy_after", out var maxAfterEl) && TryGetValueAsString(maxAfterEl, out var maxAfterText))
                            row["data.maximum_happy_after"] = maxAfterText;

                        if (TryGetPropertyIgnoreCase(dataEl, "maximum_happy_before", out var maxBeforeEl) && TryGetValueAsString(maxBeforeEl, out var maxBeforeText))
                            row["data.maximum_happy_before"] = maxBeforeText;

                        // Combine happy_increased and happy_decreased into a single signed delta column.
                        // Convention: positive = increased; negative = decreased.
                        {
                            var hasAny = false;
                            var delta = 0;

                            if (TryGetPropertyIgnoreCase(dataEl, "happy_increased", out var incEl) && TryParseInt32(incEl, out var inc))
                            {
                                hasAny = true;
                                delta += inc;
                            }

                            if (TryGetPropertyIgnoreCase(dataEl, "happy_decreased", out var decEl) && TryParseInt32(decEl, out var dec))
                            {
                                hasAny = true;
                                delta -= dec;
                            }

                            if (hasAny)
                                row["data.happy_delta"] = delta.ToString();
                        }

                        // Stat field simplification.
                        ExtractStatFields(dataEl, gymTable, row);
                    }

                    // Join derived fields (if present).
                    if (derivedRead is not null && derivedRead.Records.TryGetValue(record.LogId, out var derived))
                    {
                        row["happy_before_train"] = derived.HappyBeforeTrain.ToString();
                        row["happy_after_train"] = derived.HappyAfterTrain.ToString();
                        row["regen_ticks_applied"] = derived.RegenTicksApplied.ToString();
                    }

                    CsvWriter.WriteRow(writer, DebugColumns, row);
                    rowsWritten++;
                }
                catch (JsonException)
                {
                    // Skip malformed records.
                }
            }
        }
        catch (Exception ex)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = $"Failed to write CSV file '{outputCsvPath}': {ex.Message}",
                HeaderColumns = DebugColumns,
                ReaderStats = read.Stats,
            };
        }

        return new ExportResult
        {
            Success = true,
            ErrorMessage = null,
            OutputPath = outputCsvPath,
            HeaderColumns = DebugColumns,
            RowsWritten = rowsWritten,
            ReaderStats = read.Stats,
            DerivedFileMissing = derivedRead?.FileMissing ?? true,
            DerivedMalformedLines = derivedRead?.MalformedLines ?? 0,
        };
    }

    /// <summary>
    /// Build a deterministic header order: canonical prefix columns first, then remaining sorted ordinally.
    /// When <paramref name="includeDerived"/> is true, appends derived columns in fixed order.
    /// </summary>
    public static List<string> BuildHeaderOrder(HashSet<string> headerSet, bool includeDerived = false)
    {
        var result = new List<string>(headerSet.Count + DerivedColumns.Length + NormalizedColumns.Length);
        var remaining = new HashSet<string>(headerSet, StringComparer.Ordinal);

        // Add canonical prefix columns in fixed order (if present).
        foreach (var col in CanonicalPrefix)
        {
            if (remaining.Remove(col))
            {
                result.Add(col);
            }
        }

        // Sort remaining columns ordinally for stable cross-run ordering.
        var sorted = new List<string>(remaining);
        sorted.Sort(StringComparer.Ordinal);

        result.AddRange(sorted);

        // Append derived columns in fixed order (always present, values may be blank).
        if (includeDerived)
        {
            result.AddRange(DerivedColumns);
        }

        return result;
    }

    private static readonly (string Type, string Key)[] KnownStatTypes =
    {
        ("strength", "strength"),
        ("defense", "defense"),
        ("speed", "speed"),
        ("dexterity", "dexterity"),
    };

    private static void ExtractStatFields(
        JsonElement dataEl,
        GymModifierNormalizer.GymModifierTable? gymTable,
        Dictionary<string, string> row)
    {
        foreach (var (type, key) in KnownStatTypes)
        {
            var hasAny = false;

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_before", out var beforeEl) && TryGetValueAsString(beforeEl, out var beforeText))
            {
                row["stat_before"] = beforeText;
                hasAny = true;
            }

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_after", out var afterEl) && TryGetValueAsString(afterEl, out var afterText))
            {
                row["stat_after"] = afterText;
                hasAny = true;
            }

            // For gym trains, *_increased is the common pattern.
            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_increased", out var incEl) && TryGetValueAsString(incEl, out var incText))
            {
                // Normalize by gym multiplier when available.
                if (gymTable is not null &&
                    row.TryGetValue("data.gym", out var gymText) &&
                    int.TryParse(gymText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gymId) &&
                    double.TryParse(incText, NumberStyles.Float, CultureInfo.InvariantCulture, out var incRaw) &&
                    gymTable.TryGetMultiplier(gymId, key, out var mult) &&
                    mult > 0)
                {
                    var normalized = incRaw / mult;
                    row["stat_increased"] = normalized.ToString("0.###############", CultureInfo.InvariantCulture);
                }
                else
                {
                    row["stat_increased"] = incText;
                }

                hasAny = true;
            }

            if (TryGetPropertyIgnoreCase(dataEl, $"{key}_decreased", out var decEl) && TryGetValueAsString(decEl, out var decText))
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
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null,
        };

        if (raw is null)
            return false;

        if (raw.Value is < int.MinValue or > int.MaxValue)
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
            _ => element.GetRawText(),
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
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Add derived column values to a flattened row dictionary.
    /// </summary>
    private static void EnrichWithDerived(Dictionary<string, string> flat, DerivedGymTrain derived)
    { 
        flat["happy_before_train"] = derived.HappyBeforeTrain.ToString();
        flat["happy_after_train"] = derived.HappyAfterTrain.ToString();
        flat["regen_ticks_applied"] = derived.RegenTicksApplied.ToString();
        flat["regen_happy_gained"] = derived.RegenHappyGained.ToString();
        flat["max_happy_at_time_utc"] = derived.MaxHappyAtTimeUtc?.ToString() ?? string.Empty;
        flat["clamped_to_max"] = derived.ClampedToMax ? "true" : "false";
    }
}
