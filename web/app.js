const API_BASE = "";
const DATA_BASE = "./data/surfaces";

const el = {
  apiKey: document.getElementById("api-key"),
  runImport: document.getElementById("run-import"),
  status: document.getElementById("status"),
};

el.runImport.addEventListener("click", runImportAndRefresh);
void loadCachedSurfaces();

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
    Plotly.newPlot("gym-cloud", [], {
      margin: { l: 0, r: 0, t: 20, b: 0 },
      annotations: [{ text: "No gym points yet", showarrow: false, x: 0.5, y: 0.5, xref: "paper", yref: "paper" }],
    }, { responsive: true });
    return;
  }

  const trace = {
    type: "scatter3d",
    mode: "markers",
    name: "Gym trains",
    x: series.x,
    y: series.y,
    z: series.z,
    marker: { size: 3, opacity: 0.75 },
    text: series.text,
    hovertemplate: "Stat before: %{x}<br>Happy before: %{y}<br>Gain / energy: %{z}<br>%{text}<extra></extra>",
  };

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
  el.status.textContent = text;
  el.status.style.color = isError ? "#ff7676" : "#b8e1ff";
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}
