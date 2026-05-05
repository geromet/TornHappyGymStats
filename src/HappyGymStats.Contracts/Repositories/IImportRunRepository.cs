using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IImportRunRepository
{
    // Stages add. Caller commits via IUnitOfWork before reading the generated Id.
    Task<ImportRunEntity> CreateAsync(ImportRunEntity run, CancellationToken ct);

    // Entity is already tracked by the shared scoped DbContext; this is a semantic
    // no-op. Caller commits via IUnitOfWork to flush mutations.
    Task UpdateAsync(ImportRunEntity run, CancellationToken ct);

    Task<ImportRunEntity?> GetLatestIncompleteAsync(int playerId, CancellationToken ct);

    // Returns playerId from the most recent ImportRun with a non-null PlayerId, or 0 if none.
    Task<int> ResolvePlayerIdAsync(CancellationToken ct);
}
