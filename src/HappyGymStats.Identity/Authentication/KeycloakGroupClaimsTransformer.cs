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
        foreach (var groupClaim in principal.FindAll("groups").ToList())
        {
            var role = groupClaim.Value switch
            {
                "/admins" => Roles.Admin,
                "/users/faction-owners" => Roles.FactionOwner,
                "/users" => Roles.User,
                _ => null
            };

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
