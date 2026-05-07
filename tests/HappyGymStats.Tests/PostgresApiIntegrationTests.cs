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

namespace HappyGymStats.Tests;

public sealed class PostgresApiIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private PostgreSqlContainer? _postgres;

    private string? _dockerUnavailableReason;
    private string _surfacesCacheDirectory = string.Empty;
    private PostgresApiFactory? _factory;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("happygymstats")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _postgres.StartAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dockerUnavailableReason = $"Postgres integration tests require Docker. Start Docker locally and re-run: {ex.Message}";
            return;
        }

        _surfacesCacheDirectory = Path.Combine(Path.GetTempPath(), "happygymstats-surfaces", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_surfacesCacheDirectory);
        _factory = new PostgresApiFactory(_postgres.GetConnectionString(), _surfacesCacheDirectory);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        if (!string.IsNullOrWhiteSpace(_surfacesCacheDirectory) && Directory.Exists(_surfacesCacheDirectory))
            Directory.Delete(_surfacesCacheDirectory, recursive: true);

        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Fact(DisplayName = "PostgresApiIntegration: surfaces latest returns structured 404 when cache missing")]
    public async Task Surfaces_latest_returns_structured_404_when_cache_missing()
    {
        if (_dockerUnavailableReason is not null)
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
    public async Task Surfaces_latest_returns_cached_json_when_present()
    {
        if (_dockerUnavailableReason is not null)
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
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<HappyGymStatsDbContext>));
                if (descriptor is not null)
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
