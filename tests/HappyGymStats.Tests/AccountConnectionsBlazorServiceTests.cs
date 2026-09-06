using System.Net;
using System.Text;
using HappyGymStats.Blazor.Services;

namespace HappyGymStats.Tests;

public sealed class AccountConnectionsBlazorServiceTests
{
    [Fact]
    public async Task GetAsync_returns_safe_status_contract()
    {
        const string body = """
        {
          "state": "connected",
          "tornPlayerId": 123456,
          "storedAtUtc": "2026-09-05T20:00:00Z",
          "consent": {
            "documentVersion": "v2",
            "purpose": "Personal Torn data",
            "acceptedAtUtc": "2026-09-05T19:00:00Z"
          }
        }
        """;

        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var sut = new AccountConnectionsService(http);

        var status = await sut.GetAsync();

        Assert.Equal("connected", status.State);
        Assert.Equal(123456, status.TornPlayerId);
        Assert.Equal("v2", status.Consent?.DocumentVersion);
    }

    [Fact]
    public async Task ConnectAsync_keeps_api_key_out_of_uri_and_sends_it_only_in_post_body()
    {
        const string secret = "super-secret-torn-key";
        string? requestUri = null;
        string? requestBody = null;

        using var http = CreateHttpClient(request =>
        {
            requestUri = request.RequestUri?.ToString();
            requestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"state\":\"connected\",\"tornPlayerId\":7,\"storedAtUtc\":\"2026-09-05T20:00:00Z\",\"consent\":{\"documentVersion\":\"v2\",\"purpose\":\"Personal Torn data\",\"acceptedAtUtc\":\"2026-09-05T19:00:00Z\"}}", Encoding.UTF8, "application/json")
            };
        });
        var sut = new AccountConnectionsService(http);

        await sut.ConnectAsync(secret, consentAccepted: true);

        Assert.NotNull(requestUri);
        Assert.NotNull(requestBody);
        Assert.DoesNotContain(secret, requestUri!, StringComparison.Ordinal);
        Assert.Contains(secret, requestBody!, StringComparison.Ordinal);
        Assert.Contains("ConsentAccepted", requestBody!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"state\":\"unknown\",\"tornPlayerId\":7}")]
    [InlineData("{\"state\":\"connected\",\"tornPlayerId\":7,\"storedAtUtc\":null,\"consent\":null}")]
    [InlineData("{\"state\":\"connected\",\"tornPlayerId\":7,\"storedAtUtc\":\"2026-09-05T20:00:00Z\",\"consent\":{\"documentVersion\":\"v2\",\"purpose\":\"Personal Torn data\"}}")]
    [InlineData("{\"state\":\"connected\",\"tornPlayerId\":7,\"storedAtUtc\":\"2026-09-05T20:00:00Z\",\"consent\":{\"documentVersion\":\" \",\"purpose\":\"  \",\"acceptedAtUtc\":\"2026-09-05T19:00:00Z\"}}")]
    [InlineData("{\"state\":\"connected\",\"tornPlayerId\":7,\"storedAtUtc\":\"0001-01-01T00:00:00Z\",\"consent\":{\"documentVersion\":\"v2\",\"purpose\":\"Personal Torn data\",\"acceptedAtUtc\":\"2026-09-05T19:00:00Z\"}}")]
    [InlineData("{\"state\":\"not_connected\",\"tornPlayerId\":7,\"storedAtUtc\":null,\"consent\":null}")]
    public async Task Semantically_incomplete_status_payloads_fail_closed(string payload)
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        var sut = new AccountConnectionsService(http);

        var failure = await Assert.ThrowsAsync<AccountConnectionFailure>(() => sut.GetAsync());

        Assert.Equal("invalid_response", failure.Code);
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
    }

    [Fact]
    public async Task Not_connected_status_accepts_only_the_empty_connection_shape()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"state\":\"not_connected\",\"tornPlayerId\":null,\"storedAtUtc\":null,\"consent\":null}",
                Encoding.UTF8,
                "application/json")
        });
        var sut = new AccountConnectionsService(http);

        var status = await sut.GetAsync();

        Assert.Equal("not_connected", status.State);
        Assert.Null(status.TornPlayerId);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "consent_required", "Confirm the current connection consent")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "invalid_torn_api_key", "Torn rejected this API key")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "key_vault_unavailable", "Secure credential storage")]
    [InlineData(HttpStatusCode.Conflict, "identity_setup_required", "signed-in account is not ready")]
    public async Task ConnectAsync_maps_backend_codes_to_bounded_member_safe_errors(
        HttpStatusCode statusCode,
        string code,
        string expectedMessage)
    {
        var payload = $$"""
        { "error": { "code": "{{code}}", "message": "raw backend detail", "requestId": "trace" } }
        """;

        using var http = CreateHttpClient(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        var sut = new AccountConnectionsService(http);

        var failure = await Assert.ThrowsAsync<AccountConnectionFailure>(
            () => sut.ConnectAsync("never-render-this", consentAccepted: true));

        Assert.Equal(code, failure.Code);
        Assert.Contains(expectedMessage, failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw backend detail", failure.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("never-render-this", failure.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"error\":null}")]
    [InlineData("{\"error\":{\"code\":42}}")]
    public async Task Error_responses_with_unexpected_json_shapes_use_the_safe_status_fallback(string payload)
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        var sut = new AccountConnectionsService(http);

        var failure = await Assert.ThrowsAsync<AccountConnectionFailure>(() => sut.GetAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.Contains("could not be completed", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    public async Task Successful_responses_with_invalid_status_payloads_become_retryable_safe_failures(string payload)
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        });
        var sut = new AccountConnectionsService(http);

        var failure = await Assert.ThrowsAsync<AccountConnectionFailure>(() => sut.GetAsync());

        Assert.Equal("invalid_response", failure.Code);
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Contains("unexpected response", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
        if (payload.Length > 0)
            Assert.DoesNotContain(payload, failure.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_timeout_becomes_a_bounded_failure_without_leaking_operation_state()
    {
        using var http = new HttpClient(new TimeoutHandler())
        {
            BaseAddress = new Uri("https://localhost:7047")
        };
        var sut = new AccountConnectionsService(http);

        var failure = await Assert.ThrowsAsync<AccountConnectionFailure>(
            () => sut.ConnectAsync("never-render-timeout-secret", consentAccepted: true));

        Assert.Equal("connection_timeout", failure.Code);
        Assert.Equal(HttpStatusCode.RequestTimeout, failure.StatusCode);
        Assert.Contains("outcome is unknown", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("was not changed", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never-render-timeout-secret", failure.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mutation_response_body_io_failure_is_reconciled_as_an_unknown_outcome()
    {
        using var http = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent()
        });
        var sut = new AccountConnectionsService(http);

        var failure = await Assert.ThrowsAsync<AccountConnectionFailure>(
            () => sut.ConnectAsync("never-render-io-secret", consentAccepted: true));

        Assert.Equal("connection_interrupted", failure.Code);
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Contains("outcome is unknown", failure.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never-render-io-secret", failure.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Explicit_caller_cancellation_is_not_reclassified_as_a_timeout()
    {
        using var http = new HttpClient(new TimeoutHandler())
        {
            BaseAddress = new Uri("https://localhost:7047")
        };
        var sut = new AccountConnectionsService(http);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GetAsync(cts.Token));
    }

    [Fact]
    public async Task RevokeAsync_returns_the_authoritative_owner_scoped_status()
    {
        HttpMethod? method = null;
        string? requestUri = null;
        using var http = CreateHttpClient(request =>
        {
            method = request.Method;
            requestUri = request.RequestUri?.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"state\":\"connected\",\"tornPlayerId\":7,\"storedAtUtc\":\"2026-09-05T20:00:00Z\",\"consent\":{\"documentVersion\":\"v2\",\"purpose\":\"Personal Torn data\",\"acceptedAtUtc\":\"2026-09-05T19:00:00Z\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var sut = new AccountConnectionsService(http);

        var status = await sut.RevokeAsync();

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/v1/account/connections/torn/revoke", requestUri);
        Assert.Equal("connected", status.State);
        Assert.Equal(7, status.TornPlayerId);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new DelegateHandler(handler))
        {
            BaseAddress = new Uri("https://localhost:7047")
        };
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }

    private sealed class ThrowingContent : HttpContent
    {
        public ThrowingContent()
        {
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.FromException(new IOException("simulated response-body interruption"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new TaskCanceledException("simulated HttpClient timeout");
    }
}
