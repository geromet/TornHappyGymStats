using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using HappyGymStats.Blazor.Components.Pages;
using HappyGymStats.Blazor.Services;
using HappyGymStats.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class SharedStateRenderedTests : BunitContext
{
    [Fact]
    public void Home_renders_successful_empty_state_when_no_surface_dataset_exists()
    {
        ConfigureHome(new HomeMessageHandler(HttpStatusCode.NotFound));

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No surfaces data found. Run an import first.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Could not load surfaces data", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Home_renders_failure_state_when_surface_api_is_unavailable()
    {
        ConfigureHome(new HomeMessageHandler(HttpStatusCode.ServiceUnavailable));

        var cut = Render<Home>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Could not load surfaces data. The service is temporarily unavailable", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("No surfaces data found", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void War_renders_loading_state_while_initial_state_is_pending()
    {
        var board = CreateWarBoard();
        SetProperty(board, nameof(WarBoardService.IsLoading), true);
        ConfigureWar(board);

        var cut = Render<War>();

        Assert.Contains("Loading current war state", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No war in progress", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void War_renders_failure_state_instead_of_empty_state()
    {
        var board = CreateWarBoard();
        SetProperty(
            board,
            nameof(WarBoardService.CurrentFailure),
            new ApiFailure(
                "/api/v1/war/current",
                ApiFailureCategory.ApiUnavailable,
                "The war board could not reach the API service.",
                HttpStatusCode.ServiceUnavailable));
        ConfigureWar(board);

        var cut = Render<War>();

        Assert.Contains("War board unavailable. Refresh to retry.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No war in progress", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void War_renders_no_active_war_as_successful_empty_state()
    {
        var board = CreateWarBoard();
        SetProperty(board, nameof(WarBoardService.CurrentState), CreateWarState(WarStatus.NotReady, isReady: false, isStale: false));
        ConfigureWar(board);

        var cut = Render<War>();

        Assert.Contains("No war in progress. The board fills in automatically", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("War board unavailable", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Stale data", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void War_renders_stale_banner_for_active_stale_state()
    {
        var board = CreateWarBoard();
        SetProperty(board, nameof(WarBoardService.CurrentState), CreateWarState(WarStatus.Ok, isReady: true, isStale: true));
        ConfigureWar(board);

        var cut = Render<War>();

        Assert.Contains("Stale data", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Review heartbeat, warnings, and hub connection status before acting on roster gaps.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No war in progress", cut.Markup, StringComparison.Ordinal);
    }

    private void ConfigureHome(HttpMessageHandler handler)
    {
        Services.AddLogging();
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton(new SurfacesService(http));
        Services.AddSingleton(new UiSettingsService(http, NullLogger<UiSettingsService>.Instance));
    }

    private void ConfigureWar(WarBoardService board)
    {
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddSingleton(board);
    }

    private static WarBoardService CreateWarBoard()
    {
        var http = new HttpClient(new RejectingMessageHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };
        var board = new WarBoardService(http, new NullAccessTokenProvider(), NullLogger<WarBoardService>.Instance);
        SetField(board, "initialized", true);
        return board;
    }

    private static WarStateDto CreateWarState(string status, bool isReady, bool isStale) => new(
        Status: status,
        IsReady: isReady,
        WarId: status == WarStatus.NotReady ? null : 1234,
        AsOfUtc: new DateTimeOffset(2026, 9, 5, 1, 0, 0, TimeSpan.Zero),
        HasRoster: false,
        FactionCount: 0,
        MemberCount: 0,
        CoverageRatio: 0m,
        OpenTargetCount: 0,
        HoleCount: 0,
        Heartbeat: new WarHeartbeatDto(
            Phase: "polling",
            UpdatedAtUtc: new DateTimeOffset(2026, 9, 5, 1, 0, 0, TimeSpan.Zero),
            PollStartedAtUtc: null,
            PollCompletedAtUtc: null,
            StaleAfterUtc: null,
            IsStale: isStale,
            LastError: null),
        Warnings: Array.Empty<string>(),
        Errors: Array.Empty<string>(),
        Factions: Array.Empty<WarFactionDto>(),
        Holes: Array.Empty<WarHoleDto>());

    private static void SetProperty<T>(WarBoardService board, string name, T value)
    {
        var property = typeof(WarBoardService).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Could not find WarBoardService.{name}.");
        property.SetValue(board, value);
    }

    private static void SetField<T>(WarBoardService board, string name, T value)
    {
        var field = typeof(WarBoardService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find WarBoardService field {name}.");
        field.SetValue(board, value);
    }

    private sealed class NullAccessTokenProvider : IServerAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>(null);
    }

    private sealed class RejectingMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"War rendered-state proof unexpectedly made an HTTP request: {request.Method} {request.RequestUri}");
    }

    private sealed class HomeMessageHandler(HttpStatusCode surfacesStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/v1/ui-settings")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"gymPointCloudEnabled\":false}", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Get && path == "/api/v1/torn/surfaces/latest")
            {
                return Task.FromResult(new HttpResponseMessage(surfacesStatus));
            }

            throw new InvalidOperationException($"Unexpected Home rendered-state request: {request.Method} {path}");
        }
    }
}
