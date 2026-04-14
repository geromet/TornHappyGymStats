using HappyGymStats.Reconstruction;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Tests.Reconstruction;

public sealed class LogEventExtractorTests
{
    [Fact]
    public void Extract_FindsGymTrainHappyUsed()
    {
        var records = ReadFixtureRecords();

        var extracted = LogEventExtractor.Extract(records);
        var events = extracted.Events.ToList();

        var gym = events.OfType<GymTrainEvent>().Single();
        Assert.Equal("1001", gym.LogId);
        Assert.Equal(25, gym.HappyUsed);

        Assert.True(extracted.Stats.GymTrainEventsExtracted >= 1);
    }

    [Fact]
    public void Extract_FindsMaxHappyChange_FromDataMaximumHappyAfter()
    {
        var records = ReadFixtureRecords();

        var extracted = LogEventExtractor.Extract(records);
        var events = extracted.Events.ToList();

        var max = events.OfType<MaxHappyEvent>().Single();
        Assert.Equal("1002", max.LogId);
        Assert.Equal(4500, max.MaxHappy);

        Assert.True(extracted.Stats.MaxHappyEventsExtracted >= 1);
    }

    [Fact]
    public void Extract_WhenDetailsMissing_DoesNotThrow_AndDoesNotExtract()
    {
        var record = new JsonlLogReader.LogRecord(
            LogId: "1",
            OccurredAtUtc: DateTimeOffset.FromUnixTimeSeconds(1700000000),
            Title: "No data",
            Category: "misc",
            RawJson: "{\"id\":\"1\",\"timestamp\":1700000000,\"details\":{\"title\":\"No data\"}}");

        var extracted = LogEventExtractor.Extract(new[] { record });

        var events = extracted.Events.ToList();
        Assert.Empty(events);
        Assert.Equal(1, extracted.Stats.RecordsSeen);
        Assert.Equal(1, extracted.Stats.MissingDetailsCount);
    }

    private static IReadOnlyList<JsonlLogReader.LogRecord> ReadFixtureRecords()
    {
        var fixturePath = FixturePath("userlogs-small.jsonl");
        var read = JsonlLogReader.Read(fixturePath);
        Assert.True(read.Success, read.ErrorMessage);
        return read.Records.ToList();
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
