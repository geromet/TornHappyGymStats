namespace HappyGymStats.Visualizer

open System
open System.IO
open System.Globalization
open System.Text.Json
open System.Text.Json.Nodes

/// Thin rendering module that writes interactive Plotly 3D point-cloud HTML.
module SurfacePlotter =

    let private defaultXLabel = "Stat before train"
    let private defaultYLabel = "Happy before train"
    let private defaultZLabel = "Stat gained / energy"

    let private chartHeightPx = 1180
    let private maxChartX = 1_000_000_000.0
    let private maxChartY = 99_999.0

    let private jsNumber (value: float) : string =
        if Double.IsFinite(value) then value.ToString("R", CultureInfo.InvariantCulture) else "0"

    let private createRawPointTraceNode (name: string) (records: StatRecord list) =
        let usable = records |> List.filter (fun r -> r.EnergyUsed > 0.0)

        let trace = JsonObject()
        trace["type"] <- JsonValue.Create("scatter3d")
        trace["mode"] <- JsonValue.Create("markers")
        trace["name"] <- JsonValue.Create(name)
        trace["showlegend"] <- JsonValue.Create(true)
        trace["hovertemplate"] <- JsonValue.Create("stat: %{x:.0f}<br>happy: %{y:.0f}<br>gain / energy: %{z:.6f}<extra>Raw sample</extra>")

        trace["x"] <- (usable |> List.map (fun r -> JsonValue.Create(r.StatBefore) :> JsonNode) |> List.toArray |> JsonArray)
        trace["y"] <- (usable |> List.map (fun r -> JsonValue.Create(r.HappyBeforeTrain) :> JsonNode) |> List.toArray |> JsonArray)
        trace["z"] <- (usable |> List.map (fun r -> JsonValue.Create(r.StatIncreased / r.EnergyUsed) :> JsonNode) |> List.toArray |> JsonArray)

        let marker = JsonObject()
        marker["size"] <- JsonValue.Create(3.0)
        marker["opacity"] <- JsonValue.Create(0.72)
        marker["color"] <- JsonValue.Create("rgba(118, 219, 255, 0.9)")
        trace["marker"] <- marker
        trace

    let private createPointCloudLayoutNode () =
        let layout = JsonObject()
        layout["showlegend"] <- JsonValue.Create(true)
        layout["paper_bgcolor"] <- JsonValue.Create("rgba(0,0,0,0)")
        layout["plot_bgcolor"] <- JsonValue.Create("rgba(0,0,0,0)")

        let margin = JsonObject()
        margin["l"] <- JsonValue.Create(0)
        margin["r"] <- JsonValue.Create(0)
        margin["t"] <- JsonValue.Create(12)
        margin["b"] <- JsonValue.Create(0)
        layout["margin"] <- margin

        let scene = JsonObject()

        let axis (titleText: string) (maxValue: float) =
            let axisNode = JsonObject()
            let title = JsonObject()
            title["text"] <- JsonValue.Create(titleText)
            axisNode["title"] <- title
            axisNode["range"] <- JsonArray(JsonValue.Create(0.0) :> JsonNode, JsonValue.Create(maxValue) :> JsonNode)
            axisNode

        scene["xaxis"] <- axis defaultXLabel maxChartX
        scene["yaxis"] <- axis defaultYLabel maxChartY
        scene["zaxis"] <- axis defaultZLabel 1.0
        scene["aspectmode"] <- JsonValue.Create("manual")

        let aspectRatio = JsonObject()
        aspectRatio["x"] <- JsonValue.Create(1.55)
        aspectRatio["y"] <- JsonValue.Create(1.2)
        aspectRatio["z"] <- JsonValue.Create(0.95)
        scene["aspectratio"] <- aspectRatio

        let camera = JsonObject()
        let eye = JsonObject()
        eye["x"] <- JsonValue.Create(1.35)
        eye["y"] <- JsonValue.Create(1.55)
        eye["z"] <- JsonValue.Create(1.05)
        camera["eye"] <- eye
        scene["camera"] <- camera

        layout["scene"] <- scene
        layout

    let private renderPointCloudHtml (records: StatRecord list) =
        let id = "plot-" + Guid.NewGuid().ToString("N")
        let data = JsonArray()
        data.Add((createRawPointTraceNode "Raw samples" records) :> JsonNode)
        let layout = createPointCloudLayoutNode()
        let jsonOptions = JsonSerializerOptions(WriteIndented = false)
        let dataJson = data.ToJsonString(jsonOptions)
        let layoutJson = layout.ToJsonString(jsonOptions)

        $"""<div id="{id}" class="plot-shell" style="width: 100%%; height: {chartHeightPx}px;"></div>
<script>
  Plotly.newPlot("{id}", {dataJson}, {layoutJson}, {{ responsive: true }});
</script>"""

    /// Generates one single HTML file containing all point clouds stacked vertically.
    /// Output: <outputDir>/Surfaces.html
    /// Returns a list with that single output path (or [] if no stat records exist).
    let generateStackedPlots (records: StatRecord list) (outputDir: string) : string list =
        if not (Directory.Exists(outputDir)) then
            Directory.CreateDirectory(outputDir) |> ignore

        let allStatTypes = [ Strength; Defense; Speed; Dexterity ]
        let usableRecords = records |> List.filter (fun r -> r.EnergyUsed > 0.0)

        if usableRecords.IsEmpty then
            []
        else
            let maxGatheredStat = usableRecords |> List.map _.StatBefore |> List.max
            let maxGatheredHappy = usableRecords |> List.map _.HappyBeforeTrain |> List.max
            let maxGatheredGain = usableRecords |> List.map (fun r -> r.StatIncreased / r.EnergyUsed) |> List.max
            let defaultMaxGain = max 1.0 (ceil (maxGatheredGain * 1.2))
            let statSliderMax = max maxChartX maxGatheredStat
            let happySliderMax = max maxChartY maxGatheredHappy
            let gainSliderMax = max defaultMaxGain (ceil maxGatheredGain)

            let renderBlockHtml (heading: string) (inner: string) =
                $"""
<section class="panel">
  <h2>{heading}</h2>
  {inner}
</section>
"""

            let renderBlock (heading: string) (rawRecords: StatRecord list) =
                rawRecords
                |> renderPointCloudHtml
                |> renderBlockHtml heading

            let blocks =
                [
                    renderBlock "All stats" usableRecords
                    yield!
                        allStatTypes
                        |> List.choose (fun statType ->
                            let rawRecords = usableRecords |> List.filter (fun r -> r.StatType = statType)
                            if rawRecords.IsEmpty then None else Some (renderBlock (string statType) rawRecords))
                ]
                |> String.concat "\n"

            let full =
                $"""<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>HappyGymStats — Point Clouds</title>
  <script src="https://cdn.plot.ly/plotly-2.27.1.min.js" charset="utf-8"></script>
  <style>
    body {{ font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif; margin: 0; padding: 0; background: #0b0f19; color: #e8eefc; }}
    header {{ padding: 20px 24px; border-bottom: 1px solid rgba(255,255,255,0.08); background: rgba(255,255,255,0.02); position: sticky; top: 0; backdrop-filter: blur(10px); }}
    header h1 {{ margin: 0; font-size: 18px; font-weight: 600; }}
    header p {{ margin: 6px 0 0; opacity: 0.75; font-size: 13px; }}
    .controls {{ display: grid; grid-template-columns: repeat(3, minmax(180px, 1fr)) auto; gap: 12px; align-items: end; margin-top: 14px; }}
    .axis-control {{ display: grid; gap: 6px; min-width: 0; }}
    .axis-control span {{ display: flex; justify-content: space-between; gap: 12px; font-size: 12px; color: #c8d4e8; }}
    .axis-control output {{ color: #ffffff; font-variant-numeric: tabular-nums; }}
    .axis-control input {{ width: 100%%; accent-color: #ffb04d; }}
    .controls button {{ border: 1px solid rgba(255,255,255,0.18); background: rgba(255,176,77,0.18); color: #fff4e2; border-radius: 8px; padding: 8px 12px; font: inherit; font-size: 13px; cursor: pointer; }}
    .controls button:hover {{ background: rgba(255,176,77,0.28); }}
    main {{ width: 100vw; margin: 0; padding: 12px 12px 28px; box-sizing: border-box; }}
    .panel {{ background: rgba(255,255,255,0.03); border: 1px solid rgba(255,255,255,0.08); border-radius: 14px; padding: 10px 10px 12px; margin: 12px 0; }}
    .panel h2 {{ margin: 4px 6px 10px; font-size: 16px; font-weight: 600; }}
    @media (max-width: 760px) {{ .controls {{ grid-template-columns: 1fr; }} }}
  </style>
</head>
<body>
  <header>
    <h1>HappyGymStats — Stat gained per energy (point cloud)</h1>
    <p>Raw gym samples plotted by stat before train, happy before train, and stat gained per energy.</p>
    <div class="controls" aria-label="Axis controls">
      <label class="axis-control" for="maxStatAxis">
        <span>Max stat <output id="maxStatAxisValue"></output></span>
        <input id="maxStatAxis" type="range" min="1" max="{jsNumber statSliderMax}" step="1" value="{jsNumber maxChartX}" />
      </label>
      <label class="axis-control" for="maxHappyAxis">
        <span>Max happy <output id="maxHappyAxisValue"></output></span>
        <input id="maxHappyAxis" type="range" min="1" max="{jsNumber happySliderMax}" step="1" value="{jsNumber maxChartY}" />
      </label>
      <label class="axis-control" for="maxGainAxis">
        <span>Max gain / energy <output id="maxGainAxisValue"></output></span>
        <input id="maxGainAxis" type="range" min="0.001" max="{jsNumber gainSliderMax}" step="0.001" value="{jsNumber defaultMaxGain}" />
      </label>
      <button id="clampToData" type="button">Clamp axes to data</button>
    </div>
  </header>
  <main>
{blocks}
  </main>
  <script>
    (function() {{
      const defaultMaxStat = {jsNumber maxChartX};
      const defaultMaxHappy = {jsNumber maxChartY};
      const defaultMaxGain = {jsNumber defaultMaxGain};
      const gatheredMaxStat = {jsNumber maxGatheredStat};
      const gatheredMaxHappy = {jsNumber maxGatheredHappy};
      const gatheredMaxGain = {jsNumber maxGatheredGain};

      const formatValue = (value) => Math.round(Number(value)).toLocaleString();
      const formatGain = (value) => Number(value).toLocaleString(undefined, {{ maximumFractionDigits: 3 }});
      const plots = () => Array.from(document.querySelectorAll('.plot-shell'));

      const applyAxes = () => {{
        const maxStat = Number(document.getElementById('maxStatAxis').value || defaultMaxStat);
        const maxHappy = Number(document.getElementById('maxHappyAxis').value || defaultMaxHappy);
        const maxGain = Number(document.getElementById('maxGainAxis').value || defaultMaxGain);
        document.getElementById('maxStatAxisValue').value = formatValue(maxStat);
        document.getElementById('maxHappyAxisValue').value = formatValue(maxHappy);
        document.getElementById('maxGainAxisValue').value = formatGain(maxGain);

        if (!window.Plotly || typeof window.Plotly.relayout !== 'function') return;
        for (const plot of plots()) {{
          window.Plotly.relayout(plot, {{
            'scene.xaxis.range': [0, maxStat],
            'scene.yaxis.range': [0, maxHappy],
            'scene.zaxis.range': [0, maxGain]
          }});
        }}
      }};

      const clampToData = () => {{
        document.getElementById('maxStatAxis').value = Math.max(1, Math.ceil(gatheredMaxStat));
        document.getElementById('maxHappyAxis').value = Math.max(1, Math.ceil(gatheredMaxHappy));
        document.getElementById('maxGainAxis').value = Math.max(0.001, gatheredMaxGain).toFixed(3);
        applyAxes();
      }};

      const bindControls = () => {{
        document.getElementById('maxStatAxis').addEventListener('input', applyAxes);
        document.getElementById('maxHappyAxis').addEventListener('input', applyAxes);
        document.getElementById('maxGainAxis').addEventListener('input', applyAxes);
        document.getElementById('clampToData').addEventListener('click', clampToData);
        applyAxes();
      }};

      if (document.readyState === 'loading') {{
        document.addEventListener('DOMContentLoaded', bindControls, {{ once: true }});
      }} else {{
        bindControls();
      }}
    }})();
  </script>
</body>
</html>
"""

            let outputPath = Path.Combine(outputDir, "Surfaces.html")
            File.WriteAllText(outputPath, full)
            [ outputPath ]
