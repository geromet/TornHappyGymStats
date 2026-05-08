using HappyGymStats.Core.Models;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IModifierProvenanceRepository
{
    Task<IReadOnlyList<ModifierProvenanceRow>> GetAllAsync(CancellationToken ct);

    // Removes all existing rows for playerId, adds new ones. No SaveChanges — caller commits via IUnitOfWork.
    Task StageReplacementForPlayerAsync(Guid anonymousId, IEnumerable<ModifierProvenanceEntity> entities, CancellationToken ct);
}
