using System.Globalization;
using HappyGymStats.Export;
using Xunit;

namespace HappyGymStats.Tests.Export;

public sealed class GymModifierNormalizationTests
{
    [Fact]
    public void Export_NormalizesStatIncrease_ByGymMultiplier_WhenGymsJsonPresent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests.Gyms", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var oldCwd = Directory.GetCurrentDirectory();
        try
        {
            // Copy gyms.json into the working directory so CsvExportRunner can find it.
            var repoRoot = FindRepoRoot();
            File.Copy(Path.Combine(repoRoot, "gyms.json"), Path.Combine(tempDir, "gyms.json"), overwrite: true);

            Directory.SetCurrentDirectory(tempDir);

            var jsonlPath = Path.Combine(tempDir, "userlogs.jsonl");
            var csvPath = Path.Combine(tempDir, "out.csv");

            // Gym 2 (Average Joes) has strength=24 => multiplier 2.4
            // raw strength_increased=0.9075 => normalized = 0.378125
            var jsonl = string.Join('\n', new[]
            {
                "{\"id\":\"2001\",\"timestamp\":1700000000,\"details\":{\"id\":5300,\"title\":\"Gym train strength\",\"category\":\"Gym\"},\"data\":{\"trains\":5,\"energy_used\":25,\"gym\":2,\"strength_before\":\"14.83779604\",\"strength_after\":15.745296040000001,\"strength_increased\":0.9075,\"happy_used\":0}}",
            });

            File.WriteAllText(jsonlPath, jsonl);

            var result = CsvExportRunner.Run(jsonlPath, csvPath);
            Assert.True(result.Success, result.ErrorMessage);

            // Header should include the normalized column.
            Assert.Contains("data.strength_increased_normalized", result.HeaderColumns);

            var lines = File.ReadAllLines(csvPath);
            Assert.True(lines.Length >= 2);

            var header = ParseCsvLine(lines[0]).ToArray();
            var row = ParseCsvLine(lines[1]).ToArray();

            var idxRaw = Array.IndexOf(header, "data.strength_increased");
            var idxNorm = Array.IndexOf(header, "data.strength_increased_normalized");

            Assert.True(idxRaw >= 0);
            Assert.True(idxNorm >= 0);

            var raw = double.Parse(row[idxRaw].Trim('"'), CultureInfo.InvariantCulture);
            var norm = double.Parse(row[idxNorm].Trim('"'), CultureInfo.InvariantCulture);

            Assert.Equal(0.9075, raw, 12);
            Assert.Equal(0.9075 / 2.4, norm, 12);
        }
        finally
        {
            Directory.SetCurrentDirectory(oldCwd);
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var i = 0;

        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // Quoted field
                i++;
                var sb = new System.Text.StringBuilder();
                var done = false;
                while (i < line.Length && !done)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                        }
                        else
                        {
                            i++;
                            done = true;
                        }
                    }
                    else
                    {
                        sb.Append(line[i]);
                        i++;
                    }
                }

                if (i < line.Length && line[i] == ',')
                    i++;

                fields.Add(sb.ToString());
            }
            else
            {
                // Unquoted field
                var start = i;
                while (i < line.Length && line[i] != ',')
                    i++;

                fields.Add(line[start..i]);

                if (i < line.Length && line[i] == ',')
                    i++;
            }
        }

        return fields;
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

        throw new DirectoryNotFoundException("Unable to locate repo root (HappyGymStats.sln not found).");
    }
}
