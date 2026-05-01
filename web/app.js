const API_BASE = "";
const DATA_BASE = "./data/surfaces";

const CONFIDENCE_LOW_RGB = { r: 214, g: 64, b: 69 };
const CONFIDENCE_HIGH_RGB = { r: 56, g: 201, b: 110 };

function clampConfidence(value) {
  const n = Number(value);
  if (!Number.isFinite(n)) return 0;
  if (n < 0) return 0;
  if (n > 1) return 1;
  return n;
}

function confidenceToColor(value) {
  const t = clampConfidence(value);
  const r = Math.round(CONFIDENCE_LOW_RGB.r + (CONFIDENCE_HIGH_RGB.r - CONFIDENCE_LOW_RGB.r) * t);
  const g = Math.round(CONFIDENCE_LOW_RGB.g + (CONFIDENCE_HIGH_RGB.g - CONFIDENCE_LOW_RGB.g) * t);
  const b = Math.round(CONFIDENCE_LOW_RGB.b + (CONFIDENCE_HIGH_RGB.b - CONFIDENCE_LOW_RGB.b) * t);
  return `rgb(${r}, ${g}, ${b})`;
}

function normalizeReasons(reasonsEntry) {
  if (!Array.isArray(reasonsEntry) || reasonsEntry.length === 0) {
    return "missing-provenance-record";
  }
  return reasonsEntry.join(", ");
}

function buildGymHoverText(series, index) {
  const baseText = Array.isArray(series?.text) ? series.text[index] : "";
  const confidence = clampConfidence(Array.isArray(series?.confidence) ? series.confidence[index] : undefined);
  const confidencePercent = `${Math.round(confidence * 100)}%`;
  const reasonText = normalizeReasons(Array.isArray(series?.confidenceReasons) ? series.confidenceReasons[index] : undefined);
  const parts = [];
  if (baseText) parts.push(baseText);
  parts.push(`Confidence: ${confidencePercent}`);
  parts.push(`Evidence: ${reasonText}`);
  return parts.join("<br>");
}

function buildGymMarkerColors(series) {
  const length = Array.isArray(series?.x) ? series.x.length : 0;
  return Array.from({ length }, (_, i) => {
    const value = Array.isArray(series?.confidence) ? series.confidence[i] : undefined;
    return confidenceToColor(value);
  });
}

function buildGymTrace(series) {
  return {
    type: "scatter3d",
    mode: "markers",
    name: "Gym trains",
    x: series.x,
    y: series.y,
    z: series.z,
    marker: { size: 3, opacity: 0.8, color: buildGymMarkerColors(series) },
    text: Array.from({ length: series.x.length }, (_, i) => buildGymHoverText(series, i)),
    hovertemplate: "Stat before: %{x}<br>Happy before: %{y}<br>Gain / energy: %{z}<br>%{text}<extra></extra>",
  };
}

const el = typeof document === "undefined"
  ? null
  : {
      apiKey: document.getElementById("api-key"),
      runImport: document.getElementById("run-import"),
      status: document.getElementById("status"),
    };

if (el?.runImport) {
  el.runImport.addEventListener("click", runImportAndRefresh);
  void loadCachedSurfaces();
}

async function runImportAndRefresh() {
  const apiKey = (el.apiKey.value || "").trim();
  if (!apiKey) {
    setStatus("API key is required.", true);
    return;
  }

  try {
    setStatus("Starting import…");
    await postJson("/api/v1/torn/import-jobs", { apiKey, fresh: true });

    setStatus("Import started. Waiting for cache refresh…");
    await delay(4000);

    await loadCachedSurfaces();
  } catch (err) {
    setStatus(err instanceof Error ? err.message : String(err), true);
  }
}

async function loadCachedSurfaces() {
  try {
    setStatus("Checking cached dataset…");

    const metaRes = await fetch(`${DATA_BASE}/meta.json`, { cache: "no-cache" });
    if (!metaRes.ok) throw new Error(`Failed to load meta.json (${metaRes.status}).`);
    const meta = await metaRes.json();

    const dataRes = await fetch(`${DATA_BASE}/latest.json?v=${encodeURIComponent(meta.currentVersion)}`);
    if (!dataRes.ok) throw new Error(`Failed to load latest.json (${dataRes.status}).`);
    const payload = await dataRes.json();

    const gymCount = payload?.meta?.gymPointCount ?? payload?.series?.gymCloud?.x?.length ?? 0;
    const eventCount = payload?.meta?.eventPointCount ?? payload?.series?.eventsCloud?.x?.length ?? 0;

    renderGymCloudFromSeries(payload.series.gymCloud);
    renderEventsCloudFromSeries(payload.series.eventsCloud);

    if (gymCount === 0 && eventCount === 0) {
      setStatus(`Loaded dataset ${payload.version}, but no points were available yet. Import may still be running.`);
    } else {
      setStatus(`Loaded cached dataset ${payload.version} (synced ${payload.syncedAtUtc}) — gym: ${gymCount}, events: ${eventCount}.`);
    }
  } catch (err) {
    setStatus(`Cache load failed: ${err instanceof Error ? err.message : String(err)}`, true);
  }
}

function renderGymCloudFromSeries(series) {
  if (!series || !Array.isArray(series.x) || series.x.length === 0) {
    Plotly.newPlot(
      "gym-cloud",
      [],
      {
        margin: { l: 0, r: 0, t: 20, b: 0 },
        annotations: [{ text: "No gym points yet", showarrow: false, x: 0.5, y: 0.5, xref: "paper", yref: "paper" }],
      },
      { responsive: true }
    );
    return;
  }

  const trace = buildGymTrace(series);

  const layout = {
    margin: { l: 0, r: 0, t: 20, b: 0 },
    scene: {
      xaxis: { title: "Stat before train" },
      yaxis: { title: "Happy before train" },
      zaxis: { title: "Stat gained / energy" },
    },
  };

  Plotly.newPlot("gym-cloud", [trace], layout, { responsive: true });
}

function renderEventsCloudFromSeries(series) {
  if (!series || !Array.isArray(series.x) || series.x.length === 0) {
    Plotly.newPlot("events-cloud", [], {
      margin: { l: 0, r: 0, t: 20, b: 0 },
      annotations: [{ text: "No event points yet", showarrow: false, x: 0.5, y: 0.5, xref: "paper", yref: "paper" }],
    }, { responsive: true });
    return;
  }

  const trace = {
    type: "scatter3d",
    mode: "markers",
    name: "Happy events",
    x: series.x,
    y: series.y,
    z: series.z,
    marker: { size: 3, opacity: 0.75 },
    text: series.text,
    hovertemplate: "Before: %{x}<br>Delta: %{y}<br>After: %{z}<br>%{text}<extra></extra>",
  };

  const layout = {
    margin: { l: 0, r: 0, t: 20, b: 0 },
    scene: {
      xaxis: { title: "Happy before event" },
      yaxis: { title: "Delta" },
      zaxis: { title: "Happy after event" },
    },
  };

  Plotly.newPlot("events-cloud", [trace], layout, { responsive: true });
}

async function postJson(path, body) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`${res.status} ${res.statusText} from ${path}: ${text}`);
  }
  return res.json();
}

function setStatus(text, isError = false) {
  if (!el?.status) return;
  el.status.textContent = text;
  el.status.style.color = isError ? "#ff7676" : "#b8e1ff";
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

export { clampConfidence, confidenceToColor, buildGymHoverText, buildGymMarkerColors, buildGymTrace };
