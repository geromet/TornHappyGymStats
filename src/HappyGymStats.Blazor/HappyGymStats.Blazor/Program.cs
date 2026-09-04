using HappyGymStats.Blazor.Components;
using HappyGymStats.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using HappyGymStats.Identity.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using System.Security.Claims;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;

namespace HappyGymStats.Blazor;

/// <summary>
/// Blazor host entry point. Deliberately namespaced (not top-level statements) so the
/// assembly's entry type cannot collide with the API's global-namespace <c>Program</c>
/// in projects that reference both hosts (the test project). This removes the need for
/// the load-bearing <c>extern alias</c> on the Blazor project reference.
/// </summary>
public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();

        var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            builder.Services
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
                .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "HappyGymStats.Blazor");
        }

        builder.Services.AddMudServices();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IServerAccessTokenProvider, ServerAccessTokenProvider>();
        builder.Services.AddTransient<AccessTokenForwardingHandler>();

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireRole", policy => policy.RequireRole("hgs-user"));
        });

        var startupClientSecretMissing = false;
        string? startupClientId = null;

        var developmentAuthEnabled = DevelopmentAuthenticationExtensions.IsEnabled(builder.Configuration);
        if (developmentAuthEnabled)
        {
            DevelopmentAuthenticationExtensions.ValidateCanEnable(builder.Environment);
            builder.Services.AddDevelopmentHeaderAuthentication();
        }
        else
        {
            var keycloakSection = builder.Configuration.GetSection("Keycloak");
            var keycloakAuthority = keycloakSection["Authority"]
                ?? throw new InvalidOperationException("Missing required configuration key: Keycloak:Authority");
            var keycloakClientId = keycloakSection["ClientId"]
                ?? throw new InvalidOperationException("Missing required configuration key: Keycloak:ClientId");
            var keycloakClientSecret = keycloakSection["ClientSecret"];

            var requireClientSecret =
                string.Equals(keycloakSection["RequireClientSecret"], "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(keycloakSection["RequireClientSecret"], "true", StringComparison.OrdinalIgnoreCase);

            if (requireClientSecret && string.IsNullOrWhiteSpace(keycloakClientSecret))
            {
                startupClientSecretMissing = true;
                startupClientId = keycloakClientId;
            }

            builder.Services
                .AddAuthentication(options =>
                {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
                })
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.LoginPath = "/login";
                    options.AccessDeniedPath = "/login";
                })
                .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = keycloakAuthority;
                    options.ClientId = keycloakClientId;
                    options.ClientSecret = keycloakClientSecret;
                    options.ResponseType = OpenIdConnectResponseType.Code;
                    options.SaveTokens = true;
                    options.GetClaimsFromUserInfoEndpoint = true;
                    options.RequireHttpsMetadata = true;
                    options.Scope.Clear();
                    options.Scope.Add("openid");
                    options.Scope.Add("profile");
                    options.Scope.Add("email");
                    options.Scope.Add("roles");
                    options.TokenValidationParameters.NameClaimType = "preferred_username";
                    options.TokenValidationParameters.RoleClaimType = "roles";
                });

            builder.Services.AddScoped<IClaimsTransformation, KeycloakGroupClaimsTransformer>();
        }

        var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException("Missing required configuration key: ApiBaseUrl.");

        builder.Services.AddHttpClient<SurfacesService>(client =>
            client.BaseAddress = new Uri(apiBaseUrl));

        builder.Services.AddHttpClient<UiSettingsService>(client =>
                client.BaseAddress = new Uri(apiBaseUrl))
            .AddHttpMessageHandler<AccessTokenForwardingHandler>();

        builder.Services.AddHttpClient<WarBoardService>(client =>
                client.BaseAddress = new Uri(apiBaseUrl))
            .AddHttpMessageHandler<AccessTokenForwardingHandler>();

        builder.Services.AddHttpClient<WarScoutService>(client =>
                client.BaseAddress = new Uri(apiBaseUrl))
            .AddHttpMessageHandler<AccessTokenForwardingHandler>();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;

            options.KnownProxies.Add(IPAddress.Parse("127.0.0.1"));
        });

        var app = builder.Build();

        if (startupClientSecretMissing)
        {
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HappyGymStats.Blazor.Startup")
                .LogCritical(
                    "Keycloak:RequireClientSecret is set for client '{ClientId}', but Keycloak:ClientSecret is empty. " +
                    "Anonymous pages still serve; every sign-in will fail against a confidential client. " +
                    "Set Keycloak__ClientSecret in the unit's EnvironmentFile (/etc/happygymstats/blazor.env or blazor-dev.env) " +
                    "- check the key spelling - or clear Keycloak__RequireClientSecret if this deployment really is a public client.",
                    startupClientId);
        }

        if (developmentAuthEnabled)
        {
            app.Logger.LogWarning("Development authentication bypass is ENABLED. This host must never handle production traffic.");
        }

        if (app.Environment.IsDevelopment())
            app.UseWebAssemblyDebugging();
        else
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseForwardedHeaders();
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAdminOnlyAccessWhenConfigured(builder.Configuration);
        app.UseAntiforgery();
        app.MapStaticAssets();

        app.MapGet("/auth/login", async (HttpContext httpContext, string? returnUrl) =>
        {
            var safeReturnUrl = LocalRedirectPolicy.Normalize(returnUrl);
            var properties = new AuthenticationProperties { RedirectUri = safeReturnUrl };
            await httpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
        });

        app.MapGet("/auth/logout", async (HttpContext httpContext, string? returnUrl) =>
        {
            var safeReturnUrl = LocalRedirectPolicy.Normalize(returnUrl);
            var properties = new AuthenticationProperties { RedirectUri = safeReturnUrl };
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
        });

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(HappyGymStats.Blazor.Client._Imports).Assembly);

        app.Run();
    }
}
