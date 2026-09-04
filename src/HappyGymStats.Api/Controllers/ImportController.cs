using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.Import;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using HappyGymStats.Identity.Authentication;
using HappyGymStats.Identity.Provisional;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/torn/import-jobs")]
public sealed class ImportController : ApiControllerBase
{
    private readonly ImportOrchestrator _importService;
    private readonly IIdentityMapRepository _identityMapRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProvisionalTokenService _provisionalTokenService;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        ImportOrchestrator importService,
        IIdentityMapRepository identityMapRepo,
        IUnitOfWork unitOfWork,
        IProvisionalTokenService provisionalTokenService,
        ILogger<ImportController> logger)
    {
        _importService = importService;
        _identityMapRepo = identityMapRepo;
        _unitOfWork = unitOfWork;
        _provisionalTokenService = provisionalTokenService;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult StartImport([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ImportRequest? request)
    {
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ValidationError("apiKey is required.", new { field = "apiKey" });

        var status = _importService.Enqueue(apiKey, request?.Fresh ?? false);
        if (IsBusy(status))
            return BusyImportResponse();

        var statusCode = status.IsTerminal ? StatusCodes.Status200OK : StatusCodes.Status202Accepted;
        return StatusCode(statusCode, ToDto(status));
    }

    [HttpPost("me")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> StartMyImport(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ImportRequest? request,
        CancellationToken ct)
    {
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ValidationError("apiKey is required.", new { field = "apiKey" });

        var anonymousIdClaim = User.FindFirstValue(Claims.AnonymousId);
        if (!Guid.TryParse(anonymousIdClaim, out var callerAnonymousId))
        {
            _logger.LogWarning("Authenticated import rejected: endpoint={Endpoint} code={Code}", "/api/v1/torn/import-jobs/me", "invalid_anonymous_id_claim");
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");
        }

        var callerSub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(callerSub))
        {
            _logger.LogWarning("Authenticated import rejected: endpoint={Endpoint} code={Code} anonymousId={AnonymousId}", "/api/v1/torn/import-jobs/me", "missing_subject_claim", callerAnonymousId);
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");
        }

        var map = await _identityMapRepo.GetByAnonymousIdAsync(callerAnonymousId, ct);
        if (map is null)
        {
            _logger.LogWarning("Authenticated import rejected: endpoint={Endpoint} code={Code} anonymousId={AnonymousId}", "/api/v1/torn/import-jobs/me", "identity_map_missing", callerAnonymousId);
            return ApiError(StatusCodes.Status409Conflict, "identity_setup_required", "Identity map record is missing. Re-link your account and try again.");
        }

        if (!string.Equals(map.KeycloakSub, callerSub, StringComparison.Ordinal))
        {
            _logger.LogWarning("Authenticated import rejected: endpoint={Endpoint} code={Code} anonymousId={AnonymousId}", "/api/v1/torn/import-jobs/me", "identity_map_subject_mismatch", callerAnonymousId);
            return ApiError(StatusCodes.Status403Forbidden, "forbidden", "Caller identity does not match the mapped owner.");
        }

        var status = _importService.EnqueueForAnonymousId(apiKey, callerAnonymousId, map.PublicKey);
        if (IsBusy(status))
            return BusyImportResponse();

        var statusCode = status.IsTerminal ? StatusCodes.Status200OK : StatusCodes.Status202Accepted;

        _logger.LogInformation(
            "Authenticated import accepted: endpoint={Endpoint} statusCode={StatusCode} jobId={JobId} anonymousId={AnonymousId} outcome={Outcome}",
            "/api/v1/torn/import-jobs/me",
            statusCode,
            status.Id,
            callerAnonymousId,
            status.Outcome);

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
        if (IsBusy(status))
            return BusyImportResponse();

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

    private IActionResult BusyImportResponse()
        => ApiError(StatusCodes.Status409Conflict, "import_busy", "Another import is already in progress.");

    private static bool IsBusy(ImportJobStatus status)
        => string.Equals(status.Outcome, "busy", StringComparison.Ordinal);

    private static ImportStatusDto ToDto(ImportJobStatus s)
        => new(s.Id, s.Outcome, s.StartedAtUtc, s.CompletedAtUtc,
            s.PagesFetched, s.LogsFetched, s.LogsAppended, s.ErrorMessage);
}
