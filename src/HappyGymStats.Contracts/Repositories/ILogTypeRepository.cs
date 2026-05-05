using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface ILogTypeRepository
{
    Task<HashSet<int>> GetExistingIdsAsync(CancellationToken ct);

    // Checks existing IDs, adds only missing entries. No SaveChanges — caller commits via IUnitOfWork.
    Task AddRangeIfMissingAsync(IEnumerable<LogTypeEntity> types, CancellationToken ct);
}
