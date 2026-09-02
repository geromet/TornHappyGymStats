using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.Repositories;

public interface IWarHistoryRepository
{
    Task<RankedWarHistoryEntity?> GetWarAsync(long warId, CancellationToken ct);
    Task UpsertWarAsync(RankedWarHistoryEntity war, CancellationToken ct);
    Task<IReadOnlyList<RankedWarReportMemberEntity>> GetReportMembersAsync(long warId, long factionId, CancellationToken ct);
    Task ReplaceReportMembersAsync(long warId, DateTimeOffset capturedAtUtc, DateTimeOffset ingestedAtUtc, IReadOnlyCollection<RankedWarReportMemberEntity> members, CancellationToken ct);
    Task<bool> HasCapturedReportAsync(long warId, CancellationToken ct);

    /// <summary>
    /// Ranked wars where the given faction played on either side and a report has been captured,
    /// ordered newest-first. Used to build a faction's scouting profile from real war outcomes.
    /// </summary>
    Task<IReadOnlyList<RankedWarHistoryEntity>> GetWarsByFactionAsync(long factionId, CancellationToken ct);

    /// <summary>
    /// All captured report-member rows for the given faction across every war, used to aggregate
    /// per-member scouting profiles.
    /// </summary>
    Task<IReadOnlyList<RankedWarReportMemberEntity>> GetReportMembersByFactionAsync(long factionId, CancellationToken ct);
}
