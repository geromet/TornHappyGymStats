using HappyGymStats.Export;
using HappyGymStats.Reconstruction;
using static HappyGymStats.Reconstruction.HappyReconstructionModels;

namespace HappyGymStats.Tests.Export;

public sealed class CsvDebugExportTests
{
    [Fact]
    public void ExportDebug_ProducesFixedHeader_AndSimplifiedStatColumns()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests.ExportDebug", Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(tempDir, "data");
        var derivedDir = Path.Combine(dataDir, "derived");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(derivedDir);

        var jsonlPath = Path.Combine(dataDir, "userlogs.jsonl");
        var derivedPath = Path.Combine(derivedDir, "derived-gymtrains.jsonl");
        var csvPath = Path.Combine(dataDir, "export", "userlogs.debug.csv");

        try
        {
            // Gym-train style record with stat fields + happy deltas.
            File.WriteAllText(jsonlPath,
                "{\"id\":\"1\",\"timestamp\":1700000000,\"details\":{\"title\":\"Gym train strength\",\"category\":\"Gym\"},\"data\":{\"energy_used\":25,\"gym\":0,\"strength_before\":\"100\",\"strength_after\":105,\"strength_increased\":5,\"happy_increased\":25,\"happy_decreased\":5}}\n");

            var derived = new[]
            {
                new DerivedGymTrain(
                    LogId: "1",
                    OccurredAtUtc: DateTimeOffset.FromUnixTimeSeconds(1700000000).ToUniversalTime(),
                    HappyBeforeTrain: 2000,
                    HappyUsed: 10,
                    HappyAfterTrain: 1990,
                    RegenTicksApplied: 2,
                    RegenHappyGained: 0,
                    MaxHappyAtTimeUtc: null,
                    ClampedToMax: false)
            };

            var write = DerivedGymTrainStore.WriteAllAtomic(derivedPath, derived);
            Assert.True(write.Success, write.ErrorMessage);

            var result = CsvExportRunner.RunDebug(jsonlPath, csvPath, derivedPath);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(CsvExportRunner.DebugColumns, result.HeaderColumns);

            Assert.True(File.Exists(csvPath));
            var lines = File.ReadAllLines(csvPath);
            Assert.True(lines.Length >= 2);

            var header = CsvSplit(lines[0]);
            Assert.Equal(CsvExportRunner.DebugColumns, header);

            var fields = CsvSplit(lines[1]);

            int Idx(string name) => Array.IndexOf(header, name);

            Assert.Equal("1", fields[Idx("id")]);
            Assert.Equal("1700000000", fields[Idx("timestamp")]);
            Assert.Equal("25", fields[Idx("data.energy_used")]);
            Assert.Equal("0", fields[Idx("data.gym")]);

            // happy_increased=25, happy_decreased=5 => delta=20
            Assert.Equal("20", fields[Idx("data.happy_delta")]);

            Assert.Equal("strength", fields[Idx("stat_type")]);
            Assert.Equal("100", fields[Idx("stat_before")]);
            Assert.Equal("105", fields[Idx("stat_after")]);
            Assert.Equal("5", fields[Idx("stat_increased")]);

            // Derived join
            Assert.Equal("2000", fields[Idx("happy_before_train")]);
            Assert.Equal("1990", fields[Idx("happy_after_train")]);
            Assert.Equal("2", fields[Idx("regen_ticks_applied")]);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Simple CSV-aware splitter: respects double-quoted fields containing commas.
    /// </summary>
    private static string[] CsvSplit(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }
}
