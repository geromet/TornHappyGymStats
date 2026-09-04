using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.Identity.Authentication;

/// <summary>
/// Opt-in whole-site gate that restricts a deployment to administrators only.
/// Built for the torndev.geromet.com dev host, where the whole site must be
/// private but the build has to stay byte-identical to production.
///
/// Deliberately middleware rather than <c>AuthorizationOptions.FallbackPolicy</c>:
/// a fallback policy also captures the OIDC callback, the static-asset endpoints
/// and the cookie handler's AccessDeniedPath ("/login"), so a signed-in
/// non-admin would bounce login -> denied -> login forever.
/// </summary>
public static class RestrictedAccessExtensions
{
    /// <summary>Config key. Absent or "0"/"false" leaves the pipeline untouched.</summary>
    public const string EnabledKey = "Access:RestrictToAdmins";

    /// <summary>Keycloak group that maps to admin.</summary>
    /// <remarks>
    /// An alias for <see cref="KeycloakGroups.Admins"/>, kept so existing callers
    /// still compile. It used to be a second copy of the literal held in step with
    /// a comment; the value now comes from the one place that defines it.
    /// </remarks>
    public const string AdminGroupClaimValue = KeycloakGroups.Admins;

    /// <summary>
    /// Paths that must stay reachable for an anonymous visitor, or the sign-in
    /// round trip cannot complete. Prefix match, case-insensitive.
    /// </summary>
    public static readonly string[] AlwaysAllowedPathPrefixes =
    [
        "/login",
        "/auth/",
        "/signin-oidc",
        "/signout-callback-oidc",
        "/signout-oidc",
        "/not-found",
        "/Error",
        "/health",
        "/_framework/",
        "/_content/",
        "/_vs/",
        "/css/",
        "/js/",
        "/lib/",
        "/images/",
        "/favicon",
        "/apple-touch-icon",
        "/manifest.json",
        "/robots.txt",
        "/service-worker.js"
    ];

    public static bool IsEnabled(IConfiguration configuration)
        => string.Equals(configuration[EnabledKey], "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(configuration[EnabledKey], "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsAlwaysAllowed(PathString path)
    {
        if (!path.HasValue)
            return false;

        foreach (var prefix in AlwaysAllowedPathPrefixes)
        {
            if (path.Value!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the principal is an admin.
    ///
    /// Checks the role claim AND the raw Keycloak "groups" claim, and also a flat
    /// "roles" claim, so the gate holds without assuming a particular realm
    /// protocol-mapper setup.
    ///
    /// All three hosts — API, AdminPanel and Blazor — now register
    /// <see cref="KeycloakGroupClaimsTransformer"/>, so the role claim alone would
    /// usually suffice. The other two checks stay because this is the gate that
    /// decides whether a stranger reaches the site at all: it should not depend on
    /// a transformer having run, and it must keep working if a realm is configured
    /// to emit roles directly. (This comment previously said the Blazor host
    /// registered no transformer. That stopped being true and nothing caught it,
    /// which is the argument for the mapping living in one place.)
    /// </summary>
    public static bool IsAdministrator(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        if (principal.IsInRole(Roles.Admin))
            return true;

        foreach (var claim in principal.FindAll("groups"))
        {
            if (string.Equals(claim.Value, AdminGroupClaimValue, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Some Keycloak mappers emit roles as a flat "roles" claim rather than
        // mapping onto ClaimTypes.Role, which IsInRole would miss.
        foreach (var claim in principal.FindAll("roles"))
        {
            if (string.Equals(claim.Value, Roles.Admin, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Registers the gate when <see cref="EnabledKey"/> is set. Must run after
    /// UseAuthentication so the principal exists, and before UseAntiforgery so a
    /// rejected request never reaches component rendering.
    /// </summary>
    public static IApplicationBuilder UseAdminOnlyAccessWhenConfigured(this IApplicationBuilder app, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
            return app;

        return app.Use(async (context, next) =>
        {
            if (IsAlwaysAllowed(context.Request.Path) || IsAdministrator(context.User))
            {
                await next(context);
                return;
            }

            var logger = context.RequestServices
                .GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            logger?.CreateLogger(typeof(RestrictedAccessExtensions).FullName!)
                .LogInformation(
                    "Admin-only access gate denied {Method} {Path} (authenticated={Authenticated}).",
                    context.Request.Method,
                    context.Request.Path,
                    context.User?.Identity?.IsAuthenticated == true);

            if (context.User?.Identity?.IsAuthenticated != true)
            {
                // Anonymous: send them through the normal sign-in round trip.
                await context.ChallengeAsync();
                return;
            }

            // Signed in but not an admin. 403 rather than a redirect, so this can
            // never loop through the cookie handler's AccessDeniedPath.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "This deployment is restricted to administrators.");
        });
    }
}
