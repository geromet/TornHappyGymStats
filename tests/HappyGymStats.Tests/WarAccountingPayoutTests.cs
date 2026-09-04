using HappyGymStats.Core.War;
using HappyGymStats.Data.Entities;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// The payout engine: side-by-side policy preview (acceptance 2), milestone no-double-count
/// (acceptance 3), expense/adjustment separation (acceptance 4), freeze/versioning/reproducibility
/// (acceptance 5) and pool reconciliation with cache and termed settlement (acceptance 6).
/// </summary>
public sealed class WarAccountingPayoutTests
{
    private const long WarId = 48377;
    private const long AlphaId = 1001;
    private const long BetaId = 1002;

    private static RankedWarHistoryEntity War => WarAccountingTestData.CompletedWar(WarId);

    private static RankedWarReportMemberEntity Member(long memberId, string name, int score, int attacks)
        => WarAccountingTestData.ReportMember(WarId, memberId, name, score, attacks);

    [Fact]
    public void Two_materially_different_policies_preview_side_by_side_without_mutating_the_ledger()
    {
        // Alpha scores densely (355 respect over 40 hits = 8.875/hit); Beta spreads thinly
        // (200 respect over 50 hits = 4/hit). A respect-based policy favours Alpha; a per-hit
        // policy favours Beta — materially different answers for the same ledger.
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 355, attacks: 40),
             Member(BetaId, "Beta", score: 200, attacks: 50)]);
        var ledgerBefore = PayoutFingerprint.OfLedger(ledger);

        var respectRun = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(respectRate: 1.0m), configuredPool: 0m);
        var hitRun = PayoutEngine.Preview(ledger, WarAccountingTestData.HitPolicy(warHitRate: 5m), configuredPool: 0m);

        // The two previews disagree about who is owed more.
        var respectAlpha = Assert.Single(respectRun.Members, m => m.MemberId == AlphaId);
        var respectBeta = Assert.Single(respectRun.Members, m => m.MemberId == BetaId);
        var hitAlpha = Assert.Single(hitRun.Members, m => m.MemberId == AlphaId);
        var hitBeta = Assert.Single(hitRun.Members, m => m.MemberId == BetaId);

        Assert.Equal(355m, respectAlpha.Total);
        Assert.Equal(200m, respectBeta.Total);
        Assert.Equal(200m, hitAlpha.Total);
        Assert.Equal(250m, hitBeta.Total);
        Assert.True(respectAlpha.Total > respectBeta.Total);
        Assert.True(hitBeta.Total > hitAlpha.Total);

        // Previewing is pure: the ledger is untouched by either run.
        Assert.Equal(ledgerBefore, PayoutFingerprint.OfLedger(ledger));
        Assert.NotEqual(respectRun.RunFingerprint, hitRun.RunFingerprint);
    }

    [Fact]
    public void Paid_separately_pays_a_detected_milestone_lump_exactly_once()
    {
        // The DerDoruk war-48377 shape: three fillers at the faction median 7.875 score/attack plus
        // one member whose 955-over-40 score carries the chain-1000 milestone lump of 640.
        var ledger = WarLedgerBuilder.Build(
            War,
            [
                Member(8001, "Filler a", score: 63, attacks: 8),
                Member(8002, "Filler b", score: 63, attacks: 8),
                Member(8003, "Filler c", score: 63, attacks: 8),
                Member(9999, "DerDoruk", score: 955, attacks: 40),
            ]);
        Assert.Equal(7.875m, PayoutEngine.FactionMedianScorePerAttack(ledger));

        var policy = new PayoutPolicy(
            "Respect with milestones separate",
            "1.0",
            new PayoutRateTable(
                RespectRatePerPoint: 0.5m,
                WarHitRate: 0m,
                AssistRate: 0m,
                OutsideHitRate: 0m,
                ChainSaveRate: 0m,
                MilestoneBonusRate: 0.5m,
                PushWindowRate: 0m,
                RetaliationRate: 0m,
                EnergyRatePerPoint: 0m),
            MilestoneLumpHandling.PaidSeparately,
            LeadershipCutRate: 0m);

        var run = PayoutEngine.Preview(ledger, policy, configuredPool: 0m);
        var derDoruk = Assert.Single(run.Members, m => m.MemberId == 9999);

        // The 640 lump was pulled out of the respect credit (955 -> 315) and paid on its own line.
        var respectLine = Assert.Single(derDoruk.Lines, l => l.Kind == WarLedgerEntryKind.WarHit);
        var milestoneLine = Assert.Single(derDoruk.Lines, l => l.Kind == WarLedgerEntryKind.MilestoneBonus);
        Assert.Equal(315m, respectLine.Respect);
        Assert.Equal(157.5m, respectLine.Amount);
        Assert.Equal(640m, milestoneLine.Respect);
        Assert.Equal(320m, milestoneLine.Amount);

        // No double-count: 955 respect at 0.5 is 477.5, and that is exactly what was paid.
        Assert.Equal(477.5m, derDoruk.EarnedContribution);
        Assert.Equal(477.5m, derDoruk.Total);

        // A lump-less filler sees no milestone line and is paid straight from its respect.
        var filler = Assert.Single(run.Members, m => m.MemberId == 8001);
        Assert.Equal(31.5m, filler.EarnedContribution);
        Assert.DoesNotContain(filler.Lines, l => l.Kind == WarLedgerEntryKind.MilestoneBonus);
    }

    [Fact]
    public void Included_in_respect_pays_the_lump_once_inside_the_respect_credit()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [
                Member(8001, "Filler a", score: 63, attacks: 8),
                Member(8002, "Filler b", score: 63, attacks: 8),
                Member(8003, "Filler c", score: 63, attacks: 8),
                Member(9999, "DerDoruk", score: 955, attacks: 40),
            ]);

        var run = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(respectRate: 0.5m), configuredPool: 0m);
        var derDoruk = Assert.Single(run.Members, m => m.MemberId == 9999);

        // Full score credited at the respect rate, milestone line not paid separately: still 477.5.
        Assert.Equal(477.5m, derDoruk.EarnedContribution);
        Assert.DoesNotContain(derDoruk.Lines, l => l.Kind == WarLedgerEntryKind.MilestoneBonus);
    }

    [Fact]
    public void Separate_milestone_rate_pays_the_lump_at_its_own_rate_and_still_only_once()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [
                Member(8001, "Filler a", score: 63, attacks: 8),
                Member(8002, "Filler b", score: 63, attacks: 8),
                Member(8003, "Filler c", score: 63, attacks: 8),
                Member(9999, "DerDoruk", score: 955, attacks: 40),
            ]);

        var policy = new PayoutPolicy(
            "Respect with priced milestones",
            "1.0",
            new PayoutRateTable(
                RespectRatePerPoint: 0.5m,
                WarHitRate: 0m,
                AssistRate: 0m,
                OutsideHitRate: 0m,
                ChainSaveRate: 0m,
                MilestoneBonusRate: 0.8m,
                PushWindowRate: 0m,
                RetaliationRate: 0m,
                EnergyRatePerPoint: 0m),
            MilestoneLumpHandling.PaidSeparately,
            LeadershipCutRate: 0m);

        var run = PayoutEngine.Preview(ledger, policy, configuredPool: 0m);
        var derDoruk = Assert.Single(run.Members, m => m.MemberId == 9999);

        // 315 respect at 0.5 + 640 bonus at 0.8 = 157.5 + 512 = 669.5. The lump is not paid twice.
        Assert.Equal(669.5m, derDoruk.EarnedContribution);
    }

    [Fact]
    public void Expenses_and_manual_adjustments_stay_separate_from_earned_contribution()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 500, attacks: 50)],
            supplements: [new WarLedgerSupplement(AlphaId, "Alpha", WarLedgerEntryKind.FactionXanax, Amount: 200m)],
            adjustments:
            [
                new ManualAdjustmentInput(AlphaId, "Alpha", 50m, "LD1", "Treasurer",
                    new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero), "Approved top-up"),
            ]);

        var policy = WarAccountingTestData.RespectPolicy(respectRate: 1.0m) with { LeadershipCutRate = 0.1m };

        var run = PayoutEngine.Preview(ledger, policy, configuredPool: 0m);
        var alpha = Assert.Single(run.Members);

        Assert.Equal(500m, alpha.EarnedContribution);
        Assert.Equal(50m, alpha.ReserveCut);
        Assert.Equal(200m, alpha.ExpenseReimbursement);
        Assert.Equal(50m, alpha.ManualAdjustments);
        Assert.Equal(700m, alpha.Total); // 500 - 50 reserve + 200 expense + 50 adjustment

        Assert.Contains(alpha.Lines, l => l.Kind == WarLedgerEntryKind.FactionXanax);
        Assert.Contains(alpha.Lines, l => l.Kind == WarLedgerEntryKind.ManualAdjustment);
    }

    [Fact]
    public void Expenses_are_not_reimbursed_when_the_policy_says_no()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 500, attacks: 50)],
            supplements: [new WarLedgerSupplement(AlphaId, "Alpha", WarLedgerEntryKind.FactionXanax, Amount: 200m)]);

        var policy = WarAccountingTestData.RespectPolicy(respectRate: 1.0m) with { ReimburseExpenses = false };

        var run = PayoutEngine.Preview(ledger, policy, configuredPool: 0m);
        var alpha = Assert.Single(run.Members);

        Assert.Equal(500m, alpha.EarnedContribution);
        Assert.Equal(0m, alpha.ExpenseReimbursement);
        Assert.Equal(500m, alpha.Total);
        Assert.DoesNotContain(alpha.Lines, l => l.Kind == WarLedgerEntryKind.FactionXanax);
    }

    [Fact]
    public void Approved_runs_are_versioned_frozen_and_reproducible()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 355, attacks: 40)]);
        var policy = WarAccountingTestData.RespectPolicy(respectRate: 1.0m, version: "3.1");
        var approvedAt = new DateTimeOffset(2026, 1, 13, 12, 0, 0, TimeSpan.Zero);

        var approved = PayoutEngine.Approve(ledger, policy, configuredPool: 500m, approvedAt, "LD1", "Treasurer");

        // The run id IS the run fingerprint: an approval is verifiable against its exact inputs.
        Assert.Equal(approved.Run.RunFingerprint, approved.RunId);
        Assert.Equal("LD1", approved.ApproverId);
        Assert.Equal(approvedAt, approved.ApprovedAtUtc);

        // The approved run froze the policy snapshot (name, version and rates).
        Assert.Equal("Respect", approved.Run.PolicySnapshot.Name);
        Assert.Equal("3.1", approved.Run.PolicySnapshot.Version);

        // Re-running the same policy over the same ledger reproduces the identical run.
        var reproduced = PayoutEngine.Preview(ledger, policy, configuredPool: 500m);
        Assert.Equal(approved.Run.RunFingerprint, reproduced.RunFingerprint);
        Assert.Equal(approved.Run.LedgerFingerprint, reproduced.LedgerFingerprint);
        Assert.Equal(approved.Run.Members.Select(m => m.Total), reproduced.Members.Select(m => m.Total));
        Assert.Equal(
            approved.Run.Members.SelectMany(m => m.Lines.Select(l => l.Amount)),
            reproduced.Members.SelectMany(m => m.Lines.Select(l => l.Amount)));

        // A changed policy version is a different run; the frozen approval still points at its own.
        var reversioned = PayoutEngine.Preview(ledger, policy.WithVersion("3.2"), configuredPool: 500m);
        Assert.NotEqual(approved.Run.RunFingerprint, reversioned.RunFingerprint);

        // A changed ledger is a different ledger; the old approval is detectable as stale.
        var alteredLedger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 355, attacks: 40),
             Member(BetaId, "Beta", score: 260, attacks: 30)]);
        var alteredRun = PayoutEngine.Preview(alteredLedger, policy, configuredPool: 500m);
        Assert.NotEqual(approved.Run.LedgerFingerprint, alteredRun.LedgerFingerprint);
        Assert.NotEqual(approved.Run.RunFingerprint, alteredRun.RunFingerprint);

        // A changed pool changes the run but not the ledger it was computed from.
        var repooled = PayoutEngine.Preview(ledger, policy, configuredPool: 600m);
        Assert.NotEqual(approved.Run.RunFingerprint, repooled.RunFingerprint);
        Assert.Equal(approved.Run.LedgerFingerprint, repooled.LedgerFingerprint);
    }

    [Fact]
    public void Approval_requires_an_approver()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 355, attacks: 40)]);
        var run = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(), configuredPool: 0m);

        Assert.Throws<ArgumentException>(
            () => ApprovedPayoutRun.Create(run, DateTimeOffset.UtcNow, approverId: "", approverName: "Treasurer"));
        Assert.Throws<ArgumentException>(
            () => ApprovedPayoutRun.Create(run, DateTimeOffset.UtcNow, approverId: "LD1", approverName: ""));
    }

    [Fact]
    public void Pool_reconciles_with_cache_and_termed_settlement()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [
                Member(AlphaId, "Alpha", score: 1000, attacks: 100),
                Member(BetaId, "Beta", score: 2000, attacks: 200),
            ],
            cacheItems:
            [
                new CacheSettlementInput(MemberId: null, MemberName: null, ItemName: "Xanax", Quantity: 100, UnitValue: 15m),
                new CacheSettlementInput(BetaId, "Beta", ItemName: "Xanax", Quantity: 20, UnitValue: 15m),
            ],
            termedSettlement: new TermedSettlementInput(500m, TermedSettlementDirection.Paid, "By the terms"));

        var policy = WarAccountingTestData.RespectPolicy(respectRate: 1.0m) with { LeadershipCutRate = 0.1m };

        var alpha = Assert.Single(ledger.Entries.Where(e => e.MemberId == AlphaId && e.Kind == WarLedgerEntryKind.WarHit));
        var beta = Assert.Single(ledger.Entries.Where(e => e.MemberId == BetaId && e.Kind == WarLedgerEntryKind.WarHit));
        Assert.Equal(1000m, alpha.Respect);
        Assert.Equal(2000m, beta.Respect);

        // Available pool must cover member totals (incl. member cache buy-out) + reserve +
        // termed settlement paid, minus the faction-cache credit:
        //   members 3000 (incl. 300 Beta cache) + reserve 300 + settlement 500 - credit 1500 = 2300.
        var reconciled = PayoutEngine.Preview(ledger, policy, configuredPool: 2300m);
        Assert.True(reconciled.Reconciliation.IsReconciled);
        Assert.Equal(0m, reconciled.Reconciliation.Surplus);
        Assert.Equal(3800m, reconciled.Reconciliation.AvailablePool);
        Assert.Equal(300m, reconciled.Reconciliation.ReserveTotal);

        var betaPayout = Assert.Single(reconciled.Members, m => m.MemberId == BetaId);
        Assert.Equal(300m, betaPayout.CacheSettlement);
        Assert.Equal(2100m, betaPayout.Total); // 2000 - 200 reserve + 300 cache

        // A smaller pool is detectably under-distributed, never silently absorbed.
        var underfunded = PayoutEngine.Preview(ledger, policy, configuredPool: 2000m);
        Assert.False(underfunded.Reconciliation.IsReconciled);
        Assert.Equal(-300m, underfunded.Reconciliation.Surplus);
    }

    [Fact]
    public void Termed_settlement_received_expands_the_available_pool()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 1500, attacks: 150)],
            termedSettlement: new TermedSettlementInput(2000m, TermedSettlementDirection.Received, "Their buyout"));

        var policy = WarAccountingTestData.RespectPolicy(respectRate: 1.0m);
        var reconciled = PayoutEngine.Preview(ledger, policy, configuredPool: 0m);

        // 2000 received + 0 pool = 2000 available, member owed 1500, 500 stays in the faction.
        Assert.Equal(2000m, reconciled.Reconciliation.AvailablePool);
        Assert.Equal(1500m, reconciled.Reconciliation.MemberPayoutTotal);
        Assert.Equal(500m, reconciled.Reconciliation.Surplus);
    }

    [Fact]
    public void Engine_rejects_invalid_pool_and_cut_rate()
    {
        var ledger = WarLedgerBuilder.Build(
            War,
            [Member(AlphaId, "Alpha", score: 355, attacks: 40)]);
        var policy = WarAccountingTestData.RespectPolicy() with { LeadershipCutRate = 1.5m };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PayoutEngine.Preview(ledger, policy, configuredPool: -1m));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PayoutEngine.Preview(ledger, policy, configuredPool: 100m));
    }
}
