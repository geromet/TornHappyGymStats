using System.Text;
using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Acceptance 7: the CSV/Excel export carries enough detail to audit the calculation externally —
/// every per-member payout line with its facts and rate, every member total, and the pool
/// reconciliation, all in a deterministic, re-parseable form.
/// </summary>
public sealed class WarAccountingExportTests
{
    private const long WarId = 48377;
    private const long AlphaId = 1001;
    private const long BetaId = 1002;

    private static WarLedger LedgerWithEscapingMemberName()
    {
        var war = WarAccountingTestData.CompletedWar(WarId);
        var members = new List<HappyGymStats.Data.Entities.RankedWarReportMemberEntity>
        {
            WarAccountingTestData.ReportMember(WarId, AlphaId, "O'Brien, \"Q\"", score: 355, attacks: 40),
            WarAccountingTestData.ReportMember(WarId, BetaId, "Beta", score: 260, attacks: 30),
        };

        return WarLedgerBuilder.Build(
            war,
            members,
            supplements: [new WarLedgerSupplement(AlphaId, "O'Brien, \"Q\"", WarLedgerEntryKind.FactionXanax, Amount: 250m)],
            adjustments:
            [
                new ManualAdjustmentInput(AlphaId, "O'Brien, \"Q\"", 50m, "LD1", "Treasurer",
                    new DateTimeOffset(2026, 1, 13, 9, 0, 0, TimeSpan.Zero), "Approved, per the vote"),
            ]);
    }

    [Fact]
    public void Csv_export_carries_every_payout_line_with_facts_and_rate()
    {
        var ledger = LedgerWithEscapingMemberName();
        var run = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(respectRate: 1.0m), configuredPool: 0m);

        var csv = PayoutExporter.ExportCsv(run, ',');
        var lines = ParseExport(csv, ',');
        var lineRows = lines["line"];

        // Every member payout line is exported with member, kind, category, description, respect,
        // count, rate and amount.
        var alphaLines = lineRows.Where(r => r[1] == AlphaId.ToString()).ToArray();
        Assert.Contains(alphaLines, r => r[5].Contains("Respect credit", StringComparison.Ordinal));
        Assert.Contains(alphaLines, r => r[6] == "355" && r[7] == "40"
            && decimal.Parse(r[8], System.Globalization.CultureInfo.InvariantCulture) == 1m
            && decimal.Parse(r[9], System.Globalization.CultureInfo.InvariantCulture) == 355m);
        Assert.Contains(alphaLines, r => r[3] == ((int)WarLedgerEntryKind.FactionXanax).ToString());

        var memberRows = lines["member"];
        var alphaMember = Assert.Single(memberRows, r => r[1] == AlphaId.ToString());
        Assert.Equal(655m, decimal.Parse(alphaMember[8], System.Globalization.CultureInfo.InvariantCulture)); // 355 - 0 reserve + 250 expense + 50 adjustment

        // The member name with comma and quotes round-trips through CSV quoting.
        Assert.Contains(lineRows, r => r[1] == AlphaId.ToString() && r[2] == "O'Brien, \"Q\"");
    }

    [Fact]
    public void Export_is_deterministic_and_reconciles_to_the_run()
    {
        var ledger = WarLedgerBuilder.Build(
            WarAccountingTestData.CompletedWar(WarId),
            [
                WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
                WarAccountingTestData.ReportMember(WarId, BetaId, "Beta", score: 260, attacks: 30),
            ]);
        var run = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(respectRate: 1.0m), configuredPool: 1000m);

        var first = PayoutExporter.ExportCsv(run, ',');
        var second = PayoutExporter.ExportCsv(run, ',');
        Assert.Equal(first, second);

        var rows = ParseExport(first, ',');
        var lineRows = rows["line"];
        var memberRows = rows["member"];
        var reconcile = Assert.Single(rows["reconcile"]);

        // The header pins the exact policy, version and fingerprints an auditor would re-run.
        Assert.Contains($"# policy,{run.PolicySnapshot.Name}", first.Split('\n'));
        Assert.Contains($"# policy_version,{run.PolicySnapshot.Version}", first.Split('\n'));
        Assert.Contains($"# ledger_fingerprint,{run.LedgerFingerprint}", first.Split('\n'));
        Assert.Contains($"# run_fingerprint,{run.RunFingerprint}", first.Split('\n'));

        // Line amounts add up to member totals, and member totals add up to the reconciliation.
        var sumOfLines = lineRows.Sum(r => decimal.Parse(r[9], System.Globalization.CultureInfo.InvariantCulture));
        var sumOfMemberTotals = memberRows.Sum(r => decimal.Parse(r[8], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(sumOfMemberTotals, sumOfLines);
        Assert.Equal(sumOfMemberTotals, decimal.Parse(reconcile[4], System.Globalization.CultureInfo.InvariantCulture));

        // Reconciliation row matches the run exactly.
        Assert.Equal(run.Reconciliation.Surplus.ToString(System.Globalization.CultureInfo.InvariantCulture), reconcile[8]);
        Assert.Equal(run.Reconciliation.IsReconciled.ToString(), reconcile[9]);
    }

    [Fact]
    public void Tab_separated_export_parses_identically_for_excel()
    {
        var ledger = WarLedgerBuilder.Build(
            WarAccountingTestData.CompletedWar(WarId),
            [
                WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40),
                WarAccountingTestData.ReportMember(WarId, BetaId, "Beta", score: 260, attacks: 30),
            ]);
        var run = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(respectRate: 1.0m), configuredPool: 0m);

        var csv = PayoutExporter.ExportCsv(run, ',');
        var tsv = PayoutExporter.ExportCsv(run, '\t');

        var csvRows = ParseExport(csv, ',');
        var tsvRows = ParseExport(tsv, '\t');

        Assert.Equal(csvRows.Keys, tsvRows.Keys);
        foreach (var key in csvRows.Keys)
        {
            Assert.Equal(csvRows[key], tsvRows[key]);
        }
    }

    private static Dictionary<string, List<string[]>> ParseExport(string export, char delimiter)
    {
        var rows = new Dictionary<string, List<string[]>>();
        foreach (var raw in export.Split('\n'))
        {
            if (raw.Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var fields = ParseCsvLine(raw, delimiter);
            if (fields.Count == 0 || fields[0] is not ("line" or "member" or "reconcile"))
            {
                continue;
            }

            if (!rows.TryGetValue(fields[0], out var list))
            {
                list = [];
                rows[fields[0]] = list;
            }

            list.Add(fields.ToArray());
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
