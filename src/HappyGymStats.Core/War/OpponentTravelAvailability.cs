namespace HappyGymStats.Core.War;

public enum OpponentLocationCategory
{
    Unknown = 0,
    Torn = 1,
    Travelling = 2,
    Abroad = 3,
    HospitalAbroad = 4,
}

public enum TravelWindowPrecision
{
    Unknown = 0,
    ObservedNow = 1,
    Estimated = 2,
    Authoritative = 3,
}

public sealed record OpponentTravelObservation
{
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required OpponentLocationCategory Location { get; init; }
    public string? Destination { get; init; }
    public DateTimeOffset? AuthoritativeReturnAtUtc { get; init; }
    public TimeSpan? EstimatedRemainingTravel { get; init; }
    public TimeSpan EstimatedUncertainty { get; init; } = TimeSpan.Zero;
    public IReadOnlyList<string> Provenance { get; init; } = [];
}

public sealed record OpponentTravelAvailability
{
    public required OpponentLocationCategory Location { get; init; }
    public required DateTimeOffset EvaluatedAtUtc { get; init; }
    public required DateTimeOffset SourceObservedAtUtc { get; init; }
    public required bool IsFresh { get; init; }
    public required TravelWindowPrecision Precision { get; init; }
    public required bool IsAttackableNow { get; init; }
    public DateTimeOffset? AttackableFromUtc { get; init; }
    public DateTimeOffset? AttackableUntilUtc { get; init; }
    public string? Destination { get; init; }
    public required string Explanation { get; init; }
    public IReadOnlyList<string> Provenance { get; init; } = [];
}

public static class OpponentTravelAvailabilityEngine
{
    public static readonly TimeSpan MaximumObservationAge = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(2);

    public static OpponentTravelAvailability EvaluateLatest(
        IEnumerable<OpponentTravelObservation> observations,
        DateTimeOffset asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var asOf = asOfUtc.ToUniversalTime();
        var latest = observations
            .OrderByDescending(observation => observation.ObservedAtUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            throw new ArgumentException("At least one travel observation is required.", nameof(observations));
        }

        Validate(latest, asOf);
        var observedAt = latest.ObservedAtUtc.ToUniversalTime();
        var fresh = asOf - observedAt <= MaximumObservationAge;
        if (!fresh)
        {
            return Build(latest, asOf, fresh: false, TravelWindowPrecision.Unknown, false, null, null,
                "Unknown: latest travel/status observation is stale, so no attackable window is asserted.");
        }

        if (latest.Location == OpponentLocationCategory.Torn)
        {
            return Build(latest, asOf, fresh: true, TravelWindowPrecision.ObservedNow, true, asOf, null,
                "Observed in Torn on the freshest status sample; older travel estimates are superseded.");
        }

        if (latest.AuthoritativeReturnAtUtc is { } authoritativeReturn)
        {
            var returnAt = authoritativeReturn.ToUniversalTime();
            return Build(latest, asOf, fresh: true, TravelWindowPrecision.Authoritative, returnAt <= asOf,
                returnAt <= asOf ? asOf : returnAt, null,
                "Attackable-from time comes from an authoritative timestamp on the freshest observation.");
        }

        if (latest.Location == OpponentLocationCategory.Travelling && latest.EstimatedRemainingTravel is { } remaining)
        {
            var midpoint = observedAt + remaining;
            var from = midpoint - latest.EstimatedUncertainty;
            var until = midpoint + latest.EstimatedUncertainty;
            return Build(latest, asOf, fresh: true, TravelWindowPrecision.Estimated, until <= asOf,
                from <= asOf ? asOf : from, until <= asOf ? null : until,
                "Estimated attackable window derived from supplied travel duration and uncertainty; it is not exact.");
        }

        return Build(latest, asOf, fresh: true, TravelWindowPrecision.Unknown, false, null, null,
            "No supported timing input is present on the freshest observation, so no return or attackable ETA is invented.");
    }

    private static OpponentTravelAvailability Build(
        OpponentTravelObservation observation,
        DateTimeOffset asOf,
        bool fresh,
        TravelWindowPrecision precision,
        bool attackableNow,
        DateTimeOffset? from,
        DateTimeOffset? until,
        string explanation)
        => new()
        {
            Location = observation.Location,
            EvaluatedAtUtc = asOf,
            SourceObservedAtUtc = observation.ObservedAtUtc.ToUniversalTime(),
            IsFresh = fresh,
            Precision = precision,
            IsAttackableNow = attackableNow,
            AttackableFromUtc = from,
            AttackableUntilUtc = until,
            Destination = observation.Destination,
            Explanation = explanation,
            Provenance = observation.Provenance.ToArray(),
        };

    private static void Validate(OpponentTravelObservation observation, DateTimeOffset asOf)
    {
        if (!Enum.IsDefined(observation.Location))
        {
            throw new ArgumentOutOfRangeException(nameof(observation), observation.Location, "Location category must be defined.");
        }

        var observedAt = observation.ObservedAtUtc.ToUniversalTime();
        if (observedAt > asOf + MaximumFutureSkew)
        {
            throw new ArgumentException("Observation timestamp is implausibly ahead of the evaluation clock.", nameof(observation));
        }

        if (observation.AuthoritativeReturnAtUtc is { } authoritative && authoritative.ToUniversalTime() < observedAt)
        {
            throw new ArgumentException("Authoritative return time cannot predate its source observation.", nameof(observation));
        }

        if (observation.EstimatedRemainingTravel is { } remaining && remaining < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(observation), "Estimated remaining travel cannot be negative.");
        }

        if (observation.EstimatedUncertainty < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(observation), "Estimated uncertainty cannot be negative.");
        }
    }
}
