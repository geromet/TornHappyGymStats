
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ApiFailureCategory = HappyGymStats.Blazor.Services.ApiFailureCategory;
using WarScoutService = HappyGymStats.Blazor.Services.WarScoutService;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarScoutBlazorServiceTests
{
    [Fact]
    public async Task GetProfileAsync_classifies_not_found_responses()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new WarScoutService(http, NullLogger<WarScoutService>.Instance);

        var (profile, failure) = await sut.GetProfileAsync(222);

        Assert.Null(profile);
        Assert.NotNull(failure);
        Assert.Equal(ApiFailureCategory.NotFound, failure!.Category);
    }

    [Fact]
    public async Task GetProfileAsync_classifies_transport_failures_as_api_unavailable()
    {
        using var http = CreateHttpClient(_ => throw new HttpRequestException("boom"));
        var sut = new WarScoutService(http, NullLogger<WarScoutService>.Instance);

        var (profile, failure) = await sut.GetProfileAsync(222);

        Assert.Null(profile);
        Assert.NotNull(failure);
        Assert.Equal(ApiFailureCategory.ApiUnavailable, failure!.Category);
    }

    [Fact]
    public async Task GetProfileAsync_classifies_malformed_payloads_as_deserialization_failures()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not-json }", Encoding.UTF8, "application/json")
        });
        var sut = new WarScoutService(http, NullLogger<WarScoutService>.Instance);

        var (profile, failure) = await sut.GetProfileAsync(222);

        Assert.Null(profile);
        Assert.NotNull(failure);
        Assert.Equal(ApiFailureCategory.Deserialization, failure!.Category);
    }

    [Fact]
    public async Task GetProfileAsync_returns_the_deserialized_profile_on_success()
    {
        const string body = """
        {
          "factionId": 222,
          "factionName": "Chain Breakers",
          "totalWarsObserved": 3,
          "earliestWarStartedAtUtc": "2026-01-01T00:00:00Z",
          "latestWarStartedAtUtc": "2026-01-03T00:00:00Z",
          "activeMemberCount": 1,
          "idleProneMemberCount": 0,
          "members": [
            {
              "memberId": 9001,
              "memberName": "Alice",
              "warsParticipated": 3,
              "totalAttacks": 15,
              "totalScore": 300,
              "averageScorePerAttack": 20,
              "lumpAdjustedScorePerWar": 100,
              "maxScoreInAWar": 120,
              "minScoreInAWar": 80,
              "participationRate": 1.0,
              "idleWarCount": 0,
              "idleRate": 0.0,
              "lastSeenAtUtc": "2026-01-03T00:00:00Z",
              "threatTier": "ConsistentSwinger"
            }
          ]
        }
        """;

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var sut = new WarScoutService(http, NullLogger<WarScoutService>.Instance);

        var (profile, failure) = await sut.GetProfileAsync(222);

        Assert.Null(failure);
        Assert.NotNull(profile);
        Assert.Equal(222, profile!.FactionId);
        Assert.Equal("Chain Breakers", profile.FactionName);
        var member = Assert.Single(profile.Members);
        Assert.Equal("ConsistentSwinger", member.ThreatTier);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new DelegateHandler(handler))
        {
            BaseAddress = new Uri("https://localhost:7047")
        };
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
