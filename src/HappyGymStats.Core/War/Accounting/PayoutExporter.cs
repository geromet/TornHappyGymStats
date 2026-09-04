using System.Globalization;
using System.Text;

namespace HappyGymStats.Core.War;

/// <summary>
/// Exports a payout run to CSV (the delimiter is configurable, so the same exporter also produces a
/// tab-separated file Excel opens natively). The export is deterministic — same run, same bytes — and
/// carries enough detail to audit the calculation externally: every per-member payout line with its
/// facts and rate, every member total with its buckets, and the pool reconciliation.
/// </summary>
public static class PayoutExporter
{
    public static string ExportCsv(PayoutRun run, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(run);

        var builder = new StringBuilder();
        var d = delimiter.ToString();

        builder.Append("# HappyGymStats payout run export\n");
        builder.Append("# policy,").Append(Escape(run.PolicySnapshot.Name, delimiter)).Append('\n');
        builder.Append("# policy_version,").Append(Escape(run.PolicySnapshot.Version, delimiter)).Append('\n');
        builder.Append("# ledger_fingerprint,").Append(run.LedgerFingerprint).Append('\n');
        builder.Append("# run_fingerprint,").Append(run.RunFingerprint).Append('\n');

        builder.Append("record_type")
            .Append(d).Append("member_id")
            .Append(d).Append("member_name")
            .Append(d).Append("kind")
            .Append(d).Append("category")
            .Append(d).Append("description")
            .Append(d).Append("respect")
            .Append(d).Append("count")
            .Append(d).Append("rate")
            .Append(d).Append("amount")
            .Append('\n');

        foreach (var member in run.Members.OrderBy(m => m.MemberId))
        {
            foreach (var line in member.Lines.OrderBy(l => l.Kind).ThenBy(l => l.Description))
            {
                builder.Append("line")
                    .Append(d).Append(member.MemberId)
                    .Append(d).Append(Escape(member.MemberName, delimiter))
                    .Append(d).Append((int)line.Kind)
                    .Append(d).Append(WarLedgerKindCatalog.CategoryOf(line.Kind))
                    .Append(d).Append(Escape(line.Description, delimiter))
                    .Append(d).Append(line.Respect.ToString(CultureInfo.InvariantCulture))
                    .Append(d).Append(line.Count)
                    .Append(d).Append(line.Rate.ToString(CultureInfo.InvariantCulture))
                    .Append(d).Append(line.Amount.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        builder.Append("record_type")
            .Append(d).Append("member_id")
            .Append(d).Append("member_name")
            .Append(d).Append("earned")
            .Append(d).Append("reserve")
            .Append(d).Append("expenses")
            .Append(d).Append("adjustments")
            .Append(d).Append("cache")
            .Append(d).Append("total")
            .Append('\n');

        foreach (var member in run.Members.OrderBy(m => m.MemberId))
        {
            builder.Append("member")
                .Append(d).Append(member.MemberId)
                .Append(d).Append(Escape(member.MemberName, delimiter))
                .Append(d).Append(member.EarnedContribution.ToString(CultureInfo.InvariantCulture))
                .Append(d).Append(member.ReserveCut.ToString(CultureInfo.InvariantCulture))
                .Append(d).Append(member.ExpenseReimbursement.ToString(CultureInfo.InvariantCulture))
                .Append(d).Append(member.ManualAdjustments.ToString(CultureInfo.InvariantCulture))
                .Append(d).Append(member.CacheSettlement.ToString(CultureInfo.InvariantCulture))
                .Append(d).Append(member.Total.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }

        var r = run.Reconciliation;
        builder.Append("record_type")
            .Append(d).Append("configured_pool")
            .Append(d).Append("pool_credits")
            .Append(d).Append("available_pool")
            .Append(d).Append("member_total")
            .Append(d).Append("reserve_total")
            .Append(d).Append("settlement_total")
            .Append(d).Append("total_distributed")
            .Append(d).Append("surplus")
            .Append(d).Append("reconciled")
            .Append('\n');

        builder.Append("reconcile")
            .Append(d).Append(r.ConfiguredPool.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.PoolCredits.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.AvailablePool.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.MemberPayoutTotal.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.ReserveTotal.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.SettlementTotal.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.TotalDistributed.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.Surplus.ToString(CultureInfo.InvariantCulture))
            .Append(d).Append(r.IsReconciled)
            .Append('\n');

        return builder.ToString();
    }

    private static string Escape(string value, char delimiter)
    {
        if (!value.Contains(delimiter) && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
