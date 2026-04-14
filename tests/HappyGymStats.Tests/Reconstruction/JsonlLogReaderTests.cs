using System.Text.Json;
using HappyGymStats.Reconstruction;

namespace HappyGymStats.Tests.Reconstruction;

public sealed class JsonlLogReaderTests
{
    [Fact]
    public void Read_SkipsBlankAndMalformedLines_ReturnsValidRecordsAndStats()
    {
        var fixturePath = FixturePath("userlogs-small.jsonl");

        var result = JsonlLogReader.Read(fixturePath);
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);

        var records = result.Records.ToList();

        Assert.Equal(3, records.Count);
        Assert.Equal(5, result.Stats.LinesRead);
        Assert.Equal(1, result.Stats.BlankLines);
        Assert.Equal(1, result.Stats.MalformedLines);
        Assert.Equal(3, result.Stats.RecordsYielded);

        Assert.Contains(records, r => r.LogId == "1001" && r.Title == "Gym train strength" && r.Category == "Gym");
        Assert.Contains(records, r => r.LogId == "1002" && r.Title == "Happy maximum increase");
        Assert.Contains(records, r => r.LogId == "1003" && r.Category == "Travel");

        // Ensure the record can be parsed again later (no disposed JsonDocument coupling).
        using var doc = records[0].ParseJsonDocument();
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Read_WhenMissingIdOrTimestamp_SkipsAndCountsMalformed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "userlogs.jsonl");
        File.WriteAllText(path, string.Join("\n", new[]
        {
            // Missing id
            "{\"timestamp\":1700000000,\"details\":{}}",
            // Missing timestamp
            "{\"id\":1,\"details\":{}}",
            // Valid
            "{\"id\":2,\"timestamp\":1700000001,\"details\":{}}",
        }));

        var result = JsonlLogReader.Read(path);

        var records = result.Records.ToList();

        Assert.Single(records);
        Assert.Equal(3, result.Stats.LinesRead);
        Assert.Equal(0, result.Stats.BlankLines);
        Assert.Equal(2, result.Stats.MalformedLines);
        Assert.Equal(1, result.Stats.RecordsYielded);
        Assert.Equal("2", records[0].LogId);
    }

    [Fact]
    public void Read_WhenFileDoesNotExist_ReturnsFailedResult()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"), "missing.jsonl");

        var result = JsonlLogReader.Read(missingPath);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Empty(result.Records);
        Assert.Equal(0, result.Stats.LinesRead);
    }

    private static string FixturePath(string fileName)
    {
        var root = FindRepoRoot();
        return Path.Combine(root, "tests", "HappyGymStats.Tests", "Fixtures", fileName);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HappyGymStats.sln")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root (HappyGymStats.sln not found).");
    }
}
