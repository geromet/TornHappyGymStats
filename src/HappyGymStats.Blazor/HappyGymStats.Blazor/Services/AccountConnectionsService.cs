using System.Net;
using System.Text.Json;

namespace HappyGymStats.Blazor.Services;

public sealed class AccountConnectionsService(HttpClient http)
{
    private const string Endpoint = "/api/v1/account/connections/torn";

    public async Task<TornConnectionStatusDto> GetAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(Endpoint, ct);
            await EnsureSuccessAsync(response, ct);
            return await ReadStatusAsync(response, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    public async Task<TornConnectionStatusDto> ConnectAsync(string tornApiKey, bool consentAccepted, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(Endpoint, new { TornApiKey = tornApiKey, ConsentAccepted = consentAccepted }, ct);
            await EnsureSuccessAsync(response, ct);
            return await ReadStatusAsync(response, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    public async Task RevokeAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsync($"{Endpoint}/revoke", content: null, ct);
            await EnsureSuccessAsync(response, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw TimedOut();
        }
    }

    private static async Task<TornConnectionStatusDto> ReadStatusAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var status = await response.Content.ReadFromJsonAsync<TornConnectionStatusDto>(ct)
                ?? throw InvalidResponse();

            return IsValidStatus(status) ? status : throw InvalidResponse();
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
        catch (NotSupportedException)
        {
            throw InvalidResponse();
        }
    }

    private static bool IsValidStatus(TornConnectionStatusDto status)
        => status.State switch
        {
            "not_connected" => status.TornPlayerId is null
                && status.StoredAtUtc is null
                && status.Consent is null,
            "connected" => status.TornPlayerId is > 0
                && status.StoredAtUtc is not null
                && status.Consent is
                {
                    DocumentVersion.Length: > 0,
                    Purpose.Length: > 0
                },
            _ => false
        };

    private static AccountConnectionFailure InvalidResponse()
        => new(
            "invalid_response",
            HttpStatusCode.BadGateway,
            "The Torn connection service returned an unexpected response. Please try again.");

    private static AccountConnectionFailure TimedOut()
        => new(
            "connection_timeout",
            HttpStatusCode.RequestTimeout,
            "The Torn connection service took too long to respond. Your connection was not changed; please try again.");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? code = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String)
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
