namespace HappyGymStats.Visualizer

open System
open System.IO
open System.Text.RegularExpressions
open Plotly.NET
open Plotly.NET.Chart3D
open Plotly.NET.LayoutObjects

/// Thin rendering module — converts binned grid results into interactive Plotly 3D surface HTML.
module SurfacePlotter =

    // NOTE: For 3D charts, axis titles must be set on layout.scene.xaxis/yaxis/zaxis.

    let private defaultXLabel = "Stat before train"
    let private defaultYLabel = "Happy before train"
    let private defaultZLabel = "Stat gained / energy"

    let private chartHeightPx = 820

    let private buildScene () : Scene =
        let axis titleText =
            LinearAxis.init(Title = Title.init(Text = titleText))

        Scene.init(
            XAxis = axis defaultXLabel,
            YAxis = axis defaultYLabel,
            ZAxis = axis defaultZLabel
        )

    let private styleChartCommon (chart: GenericChart) : GenericChart =
        let transparent = Color.fromString "rgba(0,0,0,0)"

        chart
        |> Chart.withScene (buildScene ())
        |> Chart.withLayoutStyle(PaperBGColor = transparent, PlotBGColor = transparent)

    let private styleChartWithTitle (title: string) (chart: GenericChart) : GenericChart =
        // Keep margins tight but leave a sliver for the modebar.
        let margin = Margin.init(Left = 0, Right = 0, Top = 14, Bottom = 0)

        chart
        |> styleChartCommon
        |> Chart.withLayoutStyle(Title = Title.init(Text = title), Margin = margin)

    let private styleChartTight (chart: GenericChart) : GenericChart =
        // Used in Surfaces.html (we already have an <h2> per plot).
        let margin = Margin.init(Left = 0, Right = 0, Top = 12, Bottom = 0)

        chart
        |> styleChartCommon
        |> Chart.withLayoutStyle(Margin = margin)

    /// Patch Plotly.NET HTML output so Plotly.js renders missing cells as holes and uses the container size.
    let private fixHtml (html: string) : string =
        html
            // JSON cannot represent NaN/Infinity. Plotly.NET emits them as strings.
            // Plotly.js expects null for missing values.
            .Replace("\"NaN\"", "null")
            .Replace("\"Infinity\"", "null")
            .Replace("\"-Infinity\"", "null")
        |> fun s ->
            // Remove fixed width/height in layout so charts can fill the page.
            // Plotly.NET emits `"width":600,"height":600,` by default.
            Regex.Replace(
                s,
                "\"width\":\d+(?:\\.\d+)?,\"height\":\d+(?:\\.\d+)?,",
                "",
                RegexOptions.CultureInvariant)
        |> fun s ->
            // Ensure the chart container has a reasonable height.
            // The HTML can be either `<div id="...">` or `<div id="..." style="...">`.
            // (If layout.height is removed and the container has no height, Plotly renders at 0px.)
            Regex.Replace(
                s,
                "<div id=\"([^\"]+)\"([^>]*)>",
                MatchEvaluator(fun m ->
                    let id = m.Groups.[1].Value
                    let attrs = m.Groups.[2].Value
                    let ensureStyle (existing: string) =
                        // Append, don't overwrite.
                        let suffix = sprintf "; width: 100%%; height: %dpx;" chartHeightPx
                        if existing.Contains("height", StringComparison.OrdinalIgnoreCase) then existing
                        else existing + suffix

                    let newAttrs =
                        if attrs.Contains("style=\"", StringComparison.OrdinalIgnoreCase) then
                            Regex.Replace(
                                attrs,
                                "style=\\\"([^\\\"]*)\\\"",
                                MatchEvaluator(fun sm ->
                                    let cur = sm.Groups.[1].Value
                                    sprintf "style=\"%s\"" (ensureStyle cur)
                                ),
                                RegexOptions.CultureInvariant)
                        else
                            attrs + (sprintf " style=\"width: 100%%; height: %dpx;\"" chartHeightPx)

                    sprintf "<div id=\"%s\"%s>" id newAttrs
                ),
                RegexOptions.CultureInvariant)

    let private baseSurfaceChart (grid: GridResult) : GenericChart =
        let zData = grid.ZMatrix |> Seq.map Seq.ofList
        let xData = grid.XValues |> Seq.ofList
        let yData = grid.YValues |> Seq.ofList

        Chart.Surface(zData = zData, X = xData, Y = yData)

    /// Generates a Plotly.NET 3D surface chart from a GridResult.
    let generateChart (grid: GridResult) (title: string) : GenericChart =
        baseSurfaceChart grid
        |> styleChartWithTitle title

    /// Generates a surface chart from a GridResult and saves it as an HTML file.
    /// Returns the output file path.
    let generatePlot (grid: GridResult) (title: string) (outputPath: string) : string =
        let chart = generateChart grid title
        chart |> Chart.saveHtml outputPath

        let html = File.ReadAllText outputPath
        let fixedHtml = fixHtml html
        if not (obj.ReferenceEquals(html, fixedHtml)) then
            File.WriteAllText(outputPath, fixedHtml)

        outputPath

    /// Generates surface plots for all four stat types.
    /// Output: <outputDir>/<statType>.html for each stat type present in the records.
    let generatePlots (records: StatRecord list) (outputDir: string) : string list =
        if not (Directory.Exists(outputDir)) then
            Directory.CreateDirectory(outputDir) |> ignore

        let allStatTypes = [ Strength; Defense; Speed; Dexterity ]

        allStatTypes
        |> List.choose (fun statType ->
            let filtered = records |> List.filter (fun r -> r.StatType = statType)
            if filtered.IsEmpty then None
            else
                let grid = SurfaceBinner.binRecords filtered
                let title = sprintf "%A surface" statType
                let filename = sprintf "%A.html" statType
                let outputPath = Path.Combine(outputDir, filename)
                Some (generatePlot grid title outputPath)
        )

    /// Generates one single HTML file containing all surfaces stacked vertically.
    /// Output: <outputDir>/Surfaces.html
    /// Returns a list with that single output path (or [] if no stat records exist).
    let generateStackedPlots (records: StatRecord list) (outputDir: string) : string list =
        if not (Directory.Exists(outputDir)) then
            Directory.CreateDirectory(outputDir) |> ignore

        let allStatTypes = [ Strength; Defense; Speed; Dexterity ]

        let charts =
            allStatTypes
            |> List.choose (fun statType ->
                let filtered = records |> List.filter (fun r -> r.StatType = statType)
                if filtered.IsEmpty then None
                else
                    let grid = SurfaceBinner.binRecords filtered
                    let title = sprintf "%A surface" statType
                    let chart = baseSurfaceChart grid |> styleChartTight
                    Some (statType, title, chart)
            )

        if charts.IsEmpty then
            []
        else
            // Combined (all stats) surface — average across all records.
            let combinedGrid = SurfaceBinner.binRecords records
            let combinedChart = baseSurfaceChart combinedGrid |> styleChartTight

            let renderBlock (heading: string) (chart: GenericChart) =
                let inner = chart |> GenericChart.toChartHTML |> fixHtml

                $"""
<section class="panel">
  <h2>{heading}</h2>
  {inner}
</section>
"""

            let blocks =
                [
                    renderBlock "All stats (avg)" combinedChart
                    yield!
                        charts
                        |> List.map (fun (statType, _title, chart) ->
                            renderBlock (string statType) chart)
                ]
                |> String.concat "\n"

            let full =
                $"""<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>HappyGymStats — Surfaces</title>
  <script src="https://cdn.plot.ly/plotly-2.27.1.min.js" charset="utf-8"></script>
  <style>
    body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 0; padding: 0; background: #0b0f19; color: #e8eefc; }}
    header {{ padding: 20px 24px; border-bottom: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.02); position: sticky; top: 0; backdrop-filter: blur(10px); }}
    header h1 {{ margin: 0; font-size: 18px; font-weight: 600; }}
    header p {{ margin: 6px 0 0; opacity: 0.75; font-size: 13px; }}
    main {{ width: 100%%; margin: 0; padding: 12px 12px 28px; box-sizing: border-box; }}
    .panel {{ background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.08); border-radius: 14px; padding: 10px 10px 12px; margin: 12px 0; }}
    .panel h2 {{ margin: 4px 6px 10px; font-size: 16px; font-weight: 600; }}
  </style>
</head>
<body>
  <header>
    <h1>HappyGymStats — Stat gained per energy (surface)</h1>
    <p>X = stat before train · Y = happy before train · Z = stat gained / energy</p>
  </header>
  <main>
{blocks}
  </main>
</body>
</html>
"""

            let outputPath = Path.Combine(outputDir, "Surfaces.html")
            File.WriteAllText(outputPath, full)
            [ outputPath ]
