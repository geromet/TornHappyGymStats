const API_BASE = "";
const DATA_BASE = "./data/surfaces";
const MAX_RENDERED_WARNINGS = 12;

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

function buildProfileLink(targetType, rawIdentifier) {
  const id = `${rawIdentifier ?? ""}`.trim();
  if (!id) return null;
  if (!/^\d+$/.test(id)) return null;
  const encodedId = encodeURIComponent(id);
  if (targetType === "owner") return `https://www.torn.com/profiles.php?XID=${encodedId}`;
  if (targetType === "faction") return `https://www.torn.com/factions.php?step=profile&ID=${encodedId}`;
  if (targetType === "company") return `https://www.torn.com/joblist.php#!p=corpinfo&ID=${encodedId}`;
  return null;
}

function normalizeWarningsWarningsList(warnings) {
  if (!Array.isArray(warnings)) return [];
  return warnings
    .filter(entry => entry && typeof entry === "object")
    .map((entry, index) => ({ ...entry, _inputOrder: index }))
    .sort((a, b) => {
      const aReason = `${a.reasonCode ?? "missing-provenance-record"}`;
      const bReason = `${b.reasonCode ?? "missing-provenance-record"}`;
      if (aReason !== bReason) return aReason.localeCompare(bReason);
      return a._inputOrder - b._inputOrder;
    });
}

function buildWarningsViewModel(payload) {
  const normalizedWarnings = normalizeWarningsWarningsList(payload?.warnings);
  const hasFallback = !Array.isArray(payload?.warnings);
  const sourceWarnings = hasFallback
    ? [{
        reasonCode: "missing-provenance-record",
        scope: "owner",
        sourceIdentifier: "unknown",
        message: "No structured warning payload was provided; provenance is unresolved.",
      }]
    : normalizedWarnings;

  const truncated = sourceWarnings.length > MAX_RENDERED_WARNINGS;
  const visibleWarnings = sourceWarnings.slice(0, MAX_RENDERED_WARNINGS);

  const items = visibleWarnings.map((entry, index) => {
    const reasonCode = `${entry.reasonCode ?? "missing-provenance-record"}`;
    const sourceIdentifier = `${entry.sourceIdentifier ?? "unknown"}`;
    const scope = `${entry.scope ?? "owner"}`;
    const overrideUsed = Boolean(entry.manualOverrideApplied);
    const warningText = `${entry.message ?? "Missing provenance details for this record."}`.slice(0, 280);
    const linkHref = buildProfileLink(scope, sourceIdentifier);
    return {
      key: `${reasonCode}:${sourceIdentifier}:${index}`,
      reasonCode,
      scope,
      sourceIdentifier,
      warningText,
      linkHref,
      actionCopy: `Resolve ${scope} provenance by reviewing source profile${linkHref ? "" : " (identifier only)"}.`,
      overrideCopy: overrideUsed
        ? "Manual override matched and was applied to this warning context."
        : "No manual override matched this warning context.",
    };
  });

  return {
    hasFallback,
    totalWarnings: sourceWarnings.length,
    renderedCount: items.length,
    truncated,
    overflowCount: truncated ? sourceWarnings.length - items.length : 0,
    items,
  };
}

function renderWarningsPanel(viewModel) {
  if (typeof document === "undefined") return;

  let panel = document.getElementById("provenance-warnings");
  if (!panel) {
    panel = document.createElement("section");
    panel.id = "provenance-warnings";
    panel.className = "panel";
    panel.innerHTML = '<h2>Provenance warnings</h2><div id="provenance-warnings-body"></div>';
    const eventsPanel = document.getElementById("events-cloud")?.closest("section");
    const container = document.querySelector("main.container");
    if (eventsPanel?.nextSibling) {
      eventsPanel.parentNode?.insertBefore(panel, eventsPanel.nextSibling);
    } else if (container) {
      container.appendChild(panel);
    }
  }

  const body = document.getElementById("provenance-warnings-body");
  if (!body) return;

  if (viewModel.renderedCount === 0) {
    body.innerHTML = '<p data-warning-empty="true">No unresolved provenance warnings in this dataset.</p>';
    return;
  }

  const fallbackMarker = viewModel.hasFallback
    ? '<p data-warning-fallback="true"><strong>Fallback active:</strong> warning payload was malformed; using missing-provenance-record defaults.</p>'
    : '';
  const overflowMarker = viewModel.truncated
    ? `<p data-warning-overflow="true">Showing first ${viewModel.renderedCount} warnings (of ${viewModel.totalWarnings}). ${viewModel.overflowCount} additional warnings are hidden.</p>`
    : '';

  const listHtml = viewModel.items.map(item => {
    const profile = item.linkHref
      ? `<a href="${item.linkHref}" target="_blank" rel="noopener noreferrer">${item.scope} #${item.sourceIdentifier}</a>`
      : `${item.scope} #${item.sourceIdentifier}`;
    return [
      `<li data-warning-reason="${item.reasonCode}">`,
      `<p><strong>${item.warningText}</strong></p>`,
      `<p>Action: ${item.actionCopy}</p>`,
      `<p>Source: ${profile}</p>`,
      `<p>Override: ${item.overrideCopy}</p>`,
      '</li>'
    ].join('');
  }).join('');

  body.innerHTML = [
    fallbackMarker,
    `<p data-warning-count="${viewModel.totalWarnings}">Unresolved warnings: ${viewModel.totalWarnings}</p>`,
    overflowMarker,
    `<ol>${listHtml}</ol>`,
  ].join('');
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
    renderWarningsPanel(buildWarningsViewModel(payload));

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

export {
  clampConfidence,
  confidenceToColor,
  buildGymHoverText,
  buildGymMarkerColors,
  buildGymTrace,
  MAX_RENDERED_WARNINGS,
  buildProfileLink,
  buildWarningsViewModel,
};
