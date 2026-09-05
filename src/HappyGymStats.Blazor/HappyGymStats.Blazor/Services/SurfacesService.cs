using System.Net;
using System.Text.Json;
using HappyGymStats.Core.Models;

namespace HappyGymStats.Blazor.Services;

public sealed class SurfacesService(HttpClient http)
{
    private const string LatestEndpoint = "/api/v1/torn/surfaces/latest";
    private const string MyStatsEndpoint = "/api/v1/torn/surfaces/me";
    private const string ImportEndpoint = "/api/v1/torn/import-jobs";
    private const string MyStatsImportEndpoint = "/api/v1/torn/import-jobs/me";

    public async Task<SurfacesDatasetDto?> GetLatestAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync(LatestEndpoint, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccessOrThrow(response, LatestEndpoint);

        return await ReadJsonOrThrowAsync<SurfacesDatasetDto>(response, LatestEndpoint, ct);
    }

    public async Task<MyStatsDatasetDto?> GetMyStatsAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync(MyStatsEndpoint, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccessOrThrow(response, MyStatsEndpoint);

        return await ReadJsonOrThrowAsync<MyStatsDatasetDto>(response, MyStatsEndpoint, ct);
    }

    public async Task<ImportStatusDto?> StartImportAsync(string apiKey, bool fresh, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(ImportEndpoint, new { apiKey, fresh }, ct);
        EnsureSuccessOrThrow(response, ImportEndpoint);

        return await ReadImportStatusOrThrowAsync(response, ImportEndpoint, ct);
    }

    public async Task<ImportStatusDto?> StartMyStatsImportAsync(string apiKey, bool fresh = true, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(MyStatsImportEndpoint, new { apiKey, fresh }, ct);
        EnsureSuccessOrThrow(response, MyStatsImportEndpoint);

        return await ReadImportStatusOrThrowAsync(response, MyStatsImportEndpoint, ct);
    }

    private static void EnsureSuccessOrThrow(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode) return;
        throw ApiFailure.FromHttp(endpoint, response.StatusCode);
    }

    private static async Task<ImportStatusDto?> ReadImportStatusOrThrowAsync(HttpResponseMessage response, string endpoint, CancellationToken ct)
    {
        var status = await ReadJsonOrThrowAsync<ImportStatusDto>(response, endpoint, ct);
        if (status is { Outcome: "failed" })
        {
            throw new ApiFailure(
                endpoint,
                ApiFailureCategory.ImportFailure,
                "Import failed due to a backend validation or processing error.",
                response.StatusCode);
        }

        return status;
    }

    private static async Task<T?> ReadJsonOrThrowAsync<T>(HttpResponseMessage response, string endpoint, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
        catch (JsonException ex)
        {
            throw ApiFailure.Deserialization(endpoint, ex);
        }
        catch (NotSupportedException ex)
        {
            throw ApiFailure.Deserialization(endpoint, ex);
        }
    }
}
