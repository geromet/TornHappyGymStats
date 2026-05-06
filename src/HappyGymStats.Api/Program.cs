using HappyGymStats.Api.Infrastructure;
using HappyGymStats.Core.Fetch;
using HappyGymStats.Identity.Authentication;
using HappyGymStats.Identity.Provisional;
using HappyGymStats.Core.Import;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Core.Services;
using HappyGymStats.Core.Surfaces;
using HappyGymStats.Core.Torn;
using HappyGymStats.Data;
using HappyGymStats.Data.Repositories;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddKeycloakAuthentication("https://auth.geromet.com/realms/torn");
builder.Services.Configure<ProvisionalTokenOptions>(
    builder.Configuration.GetSection(ProvisionalTokenOptions.Section));
builder.Services.AddSingleton<IProvisionalTokenService, ProvisionalTokenService>();

builder.Services.AddCors(options =>
    options.AddPolicy("ReadApi", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .WithMethods("GET", "POST")));

builder.Services.AddControllers();

var connectionString = AppConfiguration.ResolveConnectionString(builder.Configuration);
var surfacesCacheDirectory = AppConfiguration.ResolveSurfacesCacheDirectory(builder.Configuration, builder.Environment);

Directory.CreateDirectory(surfacesCacheDirectory);

builder.Services.AddDbContext<HappyGymStatsDbContext>(options =>
    options.UseNpgsql(connectionString));

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

builder.Services.AddScoped<LogFetcher>();
builder.Services.AddScoped<PerkLogFetcher>();
builder.Services.AddScoped<ReconstructionRunner>();
builder.Services.AddScoped<GymTrainsService>();

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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
    await db.Database.MigrateAsync();
}

app.MapOpenApi();
app.UseCors("ReadApi");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();
app.MapControllers();

app.Run();
