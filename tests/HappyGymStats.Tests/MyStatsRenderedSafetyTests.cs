using System.IO;
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
    [Fact]
    public void No_data_state_routes_connection_setup_without_collecting_raw_key()
    {
        ConfigureServices(new TrainingMessageHandler(ResponseMode.Empty));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<MyStats>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No personal gym stats found yet.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Account &amp; Connections", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("/player-account", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Torn API Key", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("input[type=password]"));
        });
    }

    [Fact]
    public void Malformed_load_response_renders_bounded_member_safe_copy()
    {
        ConfigureServices(new TrainingMessageHandler(ResponseMode.Malformed));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<MyStats>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load your stats. Please try again.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("API response format", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Training_summary_renders_sample_date_filter_and_practical_2d_views_without_advice_claims()
    {
        ConfigureServices(new TrainingMessageHandler(ResponseMode.Dataset));
        JSInterop.Mode = JSRuntimeMode.Loose;

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
            Assert.DoesNotContain("Torn API Key", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find("select[aria-label='Stat filter']").Change("strength");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 observations in this view", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("2026-08-01 → 2026-08-15", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Source_contract_has_no_member_facing_identity_or_failure_internals()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");

        Assert.Contains("account you're signed in with", content, StringComparison.Ordinal);
        Assert.Contains("Account &amp; Connections", content, StringComparison.Ordinal);
        Assert.DoesNotContain("claim-bound", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account claims", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity map", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requested owner", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request validation failed", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API response format", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure.SafeMessage", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Torn API Key", content, StringComparison.Ordinal);
        Assert.DoesNotContain("_apiKey", content, StringComparison.Ordinal);
    }

    private void ConfigureServices(HttpMessageHandler handler)
    {
        Services.AddLogging();
        Services.AddMudServices();

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        Services.AddSingleton(new SurfacesService(http));
    }

    private sealed class TrainingMessageHandler(ResponseMode mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method != HttpMethod.Get || path != "/api/v1/torn/surfaces/me")
            {
                throw new InvalidOperationException($"Unexpected My Training request: {request.Method} {path}");
            }

            return mode switch
            {
                ResponseMode.Empty => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)),
                ResponseMode.Malformed => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{ not-valid-json", Encoding.UTF8, "application/json")
                }),
                ResponseMode.Dataset => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(MyStatsDatasetJson, Encoding.UTF8, "application/json")
                }),
                _ => throw new InvalidOperationException($"Unexpected response mode: {mode}")
            };
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HappyGymStats.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root from test output directory.");
        }

        return File.ReadAllText(Path.Combine(directory.FullName, relativePath));
    }

    private enum ResponseMode
    {
        Empty,
        Malformed,
        Dataset
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
}
