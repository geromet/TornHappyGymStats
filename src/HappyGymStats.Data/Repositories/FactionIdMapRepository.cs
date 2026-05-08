using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class FactionIdMapRepository(HappyGymStatsDbContext db) : IFactionIdMapRepository
{
    public async Task<Guid> GetOrCreateFactionAnonymousIdAsync(int affiliationId, AffiliationScope scope, CancellationToken ct)
    {
        // Check change tracker first to handle multiple calls within the same SaveChanges batch.
        var tracked = db.FactionIdMap.Local
            .FirstOrDefault(e => e.AffiliationId == affiliationId && e.Scope == scope);
        if (tracked is not null)
            return tracked.FactionAnonymousId;

        var existing = await db.FactionIdMap
            .AsNoTracking()
            .Where(e => e.AffiliationId == affiliationId && e.Scope == scope)
            .Select(e => (Guid?)e.FactionAnonymousId)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
            return existing.Value;

        var newId = Guid.NewGuid();
        db.FactionIdMap.Add(new FactionIdMapEntity
        {
            AffiliationId = affiliationId,
            Scope = scope,
            FactionAnonymousId = newId,
        });
        return newId;
    }
}
