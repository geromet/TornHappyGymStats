using System.Net;
using System.Text.Json;
using HappyGymStats.Blazor.Models;

namespace HappyGymStats.Blazor.Services;

public sealed class SurfacesService(HttpClient http)
{
    private const string LatestEndpoint = "/api/v1/torn/surfaces/latest";
    private const string ImportEndpoint = "/api/v1/torn/import-jobs";

    public async Task<SurfacesDatasetDto?> GetLatestAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync(LatestEndpoint, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        EnsureSuccessOrThrow(response, LatestEndpoint);

        return await ReadJsonOrThrowAsync<SurfacesDatasetDto>(response, LatestEndpoint, ct);
    }

    public async Task<ImportStatusDto?> StartImportAsync(string apiKey, bool fresh, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(ImportEndpoint, new { apiKey, fresh }, ct);
        EnsureSuccessOrThrow(response, ImportEndpoint);

        var status = await ReadJsonOrThrowAsync<ImportStatusDto>(response, ImportEndpoint, ct);
        if (status is { Outcome: "failed" })
        {
            throw new ApiFailure(
                ImportEndpoint,
                ApiFailureCategory.ImportFailure,
                string.IsNullOrWhiteSpace(status.ErrorMessage)
                    ? "Import failed due to a backend validation or processing error."
                    : $"Import failed: {status.ErrorMessage}",
                response.StatusCode);
        }

        return status;
    }

    private static void EnsureSuccessOrThrow(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode) return;
        throw ApiFailure.FromHttp(endpoint, response.StatusCode);
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
