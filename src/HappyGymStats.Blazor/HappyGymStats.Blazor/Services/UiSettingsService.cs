using System.Net.Http.Json;

namespace HappyGymStats.Blazor.Services;

/// <summary>
/// Reads and writes the runtime UI switches.
///
/// Every read fails OPEN: if the API is unreachable or returns something
/// unexpected, features stay enabled. A settings lookup that cannot complete
/// should never be the reason the site renders blank.
/// </summary>
public sealed class UiSettingsService(HttpClient http, ILogger<UiSettingsService> logger)
{
    private const string Endpoint = "/api/v1/ui-settings";
    private const string GymPointCloudEndpoint = "/api/v1/ui-settings/gym-point-cloud";

    public sealed record UiSettings(bool GymPointCloudEnabled)
    {
        public static UiSettings Defaults { get; } = new(GymPointCloudEnabled: true);
    }

    public async Task<UiSettings> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var dto = await http.GetFromJsonAsync<UiSettingsDto>(Endpoint, ct);
            if (dto is null)
                return UiSettings.Defaults;

            return new UiSettings(dto.GymPointCloudEnabled ?? true);
        }
        catch (Exception ex)
        {
            // Fail open, and say so, rather than hiding the page because a
            // lookup failed.
            logger.LogWarning(ex, "Could not read UI settings from {Endpoint}; defaulting to enabled.", Endpoint);
            return UiSettings.Defaults;
        }
    }

    /// <summary>
    /// Admin-only. Returns true when the change was accepted; false when the
    /// server refused it (typically 401/403 for a non-admin).
    /// </summary>
    public async Task<bool> SetGymPointCloudAsync(bool enabled, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PutAsJsonAsync(GymPointCloudEndpoint, new { enabled }, ct);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning(
                "Setting gym point cloud to {Enabled} was refused with {StatusCode}.",
                enabled, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update the gym point cloud setting.");
            return false;
        }
    }

    private sealed record UiSettingsDto(bool? GymPointCloudEnabled);
}
