using HappyGymStats.Blazor.Components;
using HappyGymStats.Blazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

var keycloakSection = builder.Configuration.GetSection("Keycloak");
var keycloakAuthority = keycloakSection["Authority"]
    ?? throw new InvalidOperationException("Missing required configuration key: Keycloak:Authority");
var keycloakClientId = keycloakSection["ClientId"]
    ?? throw new InvalidOperationException("Missing required configuration key: Keycloak:ClientId");
var keycloakClientSecret = keycloakSection["ClientSecret"];

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "hgs_auth";
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        options.Authority = keycloakAuthority.TrimEnd('/');
        options.ClientId = keycloakClientId;
        options.ClientSecret = string.IsNullOrWhiteSpace(keycloakClientSecret) ? null : keycloakClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;

        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.MapInboundClaims = false;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";

        options.SignedOutRedirectUri = "/";
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var error = Uri.EscapeDataString(context.Failure?.Message ?? "Authentication error");
            context.Response.Redirect($"/login?authError={error}");
            return Task.CompletedTask;
        };
    });

var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("Missing required configuration key: ApiBaseUrl");

// Server-side Blazor executes this HttpClient on the app host, not in the browser.
// In production we intentionally target API loopback (127.0.0.1:5047) to avoid external proxy/CDN hops.
builder.Services.AddHttpClient<SurfacesService>(client =>
    client.BaseAddress = new Uri(apiBaseUrl));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseWebAssemblyDebugging();
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
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

static string GetSafeLocalReturnUrl(string? returnUrl)
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
