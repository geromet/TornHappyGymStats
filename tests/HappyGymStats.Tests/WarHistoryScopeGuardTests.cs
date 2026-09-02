using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarHistoryScopeGuardTests
{
    [Fact]
    public void WarPoller_program_does_not_register_ranked_war_history_backfill_service()
    {
        var source = ReadSource("src/HappyGymStats.WarPoller/Program.cs");

        Assert.DoesNotContain("WarHistoryBackfill", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RankedWarHistoryBackfill", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostedService<WarHistory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostedService<RankedWarHistory", source, StringComparison.Ordinal);
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
