using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class FactionMembershipRepository(HappyGymStatsDbContext db) : IFactionMembershipRepository
{
    public async Task AddOrIgnoreAsync(Guid factionAnonymousId, Guid memberAnonymousId, CancellationToken ct)
    {
        var alreadyTracked = db.FactionMembership.Local
            .Any(e => e.FactionAnonymousId == factionAnonymousId && e.MemberAnonymousId == memberAnonymousId);
        if (alreadyTracked)
            return;

        var exists = await db.FactionMembership
            .AsNoTracking()
            .AnyAsync(e => e.FactionAnonymousId == factionAnonymousId && e.MemberAnonymousId == memberAnonymousId, ct);
        if (!exists)
            db.FactionMembership.Add(new FactionMembershipEntity
            {
                FactionAnonymousId = factionAnonymousId,
                MemberAnonymousId = memberAnonymousId,
            });
    }

    public Task<IReadOnlyList<Guid>> GetMemberAnonymousIdsAsync(Guid factionAnonymousId, CancellationToken ct)
        => db.FactionMembership
            .AsNoTracking()
            .Where(e => e.FactionAnonymousId == factionAnonymousId)
            .Select(e => e.MemberAnonymousId)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<Guid>)t.Result, TaskContinuationOptions.ExecuteSynchronously);
}
