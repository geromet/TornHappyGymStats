extern alias blazor;

using System.Net;
using System.Text;
using ApiFailure = blazor::HappyGymStats.Blazor.Services.ApiFailure;
using ApiFailureCategory = blazor::HappyGymStats.Blazor.Services.ApiFailureCategory;
using SurfacesService = blazor::HappyGymStats.Blazor.Services.SurfacesService;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SurfacesServiceFailureClassificationTests
{
    [Fact]
    public async Task GetLatest_returns_null_for_not_found_cache()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new SurfacesService(http);

        var result = await sut.GetLatestAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLatest_classifies_bad_gateway()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.GetLatestAsync());

        Assert.Equal(ApiFailureCategory.BadGateway, failure.Category);
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Equal("/api/v1/torn/surfaces/latest", failure.Endpoint);
        Assert.DoesNotContain("Response status code does not indicate success", failure.Message);
    }

    [Fact]
    public async Task GetLatest_classifies_deserialization_failures()
    {
        using var http = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            });
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.GetLatestAsync());

        Assert.Equal(ApiFailureCategory.Deserialization, failure.Category);
        Assert.Equal("/api/v1/torn/surfaces/latest", failure.Endpoint);
        Assert.Null(failure.StatusCode);
    }

    [Fact]
    public async Task StartImport_does_not_leak_api_key_in_failures()
    {
        const string secret = "super-secret-api-key";

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartImportAsync(secret, fresh: true));

        Assert.Equal(ApiFailureCategory.Validation, failure.Category);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubMessageHandler(responder)) { BaseAddress = new Uri("http://localhost") };

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
