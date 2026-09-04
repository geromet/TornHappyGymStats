using System.Linq;
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

    [Fact]
    public async Task MapsEveryGroupThroughTheSharedTable()
    {
        // If these ever disagree with KeycloakGroups.RoleFor, the mapping has been
        // duplicated again somewhere.
        foreach (var (group, role) in new[]
                 {
                     (KeycloakGroups.Admins, Roles.Admin),
                     (KeycloakGroups.FactionOwners, Roles.FactionOwner),
                     (KeycloakGroups.Users, Roles.User),
                 })
        {
            var identity = new ClaimsIdentity(
                [new Claim(KeycloakGroups.ClaimType, group)], "Test", "preferred_username", ClaimTypes.Role);
            var principal = await new KeycloakGroupClaimsTransformer()
                .TransformAsync(new ClaimsPrincipal(identity));

            Assert.True(principal.IsInRole(role), $"{group} did not grant {role}");
            Assert.Equal(role, KeycloakGroups.RoleFor(group));
        }
    }

    [Fact]
    public async Task MapsSeveralGroupsOnOnePrincipal()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(KeycloakGroups.ClaimType, KeycloakGroups.Admins),
                new Claim(KeycloakGroups.ClaimType, KeycloakGroups.Users),
            ],
            "Test", "preferred_username", "roles");

        var principal = await new KeycloakGroupClaimsTransformer()
            .TransformAsync(new ClaimsPrincipal(identity));

        Assert.True(principal.IsInRole(Roles.Admin));
        Assert.True(principal.IsInRole(Roles.User));
    }

    [Fact]
    public async Task IsIdempotentAcrossRepeatedTransformations()
    {
        // IClaimsTransformation runs once per authenticated request, but nothing
        // guarantees a principal is transformed only once — a composite or a
        // re-authentication can call it again, and duplicated role claims would
        // accumulate silently.
        var principal = PrincipalInAdmins("roles");
        var sut = new KeycloakGroupClaimsTransformer();

        await sut.TransformAsync(principal);
        await sut.TransformAsync(principal);
        await sut.TransformAsync(principal);

        Assert.Single(principal.Claims.Where(c => c.Type == "roles" && c.Value == Roles.Admin));
    }

    [Fact]
    public void UnknownGroupsGrantNothing()
    {
        Assert.Null(KeycloakGroups.RoleFor("/some-other-group"));
        Assert.Null(KeycloakGroups.RoleFor(null));
        Assert.Null(KeycloakGroups.RoleFor(""));
    }

    [Fact]
    public void RestrictedAccessAdminGroupIsTheSameConstant()
    {
        // These were two copies of "/admins" kept in step by a comment.
        Assert.Equal(KeycloakGroups.Admins, RestrictedAccessExtensions.AdminGroupClaimValue);
    }
}
