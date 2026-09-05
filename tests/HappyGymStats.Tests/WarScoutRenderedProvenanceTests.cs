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

public sealed class WarScoutRenderedProvenanceTests : BunitContext
{
    [Fact]
    public void Representative_scout_profile_renders_truthful_provenance_semantics()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);

        using var http = new HttpClient(new ScoutProfileHandler())
        {
            BaseAddress = new Uri("http://localhost")
        };

        var logger = Services.GetRequiredService<ILogger<WarScoutService>>();
        Services.AddSingleton(new WarScoutService(http, logger));

        var cut = Render<WarScout>(parameters => parameters
            .Add(component => component.FactionId, ScoutProfileHandler.FactionId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Deterministic Faction", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Example Opponent", cut.Markup, StringComparison.Ordinal);

            var warsObserved = cut.FindAll(".hgs-figure")
                .Single(element => element.TextContent.Contains("Wars observed", StringComparison.Ordinal));
            Assert.Empty(warsObserved.QuerySelectorAll(".hgs-figure-marker"));

            var winRate = cut.FindAll(".hgs-figure")
                .Single(element => element.TextContent.Contains("Win rate", StringComparison.Ordinal));
            Assert.NotNull(winRate.QuerySelector(".hgs-figure-marker-inferred"));

            var participationMarker = cut.FindAll(".hgs-figure-marker-inferred")
                .Single(element => (element.GetAttribute("aria-label") ?? string.Empty)
                    .Contains("Participation rate", StringComparison.Ordinal));
            Assert.Contains("inferred", participationMarker.TextContent, StringComparison.OrdinalIgnoreCase);

            Assert.Empty(cut.FindAll(".hgs-figure-marker-projected"));
        });
    }

    private sealed class ScoutProfileHandler : HttpMessageHandler
    {
        public const long FactionId = 123456;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/war/scout/{FactionId}", request.RequestUri?.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(CreateProfile())
            });
        }

        private static FactionScoutDto CreateProfile() => new(
            FactionId,
            "Deterministic Faction",
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
            ]);
    }
}
