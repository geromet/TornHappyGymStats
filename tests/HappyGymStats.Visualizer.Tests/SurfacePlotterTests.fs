module SurfacePlotterTests

open System
open System.IO
open Xunit
open HappyGymStats.Visualizer
open HappyGymStats.Visualizer.SurfaceBinner
open HappyGymStats.Visualizer.SurfacePlotter

// ── Helpers ────────────────────────────────────────────────────────────────

/// Resolve fixture path relative to the test project source directory.
let private fixturePath filename =
    Path.Combine(__SOURCE_DIRECTORY__, "fixtures", filename)

/// Create a temp directory for test output; returns the directory path.
let private createTempDir () =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "surfaceplotter_test_%d" (DateTime.Now.Ticks))
    Directory.CreateDirectory(dir) |> ignore
    dir

// ── Test 1: generatePlot creates an HTML file ──────────────────────────────

[<Fact>]
let ``generatePlot creates HTML file with Plotly content`` () =
    let grid : GridResult = {
        XValues = [ 100.0; 105.0 ]
        YValues = [ 1995.0; 2000.0 ]
        ZMatrix = [
            [ Double.NaN; 0.22 ]
            [ 0.2; Double.NaN ]
        ]
    }
    let dir = createTempDir ()
    try
        let outputPath = Path.Combine(dir, "test_plot.html")
        let result = generatePlot grid "Test Surface" outputPath

        Assert.True(File.Exists result)
        let html = File.ReadAllText result
        Assert.Contains("plotly", html, StringComparison.OrdinalIgnoreCase)
        Assert.Contains("Test Surface", html)

        // Ensure we patched NaN into null (so Plotly.js will render)
        Assert.DoesNotContain("\"NaN\"", html)
    finally
        Directory.Delete(dir, recursive = true)

// ── Test 2: generatePlot includes axis labels in HTML ──────────────────────

[<Fact>]
let ``generatePlot HTML contains axis labels`` () =
    let grid : GridResult = {
        XValues = [ 50.0 ]
        YValues = [ 2000.0 ]
        ZMatrix = [ [ 0.12 ] ]
    }
    let dir = createTempDir ()
    try
        let outputPath = Path.Combine(dir, "axis_test.html")
        generatePlot grid "Axis Test" outputPath |> ignore

        let html = File.ReadAllText outputPath
        Assert.Contains("Stat before train", html)
        Assert.Contains("Happy before train", html)
        Assert.Contains("Stat gained / energy", html)
    finally
        Directory.Delete(dir, recursive = true)

// ── Test 3: generatePlot with empty grid creates valid HTML ────────────────

[<Fact>]
let ``generatePlot handles empty grid`` () =
    let grid : GridResult = {
        XValues = []
        YValues = []
        ZMatrix = []
    }
    let dir = createTempDir ()
    try
        let outputPath = Path.Combine(dir, "empty_plot.html")
        let result = generatePlot grid "Empty Surface" outputPath

        Assert.True(File.Exists result)
        let html = File.ReadAllText result
        Assert.Contains("plotly", html, StringComparison.OrdinalIgnoreCase)
    finally
        Directory.Delete(dir, recursive = true)

// ── Test 4: Full pipeline from CSV through to HTML (Strength) ──────────────

[<Fact>]
let ``full pipeline generates Strength surface plot from fixture CSV`` () =
    let readResult = CsvStatReader.readStatRecords (fixturePath "sample_gym.csv")
    let strengthRecs = readResult.Records |> List.filter (fun r -> r.StatType = Strength)
    Assert.NotEmpty strengthRecs

    let grid = binRecords strengthRecs
    let dir = createTempDir ()
    try
        let outputPath = Path.Combine(dir, "Strength.html")
        let result = generatePlot grid "Strength Stat Gain / Energy Surface" outputPath

        Assert.True(File.Exists result)
        let html = File.ReadAllText result
        Assert.Contains("plotly", html, StringComparison.OrdinalIgnoreCase)
        Assert.Contains("Strength", html)
        Assert.Contains("Stat before train", html)
        Assert.Contains("Happy before train", html)
        Assert.Contains("Stat gained / energy", html)
        Assert.Contains("\"type\":\"surface\"", html, StringComparison.OrdinalIgnoreCase)
        Assert.DoesNotContain("\"NaN\"", html)
    finally
        Directory.Delete(dir, recursive = true)

// ── Test 5: generatePlots produces files for all stat types in data ────────

[<Fact>]
let ``generatePlots creates HTML files for each stat type in fixture data`` () =
    let readResult = CsvStatReader.readStatRecords (fixturePath "sample_gym.csv")
    Assert.NotEmpty readResult.Records

    let dir = createTempDir ()
    try
        let paths = generatePlots readResult.Records dir

        // Fixture has Strength and Defense records
        Assert.True(paths.Length >= 1, sprintf "Expected at least 1 plot, got %d" paths.Length)

        for path in paths do
            Assert.True(File.Exists path, sprintf "File should exist: %s" path)
            let html = File.ReadAllText path
            Assert.Contains("plotly", html, StringComparison.OrdinalIgnoreCase)
            Assert.Contains("Stat before train", html)
            Assert.Contains("Happy before train", html)
            Assert.Contains("Stat gained / energy", html)
            Assert.Contains("\"type\":\"surface\"", html, StringComparison.OrdinalIgnoreCase)
            Assert.DoesNotContain("\"NaN\"", html)

        // Strength should be present
        Assert.Contains(Path.Combine(dir, "Strength.html"), paths)
    finally
        Directory.Delete(dir, recursive = true)

// ── Test 6: generateStackedPlots produces one combined HTML ────────────────

[<Fact>]
let ``generateStackedPlots creates a single Surfaces.html file`` () =
    let readResult = CsvStatReader.readStatRecords (fixturePath "sample_gym.csv")
    Assert.NotEmpty readResult.Records

    let dir = createTempDir ()
    try
        let paths = generateStackedPlots readResult.Records dir
        Assert.Single(paths) |> ignore

        let outPath = Path.Combine(dir, "Surfaces.html")
        Assert.True(File.Exists outPath)

        let html = File.ReadAllText outPath
        Assert.Contains("HappyGymStats — Surfaces", html)
        Assert.Contains("All stats (avg)", html)
        Assert.Contains("Strength", html)
        Assert.Contains("Defense", html)
        Assert.Contains("Plotly.newPlot", html)
        Assert.Contains("height: 820px", html)
        Assert.DoesNotContain("\"NaN\"", html)
    finally
        Directory.Delete(dir, recursive = true)
