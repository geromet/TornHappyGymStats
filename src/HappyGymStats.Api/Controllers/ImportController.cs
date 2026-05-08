using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Import;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using HappyGymStats.Identity.Provisional;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/import-jobs")]
public sealed class ImportController : ApiControllerBase
{
    private readonly ImportOrchestrator _importService;
    private readonly IIdentityMapRepository _identityMapRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProvisionalTokenService _provisionalTokenService;

    public ImportController(
        ImportOrchestrator importService,
        IIdentityMapRepository identityMapRepo,
        IUnitOfWork unitOfWork,
        IProvisionalTokenService provisionalTokenService)
    {
        _importService = importService;
        _identityMapRepo = identityMapRepo;
        _unitOfWork = unitOfWork;
        _provisionalTokenService = provisionalTokenService;
    }

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

    [HttpPost("anonymous")]
    public async Task<IActionResult> StartAnonymousImport(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ImportRequest? request,
        CancellationToken ct)
    {
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ValidationError("apiKey is required.", new { field = "apiKey" });

        byte[]? publicKey = null;
        if (!string.IsNullOrEmpty(request?.PublicKey))
        {
            try { publicKey = Convert.FromBase64String(request.PublicKey); }
            catch (FormatException)
            {
                return ValidationError("publicKey must be a valid base64 string.", new { field = "publicKey" });
            }
        }

        var status = _importService.Enqueue(apiKey, fresh: true, publicKey);

        await _identityMapRepo.CreateAsync(new IdentityMapEntity
        {
            AnonymousId = status.AnonymousId,
            IsProvisional = true,
            CreatedAtUtc = status.StartedAtUtc,
            ExpiresAtUtc = status.StartedAtUtc.AddHours(24),
            PublicKey = publicKey,
        }, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var provisionalToken = _provisionalTokenService.Issue(status.AnonymousId);

        return StatusCode(StatusCodes.Status202Accepted, new
        {
            anonymousId = status.AnonymousId,
            provisionalToken,
            job = ToDto(status),
        });
    }

    private static ImportStatusDto ToDto(ImportJobStatus s)
        => new(s.Id, s.Outcome, s.StartedAtUtc, s.CompletedAtUtc,
            s.PagesFetched, s.LogsFetched, s.LogsAppended, s.ErrorMessage);
}
