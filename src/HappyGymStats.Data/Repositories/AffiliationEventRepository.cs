using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class AffiliationEventRepository(HappyGymStatsDbContext db) : IAffiliationEventRepository
{
    public Task<HashSet<string>> GetExistingSourceLogIdsAsync(Guid anonymousId, CancellationToken ct)
        => db.AffiliationEvents
            .AsNoTracking()
            .Where(a => a.AnonymousId == anonymousId)
            .Select(a => a.SourceLogEntryId)
            .ToHashSetAsync(ct);

    public Task<IReadOnlyList<AffiliationEventRecord>> GetForPlayerOrderedAsync(Guid anonymousId, CancellationToken ct)
        => db.AffiliationEvents
            .AsNoTracking()
            .Where(a => a.AnonymousId == anonymousId)
            .Join(db.UserLogEntries.Where(u => u.AnonymousId == anonymousId),
                  a => a.SourceLogEntryId,
                  u => u.LogEntryId,
                  (a, u) => new { u.OccurredAtUtc, a.Scope, a.AffiliationId, a.LogTypeId, a.SenderId })
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new AffiliationEventRecord(x.OccurredAtUtc, x.Scope, x.AffiliationId, x.LogTypeId, x.SenderId))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<AffiliationEventRecord>)t.Result, TaskContinuationOptions.ExecuteSynchronously);

    public Task AddAsync(AffiliationEventEntity entity, CancellationToken ct)
    {
        db.AffiliationEvents.Add(entity);
        return Task.CompletedTask;
        // No save — caller commits via IUnitOfWork
    }
}
