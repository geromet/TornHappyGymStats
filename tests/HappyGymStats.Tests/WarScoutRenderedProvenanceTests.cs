using System.Net;
using System.Net.Http.Json;
using Bunit;
using HappyGymStats.Blazor.Components.Pages;
using HappyGymStats.Blazor.Services;
using HappyGymStats.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;

namespace HappyGymStats.Tests;

public sealed class WarScoutRenderedProvenanceTests
{
    [Fact]
    public async Task Representative_scout_profile_renders_evidence_before_compact_roster_with_truthful_provenance()
    {
        await using var context = CreateContext();
        var profile = CreateProfile();
        using var http = new HttpClient(new ScoutProfileHandler(profile)) { BaseAddress = new Uri("http://localhost") };
        context.Services.AddSingleton<WarScoutService>(provider =>
            new WarScoutService(http, provider.GetRequiredService<ILogger<WarScoutService>>()));

        var cut = context.Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, profile.FactionId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Deterministic Faction", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Evidence coverage", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("42 pages", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("137 reports", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Multi-war sample", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Historical conclusions", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Threat roster", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Markup.IndexOf("Evidence coverage", StringComparison.Ordinal) < cut.Markup.IndexOf("Threat roster", StringComparison.Ordinal));
            Assert.Single(cut.FindAll("details.scout-member"));
            Assert.InRange(cut.FindAll(".scout-conclusion").Count, 4, 5);

            var warsObserved = cut.FindAll(".hgs-figure")
                .Single(element => element.TextContent.Contains("Wars observed", StringComparison.Ordinal));
            Assert.Empty(warsObserved.QuerySelectorAll(".hgs-figure-marker"));

            var winRate = cut.FindAll(".hgs-figure")
                .First(element => element.TextContent.Contains("Win rate", StringComparison.Ordinal));
            Assert.NotNull(winRate.QuerySelector(".hgs-figure-marker-inferred"));

            Assert.Empty(cut.FindAll(".hgs-figure-marker-projected"));
            Assert.DoesNotContain("lockdown", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("what you must beat", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Sparse_history_renders_raw_evidence_but_no_briefing_conclusions()
    {
        await using var context = CreateContext();
        var profile = CreateProfile() with
        {
            TotalWarsObserved = 2,
            EarliestWarStartedAtUtc = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            LatestWarStartedAtUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero)
        };
        using var http = new HttpClient(new ScoutProfileHandler(profile)) { BaseAddress = new Uri("http://localhost") };
        context.Services.AddSingleton<WarScoutService>(provider =>
            new WarScoutService(http, provider.GetRequiredService<ILogger<WarScoutService>>()));

        var cut = context.Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, profile.FactionId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sparse history", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Sparse multi-war sample", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll(".scout-conclusion"));
            Assert.Single(cut.FindAll("details.scout-member"));
        });
    }

    [Fact]
    public async Task Missing_history_is_a_successful_empty_state_not_a_failure()
    {
        await using var context = CreateContext();
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.NotFound))
        {
            BaseAddress = new Uri("http://localhost")
        };
        context.Services.AddSingleton<WarScoutService>(provider =>
            new WarScoutService(http, provider.GetRequiredService<ILogger<WarScoutService>>()));

        var cut = context.Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, 123456));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(
                "No captured ranked-war history exists yet",
                cut.Find("[role='status']").TextContent,
                StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[role='alert']"));
            Assert.DoesNotContain("Scouting report unavailable", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Initial_failure_uses_shared_alert_and_keyboard_native_retry()
    {
        await using var context = CreateContext();
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.ServiceUnavailable))
        {
            BaseAddress = new Uri("http://localhost")
        };
        context.Services.AddSingleton<WarScoutService>(provider =>
            new WarScoutService(http, provider.GetRequiredService<ILogger<WarScoutService>>()));

        var cut = context.Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, 123456));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Scouting report unavailable",
                cut.Find("[role='alert']").TextContent,
                StringComparison.Ordinal);
            var retry = cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Retry", StringComparison.Ordinal));
            Assert.Equal("button", retry.TagName.ToLowerInvariant());
            Assert.DoesNotContain("No captured ranked-war history", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Failed_route_change_never_retains_another_factions_report_as_stale()
    {
        await using var context = CreateContext();
        var profile = CreateProfile();
        using var http = new HttpClient(new SuccessThenFailureHandler(profile))
        {
            BaseAddress = new Uri("http://localhost")
        };
        context.Services.AddSingleton<WarScoutService>(provider =>
            new WarScoutService(http, provider.GetRequiredService<ILogger<WarScoutService>>()));

        var cut = context.Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, profile.FactionId));
        cut.WaitForAssertion(() => Assert.Contains(profile.FactionName, cut.Markup, StringComparison.Ordinal));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.FactionId, profile.FactionId + 1));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(profile.FactionName, cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Scouting report unavailable", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Stale data", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Failed_refresh_keeps_same_faction_report_and_marks_it_stale()
    {
        await using var context = CreateContext();
        var profile = CreateProfile();
        using var http = new HttpClient(new SuccessThenFailureHandler(profile))
        {
            BaseAddress = new Uri("http://localhost")
        };
        context.Services.AddSingleton<WarScoutService>(provider =>
            new WarScoutService(http, provider.GetRequiredService<ILogger<WarScoutService>>()));

        var cut = context.Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, profile.FactionId));
        cut.WaitForAssertion(() => Assert.Contains(profile.FactionName, cut.Markup, StringComparison.Ordinal));

        cut.Find("[data-testid='scout-refresh']").Click();

        cut.WaitForAssertion(() =>
        {
            var status = cut.Find("[role='status']");
            Assert.Contains("Stale data", status.TextContent, StringComparison.Ordinal);
            Assert.Contains("Showing the last loaded scouting report", status.TextContent, StringComparison.Ordinal);
            Assert.Contains(profile.FactionName, cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[role='alert']"));
        });
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();
        context.Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        return context;
    }

    private static FactionScoutDto CreateProfile()
        => new FactionScoutDto(
            FactionId: 123456,
            FactionName: "Deterministic Faction",
            TotalWarsObserved: 8,
            EarliestWarStartedAtUtc: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            LatestWarStartedAtUtc: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            ActiveMemberCount: 24,
            IdleProneMemberCount: 3,
            MedianScorePerAttack: 6.5m,
            WinRate: 0.625m,
            WarsWithKnownOutcome: 8,
            TypicalTargetScore: 4200,
            PointsPerHour: 815.25m,
            TypicalRosterSize: 27,
            Top5ScoreShare: 0.48m,
            Top10ScoreShare: 0.72m,
            Members:
            [
                new OpponentMemberProfileDto(
                    MemberId: 987654,
                    MemberName: "Example Opponent",
                    WarsParticipated: 7,
                    TotalAttacks: 91,
                    TotalScore: 612,
                    AverageScorePerAttack: 6.73m,
                    LumpAdjustedScorePerAttack: 6.41m,
                    RawMedianScorePerWar: 88m,
                    LumpAdjustedScorePerWar: 84m,
                    LumpWarCount: 2,
                    MaxScoreInAWar: 120,
                    MinScoreInAWar: 55,
                    ParticipationRate: 0.875m,
                    IdleWarCount: 1,
                    IdleRate: 0.125m,
                    LastSeenAtUtc: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
                    ThreatTier: "ConsistentSwinger")
            ])
        {
            Evidence = new WarScoutEvidenceDto(
                BackfillStatus: "Completed",
                PagesProcessed: 42,
                ReportsProcessed: 137,
                UpdatedAtUtc: new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                LastSuccessAtUtc: new DateTimeOffset(2026, 9, 2, 11, 58, 0, TimeSpan.Zero),
                IsComplete: true)
        };

    private sealed class ScoutProfileHandler(FactionScoutDto profile) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/war/scout/{profile.FactionId}", request.RequestUri?.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(profile)
            });
        }
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class SuccessThenFailureHandler(FactionScoutDto profile) : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests++;
            return Task.FromResult(_requests == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(profile) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
