using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace HappyGymStats.Identity.Authentication;

public class KeycloakGroupClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = (ClaimsIdentity)principal.Identity!;

        // Materialised: FindAll streams over the identity's claim list, and
        // AddClaim below mutates that same list mid-enumeration — which threw
        // "Collection was modified" for every user who was actually in a mapped
        // group, the only users this transformer exists for.
        foreach (var groupClaim in principal.FindAll(KeycloakGroups.ClaimType).ToList())
        {
            // The mapping itself lives in KeycloakGroups so no host can hold a
            // divergent copy of it. See that file for what went wrong before.
            var role = KeycloakGroups.RoleFor(groupClaim.Value);

            // Add the claim under THIS identity's role claim type, not a fixed
            // ClaimTypes.Role. The API and AdminPanel leave the JwtBearer
            // default (ClaimTypes.Role), but the Blazor host sets
            // TokenValidationParameters.RoleClaimType = "roles" — and
            // IsInRole/AuthorizeView only look at the identity's own role claim
            // type, so a hardcoded ClaimTypes.Role claim is invisible there.
            if (role is not null && !principal.IsInRole(role))
                identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }

        return Task.FromResult(principal);
    }
}
