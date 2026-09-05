using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using HappyGymStats.Api;
using HappyGymStats.Core.Models;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Fast API endpoint tests that intentionally run against the in-memory SQLite test host.
/// These validate endpoint contracts and pagination behavior, not production-provider parity.
/// </summary>
public sealed class SqliteApiEndpointTests : IClassFixture<SqliteApiEndpointTests.SqliteTestApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly SqliteTestApplicationFactory _factory;

    public SqliteApiEndpointTests(SqliteTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact(DisplayName = "SqliteApiEndpoint: health endpoint reports ok with SQLite provider")]
    [Trait("Category", "SqliteApiEndpoint")]
    public async Task Health_endpoint_reports_ok_with_sqlite_provider()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/health");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("HappyGymStats.Api", payload.Api);
        // This assertion is SQLite-tier specific; production provider parity is covered by PostgresApiIntegrationTests.
        Assert.Contains("Sqlite", payload.DatabaseProvider, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_endpoints_allow_cross_origin_get_requests()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/torn/health");
        request.Headers.Add("Origin", "https://example.com");

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Contains("*", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Gym_trains_global_collection_route_is_not_exposed()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/gym-trains?limit=2");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Gym_trains_endpoint_uses_owner_scoped_cursor_pagination()
    {
        var callerAnonymousId = Guid.NewGuid();
        var otherAnonymousId = Guid.NewGuid();

        await _factory.SeedUserLogEntriesAsync(
            new UserLogEntryEntity
            {
                AnonymousId = otherAnonymousId,
                LogEntryId = "other-newest",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 13, 00, 00, TimeSpan.Zero),
                LogTypeId = 1,
                HappyBeforeTrain = 900,
                HappyUsed = 100,
            },
            new UserLogEntryEntity
            {
                AnonymousId = callerAnonymousId,
                LogEntryId = "train-c",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 12, 00, 00, TimeSpan.Zero),
                LogTypeId = 1,
                HappyBeforeTrain = 300,
                HappyUsed = 50,
            },
            new UserLogEntryEntity
            {
                AnonymousId = callerAnonymousId,
                LogEntryId = "train-b",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 12, 00, 00, TimeSpan.Zero),
                LogTypeId = 1,
                HappyBeforeTrain = 280,
                HappyUsed = 40,
            },
            new UserLogEntryEntity
            {
                AnonymousId = callerAnonymousId,
                LogEntryId = "train-a",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 11, 45, 00, TimeSpan.Zero),
                LogTypeId = 1,
                HappyBeforeTrain = 260,
                HappyUsed = 40,
            });

        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString());
        var route = $"/api/v1/torn/gym-trains/{callerAnonymousId}";

        var firstPage = await client.GetFromJsonAsync<CursorPage<GymTrainDto>>($"{route}?limit=2", JsonOptions);

        Assert.NotNull(firstPage);
        Assert.Equal(new[] { "train-c", "train-b" }, firstPage.Items.Select(x => x.LogId).ToArray());
        Assert.DoesNotContain(firstPage.Items, x => x.LogId == "other-newest");
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        var secondPage = await client.GetFromJsonAsync<CursorPage<GymTrainDto>>($"{route}?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}", JsonOptions);

        Assert.NotNull(secondPage);
        Assert.Equal(new[] { "train-a" }, secondPage.Items.Select(x => x.LogId).ToArray());
        Assert.DoesNotContain(secondPage.Items, x => x.LogId == "other-newest");
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task Gym_trains_endpoint_forbids_cross_owner_path_manipulation()
    {
        var callerAnonymousId = Guid.NewGuid();
        var otherAnonymousId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString());

        var response = await client.GetAsync($"/api/v1/torn/gym-trains/{otherAnonymousId}?limit=2");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_limit_returns_standard_validation_error()
    {
        var callerAnonymousId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString());

        var response = await client.GetAsync($"/api/v1/torn/gym-trains/{callerAnonymousId}?limit=999");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("validation_failed", payload.Error.Code);
        Assert.Equal("Limit must be between 1 and 200.", payload.Error.Message);
        Assert.NotNull(payload.Error.Details);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error.RequestId));
    }

    [Fact]
    public async Task Invalid_cursor_returns_standard_validation_error()
    {
        var callerAnonymousId = Guid.NewGuid();
        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString());

        var response = await client.GetAsync($"/api/v1/torn/gym-trains/{callerAnonymousId}?cursor=not-base64");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("validation_failed", payload.Error.Code);
        Assert.Equal("Cursor is invalid.", payload.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error.RequestId));
    }

    [Fact]
    public async Task Import_latest_route_is_not_exposed()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/import-jobs/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Surfaces_latest_returns_structured_not_found_when_cache_missing()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/surfaces/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("not_found", payload.Error.Code);
        Assert.Equal("No cached surfaces dataset found.", payload.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error.RequestId));
    }

    [Fact]
    public async Task Surfaces_meta_returns_structured_not_found_when_cache_missing()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/torn/surfaces/meta");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("not_found", payload.Error.Code);
        Assert.Equal("No cached surfaces dataset found.", payload.Error.Message);
    }

    [Fact]
    public async Task Surfaces_raw_cache_files_are_not_directly_served()
    {
        // Resolve the surfaces cache directory using the same priority the host uses
        // (env var → repo-relative web/data/surfaces → ContentRootPath/data/surfaces).
        var env = _factory.Services.GetRequiredService<IWebHostEnvironment>();
        var configuredDir = Environment.GetEnvironmentVariable("HAPPYGYMSTATS_SURFACES_CACHE_DIR");
        string cacheDir;
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            cacheDir = configuredDir;
        }
        else
        {
            var candidate = Path.GetFullPath(
                Path.Combine(env.ContentRootPath, "..", "..", "..", "web", "data", "surfaces"));
            cacheDir = Directory.Exists(candidate) || File.Exists(Path.Combine(candidate, "meta.json"))
                ? candidate
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, "data", "surfaces"));
        }

        // Write a representative working file so the negative control is structural,
        // not dependent on the cache being empty.
        Directory.CreateDirectory(cacheDir);
        var probeFile = Path.Combine(cacheDir, "raw-denial-probe.json");
        await File.WriteAllTextAsync(probeFile, """{"probe": true}""");

        try
        {
            using var client = _factory.CreateClient();

            // The raw cache path must not be served by the API host.
            var rawResponse = await client.GetAsync("/data/surfaces/raw-denial-probe.json");
            Assert.Equal(HttpStatusCode.NotFound, rawResponse.StatusCode);

            // The sanitised projection endpoint must still be reachable (structured 404 = cache empty).
            var apiResponse = await client.GetAsync("/api/v1/torn/surfaces/latest");
            var payload = await apiResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal("not_found", payload.Error.Code);
        }
        finally
        {
            File.Delete(probeFile);
        }
    }

    [Fact]
    public async Task Surfaces_me_returns_unauthorized_when_anonymous_id_claim_missing_or_invalid()
    {
        using var clientWithoutClaim = _factory.CreateAuthenticatedClient(null);
        var missingClaimResponse = await clientWithoutClaim.GetAsync("/api/v1/torn/surfaces/me");

        Assert.Equal(HttpStatusCode.Unauthorized, missingClaimResponse.StatusCode);
        var missingClaimPayload = await missingClaimResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);
        Assert.NotNull(missingClaimPayload);
        Assert.Equal("unauthorized", missingClaimPayload.Error.Code);

        using var clientWithInvalidClaim = _factory.CreateAuthenticatedClient("not-a-guid");
        var invalidClaimResponse = await clientWithInvalidClaim.GetAsync("/api/v1/torn/surfaces/me");

        Assert.Equal(HttpStatusCode.Unauthorized, invalidClaimResponse.StatusCode);
        var invalidClaimPayload = await invalidClaimResponse.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);
        Assert.NotNull(invalidClaimPayload);
        Assert.Equal("unauthorized", invalidClaimPayload.Error.Code);
    }

    [Fact]
    public async Task Surfaces_me_returns_only_caller_scoped_gym_points()
    {
        var callerAnonymousId = Guid.NewGuid();
        var otherAnonymousId = Guid.NewGuid();

        await _factory.SeedUserLogEntriesAsync(
            new UserLogEntryEntity
            {
                AnonymousId = callerAnonymousId,
                LogEntryId = "caller-train",
                OccurredAtUtc = new DateTimeOffset(2026, 05, 01, 12, 00, 00, TimeSpan.Zero),
                LogTypeId = 1,
                HappyBeforeTrain = 600,
                HappyUsed = 50,
                EnergyUsed = 10,
                StrengthBefore = 2000,
                StrengthIncreased = 25,
            },
            new UserLogEntryEntity
            {
                AnonymousId = otherAnonymousId,
                LogEntryId = "other-train",
                OccurredAtUtc = new DateTimeOffset(2026, 05, 01, 13, 00, 00, TimeSpan.Zero),
                LogTypeId = 1,
                HappyBeforeTrain = 650,
                HappyUsed = 50,
                EnergyUsed = 10,
                StrengthBefore = 3000,
                StrengthIncreased = 30,
            });

        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString());
        var response = await client.GetAsync("/api/v1/torn/surfaces/me");

        response.EnsureSuccessStatusCode();

        using var payloadDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payloadDoc.RootElement;

        Assert.Equal("surfaces", root.GetProperty("dataset").GetString());
        Assert.Equal(1, root.GetProperty("meta").GetProperty("gymPointCount").GetInt32());

        var gymX = root.GetProperty("series").GetProperty("gymCloud").GetProperty("x");
        Assert.Single(gymX.EnumerateArray());
        Assert.Equal(2000, gymX[0].GetDouble());
    }

    [Fact]
    public async Task Import_requires_api_key()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/torn/import-jobs", new { fresh = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("validation_failed", payload.Error.Code);
        Assert.Equal("apiKey is required.", payload.Error.Message);
    }

    [Fact]
    public async Task Import_me_requires_valid_anonymous_id_claim()
    {
        using var clientMissingClaim = _factory.CreateAuthenticatedClient(null);
        var missingClaimResponse = await clientMissingClaim.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new { apiKey = "key" });

        Assert.Equal(HttpStatusCode.Unauthorized, missingClaimResponse.StatusCode);

        using var clientInvalidClaim = _factory.CreateAuthenticatedClient("not-a-guid");
        var invalidClaimResponse = await clientInvalidClaim.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new { apiKey = "key" });

        Assert.Equal(HttpStatusCode.Unauthorized, invalidClaimResponse.StatusCode);
    }

    [Fact]
    public async Task Import_me_returns_identity_setup_required_when_identity_map_missing()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new { apiKey = "key" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("identity_setup_required", payload.Error.Code);
    }

    [Fact]
    public async Task Import_me_rejects_identity_map_subject_mismatch()
    {
        var callerAnonymousId = Guid.NewGuid();
        await _factory.SeedIdentityMapEntriesAsync(new IdentityMapEntity
        {
            AnonymousId = callerAnonymousId,
            KeycloakSub = "mapped-sub",
            IsProvisional = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString(), keycloakSub: "different-sub");
        var response = await client.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new { apiKey = "key" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Import_me_binds_to_caller_anonymous_id_and_ignores_body_tampering()
    {
        var callerAnonymousId = Guid.NewGuid();
        var attackerAnonymousId = Guid.NewGuid();
        await _factory.SeedIdentityMapEntriesAsync(new IdentityMapEntity
        {
            AnonymousId = callerAnonymousId,
            KeycloakSub = "test-sub",
            IsProvisional = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        using var client = _factory.CreateAuthenticatedClient(callerAnonymousId.ToString(), keycloakSub: "test-sub");
        var response = await client.PostAsJsonAsync("/api/v1/torn/import-jobs/me", new
        {
            apiKey = "bad-key-for-test",
            anonymousId = attackerAnonymousId,
            ownerAnonymousId = attackerAnonymousId,
            fresh = false,
        });

        Assert.True(response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<HappyGymStats.Core.Import.ImportOrchestrator>();
        Assert.NotNull(orchestrator.Latest);
        Assert.Equal(callerAnonymousId, orchestrator.Latest!.AnonymousId);

        var publicLatestResponse = await client.GetAsync("/api/v1/torn/import-jobs/latest");
        Assert.Equal(HttpStatusCode.NotFound, publicLatestResponse.StatusCode);
    }

    [Fact]
    public async Task Import_endpoint_returns_own_status_without_global_latest_projection()
    {
        using var client = _factory.CreateClient();

        var startResponse = await client.PostAsJsonAsync("/api/v1/torn/import-jobs", new { apiKey = "bad-key-for-test", fresh = true });
        Assert.True(startResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        var startPayload = await startResponse.Content.ReadFromJsonAsync<ImportStatusDto>(JsonOptions);
        Assert.NotNull(startPayload);
        Assert.False(string.IsNullOrWhiteSpace(startPayload.Id));
        Assert.Contains(startPayload.Outcome, new[] { "queued", "running", "failed", "completed", "cancelled" });

        var latestResponse = await client.GetAsync("/api/v1/torn/import-jobs/latest");
        Assert.Equal(HttpStatusCode.NotFound, latestResponse.StatusCode);
    }

    public sealed class SqliteTestApplicationFactory : WebApplicationFactory<Program>
    {
        private SqliteConnection? _connection;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ResolveApiContentRoot());
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                _connection?.Dispose();
                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();

                services.RemoveAll(typeof(DbContextOptions<HappyGymStatsDbContext>));
                services.RemoveAll(typeof(HappyGymStatsDbContext));

                var efContextOptionConfigs = services
                    .Where(descriptor => descriptor.ServiceType.IsGenericType
                        && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IDbContextOptionsConfiguration<>)
                        && descriptor.ServiceType.GetGenericArguments()[0] == typeof(HappyGymStatsDbContext))
                    .ToList();

                foreach (var descriptor in efContextOptionConfigs)
                    services.Remove(descriptor);

                services.RemoveAll(typeof(SqliteConnection));

                services.AddSingleton(_connection);
                services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite(_connection));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        }

        public HttpClient CreateAuthenticatedClient(string? anonymousIdClaim, string keycloakSub = "test-sub")
        {
            var client = CreateClient();
            if (anonymousIdClaim is not null)
                client.DefaultRequestHeaders.Add(TestAuthHandler.AnonymousIdHeader, anonymousIdClaim);

            client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, keycloakSub);

            return client;
        }

        public void ResetDatabase()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            db.UserLogEntries.RemoveRange(db.UserLogEntries);
            db.ImportRuns.RemoveRange(db.ImportRuns);
            db.IdentityMap.RemoveRange(db.IdentityMap);
            db.SaveChanges();

            var orchestrator = scope.ServiceProvider.GetRequiredService<HappyGymStats.Core.Import.ImportOrchestrator>();
            var latestField = typeof(HappyGymStats.Core.Import.ImportOrchestrator)
                .GetField("_latest", BindingFlags.Instance | BindingFlags.NonPublic);
            latestField?.SetValue(orchestrator, null);
        }

        public async Task SeedUserLogEntriesAsync(params UserLogEntryEntity[] rows)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            db.UserLogEntries.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public async Task SeedIdentityMapEntriesAsync(params IdentityMapEntity[] rows)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            db.IdentityMap.AddRange(rows);
            await db.SaveChangesAsync();
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _connection?.Dispose();
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string AnonymousIdHeader = "X-Test-AnonymousId";
        public const string SubjectHeader = "X-Test-Subject";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = "test-sub";
            if (Request.Headers.TryGetValue(SubjectHeader, out var subjectHeader)
                && !string.IsNullOrWhiteSpace(subjectHeader.ToString()))
            {
                subject = subjectHeader.ToString();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, subject),
                new(ClaimTypes.Name, "test-user"),
                new(ClaimTypes.Role, Roles.User),
            };

            if (Request.Headers.TryGetValue(AnonymousIdHeader, out var anonymousIdHeader)
                && !string.IsNullOrWhiteSpace(anonymousIdHeader.ToString()))
            {
                claims.Add(new Claim(Claims.AnonymousId, anonymousIdHeader.ToString()));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
