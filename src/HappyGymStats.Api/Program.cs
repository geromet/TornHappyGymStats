using HappyGymStats.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using HappyGymStats.Core.Faction;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Identity.Authentication;
using HappyGymStats.Identity.Provisional;
using HappyGymStats.Core.Import;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Api.Hubs;
using HappyGymStats.Core.Services;
using HappyGymStats.Core.Surfaces;
using HappyGymStats.Core.Torn;
using HappyGymStats.Core.War;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
var developmentAuthEnabled = DevelopmentAuthenticationExtensions.IsEnabled(builder.Configuration);
if (developmentAuthEnabled)
{
    DevelopmentAuthenticationExtensions.ValidateCanEnable(builder.Environment);
    builder.Services.AddDevelopmentHeaderAuthentication();
}
else
{
    builder.Services.AddKeycloakAuthentication("https://auth.geromet.com/realms/torn");
}
builder.Services.AddScoped<IClaimsTransformation, HappyGymStatsClaimsTransformer>();
builder.Services.Configure<ProvisionalTokenOptions>(
    builder.Configuration.GetSection(ProvisionalTokenOptions.Section));
builder.Services.AddSingleton<IProvisionalTokenService, ProvisionalTokenService>();

builder.Services.AddCors(options =>
    options.AddPolicy("ReadApi", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .WithMethods("GET", "POST")));

builder.Services.AddControllers();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

var connectionString = developmentAuthEnabled
    ? AppConfiguration.ResolveDevelopmentSqliteConnectionString(builder.Configuration, builder.Environment)
    : AppConfiguration.ResolveConnectionString(builder.Configuration);
var surfacesCacheDirectory = AppConfiguration.ResolveSurfacesCacheDirectory(builder.Configuration, builder.Environment);

Directory.CreateDirectory(surfacesCacheDirectory);

builder.Services.AddDbContext<HappyGymStatsDbContext>(options =>
{
    if (developmentAuthEnabled)
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});

builder.Services.AddHttpClient<TornApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.torn.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<HappyGymStatsDbContext>());

builder.Services.AddScoped<IIdentityMapRepository, IdentityMapRepository>();
builder.Services.AddScoped<IUserLogEntryRepository, UserLogEntryRepository>();
builder.Services.AddScoped<IImportRunRepository, ImportRunRepository>();
builder.Services.AddScoped<IModifierProvenanceRepository, ModifierProvenanceRepository>();
builder.Services.AddScoped<IAffiliationEventRepository, AffiliationEventRepository>();
builder.Services.AddScoped<ILogTypeRepository, LogTypeRepository>();
builder.Services.AddScoped<IFactionIdMapRepository, FactionIdMapRepository>();
builder.Services.AddScoped<IFactionMembershipRepository, FactionMembershipRepository>();
builder.Services.AddScoped<IWarStateRepository, WarStateRepository>();

builder.Services.AddScoped<LogFetcher>();
builder.Services.AddScoped<PerkLogFetcher>();
builder.Services.AddScoped<ReconstructionRunner>();
builder.Services.AddScoped<GymTrainsService>();
builder.Services.AddScoped<FactionService>();
builder.Services.AddScoped<WarDerivedStateService>();
builder.Services.AddScoped<IFactionOwnershipVerifier, StubFactionOwnershipVerifier>();
builder.Services.AddScoped<IWarHubBroadcaster, WarHubBroadcaster>();

builder.Services.AddSingleton(new SurfacesConfig(surfacesCacheDirectory));

builder.Services.AddSingleton(sp =>
    new SurfacesCacheWriter(sp.GetRequiredService<IServiceScopeFactory>(), surfacesCacheDirectory));
builder.Services.AddSingleton<ImportOrchestrator>(sp =>
    new ImportOrchestrator(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<SurfacesCacheWriter>(),
        sp.GetRequiredService<ILogger<ImportOrchestrator>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImportOrchestrator>());

var app = builder.Build();

if (developmentAuthEnabled)
{
    app.Logger.LogWarning("Development authentication bypass is ENABLED. This host must never handle production traffic.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
    var provider = db.Database.ProviderName ?? string.Empty;
    if (app.Environment.IsEnvironment("Testing")
        || provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    if (developmentAuthEnabled)
    {
        await DevelopmentWarSeed.SeedAsync(db, app.Logger);
    }
}

app.MapOpenApi();
app.UseCors("ReadApi");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<WarHub>("/api/hub/war");

app.Run();
