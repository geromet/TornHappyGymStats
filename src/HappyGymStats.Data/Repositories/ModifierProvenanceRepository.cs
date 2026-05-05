using HappyGymStats.Core.Models;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class ModifierProvenanceRepository(HappyGymStatsDbContext db) : IModifierProvenanceRepository
{
    public Task<IReadOnlyList<ModifierProvenanceRow>> GetAllAsync(CancellationToken ct)
        => db.ModifierProvenance
            .AsNoTracking()
            .Select(x => new ModifierProvenanceRow(
                x.LogEntryId, x.Scope, x.VerificationStatus,
                x.SubjectId, x.FactionId, x.CompanyId, x.PlayerId))
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ModifierProvenanceRow>)t.Result, TaskContinuationOptions.ExecuteSynchronously);

    public async Task StageReplacementForPlayerAsync(int playerId, IEnumerable<ModifierProvenanceEntity> entities, CancellationToken ct)
    {
        var existing = await db.ModifierProvenance
            .Where(p => p.PlayerId == playerId)
            .ToListAsync(ct);
        db.ModifierProvenance.RemoveRange(existing);
        db.ModifierProvenance.AddRange(entities);
        // No save — caller commits via IUnitOfWork
    }
}
