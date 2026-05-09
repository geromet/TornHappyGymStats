using System.Security.Claims;
using System.Text.Json.Nodes;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/surfaces")]
public sealed class SurfacesController : ApiControllerBase
{
    private readonly string _cacheDirectory;
    private readonly IUserLogEntryRepository _userLogRepo;

    public SurfacesController(SurfacesConfig config, IUserLogEntryRepository userLogRepo)
    {
        _cacheDirectory = config.CacheDirectory;
        _userLogRepo = userLogRepo;
    }

    [HttpGet("meta")]
    public async Task<IActionResult> GetMeta(CancellationToken ct)
        => await ServeFile("meta.json", ct);

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatest(CancellationToken ct)
        => await ServeLatestFile(ct);

    [HttpGet("me")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> GetMine(CancellationToken ct)
    {
        var claimValue = User.FindFirstValue(Claims.AnonymousId);
        if (!Guid.TryParse(claimValue, out var callerAnonymousId))
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "anonymous_id claim is missing or invalid.");

        var gymLogs = await _userLogRepo.GetGymLogEntriesAsync(callerAnonymousId, ct);
        var surfaces = SurfaceSeriesBuilder.Build(gymLogs, new Dictionary<string, IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>>());

        return Ok(new
        {
            dataset = "surfaces",
            version = "caller-scoped-v1",
            meta = new
            {
                gymPointCount = surfaces.GymX.Length,
                eventPointCount = 0,
                recordCount = surfaces.GymX.Length,
            },
            series = new
            {
                gymCloud = new
                {
                    x = surfaces.GymX,
                    y = surfaces.GymY,
                    z = surfaces.GymZ,
                },
                eventsCloud = new
                {
                    x = Array.Empty<int>(),
                    y = Array.Empty<int>(),
                    z = Array.Empty<int>(),
                },
            },
        });
    }

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
