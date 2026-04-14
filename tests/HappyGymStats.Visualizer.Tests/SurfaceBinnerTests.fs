module SurfaceBinnerTests

open System
open Xunit
open HappyGymStats.Visualizer
open HappyGymStats.Visualizer.SurfaceBinner

// ── Helpers ────────────────────────────────────────────────────────────────

/// Create a StatRecord with the given values.
let private mkRecord statType statBefore happy statIncreased energyUsed : StatRecord = {
    StatType = statType
    StatBefore = statBefore
    HappyBeforeTrain = happy
    StatIncreased = statIncreased
    EnergyUsed = energyUsed
}

// ── Test 1: Empty input produces empty grid ────────────────────────────────

[<Fact>]
let ``binRecords returns empty grid for empty input`` () =
    let result = binRecords []
    Assert.Empty result.XValues
    Assert.Empty result.YValues
    Assert.Empty result.ZMatrix

// ── Test 2: Single record produces 1x1 grid ────────────────────────────────

[<Fact>]
let ``binRecords produces 1x1 grid for single record`` () =
    let records = [ mkRecord Strength 100.0 2000.0 5.0 25.0 ]
    let result = binRecords records

    Assert.Equal<float list>([ 100.0 ], result.XValues)
    Assert.Equal<float list>([ 2000.0 ], result.YValues)
    Assert.Equal(1, result.ZMatrix.Length)
    Assert.Equal(1, result.ZMatrix.[0].Length)
    // 5 / 25 = 0.2
    Assert.Equal(0.2, result.ZMatrix.[0].[0])

// ── Test 3: Grid dimensions match unique axis values (small datasets) ──────

[<Fact>]
let ``binRecords grid dimensions match unique axis values`` () =
    // 2 distinct X values (50, 100), 2 distinct Y values (1995, 2000)
    let records = [
        mkRecord Defense  50.0  2000.0 3.0 25.0
        mkRecord Strength 100.0 2000.0 5.0 25.0
        mkRecord Strength 100.0 1995.0 5.5 25.0
    ]
    let result = binRecords records

    Assert.Equal<float list>([ 50.0; 100.0 ], result.XValues)
    Assert.Equal<float list>([ 1995.0; 2000.0 ], result.YValues)
    Assert.Equal(2, result.ZMatrix.Length)
    for row in result.ZMatrix do
        Assert.Equal(2, row.Length)

// ── Test 4: Bin values are means of matching records ───────────────────────

[<Fact>]
let ``binRecords computes mean stat gain per energy per bin`` () =
    // Two records in the same bin (100, 2000)
    // per-energy values: 5/25=0.2 and 7/25=0.28 => mean = 0.24
    let records = [
        mkRecord Strength 100.0 2000.0 5.0 25.0
        mkRecord Strength 100.0 2000.0 7.0 25.0
        mkRecord Strength 200.0 2000.0 10.0 25.0 // per-energy = 0.4
    ]
    let result = binRecords records

    Assert.Equal<float list>([ 100.0; 200.0 ], result.XValues)
    Assert.Equal<float list>([ 2000.0 ], result.YValues)

    Assert.Equal(0.24, result.ZMatrix.[0].[0], 12)
    Assert.Equal(0.4, result.ZMatrix.[0].[1], 12)

// ── Test 5: Empty bins produce NaN ─────────────────────────────────────────

[<Fact>]
let ``binRecords fills empty bins with NaN`` () =
    let records = [
        mkRecord Strength 100.0 2000.0 5.0 25.0
        mkRecord Strength 200.0 1995.0 9.0 25.0
    ]
    let result = binRecords records

    Assert.True(Double.IsNaN result.ZMatrix.[0].[0])
    Assert.Equal(9.0 / 25.0, result.ZMatrix.[0].[1])
    Assert.Equal(5.0 / 25.0, result.ZMatrix.[1].[0])
    Assert.True(Double.IsNaN result.ZMatrix.[1].[1])

// ── Test 6: Axis values are sorted ascending (small datasets) ──────────────

[<Fact>]
let ``binRecords sorts axis values ascending`` () =
    let records = [
        mkRecord Strength 300.0 1000.0 8.0 25.0
        mkRecord Strength 100.0 3000.0 4.0 25.0
        mkRecord Strength 200.0 2000.0 6.0 25.0
    ]
    let result = binRecords records

    Assert.Equal<float list>([ 100.0; 200.0; 300.0 ], result.XValues)
    Assert.Equal<float list>([ 1000.0; 2000.0; 3000.0 ], result.YValues)

// ── Test 7: Works with fixture CSV data via CsvStatReader ──────────────────

[<Fact>]
let ``binRecords works with real fixture CSV data`` () =
    let fixturePath = System.IO.Path.Combine(__SOURCE_DIRECTORY__, "fixtures", "sample_gym.csv")
    let readResult = CsvStatReader.readStatRecords fixturePath

    let strengthRecs = readResult.Records |> List.filter (fun r -> r.StatType = Strength)
    Assert.NotEmpty strengthRecs

    let grid = binRecords strengthRecs

    Assert.Equal(2, grid.XValues.Length)
    Assert.Equal(2, grid.YValues.Length)
    Assert.Equal(2, grid.ZMatrix.Length)

    Assert.True(Double.IsNaN grid.ZMatrix.[0].[0])
    Assert.Equal(5.5 / 25.0, grid.ZMatrix.[0].[1])

    Assert.Equal(5.0 / 25.0, grid.ZMatrix.[1].[0])
    Assert.True(Double.IsNaN grid.ZMatrix.[1].[1])
