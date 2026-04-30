namespace HappyGymStats.Visualizer

open System
open System.IO
open Plotly.NET

/// Simple 2D happy-over-time plotter for debugging.
module HappyTimelinePlotter =

    // Reuse the RFC4180 parser from CsvStatReader.
    open CsvStatReader

    let private tryParseInt (s: string) : int option =
        match Int32.TryParse(s, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | false, _ -> None

    let private tryParseLong (s: string) : int64 option =
        match Int64.TryParse(s, Globalization.NumberStyles.Integer, Globalization.CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | false, _ -> None

    let private idxOf (headers: string array) (name: string) =
        headers |> Array.tryFindIndex (fun h -> h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))

    let generateHappyTimelinePlot (timelineCsvPath: string) (outputHtmlPath: string) : string =
        let lines = File.ReadAllLines(timelineCsvPath)
        if lines.Length < 2 then
            failwith "Timeline CSV is empty."

        let headers = parseCsvLine lines.[0]

        let tsIdx = idxOf headers "timestamp"
        let afterIdx = idxOf headers "happy_after_event"
        let typeIdx = idxOf headers "event_type"

        match tsIdx, afterIdx with
        | Some ti, Some ai ->
            let xs = ResizeArray<DateTime>()
            let ys = ResizeArray<float>()
            let texts = ResizeArray<string>()

            for i in 1 .. lines.Length - 1 do
                let line = lines.[i]
                if String.IsNullOrWhiteSpace(line) then () else

                let fields = parseCsvLine line
                let get idx = if idx < fields.Length then fields.[idx].Trim() else ""

                match tryParseLong (get ti), tryParseInt (get ai) with
                | Some ts, Some happy ->
                    xs.Add(DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime)
                    ys.Add(float happy)

                    let et =
                        match typeIdx with
                        | Some ei -> get ei
                        | None -> ""

                    texts.Add(et)
                | _ -> ()

            let chart =
                Chart.Line(xs, ys)
                |> Chart.withTitle "Happy timeline (after-event)"
                |> Chart.withXAxisStyle("Time (UTC)")
                |> Chart.withYAxisStyle("Happy")

            chart |> Chart.saveHtml outputHtmlPath
            outputHtmlPath
        | _ -> failwith "Timeline CSV missing required columns: timestamp, happy_after_event"
