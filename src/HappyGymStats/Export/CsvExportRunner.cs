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
