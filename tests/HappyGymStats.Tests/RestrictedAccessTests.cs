using System.Security.Claims;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HappyGymStats.Tests;

/// <summary>
/// Covers the opt-in admin-only gate used by the torndev.geromet.com dev host.
/// Two properties matter: it must be inert unless a deployment turns it on (so
/// production is unaffected), and when on it must not swallow the sign-in round
/// trip (or the host locks everyone out, admins included).
/// </summary>
public class RestrictedAccessTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static ClaimsPrincipal Principal(bool authenticated, params Claim[] claims)
    {
        var identity = authenticated
            ? new ClaimsIdentity(claims, authenticationType: "TestScheme")
            : new ClaimsIdentity(claims);
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void IsEnabled_is_false_when_key_absent()
    {
        Assert.False(RestrictedAccessExtensions.IsEnabled(Config()));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("1")]
    public void IsEnabled_is_true_for_supported_truthy_values(string value)
    {
        Assert.True(RestrictedAccessExtensions.IsEnabled(
            Config((RestrictedAccessExtensions.EnabledKey, value))));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("yes")]
    public void IsEnabled_is_false_for_everything_else(string value)
    {
        Assert.False(RestrictedAccessExtensions.IsEnabled(
            Config((RestrictedAccessExtensions.EnabledKey, value))));
    }

    [Fact]
    public void Anonymous_principal_is_never_an_administrator()
    {
        Assert.False(RestrictedAccessExtensions.IsAdministrator(null));
        Assert.False(RestrictedAccessExtensions.IsAdministrator(Principal(authenticated: false)));
        Assert.False(RestrictedAccessExtensions.IsAdministrator(
            Principal(authenticated: false, new Claim(ClaimTypes.Role, Roles.Admin))));
    }

    [Fact]
    public void Authenticated_non_admin_is_not_an_administrator()
    {
        var principal = Principal(authenticated: true,
            new Claim(ClaimTypes.Role, Roles.User),
            new Claim("groups", "/users"));

        Assert.False(RestrictedAccessExtensions.IsAdministrator(principal));
    }

    [Fact]
    public void Admin_role_claim_is_accepted()
    {
        var principal = Principal(authenticated: true, new Claim(ClaimTypes.Role, Roles.Admin));
        Assert.True(RestrictedAccessExtensions.IsAdministrator(principal));
    }

    [Fact]
    public void Keycloak_admins_group_is_accepted_without_a_claims_transformer()
    {
        // The Blazor host registers no IClaimsTransformation, so "/admins" is
        // never mapped onto a role there. Accepting the raw group claim is what
        // makes the gate work on that host.
        var principal = Principal(authenticated: true, new Claim("groups", "/admins"));
        Assert.True(RestrictedAccessExtensions.IsAdministrator(principal));
    }

    [Fact]
    public void Flat_roles_claim_is_accepted()
    {
        // Some Keycloak mappers emit a flat "roles" claim rather than mapping to
        // ClaimTypes.Role, which IsInRole would miss.
        var principal = Principal(authenticated: true, new Claim("roles", Roles.Admin));
        Assert.True(RestrictedAccessExtensions.IsAdministrator(principal));
    }

    [Theory]
    [InlineData("/signin-oidc")]
    [InlineData("/signout-callback-oidc")]
    [InlineData("/login")]
    [InlineData("/auth/login")]
    [InlineData("/auth/logout")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/MudBlazor/MudBlazor.min.css")]
    [InlineData("/css/app.css")]
    [InlineData("/favicon.png")]
    [InlineData("/not-found")]
    [InlineData("/health")]
    public void Sign_in_and_asset_paths_stay_reachable(string path)
    {
        // If any of these were gated, an anonymous visitor could never complete
        // sign-in, and a signed-in non-admin would bounce between the challenge
        // and the cookie handler's AccessDeniedPath forever.
        Assert.True(RestrictedAccessExtensions.IsAlwaysAllowed(new PathString(path)));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/war")]
    [InlineData("/my-stats")]
    [InlineData("/api/v1/torn/surfaces/latest")]
    public void Application_paths_are_gated(string path)
    {
        Assert.False(RestrictedAccessExtensions.IsAlwaysAllowed(new PathString(path)));
    }

    [Fact]
    public void Allowlist_matching_is_case_insensitive()
    {
        Assert.True(RestrictedAccessExtensions.IsAlwaysAllowed(new PathString("/Signin-Oidc")));
        Assert.True(RestrictedAccessExtensions.IsAlwaysAllowed(new PathString("/LOGIN")));
    }

    [Fact]
    public void Empty_path_is_not_treated_as_allowed()
    {
        Assert.False(RestrictedAccessExtensions.IsAlwaysAllowed(new PathString(null)));
    }
}
