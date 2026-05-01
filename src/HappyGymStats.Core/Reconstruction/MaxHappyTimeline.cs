using System.Diagnostics;
using static HappyGymStats.Core.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// Queryable max-happy timeline built from extracted <see cref="MaxHappyEvent"/> events.
/// </summary>
/// <remarks>
/// The timeline is best-effort and monotonicity is <em>not</em> assumed; max-happy can both increase or decrease.
/// </remarks>
public sealed class MaxHappyTimeline
{
    private readonly DateTimeOffset[] _timesUtc;
    private readonly int[] _maxAfterValues;
    private readonly int? _initialMaxBeforeOldest;

    private MaxHappyTimeline(DateTimeOffset[] timesUtc, int[] maxAfterValues, int? initialMaxBeforeOldest)
    {
        _timesUtc = timesUtc;
        _maxAfterValues = maxAfterValues;
        _initialMaxBeforeOldest = initialMaxBeforeOldest;
    }

    public static MaxHappyTimeline FromEvents(IEnumerable<MaxHappyEvent> events)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));

        // Sort by time asc, then log ID asc; for equal timestamps, later items in the array win.
        var ordered = events
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.LogId)
            .ToArray();

        var times = new DateTimeOffset[ordered.Length];
        var valuesAfter = new int[ordered.Length];
        int? initialBefore = null;

        if (ordered.Length > 0)
        {
            initialBefore = ordered[0].MaxHappyBefore;
        }

        for (var i = 0; i < ordered.Length; i++)
        {
            var e = ordered[i];
            EnsureUtc(e.OccurredAtUtc, nameof(events));

            times[i] = e.OccurredAtUtc;
            valuesAfter[i] = e.MaxHappyAfter;
        }

        return new MaxHappyTimeline(times, valuesAfter, initialBefore);
    }

    /// <summary>
    /// Returns the most recently known max-happy value at or before <paramref name="instantUtc"/>, or <c>null</c>
    /// if no max-happy event exists at/before that time.
    /// </summary>
    public int? MaxHappyAtUtc(DateTimeOffset instantUtc)
    {
        EnsureUtc(instantUtc, nameof(instantUtc));

        if (_timesUtc.Length == 0)
            return null;

        // Rightmost index with time <= instantUtc.
        var idx = Array.BinarySearch(_timesUtc, instantUtc);
        if (idx >= 0)
        {
            // There may be multiple entries with the same timestamp; advance to the last.
            while (idx + 1 < _timesUtc.Length && _timesUtc[idx + 1] == instantUtc)
                idx++;

            return _maxAfterValues[idx];
        }

        // If not found, BinarySearch returns bitwise complement of insertion index.
        var insertionIndex = ~idx;
        var rightmostLessThan = insertionIndex - 1;
        if (rightmostLessThan < 0)
            return _initialMaxBeforeOldest;

        Debug.Assert(_timesUtc[rightmostLessThan] <= instantUtc);
        return _maxAfterValues[rightmostLessThan];
    }

    private static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Value must be in UTC (offset +00:00).", paramName);
    }
}
