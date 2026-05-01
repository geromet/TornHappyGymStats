const API_BASE = "https://www.geromet.com";
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
    await postJson("/v1/import", { apiKey, fresh: false });

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

    renderGymCloudFromSeries(payload.series.gymCloud);
    renderEventsCloudFromSeries(payload.series.eventsCloud);

    setStatus(`Loaded cached dataset ${payload.version} (synced ${payload.syncedAtUtc}).`);
  } catch (err) {
    setStatus(`Cache load failed: ${err instanceof Error ? err.message : String(err)}`, true);
  }
}

function renderGymCloudFromSeries(series) {
  const trace = {
    type: "scatter3d",
    mode: "markers",
    name: "Gym trains",
    x: series.x,
    y: series.y,
    z: series.z,
    marker: { size: 3, opacity: 0.75 },
    text: series.text,
    hovertemplate: "Happy before: %{x}<br>Happy used: %{y}<br>Regen gained: %{z}<br>%{text}<extra></extra>",
  };

  const layout = {
    margin: { l: 0, r: 0, t: 20, b: 0 },
    scene: {
      xaxis: { title: "Happy before train" },
      yaxis: { title: "Happy used" },
      zaxis: { title: "Regen happy gained" },
    },
  };

  Plotly.newPlot("gym-cloud", [trace], layout, { responsive: true });
}

function renderEventsCloudFromSeries(series) {
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
