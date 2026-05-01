const storageKey = "happy-gym-stats.api-base-url";
const params = new URLSearchParams(window.location.search);

const elements = {
  apiBaseInput: document.querySelector("#api-base-url"),
  saveApiBaseButton: document.querySelector("#save-api-base"),
  reloadButton: document.querySelector("#reload-dashboard"),
  connectionBadge: document.querySelector("#connection-badge"),
  healthPill: document.querySelector("#health-pill"),
  healthStatus: document.querySelector("#health-status"),
  healthApi: document.querySelector("#health-api"),
  healthDb: document.querySelector("#health-db"),
  gymTrainsCount: document.querySelector("#gym-trains-count"),
  happyEventsCount: document.querySelector("#happy-events-count"),
  gymTrainsBody: document.querySelector("#gym-trains-body"),
  happyEventsBody: document.querySelector("#happy-events-body"),
  activityLog: document.querySelector("#activity-log"),
};

initialize();

function initialize() {
  const defaultBase = params.get("api")
    ?? window.localStorage.getItem(storageKey)
    ?? "https://www.geromet.com";

  elements.apiBaseInput.value = defaultBase;

  elements.saveApiBaseButton.addEventListener("click", () => {
    const value = normalizeBaseUrl(elements.apiBaseInput.value);
    if (!value) {
      log("Cannot save empty API base URL.");
      setBadge(elements.connectionBadge, "warning", "Missing URL");
      return;
    }

    window.localStorage.setItem(storageKey, value);
    elements.apiBaseInput.value = value;
    log(`Saved API base URL: ${value}`);
    setBadge(elements.connectionBadge, "idle", "Saved");
  });

  elements.reloadButton.addEventListener("click", () => refreshDashboard());

  refreshDashboard();
}

async function refreshDashboard() {
  const baseUrl = normalizeBaseUrl(elements.apiBaseInput.value);
  if (!baseUrl) {
    setBadge(elements.connectionBadge, "warning", "Missing URL");
    log("Set an API base URL before refreshing.");
    return;
  }

  window.localStorage.setItem(storageKey, baseUrl);
  elements.apiBaseInput.value = baseUrl;
  setBadge(elements.connectionBadge, "idle", "Checking");
  log(`Refreshing dashboard from ${baseUrl}`);

  try {
    const [health, gymTrainsPage, happyEventsPage] = await Promise.all([
      fetchJson(baseUrl, "/v1/health"),
      fetchJson(baseUrl, "/v1/gym-trains?limit=5"),
      fetchJson(baseUrl, "/v1/happy-events?limit=5"),
    ]);

    renderHealth(health);
    renderGymTrains(gymTrainsPage.items ?? []);
    renderHappyEvents(happyEventsPage.items ?? []);

    setBadge(elements.connectionBadge, "ok", "Connected");
    log("Refresh complete.");
  } catch (error) {
    renderHealthFailure(error);
    renderGymTrains([]);
    renderHappyEvents([]);
    setBadge(elements.connectionBadge, "error", "Failed");
    log(error instanceof Error ? error.message : String(error));
  }
}

async function fetchJson(baseUrl, path) {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: {
      Accept: "application/json",
    },
  });

  if (!response.ok) {
    const body = await safeReadText(response);
    throw new Error(`${response.status} ${response.statusText} from ${path}${body ? ` — ${body}` : ""}`);
  }

  return await response.json();
}

async function safeReadText(response) {
  try {
    return await response.text();
  } catch {
    return "";
  }
}

function renderHealth(health) {
  elements.healthStatus.textContent = health.status ?? "unknown";
  elements.healthApi.textContent = health.api ?? "unknown";
  elements.healthDb.textContent = health.databaseProvider ?? "unknown";

  if (health.status === "ok") {
    setBadge(elements.healthPill, "ok", "Healthy");
  } else {
    setBadge(elements.healthPill, "warn", String(health.status ?? "Unknown"));
  }
}

function renderHealthFailure(error) {
  elements.healthStatus.textContent = "unreachable";
  elements.healthApi.textContent = "—";
  elements.healthDb.textContent = "—";
  setBadge(elements.healthPill, "error", "Unavailable");

  if (error instanceof Error)
    log(`Health check failed: ${error.message}`);
}

function renderGymTrains(rows) {
  elements.gymTrainsCount.textContent = `${rows.length} row${rows.length === 1 ? "" : "s"}`;

  if (rows.length === 0) {
    elements.gymTrainsBody.innerHTML = `<tr><td colspan="6" class="table-empty">No rows returned.</td></tr>`;
    return;
  }

  elements.gymTrainsBody.innerHTML = rows.map(row => `
    <tr>
      <td>${formatDate(row.occurredAtUtc)}</td>
      <td>${formatNumber(row.happyBeforeTrain)}</td>
      <td>${formatNumber(row.happyUsed)}</td>
      <td>${formatNumber(row.happyAfterTrain)}</td>
      <td>${formatNumber(row.regenHappyGained)}</td>
      <td>${row.maxHappyAtTimeUtc == null ? "—" : formatNumber(row.maxHappyAtTimeUtc)}</td>
    </tr>`).join("");
}

function renderHappyEvents(rows) {
  elements.happyEventsCount.textContent = `${rows.length} row${rows.length === 1 ? "" : "s"}`;

  if (rows.length === 0) {
    elements.happyEventsBody.innerHTML = `<tr><td colspan="6" class="table-empty">No rows returned.</td></tr>`;
    return;
  }

  elements.happyEventsBody.innerHTML = rows.map(row => `
    <tr>
      <td>${formatDate(row.occurredAtUtc)}</td>
      <td>${escapeHtml(row.eventType ?? "—")}</td>
      <td>${row.delta == null ? "—" : formatSignedNumber(row.delta)}</td>
      <td>${row.happyBeforeEvent == null ? "—" : formatNumber(row.happyBeforeEvent)}</td>
      <td>${row.happyAfterEvent == null ? "—" : formatNumber(row.happyAfterEvent)}</td>
      <td>${escapeHtml(row.note ?? "—")}</td>
    </tr>`).join("");
}

function normalizeBaseUrl(value) {
  const trimmed = (value ?? "").trim();
  if (!trimmed)
    return "";

  return trimmed.replace(/\/+$/, "");
}

function setBadge(element, state, text) {
  element.className = `badge badge--${state}`;
  element.textContent = text;
}

function log(message) {
  const timestamp = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  const previous = elements.activityLog.textContent?.trim();
  elements.activityLog.textContent = previous && previous !== "Waiting for first refresh…"
    ? `[${timestamp}] ${message}\n${previous}`
    : `[${timestamp}] ${message}`;
}

function formatDate(value) {
  if (!value)
    return "—";

  const date = new Date(value);
  if (Number.isNaN(date.valueOf()))
    return escapeHtml(String(value));

  return date.toLocaleString([], {
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    timeZoneName: "short",
  });
}

function formatNumber(value) {
  return new Intl.NumberFormat().format(Number(value));
}

function formatSignedNumber(value) {
  const numeric = Number(value);
  const formatter = new Intl.NumberFormat(undefined, { signDisplay: "always" });
  return formatter.format(numeric);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}
