using System.Security.Cryptography;
using System.Text;

namespace HappyGymStats.Core.War;

/// <summary>
/// Deterministic fingerprints for ledger freeze and payout-run reproducibility. The ledger hash covers
/// every entry's facts and every pool credit but deliberately excludes the generated-at timestamp, so
/// two ledgers built from the same source events hash identically. The run hash covers the policy
/// snapshot (name, version, rates, lump handling, cut), the ledger hash and the configured pool, so a
/// frozen run can be reproduced and any drift in inputs detected by comparing fingerprints.
/// </summary>
public static class PayoutFingerprint
{
    public static string OfLedger(WarLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var builder = new StringBuilder();
        builder.Append(ledger.WarId).Append('|')
            .Append(ledger.FactionId).Append('|')
            .Append(ledger.OpponentFactionId).Append('|')
            .Append(ledger.IsTermed);

        foreach (var entry in ledger.Entries.OrderBy(e => e.MemberId).ThenBy(e => e.Kind).ThenBy(e => e.Description))
        {
            builder.Append('|')
                .Append(entry.MemberId).Append(';')
                .Append(entry.MemberName).Append(';')
                .Append((int)entry.Kind).Append(';')
                .Append(entry.Amount ?? 0m).Append(';')
                .Append(entry.Respect ?? 0m).Append(';')
                .Append(entry.Count ?? 0).Append(';')
                .Append(entry.ActorId).Append(';')
                .Append(entry.Reason);
        }

        foreach (var credit in ledger.PoolCredits.OrderBy(c => c.Description))
        {
            builder.Append('|').Append(credit.Description).Append(';').Append(credit.Amount);
        }

        return Sha256Hex(builder.ToString());
    }

    public static string OfRun(PayoutPolicy policy, WarLedger ledger, decimal configuredPool)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(ledger);

        var builder = new StringBuilder();
        builder.Append(policy.Name).Append('|')
            .Append(policy.Version).Append('|')
            .Append((int)policy.LumpHandling).Append('|')
            .Append(policy.LeadershipCutRate).Append('|')
            .Append(policy.ReimburseExpenses).Append('|')
            .Append(configuredPool).Append('|');

        var rates = policy.Rates;
        builder.Append(rates.RespectRatePerPoint).Append(';')
            .Append(rates.WarHitRate).Append(';')
            .Append(rates.AssistRate).Append(';')
            .Append(rates.OutsideHitRate).Append(';')
            .Append(rates.ChainSaveRate).Append(';')
            .Append(rates.MilestoneBonusRate).Append(';')
            .Append(rates.PushWindowRate).Append(';')
            .Append(rates.RetaliationRate).Append(';')
            .Append(rates.EnergyRatePerPoint);

        builder.Append('|').Append(OfLedger(ledger));

        return Sha256Hex(builder.ToString());
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
