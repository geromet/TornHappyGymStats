namespace HappyGymStats.Identity.Authentication;

/// <summary>
/// The single definition of which Keycloak group grants which application role.
/// </summary>
/// <remarks>
/// This exists because the mapping was previously written out in more than one
/// place. The API carried its own copy of the group switch and drifted from the
/// corrected one in <see cref="KeycloakGroupClaimsTransformer"/> — it enumerated
/// the claim list while mutating it and threw for every user who was actually in
/// a mapped group. <see cref="RestrictedAccessExtensions"/> separately repeated
/// the "/admins" literal, kept in step with a comment rather than the compiler.
///
/// Anything that needs to know about groups reads it from here. Changing a group
/// name is then one edit, and the hosts cannot disagree about what it means.
/// </remarks>
public static class KeycloakGroups
{
    /// <summary>The claim type Keycloak emits group membership under.</summary>
    public const string ClaimType = "groups";

    public const string Admins = "/admins";
    public const string FactionOwners = "/users/faction-owners";
    public const string Users = "/users";

    /// <summary>
    /// The application role a group grants, or <c>null</c> for a group this
    /// application does not care about.
    /// </summary>
    public static string? RoleFor(string? groupClaimValue) => groupClaimValue switch
    {
        Admins => Roles.Admin,
        FactionOwners => Roles.FactionOwner,
        Users => Roles.User,
        _ => null
    };
}
