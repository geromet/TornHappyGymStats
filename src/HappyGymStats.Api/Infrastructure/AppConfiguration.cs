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
