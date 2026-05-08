extern alias blazor;

using System.Net;
using System.Text;
using ApiFailure = blazor::HappyGymStats.Blazor.Services.ApiFailure;
using ApiFailureCategory = blazor::HappyGymStats.Blazor.Services.ApiFailureCategory;
using ImportStatusDto = blazor::HappyGymStats.Blazor.Models.ImportStatusDto;
using SurfacesService = blazor::HappyGymStats.Blazor.Services.SurfacesService;
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
    public async Task StartImport_classifies_400_as_validation_without_secret_leakage()
    {
        const string secret = "super-secret-api-key";

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartImportAsync(secret, fresh: true));

        Assert.Equal(ApiFailureCategory.Validation, failure.Category);
        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartImport_classifies_422_as_validation_without_secret_leakage()
    {
        const string secret = "super-secret-api-key";

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartImportAsync(secret, fresh: true));

        Assert.Equal(ApiFailureCategory.Validation, failure.Category);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failure.StatusCode);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartImport_successfully_returns_deserialized_status()
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
    public async Task StartImport_classifies_invalid_json_as_deserialization_without_secret_leakage()
    {
        const string secret = "super-secret-api-key";

        using var http = CreateHttpClient(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"bad\":", Encoding.UTF8, "application/json")
            });
        var sut = new SurfacesService(http);

        var failure = await Assert.ThrowsAsync<ApiFailure>(() => sut.StartImportAsync(secret, fresh: false));

        Assert.Equal(ApiFailureCategory.Deserialization, failure.Category);
        Assert.Equal("/api/v1/torn/import-jobs", failure.Endpoint);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
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
        Assert.Equal(1, dataset.Meta.ProvenanceWarningsDiagnostics.WarningCount);
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
              "text": ["pt"],
              "confidence": [0.9],
              "confidenceReasons": [["reason"]],
              "provenanceWarnings": [
                {
                  "logId": "log-1",
                  "scope": "record",
                  "verificationStatus": "warn",
                  "linkTarget": "record:1",
                  "rowCount": 1,
                  "hasManualOverride": false,
                  "manualOverrideSource": null
                }
              ]
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
            "recordCount": 1,
            "provenanceWarningsDiagnostics": {
              "warningCount": 1,
              "skippedMalformedRowCount": 0,
              "overrideLoadedEntryCount": 0,
              "overrideSkippedMalformedEntryCount": 0,
              "overrideHitEntryCap": false,
              "queryFailed": false,
              "reason": "ok"
            }
          }
        }
        """;
}
