using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HappyGymStats.Identity.Authentication;

public static class DevelopmentAuthenticationExtensions
{
    public const string SchemeName = "DevelopmentHeader";
    public const string EnabledKey = "HAPPYGYMSTATS_DEV_AUTH";
    public const string UserHeaderName = "X-Hgs-Dev-User";
    public const string RoleHeaderName = "X-Hgs-Dev-Roles";

    public static bool IsEnabled(IConfiguration configuration)
        => string.Equals(configuration[EnabledKey], "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(configuration[EnabledKey], "true", StringComparison.OrdinalIgnoreCase);

    public static void ValidateCanEnable(IWebHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                $"{EnabledKey} cannot be enabled in Production. This development authentication bypass is only valid for local validation hosts.");
        }
    }

    public static IServiceCollection AddDevelopmentHeaderAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentHeaderAuthenticationHandler>(SchemeName, _ => { });

        services.AddAuthorization();
        return services;
    }
}

public sealed class DevelopmentHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Logger.LogWarning(
            "Development authentication bypass active for {Method} {Path}. Never enable {Flag} in production.",
            Request.Method,
            Request.Path,
            DevelopmentAuthenticationExtensions.EnabledKey);

        var userName = Request.Headers[DevelopmentAuthenticationExtensions.UserHeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = "dev-war-planner";
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userName),
            new(ClaimTypes.Name, userName),
            new("preferred_username", userName),
            new(ClaimTypes.Role, Roles.User)
        };

        var roleHeader = Request.Headers[DevelopmentAuthenticationExtensions.RoleHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(roleHeader))
        {
            foreach (var role in roleHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
