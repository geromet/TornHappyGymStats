using HappyGymStats.Api.Hubs;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Api.Models;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Authorize(Roles = Roles.User)]
[Route("api/v1/war")]
public sealed class WarController(
    WarDerivedStateService warDerivedStateService,
    IWarHubBroadcaster hubBroadcaster,
    ILogger<WarController> logger) : ApiControllerBase
{
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var dto = (await warDerivedStateService.GetCurrentAsync(WarHubBroadcaster.ScopeKey, ct: ct)).ToStateDto();

        // "No war is running" is a successful answer, not a service fault.
        //
        // This used to fall into the 503 below together with `degraded`, so on
        // any evening between wars the board rendered a red "War board
        // unavailable. The API service is currently unavailable." The API was
        // fine; there was simply no war. 503 also tells monitoring and every
        // other caller that the service is down, which it is not.
        //
        // `degraded` keeps its 503: there a war IS running and the state cannot
        // be trusted, which is exactly what that status code is for.
        if (dto.Status == WarStatus.NotReady)
        {
            logger.LogInformation("War current request served with no active war for scope {Scope}", WarHubBroadcaster.ScopeKey);
            return Ok(dto);
        }

        if (!dto.IsReady)
        {
            logger.LogWarning(
                "War current request returned non-ready state: status={Status} warnings={Warnings} errors={Errors}",
                dto.Status,
                dto.Warnings.Count,
                dto.Errors.Count);

            return ApiError(
                StatusCodes.Status503ServiceUnavailable,
                "war_state_not_ready",
                "Current war state is not ready.",
                dto);
        }

        return Ok(dto);
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
        => Ok((await warDerivedStateService.GetCurrentAsync(WarHubBroadcaster.ScopeKey, ct: ct)).ToHealthDto());

    [AllowAnonymous]
    [HttpPost("internal/notify")]
    public async Task<IActionResult> Notify(CancellationToken ct)
    {
        if (!InternalHttpRequestBoundary.IsDirectInternalTransport(HttpContext))
        {
            logger.LogWarning(
                "Rejected non-direct war notify request: remoteIp={RemoteIp}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            return ApiError(StatusCodes.Status403Forbidden, "forbidden", "This endpoint only accepts direct internal requests.");
        }

        var dto = await hubBroadcaster.BroadcastCurrentStateAsync(ct);
        return Accepted(new WarNotifyAcceptedDto("broadcasted", dto));
    }
}
