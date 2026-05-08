using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Faction;
using HappyGymStats.Core.Services;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/factions")]
public sealed class FactionController : ApiControllerBase
{
    private readonly FactionService _service;
    private readonly IFactionOwnershipVerifier _ownershipVerifier;

    public FactionController(FactionService service, IFactionOwnershipVerifier ownershipVerifier)
    {
        _service = service;
        _ownershipVerifier = ownershipVerifier;
    }

    [HttpGet("{factionAnonymousId:guid}/members")]
    [Authorize(Roles = Roles.FactionOwner)]
    public async Task<IActionResult> GetFactionMembers(Guid factionAnonymousId, CancellationToken ct)
    {
        var callerAnonymousIdStr = User.FindFirstValue(Claims.AnonymousId);
        if (!Guid.TryParse(callerAnonymousIdStr, out var callerAnonymousId))
            return Forbid();

        var isOwner = await _ownershipVerifier.IsOwnerAsync(callerAnonymousId, factionAnonymousId, ct);
        if (!isOwner)
            return ApiError(StatusCodes.Status403Forbidden, "ownership_not_verified",
                "Faction ownership could not be verified. This feature is not yet fully implemented.");

        var members = await _service.GetMemberSummariesAsync(factionAnonymousId, ct);
        return Ok(new { factionAnonymousId, members });
    }
}
