using HappyGymStats.Storage;

namespace HappyGymStats.Tests.Store;

public sealed class JsonlLogStoreTests
{
    private sealed record TestLog(string Id, long Timestamp, string Title, string Category);

    [Fact]
    public void ScanAndQuarantine_BuildsDedupeSet_WithDuplicateIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var jsonlPath = Path.Combine(dir, "userlogs.jsonl");
        var quarantineDir = Path.Combine(dir, "quarantine");

        JsonlLogStore.Append(jsonlPath, new[]
        {
            new TestLog(Id: "abc123", Timestamp: 1_700_000_000, Title: "Gym", Category: "gym"),
            new TestLog(Id: "abc123", Timestamp: 1_700_000_001, Title: "Gym again", Category: "gym"),
            new TestLog(Id: "def456", Timestamp: 1_700_000_002, Title: "Travel", Category: "travel"),
        });

        var scan = JsonlLogStore.ScanAndQuarantine(jsonlPath, quarantineDir);

        Assert.Equal(3, scan.LinesRead);
        Assert.Equal(0, scan.MalformedLines);
        Assert.Equal(2, scan.ExistingIds.Count);
        Assert.Contains("abc123", scan.ExistingIds);
        Assert.Contains("def456", scan.ExistingIds);
        Assert.Equal("def456", scan.LastLogId);
        Assert.Equal("Travel", scan.LastLogTitle);
        Assert.Equal("travel", scan.LastLogCategory);
    }

    [Fact]
    public void ScanAndQuarantine_WhenFinalLineIsMalformed_QuarantinesAndTruncates()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var jsonlPath = Path.Combine(dir, "userlogs.jsonl");
        var quarantineDir = Path.Combine(dir, "quarantine");

        var validLine = "{\"id\":\"abc123\",\"timestamp\":1700000000,\"title\":\"Gym\",\"category\":\"gym\"}\n";
        var partialFinalLine = "{\"id\":\"def456\",\"timestamp\":1700000001"; // missing closing braces + newline

        File.WriteAllBytes(jsonlPath, System.Text.Encoding.UTF8.GetBytes(validLine + partialFinalLine));

        var scan = JsonlLogStore.ScanAndQuarantine(jsonlPath, quarantineDir);

        Assert.True(scan.MalformedLines >= 1);
        Assert.Contains("abc123", scan.ExistingIds);

        // Truncated back to the last valid newline (i.e., only the first line remains).
        var after = File.ReadAllText(jsonlPath);
        Assert.Equal(validLine, after);

        var files = Directory.GetFiles(quarantineDir);
        Assert.Contains(files, p => p.EndsWith(".partial-final.bin", StringComparison.Ordinal));
        Assert.Contains(files, p => p.EndsWith(".partial-final.txt", StringComparison.Ordinal));
    }
}
