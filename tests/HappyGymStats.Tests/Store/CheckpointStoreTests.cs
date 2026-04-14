using HappyGymStats.Storage;
using HappyGymStats.Storage.Models;

namespace HappyGymStats.Tests.Store;

public sealed class CheckpointStoreTests
{
    [Fact]
    public void TryRead_WhenMissing_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "checkpoint.json");

        var checkpoint = CheckpointStore.TryRead(path);

        Assert.Null(checkpoint);
    }

    [Fact]
    public void TryRead_WhenEmpty_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "checkpoint.json");
        File.WriteAllText(path, "\n\n");

        var checkpoint = CheckpointStore.TryRead(path);

        Assert.Null(checkpoint);
    }

    [Fact]
    public void WriteThenRead_RoundTrips_AndDoesNotPersistApiKey()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "checkpoint.json");
        var checkpoint = new Checkpoint(
            NextUrl: "https://api.torn.com/v2/user/log?from=123",
            LastLogId: "abc123",
            LastLogTimestamp: DateTimeOffset.UtcNow,
            LastLogTitle: "Gym",
            LastLogCategory: "gym",
            TotalFetchedCount: 100,
            TotalAppendedCount: 95,
            LastRunStartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastRunCompletedAt: DateTimeOffset.UtcNow,
            LastRunOutcome: "completed",
            LastErrorMessage: null,
            LastErrorAt: null);

        CheckpointStore.Write(path, checkpoint);
        var reloaded = CheckpointStore.TryRead(path);

        Assert.NotNull(reloaded);
        Assert.Equal(checkpoint, reloaded);

        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("apikey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", raw, StringComparison.OrdinalIgnoreCase);
    }
}
