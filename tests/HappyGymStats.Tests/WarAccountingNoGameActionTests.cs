using System.IO;
using System.Linq;
using System.Reflection;
using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Acceptance 8 and the standing "no game actions, ever" non-goal for the accounting module: no code
/// path in the ledger/payout pipeline may issue a state-changing request to Torn (no money transfer,
/// no item give, no attack/refill/travel). The accounting module is pure — it turns a ledger and a
/// policy into a value and a CSV string — so there is nothing here that could even reach Torn.
/// These tests make that a property of the source, not a promise.
/// </summary>
public sealed class WarAccountingNoGameActionTests
{
    private const long WarId = 48377;
    private const long AlphaId = 1001;

    private static string AccountingDirectory
        => Path.Combine(FindRepoRoot(), "src", "HappyGymStats.Core", "War", "Accounting");

    [Fact]
    public void Accounting_module_never_references_a_state_changing_torn_surface()
    {
        var files = Directory.GetFiles(AccountingDirectory, "*.cs");
        Assert.NotEmpty(files);

        var forbiddenTokens = new[]
        {
            "TornApiClient",
            "api.torn.com",
            "HttpClient",
            "SendAsync",
            "PostAsync",
            "PutAsync",
            "DeleteAsync",
            "GiveItem",
            "TransferMoney",
            "SendMoney",
            "SendItems",
            "AttackAsync",
        };

        // "attacks" as a report fact (member.Attacks) is data, not an action; what is banned is a
        // state-changing Torn call. The tokens above are the verbs and surfaces such a call would
        // need; "AttackAsync" is the action form of a Torn attack, which no accounting code may make.

        var violations = files
            .SelectMany(file => forbiddenTokens
                .Where(token => File.ReadAllText(file).Contains(token, System.StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Path.GetFileName(file)} -> {token}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "State-changing Torn references found in the accounting module:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Accounting_module_has_no_network_or_database_dependencies()
    {
        var forbiddenUsings = new[]
        {
            "using System.Net.Http",
            "using System.Net.Sockets",
            "using Microsoft.EntityFrameworkCore",
            "using Microsoft.Extensions.DependencyInjection",
            "using Npgsql",
        };

        var violations = Directory.GetFiles(AccountingDirectory, "*.cs")
            .SelectMany(file => forbiddenUsings
                .Where(use => File.ReadAllText(file).Contains(use, System.StringComparison.Ordinal))
                .Select(use => $"{Path.GetFileName(file)} -> {use}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Network/database dependencies found in the accounting module:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void The_accounting_engine_is_pure_it_returns_values_and_changes_nothing()
    {
        var ledger = WarLedgerBuilder.Build(
            WarAccountingTestData.CompletedWar(WarId),
            [WarAccountingTestData.ReportMember(WarId, AlphaId, "Alpha", score: 355, attacks: 40)]);
        var ledgerBefore = PayoutFingerprint.OfLedger(ledger);

        var run = PayoutEngine.Preview(ledger, WarAccountingTestData.RespectPolicy(), configuredPool: 0m);
        var approved = PayoutEngine.Approve(
            ledger, WarAccountingTestData.RespectPolicy(), 0m,
            new System.DateTimeOffset(2026, 1, 13, 12, 0, 0, System.TimeSpan.Zero), "LD1", "Treasurer");
        var csv = PayoutExporter.ExportCsv(run, ',');

        // Previewing, approving and exporting never touch the ledger, the network or the clock.
        Assert.Equal(ledgerBefore, PayoutFingerprint.OfLedger(ledger));
        Assert.Equal(run.RunFingerprint, approved.Run.RunFingerprint);
        Assert.StartsWith("# HappyGymStats payout run export", csv, System.StringComparison.Ordinal);

        // The only output surfaces of the module are plain data and a string: nothing in the
        // accounting surface is async, and no public method takes a network client as input.
        var accountingTypes = new[]
        {
            typeof(WarLedger),
            typeof(WarLedgerEntry),
            typeof(WarLedgerBuilder),
            typeof(PayoutPolicy),
            typeof(PayoutEngine),
            typeof(PayoutExporter),
            typeof(PayoutFingerprint),
        };

        var asyncMethods = accountingTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.ReturnType == typeof(Task)
                             || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToArray();

        Assert.True(
            asyncMethods.Length == 0,
            "Async (I/O-capable) methods found in the accounting module:\n" + string.Join('\n', asyncMethods));

        var networkParams = accountingTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.GetParameters()
                .Any(p => p.ParameterType.Name.Contains("HttpClient", StringComparison.Ordinal)
                          || p.ParameterType.Name.Contains("TornApiClient", StringComparison.Ordinal)))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToArray();

        Assert.True(
            networkParams.Length == 0,
            "Methods accepting a network client found in the accounting module:\n" + string.Join('\n', networkParams));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
