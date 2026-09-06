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
    private const string BackendImportErrorDetail = "database-provider-detail-must-not-render";

    [Fact]
    public void Failed_import_renders_bounded_member_safe_copy()
    {
        ConfigureServices(new StubMessageHandler(ResponseMode.FailedImport));

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
    public void Forbidden_import_renders_account_action_without_owner_diagnostics()
    {
        ConfigureServices(new StubMessageHandler(ResponseMode.ForbiddenImport));

        var cut = Render<MyStats>();

        cut.Find("input[type=password]").Change("safe-key");
        cut.Find("button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Import rejected. Sign in with the account you want to import.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("requested owner", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Malformed_load_response_renders_bounded_member_safe_copy()
    {
        ConfigureServices(new StubMessageHandler(ResponseMode.MalformedLoad));

        var cut = Render<MyStats>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load your stats. Please try again.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("API response format", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Missing_dataset_renders_successful_empty_state_instead_of_error()
    {
        ConfigureServices(new StubMessageHandler(ResponseMode.MissingDataset));

        var cut = Render<MyStats>();

        cut.WaitForAssertion(() =>
        {
            var state = cut.Find("[role=status]");
            Assert.Contains("No personal gym stats found yet. Import data first.", state.TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[role=alert]"));
        });
    }

    [Fact]
    public void Initial_failure_is_retryable_and_retry_can_transition_to_empty()
    {
        var handler = new StubMessageHandler(ResponseMode.FailureThenMissing);
        ConfigureServices(handler);

        var cut = Render<MyStats>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load your stats. Please try again.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
            Assert.Equal(1, handler.LoadCount);
        });

        cut.FindAll("button").Single(button => button.TextContent.Contains("Retry", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No personal gym stats found yet. Import data first.", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[role=alert]"));
            Assert.Equal(2, handler.LoadCount);
        });
    }

    [Fact]
    public void Failed_refresh_retains_last_known_good_and_marks_it_stale()
    {
        var handler = new StubMessageHandler(ResponseMode.SuccessThenRefreshFailure);
        ConfigureServices(handler);

        var cut = Render<MyStats>();
        cut.WaitForAssertion(() => Assert.Contains("1 points", cut.Markup, StringComparison.Ordinal));

        cut.Find("input[type=password]").Change("safe-key");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Import + Refresh", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Stale data.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Showing your last loaded stats", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Import completed, but your stats could not be refreshed.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Retry refresh", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("1 points", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(2, handler.LoadCount);
        });
    }

    [Fact]
    public void Source_contract_has_no_member_facing_identity_or_failure_internals()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor");

        Assert.Contains("account you're signed in with", content, StringComparison.Ordinal);
        Assert.DoesNotContain("claim-bound", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account claims", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity map", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requested owner", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request validation failed", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API response format", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure.SafeMessage", content, StringComparison.Ordinal);
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

    private sealed class StubMessageHandler(ResponseMode mode) : HttpMessageHandler
    {
        public int LoadCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/api/v1/torn/surfaces/me")
            {
                LoadCount++;

                if (mode == ResponseMode.SuccessThenRefreshFailure && LoadCount == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(SuccessDatasetJson, Encoding.UTF8, "application/json")
                    });
                }

                if (mode is ResponseMode.MalformedLoad or ResponseMode.SuccessThenRefreshFailure
                    || (mode == ResponseMode.FailureThenMissing && LoadCount == 1))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{ not-valid-json", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.Method == HttpMethod.Post && path == "/api/v1/torn/import-jobs/me")
            {
                if (mode == ResponseMode.ForbiddenImport)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                }

                if (mode == ResponseMode.SuccessThenRefreshFailure)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(SuccessImportStatusJson, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(FailedImportStatusJson, Encoding.UTF8, "application/json")
                });
            }

            throw new InvalidOperationException($"Unexpected My Stats request: {request.Method} {path}");
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
        FailedImport,
        ForbiddenImport,
        MalformedLoad,
        MissingDataset,
        FailureThenMissing,
        SuccessThenRefreshFailure
    }

    private const string SuccessDatasetJson = """
        {
          "dataset": "my-stats",
          "version": "v1",
          "series": {
            "gymCloud": { "x": [1], "y": [2], "z": [3] }
          },
          "meta": { "gymPointCount": 1, "recordCount": 1 }
        }
        """;

    private const string SuccessImportStatusJson = """
        {
          "id": "job-rendered-success",
          "outcome": "succeeded",
          "startedAtUtc": "2026-09-05T00:00:00Z",
          "completedAtUtc": "2026-09-05T00:00:01Z",
          "pagesFetched": 1,
          "logsFetched": 10,
          "logsAppended": 1,
          "errorMessage": null
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
