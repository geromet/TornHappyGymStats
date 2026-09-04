namespace HappyGymStats.Core.War;

/// <summary>
/// Resolves provider-neutral combat intelligence without knowing any provider-specific contract.
/// Freshness is the primary ordering: a newer observation beats an older one. For observations
/// made at the same instant, a newer fetch wins, then exact beats estimated. Provider and
/// observation id are final ordinal tie-breakers so the result is stable across input order.
/// </summary>
public static class CombatIntelResolver
{
    public static CombatIntelResolution Resolve(
        long playerId,
        IEnumerable<CombatIntelObservation> observations,
        CombatIntelAccessContext access,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(access);

        var visible = observations
            .Where(observation => observation.PlayerId == playerId)
            .Where(observation => IsVisibleTo(observation, access))
            .OrderByDescending(observation => observation.ObservedAtUtc)
            .ThenByDescending(observation => observation.FetchedAtUtc)
            .ThenByDescending(observation => observation.Classification)
            .ThenBy(observation => observation.Provider, StringComparer.Ordinal)
            .ThenBy(observation => observation.ObservationId, StringComparer.Ordinal)
            .ToArray();

        return new CombatIntelResolution
        {
            Winner = visible.FirstOrDefault(),
            Alternatives = visible.Skip(1).ToArray(),
            ResolvedAtUtc = nowUtc,
        };
    }

    public static bool IsVisibleTo(CombatIntelObservation observation, CombatIntelAccessContext access)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(access);

        return observation.VisibilityScope switch
        {
            CombatIntelVisibilityScope.Public => true,
            CombatIntelVisibilityScope.Faction =>
                !string.IsNullOrWhiteSpace(observation.VisibilityOwner)
                && string.Equals(observation.VisibilityOwner, access.FactionId, StringComparison.Ordinal),
            CombatIntelVisibilityScope.Member =>
                !string.IsNullOrWhiteSpace(observation.VisibilityOwner)
                && string.Equals(observation.VisibilityOwner, access.MemberId, StringComparison.Ordinal),
            _ => false,
        };
    }
}
