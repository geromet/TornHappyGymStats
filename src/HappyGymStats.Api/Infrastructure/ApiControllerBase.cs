using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Infrastructure;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult ApiError(int statusCode, string code, string message, object? details = null)
    {
        var payload = new ErrorEnvelope(new ApiError(
            Code: code,
            Message: message,
            Details: details,
            RequestId: HttpContext.TraceIdentifier));

        return StatusCode(statusCode, payload);
    }

    protected IActionResult ValidationError(string message, object? details = null)
        => ApiError(StatusCodes.Status422UnprocessableEntity, "validation_failed", message, details);
}
