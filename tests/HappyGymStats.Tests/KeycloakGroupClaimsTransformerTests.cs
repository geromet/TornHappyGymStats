using System.Security.Claims;
using HappyGymStats.Identity.Authentication;
using Xunit;

namespace HappyGymStats.Tests;

public class KeycloakGroupClaimsTransformerTests
{
    private static ClaimsPrincipal PrincipalInAdmins(string roleClaimType)
    {
        var identity = new ClaimsIdentity(
            claims: [new Claim("groups", "/admins")],
            authenticationType: "Test",
            nameType: "preferred_username",
            roleType: roleClaimType);

        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task MapsAdminsGroupOntoTheIdentitysOwnRoleClaimType()
    {
        // The Blazor host sets RoleClaimType = "roles". A claim added under a
        // hardcoded ClaimTypes.Role would be invisible to IsInRole here, which
        // is what hid the admin-only UI strip from a real administrator.
        var principal = await new KeycloakGroupClaimsTransformer()
            .TransformAsync(PrincipalInAdmins("roles"));

        Assert.True(principal.IsInRole(Roles.Admin));
        Assert.Contains(principal.Claims, c => c.Type == "roles" && c.Value == Roles.Admin);
    }

    [Fact]
    public async Task MapsAdminsGroupForHostsLeavingTheJwtBearerDefault()
    {
        var principal = await new KeycloakGroupClaimsTransformer()
            .TransformAsync(PrincipalInAdmins(ClaimTypes.Role));

        Assert.True(principal.IsInRole(Roles.Admin));
    }

    [Fact]
    public async Task IsIdempotentAcrossRepeatedRequests()
    {
        var transformer = new KeycloakGroupClaimsTransformer();
        var principal = PrincipalInAdmins("roles");

        await transformer.TransformAsync(principal);
        await transformer.TransformAsync(principal);

        Assert.Single(principal.Claims.Where(c => c.Type == "roles" && c.Value == Roles.Admin));
    }
}
