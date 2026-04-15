namespace HappyGymStats.Visualizer

open System
open System.IO

/// Core CSV stat reader — parses exported userlogs.csv and extracts gym training records.
module CsvStatReader =

    // ── RFC 4180 field parser ──────────────────────────────────────────────
    /// Parses a single CSV line into an array of fields, correctly handling
    /// quoted fields that may contain embedded commas and escaped quotes ("").
    let parseCsvLine (line: string) : string array =
        let fields = ResizeArray<string>()
        let mutable i = 0
        let len = line.Length

        while i < len do
            if line.[i] = '"' then
                // Quoted field — consume until closing quote, handling "" escapes
                i <- i + 1
                let buf = Text.StringBuilder()
                let mutable doneField = false
                while i < len && not doneField do
                    if line.[i] = '"' then
                        if i + 1 < len && line.[i + 1] = '"' then
                            // Escaped quote ""
                            buf.Append('"') |> ignore
                            i <- i + 2
                        else
                            // Closing quote
                            i <- i + 1
                            doneField <- true
                    else
                        buf.Append(line.[i]) |> ignore
                        i <- i + 1
                // Skip optional comma after closing quote
                if i < len && line.[i] = ',' then i <- i + 1
                fields.Add(buf.ToString())
            else
                // Unquoted field — consume until comma or end
                let start = i
                while i < len && line.[i] <> ',' do i <- i + 1
                fields.Add(line.[start .. i - 1])
                if i < len && line.[i] = ',' then i <- i + 1

        fields.ToArray()

    // ── Helpers ────────────────────────────────────────────────────────────
    let private tryParseFloat (s: string) : float option =
        match String.IsNullOrWhiteSpace(s) with
        | true -> None
        | false ->
            match Double.TryParse(s, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, v -> Some v
            | false, _ -> None

    let private statTypeFromTitle (title: string) : StatType option =
        if   title.Contains("strength", StringComparison.OrdinalIgnoreCase) then Some Strength
        elif title.Contains("defense",  StringComparison.OrdinalIgnoreCase) then Some Defense
        elif title.Contains("speed",    StringComparison.OrdinalIgnoreCase) then Some Speed
        elif title.Contains("dexterity",StringComparison.OrdinalIgnoreCase) then Some Dexterity
        else None

    /// Returns (before_col, increased_normalized_col, increased_raw_col) column names for a given stat type.
    /// The export pipeline may add *_increased_normalized columns when gyms.json is available.
    let private columnValuesForStat (statType: StatType) : string * string * string =
        match statType with
        | Strength -> ("data.strength_before",  "data.strength_increased_normalized",  "data.strength_increased")
        | Defense  -> ("data.defense_before",   "data.defense_increased_normalized",   "data.defense_increased")
        | Speed    -> ("data.speed_before",     "data.speed_increased_normalized",     "data.speed_increased")
        | Dexterity-> ("data.dexterity_before", "data.dexterity_increased_normalized", "data.dexterity_increased")

    // ── Public API ─────────────────────────────────────────────────────────
    /// Reads the CSV file at the given path and extracts gym training stat records.
    /// Returns a ReadResult with successfully parsed records and any parse errors.
    let readStatRecords (filePath: string) : ReadResult =
        let lines = File.ReadAllLines(filePath)
        if lines.Length = 0 then
            { Records = []; ParseErrors = [] }
        else
            // Parse header to build column-name → index map
            let headers = parseCsvLine lines.[0]
            let colIndex =
                headers
                |> Array.mapi (fun i h -> h.Trim(), i)
                |> dict

            let tryGetCol (name: string) =
                match colIndex.TryGetValue(name) with
                | true, idx -> Some idx
                | false, _ -> None

            let happyBeforeTrainIdx = tryGetCol "happy_before_train"
            let happyBeforeEventIdx = tryGetCol "happy_before_event"
            let energyUsedIdx       = tryGetCol "data.energy_used"
            let detailsTitleIdx     = tryGetCol "details.title"

            // Debug CSV schema support (fixed columns).
            let statTypeIdx     = tryGetCol "stat_type"
            let statBeforeIdx   = tryGetCol "stat_before"
            let statIncreasedIdx= tryGetCol "stat_increased"

            let debugSchema = statTypeIdx.IsSome && statBeforeIdx.IsSome && statIncreasedIdx.IsSome

            let records = ResizeArray<StatRecord>()
            let errors  = ResizeArray<string>()

            // Pre-compute column lookups for each stat type
            let statColMap =
                [ Strength; Defense; Speed; Dexterity ]
                |> List.map (fun st ->
                    let (beforeCol, increasedNormCol, increasedRawCol) = columnValuesForStat st
                    let increasedIdx =
                        match tryGetCol increasedNormCol with
                        | Some idx -> Some idx
                        | None -> tryGetCol increasedRawCol

                    st, (tryGetCol beforeCol, increasedIdx))
                |> Map.ofList

            for rowIdx in 1 .. lines.Length - 1 do
                let line = lines.[rowIdx]
                if String.IsNullOrWhiteSpace line then () else

                let fields = parseCsvLine line

                let getField (idx: int) =
                    if idx < fields.Length then fields.[idx].Trim() else ""

                if debugSchema then
                    // Debug schema: stat_type/stat_before/stat_increased columns exist.
                    match statTypeIdx, statBeforeIdx, statIncreasedIdx with
                    | Some stIdx, Some beforeIdx, Some increasedIdx ->
                        let stText = getField stIdx
                        let statTypeOpt =
                            match stText.Trim().ToLowerInvariant() with
                            | "strength" -> Some Strength
                            | "defense" -> Some Defense
                            | "speed" -> Some Speed
                            | "dexterity" -> Some Dexterity
                            | _ -> None

                        match statTypeOpt with
                        | None -> ()
                        | Some statType ->
                            let happyOpt =
                                match happyBeforeTrainIdx, happyBeforeEventIdx with
                                | Some idx, _ -> tryParseFloat (getField idx)
                                | None, Some idx -> tryParseFloat (getField idx)
                                | None, None -> None

                            let energyOpt =
                                match energyUsedIdx with
                                | Some idx -> tryParseFloat (getField idx)
                                | None -> None

                            let beforeOpt    = tryParseFloat (getField beforeIdx)
                            let increasedOpt = tryParseFloat (getField increasedIdx)

                            match beforeOpt, happyOpt, increasedOpt, energyOpt with
                            | Some before, Some happy, Some increased, Some energy when energy > 0.0 ->
                                records.Add {
                                    StatType = statType
                                    StatBefore = before
                                    HappyBeforeTrain = happy
                                    StatIncreased = increased
                                    EnergyUsed = energy
                                }
                            | _ ->
                                errors.Add(
                                    sprintf "Row %d: missing numeric fields (debug schema; stat=%A before=%A happy=%A increased=%A energy=%A)"
                                        (rowIdx + 1) statType beforeOpt happyOpt increasedOpt energyOpt)
                    | _ -> ()
                else
                    // Legacy schema: detect via details.title and stat-specific columns.
                    match detailsTitleIdx with
                    | None -> ()
                    | Some titleIdx ->
                        let title = getField titleIdx
                        match statTypeFromTitle title with
                        | None -> ()  // Not a gym train row — skip
                        | Some statType ->
                            // Extract happy_before_train (preferred) or happy_before_event (debug schema)
                            let happyOpt =
                                match happyBeforeTrainIdx, happyBeforeEventIdx with
                                | Some idx, _ -> tryParseFloat (getField idx)
                                | None, Some idx -> tryParseFloat (getField idx)
                                | None, None -> None

                            let energyOpt =
                                match energyUsedIdx with
                                | Some idx -> tryParseFloat (getField idx)
                                | None -> None

                            // Extract stat-specific before and increased values
                            match Map.find statType statColMap with
                            | Some beforeIdx, Some increasedIdx ->
                                let beforeOpt    = tryParseFloat (getField beforeIdx)
                                let increasedOpt = tryParseFloat (getField increasedIdx)

                                match beforeOpt, happyOpt, increasedOpt, energyOpt with
                                | Some before, Some happy, Some increased, Some energy when energy > 0.0 ->
                                    records.Add {
                                        StatType = statType
                                        StatBefore = before
                                        HappyBeforeTrain = happy
                                        StatIncreased = increased
                                        EnergyUsed = energy
                                    }
                                | _ ->
                                    errors.Add(
                                        sprintf "Row %d: missing numeric fields (stat=%A before=%A happy=%A increased=%A energy=%A)"
                                            (rowIdx + 1) statType beforeOpt happyOpt increasedOpt energyOpt)
                            | _ ->
                                errors.Add(
                                    sprintf "Row %d: missing column mapping for stat type %A"
                                        (rowIdx + 1) statType)

            { Records = List.ofSeq records; ParseErrors = List.ofSeq errors }
