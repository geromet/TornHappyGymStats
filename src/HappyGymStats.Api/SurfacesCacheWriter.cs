using System.Text.Json;
using HappyGymStats.Core.Reconstruction;
using HappyGymStats.Data;
using Microsoft.EntityFrameworkCore;

namespace HappyGymStats.Api;

public sealed class SurfacesCacheWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _cacheDirectory;

    public SurfacesCacheWriter(IServiceScopeFactory scopeFactory, string cacheDirectory)
    {
        _scopeFactory = scopeFactory;
        _cacheDirectory = cacheDirectory;
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
                x.VerificationReasonCode
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
                    confidenceReasons = surfaces.GymConfidenceReasons
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
                recordCount = surfaces.GymX.Length + surfaces.EventX.Length
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
}
