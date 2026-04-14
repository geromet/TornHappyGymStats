using HappyGymStats.Reconstruction;
using HappyGymStats.Storage;

namespace HappyGymStats.Tests.Reconstruction;

public sealed class ReconstructionRunnerTests
{
    [Fact]
    public void Run_EndToEnd_WritesDerivedSidecar_AndDoesNotMutateRawJsonl()
    {
        var dir = NewTempDataDir();
        var paths = TempPaths(dir);

        Directory.CreateDirectory(paths.DataDirectory);

        // Copy fixture to the location the runner reads.
        File.Copy(FixturePath("userlogs-small.jsonl"), paths.LogsJsonlPath, overwrite: true);

        var beforeBytes = File.ReadAllBytes(paths.LogsJsonlPath);

        var runner = new ReconstructionRunner(paths);
        var result = runner.Run(
            currentHappy: 1000,
            anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Stats);

        Assert.True(File.Exists(paths.DerivedGymTrainsJsonlPath));
        var derivedLines = File.ReadAllLines(paths.DerivedGymTrainsJsonlPath);
        Assert.NotEmpty(derivedLines);

        // Fixture has a blank line and a malformed JSON line.
        Assert.Equal(1, result.Stats!.MalformedLines);
        Assert.Equal(1, result.Stats.GymTrainsDerived);

        var afterBytes = File.ReadAllBytes(paths.LogsJsonlPath);
        Assert.Equal(beforeBytes, afterBytes);
    }

    [Fact]
    public void Run_WhenInputJsonlEmpty_WritesDeterministicEmptyDerivedFile()
    {
        var dir = NewTempDataDir();
        var paths = TempPaths(dir);

        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.LogsJsonlPath, string.Empty);

        var runner = new ReconstructionRunner(paths);
        var result = runner.Run(
            currentHappy: 0,
            anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000));

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Stats);

        Assert.True(File.Exists(paths.DerivedGymTrainsJsonlPath));
        Assert.Empty(File.ReadAllLines(paths.DerivedGymTrainsJsonlPath));
        Assert.Equal(0, result.Stats!.GymTrainsDerived);
    }

    [Fact]
    public void Run_WhenNoGymTrains_WritesDeterministicEmptyDerivedFile()
    {
        var dir = NewTempDataDir();
        var paths = TempPaths(dir);

        Directory.CreateDirectory(paths.DataDirectory);

        // Valid log line but without details.happy_used.
        File.WriteAllText(paths.LogsJsonlPath,
            "{\"id\":\"2001\",\"timestamp\":1700000000,\"details\":{\"id\":2322,\"title\":\"Travel\",\"category\":\"Travel\"},\"data\":{\"destination\":\"Mexico\"},\"params\":{\"color\":\"blue\"}}\n");

        var runner = new ReconstructionRunner(paths);
        var result = runner.Run(
            currentHappy: 100,
            anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000));

        Assert.True(result.Success, result.ErrorMessage);

        Assert.True(File.Exists(paths.DerivedGymTrainsJsonlPath));
        Assert.Empty(File.ReadAllLines(paths.DerivedGymTrainsJsonlPath));
        Assert.Equal(0, result.Stats!.GymTrainsDerived);
    }

    [Fact]
    public void Run_WhenCurrentHappyNegative_Throws()
    {
        var dir = NewTempDataDir();
        var paths = TempPaths(dir);
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(paths.LogsJsonlPath, string.Empty);

        var runner = new ReconstructionRunner(paths);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            runner.Run(currentHappy: -1, anchorTimeUtc: DateTimeOffset.FromUnixTimeSeconds(1700001000)));
    }

    private static AppPaths TempPaths(string dir)
    {
        var quarantine = Path.Combine(dir, "quarantine");
        return new AppPaths(
            DataDirectory: dir,
            QuarantineDirectory: quarantine,
            CheckpointPath: Path.Combine(dir, "checkpoint.json"),
            LogsJsonlPath: Path.Combine(dir, "userlogs.jsonl"));
    }

    private static string NewTempDataDir()
        => Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests", "reconstruction-runner", Guid.NewGuid().ToString("N"));

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
