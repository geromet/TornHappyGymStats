namespace HappyGymStats.AdminPanel.Infrastructure;

internal static class AdminAppConfiguration
{
    public static string ResolveConnectionString(IConfiguration configuration)
        => configuration.GetConnectionString("HappyGymStats")
           ?? configuration["HAPPYGYMSTATS_CONNECTION_STRING"]
           ?? throw new InvalidOperationException(
               "No Postgres connection string found. Set ConnectionStrings:HappyGymStats or HAPPYGYMSTATS_CONNECTION_STRING.");
}
