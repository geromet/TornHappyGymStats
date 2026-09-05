using System.Security.Claims;
using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Api.Services;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HappyGymStats.Api.Controllers;

[Route("api/v1/account/connections/torn")]
[Authorize(Roles = Roles.User)]
public sealed class AccountConnectionsController : ApiControllerBase
{
    private readonly IAccountConnectionService _connections;

    public AccountConnectionsController(IAccountConnectionService connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        if (!TryResolveOwner(out var anonymousId))
        {
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");
        }

        var result = await _connections.GetStatusAsync(anonymousId, cancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    [HttpPost]
    public async Task<IActionResult> Connect([FromBody] ConnectTornConnectionRequest request, CancellationToken cancellationToken)
    {
        if (!TryResolveOwner(out var anonymousId))
        {
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");
        }

        var result = await _connections
            .ConnectAsync(anonymousId, request.TornApiKey, request.ConsentAccepted, cancellationToken)
            .ConfigureAwait(false);
        return MapResult(result);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        if (!TryResolveOwner(out var anonymousId))
        {
            return ApiError(StatusCodes.Status401Unauthorized, "unauthorized", "Could not resolve caller identity.");
        }

        var result = await _connections.RevokeAsync(anonymousId, cancellationToken).ConfigureAwait(false);
        return MapResult(result);
    }

    private bool TryResolveOwner(out Guid anonymousId)
    {
        var raw = User.FindFirstValue(Claims.AnonymousId);
        return Guid.TryParse(raw, out anonymousId) && anonymousId != Guid.Empty;
    }

    private IActionResult MapResult(AccountConnectionOperationResult result)
        => result.Status switch
        {
            AccountConnectionOperationStatus.Success when result.Connection is not null => Ok(ToDto(result.Connection)),
            AccountConnectionOperationStatus.OwnerNotFound => ApiError(StatusCodes.Status409Conflict, "identity_setup_required", "The signed-in account is not ready for a Torn connection yet."),
            AccountConnectionOperationStatus.ConsentRequired => ApiError(StatusCodes.Status422UnprocessableEntity, "consent_required", "Accept the current Torn connection privacy notice before connecting."),
            AccountConnectionOperationStatus.InvalidTornApiKey => ApiError(StatusCodes.Status422UnprocessableEntity, "invalid_torn_api_key", "Torn could not validate this Torn API key."),
            AccountConnectionOperationStatus.TornUnavailable => ApiError(StatusCodes.Status503ServiceUnavailable, "torn_unavailable", "Torn could not validate the Torn API key right now. Try again later."),
            AccountConnectionOperationStatus.KeyVaultUnavailable => ApiError(StatusCodes.Status503ServiceUnavailable, "key_vault_unavailable", "Secure Torn API key storage is temporarily unavailable."),
            AccountConnectionOperationStatus.NotConnected => ApiError(StatusCodes.Status409Conflict, "not_connected", "No Torn connection exists for this account."),
            _ => ApiError(StatusCodes.Status500InternalServerError, "unexpected_error", "The Torn connection request could not be completed."),
        };

    private static TornConnectionStatusDto ToDto(AccountConnectionSnapshot state)
        => new(
            state.Connected ? "connected" : "not_connected",
            state.TornPlayerId,
            state.StoredAtUtc,
            state.Consent is null
                ? null
                : new TornConsentStatusDto(state.Consent.DocumentVersion, state.Consent.Purpose, state.Consent.AcceptedAtUtc));
}

public sealed record ConnectTornConnectionRequest(string? TornApiKey, bool ConsentAccepted);

public sealed record TornConnectionStatusDto(
    string State,
    int? TornPlayerId,
    DateTimeOffset? StoredAtUtc,
    TornConsentStatusDto? Consent);

public sealed record TornConsentStatusDto(string DocumentVersion, string Purpose, DateTimeOffset AcceptedAtUtc);
