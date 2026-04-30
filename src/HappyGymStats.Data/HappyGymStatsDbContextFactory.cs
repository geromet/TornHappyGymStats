using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HappyGymStats.Data;

public sealed class HappyGymStatsDbContextFactory : IDesignTimeDbContextFactory<HappyGymStatsDbContext>
{
    public HappyGymStatsDbContext CreateDbContext(string[] args)
    {
        var databasePath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "happygymstats.db");

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new HappyGymStatsDbContext(options);
    }
}
