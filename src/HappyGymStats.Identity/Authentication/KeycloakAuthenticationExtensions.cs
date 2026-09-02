using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HappyGymStats.Identity.Authentication;

public static class KeycloakAuthenticationExtensions
{
    /// <summary>
    /// Registers Keycloak JWT-bearer validation using the <c>Keycloak:Authority</c>
    /// configuration key (overridable via the <c>Keycloak__Authority</c> environment variable).
    /// Fails fast at startup when the key is missing.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["Keycloak:Authority"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key: Keycloak:Authority. " +
                "Set it in appsettings.json or via the Keycloak__Authority environment variable.");

        return services.AddKeycloakAuthentication(authority);
    }

    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        string authority)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = "happygymstats-api";
                options.RequireHttpsMetadata = true;
            });

        services.AddAuthorization();

        return services;
    }
}
