using System.Security.Claims;
using System.Security.Cryptography;
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

    [HttpGet("me")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var anonymousIdClaim = User.FindFirstValue(Claims.AnonymousId);
        if (!Guid.TryParse(anonymousIdClaim, out var anonymousId))
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");

        var entry = await _identityMapRepo.GetByAnonymousIdAsync(anonymousId, ct);
        if (entry is null)
            return ApiError(StatusCodes.Status404NotFound, "not_found", "Identity record not found.");

        return Ok(new
        {
            anonymousId = entry.AnonymousId,
            hasPublicKey = entry.PublicKey is not null,
            encryptedTornPlayerIdBase64 = entry.EncryptedTornPlayerId is not null
                ? Convert.ToBase64String(entry.EncryptedTornPlayerId)
                : null,
        });
    }

    [HttpPut("public-key")]
    [Authorize(Roles = Roles.User)]
    public async Task<IActionResult> StorePublicKey([FromBody] StorePublicKeyRequest request, CancellationToken ct)
    {
        var anonymousIdClaim = User.FindFirstValue(Claims.AnonymousId);
        if (!Guid.TryParse(anonymousIdClaim, out var anonymousId))
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(request.PublicKey);
            using var ecdh = ECDiffieHellman.Create();
            ecdh.ImportSubjectPublicKeyInfo(spki, out _);
        }
        catch
        {
            return ApiError(StatusCodes.Status422UnprocessableEntity, "validation_failed", "PublicKey is not a valid base64-encoded P-256 SPKI key.");
        }

        await _identityMapRepo.StorePublicKeyAsync(anonymousId, spki, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return NoContent();
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
public sealed record StorePublicKeyRequest(string PublicKey);
