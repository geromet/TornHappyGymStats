using HappyGymStats.Core.War;

namespace HappyGymStats.Tests;

public sealed class WarAccountingSourceFingerprintTests
{
    [Fact]
    public void Fingerprint_is_order_independent_but_changes_with_audit_significant_fact()
    {
        const long factionId = 1234;
        const long warId = 5678;
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:00:00Z");
        var first = new WarAccountingSourceMemberFact(
            factionId,
            warId,
            1001,
            "Alpha",
            125,
            6,
            11,
            capturedAt);
        var second = new WarAccountingSourceMemberFact(
            factionId,
            warId,
            1002,
            "Bravo",
            225,
            9,
            14,
            capturedAt.AddSeconds(1));

        var forward = WarAccountingSourceFingerprint.Compute(factionId, warId, new[] { first, second });
        var reverse = WarAccountingSourceFingerprint.Compute(factionId, warId, new[] { second, first });
        var changed = WarAccountingSourceFingerprint.Compute(
            factionId,
            warId,
            new[] { first, second with { Score = second.Score + 1 } });

        Assert.Equal(forward, reverse);
        Assert.NotEqual(forward, changed);
        Assert.Matches("^[0-9a-f]{64}$", forward);
    }

    [Fact]
    public void Fingerprint_rejects_wrong_scope_and_duplicate_member_identity()
    {
        const long factionId = 2234;
        const long warId = 6678;
        var capturedAt = DateTimeOffset.Parse("2026-09-05T09:05:00Z");
        var valid = new WarAccountingSourceMemberFact(
            factionId,
            warId,
            2001,
            "Charlie",
            50,
            2,
            3,
            capturedAt);

        Assert.Throws<ArgumentException>(() => WarAccountingSourceFingerprint.Compute(
            factionId,
            warId,
            new[] { valid with { FactionId = factionId + 1 } }));

        Assert.Throws<ArgumentException>(() => WarAccountingSourceFingerprint.Compute(
            factionId,
            warId,
            new[] { valid, valid with { Score = 999 } }));
    }
}
