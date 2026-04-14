module CsvStatReaderTests

open System
open System.IO
open Xunit
open HappyGymStats.Visualizer
open HappyGymStats.Visualizer.CsvStatReader

/// Resolve fixture path relative to the test project source directory.
let private fixturePath filename =
    Path.Combine(__SOURCE_DIRECTORY__, "fixtures", filename)

/// Resolve path to the real export CSV if it exists.
let private realCsvPath =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "..", "..", "dist", "linux-x64", "data", "export", "userlogs.csv")

// ── Test 1: Strength records ────────────────────────────────────────────────

[<Fact>]
let ``ReadStatRecords returns correct strength records`` () =
    let result = readStatRecords (fixturePath "sample_gym.csv")
    let strengthRecs = result.Records |> List.filter (fun r -> r.StatType = Strength)
    Assert.Equal(2, strengthRecs.Length)

    let first = strengthRecs.[0]
    Assert.Equal(Strength, first.StatType)
    Assert.Equal(100.0, first.StatBefore)
    Assert.Equal(2000.0, first.HappyBeforeTrain)
    Assert.Equal(5.0, first.StatIncreased)
    Assert.Equal(25.0, first.EnergyUsed)

    let second = strengthRecs.[1]
    Assert.Equal(Strength, second.StatType)
    Assert.Equal(105.0, second.StatBefore)
    Assert.Equal(1995.0, second.HappyBeforeTrain)
    Assert.Equal(5.5, second.StatIncreased)
    Assert.Equal(25.0, second.EnergyUsed)

// ── Test 2: Defense records ─────────────────────────────────────────────────

[<Fact>]
let ``ReadStatRecords returns correct defense records`` () =
    let result = readStatRecords (fixturePath "sample_gym.csv")
    let defenseRecs = result.Records |> List.filter (fun r -> r.StatType = Defense)
    Assert.Single(defenseRecs) |> ignore

    let rec_ = defenseRecs.[0]
    Assert.Equal(Defense, rec_.StatType)
    Assert.Equal(50.0, rec_.StatBefore)
    Assert.Equal(2000.0, rec_.HappyBeforeTrain)
    Assert.Equal(3.0, rec_.StatIncreased)
    Assert.Equal(25.0, rec_.EnergyUsed)

// ── Test 3: Non-gym rows are skipped ────────────────────────────────────────

[<Fact>]
let ``ReadStatRecords skips non-gym rows`` () =
    let result = readStatRecords (fixturePath "sample_gym.csv")
    // 2 strength + 1 defense = 3 total; the item-use row is skipped
    Assert.Equal(3, result.Records.Length)
    Assert.Empty result.ParseErrors

// ── Test 4: Empty CSV (header only) ─────────────────────────────────────────

[<Fact>]
let ``ReadStatRecords handles empty CSV with header only`` () =
    // Create a temporary empty CSV with just the header
    let tmpFile = Path.Combine(Path.GetTempPath(), sprintf "empty_test_%d.csv" (DateTime.Now.Ticks))
    try
        let header = "id,timestamp,data,data.defense_after,data.defense_before,data.defense_increased,data.dexterity_after,data.dexterity_before,data.dexterity_increased,data.energy_decreased,data.energy_increased,data.energy_used,data.faction,data.gym,data.happy_decreased,data.happy_increased,data.happy_used,data.hospital_time_increased,data.item,data.maximum_happy_after,data.maximum_happy_before,data.nerve_decreased,data.nerve_increased,data.speed_after,data.speed_before,data.speed_increased,data.strength_after,data.strength_before,data.strength_increased,data.trains,data.user,details,details.category,details.id,details.title,params,params.changed,params.color,params.italic,happy_before_train,happy_after_train,regen_ticks_applied,regen_happy_gained,max_happy_at_time_utc,clamped_to_max"
        File.WriteAllText(tmpFile, header)
        let result = readStatRecords tmpFile
        Assert.Empty result.Records
        Assert.Empty result.ParseErrors
    finally
        if File.Exists(tmpFile) then File.Delete(tmpFile)

// ── Test 5: Missing file throws FileNotFoundException ──────────────────────

[<Fact>]
let ``ReadStatRecords throws FileNotFoundException for missing file`` () =
    let missing = Path.Combine(Path.GetTempPath(), sprintf "nonexistent_%d.csv" (Guid.NewGuid().GetHashCode()))
    let action = Action(fun () -> readStatRecords missing |> ignore)
    Assert.Throws<FileNotFoundException>(action) |> ignore

// ── Test 6: Parses real CSV if available ────────────────────────────────────

[<Fact(Skip = "Requires dist output")>]
let ``ReadStatRecords parses real CSV export`` () =
    let result = readStatRecords realCsvPath
    Assert.NotEmpty result.Records
    // All records should have non-negative values
    for r in result.Records do
        Assert.True(r.StatBefore >= 0.0)
        Assert.True(r.StatIncreased > 0.0)
        Assert.True(r.HappyBeforeTrain >= 0.0)
        Assert.True(r.EnergyUsed > 0.0)
