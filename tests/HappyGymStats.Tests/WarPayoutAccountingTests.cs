using System.Globalization;
using HappyGymStats.Core.War;

namespace HappyGymStats.Tests;

public sealed class WarPayoutAccountingTests
{
    [Fact]
    public void Calculate_is_deterministic_source_scoped_and_preserves_unattributed_residual()
    {
        var source = CreateSource();
        var policy = new WarPayoutPolicy(3, ScoreRate: 10m, ChainRate: 5m, AttackRate: 2m, FixedMemberAmount: 25m);

        var result = WarPayoutCalculator.Calculate(source, policy, poolAmount: 10_000m);

        Assert.Equal(3, result.PolicyVersion);
        Assert.Equal(source.SourceSnapshotId, result.SourceSnapshotId);
        Assert.Collection(
            result.Lines,
            first =>
            {
                Assert.Equal(1001, first.MemberId);
                Assert.Equal(1_093m, first.TotalAmount);
            },
            second =>
            {
                Assert.Equal(1002, second.MemberId);
                Assert.Equal(2_137m, second.TotalAmount);
            });
        Assert.Equal(3_230m, result.AllocatedAmount);
        Assert.Equal(6_770m, result.UnattributedResidual);
        Assert.Equal(result.PoolAmount, result.AllocatedAmount + result.UnattributedResidual);
    }

    [Fact]
    public void Calculate_rejects_policy_or_pool_values_outside_bounded_domain()
    {
        var source = CreateSource();

        Assert.Throws<ArgumentOutOfRangeException>(() => WarPayoutCalculator.Calculate(
            source,
            new WarPayoutPolicy(0, 1m, 0m, 0m, 0m),
            10_000m));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarPayoutCalculator.Calculate(
            source,
            new WarPayoutPolicy(1, -1m, 0m, 0m, 0m),
            10_000m));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarPayoutCalculator.Calculate(
            source,
            new WarPayoutPolicy(1, WarPayoutPolicy.MaximumRate + 1m, 0m, 0m, 0m),
            10_000m));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarPayoutCalculator.Calculate(
            source,
            new WarPayoutPolicy(1, 1m, 0m, 0m, 0m),
            WarPayoutPolicy.MaximumAmount + 1m));
        Assert.Throws<InvalidOperationException>(() => WarPayoutCalculator.Calculate(
            source,
            new WarPayoutPolicy(1, 10m, 0m, 0m, 0m),
            1m));
    }

    [Fact]
    public void Calculate_rejects_tampered_or_cross_scope_source_and_cannot_inject_beneficiary()
    {
        var source = CreateSource();
        var policy = new WarPayoutPolicy(1, 1m, 0m, 0m, 0m);

        var wrongScope = source with
        {
            Members = Array.AsReadOnly(source.Members
                .Select(member => member.MemberId == 1001 ? member with { FactionId = member.FactionId + 1 } : member)
                .ToArray())
        };
        Assert.Throws<ArgumentException>(() => WarPayoutCalculator.Calculate(wrongScope, policy, 10_000m));

        var injected = source with
        {
            Members = Array.AsReadOnly(source.Members
                .Append(source.Members[0] with { MemberId = 9999, MemberName = "Injected" })
                .ToArray())
        };
        Assert.Throws<InvalidDataException>(() => WarPayoutCalculator.Calculate(injected, policy, 10_000m));
    }

    [Fact]
    public void Calculate_output_is_read_only_and_culture_independent()
    {
        var source = CreateSource();
        var policy = new WarPayoutPolicy(2, 1.25m, 0.5m, 0.1m, 3.33m);
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");
            var nl = WarPayoutCalculator.Calculate(source, policy, 10_000m);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var en = WarPayoutCalculator.Calculate(source, policy, 10_000m);

            Assert.Equal(nl.SourceSnapshotId, en.SourceSnapshotId);
            Assert.Equal(nl.PolicyVersion, en.PolicyVersion);
            Assert.Equal(nl.PoolAmount, en.PoolAmount);
            Assert.Equal(nl.AllocatedAmount, en.AllocatedAmount);
            Assert.Equal(nl.UnattributedResidual, en.UnattributedResidual);
            Assert.True(nl.Lines.SequenceEqual(en.Lines));
            Assert.False(nl.Lines is WarPayoutLine[]);
            Assert.Throws<NotSupportedException>(() => ((IList<WarPayoutLine>)nl.Lines).Add(nl.Lines[0]));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static FrozenWarAccountingSource CreateSource()
    {
        const long factionId = 55;
        const long warId = 77;
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:00:00Z", CultureInfo.InvariantCulture);
        var members = new[]
        {
            new WarAccountingSourceMemberFact(factionId, warId, 1002, "Bravo", 205, 8, 11, capturedAt),
            new WarAccountingSourceMemberFact(factionId, warId, 1001, "Alpha", 100, 10, 9, capturedAt)
        };

        return WarAccountingSourceFingerprint.Create(
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            factionId,
            warId,
            members,
            "tester",
            capturedAt);
    }
}
