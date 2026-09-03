using HappyGymStats.Blazor.Components;
using HappyGymStats.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using HappyGymStats.Identity.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
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

        // Data-protection key ring. Unset (localhost) keeps the framework default;
        // the server units point it at their systemd StateDirectory, which
        // survives both a restart and the release-symlink swap of a deploy.
        // Without a stable ring every deploy invalidates every auth cookie and
        // signs the whole site out.
        var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            builder.Services
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
                // Pinned, so production and dev never derive different keys from
                // a changing entry-assembly name, and so the two hosts stay
                // distinguishable if a ring is ever shared by mistake.
                .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "HappyGymStats.Blazor");
        }

        builder.Services.AddMudServices();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IServerAccessTokenProvider, ServerAccessTokenProvider>();
        builder.Services.AddTransient<AccessTokenForwardingHandler>();

        // Policy scaffold for future RBAC rollout.
        // Inactive by default until pages/endpoints explicitly opt-in via [Authorize(Policy = "RequireRole")].
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireRole", policy => policy.RequireRole("hgs-user"));
        });

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

            // A confidential client whose secret went missing (an unreadable or
            // unmounted EnvironmentFile) would otherwise start happily and fail
            // only at the token exchange, as an opaque invalid_client from
            // Keycloak on the user's first sign-in. Both server deployments set
            // this; localhost uses a public client and leaves it unset.
            var requireClientSecret =
                string.Equals(keycloakSection["RequireClientSecret"], "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(keycloakSection["RequireClientSecret"], "true", StringComparison.OrdinalIgnoreCase);

            if (requireClientSecret && string.IsNullOrWhiteSpace(keycloakClientSecret))
            {
                throw new InvalidOperationException(
                    $"Keycloak:RequireClientSecret is set for client '{keycloakClientId}', but Keycloak:ClientSecret is empty. " +
                    "Set Keycloak__ClientSecret in the unit's EnvironmentFile (/etc/happygymstats/blazor.env or blazor-dev.env), " +
                    "or clear Keycloak__RequireClientSecret if this deployment really does use a public client.");
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
        }

        // In production we intentionally target API loopback (127.0.0.1:5047) to avoid external proxy/CDN hops.
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException("Missing required configuration key: ApiBaseUrl.");

        builder.Services.AddHttpClient<SurfacesService>(client =>
            client.BaseAddress = new Uri(apiBaseUrl));

        // Writing a flag is admin-only, so this client forwards the access token.
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

        // Opt-in whole-site admin gate (Access:RestrictToAdmins). Off unless the
        // deployment sets it, so production behaviour is unchanged; the dev host
        // at torndev.geromet.com turns it on via its systemd unit.
        app.UseAdminOnlyAccessWhenConfigured(builder.Configuration);

        app.UseAntiforgery();
        app.MapStaticAssets();

        app.MapGet("/auth/login", async (HttpContext httpContext, string? returnUrl) =>
        {
            var safeReturnUrl = GetSafeLocalReturnUrl(returnUrl);
            var properties = new AuthenticationProperties { RedirectUri = safeReturnUrl };
            await httpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
        });

        app.MapGet("/auth/logout", async (HttpContext httpContext, string? returnUrl) =>
        {
            var safeReturnUrl = GetSafeLocalReturnUrl(returnUrl);
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

    private static string GetSafeLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return "/";

        if (Uri.TryCreate(returnUrl, UriKind.Relative, out var relative)
            && relative.OriginalString.StartsWith('/'))
        {
            return relative.OriginalString;
        }

        return "/";
    }
}