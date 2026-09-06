using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HappyGymStats.Data.Repositories;

public sealed class IdentityMapRepository(
    HappyGymStatsDbContext db,
    TimeProvider? timeProvider = null) : IIdentityMapRepository
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task CreateAsync(IdentityMapEntity entity, CancellationToken ct)
    {
        db.IdentityMap.Add(entity);
        return Task.CompletedTask;
    }

    public Task<IdentityMapEntity?> GetByAnonymousIdAsync(Guid anonymousId, CancellationToken ct)
        => db.IdentityMap.FirstOrDefaultAsync(e => e.AnonymousId == anonymousId, ct);

    public Task<IdentityMapEntity?> GetByKeycloakSubAsync(string keycloakSub, CancellationToken ct)
        => db.IdentityMap.FirstOrDefaultAsync(e => e.KeycloakSub == keycloakSub, ct);

    public async Task<bool> ClaimProvisionalAsync(Guid anonymousId, string keycloakSub, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();

        try
        {
            var updated = await db.IdentityMap
                .Where(e => e.AnonymousId == anonymousId
                            && e.IsProvisional
                            && e.KeycloakSub == null
                            && e.ExpiresAtUtc != null
                            && e.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.KeycloakSub, keycloakSub)
                    .SetProperty(e => e.IsProvisional, false)
                    .SetProperty(e => e.ExpiresAtUtc, (DateTimeOffset?)null), ct);

            return updated == 1;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Another provisional identity may have won a concurrent claim for the
            // same Keycloak subject. Treat that database-enforced conflict as a safe
            // non-claim rather than leaking a provider exception through the API.
            return false;
        }
    }

    public async Task StoreEncryptedTornPlayerIdAsync(Guid anonymousId, byte[] encryptedTornPlayerId, CancellationToken ct)
    {
        var entry = await db.IdentityMap.FirstOrDefaultAsync(e => e.AnonymousId == anonymousId, ct);
        if (entry is not null)
            entry.EncryptedTornPlayerId = encryptedTornPlayerId;
    }

    public async Task StorePublicKeyAsync(Guid anonymousId, byte[] publicKeySpki, CancellationToken ct)
    {
        var entry = await db.IdentityMap.FirstOrDefaultAsync(e => e.AnonymousId == anonymousId, ct);
        if (entry is not null)
            entry.PublicKey = publicKeySpki;
    }
}
