using HappyGymStats.AdminPanel.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.AdminPanel.Controllers;

[Route("admin/health")]
public sealed class AdminHealthController : AdminApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok", utcNow = DateTimeOffset.UtcNow });
}
