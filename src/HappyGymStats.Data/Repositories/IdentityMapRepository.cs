using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class IdentityMapRepository(HappyGymStatsDbContext db) : IIdentityMapRepository
{
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
        var entry = await db.IdentityMap
            .FirstOrDefaultAsync(e => e.AnonymousId == anonymousId && e.IsProvisional, ct);

        if (entry is null) return false;

        entry.KeycloakSub = keycloakSub;
        entry.IsProvisional = false;
        entry.ExpiresAtUtc = null;
        return true;
    }

    public async Task StoreEncryptedTornPlayerIdAsync(Guid anonymousId, byte[] encryptedTornPlayerId, CancellationToken ct)
    {
        var entry = await db.IdentityMap.FirstOrDefaultAsync(e => e.AnonymousId == anonymousId, ct);
        if (entry is not null)
            entry.EncryptedTornPlayerId = encryptedTornPlayerId;
    }
}
