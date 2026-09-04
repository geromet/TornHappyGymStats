using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class ImportRunRepository(HappyGymStatsDbContext db) : IImportRunRepository
{
    public Task<ImportRunEntity> CreateAsync(ImportRunEntity run, CancellationToken ct)
    {
        db.ImportRuns.Add(run);
        return Task.FromResult(run);
    }

    public Task UpdateAsync(ImportRunEntity run, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<ImportRunEntity?> GetLatestIncompleteAsync(Guid anonymousId, CancellationToken ct)
    {
        if (anonymousId == Guid.Empty)
            throw new ArgumentException("AnonymousId must identify the import owner.", nameof(anonymousId));

        return db.ImportRuns
            .AsNoTracking()
            .Where(r => r.AnonymousId == anonymousId && r.CompletedAtUtc == null && r.NextUrl != null)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
    }
}
