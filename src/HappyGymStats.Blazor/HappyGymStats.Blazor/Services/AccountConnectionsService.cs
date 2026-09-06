using System.Net;
using System.Text.Json;

namespace HappyGymStats.Blazor.Services;

public sealed class AccountConnectionsService(HttpClient http)
{
    private const string Endpoint = "/api/v1/account/connections/torn";

    public async Task<TornConnectionStatusDto> GetAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Endpoint, ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadStatusAsync(response, ct);
    }

    public async Task<TornConnectionStatusDto> ConnectAsync(string tornApiKey, bool consentAccepted, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(Endpoint, new { TornApiKey = tornApiKey, ConsentAccepted = consentAccepted }, ct);
        await EnsureSuccessAsync(response, ct);
        return await ReadStatusAsync(response, ct);
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"{Endpoint}/revoke", content: null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task<TornConnectionStatusDto> ReadStatusAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TornConnectionStatusDto>(ct)
                ?? throw ApiFailure.Deserialization(Endpoint, new JsonException("Connection status payload was empty."));
        }
        catch (JsonException ex)
        {
            throw ApiFailure.Deserialization(Endpoint, ex);
        }
        catch (NotSupportedException ex)
        {
            throw ApiFailure.Deserialization(Endpoint, ex);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? code = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var codeElement))
            {
                code = codeElement.GetString();
            }
        }
        catch (JsonException)
        {
            // The UI never surfaces raw backend payloads. Status-based fallback below remains safe.
        }

        throw new AccountConnectionFailure(
            code,
            response.StatusCode,
            SafeMessageFor(code, response.StatusCode));
    }

    private static string SafeMessageFor(string? code, HttpStatusCode statusCode) => code switch
    {
        "identity_setup_required" => "Your signed-in account is not ready for a personal Torn connection yet.",
        "consent_required" => "Confirm the current connection consent before storing this Torn API key.",
        "invalid_torn_api_key" => "Torn rejected this API key. Check the key and its required access, then try again.",
        "torn_unavailable" => "Torn is temporarily unavailable. Your existing connection has not been changed.",
        "key_vault_unavailable" => "Secure credential storage is temporarily unavailable. Please try again later.",
        "not_connected" => "There is no Torn connection to revoke.",
        _ => statusCode switch
        {
            HttpStatusCode.Unauthorized => "Your sign-in has expired. Sign in again and retry.",
            HttpStatusCode.Forbidden => "Your account is not allowed to manage this connection.",
            _ => "The Torn connection request could not be completed. Please try again."
        }
    };
}

public sealed class AccountConnectionFailure(string? code, HttpStatusCode statusCode, string safeMessage)
    : Exception(safeMessage)
{
    public string? Code { get; } = code;
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string SafeMessage { get; } = safeMessage;
}

public sealed record TornConnectionStatusDto(
    string State,
    int? TornPlayerId,
    DateTimeOffset? StoredAtUtc,
    TornConsentStatusDto? Consent);

public sealed record TornConsentStatusDto(
    string DocumentVersion,
    string Purpose,
    DateTimeOffset AcceptedAtUtc);
