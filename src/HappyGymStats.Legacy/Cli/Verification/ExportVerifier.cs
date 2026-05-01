using System.Globalization;
using System.Text.Json;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Legacy.Cli.Export;
using HappyGymStats.Visualizer;

namespace HappyGymStats.Legacy.Cli.Verification;

public static class ExportVerifier
{
    public static VerifyReport Verify(VerifyOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.CsvPath))
            throw new ArgumentException("CsvPath is required", nameof(opt));

        // Read stat rows via the Visualizer parser (this defines what the charts use).
        var detailed = CsvStatReader.readStatRecordsDetailed(opt.CsvPath);
        var statRows = detailed.Records;

        // Optional: build JSONL lookup
        Dictionary<string, JsonlLogReader.LogRecord>? jsonlById = null;
        if (!string.IsNullOrWhiteSpace(opt.LogsJsonlPath))
        {
            var read = JsonlLogReader.Read(opt.LogsJsonlPath!);
            if (!read.Success)
                throw new InvalidOperationException(read.ErrorMessage ?? "Unable to read JSONL");

            jsonlById = read.Records.ToDictionary(r => r.LogId, StringComparer.Ordinal);
        }

        // Optional: derived gym trains lookup
        Dictionary<string, HappyReconstructionModels.DerivedGymTrain>? derivedById = null;
        if (!string.IsNullOrWhiteSpace(opt.DerivedGymTrainsJsonlPath))
        {
            var derivedRead = DerivedGymTrainReader.Read(opt.DerivedGymTrainsJsonlPath!);
            if (derivedRead.ErrorMessage is not null)
                throw new InvalidOperationException(derivedRead.ErrorMessage);

            derivedById = derivedRead.Records;
        }

        // Optional: gyms.json normalization table (if available). Used to validate *_increased_normalized columns.
        GymModifierNormalizer.GymModifierTable? gymTable = null;
        {
            var ok = GymModifierNormalizer.GymModifierTable.TryLoadFromDefaultLocations(out gymTable, out _);
            if (!ok)
                gymTable = null;
        }


        // Read the CSV itself so we can compare raw fields (not just what the Visualizer extracted).
        var csvLines = File.ReadAllLines(opt.CsvPath);
        if (csvLines.Length == 0)
            throw new InvalidOperationException($"CSV file is empty: {opt.CsvPath}");

        var headers = CsvStatReader.parseCsvLine(csvLines[0]);
        var idx = headers
            .Select((h, i) => (Name: h.Trim(), Index: i))
            .ToDictionary(t => t.Name, t => t.Index, StringComparer.Ordinal);

        int? TryCol(string name)
        {
            return idx.TryGetValue(name, out var i) ? i : null;
        }

        static double? TryParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        // Helpers for stat columns.
        static string StatBeforeCol(StatType st)
        {
            return st.Tag switch
            {
                0 => "data.strength_before",
                1 => "data.defense_before",
                2 => "data.speed_before",
                3 => "data.dexterity_before",
                _ => throw new ArgumentOutOfRangeException(nameof(st))
            };
        }

        static (string norm, string raw) StatIncreasedCols(StatType st)
        {
            return st.Tag switch
            {
                0 => ("data.strength_increased_normalized", "data.strength_increased"),
                1 => ("data.defense_increased_normalized", "data.defense_increased"),
                2 => ("data.speed_increased_normalized", "data.speed_increased"),
                3 => ("data.dexterity_increased_normalized", "data.dexterity_increased"),
                _ => throw new ArgumentOutOfRangeException(nameof(st))
            };
        }

        static string StatKey(StatType st)
        {
            return st.Tag switch
            {
                0 => "strength",
                1 => "defense",
                2 => "speed",
                3 => "dexterity",
                _ => throw new ArgumentOutOfRangeException(nameof(st))
            };
        }

        var mismatches = new List<Mismatch>();

        var jsonlFound = 0;
        var derivedFound = 0;

        // Compute top outliers (by stat gained per energy) directly from the extracted records.
        var outlierLines = statRows
            .Select(r =>
            {
                var perEnergy = r.EnergyUsed > 0.0 ? r.StatIncreased / r.EnergyUsed : double.NaN;
                return (r, perEnergy);
            })
            .OrderByDescending(t => t.perEnergy)
            .Take(Math.Max(0, opt.TopOutliers))
            .Select(t =>
            {
                var r = t.r;
                return
                    $"id={r.LogId} stat={r.StatType} before={r.StatBefore.ToString(CultureInfo.InvariantCulture)} happy={r.HappyBeforeTrain.ToString(CultureInfo.InvariantCulture)} increased={r.StatIncreased.ToString(CultureInfo.InvariantCulture)} energy={r.EnergyUsed.ToString(CultureInfo.InvariantCulture)} perEnergy={t.perEnergy.ToString("0.####", CultureInfo.InvariantCulture)}";
            })
            .ToList();

        // For each extracted record, verify CSV ↔ JSONL raw values, and CSV ↔ derived-happy values.
        // We locate CSV values by id (log id is unique).
        // Note: this is O(n) in rows because statRows is typically much smaller than total logs.
        var csvById = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var i = 1; i < csvLines.Length; i++)
        {
            var fields = CsvStatReader.parseCsvLine(csvLines[i]);
            var idCol = TryCol("id");
            if (idCol is null || idCol.Value >= fields.Length) continue;
            var id = fields[idCol.Value].Trim();
            if (string.IsNullOrWhiteSpace(id)) continue;
            csvById[id] = fields;
        }

        foreach (var r in statRows)
        {
            if (!csvById.TryGetValue(r.LogId, out var fields))
            {
                mismatches.Add(new Mismatch(r.LogId, "csv_row", "present", "missing"));
                continue;
            }

            string Field(int? col)
            {
                return col is null || col.Value >= fields.Length ? "" : fields[col.Value].Trim();
            }

            // Verify derived happy join.
            if (derivedById is not null)
            {
                if (derivedById.TryGetValue(r.LogId, out var d))
                {
                    derivedFound++;
                    var csvHappyText = Field(TryCol("happy_before_train"));
                    var csvHappy = TryParseDouble(csvHappyText);
                    if (csvHappy is not null && Math.Abs(csvHappy.Value - d.HappyBeforeTrain) > 0.0001)
                        mismatches.Add(new Mismatch(r.LogId, "happy_before_train",
                            d.HappyBeforeTrain.ToString(CultureInfo.InvariantCulture), csvHappyText));
                }
                else
                {
                    mismatches.Add(new Mismatch(r.LogId, "derived_gymtrain", "present", "missing"));
                }
            }

            // Verify raw JSONL payload fields.
            if (jsonlById is not null)
            {
                if (!jsonlById.TryGetValue(r.LogId, out var jr))
                {
                    mismatches.Add(new Mismatch(r.LogId, "jsonl_row", "present", "missing"));
                    continue;
                }

                jsonlFound++;

                using var doc = JsonDocument.Parse(jr.RawJson);
                var root = doc.RootElement;

                if (!TryGetPropertyIgnoreCase(root, "data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
                {
                    mismatches.Add(new Mismatch(r.LogId, "jsonl.data", "object", dataEl.ValueKind.ToString()));
                    continue;
                }

                // energy_used
                var csvEnergyText = Field(TryCol("data.energy_used"));
                var csvEnergy = TryParseDouble(csvEnergyText);
                var jsonEnergy = TryGetNumber(dataEl, "energy_used");
                if (csvEnergy is not null && jsonEnergy is not null &&
                    Math.Abs(csvEnergy.Value - jsonEnergy.Value) > 0.0001)
                    mismatches.Add(new Mismatch(r.LogId, "data.energy_used",
                        jsonEnergy.Value.ToString(CultureInfo.InvariantCulture), csvEnergyText));

                // stat_before
                var beforeCol = StatBeforeCol(r.StatType);
                var csvBeforeText = Field(TryCol(beforeCol));
                var csvBefore = TryParseDouble(csvBeforeText);
                var jsonBefore = TryGetNumber(dataEl, beforeCol[5..]); // strip "data."
                if (csvBefore is not null && jsonBefore is not null &&
                    Math.Abs(csvBefore.Value - jsonBefore.Value) > 0.0001)
                    mismatches.Add(new Mismatch(r.LogId, beforeCol,
                        jsonBefore.Value.ToString(CultureInfo.InvariantCulture), csvBeforeText));

                // stat_increased RAW (not normalized) should match JSONL.
                var (normCol, rawCol) = StatIncreasedCols(r.StatType);
                var csvIncRawText = Field(TryCol(rawCol));
                var csvIncRaw = TryParseDouble(csvIncRawText);
                var jsonIncRaw = TryGetNumber(dataEl, rawCol[5..]);
                if (csvIncRaw is not null && jsonIncRaw is not null &&
                    Math.Abs(csvIncRaw.Value - jsonIncRaw.Value) > 0.0001)
                    mismatches.Add(new Mismatch(r.LogId, rawCol,
                        jsonIncRaw.Value.ToString(CultureInfo.InvariantCulture), csvIncRawText));

                // If a normalized increased column exists, validate it against gyms.json multiplier.
                // This catches cases where normalization could introduce spikes.
                if (gymTable is not null)
                {
                    var csvGymText = Field(TryCol("data.gym"));
                    if (int.TryParse(csvGymText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gymId))
                        if (gymTable.TryGetMultiplier(gymId, StatKey(r.StatType), out var mult) && mult > 0)
                        {
                            var csvNormText = Field(TryCol(normCol));
                            var csvNorm = TryParseDouble(csvNormText);

                            if (csvNorm is not null && csvIncRaw is not null)
                            {
                                var expected = csvIncRaw.Value / mult;
                                if (Math.Abs(csvNorm.Value - expected) > 0.000001)
                                    mismatches.Add(new Mismatch(r.LogId, normCol,
                                        expected.ToString(CultureInfo.InvariantCulture), csvNormText));
                            }
                        }
                }
            }
        }

        // Verify Surfaces.html contains the raw all-stats point cloud generated from CSV rows.
        var visualizationMatches = true;
        if (!string.IsNullOrWhiteSpace(opt.SurfacesHtmlPath))
        {
            var html = File.ReadAllText(opt.SurfacesHtmlPath!);
            var rawSampleCount = TryExtractFirstRawSampleCount(html);
            visualizationMatches = rawSampleCount == statRows.Count(r => r.EnergyUsed > 0.0);
        }

        var statRowCount = statRows.Count();

        return new VerifyReport(
            statRowCount,
            jsonlFound,
            derivedFound,
            mismatches.Count,
            mismatches.Take(25).ToList(),
            outlierLines,
            visualizationMatches);
    }

    private static double? TryGetNumber(JsonElement obj, string prop)
    {
        if (!TryGetPropertyIgnoreCase(obj, prop, out var el))
            return null;

        if (el.ValueKind == JsonValueKind.Number)
            return el.TryGetDouble(out var d) ? d : null;

        if (el.ValueKind == JsonValueKind.String)
        {
            var s = el.GetString();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
        }

        return null;
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

    private static int? TryExtractFirstRawSampleCount(string html)
    {
        const string marker = "Plotly.newPlot(";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;

        var start = html.IndexOf('[', idx);
        if (start < 0) return null;

        var (jsonArray, _) = ExtractJsonArray(html, start);
        if (jsonArray is null) return null;

        using var doc = JsonDocument.Parse(jsonArray);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return null;

        var trace = doc.RootElement[0];
        if (!trace.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "scatter3d")
            return null;

        if (!trace.TryGetProperty("name", out var nameEl) || nameEl.GetString() != "Raw samples")
            return null;

        return trace.TryGetProperty("x", out var xEl) && xEl.ValueKind == JsonValueKind.Array
            ? xEl.GetArrayLength()
            : null;
    }

    private static (string? json, int endIndex) ExtractJsonArray(string s, int startIndex)
    {
        // Extract a JSON array from a string, handling nested arrays/objects and string escapes.
        if (startIndex < 0 || startIndex >= s.Length || s[startIndex] != '[')
            return (null, -1);

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < s.Length; i++)
        {
            var ch = s[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"') inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '[') depth++;
            if (ch == ']')
            {
                depth--;
                if (depth == 0)
                {
                    var json = s.Substring(startIndex, i - startIndex + 1);
                    return (json, i);
                }
            }
        }

        return (null, -1);
    }

    public sealed record VerifyOptions(
        string CsvPath,
        string? LogsJsonlPath,
        string? DerivedGymTrainsJsonlPath,
        string? SurfacesHtmlPath,
        int TopOutliers);

    public sealed record Mismatch(string LogId, string Field, string Expected, string Actual);

    public sealed record VerifyReport(
        int StatRows,
        int JsonlRowsFound,
        int DerivedRowsFound,
        int Mismatches,
        IReadOnlyList<Mismatch> SampleMismatches,
        IReadOnlyList<string> TopOutlierLines,
        bool VisualizationHtmlMatchesCsv);
}