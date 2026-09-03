using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Api.Controllers;

/// <summary>
/// Runtime UI feature switches.
///
/// Reading is anonymous: the frontend has to know whether to render the gym
/// point cloud before it knows who the visitor is, and the flags carry no
/// sensitive information. Writing requires the admin role.
/// </summary>
[Route("api/v1/ui-settings")]
public sealed class UiSettingsController(HappyGymStatsDbContext db) : ApiControllerBase
{
    /// <summary>Key for the gym point-cloud toggle. Absent means enabled.</summary>
    public const string GymPointCloudKey = "ui.gym-point-cloud.enabled";

    /// <summary>
    /// Switches the frontend may need. Every flag defaults to ENABLED when the
    /// row is absent, so an empty table behaves exactly like today and a failed
    /// read never blanks the site.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetUiSettings(CancellationToken ct)
    {
        var gymPointCloud = await ReadBoolAsync(GymPointCloudKey, defaultValue: true, ct);
        return Ok(new { gymPointCloudEnabled = gymPointCloud });
    }

    [HttpPut("gym-point-cloud")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> SetGymPointCloud([FromBody] SetFlagRequest request, CancellationToken ct)
    {
        if (request is null)
            return ApiError(StatusCodes.Status400BadRequest, "invalid_body", "Request body is required.");

        // Prefer the pseudonymous id; fall back to the username. A Torn player id
        // must never be written here.
        var actor = User.FindFirstValue(Claims.AnonymousId)
                    ?? User.Identity?.Name
                    ?? "unknown-admin";

        await UpsertAsync(GymPointCloudKey, request.Enabled ? "true" : "false", actor, ct);

        return Ok(new
        {
            gymPointCloudEnabled = request.Enabled,
            updatedBy = actor,
        });
    }

    public sealed record SetFlagRequest(bool Enabled);

    private async Task<bool> ReadBoolAsync(string key, bool defaultValue, CancellationToken ct)
    {
        var row = await db.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null)
            return defaultValue;

        return row.Value switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => defaultValue,
        };
    }

    private async Task UpsertAsync(string key, string value, string actor, CancellationToken ct)
    {
        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null)
        {
            db.AppSettings.Add(new AppSettingEntity
            {
                Key = key,
                Value = value,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = actor,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            existing.UpdatedBy = actor;
        }

        await db.SaveChangesAsync(ct);
    }
}
