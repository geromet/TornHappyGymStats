using HappyGymStats.AdminPanel.Infrastructure;
using HappyGymStats.Data;
using HappyGymStats.Identity.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Auth: Keycloak JWT validation + group→role mapping only.
// IClaimsTransformation is intentionally NOT enriched with anonymous_id — admins cannot
// resolve AnonymousId → player identity.
builder.Services.AddKeycloakAuthentication("https://auth.geromet.com/realms/torn");
builder.Services.AddScoped<IClaimsTransformation, KeycloakGroupClaimsTransformer>();

// All endpoints require admin role by default; health is [AllowAnonymous].
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(Roles.Admin)
        .Build();
});

builder.Services.AddControllers();

var connectionString = AdminAppConfiguration.ResolveConnectionString(builder.Configuration);

// Read-only: no change tracking needed.
builder.Services.AddDbContext<HappyGymStatsDbContext>(options =>
    options.UseNpgsql(connectionString)
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

// IdentityMap is deliberately NOT registered — admin cannot resolve AnonymousId → player.

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
