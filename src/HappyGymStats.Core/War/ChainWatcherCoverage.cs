namespace HappyGymStats.Core.War;

public sealed class WatcherShift
{
    public WatcherShift(Guid id, long watcherId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Shift id must be non-empty.", nameof(id));
        if (watcherId <= 0) throw new ArgumentOutOfRangeException(nameof(watcherId), "Watcher id must be positive.");

        startsAtUtc = startsAtUtc.ToUniversalTime();
        endsAtUtc = endsAtUtc.ToUniversalTime();
        if (endsAtUtc <= startsAtUtc)
            throw new ArgumentException("Watcher shift end must be after its start.", nameof(endsAtUtc));

        Id = id;
        WatcherId = watcherId;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public Guid Id { get; }
    public long WatcherId { get; }
    public DateTimeOffset StartsAtUtc { get; }
    public DateTimeOffset EndsAtUtc { get; }

    public bool Covers(DateTimeOffset instant)
    {
        instant = instant.ToUniversalTime();
        return StartsAtUtc <= instant && instant < EndsAtUtc;
    }
}

public sealed record WatcherCheckIn(Guid ShiftId, DateTimeOffset OccurredAtUtc);
public sealed record WatcherCheckOut(Guid ShiftId, DateTimeOffset OccurredAtUtc);
public sealed record WatcherHandoff(Guid FromShiftId, Guid ToShiftId, DateTimeOffset OccurredAtUtc);

public readonly record struct WatcherCoverageGap(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc)
{
    public TimeSpan Duration => EndsAtUtc - StartsAtUtc;
}

public enum WatcherShiftState
{
    Upcoming,
    AwaitingCheckIn,
    Active,
    NoShow,
    Completed,
}

public static class ChainWatcherCoverage
{
    public static IReadOnlyList<WatcherCoverageGap> FindGaps(
        IEnumerable<WatcherShift> shifts,
        DateTimeOffset windowStartsAtUtc,
        DateTimeOffset windowEndsAtUtc)
    {
        ArgumentNullException.ThrowIfNull(shifts);

        windowStartsAtUtc = windowStartsAtUtc.ToUniversalTime();
        windowEndsAtUtc = windowEndsAtUtc.ToUniversalTime();
        if (windowEndsAtUtc <= windowStartsAtUtc)
            throw new ArgumentException("Coverage window end must be after its start.", nameof(windowEndsAtUtc));

        var intervals = shifts
            .Where(shift => shift.EndsAtUtc > windowStartsAtUtc && shift.StartsAtUtc < windowEndsAtUtc)
            .Select(shift => (
                Start: shift.StartsAtUtc < windowStartsAtUtc ? windowStartsAtUtc : shift.StartsAtUtc,
                End: shift.EndsAtUtc > windowEndsAtUtc ? windowEndsAtUtc : shift.EndsAtUtc))
            .OrderBy(interval => interval.Start)
            .ThenBy(interval => interval.End)
            .ToArray();

        if (intervals.Length == 0)
            return [new WatcherCoverageGap(windowStartsAtUtc, windowEndsAtUtc)];

        var gaps = new List<WatcherCoverageGap>();
        var coveredUntil = windowStartsAtUtc;

        foreach (var interval in intervals)
        {
            if (interval.Start > coveredUntil)
                gaps.Add(new WatcherCoverageGap(coveredUntil, interval.Start));

            if (interval.End > coveredUntil)
                coveredUntil = interval.End;

            if (coveredUntil >= windowEndsAtUtc)
                break;
        }

        if (coveredUntil < windowEndsAtUtc)
            gaps.Add(new WatcherCoverageGap(coveredUntil, windowEndsAtUtc));

        return gaps;
    }

    public static IReadOnlyList<WatcherShift> ActiveAt(IEnumerable<WatcherShift> shifts, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        instant = instant.ToUniversalTime();

        return shifts
            .Where(shift => shift.Covers(instant))
            .OrderBy(shift => shift.StartsAtUtc)
            .ThenBy(shift => shift.WatcherId)
            .ThenBy(shift => shift.Id)
            .ToArray();
    }

    public static WatcherShiftState GetState(
        WatcherShift shift,
        IEnumerable<WatcherCheckIn> checkIns,
        IEnumerable<WatcherCheckOut> checkOuts,
        DateTimeOffset nowUtc,
        TimeSpan checkInGrace)
    {
        ArgumentNullException.ThrowIfNull(shift);
        ArgumentNullException.ThrowIfNull(checkIns);
        ArgumentNullException.ThrowIfNull(checkOuts);
        if (checkInGrace < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(checkInGrace), "Check-in grace cannot be negative.");

        nowUtc = nowUtc.ToUniversalTime();
        if (nowUtc < shift.StartsAtUtc)
            return WatcherShiftState.Upcoming;

        var firstCheckIn = checkIns
            .Where(item => item.ShiftId == shift.Id)
            .Select(item => item.OccurredAtUtc.ToUniversalTime())
            .Where(at => at >= shift.StartsAtUtc && at < shift.EndsAtUtc)
            .OrderBy(at => at)
            .Select(at => (DateTimeOffset?)at)
            .FirstOrDefault();

        if (firstCheckIn is null)
        {
            var graceEndsAt = shift.StartsAtUtc + checkInGrace;
            if (graceEndsAt > shift.EndsAtUtc)
                graceEndsAt = shift.EndsAtUtc;

            return nowUtc < graceEndsAt
                ? WatcherShiftState.AwaitingCheckIn
                : WatcherShiftState.NoShow;
        }

        if (nowUtc >= shift.EndsAtUtc)
            return WatcherShiftState.Completed;

        var checkedOut = checkOuts
            .Where(item => item.ShiftId == shift.Id)
            .Select(item => item.OccurredAtUtc.ToUniversalTime())
            .Any(at => at >= firstCheckIn.Value && at <= nowUtc);

        return checkedOut ? WatcherShiftState.Completed : WatcherShiftState.Active;
    }

    public static bool IsValidAdjacentHandoff(
        WatcherHandoff handoff,
        IReadOnlyDictionary<Guid, WatcherShift> shiftsById)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(shiftsById);

        if (!shiftsById.TryGetValue(handoff.FromShiftId, out var from)
            || !shiftsById.TryGetValue(handoff.ToShiftId, out var to)
            || from.Id == to.Id)
        {
            return false;
        }

        var occurredAtUtc = handoff.OccurredAtUtc.ToUniversalTime();
        return from.EndsAtUtc == to.StartsAtUtc
            && occurredAtUtc == from.EndsAtUtc;
    }
}
