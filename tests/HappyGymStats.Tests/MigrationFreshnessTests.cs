using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Tests;

public sealed class MigrationFreshnessTests
{
    [Fact]
    public void Current_model_matches_latest_migration_snapshot()
    {
        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql("Host=localhost;Database=happygymstats_migration_freshness;Username=unused;Password=unused")
            .Options;

        using var db = new HappyGymStatsDbContext(options);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The current EF model differs from the latest migration snapshot. Add and review a migration before merging model changes.");
    }
}
