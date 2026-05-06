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
        // No save — caller commits via IUnitOfWork
    }

    public Task UpdateAsync(ImportRunEntity run, CancellationToken ct)
    {
        // Entity already tracked by shared scoped DbContext. No-op here.
        // Caller commits via IUnitOfWork to flush mutations.
        return Task.CompletedTask;
    }

    public Task<ImportRunEntity?> GetLatestIncompleteAsync(CancellationToken ct)
        => db.ImportRuns
            .Where(r => r.CompletedAtUtc == null && r.NextUrl != null)
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<Guid?> ResolveAnonymousIdAsync(CancellationToken ct)
    {
        return await db.ImportRuns
            .AsNoTracking()
            .Where(r => r.AnonymousId != null)
            .OrderByDescending(r => r.StartedAtUtc)
            .Select(r => r.AnonymousId)
            .FirstOrDefaultAsync(ct);
    }
}
