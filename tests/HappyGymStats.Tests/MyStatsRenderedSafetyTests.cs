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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/api/v1/torn/surfaces/me")
            {
                if (mode == ResponseMode.MalformedLoad)
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
        MalformedLoad
    }

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
