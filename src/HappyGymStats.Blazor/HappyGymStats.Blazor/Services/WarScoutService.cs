using System.Net.Http.Json;
using HappyGymStats.Core.Models;

namespace HappyGymStats.Blazor.Services;

/// <summary>
/// Fetches a faction's pre-war scouting report. Relies on <see cref="AccessTokenForwardingHandler"/>
/// (registered on this client) to attach the caller's access token, so it never needs to touch
/// Torn or the token provider directly.
/// </summary>
public sealed class WarScoutService(HttpClient http, ILogger<WarScoutService> logger)
{
    public async Task<(FactionScoutDto? Profile, ApiFailure? Failure)> GetProfileAsync(long factionId, CancellationToken ct = default)
    {
        var endpoint = $"/api/v1/war/scout/{factionId}";

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(endpoint, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "War scout request failed for {Endpoint}", endpoint);
            return (null, new ApiFailure(endpoint, ApiFailureCategory.ApiUnavailable, "The scouting report could not reach the API service.", null, ex));
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, ApiFailure.FromHttp(endpoint, response.StatusCode));
        }

        try
        {
            var dto = await response.Content.ReadFromJsonAsync<FactionScoutDto>(cancellationToken: ct);
            return dto is null
                ? (null, ApiFailure.Deserialization(endpoint, new InvalidOperationException("Scouting report payload was empty.")))
                : (dto, null);
        }
        catch (NotSupportedException ex)
        {
            return (null, ApiFailure.Deserialization(endpoint, ex));
        }
        catch (System.Text.Json.JsonException ex)
        {
            return (null, ApiFailure.Deserialization(endpoint, ex));
        }
    }
}
