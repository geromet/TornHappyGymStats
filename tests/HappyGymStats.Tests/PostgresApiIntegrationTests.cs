using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HappyGymStats.Api;
using HappyGymStats.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace HappyGymStats.Tests;

public sealed class PostgresApiIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const string SkipEnvVar = "HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION";
    private const string StartupTimeoutEnvVar = "HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS";

    private PostgreSqlContainer? _postgres;
    private string? _skipReason;
    private string _surfacesCacheDirectory = string.Empty;
    private PostgresApiFactory? _factory;

    public PostgresApiIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        if (ShouldForceSkipFromEnvironment())
            return;

        var startupTimeout = ResolveStartupTimeout();

        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("happygymstats")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            using var startupCts = new CancellationTokenSource(startupTimeout);
            await _postgres.StartAsync(startupCts.Token);
        }
        catch (OperationCanceledException)
        {
            _skipReason =
                $"[timeout] Postgres integration tests exceeded startup timeout of {startupTimeout.TotalSeconds:0}s while waiting for Docker/Testcontainers. " +
                $"Increase {StartupTimeoutEnvVar} or ensure Docker is healthy.";
            return;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _skipReason =
                $"[docker] Postgres integration tests require Docker. Start Docker locally and re-run. Details: {ex.Message}";
            return;
        }

        _surfacesCacheDirectory = Path.Combine(Path.GetTempPath(), "happygymstats-surfaces", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_surfacesCacheDirectory);

        try
        {
            _factory = new PostgresApiFactory(_postgres.GetConnectionString(), _surfacesCacheDirectory);

            // Force host startup so migration/startup failures are attributed to startup phase.
            using var _ = _factory.CreateClient();
        }
        catch (Exception ex)
        {
            throw new XunitException(
                $"[startup] Failed to build API host with Npgsql provider and run startup migrations. " +
                $"ConnectionString='{_postgres.GetConnectionString()}'. Details: {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        if (!string.IsNullOrWhiteSpace(_surfacesCacheDirectory) && Directory.Exists(_surfacesCacheDirectory))
            Directory.Delete(_surfacesCacheDirectory, recursive: true);

        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact(DisplayName = "PostgresApiIntegration: api startup health reports ok with Npgsql provider")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Api_startup_health_reports_ok_with_npgsql_provider()
    {
        if (ShouldSkipIntegration())
            return;

        using var client = _factory!.CreateClient();

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync("/api/v1/torn/health");
        }
        catch (Exception ex)
        {
            throw new XunitException($"[health] Failed calling /api/v1/torn/health after startup. Details: {ex.Message}");
        }

        Assert.True(
            response.IsSuccessStatusCode,
            $"[health] Expected success status but got {(int)response.StatusCode} ({response.StatusCode}).");

        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);

        Assert.True(
            payload.DatabaseProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            || payload.DatabaseProvider.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase),
            $"[provider] Expected Npgsql/PostgreSQL provider in health response but got '{payload.DatabaseProvider}'.");
    }

    [Fact(DisplayName = "PostgresApiIntegration: surfaces latest returns structured 404 when cache missing")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Surfaces_latest_returns_structured_404_when_cache_missing()
    {
        if (ShouldSkipIntegration())
            return;

        using var client = _factory!.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/surfaces/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("not_found", payload.Error.Code);
        Assert.Equal("No cached surfaces dataset found.", payload.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error.RequestId));
    }

    [Fact(DisplayName = "PostgresApiIntegration: surfaces latest returns cached json when present")]
    [Trait("Category", "PostgresApiIntegration")]
    public async Task Surfaces_latest_returns_cached_json_when_present()
    {
        if (ShouldSkipIntegration())
            return;

        var latestPath = Path.Combine(_surfacesCacheDirectory, "latest.json");
        var latestFixturePath = ResolveRepositoryPath("tests", "fixtures", "surfaces", "latest-confidence-sample.json");
        File.Copy(latestFixturePath, latestPath, overwrite: true);

        using var client = _factory!.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/surfaces/latest");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("generatedAtUtc", out _));
        Assert.True(root.TryGetProperty("series", out var series));
        Assert.Equal(JsonValueKind.Array, series.ValueKind);
    }

    private bool ShouldSkipIntegration()
    {
        if (_skipReason is null)
            return false;

        _output.WriteLine(_skipReason);
        return true;
    }

    private bool ShouldForceSkipFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(SkipEnvVar);
        if (!string.Equals(raw, "1", StringComparison.Ordinal)
            && !string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase))
            return false;

        _skipReason = $"[skip] {SkipEnvVar} is set; Postgres integration tier intentionally skipped.";
        return true;
    }

    private static TimeSpan ResolveStartupTimeout()
    {
        var raw = Environment.GetEnvironmentVariable(StartupTimeoutEnvVar);
        if (int.TryParse(raw, out var seconds) && seconds >= 15 && seconds <= 600)
            return TimeSpan.FromSeconds(seconds);

        return TimeSpan.FromSeconds(90);
    }

    private static string ResolveRepositoryPath(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");

        return Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
    }

    private sealed class PostgresApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _surfacesCacheDirectory;

        public PostgresApiFactory(string connectionString, string surfacesCacheDirectory)
        {
            _connectionString = connectionString;
            _surfacesCacheDirectory = surfacesCacheDirectory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ResolveApiContentRoot());
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HAPPYGYMSTATS_SURFACES_CACHE_DIR"] = _surfacesCacheDirectory,
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(DbContextOptions<HappyGymStatsDbContext>)
                                || (d.ServiceType.IsGenericType
                                    && d.ServiceType.GetGenericTypeDefinition().FullName?.StartsWith(
                                        "Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration",
                                        StringComparison.Ordinal) == true
                                    && d.ServiceType.GenericTypeArguments[0] == typeof(HappyGymStatsDbContext)))
                    .ToList();

                foreach (var descriptor in descriptors)
                    services.Remove(descriptor);

                services.AddDbContext<HappyGymStatsDbContext>(options => options.UseNpgsql(_connectionString));
            });
        }

        private static string ResolveApiContentRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
                dir = dir.Parent;

            if (dir is null)
                throw new DirectoryNotFoundException("Unable to locate repository root from test output directory.");

            return Path.Combine(dir.FullName, "src", "HappyGymStats.Api");
        }
    }
}
