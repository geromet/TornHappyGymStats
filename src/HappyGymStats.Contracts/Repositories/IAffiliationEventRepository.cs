using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IAffiliationEventRepository
{
    Task<HashSet<string>> GetExistingSourceLogIdsAsync(int playerId, CancellationToken ct);

    // Stages add. Caller commits via IUnitOfWork.
    Task AddAsync(AffiliationEventEntity entity, CancellationToken ct);
}
