using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var databasePath = ResolveDatabasePath(builder.Configuration, builder.Environment);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddDbContext<HappyGymStatsDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", async (HappyGymStatsDbContext db, CancellationToken ct) =>
    Results.Ok(new HealthResponse(
        Status: await db.Database.CanConnectAsync(ct) ? "ok" : "degraded",
        Api: "HappyGymStats.Api",
        DatabaseProvider: db.Database.ProviderName ?? "unknown")))
    .WithName("GetHealth")
    .WithOpenApi();

app.MapGet("/api/gym-trains", async (HappyGymStatsDbContext db, int? limit, CancellationToken ct) =>
{
    var take = ClampLimit(limit);
    var rows = await db.DerivedGymTrains
        .AsNoTracking()
        .OrderByDescending(row => row.OccurredAtUtc)
        .Take(take)
        .Select(row => new GymTrainDto(
            row.LogId,
            row.OccurredAtUtc,
            row.HappyBeforeTrain,
            row.HappyAfterTrain,
            row.HappyUsed,
            row.RegenTicksApplied,
            row.RegenHappyGained,
            row.MaxHappyAtTimeUtc,
            row.ClampedToMax))
        .ToListAsync(ct);

    return Results.Ok(rows);
})
    .WithName("ListGymTrains")
    .WithOpenApi();

app.MapGet("/api/happy-events", async (HappyGymStatsDbContext db, int? limit, CancellationToken ct) =>
{
    var take = ClampLimit(limit);
    var rows = await db.DerivedHappyEvents
        .AsNoTracking()
        .OrderByDescending(row => row.OccurredAtUtc)
        .Take(take)
        .Select(row => new HappyEventDto(
            row.EventId,
            row.EventType,
            row.OccurredAtUtc,
            row.SourceLogId,
            row.HappyBeforeEvent,
            row.HappyAfterEvent,
            row.Delta,
            row.Note))
        .ToListAsync(ct);

    return Results.Ok(rows);
})
    .WithName("ListHappyEvents")
    .WithOpenApi();

app.Run();

static string ResolveDatabasePath(IConfiguration configuration, IWebHostEnvironment environment)
{
    var configuredPath = configuration.GetConnectionString("HappyGymStats")
        ?? configuration["HAPPYGYMSTATS_DATABASE"];

    if (!string.IsNullOrWhiteSpace(configuredPath))
        return Path.GetFullPath(configuredPath);

    return Path.Combine(environment.ContentRootPath, "data", "happygymstats.db");
}

static int ClampLimit(int? limit) => Math.Clamp(limit ?? 100, 1, 500);

public sealed record HealthResponse(string Status, string Api, string DatabaseProvider);

public sealed record GymTrainDto(
    string LogId,
    DateTimeOffset OccurredAtUtc,
    int HappyBeforeTrain,
    int HappyAfterTrain,
    int HappyUsed,
    long RegenTicksApplied,
    int RegenHappyGained,
    int? MaxHappyAtTimeUtc,
    bool ClampedToMax);

public sealed record HappyEventDto(
    string EventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string? SourceLogId,
    int? HappyBeforeEvent,
    int? HappyAfterEvent,
    int? Delta,
    string? Note);
