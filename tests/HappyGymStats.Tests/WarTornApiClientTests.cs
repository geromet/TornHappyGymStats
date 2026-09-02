using System.Net;
using System.Text;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using Xunit;

namespace HappyGymStats.Tests;

public sealed class WarTornApiClientTests
{
    private const string ApiKey = "limited-key-123";

    [Fact]
    public async Task GetLiveFactionWarsAsync_uses_expected_endpoint_and_deserializes_fixture()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/faction/?selections=rankedwars&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/live-faction-wars.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetLiveFactionWarsAsync(ApiKey);

        Assert.Equal(2, payload.Wars.Count);
        Assert.Null(payload.Wars[0].End);
        Assert.Equal(1730907200, payload.Wars[1].End?.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task GetRankedWarReportAsync_uses_expected_endpoint_and_deserializes_fixture()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/torn/48377?selections=rankedwarreport&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/ranked-war-report-48377.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetRankedWarReportAsync(ApiKey, 48377);

        Assert.Equal(48377, payload.War.WarId);
        Assert.Null(payload.War.End);
        Assert.Equal(2, payload.Factions.Count);
        Assert.Equal("Alice", payload.Factions[0].Members[0].Name);
    }

    [Fact]
    public async Task GetGlobalRankedWarsAsync_normalizes_zero_and_null_live_end_values()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/torn/?selections=rankedwars&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/global-ranked-wars-live.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetGlobalRankedWarsAsync(ApiKey);

        Assert.Equal(3, payload.Wars.Count);
        Assert.Null(payload.Wars[0].End);
        Assert.Null(payload.Wars[1].End);
        Assert.NotNull(payload.Wars[2].End);
    }

    [Fact]
    public async Task GetUserAttacksPageAsync_uses_default_endpoint_and_deserializes_fixture()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/user/?selections=attacks&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/user-attacks-page.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetUserAttacksPageAsync(ApiKey);

        Assert.Equal(3, payload.Attacks.Count);
        Assert.Equal("/user/?selections=attacks&page=2", payload.Metadata?.Links?.Next);
        Assert.Null(payload.Attacks[2].WarId);
    }

    [Fact]
    public async Task GetUserAttacksPageAsync_relative_url_strips_existing_key_before_injecting_supplied_key()
    {
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/user/?selections=attacks&page=2&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(ReadFixture("tests/fixtures/war/user-attacks-page.json")));
        });

        var sut = CreateClient(handler);

        var payload = await sut.GetUserAttacksPageAsync(ApiKey, new Uri("/user/?selections=attacks&page=2&key=caller-provided", UriKind.Relative));

        Assert.Equal(3, payload.Attacks.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_status_retryables_surface_retryable_torn_api_exception(HttpStatusCode statusCode)
    {
        var sut = CreateClient(new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            })));

        var ex = await Assert.ThrowsAsync<TornApiException>(() => sut.GetGlobalRankedWarsAsync(ApiKey));

        Assert.True(ex.IsRetryable);
        Assert.Equal(statusCode, ex.StatusCode);
        Assert.Null(ex.TornErrorCode);
        Assert.DoesNotContain(ApiKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Torn_code_5_surfaces_retryable_redacted_exception()
    {
        const string response = """
        {
          "error": {
            "code": 5,
            "error": "Rate limit hit for https://api.torn.com/torn/?selections=rankedwars&key=caller-secret"
          }
        }
        """;

        var sut = CreateClient(new RecordingHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(response))));

        var ex = await Assert.ThrowsAsync<TornApiException>(() => sut.GetGlobalRankedWarsAsync(ApiKey));

        Assert.True(ex.IsRetryable);
        Assert.Equal(5, ex.TornErrorCode);
        Assert.DoesNotContain(ApiKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("https://api.torn.com", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted-url]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auth_or_permission_torn_errors_are_non_retryable()
    {
        const string response = """
        {
          "error": {
            "code": 2,
            "error": "Incorrect key"
          }
        }
        """;

        var sut = CreateClient(new RecordingHttpMessageHandler((_, _) => Task.FromResult(JsonResponse(response))));

        var ex = await Assert.ThrowsAsync<TornApiException>(() => sut.GetLiveFactionWarsAsync(ApiKey));

        Assert.False(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Equal(2, ex.TornErrorCode);
    }

    [Fact]
    public async Task Malformed_json_surfaces_safe_non_retryable_exception_for_success_response()
    {
        var sut = CreateClient(new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{ not-json"))));

        var ex = await Assert.ThrowsAsync<TornApiException>(() => sut.GetRankedWarReportAsync(ApiKey, 48377));

        Assert.False(ex.IsRetryable);
        Assert.Equal(HttpStatusCode.OK, ex.StatusCode);
        Assert.Null(ex.TornErrorCode);
        Assert.DoesNotContain(ApiKey, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("https://api.torn.com", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_is_not_wrapped_when_caller_token_is_cancelled()
    {
        var handler = new RecordingHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });

        var sut = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        var pending = sut.GetUserAttacksPageAsync(ApiKey, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task Existing_user_log_wrapper_still_parses_logs_via_shared_fetch_path()
    {
        const string response = """
        {
          "logs": {
            "1": {
              "id": 1,
              "timestamp": 1731000200,
              "details": {
                "title": "Attack",
                "category": "Combat",
                "id": 77
              }
            }
          },
          "_metadata": {
            "links": {
              "next": "https://api.torn.com/user/?selections=log&from=2"
            }
          }
        }
        """;

        var sut = CreateClient(new RecordingHttpMessageHandler((request, _) =>
        {
            Assert.Equal("https://api.torn.com/user/?selections=log&key=limited-key-123", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(JsonResponse(response));
        }));

        var page = await sut.GetUserLogPageAsync(ApiKey, new Uri("https://api.torn.com/user/?selections=log"));

        Assert.Single(page.Logs);
        Assert.Equal("Attack", page.Logs[0].Title);
        Assert.Equal("Combat", page.Logs[0].Category);
        Assert.Equal(77, page.Logs[0].LogTypeId);
        Assert.Equal("https://api.torn.com/user/?selections=log&from=2", page.NextUrl?.AbsoluteUri);
    }

    private static TornApiClient CreateClient(HttpMessageHandler handler)
        => new(new HttpClient(handler));

    private static HttpResponseMessage JsonResponse(string content, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string ReadFixture(string relativePath)
    {
        var root = ResolveRepositoryRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string ResolveRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(current, "HappyGymStats.slnx"))
                || File.Exists(Path.Combine(current, "HappyGymStats.sln")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
