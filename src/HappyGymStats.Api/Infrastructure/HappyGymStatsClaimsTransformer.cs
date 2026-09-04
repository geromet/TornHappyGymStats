using System.Security.Claims;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace HappyGymStats.Api.Infrastructure;

/// <summary>
/// Single IClaimsTransformation that handles both Keycloak group→role mapping
/// and IdentityMap anonymous_id enrichment for authenticated users.
/// </summary>
/// <remarks>
/// The group→role half is <b>delegated</b>, not reimplemented. This class used to
/// carry its own copy, and the copy predated both corrections since made to
/// <see cref="KeycloakGroupClaimsTransformer"/>: it enumerated the claim list while
/// mutating it, and it wrote the role under a hardcoded ClaimTypes.Role. The API is
/// the host serving the war board and was the only host still running the
/// uncorrected version, so a signed-in administrator got an
/// InvalidOperationException instead of a principal — which also meant the
/// AnonymousId enrichment below never ran for exactly the users who had one.
///
/// Only the enrichment is this transformer's own work. Keep it that way: if the
/// group mapping needs to change, change it in Identity, where three hosts and a
/// test suite already depend on it.
/// </remarks>
public sealed class HappyGymStatsClaimsTransformer(IIdentityMapRepository identityMapRepo)
    : IClaimsTransformation
{
    private static readonly KeycloakGroupClaimsTransformer GroupRoles = new();

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        await GroupRoles.TransformAsync(principal);

        var identity = (ClaimsIdentity)principal.Identity!;

        // Enrich with AnonymousId from IdentityMap for linked accounts
        if (principal.Identity?.IsAuthenticated == true
            && principal.FindFirst(Claims.AnonymousId) is null)
        {
            var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(sub))
            {
                var entry = await identityMapRepo.GetByKeycloakSubAsync(sub, CancellationToken.None);
                if (entry is { IsProvisional: false })
                    identity.AddClaim(new Claim(Claims.AnonymousId, entry.AnonymousId.ToString()));
            }
        }

        return principal;
    }
}
