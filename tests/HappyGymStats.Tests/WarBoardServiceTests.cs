extern alias blazor;

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ApiFailureCategory = blazor::HappyGymStats.Blazor.Services.ApiFailureCategory;
using IServerAccessTokenProvider = blazor::HappyGymStats.Blazor.Services.IServerAccessTokenProvider;
using WarBoardService = blazor::HappyGymStats.Blazor.Services.WarBoardService;
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
