using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IWarHistoryRepository
{
    Task<RankedWarHistoryEntity?> GetWarAsync(long warId, CancellationToken ct);
    Task UpsertWarAsync(RankedWarHistoryEntity war, CancellationToken ct);
    Task<IReadOnlyList<RankedWarReportMemberEntity>> GetReportMembersAsync(long warId, long factionId, CancellationToken ct);
    Task ReplaceReportMembersAsync(long warId, DateTimeOffset capturedAtUtc, DateTimeOffset ingestedAtUtc, IReadOnlyCollection<RankedWarReportMemberEntity> members, CancellationToken ct);
    Task<bool> HasCapturedReportAsync(long warId, CancellationToken ct);
}
