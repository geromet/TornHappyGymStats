using System;
using System.Collections.Generic;
using System.Text.Json;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Reconstruction;

/// <summary>
/// Best-effort extractor that converts raw Torn user log JSON into typed reconstruction events.
/// </summary>
/// <remarks>
/// Extraction is defensive: schema varies between log categories and over time.
/// The extractor never throws for malformed records; it simply skips and updates stats.
/// </remarks>
public static class LogEventExtractor
{
    public sealed class ExtractionStats
    {
        public int RecordsSeen { get; internal set; }
        public int GymTrainEventsExtracted { get; internal set; }
        public int MaxHappyEventsExtracted { get; internal set; }
        public int HappyDeltaEventsExtracted { get; internal set; }
        public int JsonParseFailures { get; internal set; }
        public int MissingDetailsCount { get; internal set; }
        public int NumericOutOfRangeCount { get; internal set; }
    }

    public sealed record ExtractResult(
        IEnumerable<ReconstructionEvent> Events,
        ExtractionStats Stats);

    public static ExtractResult Extract(IEnumerable<ReconstructionLogRecord> records)
    {
        var stats = new ExtractionStats();

        IEnumerable<ReconstructionEvent> Iterator()
        {
            foreach (var record in records)
            {
                stats.RecordsSeen++;

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(record.RawJson);
                }
                catch (JsonException)
                {
                    // Should be rare (the reader already parses), but keep this layer defensive.
                    stats.JsonParseFailures++;
                    continue;
                }

                using (doc)
                {
                    var root = doc.RootElement;

                    // Gym train detection: data.happy_used is the primary signal.
                    if (TryGetPropertyIgnoreCase(root, "data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
                    {
                        var extractedGymTrain = false;

                        if (TryGetPropertyIgnoreCase(dataEl, "happy_used", out var happyUsedValue))
                        {
                            var parsed = TryParseInt32Bounded(happyUsedValue, out var happyUsed, out var happyUsedOutOfRange);

                            if (parsed && !happyUsedOutOfRange)
                            {
                                extractedGymTrain = true;
                                stats.GymTrainEventsExtracted++;
                                yield return new GymTrainEvent(
                                    LogId: record.LogId,
                                    OccurredAtUtc: record.OccurredAtUtc,
                                    HappyUsed: happyUsed);
                            }
                            else if (parsed && happyUsedOutOfRange)
                            {
                                stats.NumericOutOfRangeCount++;
                            }
                        }

                        // Max happy detection: prefer explicit before/after when present.
                        {
                            var parsed = TryExtractMaxHappyBeforeAfter(dataEl, record.Title, out var maxBefore, out var maxAfter, out var outOfRange);
                            if (parsed && !outOfRange)
                            {
                                stats.MaxHappyEventsExtracted++;
                                yield return new MaxHappyEvent(
                                    LogId: record.LogId,
                                    OccurredAtUtc: record.OccurredAtUtc,
                                    MaxHappyBefore: maxBefore,
                                    MaxHappyAfter: maxAfter);
                            }
                            else if (parsed && outOfRange)
                            {
                                stats.NumericOutOfRangeCount++;
                            }
                        }

                        var extractedOverdose = false;

                        // Overdose detection (anchor-capable).
                        // Title examples: "Item use ecstasy overdose".
                        if (!string.IsNullOrWhiteSpace(record.Title)
                            && record.Title.Contains("overdose", StringComparison.OrdinalIgnoreCase)
                            && TryGetPropertyIgnoreCase(dataEl, "happy_decreased", out var odDecEl))
                        {
                            var parsed = TryParseInt32Bounded(odDecEl, out var happyDecreased, out var odOutOfRange);
                            if (parsed && !odOutOfRange && happyDecreased > 0)
                            {
                                if (TryGetOverdosePercent(record.Title!, out var drugName, out var percentLoss))
                                {
                                    extractedOverdose = true;
                                    yield return new OverdoseEvent(
                                        LogId: record.LogId,
                                        OccurredAtUtc: record.OccurredAtUtc,
                                        DrugName: drugName,
                                        PercentLoss: percentLoss,
                                        HappyDecreased: happyDecreased);
                                }
                            }
                            else if (parsed && odOutOfRange)
                            {
                                stats.NumericOutOfRangeCount++;
                            }
                        }

                        // Happy delta detection: apply to any record reporting a direct happy change.
                        // Skip for gym trains to avoid double-counting in case Torn adds delta fields later.
                        // Skip for overdoses when we recognized the drug (handled separately as anchor).
                        if (!extractedGymTrain && !extractedOverdose)
                        {
                            var delta = 0;
                            var hasNonZeroDelta = false;

                            if (TryGetPropertyIgnoreCase(dataEl, "happy_increased", out var incEl))
                            {
                                var parsed = TryParseInt32Bounded(incEl, out var inc, out var incOutOfRange);
                                if (parsed && !incOutOfRange)
                                {
                                    if (inc != 0)
                                        hasNonZeroDelta = true;

                                    delta += inc;
                                }
                                else if (parsed && incOutOfRange)
                                {
                                    stats.NumericOutOfRangeCount++;
                                }
                            }

                            if (TryGetPropertyIgnoreCase(dataEl, "happy_decreased", out var decEl))
                            {
                                var parsed = TryParseInt32Bounded(decEl, out var dec, out var decOutOfRange);
                                if (parsed && !decOutOfRange)
                                {
                                    if (dec != 0)
                                        hasNonZeroDelta = true;

                                    delta -= dec;
                                }
                                else if (parsed && decOutOfRange)
                                {
                                    stats.NumericOutOfRangeCount++;
                                }
                            }

                            if (hasNonZeroDelta && delta != 0)
                            {
                                stats.HappyDeltaEventsExtracted++;
                                yield return new HappyDeltaEvent(
                                    LogId: record.LogId,
                                    OccurredAtUtc: record.OccurredAtUtc,
                                    Delta: delta);
                            }
                        }
                    }
                    else
                    {
                        stats.MissingDetailsCount++;
                    }
                }
            }
        }

        return new ExtractResult(
            Events: Iterator(),
            Stats: stats);
    }

    private static bool TryExtractMaxHappyBeforeAfter(
        JsonElement data,
        string? title,
        out int maxBefore,
        out int maxAfter,
        out bool outOfRange)
    {
        maxBefore = 0;
        maxAfter = 0;
        outOfRange = false;

        // 1) Preferred: explicit before/after properties.
        if (TryGetPropertyIgnoreCase(data, "maximum_happy_after", out var maxAfterEl))
        {
            if (!TryParseInt32Bounded(maxAfterEl, out maxAfter, out outOfRange))
                return false;

            if (outOfRange)
                return true;

            if (TryGetPropertyIgnoreCase(data, "maximum_happy_before", out var maxBeforeEl))
            {
                if (!TryParseInt32Bounded(maxBeforeEl, out maxBefore, out outOfRange))
                    return false;

                if (outOfRange)
                    return true;

                return true;
            }

            // If before is missing, fall back to "before == after".
            maxBefore = maxAfter;
            return true;
        }

        // 2) Title heuristic: if the title mentions max+happy, accept 1-2 numeric values.
        if (!string.IsNullOrWhiteSpace(title)
            && ContainsIgnoreCase(title, "max")
            && ContainsIgnoreCase(title, "happy"))
        {
            var numbers = new List<int>(capacity: 2);

            foreach (var prop in data.EnumerateObject())
            {
                if (!TryParseInt32Bounded(prop.Value, out var n, out outOfRange))
                    continue;

                if (outOfRange)
                    return true;

                numbers.Add(n);
                if (numbers.Count >= 2)
                    break;
            }

            if (numbers.Count == 1)
            {
                maxBefore = numbers[0];
                maxAfter = numbers[0];
                return true;
            }

            if (numbers.Count == 2)
            {
                maxBefore = numbers[0];
                maxAfter = numbers[1];
                return true;
            }
        }

        return false;
    }

    private static bool TryGetOverdosePercent(string title, out string drugName, out double percentLoss)
    {
        drugName = string.Empty;
        percentLoss = 0;

        // Known overdose rules (user-provided):
        // Ecstasy 100%, Ketamine 100%, LSD 50%, PCP 100%, Shrooms 100%, Speed 100%, Xanax 100%.
        if (!title.Contains("overdose", StringComparison.OrdinalIgnoreCase))
            return false;

        var t = title.ToLowerInvariant();

        if (t.Contains("ecstasy")) { drugName = "ecstasy"; percentLoss = 1.0; return true; }
        if (t.Contains("ketamine")) { drugName = "ketamine"; percentLoss = 1.0; return true; }
        if (t.Contains("lsd")) { drugName = "lsd"; percentLoss = 0.5; return true; }
        if (t.Contains("pcp")) { drugName = "pcp"; percentLoss = 1.0; return true; }
        if (t.Contains("shrooms") || t.Contains("mushroom")) { drugName = "shrooms"; percentLoss = 1.0; return true; }
        if (t.Contains("speed")) { drugName = "speed"; percentLoss = 1.0; return true; }
        if (t.Contains("xanax")) { drugName = "xanax"; percentLoss = 1.0; return true; }

        return false;
    }

    private static bool TryParseInt32Bounded(JsonElement value, out int n, out bool outOfRange)
    {
        n = 0;
        outOfRange = false;

        long? raw = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var i64) => i64,
            JsonValueKind.Number when value.TryGetDouble(out var d) => (long)d,
            JsonValueKind.String when long.TryParse(value.GetString(), out var s) => s,
            _ => null,
        };

        if (raw is null)
            return false;

        if (raw.Value is < int.MinValue or > int.MaxValue)
        {
            outOfRange = true;
            return true;
        }

        n = (int)raw.Value;
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

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
