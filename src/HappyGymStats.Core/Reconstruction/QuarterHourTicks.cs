using System.Diagnostics;

namespace HappyGymStats.Core.Reconstruction;

/// <summary>
/// Helpers for counting Torn "happy regeneration" quarter-hour ticks.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tick instants</strong> are defined as the UTC times where minutes are exactly
/// 00, 15, 30, or 45 (with seconds == 0).
/// </para>
/// <para>
/// A tick is counted if it is <strong>strictly after</strong> the earlier instant and
/// <strong>less-than-or-equal</strong> to the later instant (earlier-exclusive, later-inclusive).
/// This convention is locked down by unit tests and is used by happy reconstruction.
/// </para>
/// <para>
/// Examples (UTC):
/// <list type="bullet">
/// <item><description>12:15:00 → 12:15:00 == 0 ticks</description></item>
/// <item><description>12:14:59 → 12:15:00 == 1 tick</description></item>
/// <item><description>12:00:00 → 13:00:00 == 4 ticks (12:15, 12:30, 12:45, 13:00)</description></item>
/// </list>
/// </para>
/// </remarks>
public static class QuarterHourTicks
{
    private static readonly TimeSpan QuarterHour = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Enumerate quarter-hour tick instants between <paramref name="earlierUtc"/> and <paramref name="laterUtc"/>.
    /// </summary>
    /// <remarks>
    /// Uses the same convention as <see cref="CountTicksBetweenUtc"/>: earlier-exclusive, later-inclusive.
    /// </remarks>
    public static IEnumerable<DateTimeOffset> EnumerateTickInstantsBetweenUtc(DateTimeOffset earlierUtc, DateTimeOffset laterUtc)
    {
        EnsureUtc(earlierUtc, nameof(earlierUtc));
        EnsureUtc(laterUtc, nameof(laterUtc));

        if (laterUtc < earlierUtc)
            throw new ArgumentOutOfRangeException(nameof(laterUtc), laterUtc, "laterUtc must be >= earlierUtc.");

        var nextTick = NextQuarterHourAfterUtc(earlierUtc);
        while (nextTick <= laterUtc)
        {
            yield return nextTick;
            nextTick = nextTick.Add(QuarterHour);
        }
    }

    /// <summary>
    /// Count quarter-hour tick instants between <paramref name="earlierUtc"/> and <paramref name="laterUtc"/>.
    /// </summary>
    /// <param name="earlierUtc">Earlier instant in UTC (offset must be +00:00).</param>
    /// <param name="laterUtc">Later instant in UTC (offset must be +00:00) and must be >= <paramref name="earlierUtc"/>.</param>
    /// <returns>
    /// The number of tick instants T such that: <c>earlierUtc &lt; T &lt;= laterUtc</c>.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when either parameter is not in UTC (offset != 0).</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="laterUtc"/> is earlier than <paramref name="earlierUtc"/>.</exception>
    public static long CountTicksBetweenUtc(DateTimeOffset earlierUtc, DateTimeOffset laterUtc)
    {
        EnsureUtc(earlierUtc, nameof(earlierUtc));
        EnsureUtc(laterUtc, nameof(laterUtc));

        if (laterUtc < earlierUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(laterUtc), laterUtc, "laterUtc must be >= earlierUtc.");
        }

        // Find the first quarter-hour instant strictly after earlierUtc.
        var nextTick = NextQuarterHourAfterUtc(earlierUtc);
        if (nextTick > laterUtc)
        {
            return 0;
        }

        var delta = laterUtc - nextTick;
        Debug.Assert(delta.Ticks >= 0, "delta should be non-negative due to nextTick <= laterUtc.");

        // later-inclusive: include nextTick itself and every +15m boundary after it up to laterUtc.
        return (delta.Ticks / QuarterHour.Ticks) + 1;
    }

    private static DateTimeOffset NextQuarterHourAfterUtc(DateTimeOffset instantUtc)
    {
        // instantUtc offset already validated as UTC.
        var m = (instantUtc.Minute / 15) * 15;
        var floored = new DateTimeOffset(
            instantUtc.Year,
            instantUtc.Month,
            instantUtc.Day,
            instantUtc.Hour,
            m,
            0,
            TimeSpan.Zero);

        // Ensure strictness: if instant is already exactly on a tick boundary, we advance to the next one.
        if (floored <= instantUtc)
        {
            floored = floored.Add(QuarterHour);
        }

        return floored;
    }

    private static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Value must be in UTC (offset +00:00).", paramName);
        }
    }
}
