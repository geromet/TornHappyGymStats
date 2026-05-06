using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Identity.Authentication;
using HappyGymStats.Identity.Provisional;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/identity")]
public sealed class IdentityController : ApiControllerBase
{
    private readonly IIdentityMapRepository _identityMapRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProvisionalTokenService _provisionalTokenService;

    public IdentityController(
        IIdentityMapRepository identityMapRepo,
        IUnitOfWork unitOfWork,
        IProvisionalTokenService provisionalTokenService)
    {
        _identityMapRepo = identityMapRepo;
        _unitOfWork = unitOfWork;
        _provisionalTokenService = provisionalTokenService;
    }

    [HttpPost("claim-provisional")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> ClaimProvisional([FromBody] ClaimProvisionalRequest request, CancellationToken ct)
    {
        var keycloakSub = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(keycloakSub))
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");

        var anonymousId = _provisionalTokenService.Validate(request.ProvisionalToken);
        if (anonymousId is null)
            return ApiError(StatusCodes.Status400BadRequest, "invalid_token", "Provisional token is invalid or expired.");

        var existing = await _identityMapRepo.GetByKeycloakSubAsync(keycloakSub, ct);
        if (existing is not null)
            return ApiError(StatusCodes.Status409Conflict, "already_linked", "This account is already linked to an anonymous ID.");

        var claimed = await _identityMapRepo.ClaimProvisionalAsync(anonymousId.Value, keycloakSub, ct);
        if (!claimed)
            return ApiError(StatusCodes.Status409Conflict, "not_claimable", "Provisional token has already been claimed or does not exist.");

        await _unitOfWork.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record ClaimProvisionalRequest(string ProvisionalToken);
