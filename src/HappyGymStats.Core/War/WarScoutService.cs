using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

public sealed class WarScoutService(
    IWarHistoryRepository repository,
    IRankedWarHistoryBackfillStateRepository backfillStateRepository)
{
    public async Task<FactionScoutProfile?> GetProfileAsync(long factionId, CancellationToken ct)
    {
        if (factionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factionId), factionId, "Faction id must be positive.");
        }

        var wars = await repository.GetWarsByFactionAsync(factionId, ct);
        if (wars.Count == 0)
        {
            return null;
        }

        var members = await repository.GetReportMembersByFactionAsync(factionId, ct);
        var factionName = ResolveFactionName(factionId, wars, members);
        var profile = OpponentProfileEngine.BuildProfile(factionId, factionName, wars, members);
        var backfillState = await backfillStateRepository.GetLatestAsync(ct);

        return profile with { Evidence = ToEvidenceMetadata(backfillState) };
    }

    private static WarScoutEvidenceMetadata ToEvidenceMetadata(RankedWarHistoryBackfillStateEntity? state)
    {
        if (state is null)
        {
            return WarScoutEvidenceMetadata.NotStarted;
        }

        var status = string.IsNullOrWhiteSpace(state.Status)
            ? RankedWarHistoryBackfillStatus.NotStarted
            : state.Status;

        return new WarScoutEvidenceMetadata(
            BackfillStatus: status,
            PagesProcessed: state.PagesProcessed,
            ReportsProcessed: state.ReportsProcessed,
            UpdatedAtUtc: state.UpdatedAtUtc,
            LastSuccessAtUtc: state.LastSuccessAtUtc,
            IsComplete: string.Equals(status, RankedWarHistoryBackfillStatus.Completed, StringComparison.Ordinal));
    }

    private static string ResolveFactionName(
        long factionId,
        IReadOnlyList<RankedWarHistoryEntity> wars,
        IReadOnlyList<RankedWarReportMemberEntity> members)
    {
        // Prefer the most recently captured report-member row's faction name, since it reflects
        // the faction's current name at the time of that report; fall back to the history row.
        var latestMemberRow = members
            .Where(m => m.FactionId == factionId)
            .OrderByDescending(m => m.CapturedAtUtc)
            .FirstOrDefault();

        if (latestMemberRow is not null)
        {
            return latestMemberRow.FactionName;
        }

        var latestWar = wars.OrderByDescending(w => w.StartedAtUtc).First();
        return latestWar.FactionId == factionId ? latestWar.FactionName : latestWar.OpponentFactionName;
    }
}
