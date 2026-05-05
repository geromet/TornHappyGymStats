using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class LogTypeRepository(HappyGymStatsDbContext db) : ILogTypeRepository
{
    public Task<HashSet<int>> GetExistingIdsAsync(CancellationToken ct)
        => db.LogTypes
            .AsNoTracking()
            .Select(lt => lt.LogTypeId)
            .ToHashSetAsync(ct);

    public async Task AddRangeIfMissingAsync(IEnumerable<LogTypeEntity> types, CancellationToken ct)
    {
        var existing = await GetExistingIdsAsync(ct);
        foreach (var type in types)
        {
            if (!existing.Contains(type.LogTypeId))
                db.LogTypes.Add(type);
        }
        // No save — caller commits via IUnitOfWork
    }
}
