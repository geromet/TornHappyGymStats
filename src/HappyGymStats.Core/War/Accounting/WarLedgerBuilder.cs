using HappyGymStats.Data.Entities;

namespace HappyGymStats.Core.War;

/// <summary>
/// Builds the immutable <see cref="WarLedger"/> for a completed ranked war from the ranked-war
/// report rows and any supplemental event records. The builder validates that the source events are
/// complete (the war has ended), that the report rows belong to the war and faction being accounted,
/// and that manual adjustments carry the actor/timestamp/reason the accounting principles demand.
/// The ledger is a pure value: the same source rows fed to the same inputs produce the same ledger.
/// </summary>
public static class WarLedgerBuilder
{
    public static WarLedger Build(
        RankedWarHistoryEntity war,
        IReadOnlyList<RankedWarReportMemberEntity> members,
        IReadOnlyList<WarLedgerSupplement>? supplements = null,
        IReadOnlyList<ManualAdjustmentInput>? adjustments = null,
        IReadOnlyList<CacheSettlementInput>? cacheItems = null,
        TermedSettlementInput? termedSettlement = null,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(war);
        ArgumentNullException.ThrowIfNull(members);

        if (war.EndedAtUtc is null)
        {
            throw new InvalidOperationException(
                "A payout ledger can only be built from a completed ranked war (EndedAtUtc must be set).");
        }

        var factionRows = members
            .Where(m => m.WarId == war.WarId && m.FactionId == war.FactionId)
            .GroupBy(m => m.MemberId)
            .Select(g => g.OrderByDescending(r => r.CapturedAtUtc).First())
            .ToArray();

        var entries = new List<WarLedgerEntry>();
        var poolCredits = new List<WarLedgerPoolCredit>();

        foreach (var member in factionRows)
        {
            if (member.Score <= 0 && member.Attacks <= 0)
            {
                continue;
            }

            entries.Add(new WarLedgerEntry(
                member.MemberId,
                member.MemberName,
                WarLedgerEntryKind.WarHit,
                $"War hit report: {member.Attacks} hit(s), {member.Score} respect",
                Amount: null,
                Respect: member.Score,
                Count: member.Attacks,
                OccurredAtUtc: war.EndedAtUtc,
                ActorId: null,
                ActorName: null,
                Reason: null,
                SourceReference: $"rankedwarreport:{war.WarId}:{member.MemberId}"));
        }

        foreach (var supplement in supplements ?? [])
        {
            ValidateSupplement(supplement);

            entries.Add(new WarLedgerEntry(
                supplement.MemberId,
                supplement.MemberName,
                supplement.Kind,
                DescribeSupplement(supplement),
                supplement.Amount,
                supplement.Respect,
                supplement.Count,
                supplement.OccurredAtUtc,
                ActorId: null,
                ActorName: null,
                Reason: null,
                supplement.SourceReference));
        }

        foreach (var adjustment in adjustments ?? [])
        {
            ValidateAdjustment(adjustment);

            entries.Add(new WarLedgerEntry(
                adjustment.MemberId,
                adjustment.MemberName,
                WarLedgerEntryKind.ManualAdjustment,
                "Manual adjustment",
                adjustment.Amount,
                Respect: null,
                Count: null,
                OccurredAtUtc: adjustment.TimestampUtc,
                ActorId: adjustment.ActorId,
                ActorName: adjustment.ActorName,
                Reason: adjustment.Reason,
                SourceReference: null));
        }

        foreach (var cache in cacheItems ?? [])
        {
            ValidateCacheItem(cache);

            if (cache.MemberId is null)
            {
                poolCredits.Add(new WarLedgerPoolCredit(
                    $"Faction cache liquidation: {cache.Quantity} x {cache.ItemName}", cache.TotalValue));
            }
            else
            {
                entries.Add(new WarLedgerEntry(
                    cache.MemberId.Value,
                    cache.MemberName ?? string.Empty,
                    WarLedgerEntryKind.CacheSettlement,
                    $"Cache settlement: {cache.Quantity} x {cache.ItemName}",
                    cache.TotalValue,
                    Respect: null,
                    Count: cache.Quantity,
                    OccurredAtUtc: null,
                    ActorId: null,
                    ActorName: null,
                    Reason: null,
                    SourceReference: null));
            }
        }

        if (termedSettlement is not null)
        {
            if (termedSettlement.Amount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(termedSettlement), termedSettlement.Amount, "A termed-war settlement amount must be positive.");
            }

            if (termedSettlement.Direction == TermedSettlementDirection.Received)
            {
                poolCredits.Add(new WarLedgerPoolCredit(
                    "Termed-war settlement received from " + war.OpponentFactionName, termedSettlement.Amount));
            }
            else
            {
                entries.Add(new WarLedgerEntry(
                    0,
                    war.OpponentFactionName,
                    WarLedgerEntryKind.TermedWarSettlement,
                    $"Termed-war settlement paid to {war.OpponentFactionName}",
                    termedSettlement.Amount,
                    Respect: null,
                    Count: null,
                    OccurredAtUtc: null,
                    ActorId: null,
                    ActorName: null,
                    Reason: termedSettlement.Note,
                    SourceReference: null));
            }
        }

        return new WarLedger(
            war.WarId,
            war.FactionId,
            war.FactionName,
            war.OpponentFactionId,
            war.OpponentFactionName,
            IsTermed: termedSettlement is not null,
            GeneratedAtUtc: generatedAtUtc ?? DateTimeOffset.UtcNow,
            Entries: entries,
            PoolCredits: poolCredits);
    }

    private static string DescribeSupplement(WarLedgerSupplement supplement) => supplement.Kind switch
    {
        WarLedgerEntryKind.Assist => $"Assists: {supplement.Count ?? 0}",
        WarLedgerEntryKind.OutsideHit => $"Outside/filler hits: {supplement.Count ?? 0}",
        WarLedgerEntryKind.ChainSave => $"Chain save: {supplement.Count ?? 0}",
        WarLedgerEntryKind.MilestoneBonus => $"Milestone bonus: {supplement.Respect ?? 0}",
        WarLedgerEntryKind.PushWindowParticipation => $"Push-window participation: {supplement.Count ?? 0}",
        WarLedgerEntryKind.RetaliationHit => $"Retaliation hits: {supplement.Count ?? 0}",
        WarLedgerEntryKind.EnergyExpenditure => $"Energy expended: {supplement.Count ?? 0}",
        WarLedgerEntryKind.FactionXanax => "Faction-provided Xanax",
        WarLedgerEntryKind.FactionPoints => "Faction points/refills",
        WarLedgerEntryKind.Meds => "Meds",
        WarLedgerEntryKind.Bounty => "Bounty",
        WarLedgerEntryKind.ReviveContract => "Revive-contract cost",
        WarLedgerEntryKind.ManualExpense => "Manually approved war expense",
        _ => supplement.Kind.ToString(),
    };

    private static void ValidateSupplement(WarLedgerSupplement supplement)
    {
        ArgumentNullException.ThrowIfNull(supplement);
        if (supplement.MemberId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(supplement), "Supplement member id must be positive.");
        }

        if (WarLedgerKindCatalog.CategoryOf(supplement.Kind) == WarLedgerCategory.Settlement
            || supplement.Kind == WarLedgerEntryKind.ManualAdjustment)
        {
            throw new ArgumentException(
                $"Kind {supplement.Kind} has a dedicated input and cannot be supplied as a supplement.", nameof(supplement));
        }

        if (WarLedgerKindCatalog.IsExpense(supplement.Kind) && supplement.Amount is not > 0)
        {
            throw new ArgumentException(
                $"Expense supplement {supplement.Kind} must carry a positive Amount.", nameof(supplement));
        }
    }

    private static void ValidateAdjustment(ManualAdjustmentInput adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        if (string.IsNullOrWhiteSpace(adjustment.ActorId)
            || string.IsNullOrWhiteSpace(adjustment.ActorName)
            || string.IsNullOrWhiteSpace(adjustment.Reason))
        {
            throw new ArgumentException(
                "A manual adjustment requires an actor id, an actor name and a reason.", nameof(adjustment));
        }
    }

    private static void ValidateCacheItem(CacheSettlementInput cache)
    {
        ArgumentNullException.ThrowIfNull(cache);

        if (cache.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cache), "Cache quantity must be positive.");
        }

        if (cache.UnitValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cache), "Cache unit value must be positive.");
        }

        if (cache.MemberId is null && string.IsNullOrWhiteSpace(cache.MemberName))
        {
            return;
        }

        if (cache.MemberId is > 0 && string.IsNullOrWhiteSpace(cache.MemberName))
        {
            throw new ArgumentException(
                "A member-held cache item must name its member.", nameof(cache));
        }
    }
}
