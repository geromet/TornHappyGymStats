using System.Text.Json;
using HappyGymStats.Core.Models;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Core.Repositories;
using HappyGymStats.Data.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HappyGymStats.Core.Surfaces;

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
        var userLogRepo = scope.ServiceProvider.GetRequiredService<IUserLogEntryRepository>();
        var provenanceRepo = scope.ServiceProvider.GetRequiredService<IModifierProvenanceRepository>();

        var gymLogRows = await userLogRepo.GetGymLogEntriesAsync(ct);

        var provenanceRows = await provenanceRepo.GetAllAsync(ct);

        // Convert integer scope/status to strings for SurfaceSeriesBuilder.
        var provenanceConverted = provenanceRows
            .Select(x => new
            {
                x.LogEntryId,
                ScopeStr = ScopeIntToString(x.Scope),
                StatusStr = StatusIntToString(x.VerificationStatus),
                x.SubjectId,
                x.FactionId,
                x.CompanyId,
                x.AnonymousId
            })
            .ToList();

        var provenanceByLogId = provenanceConverted
            .GroupBy(x => x.LogEntryId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SurfaceSeriesBuilder.ModifierProvenance>)g
                    .OrderBy(x => x.ScopeStr, StringComparer.Ordinal)
                    .Select(x => new SurfaceSeriesBuilder.ModifierProvenance(
                        x.ScopeStr,
                        x.StatusStr,
                        $"{x.ScopeStr}-{x.StatusStr}"))
                    .ToList(),
                StringComparer.Ordinal);

        var surfaces = SurfaceSeriesBuilder.Build(gymLogRows, provenanceByLogId);
        var overridePath = Path.Combine(_cacheDirectory, OverrideFileName);
        var overrideResult = ModifierOverrideLoader.LoadFromFile(overridePath);
        var warningProjection = ProjectProvenanceWarnings(provenanceConverted, overrideResult);

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

    private static string ScopeIntToString(int scope)
        => scope switch
        {
            (int)ModifierScope.Personal => "personal",
            (int)ModifierScope.Faction => "faction",
            (int)ModifierScope.Company => "company",
            _ => $"scope-{scope}"
        };

    private static string StatusIntToString(int status)
        => status switch
        {
            (int)VerificationStatus.Verified => "verified",
            (int)VerificationStatus.Unresolved => "unresolved",
            (int)VerificationStatus.Unavailable => "unavailable",
            _ => $"status-{status}"
        };

    private WarningProjection ProjectProvenanceWarnings(
        IEnumerable<dynamic> provenanceRows,
        ModifierOverrideLoadResult overrideResult)
    {
        var skippedMalformedRows = 0;

        var grouped = provenanceRows
            .Where(row =>
            {
                var valid = KnownScopes.Contains((string)row.ScopeStr) && KnownStatuses.Contains((string)row.StatusStr);
                if (!valid)
                {
                    skippedMalformedRows++;
                }

                return valid;
            })
            .Where(row => string.Equals((string)row.StatusStr, "unresolved", StringComparison.Ordinal)
                || string.Equals((string)row.StatusStr, "unavailable", StringComparison.Ordinal))
            .GroupBy(
                row => new
                {
                    LogId = (string)row.LogEntryId,
                    Scope = (string)row.ScopeStr,
                    Status = (string)row.StatusStr,
                    LinkTarget = BuildLinkTarget((string)row.ScopeStr, (int?)row.SubjectId, (int?)row.FactionId, (int?)row.CompanyId),
                    PlaceholderId = ResolvePlaceholder((string)row.ScopeStr, (int?)row.FactionId, (int?)row.CompanyId)
                })
            .Select(g =>
            {
                var ov = overrideResult.Find(g.Key.Scope, g.Key.PlaceholderId);
                return new ProvenanceWarning(
                    LogId: g.Key.LogId,
                    Scope: g.Key.Scope,
                    VerificationStatus: g.Key.Status,
                    LinkTarget: ov?.LinkTarget ?? g.Key.LinkTarget,
                    RowCount: g.Count(),
                    HasManualOverride: ov is not null,
                    ManualOverrideSource: ov is null ? null : "local-manual");
            })
            .OrderBy(x => x.LogId, StringComparer.Ordinal)
            .ThenBy(x => x.Scope, StringComparer.Ordinal)
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

    private static string BuildLinkTarget(string scope, int? subjectId, int? factionId, int? companyId)
        => scope switch
        {
            "personal" when subjectId is not null => $"/profiles/{subjectId}",
            "faction" when factionId is not null => $"/factions/{factionId}",
            "company" when companyId is not null => $"/companies/{companyId}",
            _ => "/provenance/unresolved"
        };

    private static string? ResolvePlaceholder(string scope, int? factionId, int? companyId)
        => scope switch
        {
            "faction" => factionId?.ToString(),
            "company" => companyId?.ToString(),
            _ => null
        };

    private sealed record ProvenanceWarning(
        string LogId,
        string Scope,
        string VerificationStatus,
        string LinkTarget,
        int RowCount,
        bool HasManualOverride,
        string? ManualOverrideSource);

    private sealed record WarningProjection(IReadOnlyList<ProvenanceWarning> Warnings, int SkippedMalformedRowCount);
}
