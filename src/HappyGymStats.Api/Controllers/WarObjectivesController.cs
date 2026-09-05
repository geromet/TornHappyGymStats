using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.War;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Authorize(Roles = Roles.User)]
[Route("api/v1/war/objectives")]
public sealed class WarObjectivesController(IWarObjectiveRepository objectives) : ApiControllerBase
{
    [HttpGet("{factionId:long}/{warId:long}/current")]
    public async Task<IActionResult> GetCurrent(long factionId, long warId, CancellationToken ct)
    {
        var current = await objectives.GetCurrentAsync(factionId, warId, ct);
        return current is null
            ? ApiError(StatusCodes.Status404NotFound, "war_objective_not_found", "No objective exists for this war.")
            : Ok(ToDto(current));
    }

    [HttpGet("{factionId:long}/{warId:long}/history")]
    public async Task<IActionResult> GetHistory(long factionId, long warId, CancellationToken ct)
        => Ok((await objectives.GetHistoryAsync(factionId, warId, ct)).Select(ToDto));

    [Authorize(Roles = Roles.FactionOwner + "," + Roles.Admin)]
    [HttpPost]
    public async Task<IActionResult> Append(
        [FromBody] AppendWarObjectiveRequest request,
        CancellationToken ct)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor))
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Authenticated user identity is unavailable.");

        var created = await objectives.AppendNextAsync(
            request.FactionId,
            request.WarId,
            request.Mode,
            actor,
            DateTimeOffset.UtcNow,
            request.StopAtFactionScore,
            request.Notes,
            ct);

        return Created(
            $"/api/v1/war/objectives/{created.FactionId}/{created.Objective.WarId}/current",
            ToDto(created));
    }

    private static WarObjectiveVersionDto ToDto(FactionWarObjectiveVersion stored)
        => new(
            stored.FactionId,
            stored.Objective.WarId,
            stored.Objective.Version,
            stored.Objective.Mode,
            stored.Objective.IsExplicit,
            stored.Objective.StopAtFactionScore,
            stored.Objective.Notes,
            stored.Objective.ChangedBy,
            stored.Objective.CreatedAtUtc);
}

public sealed record AppendWarObjectiveRequest(
    long FactionId,
    long WarId,
    WarObjectiveMode Mode,
    int? StopAtFactionScore,
    string? Notes);

public sealed record WarObjectiveVersionDto(
    long FactionId,
    long WarId,
    int Version,
    WarObjectiveMode Mode,
    bool IsExplicit,
    int? StopAtFactionScore,
    string? Notes,
    string ChangedBy,
    DateTimeOffset CreatedAtUtc);
