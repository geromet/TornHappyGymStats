using System.Net;
using System.Text;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarHistoryTornApiClientTests
{
    private const string ApiKey = "limited-key-123";

    [Fact]
    public async Task GetRankedWarHistoryPageAsync_uses_expected_v2_endpoint_and_deserializes_fixture()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.torn.com/v2/faction/warfareranked?selections=warfareranked&key=limited-key-123",
                request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/v2-warfareranked-page.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetRankedWarHistoryPageAsync(ApiKey);

        Assert.Equal(2, payload.Wars.Count);

        var liveWar = payload.Wars[0];
        Assert.Equal(48377, liveWar.WarId);
        Assert.Equal(111, liveWar.FactionId);
        Assert.Equal("Happy Gym", liveWar.FactionName);
        Assert.Equal(222, liveWar.OpponentId);
        Assert.Equal("Chain Breakers", liveWar.OpponentName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731000000), liveWar.Start);
        Assert.Null(liveWar.End);
        Assert.Null(liveWar.WinnerFactionId);
        Assert.Equal(128, liveWar.Score);
        Assert.Equal(42, liveWar.Chain);
        Assert.Equal(111, liveWar.OpponentScore);
        Assert.Equal(33, liveWar.OpponentChain);
        Assert.Null(liveWar.Status);

        Assert.NotNull(payload.Metadata);
        Assert.Equal(1, payload.Metadata!.Page);
        Assert.Equal(2, payload.Metadata.PerPage);
        Assert.Equal("cursor-2", payload.Metadata.NextCursor);
        Assert.True(payload.Metadata.HasMore);
        Assert.Equal("/v2/faction/warfareranked?selections=warfareranked&page=2", payload.Metadata.Links?.Next);
        Assert.Null(payload.Metadata.Links?.Prev);
    }

    [Fact]
    public async Task GetRankedWarHistoryPageAsync_normalizes_relative_next_url()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal(
                "https://api.torn.com/v2/faction/warfareranked?selections=warfareranked&page=2&key=limited-key-123",
                request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/v2-warfareranked-page.json")));
        });

        var sut = CreateClient(handler);

        await sut.GetRankedWarHistoryPageAsync(ApiKey, new Uri("/v2/faction/warfareranked?selections=warfareranked&page=2", UriKind.Relative));
    }

    [Fact]
    public async Task GetRankedWarReportAsync_uses_expected_endpoint_and_deserializes_history_fixture()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/torn/48377?selections=rankedwarreport&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/v2-ranked-war-report-48377.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetRankedWarReportAsync(ApiKey, 48377);

        Assert.Equal(48377, payload.War.WarId);
        Assert.Equal("Happy Gym vs Chain Breakers", payload.War.Name);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731000000), payload.War.Start);
        Assert.Null(payload.War.End);
        Assert.True(payload.War.IsLive);
        Assert.Null(payload.War.WinnerFactionId);
        Assert.Null(payload.War.Status);

        Assert.Equal(2, payload.Factions.Count);
        var home = payload.Factions[0];
        Assert.Equal(111, home.FactionId);
        Assert.Equal("Happy Gym", home.Name);
        Assert.Equal(128, home.Score);
        Assert.Equal(42, home.Chain);
        Assert.Equal(18, home.Attacks);
        Assert.Equal(3, home.Members.Count);
        Assert.Null(home.Members[1].Status?.Until);
        Assert.Null(home.Members[2].Status);

        Assert.Equal(new long[] { 1003, 2002 }, payload.IdleAttackers);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetRankedWarReportAsync_requires_positive_war_id(long warId)
    {
        var sut = CreateClient(new RecordingHttpMessageHandler((_, _) => throw new InvalidOperationException("Should not send request.")));

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.GetRankedWarReportAsync(ApiKey, warId));

        Assert.Equal("warId", ex.ParamName);
    }

    [Fact]
    public void History_and_report_fixtures_tolerate_unknown_fields_and_nullable_timestamps()
    {
        var history = DeserializeFixture<RankedWarHistoryPageResponse>("tests/fixtures/war/v2-warfareranked-page.json");
        var report = DeserializeFixture<RankedWarReportResponse>("tests/fixtures/war/v2-ranked-war-report-48377.json");

        Assert.Null(history.Wars[0].End);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1730807200), history.Wars[1].End);
        Assert.Null(report.War.End);
        Assert.Null(report.War.Status);
        Assert.Null(report.Factions[0].Members[1].Status?.Until);
        Assert.Null(report.Factions[0].Members[2].Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1731007200), report.Factions[1].Members[1].Status?.Until);
    }

    private static TornApiClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler));

    private static string ReadFixture(string relativePath)
        => File.ReadAllText(Path.Combine(ResolveRepositoryRoot(), relativePath), Encoding.UTF8);

    private static T DeserializeFixture<T>(string relativePath)
        => System.Text.Json.JsonSerializer.Deserialize<T>(ReadFixture(relativePath), WarEndpointJson.SerializerOptions)
           ?? throw new InvalidOperationException($"Fixture {relativePath} deserialized to null.");

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ResolveRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "HappyGymStats.sln")) || File.Exists(Path.Combine(current, "HappyGymStats.slnx")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responder(request, cancellationToken);
    }
}
