using HappyGymStats.Export;
using HappyGymStats.Reconstruction;

namespace HappyGymStats.Tests.Export;

public sealed class CsvHeaderCoverageTests
{
    /// <summary>
    /// Verifies that the 2-pass CSV export produces a deterministic union-of-keys header
    /// that covers all expected keys from the fixture dataset, including nested dotted paths
    /// and parent object keys.
    /// </summary>
    [Fact]
    public void Export_ProducesExpectedHeaderKeys()
    {
        var (jsonlPath, csvPath, tempDir) = SetupTempFiles();

        try
        {
            var result = CsvExportRunner.Run(jsonlPath, csvPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
            Assert.True(result.RowsWritten > 0, "Expected at least one row to be written.");
            Assert.NotNull(result.ReaderStats);
            Assert.Equal(3, result.ReaderStats.RecordsYielded);

            // Verify canonical prefix order: id, timestamp must come first.
            AssertHeaderStartsWith(result.HeaderColumns, "id", "timestamp");

            // Verify required keys are present in the header.
            var headerSet = new HashSet<string>(result.HeaderColumns, StringComparer.Ordinal);

            Assert.Contains("id", headerSet);
            Assert.Contains("timestamp", headerSet);
            Assert.Contains("details", headerSet);
            Assert.Contains("details.title", headerSet);
            Assert.Contains("details.category", headerSet);
            Assert.Contains("data", headerSet);
            Assert.Contains("data.happy_used", headerSet);
            Assert.Contains("data.maximum_happy_after", headerSet);
            Assert.Contains("data.destination", headerSet);

            // Verify the CSV file exists and has content.
            Assert.True(File.Exists(csvPath), "CSV output file should exist.");
            var lines = File.ReadAllLines(csvPath);
            Assert.True(lines.Length >= 2, "CSV should have at least a header line and one data row.");

            // First line is the header.
            var headerLine = lines[0];
            Assert.Contains("id", headerLine);
            Assert.Contains("timestamp", headerLine);
            Assert.Contains("data.happy_used", headerLine);        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that malformed and blank lines in the JSONL are skipped without crashing.
    /// </summary>
    [Fact]
    public void Export_SkipsMalformedLines()
    {
        var (jsonlPath, csvPath, tempDir) = SetupTempFiles();

        try
        {
            var result = CsvExportRunner.Run(jsonlPath, csvPath);

            Assert.True(result.Success, $"Export failed: {result.ErrorMessage}");
            Assert.NotNull(result.ReaderStats);
            Assert.Equal(1, result.ReaderStats.BlankLines);
            Assert.Equal(1, result.ReaderStats.MalformedLines);
            Assert.Equal(3, result.ReaderStats.RecordsYielded);
            Assert.Equal(3, result.RowsWritten);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies that a missing source JSONL file returns a failed result with a clear message.
    /// </summary>
    [Fact]
    public void Export_WhenSourceMissing_ReturnsFailedResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests.Export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var missingJsonl = Path.Combine(tempDir, "nonexistent.jsonl");
        var csvPath = Path.Combine(tempDir, "output.csv");

        try
        {
            var result = CsvExportRunner.Run(missingJsonl, csvPath);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, result.RowsWritten);
        }
        finally
        {
            Cleanup(tempDir);
        }
    }

    /// <summary>
    /// Verifies CSV escaping for values containing commas, quotes, and newlines.
    /// </summary>
    [Fact]
    public void CsvWriter_EscapesSpecialCharacters()
    {
        // Simple values — no quoting needed.
        Assert.Equal("hello", CsvWriter.EscapeField("hello"));
        Assert.Equal("42", CsvWriter.EscapeField("42"));

        // Comma requires quoting.
        Assert.Equal("\"hello,world\"", CsvWriter.EscapeField("hello,world"));

        // Double quote requires quoting and doubling.
        Assert.Equal("\"say \"\"hi\"\"\"", CsvWriter.EscapeField("say \"hi\""));

        // Newline requires quoting.
        Assert.Equal("\"line1\nline2\"", CsvWriter.EscapeField("line1\nline2"));

        // Carriage return requires quoting.
        Assert.Equal("\"a\rb\"", CsvWriter.EscapeField("a\rb"));
    }

    /// <summary>
    /// Verifies that JsonFlattener correctly flattens nested objects into dotted paths
    /// and includes parent object keys with compact JSON values.
    /// </summary>
    [Fact]
    public void JsonFlattener_FlattensNestedObjects()
    {
        var json = """{"id":1001,"details":{"happy_used":25}}""";
        var flat = JsonFlattener.Flatten(json);

        Assert.Equal("1001", flat["id"]);
        Assert.Equal("25", flat["details.happy_used"]);
        Assert.Contains("details", flat.Keys);
        // Parent object value should be compact JSON.
        Assert.Equal("""{"happy_used":25}""", flat["details"]);
    }

    /// <summary>
    /// Verifies that JsonFlattener handles primitive, null, and boolean values.
    /// </summary>
    [Fact]
    public void JsonFlattener_HandlesPrimitives()
    {
        var json = """{"name":"test","count":42,"active":true,"deleted":false,"note":null}""";
        var flat = JsonFlattener.Flatten(json);

        Assert.Equal("test", flat["name"]);
        Assert.Equal("42", flat["count"]);
        Assert.Equal("true", flat["active"]);
        Assert.Equal("false", flat["deleted"]);
        Assert.Equal(string.Empty, flat["note"]);
    }

    /// <summary>
    /// Verifies deterministic header order: canonical prefix first, remaining sorted ordinally.
    /// </summary>
    [Fact]
    public void BuildHeaderOrder_CanonicalPrefixFirst_ThenSorted()
    {
        var headerSet = new HashSet<string>(StringComparer.Ordinal)
        {
            "data.happy_used", "id", "timestamp", "data", "zzz_last"
        };

        var order = CsvExportRunner.BuildHeaderOrder(headerSet);

        Assert.Equal("id", order[0]);
        Assert.Equal("timestamp", order[1]);

        // Remaining sorted ordinally.
        var remaining = order.Skip(2).ToList();
        Assert.Equal(new[] { "data", "data.happy_used", "zzz_last" }, remaining);
    }

    // --- Helpers ---

    private static (string jsonlPath, string csvPath, string tempDir) SetupTempFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HappyGymStats.Tests.Export", Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(tempDir, "data");
        Directory.CreateDirectory(dataDir);

        // Copy fixture into temp data directory.
        var fixtureSource = FixturePath("userlogs-small.jsonl");
        var jsonlPath = Path.Combine(dataDir, "userlogs.jsonl");
        File.Copy(fixtureSource, jsonlPath, overwrite: true);

        var csvDir = Path.Combine(dataDir, "export");
        var csvPath = Path.Combine(csvDir, "userlogs.csv");

        return (jsonlPath, csvPath, tempDir);
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

    private static void AssertHeaderStartsWith(IReadOnlyList<string> header, params string[] expectedPrefix)
    {
        for (var i = 0; i < expectedPrefix.Length; i++)
        {
            Assert.True(
                i < header.Count && string.Equals(header[i], expectedPrefix[i], StringComparison.Ordinal),
                $"Expected header[{i}] to be '{expectedPrefix[i]}', but was '{(i < header.Count ? header[i] : "<missing>")}'.");
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
}
