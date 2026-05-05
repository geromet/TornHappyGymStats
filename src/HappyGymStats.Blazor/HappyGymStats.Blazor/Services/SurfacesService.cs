using System.Net;
using HappyGymStats.Blazor.Models;

namespace HappyGymStats.Blazor.Services;

public sealed class SurfacesService(HttpClient http)
{
    public async Task<SurfacesDatasetDto?> GetLatestAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/api/v1/torn/surfaces/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SurfacesDatasetDto>(ct);
    }

    public async Task<ImportStatusDto?> StartImportAsync(string apiKey, bool fresh, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/v1/torn/import-jobs", new { apiKey, fresh }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ImportStatusDto>(ct);
    }
}
