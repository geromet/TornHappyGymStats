namespace HappyGymStats.Core.War;

public enum CombatIntelClassification
{
    Estimated = 0,
    Exact = 1,
}

public enum CombatIntelVisibilityScope
{
    Public = 0,
    Faction = 1,
    Member = 2,
}

/// <summary>
/// Provider-neutral combat-intelligence observation. Provider-specific payloads stay at the
/// adapter boundary; consumers reason only about value/range, provenance, time and visibility.
/// </summary>
public sealed record CombatIntelObservation
{
    public required string ObservationId { get; init; }
    public long PlayerId { get; init; }
    public required string Provider { get; init; }
    public DateTimeOffset FetchedAtUtc { get; init; }
    public DateTimeOffset ObservedAtUtc { get; init; }
    public CombatIntelClassification Classification { get; init; }
    public decimal? Value { get; init; }
    public decimal? LowerBound { get; init; }
    public decimal? UpperBound { get; init; }
    public string? ProviderMetadata { get; init; }
    public CombatIntelVisibilityScope VisibilityScope { get; init; }
    public string? VisibilityOwner { get; init; }
    public string? SupersedesObservationId { get; init; }
}

public sealed record CombatIntelAccessContext
{
    public string? FactionId { get; init; }
    public string? MemberId { get; init; }
}

/// <summary>
/// A deterministic resolved view. Winner and alternatives retain their original observations so
/// callers can always explain source, age, exact-vs-estimated classification and uncertainty.
/// </summary>
public sealed record CombatIntelResolution
{
    public CombatIntelObservation? Winner { get; init; }
    public IReadOnlyList<CombatIntelObservation> Alternatives { get; init; } = [];
    public DateTimeOffset ResolvedAtUtc { get; init; }
}
