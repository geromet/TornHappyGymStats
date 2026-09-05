using System.Net;
using System.Text;
using Bunit;
using HappyGymStats.Blazor.Components.Pages;
using HappyGymStats.Blazor.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class MyStatsRenderedSafetyTests : BunitContext
{
    private const string BackendImportErrorDetail = "database-provider-detail-must-not-render";

    [Fact]
    public void Failed_import_renders_bounded_member_safe_copy()
    {
        Services.AddLogging();
        Services.AddMudServices();

        using var http = new HttpClient(new StubMessageHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        Services.AddSingleton(new SurfacesService(http));

        var cut = Render<MyStats>();

        cut.Find("input[type=password]").Change("safe-key");
        cut.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Import failed. Please try again.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain(BackendImportErrorDetail, cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Training_summary_renders_sample_date_filter_and_practical_2d_views_without_advice_claims()
    {
        Services.AddLogging();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        using var http = new HttpClient(new DatasetMessageHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        Services.AddSingleton(new SurfacesService(http));

        var cut = Render<MyStats>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("My Training", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Gain / energy over time", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Gain / energy vs stat before", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Gain / energy vs happiness", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("2026-08-01 → 2026-09-01", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("3 observations in this view", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("best", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("optimal", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("scatter3d", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });

        cut.Find("select[aria-label='Stat filter']").Change("strength");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 observations in this view", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("2026-08-01 → 2026-08-15", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/api/v1/torn/surfaces/me")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/torn/import-jobs/me")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(FailedImportStatusJson, Encoding.UTF8, "application/json")
                });
            }

            throw new InvalidOperationException($"Unexpected My Stats request: {request.Method} {path}");
        }
    }

    private sealed class DatasetMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/v1/torn/surfaces/me")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(MyStatsDatasetJson, Encoding.UTF8, "application/json")
                });
            }

            throw new InvalidOperationException($"Unexpected My Training request: {request.Method} {path}");
        }
    }

    private const string MyStatsDatasetJson = """
        {
          "dataset": "my-stats",
          "version": "sample-v1",
          "series": {
            "gymCloud": {
              "x": [1000000, 1500000, 2000000],
              "y": [1000, 1500, 2000],
              "z": [1.1, 1.3, 1.6],
              "text": [
                "strength 2026-08-01T12:00:00.0000000+00:00",
                "strength 2026-08-15T12:00:00.0000000+00:00",
                "speed 2026-09-01T12:00:00.0000000+00:00"
              ]
            }
          },
          "meta": {
            "gymPointCount": 3,
            "recordCount": 3
          }
        }
        """;

    private const string FailedImportStatusJson = """
        {
          "id": "job-rendered-proof",
          "outcome": "failed",
          "startedAtUtc": "2026-09-05T00:00:00Z",
          "completedAtUtc": "2026-09-05T00:00:01Z",
          "pagesFetched": 1,
          "logsFetched": 10,
          "logsAppended": 0,
          "errorMessage": "database-provider-detail-must-not-render"
        }
        """;
}
