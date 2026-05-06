using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IAffiliationEventRepository
{
    Task<HashSet<string>> GetExistingSourceLogIdsAsync(Guid anonymousId, CancellationToken ct);

    Task<IReadOnlyList<AffiliationEventRecord>> GetForPlayerOrderedAsync(Guid anonymousId, CancellationToken ct);

    // Stages add. Caller commits via IUnitOfWork.
    Task AddAsync(AffiliationEventEntity entity, CancellationToken ct);
}
