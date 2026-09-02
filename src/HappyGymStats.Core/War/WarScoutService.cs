using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

public sealed class WarScoutService(IWarHistoryRepository repository)
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

        return OpponentProfileEngine.BuildProfile(factionId, factionName, wars, members);
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
