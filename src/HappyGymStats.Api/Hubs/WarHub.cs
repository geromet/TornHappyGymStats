using HappyGymStats.Api.Models;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HappyGymStats.Api.Hubs;

[Authorize(Roles = Roles.User)]
public sealed class WarHub(WarDerivedStateService warDerivedStateService) : Hub
{
    public async Task RequestCurrentState()
    {
        var ct = Context.ConnectionAborted;
        var dto = (await warDerivedStateService.GetCurrentAsync(WarHubBroadcaster.ScopeKey, ct: ct)).ToStateDto();
        await Clients.Caller.SendAsync(WarHubBroadcaster.EventName, dto, ct);
    }
}
