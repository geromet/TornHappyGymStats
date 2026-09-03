using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HappyGymStats.Identity.Authentication;

public static class KeycloakAuthenticationExtensions
{
    /// <summary>Audience the production API expects in the access token.</summary>
    public const string DefaultAudience = "happygymstats-api";

    /// <summary>
    /// Registers Keycloak JWT-bearer validation using the <c>Keycloak:Authority</c>
    /// configuration key (overridable via the <c>Keycloak__Authority</c> environment variable).
    /// Fails fast at startup when the key is missing.
    ///
    /// The accepted audience comes from <c>Keycloak:Audience</c> and defaults to
    /// <see cref="DefaultAudience"/>. The dev host sets <c>Keycloak__Audience=happygymstats-api-dev</c>
    /// so an access token minted for dev is rejected by the production API and
    /// vice versa — same realm, same issuer, different audience.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["Keycloak:Authority"]
            ?? throw new InvalidOperationException(
                "Missing required configuration key: Keycloak:Authority. " +
                "Set it in appsettings.json or via the Keycloak__Authority environment variable.");

        var audience = configuration["Keycloak:Audience"] ?? DefaultAudience;

        return services.AddKeycloakAuthentication(authority, audience);
    }

    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        string authority,
        string audience = DefaultAudience)
    {
        if (string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException(
                "Keycloak:Audience was set to an empty value. Leave it unset for " +
                $"'{DefaultAudience}', or set it to the audience this deployment expects.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
            });

        services.AddAuthorization();

        return services;
    }
}
