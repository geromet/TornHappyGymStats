using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Api.Models;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Authorize(Roles = Roles.User)]
[Route("api/v1/war/scout")]
public sealed class WarScoutController(WarScoutService scoutService) : ApiControllerBase
{
    [HttpGet("{factionId:long}")]
    public async Task<IActionResult> GetProfile(long factionId, CancellationToken ct)
    {
        if (factionId <= 0)
        {
            return ValidationError("Faction id must be positive.");
        }

        var profile = await scoutService.GetProfileAsync(factionId, ct);
        if (profile is null)
        {
            return ApiError(
                StatusCodes.Status404NotFound,
                "no_war_history",
                $"No captured ranked-war history exists yet for faction {factionId}.");
        }

        return Ok(profile.ToDto());
    }
}
