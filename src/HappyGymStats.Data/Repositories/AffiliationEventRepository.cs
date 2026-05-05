using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class AffiliationEventRepository(HappyGymStatsDbContext db) : IAffiliationEventRepository
{
    public Task<HashSet<string>> GetExistingSourceLogIdsAsync(int playerId, CancellationToken ct)
        => db.AffiliationEvents
            .AsNoTracking()
            .Where(a => a.PlayerId == playerId)
            .Select(a => a.SourceLogEntryId)
            .ToHashSetAsync(ct);

    public Task AddAsync(AffiliationEventEntity entity, CancellationToken ct)
    {
        db.AffiliationEvents.Add(entity);
        return Task.CompletedTask;
        // No save — caller commits via IUnitOfWork
    }
}
