using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IWarStateRepository
{
    Task<WarCurrentEntity?> GetCurrentAsync(string scopeKey, CancellationToken ct);
    Task UpsertCurrentAsync(WarCurrentEntity current, CancellationToken ct);

    Task<IReadOnlyList<WarRosterSnapshotEntity>> GetRosterSnapshotAsync(long warId, CancellationToken ct);
    Task ReplaceRosterSnapshotAsync(long warId, IReadOnlyCollection<WarRosterSnapshotEntity> rosterEntries, CancellationToken ct);

    Task<IReadOnlyList<WarScoreSampleEntity>> GetScoreSamplesAsync(long warId, CancellationToken ct);
    Task AddScoreSampleAsync(WarScoreSampleEntity sample, CancellationToken ct);

    Task<WarPollerHeartbeatEntity?> GetHeartbeatAsync(string scopeKey, CancellationToken ct);
    Task UpsertHeartbeatAsync(WarPollerHeartbeatEntity heartbeat, CancellationToken ct);
}
