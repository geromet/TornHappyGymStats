using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using HappyGymStats.Api;
using HappyGymStats.Api.Models;
using HappyGymStats.Core.Models;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarScoutEndpointTests : IClassFixture<SqliteApiEndpointTests.SqliteTestApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const long ScoutedFactionId = 222;

    private readonly SqliteApiEndpointTests.SqliteTestApplicationFactory _factory;

    public WarScoutEndpointTests(SqliteApiEndpointTests.SqliteTestApplicationFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
        ResetScoutTables();
    }

    [Fact]
    public async Task Scout_endpoint_rejects_anonymous_requests()
    {
        using var anonymousApp = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = RejectingAuthHandler.SchemeName;
                options.DefaultChallengeScheme = RejectingAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, RejectingAuthHandler>(RejectingAuthHandler.SchemeName, _ => { });
        }));
        using var client = anonymousApp.CreateClient();

        var response = await client.GetAsync($"/api/v1/war/scout/{ScoutedFactionId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scout_endpoint_rejects_non_positive_faction_ids()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString());

        var response = await client.GetAsync("/api/v1/war/scout/0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Scout_endpoint_returns_404_when_no_captured_history_exists()
    {
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString());

        var response = await client.GetAsync($"/api/v1/war/scout/{ScoutedFactionId}");
        var envelope = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(envelope);
        Assert.Equal("no_war_history", envelope.Error.Code);
    }

    [Fact]
    public async Task Scout_endpoint_returns_aggregated_profile_for_a_faction_with_captured_history()
    {
        await SeedWarAndReportAsync();

        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid().ToString());

        var response = await client.GetAsync($"/api/v1/war/scout/{ScoutedFactionId}");
        response.EnsureSuccessStatusCode();

        var profile = await response.Content.ReadFromJsonAsync<FactionScoutDto>(JsonOptions);
        Assert.NotNull(profile);
        Assert.Equal(ScoutedFactionId, profile.FactionId);
        Assert.Equal(1, profile.TotalWarsObserved);
        var member = Assert.Single(profile.Members);
        Assert.Equal(9001, member.MemberId);
        Assert.Equal(80, member.TotalScore);
    }

    private async Task SeedWarAndReportAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();

        db.RankedWarHistory.Add(new RankedWarHistoryEntity
        {
            WarId = 555,
            FactionId = ScoutedFactionId,
            FactionName = "Chain Breakers",
            OpponentFactionId = 111,
            OpponentFactionName = "Happy Gym",
            StartedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
            ReportCapturedAtUtc = DateTimeOffset.UtcNow,
            ReportIngestedAtUtc = DateTimeOffset.UtcNow,
        });

        db.RankedWarReportMembers.Add(new RankedWarReportMemberEntity
        {
            WarId = 555,
            FactionId = ScoutedFactionId,
            FactionName = "Chain Breakers",
            MemberId = 9001,
            MemberName = "Alice",
            Score = 80,
            Attacks = 8,
            CapturedAtUtc = DateTimeOffset.UtcNow,
            IngestedAtUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private void ResetScoutTables()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
        db.RankedWarReportMembers.RemoveRange(db.RankedWarReportMembers);
        db.RankedWarHistory.RemoveRange(db.RankedWarHistory);
        db.SaveChanges();
    }

    private sealed class RejectingAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RejectAll";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Fail("anonymous"));
    }
}
