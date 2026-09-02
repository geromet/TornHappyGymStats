using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

public sealed class WarHistoryIngestWriter(IWarHistoryRepository repository, IUnitOfWork unitOfWork) : IWarHistoryIngestWriter
{
    public async Task<IReadOnlyList<RankedWarHistoryEntity>> WriteHistoryPageAsync(
        RankedWarHistoryPageResponse page,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);

        var rows = page.Wars.Select(war => MapHistoryEntry(war, capturedAtUtc, ingestedAtUtc)).ToArray();
        foreach (var row in rows)
        {
            await repository.UpsertWarAsync(row, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<RankedWarReportMemberEntity>> WriteReportAsync(
        RankedWarReportResponse report,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        EnsurePositive(nameof(report.War.WarId), report.War.WarId);

        var existingWar = await repository.GetWarAsync(report.War.WarId, ct)
            ?? throw new InvalidOperationException($"Ranked war {report.War.WarId} must exist before persisting report members.");

        var idleAttackers = ValidateIdleAttackers(report.IdleAttackers);
        var members = MapReportMembers(report, idleAttackers, capturedAtUtc, ingestedAtUtc);
        var updatedWar = MapReportWar(existingWar, report, capturedAtUtc, ingestedAtUtc);

        await repository.UpsertWarAsync(updatedWar, ct);
        await repository.ReplaceReportMembersAsync(report.War.WarId, capturedAtUtc, ingestedAtUtc, members, ct);

        return members;
    }

    private static RankedWarHistoryEntity MapHistoryEntry(
        RankedWarHistoryEntry war,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(war);
        EnsurePositive(nameof(war.WarId), war.WarId);
        EnsurePositive(nameof(war.FactionId), war.FactionId);
        EnsurePositive(nameof(war.OpponentId), war.OpponentId);
        EnsureRequired(nameof(war.FactionName), war.FactionName);
        EnsureRequired(nameof(war.OpponentName), war.OpponentName);

        return new RankedWarHistoryEntity
        {
            WarId = war.WarId,
            FactionId = war.FactionId,
            FactionName = war.FactionName.Trim(),
            OpponentFactionId = war.OpponentId,
            OpponentFactionName = war.OpponentName.Trim(),
            StartedAtUtc = war.Start,
            EndedAtUtc = war.End,
            WinnerFactionId = war.WinnerFactionId,
            FactionScore = war.Score,
            FactionChain = war.Chain,
            OpponentScore = war.OpponentScore,
            OpponentChain = war.OpponentChain,
            Status = NormalizeOptional(war.Status),
            CapturedAtUtc = capturedAtUtc,
            IngestedAtUtc = ingestedAtUtc,
        };
    }

    private static RankedWarHistoryEntity MapReportWar(
        RankedWarHistoryEntity existing,
        RankedWarReportResponse report,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc)
    {
        var factions = ValidateFactions(report.Factions);
        var factionById = factions.ToDictionary(f => f.FactionId);

        if (!factionById.TryGetValue(existing.FactionId, out var ownFaction))
        {
            throw new InvalidOperationException(
                $"Ranked war {existing.WarId} report did not include expected faction {existing.FactionId}.");
        }

        if (!factionById.TryGetValue(existing.OpponentFactionId, out var opponentFaction))
        {
            throw new InvalidOperationException(
                $"Ranked war {existing.WarId} report did not include expected opponent faction {existing.OpponentFactionId}.");
        }

        return new RankedWarHistoryEntity
        {
            WarId = existing.WarId,
            FactionId = existing.FactionId,
            FactionName = ownFaction.Name.Trim(),
            OpponentFactionId = existing.OpponentFactionId,
            OpponentFactionName = opponentFaction.Name.Trim(),
            StartedAtUtc = report.War.Start,
            EndedAtUtc = report.War.End,
            WinnerFactionId = report.War.WinnerFactionId,
            FactionScore = ownFaction.Score,
            FactionChain = ownFaction.Chain,
            OpponentScore = opponentFaction.Score,
            OpponentChain = opponentFaction.Chain,
            Status = NormalizeOptional(report.War.Status),
            CapturedAtUtc = capturedAtUtc,
            IngestedAtUtc = ingestedAtUtc,
            ReportCapturedAtUtc = capturedAtUtc,
            ReportIngestedAtUtc = ingestedAtUtc,
        };
    }

    private static RankedWarReportMemberEntity[] MapReportMembers(
        RankedWarReportResponse report,
        HashSet<long> idleAttackers,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc)
    {
        var factions = ValidateFactions(report.Factions);
        var seenMembers = new HashSet<(long FactionId, long MemberId)>();
        var members = new List<RankedWarReportMemberEntity>();

        foreach (var faction in factions)
        {
            foreach (var member in faction.Members)
            {
                ArgumentNullException.ThrowIfNull(member);
                EnsurePositive(nameof(member.UserId), member.UserId);
                EnsureRequired(nameof(member.Name), member.Name);

                var key = (faction.FactionId, member.UserId);
                if (!seenMembers.Add(key))
                {
                    throw new InvalidOperationException(
                        $"Ranked war {report.War.WarId} report contains duplicate member {member.UserId} in faction {faction.FactionId}.");
                }

                members.Add(new RankedWarReportMemberEntity
                {
                    WarId = report.War.WarId,
                    FactionId = faction.FactionId,
                    FactionName = faction.Name.Trim(),
                    MemberId = member.UserId,
                    MemberName = member.Name.Trim(),
                    Score = member.Score,
                    Chain = member.Chain,
                    Attacks = member.Attacks,
                    StatusState = NormalizeOptional(member.Status?.State),
                    StatusUntilUtc = member.Status?.Until,
                    IsIdleAttacker = idleAttackers.Contains(member.UserId),
                    CapturedAtUtc = capturedAtUtc,
                    IngestedAtUtc = ingestedAtUtc,
                });
            }
        }

        return members.ToArray();
    }

    private static RankedWarFactionReport[] ValidateFactions(IReadOnlyList<RankedWarFactionReport> factions)
    {
        ArgumentNullException.ThrowIfNull(factions);

        if (factions.Count == 0)
        {
            throw new InvalidOperationException("Ranked war report must include at least one faction.");
        }

        var seenFactionIds = new HashSet<long>();
        foreach (var faction in factions)
        {
            ArgumentNullException.ThrowIfNull(faction);
            EnsurePositive(nameof(faction.FactionId), faction.FactionId);
            EnsureRequired(nameof(faction.Name), faction.Name);

            if (!seenFactionIds.Add(faction.FactionId))
            {
                throw new InvalidOperationException($"Ranked war report contains duplicate faction {faction.FactionId}.");
            }
        }

        return factions.ToArray();
    }

    private static HashSet<long> ValidateIdleAttackers(IReadOnlyList<long> idleAttackers)
    {
        ArgumentNullException.ThrowIfNull(idleAttackers);
        var seen = new HashSet<long>();
        foreach (var attackerId in idleAttackers)
        {
            EnsurePositive(nameof(idleAttackers), attackerId);
            seen.Add(attackerId);
        }

        return seen;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsurePositive(string paramName, long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
        }
    }

    private static void EnsureRequired(string paramName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }
    }
}
