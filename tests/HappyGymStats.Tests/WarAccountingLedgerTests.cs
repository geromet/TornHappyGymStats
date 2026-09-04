using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Acceptance 1 and the accounting principles for the war ledger: a completed ranked war produces a
/// reconciled member/event ledger, raw source events stay immutable, and manual adjustments cannot be
/// anonymous.
/// </summary>
public sealed class WarAccountingLedgerTests
{
    private const long WarId = 48377;
    private const long AlphaId = 1001;
    private const long BetaId = 1002;
    private const long IdleId = 1003;

    [Fact]
    public void Completed_war_produces_a_reconciled_member_event_ledger()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
            WarAccountingTestData.ReportMember(WarId, BetaId, "Beta", score: 260, attacks: 30),
            WarAccountingTestData.ReportMember(WarId, IdleId, "Idle", score: 0, attacks: 0),
        };

        var ledger = WarLedgerBuilder.Build(war, members);

        Assert.Equal(WarId, ledger.WarId);
        Assert.Equal(WarAccountingTestData.FactionId, ledger.FactionId);

        var warHits = ledger.Entries.Where(e => e.Kind == WarLedgerEntryKind.WarHit).ToArray();
        Assert.Equal(2, warHits.Length);
        Assert.DoesNotContain(warHits, e => e.MemberId == IdleId);

        // The ledger reconciles back to the source rows: every respect point and every hit
        // that the report attributed to a member is in the ledger, no more, no less.
        Assert.Equal(615m, ledger.Totals.TotalRespect);
        Assert.Equal(70, ledger.Totals.WarHitCount);
        Assert.Equal(2, ledger.Totals.MemberCount);
        Assert.Equal(members.Where(m => m.MemberId != IdleId).Sum(m => m.Score), ledger.Totals.TotalRespect);
        Assert.Equal(members.Where(m => m.MemberId != IdleId).Sum(m => m.Attacks), ledger.Totals.WarHitCount);

        // Every war-hit entry is pinned to the report row it came from.
        Assert.All(warHits, e => Assert.Equal($"rankedwarreport:{WarId}:{e.MemberId}", e.SourceReference));
    }

    [Fact]
    public void Build_rejects_a_war_that_has_not_ended()
    {
        var war = WarAccountingTestData.UnfinishedWar(WarId);

        var exception = Assert.Throws<InvalidOperationException>(
            () => WarLedgerBuilder.Build(war, []));

        Assert.Contains("completed ranked war", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ignores_report_rows_from_other_wars_and_factions()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
            WarAccountingTestData.ReportMember(WarId + 1, 2001, "Other war", score: 999, attacks: 9),
            WarAccountingTestData.ReportMember(WarId, 2002, "Other faction", score: 888, attacks: 8,
                factionId: WarAccountingTestData.OpponentFactionId),
        };

        var ledger = WarLedgerBuilder.Build(war, members);

        var memberIds = ledger.Entries.Select(e => e.MemberId).ToArray();
        Assert.DoesNotContain(2001L, memberIds);
        Assert.DoesNotContain(2002L, memberIds);
        Assert.Contains(AlphaId, memberIds);
    }

    [Fact]
    public void Supplements_expenses_and_adjustments_are_separately_visible_from_earned_contribution()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
            WarAccountingTestData.ReportMember(WarId, BetaId, "Beta", score: 260, attacks: 30),
        };
        var supplements = new List<WarLedgerSupplement>
        {
            new(AlphaId, "Alpha", WarLedgerEntryKind.OutsideHit, Count: 5, Respect: 12),
            new(AlphaId, "Alpha", WarLedgerEntryKind.Assist, Count: 3),
            new(BetaId, "Beta", WarLedgerEntryKind.FactionXanax, Amount: 250m),
            new(BetaId, "Beta", WarLedgerEntryKind.EnergyExpenditure, Count: 1000),
            new(BetaId, "Beta", WarLedgerEntryKind.MilestoneBonus, Respect: 160),
        };
        var adjustments = new List<ManualAdjustmentInput>
        {
            new(AlphaId, "Alpha", Amount: 100m, "LD1", "Treasurer", new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero), "Filler bonus approved by leadership"),
        };

        var ledger = WarLedgerBuilder.Build(war, members, supplements, adjustments);

        var earned = ledger.Entries.Where(e => e.Category == WarLedgerCategory.EarnedContribution).ToArray();
        var expenses = ledger.Entries.Where(e => e.Category == WarLedgerCategory.Expense).ToArray();
        var manual = ledger.Entries.Where(e => e.Category == WarLedgerCategory.ManualAdjustment).ToArray();

        Assert.Contains(earned, e => e.Kind == WarLedgerEntryKind.OutsideHit);
        Assert.Contains(earned, e => e.Kind == WarLedgerEntryKind.Assist);
        Assert.Contains(earned, e => e.Kind == WarLedgerEntryKind.EnergyExpenditure);
        Assert.Contains(earned, e => e.Kind == WarLedgerEntryKind.MilestoneBonus);
        Assert.Single(expenses);
        Assert.Equal(250m, expenses[0].Amount);
        Assert.Single(manual);
        Assert.Equal(100m, manual[0].Amount);

        // Buckets are kept apart so "earned" is auditable without the noise of spending.
        Assert.Equal(250m, ledger.Totals.TotalExpenses);
        Assert.Equal(100m, ledger.Totals.TotalManualAdjustments);
        Assert.Equal(5, ledger.Totals.OutsideHitCount);
        Assert.Equal(3, ledger.Totals.AssistCount);

        // The adjustment names its actor, timestamp and reason — the "not anonymous" rule.
        var adjustment = manual[0];
        Assert.Equal("LD1", adjustment.ActorId);
        Assert.Equal("Treasurer", adjustment.ActorName);
        Assert.Equal(new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero), adjustment.OccurredAtUtc);
        Assert.Equal("Filler bonus approved by leadership", adjustment.Reason);
    }

    [Theory]
    [InlineData("", "Treasurer", "reason")]
    [InlineData("LD1", "", "reason")]
    [InlineData("LD1", "Treasurer", "")]
    public void Manual_adjustments_require_actor_timestamp_and_reason(string actorId, string actorName, string reason)
    {
        var war = WarAccountingTestData.CompletedWar(WarId);

        var adjustment = new ManualAdjustmentInput(
            AlphaId, "Alpha", 100m, actorId, actorName, DateTimeOffset.UtcNow, reason);

        Assert.Throws<ArgumentException>(() => WarLedgerBuilder.Build(war, [], adjustments: [adjustment]));
    }

    [Fact]
    public void Supplement_expenses_must_carry_a_positive_amount()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);

        var supplement = new WarLedgerSupplement(AlphaId, "Alpha", WarLedgerEntryKind.FactionXanax, Amount: 0m);

        Assert.Throws<ArgumentException>(() => WarLedgerBuilder.Build(war, [], supplements: [supplement]));
    }

    [Fact]
    public void Cache_and_termed_settlements_flow_into_pool_credits_or_member_settlement_lines()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
        };

        var ledger = WarLedgerBuilder.Build(
            war,
            members,
            cacheItems:
            [
                new CacheSettlementInput(MemberId: null, MemberName: null, ItemName: "Xanax", Quantity: 100, UnitValue: 15m),
                new CacheSettlementInput(AlphaId, "Alpha", ItemName: "Xanax", Quantity: 20, UnitValue: 15m),
            ],
            termedSettlement: new TermedSettlementInput(500m, TermedSettlementDirection.Paid, "By the terms: our war ended early"));

        Assert.True(ledger.IsTermed);

        var memberCache = Assert.Single(
            ledger.Entries.Where(e => e.Kind == WarLedgerEntryKind.CacheSettlement));
        Assert.Equal(300m, memberCache.Amount);
        Assert.Equal(AlphaId, memberCache.MemberId);

        var termed = Assert.Single(
            ledger.Entries.Where(e => e.Kind == WarLedgerEntryKind.TermedWarSettlement));
        Assert.Equal(500m, termed.Amount);
        Assert.Equal(0L, termed.MemberId);
        Assert.Equal(WarAccountingTestData.OpponentFactionName, termed.MemberName);

        var factionCacheCredit = Assert.Single(
            ledger.PoolCredits.Where(c => c.Description.StartsWith("Faction cache liquidation", StringComparison.Ordinal)));
        Assert.Equal(1500m, factionCacheCredit.Amount);

        Assert.Equal(800m, ledger.Totals.TotalSettlement);
        Assert.Equal(1500m, ledger.Totals.TotalPoolCredits);
    }

    [Fact]
    public void Termed_settlement_received_credits_the_pool_instead_of_a_distribution_line()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
        };

        var ledger = WarLedgerBuilder.Build(
            war,
            members,
            termedSettlement: new TermedSettlementInput(2000m, TermedSettlementDirection.Received, "Their buyout"));

        Assert.True(ledger.IsTermed);
        Assert.DoesNotContain(ledger.Entries, e => e.Kind == WarLedgerEntryKind.TermedWarSettlement);
        var credit = Assert.Single(ledger.PoolCredits);
        Assert.Equal(2000m, credit.Amount);
    }

    [Fact]
    public void Same_source_rows_always_build_the_same_immutable_ledger()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
            WarAccountingTestData.ReportMember(WarId, BetaId, "Beta", score: 260, attacks: 30),
        };

        var first = WarLedgerBuilder.Build(war, members, generatedAtUtc: new DateTimeOffset(2026, 1, 13, 8, 0, 0, TimeSpan.Zero));
        var second = WarLedgerBuilder.Build(war, members, generatedAtUtc: new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero));

        // The generated-at stamp differs, but the fingerprint covers facts only: raw events are
        // immutable, so the same source events hash identically no matter when the ledger was built.
        Assert.NotEqual(first.GeneratedAtUtc, second.GeneratedAtUtc);
        Assert.Equal(PayoutFingerprint.OfLedger(first), PayoutFingerprint.OfLedger(second));
        Assert.Equal(first.Entries, second.Entries);
    }

    [Fact]
    public void Every_entry_explains_itself_to_a_member()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
        };

        var ledger = WarLedgerBuilder.Build(
            war,
            members,
            adjustments:
            [
                new ManualAdjustmentInput(AlphaId, "Alpha", 50m, "LD1", "Treasurer",
                    new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero), "Approved top-up"),
            ]);

        var hit = Assert.Single(ledger.Entries.Where(e => e.Kind == WarLedgerEntryKind.WarHit));
        Assert.Contains("355 respect", hit.Explain(), StringComparison.Ordinal);
        Assert.Contains("40 hit", hit.Explain(), StringComparison.Ordinal);

        var adjustment = Assert.Single(ledger.Entries.Where(e => e.Kind == WarLedgerEntryKind.ManualAdjustment));
        Assert.Contains("Approved top-up", adjustment.Explain(), StringComparison.Ordinal);
        Assert.Contains("Treasurer", adjustment.Explain(), StringComparison.Ordinal);
    }
}
