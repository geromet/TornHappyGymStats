using System.Security.Claims;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace HappyGymStats.Api.Infrastructure;

/// <summary>
/// Single IClaimsTransformation that handles both Keycloak group→role mapping
/// and IdentityMap anonymous_id enrichment for authenticated users.
/// </summary>
public sealed class HappyGymStatsClaimsTransformer(IIdentityMapRepository identityMapRepo)
    : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = (ClaimsIdentity)principal.Identity!;

        // Keycloak group → ASP.NET role
        foreach (var groupClaim in principal.FindAll("groups"))
        {
            var role = groupClaim.Value switch
            {
                "/admins" => Roles.Admin,
                "/users/faction-owners" => Roles.FactionOwner,
                "/users" => Roles.User,
                _ => null
            };
            if (role is not null && !principal.IsInRole(role))
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

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
