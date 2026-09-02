using HappyGymStats.Api.Models;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.War;
using Microsoft.AspNetCore.SignalR;

namespace HappyGymStats.Api.Hubs;

public interface IWarHubBroadcaster
{
    Task<WarStateDto> BroadcastCurrentStateAsync(CancellationToken ct);
}

public sealed class WarHubBroadcaster(
    IHubContext<WarHub> hubContext,
    WarDerivedStateService warDerivedStateService,
    ILogger<WarHubBroadcaster> logger) : IWarHubBroadcaster
{
    public const string ScopeKey = "public-war";
    public const string EventName = "WarStateUpdated";

    public async Task<WarStateDto> BroadcastCurrentStateAsync(CancellationToken ct)
    {
        var dto = (await warDerivedStateService.GetCurrentAsync(ScopeKey, ct: ct)).ToStateDto();

        await hubContext.Clients.All.SendAsync(EventName, dto, ct);

        logger.LogInformation(
            "Broadcasted war state update: warId={WarId} status={Status} factions={FactionCount} holes={HoleCount}",
            dto.WarId,
            dto.Status,
            dto.FactionCount,
            dto.HoleCount);

        return dto;
    }
}
