using System.Net;
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
        if (!IsLoopbackRequest())
        {
            logger.LogWarning(
                "Rejected non-loopback war notify request: remoteIp={RemoteIp}",
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            return ApiError(StatusCodes.Status403Forbidden, "forbidden", "This endpoint only accepts loopback requests.");
        }

        var dto = await hubBroadcaster.BroadcastCurrentStateAsync(ct);
        return Accepted(new WarNotifyAcceptedDto("broadcasted", dto));
    }

    private bool IsLoopbackRequest()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is null || IPAddress.IsLoopback(remoteIp);
    }
}
