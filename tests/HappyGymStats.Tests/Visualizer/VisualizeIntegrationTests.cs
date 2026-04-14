using System.IO;
using Microsoft.FSharp.Collections;
using Xunit;

namespace HappyGymStats.Tests.Visualizer;

public sealed class VisualizeIntegrationTests
{
    private static string GetFixturePath()
    {
        // When running via `dotnet test`, content files are copied alongside the DLL.
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Visualizer", "fixtures", "sample_gym.csv");
        if (File.Exists(candidate))
            return candidate;

        // Fallback: relative to project source directory (IDE / direct run)
        var srcRelative = Path.Combine(Directory.GetCurrentDirectory(), "Visualizer", "fixtures", "sample_gym.csv");
        if (File.Exists(srcRelative))
            return srcRelative;

        throw new FileNotFoundException($"Fixture CSV not found. Tried: {candidate}, {srcRelative}");
    }

    /// <summary>
    /// Verifies that readStatRecords parses the fixture CSV and returns 3 records
    /// (2 strength + 1 defense) with no parse errors.
    /// </summary>
    [Fact]
    public void ReadStatRecords_FromFixtureCsv_ReturnsThreeRecords()
    {
        var fixturePath = GetFixturePath();
        var result = HappyGymStats.Visualizer.CsvStatReader.readStatRecords(fixturePath);

        Assert.Equal(3, result.Records.Count());
        Assert.Empty(result.ParseErrors);

        // Verify stat type breakdown: 2 strength, 1 defense
        var strengthRecords = result.Records.Where(r => r.StatType == HappyGymStats.Visualizer.StatType.Strength).ToList();
        var defenseRecords = result.Records.Where(r => r.StatType == HappyGymStats.Visualizer.StatType.Defense).ToList();

        Assert.Equal(2, strengthRecords.Count);
        Assert.Single(defenseRecords);
    }

    /// <summary>
    /// Verifies that generatePlots creates Strength.html and Defense.html from fixture records,
    /// and does NOT create Speed.html or Dexterity.html (no data for those).
    /// </summary>
    [Fact]
    public void GeneratePlots_FromFixtureRecords_CreatesExpectedHtmlFiles()
    {
        var fixturePath = GetFixturePath();
        var result = HappyGymStats.Visualizer.CsvStatReader.readStatRecords(fixturePath);

        var tempDir = Path.Combine(Path.GetTempPath(), $"HappyGymStats-VizTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var generatedPaths = HappyGymStats.Visualizer.SurfacePlotter.generatePlots(result.Records, tempDir).ToList();

            Assert.Equal(2, generatedPaths.Count);

            // Verify expected files exist
            Assert.True(File.Exists(Path.Combine(tempDir, "Strength.html")), "Strength.html should be generated");
            Assert.True(File.Exists(Path.Combine(tempDir, "Defense.html")), "Defense.html should be generated");

            // Verify non-existent files for stats with no data
            Assert.False(File.Exists(Path.Combine(tempDir, "Speed.html")), "Speed.html should NOT be generated (no data)");
            Assert.False(File.Exists(Path.Combine(tempDir, "Dexterity.html")), "Dexterity.html should NOT be generated (no data)");

            // Verify generated files are non-empty HTML
            foreach (var path in generatedPaths)
            {
                var content = File.ReadAllText(path);
                Assert.False(string.IsNullOrWhiteSpace(content), $"Generated file {path} should not be empty");
                Assert.Contains("<html", content, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that generatePlots with an empty record list produces no output files.
    /// </summary>
    [Fact]
    public void GeneratePlots_EmptyInput_ProducesNoFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"HappyGymStats-VizEmptyTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var emptyRecords = FSharpList<HappyGymStats.Visualizer.StatRecord>.Empty;
            var generatedPaths = HappyGymStats.Visualizer.SurfacePlotter.generatePlots(emptyRecords, tempDir).ToList();

            Assert.Empty(generatedPaths);
            Assert.Empty(Directory.GetFiles(tempDir, "*.html"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that readStatRecords throws FileNotFoundException for a non-existent path.
    /// </summary>
    [Fact]
    public void ReadStatRecords_MissingFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.csv");

        Assert.Throws<FileNotFoundException>(() =>
            HappyGymStats.Visualizer.CsvStatReader.readStatRecords(missingPath));
    }
}
