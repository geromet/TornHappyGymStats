using System.Text;
using System.Text.Json;
using HappyGymStats.Api;
using HappyGymStats.Data;
using HappyGymStats.Data.Entities;
using HappyGymStats.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
    options.AddPolicy("ReadApi", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .WithMethods("GET", "POST")));

var databasePath = ResolveDatabasePath(builder.Configuration, builder.Environment);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

builder.Services.AddDbContext<HappyGymStatsDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddSingleton(sp =>
    new ImportService(sp.GetRequiredService<IServiceScopeFactory>(), databasePath));
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImportService>());

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

app.UseCors("ReadApi");

// ---- Import endpoints -------------------------------------------------------

app.MapPost("/v1/import", (
    ImportService importService,
    HttpContext httpContext,
    ImportRequest? request) =>
{
    var apiKey = request?.ApiKey?.Trim();
    if (string.IsNullOrWhiteSpace(apiKey))
        return ValidationError(httpContext, "apiKey is required.", new { field = "apiKey" });

    var status = importService.Enqueue(apiKey, request?.Fresh ?? false);

    var statusCode = status.IsTerminal
        ? StatusCodes.Status200OK
        : StatusCodes.Status202Accepted;

    return Results.Json(ToDto(status), statusCode: statusCode);
})
    .WithName("StartImport")
    .WithOpenApi();

app.MapGet("/v1/import/latest", (ImportService importService, HttpContext httpContext) =>
{
    var status = importService.Latest;
    if (status is null)
        return Error(httpContext, StatusCodes.Status404NotFound, "not_found", "No import has been started.", null);

    return Results.Ok(ToDto(status));
})
    .WithName("GetLatestImport")
    .WithOpenApi();

// ---- Read endpoints ---------------------------------------------------------

app.MapGet("/v1/health", async (HappyGymStatsDbContext db, CancellationToken ct) =>
    Results.Ok(new HealthResponse(
        Status: await db.Database.CanConnectAsync(ct) ? "ok" : "degraded",
        Api: "HappyGymStats.Api",
        DatabaseProvider: db.Database.ProviderName ?? "unknown")))
    .WithName("GetHealth")
    .WithOpenApi();

app.MapGet("/v1/gym-trains", async (
    HappyGymStatsDbContext db,
    HttpContext httpContext,
    int? limit,
    string? cursor,
    CancellationToken ct) =>
{
    if (!TryGetLimit(limit, out var take, out var limitError))
        return ValidationError(httpContext, limitError!, new { field = "limit", min = 1, max = Pagination.MaxLimit });

    if (!TryDecodeCursor(cursor, out var pageCursor))
        return ValidationError(httpContext, "Cursor is invalid.", new { field = "cursor" });

    var query = CreateGymTrainPageQuery(db, pageCursor)
        .OrderByDescending(row => row.OccurredAtUtc)
        .ThenByDescending(row => row.LogId)
        .Take(take + 1)
        .Select(row => new GymTrainDto(
            row.LogId,
            row.OccurredAtUtc,
            row.HappyBeforeTrain,
            row.HappyAfterTrain,
            row.HappyUsed,
            row.RegenTicksApplied,
            row.RegenHappyGained,
            row.MaxHappyAtTimeUtc,
            row.ClampedToMax));

    var rows = await query.ToListAsync(ct);
    var response = CreatePage(rows, take, row => new PageCursor(row.OccurredAtUtc, row.LogId));

    return Results.Ok(response);
})
    .WithName("ListGymTrains")
    .WithOpenApi();

app.MapGet("/v1/happy-events", async (
    HappyGymStatsDbContext db,
    HttpContext httpContext,
    int? limit,
    string? cursor,
    CancellationToken ct) =>
{
    if (!TryGetLimit(limit, out var take, out var limitError))
        return ValidationError(httpContext, limitError!, new { field = "limit", min = 1, max = Pagination.MaxLimit });

    if (!TryDecodeCursor(cursor, out var pageCursor))
        return ValidationError(httpContext, "Cursor is invalid.", new { field = "cursor" });

    var query = CreateHappyEventPageQuery(db, pageCursor)
        .OrderByDescending(row => row.OccurredAtUtc)
        .ThenByDescending(row => row.EventId)
        .Take(take + 1)
        .Select(row => new HappyEventDto(
            row.EventId,
            row.EventType,
            row.OccurredAtUtc,
            row.SourceLogId,
            row.HappyBeforeEvent,
            row.HappyAfterEvent,
            row.Delta,
            row.Note));

    var rows = await query.ToListAsync(ct);
    var response = CreatePage(rows, take, row => new PageCursor(row.OccurredAtUtc, row.EventId));

    return Results.Ok(response);
})
    .WithName("ListHappyEvents")
    .WithOpenApi();

app.Run();

// ---- Helpers ----------------------------------------------------------------

static ImportStatusDto ToDto(ImportJobStatus s)
    => new(s.Id, s.Outcome, s.StartedAtUtc, s.CompletedAtUtc,
           s.PagesFetched, s.LogsFetched, s.LogsAppended, s.ErrorMessage);

static IQueryable<DerivedGymTrainEntity> CreateGymTrainPageQuery(HappyGymStatsDbContext db, PageCursor? cursor)
{
    if (cursor is null)
        return db.DerivedGymTrains.AsNoTracking();

    return db.DerivedGymTrains
        .FromSqlInterpolated($@"
SELECT *
FROM DerivedGymTrains
WHERE OccurredAtUtc < {cursor.OccurredAtUtc.UtcDateTime}
   OR (OccurredAtUtc = {cursor.OccurredAtUtc.UtcDateTime} AND LogId < {cursor.Id})")
        .AsNoTracking();
}

static IQueryable<DerivedHappyEventEntity> CreateHappyEventPageQuery(HappyGymStatsDbContext db, PageCursor? cursor)
{
    if (cursor is null)
        return db.DerivedHappyEvents.AsNoTracking();

    return db.DerivedHappyEvents
        .FromSqlInterpolated($@"
SELECT *
FROM DerivedHappyEvents
WHERE OccurredAtUtc < {cursor.OccurredAtUtc.UtcDateTime}
   OR (OccurredAtUtc = {cursor.OccurredAtUtc.UtcDateTime} AND EventId < {cursor.Id})")
        .AsNoTracking();
}

static CursorPage<T> CreatePage<T>(IReadOnlyList<T> rows, int take, Func<T, PageCursor> cursorSelector)
{
    var items = rows.Take(take).ToArray();
    var nextCursor = rows.Count > take && items.Length > 0
        ? EncodeCursor(cursorSelector(items[^1]))
        : null;

    return new CursorPage<T>(items, nextCursor);
}

static bool TryGetLimit(int? limit, out int take, out string? error)
{
    if (limit is null)
    {
        take = Pagination.DefaultLimit;
        error = null;
        return true;
    }

    if (limit < 1 || limit > Pagination.MaxLimit)
    {
        take = 0;
        error = $"Limit must be between 1 and {Pagination.MaxLimit}.";
        return false;
    }

    take = limit.Value;
    error = null;
    return true;
}

static bool TryDecodeCursor(string? value, out PageCursor? cursor)
{
    cursor = null;

    if (string.IsNullOrWhiteSpace(value))
        return true;

    try
    {
        var json = Encoding.UTF8.GetString(Base64UrlDecode(value));
        cursor = JsonSerializer.Deserialize<PageCursor>(json);
        return cursor is not null && !string.IsNullOrWhiteSpace(cursor.Id);
    }
    catch (FormatException)
    {
        return false;
    }
    catch (JsonException)
    {
        return false;
    }
}

static string EncodeCursor(PageCursor cursor)
    => Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)));

static byte[] Base64UrlDecode(string value)
{
    var padded = value.Replace('-', '+').Replace('_', '/');
    padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
    return Convert.FromBase64String(padded);
}

static string Base64UrlEncode(byte[] value)
    => Convert.ToBase64String(value)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

static IResult ValidationError(HttpContext httpContext, string message, object? details)
    => Error(httpContext, StatusCodes.Status422UnprocessableEntity, "validation_failed", message, details);

static IResult Error(HttpContext httpContext, int statusCode, string code, string message, object? details)
{
    var payload = new ErrorEnvelope(new ApiError(
        Code: code,
        Message: message,
        Details: details,
        RequestId: httpContext.TraceIdentifier));

    return Results.Json(payload, statusCode: statusCode);
}

static string ResolveDatabasePath(IConfiguration configuration, IWebHostEnvironment environment)
{
    var configuredPath = configuration.GetConnectionString("HappyGymStats")
        ?? configuration["HAPPYGYMSTATS_DATABASE"];

    var fallbackDataDirectory = DataDirectory.ResolveBasePath("HappyGymStats");
    return SqlitePaths.ResolveDatabasePath(fallbackDataDirectory, configuredPath);
}

// ---- Types ------------------------------------------------------------------

internal static class Pagination
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 200;
}

public sealed record ImportRequest(string? ApiKey, bool? Fresh);

public sealed record ImportStatusDto(
    string Id,
    string Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int PagesFetched,
    long LogsFetched,
    long LogsAppended,
    string? ErrorMessage);

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

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record ApiError(string Code, string Message, object? Details, string RequestId);

public sealed record ErrorEnvelope(ApiError Error);

public sealed record PageCursor(DateTimeOffset OccurredAtUtc, string Id);

public partial class Program;
