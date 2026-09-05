namespace HappyGymStats.Core.War;

/// <summary>
/// Versioned, bounded aggregate payout policy. This policy deliberately uses only
/// facts that exist in the frozen ranked-war source snapshot; it does not infer
/// per-hit FF/retal/assist attribution.
/// </summary>
public sealed record WarPayoutPolicy(
    int Version,
    decimal ScoreRate,
    decimal ChainRate,
    decimal AttackRate,
    decimal FixedMemberAmount)
{
    public const decimal MaximumRate = 1_000_000_000m;
    public const decimal MaximumAmount = 1_000_000_000_000_000m;

    public WarPayoutPolicy Validate()
    {
        if (Version <= 0)
            throw new ArgumentOutOfRangeException(nameof(Version), Version, "Policy version must be positive.");

        ValidateRate(ScoreRate, nameof(ScoreRate));
        ValidateRate(ChainRate, nameof(ChainRate));
        ValidateRate(AttackRate, nameof(AttackRate));
        ValidateAmount(FixedMemberAmount, nameof(FixedMemberAmount));
        return this;
    }

    internal static void ValidateAmount(decimal value, string name)
    {
        if (value < 0m || value > MaximumAmount)
            throw new ArgumentOutOfRangeException(name, value, $"Amount must be between 0 and {MaximumAmount}.");
    }

    private static void ValidateRate(decimal value, string name)
    {
        if (value < 0m || value > MaximumRate)
            throw new ArgumentOutOfRangeException(name, value, $"Rate must be between 0 and {MaximumRate}.");
    }
}

public sealed record WarPayoutLine(
    long MemberId,
    string MemberName,
    int Score,
    int Chain,
    int Attacks,
    decimal ScoreAmount,
    decimal ChainAmount,
    decimal AttackAmount,
    decimal FixedAmount,
    decimal TotalAmount);

/// <summary>
/// Deterministic aggregate reconciliation. Any pool value not justified by the
/// frozen aggregate facts and bounded policy remains explicit as UnattributedResidual.
/// </summary>
public sealed record WarPayoutReconciliation(
    Guid SourceSnapshotId,
    int PolicyVersion,
    decimal PoolAmount,
    decimal AllocatedAmount,
    decimal UnattributedResidual,
    IReadOnlyList<WarPayoutLine> Lines);

public static class WarPayoutCalculator
{
    public static WarPayoutReconciliation Calculate(
        FrozenWarAccountingSource source,
        WarPayoutPolicy policy,
        decimal poolAmount)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        WarPayoutPolicy.ValidateAmount(poolAmount, nameof(poolAmount));

        // Re-run canonical validation at the calculation boundary so a caller cannot
        // smuggle an unrelated faction/war member or duplicate beneficiary into lines.
        var fingerprint = WarAccountingSourceFingerprint.Compute(source.FactionId, source.WarId, source.Members);
        if (!string.Equals(fingerprint, source.Fingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Frozen accounting source fingerprint does not match its member facts.");

        var lines = source.Members
            .OrderBy(member => member.MemberId)
            .Select(member => BuildLine(member, policy))
            .ToArray();

        decimal allocated;
        try
        {
            allocated = lines.Aggregate(0m, static (total, line) => checked(total + line.TotalAmount));
        }
        catch (OverflowException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), "Payout arithmetic overflowed the supported monetary range.", ex);
        }

        if (allocated > WarPayoutPolicy.MaximumAmount)
            throw new ArgumentOutOfRangeException(nameof(policy), allocated, "Allocated payout exceeds the supported monetary range.");
        if (allocated > poolAmount)
            throw new InvalidOperationException("Calculated member payouts exceed the declared payout pool.");

        return new WarPayoutReconciliation(
            source.SourceSnapshotId,
            policy.Version,
            poolAmount,
            allocated,
            poolAmount - allocated,
            Array.AsReadOnly(lines));
    }

    private static WarPayoutLine BuildLine(WarAccountingSourceMemberFact member, WarPayoutPolicy policy)
    {
        try
        {
            var scoreAmount = Money(checked(member.Score * policy.ScoreRate));
            var chainAmount = Money(checked(member.Chain * policy.ChainRate));
            var attackAmount = Money(checked(member.Attacks * policy.AttackRate));
            var fixedAmount = Money(policy.FixedMemberAmount);
            var total = checked(scoreAmount + chainAmount + attackAmount + fixedAmount);

            if (total > WarPayoutPolicy.MaximumAmount)
                throw new ArgumentOutOfRangeException(nameof(policy), total, "Member payout exceeds the supported monetary range.");

            return new WarPayoutLine(
                member.MemberId,
                member.MemberName,
                member.Score,
                member.Chain,
                member.Attacks,
                scoreAmount,
                chainAmount,
                attackAmount,
                fixedAmount,
                total);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), "Payout arithmetic overflowed the supported monetary range.", ex);
        }
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
