using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class RankedWarHistoryBackfillStateRepository(HappyGymStatsDbContext db) : IRankedWarHistoryBackfillStateRepository
{
    public Task<RankedWarHistoryBackfillStateEntity?> GetAsync(string scopeKey, CancellationToken ct)
    {
        EnsureNotBlank(nameof(scopeKey), scopeKey);
        return db.RankedWarHistoryBackfillState
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.ScopeKey == scopeKey, ct);
    }

    public async Task UpsertAsync(RankedWarHistoryBackfillStateEntity state, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureNotBlank(nameof(state.ScopeKey), state.ScopeKey);

        var tracked = db.RankedWarHistoryBackfillState.Local.FirstOrDefault(e => e.ScopeKey == state.ScopeKey);
        if (tracked is not null)
        {
            Copy(state, tracked);
            return;
        }

        var existing = await db.RankedWarHistoryBackfillState
            .SingleOrDefaultAsync(e => e.ScopeKey == state.ScopeKey, ct);

        if (existing is null)
        {
            db.RankedWarHistoryBackfillState.Add(state);
            return;
        }

        Copy(state, existing);
    }

    private static void Copy(RankedWarHistoryBackfillStateEntity source, RankedWarHistoryBackfillStateEntity target)
    {
        target.Status = source.Status;
        target.Phase = source.Phase;
        target.NextHistoryPageUrl = source.NextHistoryPageUrl;
        target.LastProcessedWarId = source.LastProcessedWarId;
        target.PagesProcessed = source.PagesProcessed;
        target.ReportsProcessed = source.ReportsProcessed;
        target.RetryCount = source.RetryCount;
        target.LastFailureCategory = source.LastFailureCategory;
        target.LastErrorMessage = source.LastErrorMessage;
        target.LastSuccessAtUtc = source.LastSuccessAtUtc;
        target.LastFailureAtUtc = source.LastFailureAtUtc;
        target.NextRetryAtUtc = source.NextRetryAtUtc;
        target.CreatedAtUtc = source.CreatedAtUtc;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
    }

    private static void EnsureNotBlank(string paramName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }
    }
}
