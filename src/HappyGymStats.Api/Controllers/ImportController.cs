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
    private const string StatusCapabilityCookie = "hgs-import-status";

    private readonly ImportOrchestrator _importService;
    private readonly IIdentityMapRepository _identityMapRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProvisionalTokenService _provisionalTokenService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ImportController> _logger;

    public ImportController(
        ImportOrchestrator importService,
        IIdentityMapRepository identityMapRepo,
        IUnitOfWork unitOfWork,
        IProvisionalTokenService provisionalTokenService,
        IWebHostEnvironment environment,
        ILogger<ImportController> logger)
    {
        _importService = importService;
        _identityMapRepo = identityMapRepo;
        _unitOfWork = unitOfWork;
        _provisionalTokenService = provisionalTokenService;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult StartImport([FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ImportRequest? request)
    {
        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ValidationError("apiKey is required.", new { field = "apiKey" });

        if (request?.Fresh != true)
        {
            return ValidationError(
                "Anonymous import requests must be fresh. Resume through the authenticated /me endpoint.",
                new { field = "fresh" });
        }

        var admission = _importService.TryEnqueueFresh(apiKey);
        if (!admission.Accepted)
            return ImportBusy();

        var status = admission.Status!;
        SetStatusCapability(status.AnonymousId);
        return StatusCode(StatusCodes.Status202Accepted, ToDto(status));
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

        var owner = await ResolveAuthenticatedOwnerAsync(ct);
        if (!owner.Success)
            return owner.Error!;

        var admission = _importService.TryEnqueueForAnonymousId(
            apiKey,
            owner.AnonymousId,
            request?.Fresh ?? false,
            owner.PublicKey);
        if (!admission.Accepted)
            return ImportBusy();

        var status = admission.Status!;
        _logger.LogInformation(
            "Authenticated import accepted: endpoint={Endpoint} statusCode={StatusCode} jobId={JobId} anonymousId={AnonymousId} outcome={Outcome}",
            "/api/v1/torn/import-jobs/me",
            StatusCodes.Status202Accepted,
            status.Id,
            owner.AnonymousId,
            status.Outcome);

        return StatusCode(StatusCodes.Status202Accepted, ToDto(status));
    }

    [HttpGet("latest")]
    public IActionResult GetLatestImport()
    {
        ImportJobStatus? status = null;

        var anonymousIdClaim = User.FindFirstValue(Claims.AnonymousId);
        if (Guid.TryParse(anonymousIdClaim, out var callerAnonymousId))
            status = _importService.GetLatestForAnonymousId(callerAnonymousId);

        if (status is null && Request.Cookies.TryGetValue(StatusCapabilityCookie, out var capability))
            status = _importService.GetLatestForCapability(capability);

        if (status is null)
            return ApiError(StatusCodes.Status404NotFound, "not_found", "No import has been started for this caller.");

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

        var admission = _importService.TryEnqueueFresh(apiKey, publicKey);
        if (!admission.Accepted)
            return ImportBusy();

        var status = admission.Status!;
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
        SetStatusCapability(status.AnonymousId);

        return StatusCode(StatusCodes.Status202Accepted, new
        {
            anonymousId = status.AnonymousId,
            provisionalToken,
            job = ToDto(status),
        });
    }

    private async Task<AuthenticatedOwnerResolution> ResolveAuthenticatedOwnerAsync(CancellationToken ct)
    {
        var anonymousIdClaim = User.FindFirstValue(Claims.AnonymousId);
        if (!Guid.TryParse(anonymousIdClaim, out var callerAnonymousId))
        {
            _logger.LogWarning("Authenticated import rejected: code={Code}", "invalid_anonymous_id_claim");
            return AuthenticatedOwnerResolution.Fail(
                ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity."));
        }

        var callerSub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(callerSub))
        {
            _logger.LogWarning("Authenticated import rejected: code={Code} anonymousId={AnonymousId}", "missing_subject_claim", callerAnonymousId);
            return AuthenticatedOwnerResolution.Fail(
                ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity."));
        }

        var map = await _identityMapRepo.GetByAnonymousIdAsync(callerAnonymousId, ct);
        if (map is null)
        {
            _logger.LogWarning("Authenticated import rejected: code={Code} anonymousId={AnonymousId}", "identity_map_missing", callerAnonymousId);
            return AuthenticatedOwnerResolution.Fail(
                ApiError(StatusCodes.Status409Conflict, "identity_setup_required", "Identity map record is missing. Re-link your account and try again."));
        }

        if (!string.Equals(map.KeycloakSub, callerSub, StringComparison.Ordinal))
        {
            _logger.LogWarning("Authenticated import rejected: code={Code} anonymousId={AnonymousId}", "identity_map_subject_mismatch", callerAnonymousId);
            return AuthenticatedOwnerResolution.Fail(
                ApiError(StatusCodes.Status403Forbidden, "forbidden", "Caller identity does not match the mapped owner."));
        }

        return AuthenticatedOwnerResolution.Ok(callerAnonymousId, map.PublicKey);
    }

    private void SetStatusCapability(Guid anonymousId)
    {
        var capability = _importService.IssueStatusCapability(anonymousId);
        Response.Cookies.Append(StatusCapabilityCookie, capability, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsEnvironment("Testing"),
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromHours(24),
            IsEssential = true,
        });
    }

    private IActionResult ImportBusy()
        => ApiError(
            StatusCodes.Status409Conflict,
            "import_busy",
            "Another import is already running. Try again shortly.");

    private static ImportStatusDto ToDto(ImportJobStatus s)
        => new(s.Id, s.Outcome, s.StartedAtUtc, s.CompletedAtUtc,
            s.PagesFetched, s.LogsFetched, s.LogsAppended, s.ErrorMessage);

    private sealed record AuthenticatedOwnerResolution(
        bool Success,
        Guid AnonymousId,
        byte[]? PublicKey,
        IActionResult? Error)
    {
        public static AuthenticatedOwnerResolution Ok(Guid anonymousId, byte[]? publicKey)
            => new(true, anonymousId, publicKey, null);

        public static AuthenticatedOwnerResolution Fail(IActionResult error)
            => new(false, Guid.Empty, null, error);
    }
}
