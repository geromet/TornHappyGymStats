using HappyGymStats.Core.Models;
using HappyGymStats.Core.Repositories;

namespace HappyGymStats.Core.Services;

public sealed class FactionService(
    IFactionMembershipRepository memberRepo,
    IUserLogEntryRepository logRepo)
{
    public async Task<IReadOnlyList<FactionMemberSummaryDto>> GetMemberSummariesAsync(
        Guid factionAnonymousId,
        CancellationToken ct)
    {
        var memberIds = await memberRepo.GetMemberAnonymousIdsAsync(factionAnonymousId, ct);
        if (memberIds.Count == 0)
            return [];

        var summaries = await logRepo.GetGymTrainSummariesAsync(memberIds, ct);

        return memberIds
            .Select(id =>
            {
                var s = summaries.GetValueOrDefault(id);
                return new FactionMemberSummaryDto(id, s?.TrainCount ?? 0, s?.LastTrainAtUtc);
            })
            .ToList();
    }
}
