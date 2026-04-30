using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class ApiEndpointTests : IClassFixture<ApiEndpointTests.TestApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly TestApplicationFactory _factory;

    public ApiEndpointTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("HappyGymStats.Api", payload.Api);
        Assert.Contains("Sqlite", payload.DatabaseProvider, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_endpoints_allow_cross_origin_get_requests()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        request.Headers.Add("Origin", "https://example.com");

        var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        Assert.Contains("*", response.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Gym_trains_endpoint_uses_cursor_pagination()
    {
        await _factory.SeedGymTrainsAsync(
            new DerivedGymTrainEntity
            {
                LogId = "train-c",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 12, 00, 00, TimeSpan.Zero),
                HappyBeforeTrain = 300,
                HappyAfterTrain = 250,
                HappyUsed = 50,
                RegenTicksApplied = 0,
                RegenHappyGained = 0,
                MaxHappyAtTimeUtc = 500,
                ClampedToMax = false,
            },
            new DerivedGymTrainEntity
            {
                LogId = "train-b",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 12, 00, 00, TimeSpan.Zero),
                HappyBeforeTrain = 280,
                HappyAfterTrain = 240,
                HappyUsed = 40,
                RegenTicksApplied = 0,
                RegenHappyGained = 0,
                MaxHappyAtTimeUtc = 500,
                ClampedToMax = false,
            },
            new DerivedGymTrainEntity
            {
                LogId = "train-a",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 11, 45, 00, TimeSpan.Zero),
                HappyBeforeTrain = 260,
                HappyAfterTrain = 220,
                HappyUsed = 40,
                RegenTicksApplied = 1,
                RegenHappyGained = 5,
                MaxHappyAtTimeUtc = 500,
                ClampedToMax = false,
            });

        using var client = _factory.CreateClient();

        var firstPage = await client.GetFromJsonAsync<CursorPage<GymTrainDto>>("/v1/gym-trains?limit=2", JsonOptions);

        Assert.NotNull(firstPage);
        Assert.Equal(new[] { "train-c", "train-b" }, firstPage.Items.Select(x => x.LogId).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        var secondPage = await client.GetFromJsonAsync<CursorPage<GymTrainDto>>($"/v1/gym-trains?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}", JsonOptions);

        Assert.NotNull(secondPage);
        Assert.Equal(new[] { "train-a" }, secondPage.Items.Select(x => x.LogId).ToArray());
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task Happy_events_endpoint_uses_cursor_pagination()
    {
        await _factory.SeedHappyEventsAsync(
            new DerivedHappyEventEntity
            {
                EventId = "event-c",
                EventType = "gym_train",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 12, 00, 00, TimeSpan.Zero),
                SourceLogId = "train-c",
                HappyBeforeEvent = 300,
                HappyAfterEvent = 250,
                Delta = -50,
                Note = null,
            },
            new DerivedHappyEventEntity
            {
                EventId = "event-b",
                EventType = "gym_train",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 12, 00, 00, TimeSpan.Zero),
                SourceLogId = "train-b",
                HappyBeforeEvent = 280,
                HappyAfterEvent = 240,
                Delta = -40,
                Note = null,
            },
            new DerivedHappyEventEntity
            {
                EventId = "event-a",
                EventType = "regen",
                OccurredAtUtc = new DateTimeOffset(2026, 04, 30, 11, 45, 00, TimeSpan.Zero),
                SourceLogId = null,
                HappyBeforeEvent = 255,
                HappyAfterEvent = 260,
                Delta = 5,
                Note = "quarter-hour tick",
            });

        using var client = _factory.CreateClient();

        var firstPage = await client.GetFromJsonAsync<CursorPage<HappyEventDto>>("/v1/happy-events?limit=2", JsonOptions);

        Assert.NotNull(firstPage);
        Assert.Equal(new[] { "event-c", "event-b" }, firstPage.Items.Select(x => x.EventId).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        var secondPage = await client.GetFromJsonAsync<CursorPage<HappyEventDto>>($"/v1/happy-events?limit=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}", JsonOptions);

        Assert.NotNull(secondPage);
        Assert.Equal(new[] { "event-a" }, secondPage.Items.Select(x => x.EventId).ToArray());
        Assert.Null(secondPage.NextCursor);
    }

    [Fact]
    public async Task Invalid_limit_returns_standard_validation_error()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/gym-trains?limit=999");

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
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/happy-events?cursor=not-base64");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("validation_failed", payload.Error.Code);
        Assert.Equal("Cursor is invalid.", payload.Error.Message);
        Assert.False(string.IsNullOrWhiteSpace(payload.Error.RequestId));
    }

    [Fact]
    public async Task Import_latest_returns_not_found_before_any_import()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/import/latest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("not_found", payload.Error.Code);
    }

    [Fact]
    public async Task Import_requires_api_key()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/import", new { fresh = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("validation_failed", payload.Error.Code);
        Assert.Equal("apiKey is required.", payload.Error.Message);
    }

    [Fact]
    public async Task Import_endpoint_accepts_request_and_exposes_latest_status()
    {
        using var client = _factory.CreateClient();

        var startResponse = await client.PostAsJsonAsync("/v1/import", new { apiKey = "bad-key-for-test", fresh = true });
        Assert.True(startResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

        var startPayload = await startResponse.Content.ReadFromJsonAsync<ImportStatusDto>(JsonOptions);
        Assert.NotNull(startPayload);
        Assert.False(string.IsNullOrWhiteSpace(startPayload.Id));

        var latestResponse = await client.GetAsync("/v1/import/latest");
        latestResponse.EnsureSuccessStatusCode();

        var latestPayload = await latestResponse.Content.ReadFromJsonAsync<ImportStatusDto>(JsonOptions);
        Assert.NotNull(latestPayload);
        Assert.Equal(startPayload.Id, latestPayload.Id);
        Assert.Contains(latestPayload.Outcome, new[] { "queued", "running", "failed", "completed", "cancelled" });
    }

    public sealed class TestApplicationFactory : WebApplicationFactory<Program>
    {
        private SqliteConnection? _connection;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(ResolveApiContentRoot());
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                _connection?.Dispose();
                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();

                var dbContextDescriptor = services.SingleOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(DbContextOptions<HappyGymStatsDbContext>));

                if (dbContextDescriptor is not null)
                    services.Remove(dbContextDescriptor);

                var connectionDescriptor = services.SingleOrDefault(descriptor =>
                    descriptor.ServiceType == typeof(SqliteConnection));

                if (connectionDescriptor is not null)
                    services.Remove(connectionDescriptor);

                services.AddSingleton(_connection);
                services.AddDbContext<HappyGymStatsDbContext>(options => options.UseSqlite(_connection));
            });
        }

        public void ResetDatabase()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            db.DerivedGymTrains.RemoveRange(db.DerivedGymTrains);
            db.DerivedHappyEvents.RemoveRange(db.DerivedHappyEvents);
            db.ImportCheckpoints.RemoveRange(db.ImportCheckpoints);
            db.ImportRuns.RemoveRange(db.ImportRuns);
            db.RawUserLogs.RemoveRange(db.RawUserLogs);
            db.SaveChanges();
        }

        public async Task SeedGymTrainsAsync(params DerivedGymTrainEntity[] rows)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            db.DerivedGymTrains.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public async Task SeedHappyEventsAsync(params DerivedHappyEventEntity[] rows)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
            db.DerivedHappyEvents.AddRange(rows);
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
}
