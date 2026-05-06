using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace HappyGymStats.Identity.Authentication;

public static class KeycloakAuthenticationExtensions
{
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
        services.AddTransient<IClaimsTransformation, KeycloakGroupClaimsTransformer>();

        return services;
    }
}
