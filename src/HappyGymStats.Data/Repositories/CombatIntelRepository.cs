using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class CombatIntelRepository(HappyGymStatsDbContext db) : ICombatIntelRepository
{
    public async Task AppendAsync(
        CombatIntelObservation observation,
        DateTimeOffset trustedReferenceTimeUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var latestAllowedProviderTime = trustedReferenceTimeUtc.ToUniversalTime()
            + CombatIntelObservation.MaxProviderFutureSkew;
        if (observation.FetchedAtUtc.ToUniversalTime() > latestAllowedProviderTime
            || observation.ObservedAtUtc.ToUniversalTime() > latestAllowedProviderTime)
        {
            throw new ArgumentException(
                "Observation provider timestamps exceed the trusted ingestion clock skew allowance.",
                nameof(observation));
        }

        if (db.CombatIntelObservations.Local.Any(e => e.ObservationId == observation.ObservationId)
            || await db.CombatIntelObservations.AsNoTracking()
                .AnyAsync(e => e.ObservationId == observation.ObservationId, ct))
        {
            throw new InvalidOperationException(
                $"Combat-intel observation '{observation.ObservationId}' is already persisted.");
        }

        if (observation.SupersedesObservationId is not null)
        {
            var superseded = db.CombatIntelObservations.Local
                .FirstOrDefault(e => e.ObservationId == observation.SupersedesObservationId)
                ?? await db.CombatIntelObservations.AsNoTracking()
                    .SingleOrDefaultAsync(e => e.ObservationId == observation.SupersedesObservationId, ct);

            if (superseded is null)
            {
                throw new InvalidOperationException(
                    $"Superseded combat-intel observation '{observation.SupersedesObservationId}' does not exist.");
            }

            if (superseded.PlayerId != observation.PlayerId)
            {
                throw new InvalidOperationException(
                    "A combat-intel observation cannot supersede an observation for another player.");
            }

            if (superseded.VisibilityScope != observation.VisibilityScope)
            {
                throw new InvalidOperationException(
                    "A combat-intel observation cannot change visibility scope through supersession.");
            }

            if (observation.VisibilityScope != CombatIntelVisibilityScope.Public
                && !string.Equals(
                    superseded.VisibilityOwner,
                    observation.VisibilityOwner,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A private combat-intel observation cannot supersede an observation owned by another visibility principal.");
            }
        }

        db.CombatIntelObservations.Add(ToEntity(observation));
    }

    public async Task<IReadOnlyList<CombatIntelObservation>> GetHistoryAsync(
        long playerId,
        string? provider,
        DateTimeOffset? observedSinceUtc,
        CancellationToken ct)
    {
        if (playerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerId), playerId, "Player id must be positive.");
        }

        if (provider is not null && string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Provider filter must be non-empty when supplied.", nameof(provider));
        }

        var query = db.CombatIntelObservations
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId);

        if (provider is not null)
        {
            query = query.Where(e => e.Provider == provider);
        }

        if (observedSinceUtc.HasValue)
        {
            var sinceUtc = observedSinceUtc.Value.ToUniversalTime();
            query = query.Where(e => e.ObservedAtUtc >= sinceUtc);
        }

        var rows = await query
            .OrderByDescending(e => e.ObservedAtUtc)
            .ThenByDescending(e => e.FetchedAtUtc)
            .ThenBy(e => e.ObservationId)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToArray();
    }

    private static CombatIntelObservationEntity ToEntity(CombatIntelObservation observation) => new()
    {
        ObservationId = observation.ObservationId,
        PlayerId = observation.PlayerId,
        Provider = observation.Provider,
        FetchedAtUtc = observation.FetchedAtUtc.ToUniversalTime(),
        ObservedAtUtc = observation.ObservedAtUtc.ToUniversalTime(),
        Classification = observation.Classification,
        Value = observation.Value,
        LowerBound = observation.LowerBound,
        UpperBound = observation.UpperBound,
        ProviderMetadata = observation.ProviderMetadata,
        VisibilityScope = observation.VisibilityScope,
        VisibilityOwner = observation.VisibilityOwner,
        SupersedesObservationId = observation.SupersedesObservationId,
    };

    private static CombatIntelObservation ToDomain(CombatIntelObservationEntity entity) =>
        CombatIntelObservation.Create(
            entity.ObservationId,
            entity.PlayerId,
            entity.Provider,
            entity.FetchedAtUtc,
            entity.ObservedAtUtc,
            entity.Classification,
            entity.Value,
            entity.LowerBound,
            entity.UpperBound,
            entity.VisibilityScope,
            entity.VisibilityOwner,
            entity.ProviderMetadata,
            entity.SupersedesObservationId);
}
