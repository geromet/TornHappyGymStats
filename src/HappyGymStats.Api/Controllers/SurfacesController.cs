using HappyGymStats.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Nodes;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/surfaces")]
public sealed class SurfacesController : ApiControllerBase
{
    private readonly string _cacheDirectory;

    public SurfacesController(SurfacesConfig config) => _cacheDirectory = config.CacheDirectory;

    [HttpGet("meta")]
    public async Task<IActionResult> GetMeta(CancellationToken ct)
        => await ServeFile("meta.json", ct);

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(CancellationToken ct)
        => await ServeLatestFile(ct);

    private async Task<IActionResult> ServeLatestFile(CancellationToken ct)
    {
        var path = Path.Combine(_cacheDirectory, "latest.json");
        if (!System.IO.File.Exists(path))
            return ApiError(StatusCodes.Status404NotFound, "not_found", "No cached surfaces dataset found.");

        var json = await System.IO.File.ReadAllTextAsync(path, ct);
        return Content(SanitizeLatestPayload(json), "application/json");
    }

    private async Task<IActionResult> ServeFile(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(_cacheDirectory, fileName);
        if (!System.IO.File.Exists(path))
            return ApiError(StatusCodes.Status404NotFound, "not_found", "No cached surfaces dataset found.");

        var json = await System.IO.File.ReadAllTextAsync(path, ct);
        return Content(json, "application/json");
    }

    private static string SanitizeLatestPayload(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        if (node == null) return json;

        node.Remove("syncedAtUtc");

        if (node["series"] is JsonObject series)
        {
            if (series["gymCloud"] is JsonObject gymCloud)
            {
                gymCloud.Remove("text");
                gymCloud.Remove("confidence");
                gymCloud.Remove("confidenceReasons");
                gymCloud.Remove("provenanceWarnings");
            }

            if (series["eventsCloud"] is JsonObject eventsCloud)
            {
                eventsCloud.Remove("text");
            }
        }

        if (node["meta"] is JsonObject meta)
        {
            meta.Remove("provenanceWarningsDiagnostics");
        }

        return node.ToJsonString();
    }
}
