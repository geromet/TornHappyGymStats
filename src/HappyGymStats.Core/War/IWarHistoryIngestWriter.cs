using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

public interface IWarHistoryIngestWriter
{
    Task<IReadOnlyList<RankedWarHistoryEntity>> WriteHistoryPageAsync(
        RankedWarHistoryPageResponse page,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc,
        CancellationToken ct);

    Task<IReadOnlyList<RankedWarReportMemberEntity>> WriteReportAsync(
        RankedWarReportResponse report,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc,
        CancellationToken ct);
}
