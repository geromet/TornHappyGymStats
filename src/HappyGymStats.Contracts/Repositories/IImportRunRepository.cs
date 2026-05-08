using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IImportRunRepository
{
    // Stages add. Caller commits via IUnitOfWork before reading the generated Id.
    Task<ImportRunEntity> CreateAsync(ImportRunEntity run, CancellationToken ct);

    // Entity is already tracked by the shared scoped DbContext; this is a semantic
    // no-op. Caller commits via IUnitOfWork to flush mutations.
    Task UpdateAsync(ImportRunEntity run, CancellationToken ct);

    Task<ImportRunEntity?> GetLatestIncompleteAsync(CancellationToken ct);

    // Returns AnonymousId from the most recent ImportRun with a non-null AnonymousId, or null if none.
    Task<Guid?> ResolveAnonymousIdAsync(CancellationToken ct);
}
