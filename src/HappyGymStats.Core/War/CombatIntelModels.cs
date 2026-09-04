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
/// Provider-neutral combat-intelligence observation. Invalid value/range, provenance,
/// chronology, and visibility combinations are rejected at creation so downstream
/// resolution/persistence can trust this vocabulary instead of revalidating it.
/// </summary>
public sealed record CombatIntelObservation
{
    private CombatIntelObservation(
        string observationId,
        long playerId,
        string provider,
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset observedAtUtc,
        CombatIntelClassification classification,
        decimal? value,
        decimal? lowerBound,
        decimal? upperBound,
        string? providerMetadata,
        CombatIntelVisibilityScope visibilityScope,
        string? visibilityOwner,
        string? supersedesObservationId)
    {
        ObservationId = observationId;
        PlayerId = playerId;
        Provider = provider;
        FetchedAtUtc = fetchedAtUtc;
        ObservedAtUtc = observedAtUtc;
        Classification = classification;
        Value = value;
        LowerBound = lowerBound;
        UpperBound = upperBound;
        ProviderMetadata = providerMetadata;
        VisibilityScope = visibilityScope;
        VisibilityOwner = visibilityOwner;
        SupersedesObservationId = supersedesObservationId;
    }

    public string ObservationId { get; }
    public long PlayerId { get; }
    public string Provider { get; }
    public DateTimeOffset FetchedAtUtc { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public CombatIntelClassification Classification { get; }
    public decimal? Value { get; }
    public decimal? LowerBound { get; }
    public decimal? UpperBound { get; }
    public string? ProviderMetadata { get; }
    public CombatIntelVisibilityScope VisibilityScope { get; }
    public string? VisibilityOwner { get; }
    public string? SupersedesObservationId { get; }

    public static CombatIntelObservation Create(
        string observationId,
        long playerId,
        string provider,
        DateTimeOffset fetchedAtUtc,
        DateTimeOffset observedAtUtc,
        CombatIntelClassification classification,
        decimal? value = null,
        decimal? lowerBound = null,
        decimal? upperBound = null,
        CombatIntelVisibilityScope visibilityScope = CombatIntelVisibilityScope.Public,
        string? visibilityOwner = null,
        string? providerMetadata = null,
        string? supersedesObservationId = null)
    {
        if (string.IsNullOrWhiteSpace(observationId))
        {
            throw new ArgumentException("Observation id must be non-empty.", nameof(observationId));
        }

        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId), playerId, "Player id must be positive.");
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider must be non-empty.", nameof(provider));
        }

        if (observedAtUtc > fetchedAtUtc)
        {
            throw new ArgumentException("Observed time cannot be later than fetch time.", nameof(observedAtUtc));
        }

        if (string.Equals(supersedesObservationId, observationId, StringComparison.Ordinal))
        {
            throw new ArgumentException("An observation cannot supersede itself.", nameof(supersedesObservationId));
        }

        switch (classification)
        {
            case CombatIntelClassification.Exact:
                if (!value.HasValue || value.Value < 0 || lowerBound.HasValue || upperBound.HasValue)
                {
                    throw new ArgumentException(
                        "Exact observations require a non-negative Value and may not carry estimate bounds.",
                        nameof(classification));
                }
                break;

            case CombatIntelClassification.Estimated:
                if (value.HasValue ||
                    !lowerBound.HasValue ||
                    !upperBound.HasValue ||
                    lowerBound.Value < 0 ||
                    upperBound.Value < 0 ||
                    lowerBound.Value > upperBound.Value)
                {
                    throw new ArgumentException(
                        "Estimated observations require non-negative LowerBound <= UpperBound and may not carry an exact Value.",
                        nameof(classification));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unknown combat-intel classification.");
        }

        switch (visibilityScope)
        {
            case CombatIntelVisibilityScope.Public:
                if (!string.IsNullOrWhiteSpace(visibilityOwner))
                {
                    throw new ArgumentException("Public observations may not name a visibility owner.", nameof(visibilityOwner));
                }
                visibilityOwner = null;
                break;

            case CombatIntelVisibilityScope.Faction:
            case CombatIntelVisibilityScope.Member:
                if (string.IsNullOrWhiteSpace(visibilityOwner))
                {
                    throw new ArgumentException("Private observations require a visibility owner.", nameof(visibilityOwner));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(visibilityScope), visibilityScope, "Unknown combat-intel visibility scope.");
        }

        return new CombatIntelObservation(
            observationId,
            playerId,
            provider,
            fetchedAtUtc,
            observedAtUtc,
            classification,
            value,
            lowerBound,
            upperBound,
            providerMetadata,
            visibilityScope,
            visibilityOwner,
            supersedesObservationId);
    }
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
