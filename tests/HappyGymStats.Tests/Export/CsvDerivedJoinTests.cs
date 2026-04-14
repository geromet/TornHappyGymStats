using System.Text.Json;
using HappyGymStats.Export;

namespace HappyGymStats.Tests.Export;

public sealed class CsvDerivedJoinTests
{
    /// <summary>
    /// Verifies that derived columns are present in the CSV header when a derived sidecar is provided.
    /// </summary>
    [Fact]
    public void Export_WithDerived_IncludesDerivedColumnsInHeader()
    {
        var (jsonlPath, derivedPath, csvPath, tempDir) = SetupTempFiles(withDerived: true);

        try
        {
            var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");

            // All 6 derived columns must be in the header.
            var headerSet = new HashSet<string>(result.HeaderColumns, StringComparer.Ordinal);
            foreach (var col in CsvExportRunner.DerivedColumns)
            {
                Assert.Contains(col, headerSet);
            }

            // Derived columns must be at the end, in fixed order.
            var headerList = result.HeaderColumns;
            var derivedStart = headerList.Count - CsvExportRunner.DerivedColumns.Length;
            for (var i = 0; i < CsvExportRunner.DerivedColumns.Length; i++)
            {
                Assert.Equal(CsvExportRunner.DerivedColumns[i], headerList[derivedStart + i]);
            }
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that the row for id=1001 has populated derived fields from the sidecar.
    /// </summary>
    [Fact]
    public void Export_WithDerived_PopulatesMatchingRow()
    {
        var (jsonlPath, derivedPath, csvPath, tempDir) = SetupTempFiles(withDerived: true);

        try
        {
            var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
            Assert.False(result.DerivedFileMissing);

            var lines = File.ReadAllLines(csvPath);
            Assert.True(lines.Length >= 2, "CSV should have header + at least one data row.");

            var headerLine = lines[0];
            var headers = headerLine.Split(',');

            // Find the index of "happy_before_train".
            var happyBeforeIdx = Array.IndexOf(headers, "happy_before_train");
            Assert.True(happyBeforeIdx >= 0, "happy_before_train column not found in header.");

            // Find the row for id=1001 (first data row).
            var idIdx = Array.IndexOf(headers, "id");
            Assert.True(idIdx >= 0, "id column not found in header.");

            string? row1001 = null;
            for (var i = 1; i < lines.Length; i++)
            {
                var fields = CsvSplit(lines[i]);
                if (fields.Length > idIdx && fields[idIdx] == "1001")
                {
                    row1001 = lines[i];
                    break;
                }
            }

            Assert.NotNull(row1001);
            var rowFields = CsvSplit(row1001);
            Assert.Equal("4500", rowFields[happyBeforeIdx]);

            // Verify happy_after_train for id=1001.
            var happyAfterIdx = Array.IndexOf(headers, "happy_after_train");
            Assert.Equal("3975", rowFields[happyAfterIdx]);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that rows without a matching derived record have blank derived columns.
    /// </summary>
    [Fact]
    public void Export_WithDerived_NonMatchingRowsAreBlank()
    {
        var (jsonlPath, derivedPath, csvPath, tempDir) = SetupTempFiles(withDerived: true);

        try
        {
            var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");

            var lines = File.ReadAllLines(csvPath);
            var headers = CsvSplit(lines[0]);
            var idIdx = Array.IndexOf(headers, "id");
            var happyBeforeIdx = Array.IndexOf(headers, "happy_before_train");

            // Row for id=1002 (no derived record) should have blank derived columns.
            for (var i = 1; i < lines.Length; i++)
            {
                var fields = CsvSplit(lines[i]);
                if (fields.Length > idIdx && fields[idIdx] == "1002")
                {
                    Assert.True(happyBeforeIdx < fields.Length, "Row too short for derived columns.");
                    Assert.Equal(string.Empty, fields[happyBeforeIdx]);
                    return;
                }
            }

            Assert.Fail("Row with id=1002 not found in CSV.");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that export succeeds when the derived sidecar is missing.
    /// Derived columns exist in the header but values are blank.
    /// </summary>
    [Fact]
    public void Export_WithoutDerived_SucceedsWithBlankColumns()
    {
        var (jsonlPath, derivedPath, csvPath, tempDir) = SetupTempFiles(withDerived: false);

        try
        {
            // Pass a path to a non-existent derived file.
            var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
            Assert.True(result.DerivedFileMissing, "Expected DerivedFileMissing to be true.");

            // Header still has derived columns.
            var headerSet = new HashSet<string>(result.HeaderColumns, StringComparer.Ordinal);
            foreach (var col in CsvExportRunner.DerivedColumns)
            {
                Assert.Contains(col, headerSet);
            }

            // All data rows should have blank derived fields.
            var lines = File.ReadAllLines(csvPath);
            var headers = CsvSplit(lines[0]);
            var happyBeforeIdx = Array.IndexOf(headers, "happy_before_train");
            Assert.True(happyBeforeIdx >= 0);

            for (var i = 1; i < lines.Length; i++)
            {
                var fields = CsvSplit(lines[i]);
                if (happyBeforeIdx < fields.Length)
                {
                    Assert.Equal(string.Empty, fields[happyBeforeIdx]);
                }
            }
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that export succeeds with no derived path at all (null).
    /// </summary>
    [Fact]
    public void Export_NullDerivedPath_Succeeds()
    {
        var (jsonlPath, _, csvPath, tempDir) = SetupTempFiles(withDerived: false);

        try
        {
            var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedJsonlPath: null);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
            Assert.True(result.DerivedFileMissing);

            var headerSet = new HashSet<string>(result.HeaderColumns, StringComparer.Ordinal);
            foreach (var col in CsvExportRunner.DerivedColumns)
            {
                Assert.Contains(col, headerSet);
            }
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that malformed lines in the derived file are skipped and counted.
    /// </summary>
    [Fact]
    public void Export_MalformedDerivedLines_SkippedAndCounted()
    {
        var (jsonlPath, derivedPath, csvPath, tempDir) = SetupTempFiles(withDerived: true);

        try
        {
            // Append a malformed line to the derived file.
            File.AppendAllText(derivedPath, "{invalid-json}\n");

            var result = CsvExportRunner.Run(jsonlPath, csvPath, derivedPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
            Assert.Equal(1, result.DerivedMalformedLines);

            // The valid derived record for id=1001 should still be present.
            var lines = File.ReadAllLines(csvPath);
            var headers = CsvSplit(lines[0]);
            var idIdx = Array.IndexOf(headers, "id");
            var happyBeforeIdx = Array.IndexOf(headers, "happy_before_train");

            for (var i = 1; i < lines.Length; i++)
            {
                var fields = CsvSplit(lines[i]);
                if (fields.Length > idIdx && fields[idIdx] == "1001")
                {
                    Assert.Equal("4500", fields[happyBeforeIdx]);
                    return;
                }
            }

            Assert.Fail("Row with id=1001 not found.");
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    // --- Helpers ---

    private static (string jsonlPath, string derivedPath, string csvPath, string tempDir)
        SetupTempFiles(bool withDerived)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests.DerivedJoin", Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(tempDir, "data");
        var derivedDir = Path.Combine(dataDir, "derived");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(derivedDir);

        // Copy userlogs fixture.
        var fixtureSource = FixturePath("userlogs-small.jsonl");
        var jsonlPath = Path.Combine(dataDir, "userlogs.jsonl");
        File.Copy(fixtureSource, jsonlPath, overwrite: true);

        // Optionally copy derived fixture.
        var derivedPath = Path.Combine(derivedDir, "derived-gymtrains.jsonl");
        if (withDerived)
        {
            var derivedFixtureSource = FixturePath("derived-gymtrains-small.jsonl");
            File.Copy(derivedFixtureSource, derivedPath, overwrite: true);
        }
        // If !withDerived, derivedPath points to a non-existent file.

        var csvDir = Path.Combine(dataDir, "export");
        var csvPath = Path.Combine(csvDir, "userlogs.csv");

        return (jsonlPath, derivedPath, csvPath, tempDir);
    }

    private static void Cleanup(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
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
