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

    public static ExtractResult Extract(IEnumerable<JsonlLogReader.LogRecord> records)
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

                        // Max happy detection: data.maximum_happy_after with known title patterns.
                        {
                            var parsed = TryExtractMaxHappy(dataEl, record.Title, out var maxHappy, out var maxHappyOutOfRange);
                            if (parsed && !maxHappyOutOfRange)
                            {
                                stats.MaxHappyEventsExtracted++;
                                yield return new MaxHappyEvent(
                                    LogId: record.LogId,
                                    OccurredAtUtc: record.OccurredAtUtc,
                                    MaxHappy: maxHappy);
                            }
                            else if (parsed && maxHappyOutOfRange)
                            {
                                stats.NumericOutOfRangeCount++;
                            }
                        }

                        // Happy delta detection: apply to any record reporting a direct happy change.
                        // Skip for gym trains to avoid double-counting in case Torn adds delta fields later.
                        if (!extractedGymTrain)
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

    private static bool TryExtractMaxHappy(JsonElement data, string? title, out int maxHappy, out bool outOfRange)
    {
        maxHappy = 0;
        outOfRange = false;

        // 1) Direct property: data.maximum_happy_after (used in Torn "Happy maximum increase/decrease" logs).
        if (TryGetPropertyIgnoreCase(data, "maximum_happy_after", out var maxAfterEl))
        {
            if (TryParseInt32Bounded(maxAfterEl, out maxHappy, out outOfRange))
                return !outOfRange;

            return false;
        }

        // 2) Property-name heuristic on any data key containing both "max" and "happy".
        foreach (var prop in data.EnumerateObject())
        {
            if (ContainsIgnoreCase(prop.Name, "max") && ContainsIgnoreCase(prop.Name, "happy"))
            {
                if (TryParseInt32Bounded(prop.Value, out maxHappy, out outOfRange))
                    return !outOfRange;

                return false;
            }
        }

        // 3) Title heuristic: if the title mentions max+happy, accept a single numeric value in data.
        if (!string.IsNullOrWhiteSpace(title)
            && ContainsIgnoreCase(title, "max")
            && ContainsIgnoreCase(title, "happy"))
        {
            int? candidate = null;

            foreach (var prop in data.EnumerateObject())
            {
                if (!TryParseInt32Bounded(prop.Value, out var n, out outOfRange))
                    continue;

                if (outOfRange)
                    return false;

                if (candidate is not null)
                {
                    // More than one numeric value => ambiguous, do not guess.
                    return false;
                }

                candidate = n;
            }

            if (candidate is not null)
            {
                maxHappy = candidate.Value;
                return true;
            }
        }

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
