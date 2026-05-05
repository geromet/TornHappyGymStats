using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// Best-effort extractor that converts typed Torn user log records into typed reconstruction events.
/// </summary>
/// <remarks>
/// Extraction is defensive: the extractor never throws for records that cannot be processed;
/// it simply skips and updates stats.
/// </remarks>
public static class LogEventExtractor
{
    public sealed class ExtractionStats
    {
        public int RecordsSeen { get; internal set; }
        public int GymTrainEventsExtracted { get; internal set; }
        public int MaxHappyEventsExtracted { get; internal set; }
        public int HappyDeltaEventsExtracted { get; internal set; }
        public int SkippedCount { get; internal set; }
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

                // Gym train detection: HappyUsed is the primary signal.
                if (record.HappyUsed != null)
                {
                    var happyUsed = record.HappyUsed.Value;
                    stats.GymTrainEventsExtracted++;
                    yield return new GymTrainEvent(
                        LogId: record.LogId,
                        OccurredAtUtc: record.OccurredAtUtc,
                        HappyUsed: happyUsed);

                    // Gym trains skip happy delta processing below.
                    // Still check for max-happy.
                    if (record.MaxHappyAfter != null)
                    {
                        var maxAfter = record.MaxHappyAfter.Value;
                        var maxBefore = record.MaxHappyBefore ?? maxAfter;
                        stats.MaxHappyEventsExtracted++;
                        yield return new MaxHappyEvent(
                            LogId: record.LogId,
                            OccurredAtUtc: record.OccurredAtUtc,
                            MaxHappyBefore: maxBefore,
                            MaxHappyAfter: maxAfter);
                    }

                    continue;
                }

                // Max happy detection.
                if (record.MaxHappyAfter != null)
                {
                    var maxAfter = record.MaxHappyAfter.Value;
                    var maxBefore = record.MaxHappyBefore ?? maxAfter;
                    stats.MaxHappyEventsExtracted++;
                    yield return new MaxHappyEvent(
                        LogId: record.LogId,
                        OccurredAtUtc: record.OccurredAtUtc,
                        MaxHappyBefore: maxBefore,
                        MaxHappyAfter: maxAfter);
                    continue;
                }

                // Overdose detection (anchor-capable).
                if (!string.IsNullOrWhiteSpace(record.Title)
                    && record.Title.Contains("overdose", StringComparison.OrdinalIgnoreCase)
                    && record.HappyDecreased != null)
                {
                    var happyDecreased = record.HappyDecreased.Value;
                    if (happyDecreased > 0 && TryGetOverdosePercent(record.Title!, out var drugName, out var percentLoss))
                    {
                        yield return new OverdoseEvent(
                            LogId: record.LogId,
                            OccurredAtUtc: record.OccurredAtUtc,
                            DrugName: drugName,
                            PercentLoss: percentLoss,
                            HappyDecreased: happyDecreased);
                        continue;
                    }
                }

                // Happy delta detection: apply to any record reporting a direct happy change.
                {
                    var increased = record.HappyIncreased ?? 0;
                    var decreased = record.HappyDecreased ?? 0;

                    if (increased != 0 || decreased != 0)
                    {
                        var delta = increased - decreased;
                        if (delta != 0)
                        {
                            stats.HappyDeltaEventsExtracted++;
                            yield return new HappyDeltaEvent(
                                LogId: record.LogId,
                                OccurredAtUtc: record.OccurredAtUtc,
                                Delta: delta);
                        }
                        else
                        {
                            stats.SkippedCount++;
                        }
                    }
                    else
                    {
                        stats.SkippedCount++;
                    }
                }
            }
        }

        return new ExtractResult(
            Events: Iterator(),
            Stats: stats);
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
}
