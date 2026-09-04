namespace HappyGymStats.Core.War;

/// <summary>
/// The kinds of event a post-war ledger can carry, covering everything listed in the war-accounting
/// scope: war hits, assists, outside/filler chain hits, chain saves, milestone-crossing lumps,
/// push-window participation, retaliation hits, energy expenditure, faction-provided Xanax, faction
/// points/refills, meds, bounties, revive-contract costs, other manual expenses, manual adjustments,
/// cache settlement and termed-war settlement. Raw source events are immutable; money is assigned
/// later by a payout policy, not by the ledger itself.
/// </summary>
public enum WarLedgerEntryKind
{
    WarHit,
    Assist,
    OutsideHit,
    ChainSave,
    MilestoneBonus,
    PushWindowParticipation,
    RetaliationHit,
    EnergyExpenditure,
    FactionXanax,
    FactionPoints,
    Meds,
    Bounty,
    ReviveContract,
    ManualExpense,
    ManualAdjustment,
    CacheSettlement,
    TermedWarSettlement,
}

/// <summary>
/// The accounting bucket an entry belongs to. Earned contribution is what a member did for the war;
/// expenses are money the faction reimburses; manual adjustments are signed corrections that require
/// an actor, a timestamp and a reason; settlement entries move value between the faction, its members
/// and the opposing faction (cache buy-outs, termed-war settlements). Buckets are what keep
/// "what was earned" auditable separately from "what was spent and adjusted".
/// </summary>
public enum WarLedgerCategory
{
    EarnedContribution,
    Expense,
    ManualAdjustment,
    Settlement,
}

/// <summary>
/// Static catalogue mapping every <see cref="WarLedgerEntryKind"/> to its accounting bucket. Kept in
/// one place so the ledger, the payout engine and the exporter cannot disagree about what a kind
/// means.
/// </summary>
public static class WarLedgerKindCatalog
{
    public static WarLedgerCategory CategoryOf(WarLedgerEntryKind kind) => kind switch
    {
        WarLedgerEntryKind.WarHit
            or WarLedgerEntryKind.Assist
            or WarLedgerEntryKind.OutsideHit
            or WarLedgerEntryKind.ChainSave
            or WarLedgerEntryKind.MilestoneBonus
            or WarLedgerEntryKind.PushWindowParticipation
            or WarLedgerEntryKind.RetaliationHit
            or WarLedgerEntryKind.EnergyExpenditure => WarLedgerCategory.EarnedContribution,

        WarLedgerEntryKind.FactionXanax
            or WarLedgerEntryKind.FactionPoints
            or WarLedgerEntryKind.Meds
            or WarLedgerEntryKind.Bounty
            or WarLedgerEntryKind.ReviveContract
            or WarLedgerEntryKind.ManualExpense => WarLedgerCategory.Expense,

        WarLedgerEntryKind.ManualAdjustment => WarLedgerCategory.ManualAdjustment,

        WarLedgerEntryKind.CacheSettlement
            or WarLedgerEntryKind.TermedWarSettlement => WarLedgerCategory.Settlement,

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ledger entry kind."),
    };

    public static bool IsEarned(WarLedgerEntryKind kind)
        => CategoryOf(kind) == WarLedgerCategory.EarnedContribution;

    public static bool IsExpense(WarLedgerEntryKind kind)
        => CategoryOf(kind) == WarLedgerCategory.Expense;
}

/// <summary>
/// One immutable raw event in the war ledger. Earned entries carry facts (<see cref="Respect"/>,
/// <see cref="Count"/>) rather than money; expense, adjustment and settlement entries carry the money
/// value in <see cref="Amount"/>. Manual adjustments must name an actor (<see cref="ActorId"/>,
/// <see cref="ActorName"/>) and a <see cref="Reason"/>. <see cref="SourceReference"/> pins the entry
/// to the report row or external event it came from, so an audit can walk back to the source.
/// </summary>
public sealed record WarLedgerEntry(
    long MemberId,
    string MemberName,
    WarLedgerEntryKind Kind,
    string Description,
    decimal? Amount,
    decimal? Respect,
    int? Count,
    DateTimeOffset? OccurredAtUtc,
    string? ActorId,
    string? ActorName,
    string? Reason,
    string? SourceReference)
{
    public WarLedgerCategory Category => WarLedgerKindCatalog.CategoryOf(Kind);

    public bool IsManualAdjustment => Kind == WarLedgerEntryKind.ManualAdjustment;

    /// <summary>A human-readable "you received X because..." explanation for this entry.</summary>
    public string Explain()
    {
        var prefix = Description;
        return Kind switch
        {
            WarLedgerEntryKind.WarHit => $"{prefix} — {Count} hit(s), {Respect} respect.",
            WarLedgerEntryKind.ManualAdjustment => $"{prefix} ({Amount}): {Reason} (by {ActorName ?? ActorId}).",
            _ => string.IsNullOrWhiteSpace(Reason) ? prefix : $"{prefix} ({Reason}).",
        };
    }
}

/// <summary>
/// A value that flows INTO the available pool rather than out of it: liquidation of faction-owned
/// cache, or a termed-war settlement received from the opposing faction. Kept off the entry list so
/// the distribution math (member payouts + reserve + settlement paid) reconciles against the pool
/// without sign ambiguity.
/// </summary>
public sealed record WarLedgerPoolCredit(string Description, decimal Amount);

/// <summary>
/// The immutable, reconciled event ledger for one completed ranked war. Built once from the raw
/// ranked-war report rows and supplemental event records; it is never mutated after construction.
/// <see cref="Totals"/> reconciles the entries back to the source rows so a completed war can be
/// audited at a glance.
/// </summary>
public sealed record WarLedger(
    long WarId,
    long FactionId,
    string FactionName,
    long OpponentFactionId,
    string OpponentFactionName,
    bool IsTermed,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<WarLedgerEntry> Entries,
    IReadOnlyList<WarLedgerPoolCredit> PoolCredits)
{
    public WarLedgerTotals Totals => WarLedgerTotals.From(this);
}

/// <summary>
/// Fact totals of a ledger, summed across categories so earned contribution, expenses, manual
/// adjustments and settlement are separately visible. <see cref="TotalRespect"/> and the hit counts
/// reconcile against the ranked-war report rows the ledger was built from.
/// </summary>
public sealed record WarLedgerTotals(
    int MemberCount,
    decimal TotalRespect,
    int WarHitCount,
    int AssistCount,
    int OutsideHitCount,
    decimal TotalExpenses,
    decimal TotalManualAdjustments,
    decimal TotalSettlement,
    decimal TotalPoolCredits)
{
    public static WarLedgerTotals From(WarLedger ledger)
    {
        var earned = ledger.Entries.Where(e => e.Category == WarLedgerCategory.EarnedContribution).ToArray();
        var expenses = ledger.Entries.Where(e => e.Category == WarLedgerCategory.Expense).ToArray();

        return new WarLedgerTotals(
            MemberCount: ledger.Entries.Select(e => e.MemberId).Distinct().Count(),
            TotalRespect: earned.Sum(e => e.Respect ?? 0m),
            WarHitCount: earned.Where(e => e.Kind == WarLedgerEntryKind.WarHit).Sum(e => e.Count ?? 0),
            AssistCount: earned.Where(e => e.Kind == WarLedgerEntryKind.Assist).Sum(e => e.Count ?? 0),
            OutsideHitCount: earned.Where(e => e.Kind == WarLedgerEntryKind.OutsideHit).Sum(e => e.Count ?? 0),
            TotalExpenses: expenses.Sum(e => e.Amount ?? 0m),
            TotalManualAdjustments: ledger.Entries
                .Where(e => e.Category == WarLedgerCategory.ManualAdjustment)
                .Sum(e => e.Amount ?? 0m),
            TotalSettlement: ledger.Entries
                .Where(e => e.Category == WarLedgerCategory.Settlement)
                .Sum(e => e.Amount ?? 0m),
            TotalPoolCredits: ledger.PoolCredits.Sum(c => c.Amount));
    }
}
