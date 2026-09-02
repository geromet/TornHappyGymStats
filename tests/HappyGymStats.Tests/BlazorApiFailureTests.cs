
using System.Net;
using System.Text;
using System.Text.Json;
using ApiFailure = HappyGymStats.Blazor.Services.ApiFailure;
using ApiFailureCategory = HappyGymStats.Blazor.Services.ApiFailureCategory;
using SurfacesService = HappyGymStats.Blazor.Services.SurfacesService;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class BlazorApiFailureTests
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
    public async Task GetLatest_classifies_502_as_bad_gateway()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.GetLatestAsync());

        Assert.Equal(ApiFailureCategory.BadGateway, failure.Category);
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Equal("/api/v1/torn/surfaces/latest", failure.Endpoint);
    }

    [Fact]
    public async Task GetLatest_classifies_other_5xx_as_http_failure()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.GetLatestAsync());

        Assert.Equal(ApiFailureCategory.HttpFailure, failure.Category);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Equal("The API request failed with status 500.", failure.SafeMessage);
    }

    [Fact]
    public async Task GetLatest_classifies_invalid_json_as_deserialization_failure()
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
    public async Task StartMyStatsImport_posts_to_me_endpoint_without_ownership_fields()
    {
        HttpRequestMessage? captured = null;
        using var http = CreateHttpClient(request =>
        {
            captured = request;
            return JsonResponse(HttpStatusCode.Accepted, SuccessImportStatusJson);
        });
        var sut = new SurfacesService(http);

        var status = await sut.StartMyStatsImportAsync("super-secret-api-key", fresh: true);

        Assert.NotNull(status);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/v1/torn/import-jobs/me", captured.RequestUri!.AbsolutePath);

        var body = await captured.Content!.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("super-secret-api-key", json.RootElement.GetProperty("apiKey").GetString());
        Assert.True(json.RootElement.TryGetProperty("fresh", out var freshNode));
        Assert.True(freshNode.GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("anonymousId", out _));
        Assert.False(json.RootElement.TryGetProperty("playerId", out _));
        Assert.False(json.RootElement.TryGetProperty("owner", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ApiFailureCategory.Validation)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ApiFailureCategory.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, ApiFailureCategory.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ApiFailureCategory.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiFailureCategory.IdentitySetupRequired)]
    [InlineData(HttpStatusCode.Conflict, ApiFailureCategory.IdentitySetupRequired)]
    [InlineData(HttpStatusCode.BadGateway, ApiFailureCategory.BadGateway)]
    public async Task StartMyStatsImport_classifies_http_failures_without_secret_leakage(HttpStatusCode statusCode, ApiFailureCategory expectedCategory)
    {
        const string secret = "super-secret-api-key";

        using var http = CreateHttpClient(_ => new HttpResponseMessage(statusCode));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartMyStatsImportAsync(secret));

        Assert.Equal(expectedCategory, failure.Category);
        Assert.Equal(statusCode, failure.StatusCode);
        Assert.Equal("/api/v1/torn/import-jobs/me", failure.Endpoint);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, failure.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartMyStatsImport_classifies_invalid_json_as_deserialization_without_secret_leakage()
    {
        const string secret = "super-secret-api-key";

        using var http = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"bad\":", Encoding.UTF8, "application/json")
            });
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartMyStatsImportAsync(secret, fresh: false));

        Assert.Equal(ApiFailureCategory.Deserialization, failure.Category);
        Assert.Equal("/api/v1/torn/import-jobs/me", failure.Endpoint);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartMyStatsImport_classifies_failed_import_outcome()
    {
        using var http = CreateHttpClient(_ => JsonResponse(HttpStatusCode.OK, FailedImportStatusJson));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartMyStatsImportAsync("safe-key"));

        Assert.Equal(ApiFailureCategory.ImportFailure, failure.Category);
        Assert.Equal("/api/v1/torn/import-jobs/me", failure.Endpoint);
    }

    [Fact]
    public async Task StartImport_successfully_returns_deserialized_status_for_global_path()
    {
        using var http = CreateHttpClient(_ => JsonResponse(HttpStatusCode.OK, SuccessImportStatusJson));
        var sut = new SurfacesService(http);

        var status = await sut.StartImportAsync("safe-key", fresh: true);

        Assert.NotNull(status);
        Assert.Equal("ok", status!.Outcome);
        Assert.Equal(2, status.PagesFetched);
        Assert.Equal(123L, status.LogsFetched);
    }

    [Fact]
    public async Task GetLatest_successfully_returns_deserialized_dataset()
    {
        using var http = CreateHttpClient(_ => JsonResponse(HttpStatusCode.OK, SuccessDatasetJson));
        var sut = new SurfacesService(http);

        var dataset = await sut.GetLatestAsync();

        Assert.NotNull(dataset);
        Assert.Equal("surfaces", dataset!.Dataset);
        Assert.Equal(1, dataset.Meta.GymPointCount);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubMessageHandler(responder)) { BaseAddress = new Uri("http://localhost") };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private const string SuccessImportStatusJson = """
        {
          "id": "job-123",
          "outcome": "ok",
          "startedAtUtc": "2025-01-01T00:00:00Z",
          "completedAtUtc": "2025-01-01T00:01:00Z",
          "pagesFetched": 2,
          "logsFetched": 123,
          "logsAppended": 120,
          "errorMessage": null
        }
        """;

    private const string FailedImportStatusJson = """
        {
          "id": "job-124",
          "outcome": "failed",
          "startedAtUtc": "2025-01-01T00:00:00Z",
          "completedAtUtc": "2025-01-01T00:01:00Z",
          "pagesFetched": 1,
          "logsFetched": 10,
          "logsAppended": 0,
          "errorMessage": "validation"
        }
        """;

    private const string SuccessDatasetJson = """
        {
          "dataset": "surfaces",
          "version": "v1",
          "syncedAtUtc": "2025-01-01T00:00:00Z",
          "series": {
            "gymCloud": {
              "x": [1],
              "y": [2],
              "z": [3],
              "text": ["pt"]
            },
            "eventsCloud": {
              "x": [1],
              "y": [2],
              "z": [3],
              "text": ["event"]
            }
          },
          "meta": {
            "gymPointCount": 1,
            "eventPointCount": 1,
            "recordCount": 1
          }
        }
        """;
}
