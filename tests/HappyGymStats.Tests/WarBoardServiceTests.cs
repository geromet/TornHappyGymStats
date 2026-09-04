
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ApiFailureCategory = HappyGymStats.Blazor.Services.ApiFailureCategory;
using IServerAccessTokenProvider = HappyGymStats.Blazor.Services.IServerAccessTokenProvider;
using WarBoardService = HappyGymStats.Blazor.Services.WarBoardService;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarBoardServiceTests
{
    [Fact]
    public async Task Refresh_classifies_service_unavailable_bootstrap_failures()
    {
        using var http = CreateHttpClient(_ => throw new HttpRequestException("boom"));
        await using var sut = new WarBoardService(http, new TestAccessTokenProvider(), NullLogger<WarBoardService>.Instance);

        await sut.RefreshAsync();

        Assert.NotNull(sut.CurrentFailure);
        Assert.Equal(ApiFailureCategory.ApiUnavailable, sut.CurrentFailure!.Category);
        Assert.Null(sut.CurrentState);
    }

    [Fact]
    public async Task Refresh_classifies_unauthorized_bootstrap_responses()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await using var sut = new WarBoardService(http, new TestAccessTokenProvider(), NullLogger<WarBoardService>.Instance);

        await sut.RefreshAsync();

        Assert.NotNull(sut.CurrentFailure);
        Assert.Equal(ApiFailureCategory.Unauthorized, sut.CurrentFailure!.Category);
    }

    [Fact]
    public async Task Refresh_classifies_malformed_bootstrap_payloads()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not-json }", Encoding.UTF8, "application/json")
        });
        await using var sut = new WarBoardService(http, new TestAccessTokenProvider(), NullLogger<WarBoardService>.Instance);

        await sut.RefreshAsync();

        Assert.NotNull(sut.CurrentFailure);
        Assert.Equal(ApiFailureCategory.Deserialization, sut.CurrentFailure!.Category);
        Assert.Null(sut.CurrentState);
    }

    [Fact]
    public async Task No_active_war_is_a_state_not_a_failure_and_is_not_stale()
    {
        // A 200 carrying status "not-ready" is the API saying there is no war on.
        // Two things must be true of it, and neither was: it is not a failure,
        // and it is not stale data. HasStaleData tested `IsReady == false`, which
        // covers "no war" as well as "degraded", so the board raised "Stale data.
        // Review heartbeat, warnings, and hub connection status before acting on
        // roster gaps" over a board with no heartbeat and no roster.
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(NoActiveWarPayload, Encoding.UTF8, "application/json")
        });
        await using var sut = new WarBoardService(http, new TestAccessTokenProvider(), NullLogger<WarBoardService>.Instance);

        await sut.RefreshAsync();

        Assert.Null(sut.CurrentFailure);
        Assert.NotNull(sut.CurrentState);
        Assert.True(sut.HasNoActiveWar);
        Assert.False(sut.HasStaleData);
        Assert.False(sut.HasError);
    }

    private const string NoActiveWarPayload = """
        {
          "status": "not-ready",
          "isReady": false,
          "warId": null,
          "asOfUtc": "2026-09-04T00:00:00+00:00",
          "hasRoster": false,
          "factionCount": 0,
          "memberCount": 0,
          "coverageRatio": 0,
          "openTargetCount": 0,
          "holeCount": 0,
          "heartbeat": { "phase": "idle", "isStale": false },
          "warnings": [],
          "errors": [],
          "factions": [],
          "holes": []
        }
        """;

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

internal sealed class TestAccessTokenProvider(string? accessToken = null) : IServerAccessTokenProvider
{
    public Task<string?> GetAccessTokenAsync() => Task.FromResult(accessToken);
}
