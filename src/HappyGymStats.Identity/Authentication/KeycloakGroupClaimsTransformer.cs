using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace HappyGymStats.Identity.Authentication;

public class KeycloakGroupClaimsTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = (ClaimsIdentity)principal.Identity!;

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

        return Task.FromResult(principal);
    }
}
