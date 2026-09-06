using System.Net;
using System.Text;
using HappyGymStats.Api.Services;
using HappyGymStats.Core.Torn;

namespace HappyGymStats.Tests;

public sealed class TornConnectionValidatorTests
{
    private const string FixtureKey = "validator-fixture-key";

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
