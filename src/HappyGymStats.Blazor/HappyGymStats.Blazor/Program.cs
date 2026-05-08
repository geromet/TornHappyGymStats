using HappyGymStats.Blazor.Components;
using HappyGymStats.Blazor.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();

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
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(HappyGymStats.Blazor.Client._Imports).Assembly);

app.Run();
