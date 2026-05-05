using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Data;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/health")]
public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetHealth([FromServices] HappyGymStatsDbContext db, CancellationToken ct)
        => Ok(new HealthResponse(
            Status: await db.Database.CanConnectAsync(ct) ? "ok" : "degraded",
            Api: "HappyGymStats.Api",
            DatabaseProvider: db.Database.ProviderName ?? "unknown"));
}
