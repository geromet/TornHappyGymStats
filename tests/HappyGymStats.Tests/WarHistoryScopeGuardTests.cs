using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarHistoryScopeGuardTests
{
    [Fact]
    public void WarPoller_program_registers_the_disabled_by_default_ranked_war_history_backfill_service()
    {
        var source = ReadSource("src/HappyGymStats.WarPoller/Program.cs");

        Assert.Contains("AddHostedService<RankedWarHistoryBackfillHostedService>", source, StringComparison.Ordinal);

        var optionsSource = ReadSource("src/HappyGymStats.WarPoller/WarPollerOptions.cs");
        Assert.Contains("RankedWarHistoryBackfillEnabled { get; set; }", optionsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RankedWarHistoryBackfillEnabled { get; set; } = true", optionsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WarPoller_backfill_service_does_not_introduce_opponent_profile_scoring()
    {
        var workerSource = ReadSource("src/HappyGymStats.WarPoller/RankedWarHistoryBackfillWorker.cs");
        var hostedServiceSource = ReadSource("src/HappyGymStats.WarPoller/RankedWarHistoryBackfillHostedService.cs");

        Assert.DoesNotContain("OpponentProfile", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("LumpAdjusted", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OpponentProfile", hostedServiceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("/war/scout", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("/war/scout", hostedServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_program_does_not_expose_war_scout_route()
    {
        var source = ReadSource("src/HappyGymStats.Api/Program.cs");

        Assert.DoesNotContain("/war/scout", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGroup(\"/war/scout\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet(\"/war/scout\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost(\"/war/scout\"", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), relativePath));

    private static string ResolveRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(current, "HappyGymStats.slnx"))
                || File.Exists(Path.Combine(current, "HappyGymStats.sln")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
