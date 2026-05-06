using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.AdminPanel.Infrastructure;

[ApiController]
public abstract class AdminApiControllerBase : ControllerBase
{
    protected IActionResult ApiError(int statusCode, string code, string message)
        => StatusCode(statusCode, new { error = new { code, message } });

    protected static int ClampLimit(int? limit, int max = 200)
        => Math.Clamp(limit ?? 50, 1, max);
}
