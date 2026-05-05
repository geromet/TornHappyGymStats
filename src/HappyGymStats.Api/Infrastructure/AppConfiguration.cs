using HappyGymStats.Core.Storage;
using HappyGymStats.Data.Storage;

namespace HappyGymStats.Api.Infrastructure;

internal static class AppConfiguration
{
    public static string ResolveDatabasePath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration.GetConnectionString("HappyGymStats")
                             ?? configuration["HAPPYGYMSTATS_DATABASE"];

        var fallbackDataDirectory = DataDirectory.ResolveBasePath("HappyGymStats");
        return SqlitePaths.ResolveDatabasePath(fallbackDataDirectory, configuredPath);
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
