using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Data.Repositories;

public sealed class WarHistoryRepository(HappyGymStatsDbContext db) : IWarHistoryRepository
{
    public Task<RankedWarHistoryEntity?> GetWarAsync(long warId, CancellationToken ct)
    {
        EnsurePositive(nameof(warId), warId);
        return db.RankedWarHistory
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.WarId == warId, ct);
    }

    public async Task UpsertWarAsync(RankedWarHistoryEntity war, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(war);
        ValidateWar(war);

        var tracked = db.RankedWarHistory.Local.FirstOrDefault(e => e.WarId == war.WarId);
        if (tracked is not null)
        {
            CopyWar(war, tracked);
            return;
        }

        var existing = await db.RankedWarHistory
            .SingleOrDefaultAsync(e => e.WarId == war.WarId, ct);

        if (existing is null)
        {
            db.RankedWarHistory.Add(war);
            return;
        }

        CopyWar(war, existing);
    }

    public async Task<IReadOnlyList<RankedWarReportMemberEntity>> GetReportMembersAsync(long warId, long factionId, CancellationToken ct)
    {
        EnsurePositive(nameof(warId), warId);
        EnsurePositive(nameof(factionId), factionId);

        return await db.RankedWarReportMembers
            .AsNoTracking()
            .Where(e => e.WarId == warId && e.FactionId == factionId)
            .OrderBy(e => e.MemberId)
            .ToListAsync(ct);
    }

    public async Task ReplaceReportMembersAsync(
        long warId,
        DateTimeOffset capturedAtUtc,
        DateTimeOffset ingestedAtUtc,
        IReadOnlyCollection<RankedWarReportMemberEntity> members,
        CancellationToken ct)
    {
        EnsurePositive(nameof(warId), warId);
        ArgumentNullException.ThrowIfNull(members);

        foreach (var member in members)
        {
            ValidateMember(warId, member);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var war = await db.RankedWarHistory.SingleOrDefaultAsync(e => e.WarId == warId, ct)
            ?? throw new InvalidOperationException($"Cannot replace ranked war report members for war {warId} before the war history row exists.");

        war.ReportCapturedAtUtc = capturedAtUtc;
        war.ReportIngestedAtUtc = ingestedAtUtc;

        var trackedMembers = db.RankedWarReportMembers.Local.Where(e => e.WarId == warId).ToList();
        if (trackedMembers.Count > 0)
        {
            db.RankedWarReportMembers.RemoveRange(trackedMembers);
        }

        var existingMembers = await db.RankedWarReportMembers
            .Where(e => e.WarId == warId)
            .ToListAsync(ct);

        if (existingMembers.Count > 0)
        {
            db.RankedWarReportMembers.RemoveRange(existingMembers);
        }

        db.RankedWarReportMembers.AddRange(members);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public Task<bool> HasCapturedReportAsync(long warId, CancellationToken ct)
    {
        EnsurePositive(nameof(warId), warId);
        return db.RankedWarHistory
            .AsNoTracking()
            .AnyAsync(e => e.WarId == warId && e.ReportCapturedAtUtc != null, ct);
    }

    public async Task<IReadOnlyList<RankedWarHistoryEntity>> GetWarsByFactionAsync(long factionId, CancellationToken ct)
    {
        EnsurePositive(nameof(factionId), factionId);

        return await db.RankedWarHistory
            .AsNoTracking()
            .Where(e => (e.FactionId == factionId || e.OpponentFactionId == factionId) && e.ReportCapturedAtUtc != null)
            .OrderByDescending(e => e.StartedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RankedWarReportMemberEntity>> GetReportMembersByFactionAsync(long factionId, CancellationToken ct)
    {
        EnsurePositive(nameof(factionId), factionId);

        return await db.RankedWarReportMembers
            .AsNoTracking()
            .Where(e => e.FactionId == factionId)
            .OrderBy(e => e.MemberId)
            .ThenBy(e => e.CapturedAtUtc)
            .ToListAsync(ct);
    }

    private static void ValidateWar(RankedWarHistoryEntity war)
    {
        EnsurePositive(nameof(war.WarId), war.WarId);
        EnsurePositive(nameof(war.FactionId), war.FactionId);
        EnsurePositive(nameof(war.OpponentFactionId), war.OpponentFactionId);
        EnsureNotBlank(nameof(war.FactionName), war.FactionName);
        EnsureNotBlank(nameof(war.OpponentFactionName), war.OpponentFactionName);

        if (war.WinnerFactionId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(war.WinnerFactionId), "Winner faction id must be positive when present.");
        }
    }

    private static void ValidateMember(long warId, RankedWarReportMemberEntity member)
    {
        ArgumentNullException.ThrowIfNull(member);
        EnsurePositive(nameof(member.WarId), member.WarId);
        EnsurePositive(nameof(member.FactionId), member.FactionId);
        EnsurePositive(nameof(member.MemberId), member.MemberId);
        EnsureNotBlank(nameof(member.FactionName), member.FactionName);
        EnsureNotBlank(nameof(member.MemberName), member.MemberName);

        if (member.WarId != warId)
        {
            throw new ArgumentException($"Report member war id {member.WarId} does not match requested war id {warId}.", nameof(member));
        }
    }

    private static void EnsurePositive(string paramName, long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Value must be positive.");
        }
    }

    private static void EnsureNotBlank(string paramName, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }
    }

    private static void CopyWar(RankedWarHistoryEntity source, RankedWarHistoryEntity target)
    {
        target.FactionId = source.FactionId;
        target.FactionName = source.FactionName;
        target.OpponentFactionId = source.OpponentFactionId;
        target.OpponentFactionName = source.OpponentFactionName;
        target.StartedAtUtc = source.StartedAtUtc;
        target.EndedAtUtc = source.EndedAtUtc;
        target.WinnerFactionId = source.WinnerFactionId;
        target.FactionScore = source.FactionScore;
        target.FactionChain = source.FactionChain;
        target.OpponentScore = source.OpponentScore;
        target.OpponentChain = source.OpponentChain;
        target.Status = source.Status;
        target.CapturedAtUtc = source.CapturedAtUtc;
        target.IngestedAtUtc = source.IngestedAtUtc;
        target.ReportCapturedAtUtc = source.ReportCapturedAtUtc;
        target.ReportIngestedAtUtc = source.ReportIngestedAtUtc;
    }
}
