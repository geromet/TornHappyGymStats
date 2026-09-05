using System;
using System.IO;
using System.Net;
using System.Text;
using Bunit;
using HappyGymStats.Blazor.Components.Pages;
using HappyGymStats.Blazor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace HappyGymStats.Tests;

public sealed class HomeMemberSafetyTests : BunitContext
{
    [Fact]
    public void Home_does_not_render_implementation_or_raw_error_detail_to_members()
    {
        var content = ReadRepoFile(
            "src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor");

        Assert.Contains("Could not load training data. Please try again.", content, StringComparison.Ordinal);
        Assert.Contains("Import failed. Please try again.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("status.ErrorMessage", content, StringComparison.Ordinal);
        Assert.DoesNotContain("backend rejected", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("API response format", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure.SafeMessage", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Malformed_load_response_renders_bounded_member_safe_copy()
    {
        Services.AddLogging();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var http = new HttpClient(new MalformedLoadHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        Services.AddSingleton(new SurfacesService(http));
        Services.AddSingleton(sp => new UiSettingsService(
            http,
            sp.GetRequiredService<ILogger<UiSettingsService>>()));

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load training data. Please try again.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("API response format", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed class MalformedLoadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;

            if (request.Method == HttpMethod.Get && path == "/api/v1/ui-settings")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.Method == HttpMethod.Get && path == "/api/v1/torn/surfaces/latest")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{ not-valid-json", Encoding.UTF8, "application/json")
                });
            }

            throw new InvalidOperationException($"Unexpected Home request: {request.Method} {path}");
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
}
