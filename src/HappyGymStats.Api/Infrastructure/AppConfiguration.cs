namespace HappyGymStats.Api.Infrastructure;

internal static class AppConfiguration
{
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString("HappyGymStats")
               ?? configuration["HAPPYGYMSTATS_CONNECTION_STRING"]
               ?? throw new InvalidOperationException(
                   "No Postgres connection string found. Set ConnectionStrings:HappyGymStats or HAPPYGYMSTATS_CONNECTION_STRING.");
    }


    public static string ResolveDevelopmentSqliteConnectionString(
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        if (environment.IsProduction())
            throw new InvalidOperationException("Development SQLite mode cannot be enabled in Production.");

        var configured = configuration["HAPPYGYMSTATS_DEV_AUTH_SQLITE_PATH"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                return configured;

            if (configured.Contains(';') || configured.Contains("Host=", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "HAPPYGYMSTATS_DEV_AUTH_SQLITE_PATH must be a SQLite file path or Data Source=... connection string.");

            return $"Data Source={configured}";
        }

        var path = Path.Combine(Path.GetTempPath(), "happygymstats-dev-auth.sqlite");
        return $"Data Source={path}";
    }

    public static string ResolveSurfacesCacheDirectory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["HAPPYGYMSTATS_SURFACES_CACHE_DIR"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var repoRelativeCandidate = Path.GetFullPath(
            Path.Combine(environment.ContentRootPath, "..", "..", "..", "web", "data", "surfaces"));

        if (Directory.Exists(repoRelativeCandidate) || File.Exists(Path.Combine(repoRelativeCandidate, "meta.json")))
            return repoRelativeCandidate;

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "data", "surfaces"));
    }
}
