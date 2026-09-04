using System.Net;
using System.Text;
using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class FfScouterCombatIntelProviderTests
{
    private const string Secret = "abc123def456ghij";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_757_000_000);

    [Fact]
    public async Task FetchAsync_maps_direct_spy_totals_and_drops_unbounded_public_estimates()
    {
        const string fixture = """
            [
              { "player_id": 10, "bs_estimate": 123456789, "last_updated": 1756999900, "source": "premium" },
              { "player_id": 20, "bs_estimate": 987654321, "last_updated": 1756999800, "source": "spies" },
              { "player_id": 30, "bs_estimate": 555555, "last_updated": 1756999700, "source": "bss" },
              { "player_id": 40, "bs_estimate": null, "last_updated": null, "source": "bss" }
            ]
            """;
        var handler = new RecordingHandler(_ => JsonResponse(fixture));
        var provider = CreateProvider(handler);

        var result = await provider.FetchAsync([10, 20, 30, 40]);

        Assert.Equal(CombatIntelProviderFetchStatus.Partial, result.Status);
        Assert.Equal([10L, 20L], result.Observations.Select(x => x.PlayerId).ToArray());
        Assert.Equal([30L, 40L], result.MissingPlayerIds);
        Assert.All(result.Observations, observation =>
        {
            Assert.Equal("ffscouter", observation.Provider);
            Assert.Equal(CombatIntelClassification.Exact, observation.Classification);
            Assert.DoesNotContain(Secret, observation.ObservationId, StringComparison.Ordinal);
            Assert.DoesNotContain(Secret, observation.ProviderMetadata ?? string.Empty, StringComparison.Ordinal);
            Assert.True(observation.ObservedAtUtc <= observation.FetchedAtUtc);
        });
        Assert.Equal(123456789m, result.Observations[0].Value);
        Assert.Equal(987654321m, result.Observations[1].Value);
    }

    [Fact]
    public async Task FetchAsync_partial_payload_never_coerces_missing_or_invalid_values_to_zero()
    {
        const string fixture = """
            [
              { "player_id": 10, "bs_estimate": -1, "last_updated": 1756999900, "source": "spies" },
              { "player_id": 20, "last_updated": 1756999800, "source": "premium" },
              { "player_id": 30, "bs_estimate": 100, "last_updated": 1758000000, "source": "spies" }
            ]
            """;
        var provider = CreateProvider(new RecordingHandler(_ => JsonResponse(fixture)));

        var result = await provider.FetchAsync([10, 20, 30]);

        Assert.Equal(CombatIntelProviderFetchStatus.Partial, result.Status);
        Assert.Empty(result.Observations);
        Assert.Equal([10L, 20L, 30L], result.MissingPlayerIds);
    }

    [Fact]
    public async Task FetchAsync_malformed_payload_returns_secret_safe_unavailable_result()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{ definitely-not-json"));
        var provider = CreateProvider(handler);

        var result = await provider.FetchAsync([42]);

        Assert.Equal(CombatIntelProviderFetchStatus.Unavailable, result.Status);
        Assert.Empty(result.Observations);
        Assert.Equal([42L], result.MissingPlayerIds);
        Assert.Equal("ffscouter_unavailable", result.FailureCode);
        Assert.DoesNotContain(Secret, result.FailureCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_provider_error_does_not_fabricate_observations()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var provider = CreateProvider(handler);

        var result = await provider.FetchAsync([42, 43]);

        Assert.Equal(CombatIntelProviderFetchStatus.Unavailable, result.Status);
        Assert.Empty(result.Observations);
        Assert.Equal([42L, 43L], result.MissingPlayerIds);
    }

    [Fact]
    public async Task FetchAsync_caches_by_target_set_without_exposing_credential()
    {
        const string fixture = """[{ "player_id": 42, "bs_estimate": 5000, "last_updated": 1756999900, "source": "spies" }]""";
        var handler = new RecordingHandler(_ => JsonResponse(fixture));
        var provider = CreateProvider(handler);

        var first = await provider.FetchAsync([42]);
        var second = await provider.FetchAsync([42, 42]);

        Assert.Equal(1, handler.RequestCount);
        Assert.Same(first, second);
        Assert.DoesNotContain(Secret, first.Observations.Single().ObservationId, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, first.Observations.Single().ProviderMetadata ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_batches_at_documented_provider_limit()
    {
        var handler = new RecordingHandler(request =>
        {
            var query = request.RequestUri!.Query;
            var targetsValue = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Single(part => part.StartsWith("targets=", StringComparison.Ordinal))
                .Split('=', 2)[1];
            var targets = Uri.UnescapeDataString(targetsValue).Split(',').Select(long.Parse).ToArray();
            var rows = string.Join(',', targets.Select(id =>
                $$"""{"player_id":{{id}},"bs_estimate":{{id * 100}},"last_updated":1756999900,"source":"spies"}"""));
            return JsonResponse($"[{rows}]");
        });
        var provider = CreateProvider(handler, minimumRequestInterval: TimeSpan.Zero);
        var ids = Enumerable.Range(1, FfScouterCombatIntelProvider.MaxTargetsPerRequest + 1).Select(x => (long)x).ToArray();

        var result = await provider.FetchAsync(ids);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal(CombatIntelProviderFetchStatus.Available, result.Status);
        Assert.Equal(ids.Length, result.Observations.Count);
        Assert.All(handler.TargetCounts, count => Assert.InRange(count, 1, FfScouterCombatIntelProvider.MaxTargetsPerRequest));
    }

    private static FfScouterCombatIntelProvider CreateProvider(
        RecordingHandler handler,
        TimeSpan? minimumRequestInterval = null)
    {
        return new FfScouterCombatIntelProvider(
            new HttpClient(handler),
            Secret,
            new Uri("https://ffscouter.test/"),
            new FixedTimeProvider(Now),
            minimumRequestInterval: minimumRequestInterval ?? TimeSpan.Zero);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public List<int> TargetCounts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var targets = request.RequestUri!.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part => part.StartsWith("targets=", StringComparison.Ordinal));
            if (targets is not null)
            {
                var value = Uri.UnescapeDataString(targets.Split('=', 2)[1]);
                TargetCounts.Add(value.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
            }
            return Task.FromResult(responder(request));
        }
    }
}
