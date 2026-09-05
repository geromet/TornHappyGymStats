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
        => Ok(ToDto(await objectives.GetEffectiveAsync(factionId, warId, ct)));

    [HttpGet("{factionId:long}/{warId:long}/evaluation")]
    public async Task<IActionResult> GetEvaluation(
        long factionId,
        long warId,
        [FromQuery] int factionScore,
        CancellationToken ct)
    {
        if (factionScore < 0)
        {
            return ApiError(
                StatusCodes.Status400BadRequest,
                "invalid_faction_score",
                "Faction score cannot be negative.");
        }

        var current = await objectives.GetEffectiveAsync(factionId, warId, ct);
        var evaluation = WarObjectiveEvaluator.Evaluate(current.Objective, factionScore);
        return Ok(new WarObjectiveEvaluationDto(
            ToDto(current),
            factionScore,
            evaluation.RecommendationsAllowed,
            evaluation.StopReason));
    }

    [HttpGet("{factionId:long}/{warId:long}/history")]
    public async Task<IActionResult> GetHistory(long factionId, long warId, CancellationToken ct)
        => Ok((await objectives.GetHistoryAsync(factionId, warId, ct)).Select(ToDto));

    // The current identity model grants faction-owner as a global role and does not
    // carry a claim that binds that role to the client-supplied Torn faction id.
    // Until that binding exists, accepting faction-owner here would let one owner
    // target another faction simply by changing request.FactionId. Keep mutation
    // admin-only rather than pretending the role itself establishes data scope.
    [Authorize(Roles = Roles.Admin)]
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

public sealed record WarObjectiveEvaluationDto(
    WarObjectiveVersionDto Objective,
    int FactionScore,
    bool RecommendationsAllowed,
    string? StopReason);
