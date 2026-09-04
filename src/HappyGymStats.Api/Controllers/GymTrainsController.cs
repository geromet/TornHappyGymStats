using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/gym-trains")]
public sealed class GymTrainsController : ApiControllerBase
{
    private readonly IUserLogEntryRepository _userLogs;

    public GymTrainsController(IUserLogEntryRepository userLogs) => _userLogs = userLogs;

    [HttpGet]
    public async Task<IActionResult> ListGymTrains(int? limit, string? cursor, CancellationToken ct)
    {
        if (!PaginationHelper.TryGetLimit(limit, out var take, out var limitError))
            return ValidationError(limitError!, new { field = "limit", min = 1, max = Pagination.MaxLimit });

        if (!CursorEncoder.TryDecode(cursor, out var pageCursor))
            return ValidationError("Cursor is invalid.", new { field = "cursor" });

        var page = await _userLogs.GetGymTrainsPageAsync(take, pageCursor, ct);
        return Ok(page);
    }

    [HttpGet("{anonymousId:guid}")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> GetUserGymTrains(Guid anonymousId, int? limit, string? cursor, CancellationToken ct)
    {
        var callerAnonymousId = User.FindFirstValue(Claims.AnonymousId);
        if (callerAnonymousId != anonymousId.ToString())
            return Forbid();

        if (!PaginationHelper.TryGetLimit(limit, out var take, out var limitError))
            return ValidationError(limitError!, new { field = "limit", min = 1, max = Pagination.MaxLimit });

        if (!CursorEncoder.TryDecode(cursor, out var pageCursor))
            return ValidationError("Cursor is invalid.", new { field = "cursor" });

        var page = await _userLogs.GetGymTrainsPageAsync(anonymousId, take, pageCursor, ct);
        return Ok(page);
    }
}
