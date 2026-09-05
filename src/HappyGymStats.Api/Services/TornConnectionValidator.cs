using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using HappyGymStats.Core.Torn;

namespace HappyGymStats.Api.Services;

public interface ITornConnectionValidator
{
    Task<int> GetPlayerIdAsync(string apiKey, CancellationToken cancellationToken = default);
}

public sealed class TornConnectionValidationException : Exception
{
    public TornConnectionValidationException(bool isTransient)
        : base(isTransient
            ? "Torn could not validate the API key right now."
            : "Torn rejected the API key.")
    {
        IsTransient = isTransient;
    }

    public bool IsTransient { get; }
}

/// <summary>
/// Minimal server-side validation path for member-submitted Torn API keys.
/// The credential is carried only in Torn's Authorization header and never in the request URI.
/// </summary>
public sealed class TornConnectionValidator : ITornConnectionValidator
{
    private readonly HttpClient _http;
    private readonly TornRateLimiter _rateLimiter;

    public TornConnectionValidator(HttpClient http, TornRateLimiter rateLimiter)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    }

    public async Task<int> GetPlayerIdAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Torn API key is required.", nameof(apiKey));
        }

        var keyIdentity = TornRateLimiter.KeyIdentity(apiKey);
        await _rateLimiter
            .AcquireAsync(keyIdentity, TornRequestPriority.Other, cancellationToken)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "v2/user/basic?selections=basic");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TornConnectionValidationException(isTransient: true);
        }
        catch (HttpRequestException)
        {
            throw new TornConnectionValidationException(isTransient: true);
        }

        using var responseScope = response;
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        JsonDocument document;
        try
        {
            document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            throw new TornConnectionValidationException(IsTransientStatus(response.StatusCode));
        }

        using (document)
        {
            if (TryGetTornError(document.RootElement, out var tornErrorCode))
            {
                var transient = tornErrorCode == 5 || IsTransientStatus(response.StatusCode);
                if (transient)
                {
                    _rateLimiter.ReportThrottled(keyIdentity);
                }

                throw new TornConnectionValidationException(transient);
            }

            if (!response.IsSuccessStatusCode)
            {
                var transient = IsTransientStatus(response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _rateLimiter.ReportThrottled(keyIdentity);
                }

                throw new TornConnectionValidationException(transient);
            }

            if (!document.RootElement.TryGetProperty("player_id", out var playerIdElement)
                || !playerIdElement.TryGetInt32(out var playerId)
                || playerId <= 0)
            {
                throw new TornConnectionValidationException(isTransient: false);
            }

            _rateLimiter.ReportSuccess(keyIdentity);
            return playerId;
        }
    }

    private static bool TryGetTornError(JsonElement root, out int code)
    {
        code = 0;
        if (!root.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (error.TryGetProperty("code", out var codeElement))
        {
            codeElement.TryGetInt32(out code);
        }

        return true;
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
