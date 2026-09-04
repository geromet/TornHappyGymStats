namespace HappyGymStats.Core.War;

/// <summary>
/// One explainable payout line for a member — the unit of "you received X because...". Carries the
/// kind, the human description, the facts it was computed from (respect, count) and the rate applied,
/// so a member can see exactly why their total differs from another member's.
/// </summary>
public sealed record PayoutLine(
    WarLedgerEntryKind Kind,
    string Description,
    decimal Respect,
    int Count,
    decimal Rate,
    decimal Amount)
{
    public string Note => Count > 0
        ? $"{Description}: {Count:N0} x {Rate} = {Amount}"
        : $"{Description}: {Amount}";
}

/// <summary>
/// The fully-computed payout for one member under a policy. <see cref="EarnedContribution"/> is the
/// gross policy value of the member's earned entries; <see cref="ReserveCut"/> is the
/// leadership/faction cut withheld from it; <see cref="ExpenseReimbursement"/>,
/// <see cref="ManualAdjustments"/> and <see cref="CacheSettlement"/> are the separate buckets;
/// <see cref="Total"/> is what the member actually receives.
/// </summary>
public sealed record MemberPayout(
    long MemberId,
    string MemberName,
    decimal EarnedContribution,
    decimal ReserveCut,
    decimal ExpenseReimbursement,
    decimal ManualAdjustments,
    decimal CacheSettlement,
    decimal Total,
    IReadOnlyList<PayoutLine> Lines);

/// <summary>
/// The pool reconciliation for a payout run: what was available (configured pool + pool credits) versus
/// what was distributed (member totals + settlement entries) plus the reserve withheld. The run is
/// reconciled exactly when <see cref="Surplus"/> is zero.
/// </summary>
public sealed record PayoutReconciliation(
    decimal ConfiguredPool,
    decimal PoolCredits,
    decimal AvailablePool,
    decimal MemberPayoutTotal,
    decimal ReserveTotal,
    decimal SettlementTotal,
    decimal TotalDistributed,
    decimal Surplus,
    bool IsReconciled);

/// <summary>
/// A computed payout run. <see cref="PolicySnapshot"/> is the exact policy that produced it,
/// <see cref="LedgerFingerprint"/> and <see cref="RunFingerprint"/> are the deterministic hashes that
/// make it frozen and reproducible: re-running the same policy over the same ledger yields the same
/// fingerprint, and any change to policy, ledger or pool changes it.
/// </summary>
public sealed record PayoutRun(
    PayoutPolicy PolicySnapshot,
    decimal ConfiguredPool,
    IReadOnlyList<MemberPayout> Members,
    PayoutReconciliation Reconciliation,
    string LedgerFingerprint,
    string RunFingerprint);

/// <summary>
/// An approved, frozen payout run. Approval adds the human gate (who and when) without touching the
/// underlying computation — the run was already immutable. <see cref="RunId"/> is the run fingerprint,
/// so an approval is verifiable against the exact inputs that produced it.
/// </summary>
public sealed record ApprovedPayoutRun(
    string RunId,
    DateTimeOffset ApprovedAtUtc,
    string ApproverId,
    string ApproverName,
    PayoutRun Run)
{
    public static ApprovedPayoutRun Create(
        PayoutRun run,
        DateTimeOffset approvedAtUtc,
        string approverId,
        string approverName)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (string.IsNullOrWhiteSpace(approverId) || string.IsNullOrWhiteSpace(approverName))
        {
            throw new ArgumentException("An approved payout run must name its approver.");
        }

        return new ApprovedPayoutRun(run.RunFingerprint, approvedAtUtc, approverId, approverName, run);
    }
}

/// <summary>
/// The pure payout engine. Turns an immutable <see cref="WarLedger"/> and a versioned
/// <see cref="PayoutPolicy"/> into a per-member, explainable payout run and reconciles it against the
/// configured pool. Pure by construction: no I/O, no clock, no Torn calls — it only reads the ledger
/// and returns a value, so previewing alternative policies side-by-side cannot mutate anything.
/// </summary>
public static class PayoutEngine
{
    public static PayoutRun Preview(WarLedger ledger, PayoutPolicy policy, decimal configuredPool)
        => Compute(ledger, policy, configuredPool);

    public static ApprovedPayoutRun Approve(
        WarLedger ledger,
        PayoutPolicy policy,
        decimal configuredPool,
        DateTimeOffset approvedAtUtc,
        string approverId,
        string approverName)
        => ApprovedPayoutRun.Create(Compute(ledger, policy, configuredPool), approvedAtUtc, approverId, approverName);

    public static PayoutRun Compute(WarLedger ledger, PayoutPolicy policy, decimal configuredPool)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(policy);

        if (configuredPool < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(configuredPool), configuredPool, "Configured pool cannot be negative.");
        }

        if (policy.LeadershipCutRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), policy.LeadershipCutRate, "Leadership cut rate must be between 0 and 1.");
        }

        var factionMedian = FactionMedianScorePerAttack(ledger);

        var members = ledger.Entries
            .Select(e => e.MemberId)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => BuildMemberPayout(ledger, policy, id, factionMedian))
            .ToArray();

        var memberPayoutTotal = members.Sum(m => m.Total);
        var reserveTotal = members.Sum(m => m.ReserveCut);
        // Member-held cache settlement already sits inside each member's Total, so the
        // reconciliation counts only faction-to-faction settlement (termed-war settlements paid).
        // Counting member cache twice would make every member-cache run under-distributed.
        var settlementTotal = ledger.Entries
            .Where(e => e.Kind == WarLedgerEntryKind.TermedWarSettlement)
            .Sum(e => e.Amount ?? 0m);
        var poolCredits = ledger.PoolCredits.Sum(c => c.Amount);
        var availablePool = configuredPool + poolCredits;
        var totalDistributed = memberPayoutTotal + settlementTotal;
        var surplus = availablePool - (totalDistributed + reserveTotal);

        var reconciliation = new PayoutReconciliation(
            ConfiguredPool: configuredPool,
            PoolCredits: poolCredits,
            AvailablePool: availablePool,
            MemberPayoutTotal: memberPayoutTotal,
            ReserveTotal: reserveTotal,
            SettlementTotal: settlementTotal,
            TotalDistributed: totalDistributed,
            Surplus: surplus,
            IsReconciled: surplus == 0m);

        return new PayoutRun(
            PolicySnapshot: policy,
            ConfiguredPool: configuredPool,
            Members: members,
            Reconciliation: reconciliation,
            LedgerFingerprint: PayoutFingerprint.OfLedger(ledger),
            RunFingerprint: PayoutFingerprint.OfRun(policy, ledger, configuredPool));
    }

    /// <summary>
    /// The faction median score/attack over the ledger's war-hit entries — the lump detector's
    /// baseline. Zero-attack entries are excluded so an idle roster doesn't drag it toward zero,
    /// mirroring the opponent scout profile.
    /// </summary>
    public static decimal FactionMedianScorePerAttack(WarLedger ledger)
    {
        var rates = ledger.Entries
            .Where(e => e.Kind == WarLedgerEntryKind.WarHit && (e.Count ?? 0) > 0)
            .Select(e => (decimal)(e.Respect ?? 0m) / e.Count!.Value)
            .OrderBy(rate => rate)
            .ToArray();

        if (rates.Length == 0)
        {
            return 0m;
        }

        var mid = rates.Length / 2;
        return rates.Length % 2 == 1
            ? rates[mid]
            : (rates[mid - 1] + rates[mid]) / 2m;
    }

    private static MemberPayout BuildMemberPayout(
        WarLedger ledger,
        PayoutPolicy policy,
        long memberId,
        decimal factionMedian)
    {
        var entries = ledger.Entries.Where(e => e.MemberId == memberId).ToArray();
        var lines = new List<PayoutLine>();
        var earned = 0m;

        foreach (var hit in entries.Where(e => e.Kind == WarLedgerEntryKind.WarHit))
        {
            var respect = hit.Respect ?? 0m;
            var hits = hit.Count ?? 0;
            int? lump = null;

            if (policy.LumpHandling == MilestoneLumpHandling.PaidSeparately)
            {
                lump = MilestoneLumpDetector.Detect(respect, hits, factionMedian);
            }

            var creditedRespect = respect - (lump ?? 0);
            var respectRate = policy.Rates.RespectRatePerPoint;

            if (creditedRespect != 0m)
            {
                var amount = creditedRespect * respectRate;
                if (amount != 0m)
                {
                    lines.Add(new PayoutLine(
                        WarLedgerEntryKind.WarHit,
                        $"Respect credit ({creditedRespect} respect @ {respectRate}/pt)",
                        creditedRespect,
                        hits,
                        respectRate,
                        amount));
                    earned += amount;
                }
            }

            // A policy can pay war hits by count as well as (or instead of) by respect.
            var hitRate = policy.Rates.WarHitRate;
            if (hitRate > 0 && hits > 0)
            {
                var hitAmount = hits * hitRate;
                lines.Add(new PayoutLine(
                    WarLedgerEntryKind.WarHit,
                    $"Hit credit ({hits} hits @ {hitRate})",
                    0m,
                    hits,
                    hitRate,
                    hitAmount));
                earned += hitAmount;
            }

            if (lump is int bonus && policy.Rates.MilestoneBonusRate > 0)
            {
                var bonusRate = policy.Rates.MilestoneBonusRate;
                var amount = bonus * bonusRate;
                lines.Add(new PayoutLine(
                    WarLedgerEntryKind.MilestoneBonus,
                    $"Chain-milestone lump {bonus} @ {bonusRate}/pt",
                    bonus,
                    1,
                    bonusRate,
                    amount));
                earned += amount;
            }
        }

        foreach (var group in entries
                     .Where(e => e.Category == WarLedgerCategory.EarnedContribution
                                 && e.Kind != WarLedgerEntryKind.WarHit)
                     .GroupBy(e => e.Kind)
                     .OrderBy(g => g.Key))
        {
            var kind = group.Key;
            var rate = RateFor(kind, policy.Rates);
            if (rate == 0m)
            {
                continue;
            }

            var count = group.Sum(e => e.Count ?? 0);
            var respect = group.Sum(e => e.Respect ?? 0m);
            var amount = count * rate;

            if (amount != 0m)
            {
                lines.Add(new PayoutLine(kind, $"{kind} credit", respect, count, rate, amount));
                earned += amount;
            }
        }

        var expenseReimbursement = 0m;
        if (policy.ReimburseExpenses)
        {
            foreach (var group in entries
                         .Where(e => e.Category == WarLedgerCategory.Expense)
                         .GroupBy(e => e.Kind)
                         .OrderBy(g => g.Key))
            {
                var amount = group.Sum(e => e.Amount ?? 0m);
                if (amount == 0m)
                {
                    continue;
                }

                lines.Add(new PayoutLine(group.Key, $"Reimbursement ({group.Key})", 0m, group.Count(), 0m, amount));
                expenseReimbursement += amount;
            }
        }

        var manualAdjustments = entries
            .Where(e => e.Category == WarLedgerCategory.ManualAdjustment)
            .Sum(e => e.Amount ?? 0m);
        if (manualAdjustments != 0m)
        {
            lines.Add(new PayoutLine(
                WarLedgerEntryKind.ManualAdjustment,
                $"Manual adjustment ({manualAdjustments})",
                0m,
                0,
                0m,
                manualAdjustments));
        }

        var cacheSettlement = entries
            .Where(e => e.Kind == WarLedgerEntryKind.CacheSettlement)
            .Sum(e => e.Amount ?? 0m);
        if (cacheSettlement != 0m)
        {
            lines.Add(new PayoutLine(
                WarLedgerEntryKind.CacheSettlement,
                $"Cache settlement ({cacheSettlement})",
                0m,
                0,
                0m,
                cacheSettlement));
        }

        var reserveCut = Math.Round(earned * policy.LeadershipCutRate, 2);
        var total = earned - reserveCut + expenseReimbursement + manualAdjustments + cacheSettlement;

        return new MemberPayout(
            memberId,
            entries[0].MemberName,
            EarnedContribution: earned,
            ReserveCut: reserveCut,
            ExpenseReimbursement: expenseReimbursement,
            ManualAdjustments: manualAdjustments,
            CacheSettlement: cacheSettlement,
            Total: total,
            Lines: lines);
    }

    private static decimal RateFor(WarLedgerEntryKind kind, PayoutRateTable rates) => kind switch
    {
        WarLedgerEntryKind.WarHit => 0m,
        WarLedgerEntryKind.Assist => rates.AssistRate,
        WarLedgerEntryKind.OutsideHit => rates.OutsideHitRate,
        WarLedgerEntryKind.ChainSave => rates.ChainSaveRate,
        WarLedgerEntryKind.MilestoneBonus => rates.MilestoneBonusRate,
        WarLedgerEntryKind.PushWindowParticipation => rates.PushWindowRate,
        WarLedgerEntryKind.RetaliationHit => rates.RetaliationRate,
        WarLedgerEntryKind.EnergyExpenditure => rates.EnergyRatePerPoint,
        _ => 0m,
    };
}
