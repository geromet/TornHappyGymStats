using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Import;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/import-jobs")]
public sealed class ImportController : ApiControllerBase
{
    private readonly ImportOrchestrator _importService;

    public ImportController(ImportOrchestrator importService) => _importService = importService;

    [HttpPost]
    public IActionResult StartImport([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ImportRequest? request)
    {
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ValidationError("apiKey is required.", new { field = "apiKey" });

        var status = _importService.Enqueue(apiKey, request?.Fresh ?? false);
        var statusCode = status.IsTerminal ? StatusCodes.Status200OK : StatusCodes.Status202Accepted;

        return StatusCode(statusCode, ToDto(status));
    }

    [HttpGet("latest")]
    public IActionResult GetLatestImport()
    {
        var status = _importService.Latest;
        if (status is null)
            return ApiError(StatusCodes.Status404NotFound, "not_found", "No import has been started.");

        return Ok(ToDto(status));
    }

    private static ImportStatusDto ToDto(ImportJobStatus s)
        => new(s.Id, s.Outcome, s.StartedAtUtc, s.CompletedAtUtc,
            s.PagesFetched, s.LogsFetched, s.LogsAppended, s.ErrorMessage);
}
