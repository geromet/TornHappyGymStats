using HappyGymStats.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

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
        => await ServeFile("latest.json", ct);

    private async Task<IActionResult> ServeFile(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(_cacheDirectory, fileName);
        if (!System.IO.File.Exists(path))
            return ApiError(StatusCodes.Status404NotFound, "not_found", "No cached surfaces dataset found.");

        var json = await System.IO.File.ReadAllTextAsync(path, ct);
        return Content(json, "application/json");
    }
}
