using System.Text.Json;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.Api;

public sealed class SurfacesCacheWriter
{
    private const int MaxWarningsPerLog = 20;
    private static readonly string OverrideFileName = "modifier-overrides.local.json";

    private static readonly HashSet<string> KnownScopes = new(StringComparer.Ordinal)
    {
        "personal",
        "faction",
        "company"
    };

    private static readonly HashSet<string> KnownStatuses = new(StringComparer.Ordinal)
    {
        "verified",
        "unresolved",
        "unavailable"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cacheDirectory;
    private readonly ILogger<SurfacesCacheWriter>? _logger;

    public SurfacesCacheWriter(IServiceScopeFactory scopeFactory, string cacheDirectory, ILogger<SurfacesCacheWriter>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _cacheDirectory = cacheDirectory;
        _logger = logger;
    }

    public async Task WriteLatestAsync(string version, DateTimeOffset syncedAtUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HappyGymStatsDbContext>();

        var derivedRows = await db.DerivedGymTrains.AsNoTracking()
            .Select(x => new SurfaceSeriesBuilder.DerivedGym(x.LogId, x.HappyBeforeTrain))
            .ToListAsync(ct);

        var derivedByLogId = derivedRows.ToDictionary(x => x.LogId, x => x);

        var rawRows = await db.RawUserLogs.AsNoTracking()
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new SurfaceSeriesBuilder.RawGymLog(x.LogId, x.OccurredAtUtc, x.RawJson))
            .ToListAsync(ct);

        var eventRows = await db.DerivedHappyEvents.AsNoTracking()
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new SurfaceSeriesBuilder.DerivedHappyEvent(
                x.OccurredAtUtc,
                x.EventType,
                x.HappyBeforeEvent ?? 0,
                x.Delta ?? 0,
                x.HappyAfterEvent ?? 0))
            .ToListAsync(ct);

        var provenanceRows = await db.ModifierProvenance.AsNoTracking()
            .Select(x => new
            {
                x.DerivedGymTrainLogId,
                x.Scope,
                x.VerificationStatus,
                x.VerificationReasonCode,
                x.SubjectId,
                x.FactionId,
                x.CompanyId
            })
            .ToListAsync(ct);

        var provenanceByLogId = provenanceRows
            .GroupBy(x => x.DerivedGymTrainLogId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>)g
                    .OrderBy(x => x.Scope, StringComparer.Ordinal)
                    .Select(x => new SurfaceSeriesBuilder.ModifierProvenance(x.Scope, x.VerificationStatus, x.VerificationReasonCode))
                    .ToList(),
                StringComparer.Ordinal);

        var surfaces = SurfaceSeriesBuilder.Build(rawRows, derivedByLogId, eventRows, provenanceByLogId);
        var overridePath = Path.Combine(_cacheDirectory, OverrideFileName);
        var overrideResult = ModifierOverrideLoader.LoadFromFile(overridePath);
        var warningProjection = ProjectProvenanceWarnings(provenanceRows, overrideResult);

        var payload = new
        {
            dataset = "surfaces",
            version,
            syncedAtUtc,
            series = new
            {
                gymCloud = new
                {
                    x = surfaces.GymX,
                    y = surfaces.GymY,
                    z = surfaces.GymZ,
                    text = surfaces.GymText,
                    confidence = surfaces.GymConfidence,
                    confidenceReasons = surfaces.GymConfidenceReasons,
                    provenanceWarnings = warningProjection.Warnings
                },
                eventsCloud = new
                {
                    x = surfaces.EventX,
                    y = surfaces.EventY,
                    z = surfaces.EventZ,
                    text = surfaces.EventText
                }
            },
            meta = new
            {
                gymPointCount = surfaces.GymX.Length,
                eventPointCount = surfaces.EventX.Length,
                recordCount = surfaces.GymX.Length + surfaces.EventX.Length,
                provenanceWarningsDiagnostics = new
                {
                    warningCount = warningProjection.Warnings.Count,
                    skippedMalformedRowCount = warningProjection.SkippedMalformedRowCount,
                    overrideLoadedEntryCount = overrideResult.LoadedEntryCount,
                    overrideSkippedMalformedEntryCount = overrideResult.SkippedMalformedEntryCount,
                    overrideHitEntryCap = overrideResult.HitEntryCap,
                    overrideDiagnostics = overrideResult.Diagnostics,
                    queryFailed = false,
                    reason = "ok"
                }
            }
        };

        var meta = new
        {
            dataset = "surfaces",
            currentVersion = version,
            syncedAtUtc
        };

        Directory.CreateDirectory(_cacheDirectory);
        var latestPath = Path.Combine(_cacheDirectory, "latest.json");
        var metaPath = Path.Combine(_cacheDirectory, "meta.json");

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        var latestTemp = latestPath + ".tmp";
        var metaTemp = metaPath + ".tmp";

        await File.WriteAllTextAsync(latestTemp, JsonSerializer.Serialize(payload, jsonOptions), ct);
        await File.WriteAllTextAsync(metaTemp, JsonSerializer.Serialize(meta, jsonOptions), ct);

        File.Move(latestTemp, latestPath, overwrite: true);
        File.Move(metaTemp, metaPath, overwrite: true);
    }

    private WarningProjection ProjectProvenanceWarnings(IEnumerable<dynamic> provenanceRows, ModifierOverrideLoadResult overrideResult)
    {
        var skippedMalformedRows = 0;

        var grouped = provenanceRows
            .Where(row =>
            {
                var valid = KnownScopes.Contains((string)row.Scope) && KnownStatuses.Contains((string)row.VerificationStatus);
                if (!valid)
                {
                    skippedMalformedRows++;
                }

                return valid;
            })
            .Where(row => string.Equals((string)row.VerificationStatus, "unresolved", StringComparison.Ordinal)
                || string.Equals((string)row.VerificationStatus, "unavailable", StringComparison.Ordinal))
            .GroupBy(
                row => new
                {
                    LogId = (string)row.DerivedGymTrainLogId,
                    Scope = (string)row.Scope,
                    Status = (string)row.VerificationStatus,
                    Reason = (string)row.VerificationReasonCode,
                    LinkTarget = BuildLinkTarget((string)row.Scope, (string?)row.SubjectId, (string?)row.FactionId, (string?)row.CompanyId),
                    PlaceholderId = ResolvePlaceholder((string)row.Scope, (string?)row.FactionId, (string?)row.CompanyId)
                })
            .Select(g =>
            {
                var ov = overrideResult.Find(g.Key.Scope, g.Key.PlaceholderId);
                return new ProvenanceWarning(
                    LogId: g.Key.LogId,
                    Scope: g.Key.Scope,
                    VerificationStatus: g.Key.Status,
                    ReasonCode: g.Key.Reason,
                    LinkTarget: ov?.LinkTarget ?? g.Key.LinkTarget,
                    RowCount: g.Count(),
                    HasManualOverride: ov is not null,
                    ManualOverrideSource: ov is null ? null : "local-manual");
            })
            .OrderBy(x => x.LogId, StringComparer.Ordinal)
            .ThenBy(x => x.Scope, StringComparer.Ordinal)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToList();

        var bounded = grouped
            .GroupBy(x => x.LogId, StringComparer.Ordinal)
            .SelectMany(g => g.Take(MaxWarningsPerLog))
            .ToList();

        if (skippedMalformedRows > 0)
        {
            _logger?.LogWarning("Skipped malformed modifier provenance rows while building warning projection. count={Count}", skippedMalformedRows);
        }

        return new WarningProjection(bounded, skippedMalformedRows);
    }

    private static string BuildLinkTarget(string scope, string? subjectId, string? factionId, string? companyId)
        => scope switch
        {
            "personal" when !string.IsNullOrWhiteSpace(subjectId) => $"/profiles/{subjectId}",
            "faction" when !string.IsNullOrWhiteSpace(factionId) => $"/factions/{factionId}",
            "company" when !string.IsNullOrWhiteSpace(companyId) => $"/companies/{companyId}",
            _ => "/provenance/unresolved"
        };

    private static string? ResolvePlaceholder(string scope, string? factionId, string? companyId)
        => scope switch
        {
            "faction" => factionId,
            "company" => companyId,
            _ => null
        };

    private sealed record ProvenanceWarning(
        string LogId,
        string Scope,
        string VerificationStatus,
        string ReasonCode,
        string LinkTarget,
        int RowCount,
        bool HasManualOverride,
        string? ManualOverrideSource);

    private sealed record WarningProjection(IReadOnlyList<ProvenanceWarning> Warnings, int SkippedMalformedRowCount);
}
