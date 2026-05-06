using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IFactionIdMapRepository
{
    // Returns the stable FactionAnonymousId for this affiliation, creating one if it
    // does not yet exist. Stages add if new. Caller commits via IUnitOfWork.
    Task<Guid> GetOrCreateFactionAnonymousIdAsync(int affiliationId, AffiliationScope scope, CancellationToken ct);
}
