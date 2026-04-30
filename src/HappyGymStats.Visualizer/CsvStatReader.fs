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

    /// Returns (before_col, after_col, increased_normalized_col, increased_raw_col) column names for a given stat type.
    /// The export pipeline may add *_increased_normalized columns when gyms.json is available.
    let private columnValuesForStat (statType: StatType) : string * string * string * string =
        match statType with
        | Strength -> ("data.strength_before",  "data.strength_after",  "data.strength_increased_normalized",  "data.strength_increased")
        | Defense  -> ("data.defense_before",   "data.defense_after",   "data.defense_increased_normalized",   "data.defense_increased")
        | Speed    -> ("data.speed_before",     "data.speed_after",     "data.speed_increased_normalized",     "data.speed_increased")
        | Dexterity-> ("data.dexterity_before", "data.dexterity_after", "data.dexterity_increased_normalized", "data.dexterity_increased")

    type private ParsedRow =
        {
            OriginalIndex: int
            LogId: string
            Timestamp: int64 option
            StatType: StatType
            StatBefore: float
            StatAfter: float
            HappyBeforeTrain: float
            StatIncreased: float
            EnergyUsed: float
        }

    let private statTypeOrder = function
        | Strength -> 0
        | Defense -> 1
        | Speed -> 2
        | Dexterity -> 3

    let private tryBuildDetailedRecords (rows: ParsedRow list) : StatRecordRow list =
        let sorted =
            rows
            |> List.sortBy (fun r -> (defaultArg r.Timestamp Int64.MinValue, r.OriginalIndex, statTypeOrder r.StatType, r.LogId))

        let current = System.Collections.Generic.Dictionary<StatType, float>()
        let totalsById = System.Collections.Generic.Dictionary<string, float>()

        for row in sorted do
            current[row.StatType] <- row.StatBefore
            let total =
                [ Strength; Defense; Speed; Dexterity ]
                |> List.sumBy (fun stat -> if current.ContainsKey(stat) then current[stat] else 0.0)
            totalsById[row.LogId] <- total
            current[row.StatType] <- row.StatAfter

        rows
        |> List.map (fun row ->
            {
                LogId = row.LogId
                Timestamp = row.Timestamp
                StatType = row.StatType
                StatBefore = row.StatBefore
                StatTotalBefore = if totalsById.ContainsKey(row.LogId) then totalsById[row.LogId] else row.StatBefore
                HappyBeforeTrain = row.HappyBeforeTrain
                StatIncreased = row.StatIncreased
                EnergyUsed = row.EnergyUsed
            })
    // ── Public API ─────────────────────────────────────────────────────────

    let private tryParseInt64 (s: string) : int64 option =
        match String.IsNullOrWhiteSpace(s) with
        | true -> None
        | false ->
            match Int64.TryParse(s, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture) with
            | true, v -> Some v
            | false, _ -> None

    /// Reads the CSV file at the given path and extracts gym training stat records.
    /// Detailed variant that preserves the log id (and timestamp when present) for verification.
    let readStatRecordsDetailed (filePath: string) : ReadResultDetailed =
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

            let idIdx             = tryGetCol "id"
            let timestampIdx      = tryGetCol "timestamp"
            let happyBeforeTrainIdx = tryGetCol "happy_before_train"
            let happyBeforeEventIdx = tryGetCol "happy_before_event"
            let energyUsedIdx       = tryGetCol "data.energy_used"
            let detailsTitleIdx     = tryGetCol "details.title"

            // Debug CSV schema support (fixed columns).
            let statTypeIdx     = tryGetCol "stat_type"
            let statBeforeIdx   = tryGetCol "stat_before"
            let statIncreasedIdx= tryGetCol "stat_increased"

            let debugSchema = statTypeIdx.IsSome && statBeforeIdx.IsSome && statIncreasedIdx.IsSome

            let parsedRows = ResizeArray<ParsedRow>()
            let errors  = ResizeArray<string>()

            // Pre-compute column lookups for each stat type
            let statColMap =
                [ Strength; Defense; Speed; Dexterity ]
                |> List.map (fun st ->
                    let (beforeCol, afterCol, increasedNormCol, increasedRawCol) = columnValuesForStat st
                    let increasedIdx =
                        match tryGetCol increasedNormCol with
                        | Some idx -> Some idx
                        | None -> tryGetCol increasedRawCol

                    st, (tryGetCol beforeCol, tryGetCol afterCol, increasedIdx))
                |> Map.ofList

            for rowIdx in 1 .. lines.Length - 1 do
                let line = lines.[rowIdx]
                if String.IsNullOrWhiteSpace line then () else

                let fields = parseCsvLine line

                let getField (idx: int) =
                    if idx < fields.Length then fields.[idx].Trim() else ""

                let logIdOpt =
                    match idIdx with
                    | Some idx ->
                        let v = getField idx
                        if String.IsNullOrWhiteSpace(v) then None else Some v
                    | None -> None

                match logIdOpt with
                | None ->
                    errors.Add(sprintf "Row %d: missing id column/value" (rowIdx + 1))
                | Some logId ->
                    let tsOpt =
                        match timestampIdx with
                        | Some idx -> tryParseInt64 (getField idx)
                        | None -> None

                    if debugSchema then
                        match statTypeIdx, statBeforeIdx, statIncreasedIdx with
                        | Some stIdx, Some beforeIdx, Some increasedIdx ->
                            let statAfterIdx = tryGetCol "stat_after"
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

                                let beforeOpt = tryParseFloat (getField beforeIdx)
                                let increasedOpt = tryParseFloat (getField increasedIdx)
                                let afterOpt =
                                    match statAfterIdx with
                                    | Some idx -> tryParseFloat (getField idx)
                                    | None ->
                                        match beforeOpt, increasedOpt with
                                        | Some before, Some increased -> Some (before + increased)
                                        | _ -> None

                                match beforeOpt, afterOpt, happyOpt, increasedOpt, energyOpt with
                                | Some before, Some after, Some happy, Some increased, Some energy when energy > 0.0 ->
                                    parsedRows.Add {
                                        OriginalIndex = rowIdx
                                        LogId = logId
                                        Timestamp = tsOpt
                                        StatType = statType
                                        StatBefore = before
                                        StatAfter = after
                                        HappyBeforeTrain = happy
                                        StatIncreased = increased
                                        EnergyUsed = energy
                                    }
                                | _ ->
                                    errors.Add(
                                        sprintf "Row %d: missing numeric fields (debug schema; stat=%A before=%A after=%A happy=%A increased=%A energy=%A)"
                                            (rowIdx + 1) statType beforeOpt afterOpt happyOpt increasedOpt energyOpt)
                        | _ -> ()
                    else
                        match detailsTitleIdx with
                        | None -> ()
                        | Some titleIdx ->
                            let title = getField titleIdx
                            match statTypeFromTitle title with
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

                                match Map.find statType statColMap with
                                | Some beforeIdx, Some afterIdx, Some increasedIdx ->
                                    let beforeOpt = tryParseFloat (getField beforeIdx)
                                    let afterOpt = tryParseFloat (getField afterIdx)
                                    let increasedOpt = tryParseFloat (getField increasedIdx)

                                    match beforeOpt, afterOpt, happyOpt, increasedOpt, energyOpt with
                                    | Some before, Some after, Some happy, Some increased, Some energy when energy > 0.0 ->
                                        parsedRows.Add {
                                            OriginalIndex = rowIdx
                                            LogId = logId
                                            Timestamp = tsOpt
                                            StatType = statType
                                            StatBefore = before
                                            StatAfter = after
                                            HappyBeforeTrain = happy
                                            StatIncreased = increased
                                            EnergyUsed = energy
                                        }
                                    | _ ->
                                        errors.Add(
                                            sprintf "Row %d: missing numeric fields (stat=%A before=%A after=%A happy=%A increased=%A energy=%A)"
                                                (rowIdx + 1) statType beforeOpt afterOpt happyOpt increasedOpt energyOpt)
                                | _ ->
                                    errors.Add(
                                        sprintf "Row %d: missing column mapping for stat type %A"
                                            (rowIdx + 1) statType)

            { Records = parsedRows |> List.ofSeq |> tryBuildDetailedRecords; ParseErrors = List.ofSeq errors }

    /// Reads the CSV file at the given path and extracts gym training stat records.
    /// Returns a ReadResult with successfully parsed records and any parse errors.
    let readStatRecords (filePath: string) : ReadResult =
        let detailed = readStatRecordsDetailed filePath
        {
            Records =
                detailed.Records
                |> List.map (fun r ->
                    {
                        StatType = r.StatType
                        StatBefore = r.StatBefore
                        StatTotalBefore = r.StatTotalBefore
                        HappyBeforeTrain = r.HappyBeforeTrain
                        StatIncreased = r.StatIncreased
                        EnergyUsed = r.EnergyUsed
                    })
            ParseErrors = detailed.ParseErrors
        }
