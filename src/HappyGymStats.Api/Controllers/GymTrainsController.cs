using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/gym-trains")]
public sealed class GymTrainsController : ApiControllerBase
{
    private readonly GymTrainsService _service;

    public GymTrainsController(GymTrainsService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> ListGymTrains(int? limit, string? cursor, CancellationToken ct)
    {
        if (!PaginationHelper.TryGetLimit(limit, out var take, out var limitError))
            return ValidationError(limitError!, new { field = "limit", min = 1, max = Pagination.MaxLimit });

        if (!CursorEncoder.TryDecode(cursor, out var pageCursor))
            return ValidationError("Cursor is invalid.", new { field = "cursor" });

        var page = await _service.GetPageAsync(take, pageCursor, ct);
        return Ok(page);
    }
}
