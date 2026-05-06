using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HappyGymStats.Data;

public sealed class HappyGymStatsDbContextFactory : IDesignTimeDbContextFactory<HappyGymStatsDbContext>
{
    public HappyGymStatsDbContext CreateDbContext(string[] args)
    {
        var connectionString = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
            ? args[0]
            : "Host=localhost;Database=happygymstats;Username=happygymstats;Password=changeme";

        var options = new DbContextOptionsBuilder<HappyGymStatsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new HappyGymStatsDbContext(options);
    }
}
