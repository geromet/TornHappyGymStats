using System.Text.Json;
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

        var gymRows = await db.DerivedGymTrains.AsNoTracking()
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new { x.HappyBeforeTrain, x.HappyUsed, x.RegenHappyGained, x.OccurredAtUtc })
            .ToListAsync(ct);

        var eventRows = await db.DerivedHappyEvents.AsNoTracking()
            .Where(x => x.Delta != null && x.HappyBeforeEvent != null && x.HappyAfterEvent != null)
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => new { x.HappyBeforeEvent, x.Delta, x.HappyAfterEvent, x.EventType, x.OccurredAtUtc })
            .ToListAsync(ct);

        var payload = new
        {
            dataset = "surfaces",
            version,
            syncedAtUtc,
            series = new
            {
                gymCloud = new
                {
                    x = gymRows.Select(x => x.HappyBeforeTrain).ToArray(),
                    y = gymRows.Select(x => x.HappyUsed).ToArray(),
                    z = gymRows.Select(x => x.RegenHappyGained).ToArray(),
                    text = gymRows.Select(x => x.OccurredAtUtc.ToString("O")).ToArray()
                },
                eventsCloud = new
                {
                    x = eventRows.Select(x => x.HappyBeforeEvent!.Value).ToArray(),
                    y = eventRows.Select(x => x.Delta!.Value).ToArray(),
                    z = eventRows.Select(x => x.HappyAfterEvent!.Value).ToArray(),
                    text = eventRows.Select(x => $"{x.EventType} {x.OccurredAtUtc:O}").ToArray()
                }
            },
            meta = new
            {
                gymPointCount = gymRows.Count,
                eventPointCount = eventRows.Count,
                recordCount = gymRows.Count + eventRows.Count
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
