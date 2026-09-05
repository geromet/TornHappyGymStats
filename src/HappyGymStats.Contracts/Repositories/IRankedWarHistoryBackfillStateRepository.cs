using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IRankedWarHistoryBackfillStateRepository
{
    Task<RankedWarHistoryBackfillStateEntity?> GetAsync(string scopeKey, CancellationToken ct);
    Task<RankedWarHistoryBackfillStateEntity?> GetLatestAsync(CancellationToken ct);
    Task UpsertAsync(RankedWarHistoryBackfillStateEntity state, CancellationToken ct);
}
