using System.Net;
using System.Text;
using HappyGymStats.Api.Services;
using HappyGymStats.Core.Torn;

namespace HappyGymStats.Tests;

public sealed class TornConnectionValidatorTests
{
    private const string FixtureKey = "validator-fixture-key";

    [Fact]
    public async Task Limited_access_key_is_validated_before_identity_without_secret_in_uri()
    {
        var requestedPaths = new List<string>();
        var validator = CreateValidator((request, _) =>
        {
            AssertCredentialTransport(request);
            requestedPaths.Add(request.RequestUri!.PathAndQuery);

            return Task.FromResult(request.RequestUri.AbsolutePath switch
            {
                "/v2/key/info" => JsonResponse(KeyInfoJson("Limited Access")),
                "/v2/user/basic" => JsonResponse("{\"player_id\":123}"),
                _ => throw new InvalidOperationException($"Unexpected Torn request: {request.RequestUri.AbsolutePath}"),
            });
        });

        var playerId = await validator.GetPlayerIdAsync(FixtureKey);

        Assert.Equal(123, playerId);
        Assert.Equal(
            ["/v2/key/info", "/v2/user/basic?selections=basic"],
            requestedPaths);
    }

    [Theory]
    [InlineData("Full Access")]
    [InlineData("Custom")]
    [InlineData("Minimal Access")]
    [InlineData("Public Only")]
    public async Task Non_limited_key_is_rejected_before_identity_lookup(string accessType)
    {
        var requests = 0;
        var validator = CreateValidator((request, _) =>
        {
            requests++;
            AssertCredentialTransport(request);
            Assert.Equal("/v2/key/info", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(KeyInfoJson(accessType)));
        });

        var failure = await Assert.ThrowsAsync<TornConnectionValidationException>(
            () => validator.GetPlayerIdAsync(FixtureKey));

        Assert.False(failure.IsTransient);
        Assert.Equal(1, requests);
        Assert.DoesNotContain(FixtureKey, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(accessType, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"info\":{}}")]
    [InlineData("{\"info\":{\"access\":null}}")]
    [InlineData("{\"info\":{\"access\":{}}}")]
    [InlineData("{\"info\":{\"access\":{\"type\":4}}}")]
    [InlineData("{\"info\":{\"access\":{\"type\":\"\"}}}")]
    public async Task Malformed_key_access_contract_fails_closed_without_identity_lookup(string payload)
    {
        var requests = 0;
        var validator = CreateValidator((request, _) =>
        {
            requests++;
            AssertCredentialTransport(request);
            Assert.Equal("/v2/key/info", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(payload));
        });

        var failure = await Assert.ThrowsAsync<TornConnectionValidationException>(
            () => validator.GetPlayerIdAsync(FixtureKey));

        Assert.True(failure.IsTransient);
        Assert.Equal(1, requests);
        Assert.DoesNotContain(payload, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FixtureKey, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"unexpected\"")]
    [InlineData("{\"player_id\":\"123\"}")]
    [InlineData("{\"error\":{\"code\":\"5\"}}")]
    [InlineData("{\"error\":null}")]
    [InlineData("not-json")]
    public async Task Malformed_upstream_contract_is_a_sanitized_transient_failure(string payload)
    {
        var validator = CreateValidator((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        }));

        var failure = await Assert.ThrowsAsync<TornConnectionValidationException>(
            () => validator.GetPlayerIdAsync(FixtureKey));

        Assert.True(failure.IsTransient);
        Assert.DoesNotContain(payload, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FixtureKey, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Response_body_read_failure_is_a_sanitized_transient_failure()
    {
        var validator = CreateValidator((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingContent(),
        }));

        var failure = await Assert.ThrowsAsync<TornConnectionValidationException>(
            () => validator.GetPlayerIdAsync(FixtureKey));

        Assert.True(failure.IsTransient);
        Assert.DoesNotContain(FixtureKey, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_timeout_is_transient_but_caller_cancellation_is_preserved()
    {
        var timeoutValidator = CreateValidator((_, _) =>
            throw new OperationCanceledException("simulated transport timeout"));
        var timeout = await Assert.ThrowsAsync<TornConnectionValidationException>(
            () => timeoutValidator.GetPlayerIdAsync(FixtureKey));
        Assert.True(timeout.IsTransient);

        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var cancelledValidator = CreateValidator((_, ct) =>
            throw new OperationCanceledException("caller cancelled", null, ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledValidator.GetPlayerIdAsync(FixtureKey, callerCancellation.Token));
    }

    private static void AssertCredentialTransport(HttpRequestMessage request)
    {
        Assert.NotNull(request.RequestUri);
        Assert.DoesNotContain(FixtureKey, request.RequestUri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("ApiKey", request.Headers.Authorization?.Scheme);
        Assert.Equal(FixtureKey, request.Headers.Authorization?.Parameter);
    }

    private static HttpResponseMessage JsonResponse(string payload)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };

    private static string KeyInfoJson(string accessType)
        => $$"""
        {
          "info": {
            "access": {
              "type": "{{accessType}}"
            }
          }
        }
        """;

    private static TornConnectionValidator CreateValidator(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        => new(
            new HttpClient(new DelegateHandler(handler))
            {
                BaseAddress = new Uri("https://api.torn.com/"),
            },
            new TornRateLimiter());

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => callback(request, cancellationToken);
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.FromException(new IOException("simulated body read failure"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
