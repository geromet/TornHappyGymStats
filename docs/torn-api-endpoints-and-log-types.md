# Torn API Endpoints & Log Type/Category Usage

_Last updated: 2026-05-08_

This document describes how HappyGymStats currently uses Torn API endpoints, how paging works, and which log types/categories are consumed in each stage.

---

## 1) Endpoint Inventory (Current Implementation)

## 1.1 `GET /v2/user/basic`

**Purpose in system**
- Validate submitted API key.
- Resolve Torn player identity (`profile.id`) at import start.

**Where used**
- `src/HappyGymStats.Core/Torn/TornApiClient.cs` → `GetPlayerIdAsync`
- Called from `ImportOrchestrator.RunImportAsync` before fetch pipeline starts.

**Response fields used**
- `profile.id` (required)

**Failure behavior**
- Torn error payload (`code`, `error`) translated to user-safe `TornApiException`.
- Non-success HTTP and malformed JSON handled as exceptions.
- Retryability inferred for transient/network/rate-limit conditions.

---

## 1.2 `GET /v2/user/log?cat=25`

**Purpose in system**
- Primary gym/happy import stream for baseline user logs.

**Where used**
- `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`
  - `LogFetcher.RunAsync` started with fresh URL: `https://api.torn.com/v2/user/log?cat=25`

**How it is consumed**
- Pages fetched with retry/backoff.
- Logs deduped by `log.id` (`LogEntryId`).
- Mapped into `UserLogEntries` fields including:
  - `details.id` → `LogTypeId`
  - `details.title` → `Title`
  - `details.category` → `Category`
  - `data.*` fields for happy/energy/stat projections.

**Paging behavior**
- Prefers `_metadata.links.prev` to traverse older history.
- Falls back to `_metadata.links.next` only when `prev` missing.
- Stops when next cursor is absent/null/empty or page has zero logs.

---

## 1.3 `GET /v2/user/log?log=<typeId>&limit=100`

**Purpose in system**
- Secondary targeted fetch for perk/property/faction/company-related log types.
- Used to enrich reconstruction and provenance beyond cat-based baseline.

**Where used**
- `src/HappyGymStats.Core/Fetch/PerkLogFetcher.cs`
- Loop URL template: `https://api.torn.com/v2/user/log?log={id}&limit=100`

**How it is consumed**
- For each configured type, paged fetch with retry/backoff.
- New logs inserted into `UserLogEntries`.
- If scope is faction/company, extract affiliation events into `AffiliationEvents`.

---

## 2) Authentication, Transport, and Error Handling

## 2.1 API key injection
- API key is appended as query parameter `key=...` by `BuildUrlWithApiKey`.
- Existing `key=` query params are filtered/replaced to avoid duplication.

## 2.2 HTTP client behavior
- Accept header `application/json`.
- Reads headers first (`ResponseHeadersRead`) then parses JSON stream.

## 2.3 Retry policy summary
- Retries for network timeouts, request failures, HTTP 429/5xx, and obvious Torn rate-limit errors.
- Exponential backoff controlled by `FetchOptions`.
- Non-retryable semantic errors bubble and fail the job.

---

## 3) Log Payload Fields Used by This Project

From each `log` element (`UserLog` model):

- `id` → unique source event identity for dedup
- `timestamp` → `OccurredAtUtc`
- `details.id` → internal `LogTypeId`
- `details.title` → `Title`
- `details.category` → `Category`
- `data` object (best-effort parse) for domain fields:
  - `happy_before`, `happy_used`, `happy_increased`, `happy_decreased`
  - `maximum_happy_before`, `maximum_happy_after`
  - `energy_used`, stat-before/increase values
  - `property_id`, `happy` (property max-happy transitions)
  - affiliation keys (`faction` / `company`) and sender/position metadata

All other payload content is retained only via raw element clone for future extraction logic.

---

## 4) Configured Targeted Log Types (`PerkLogTypes.All`)

Source: `src/HappyGymStats.Core/Fetch/PerkLogTypes.cs`

## 4.1 Personal scope

**Property**
- `5900` Property upgrade
- `5905` Property staff
- `5910` Property move
- `5915` Property kick
- `5916` Property kick receive
- `5920` Property upkeep

**Education**
- `5963` Education complete

**Book**
- `2051` Item finish book
- `2052` Item finish book strength increase
- `2053` Item finish book speed increase
- `2054` Item finish book defense increase
- `2055` Item finish book dexterity increase
- `2056` Item finish book working stats increase
- `2057` Item finish book list capacity increase
- `2058` Item finish book merit reset
- `2059` Item finish book drug addiction removal

**Enhancer**
- `2120` Item use parachute
- `2130` Item use skateboard
- `2140` Item use boxing gloves
- `2150` Item use dumbbells

**Stock**
- `5511` Stock sell
- `5545` Stock special passive active

## 4.2 Company scope
- `6210` Job join
- `6215` Job promote
- `6217` Job fired
- `6243` Company application accept receive
- `6260` Company quit
- `6261` Company fire send
- `6262` Company fire receive

## 4.3 Faction scope
- `6253` Faction application accept receive
- `6827` Faction member position auto change receive

---

## 5) Reconstruction-Relevant Log Type Rules (Current)

Source: `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`

## 5.1 Max-happy property transitions
- `5910` (property move): always considered for max-happy transition when fields present.
- `5900` / `5905`: considered only when event property matches current residence context.

## 5.2 Gym train extraction
- Primary signal is `HappyUsed != null` (not just log type ID).

## 5.3 Overdose handling
- Based on title pattern containing overdose + known substances.
- Converts into typed overdose event with percent-loss rules.

## 5.4 Company provenance special handling
- In `ReconstructionRunner`, leave/fire-type company logs (`6260`, `6261`, `6262`) invalidate active company affiliation for provenance mapping at/after event point.

---

## 6) Categories vs Log Types in Current Logic

- **LogTypeId (`details.id`)** is authoritative for most deterministic rules.
- **Category (`details.category`)** is ingested and stored but currently not a primary branching key for reconstruction logic.
- **Title (`details.title`)** is used in specific heuristic cases (e.g., overdose text detection).

Implication: adding category-driven rules should be explicit and tested, since current pipeline is mostly type-ID driven.

---

## 7) Operational Notes for Extending Endpoint/Type Usage

If adding a new Torn endpoint or log type:

1. Add fetch entrypoint in orchestrator/fetcher with clear scope (baseline vs targeted).
2. Add parse mapping for required `data.*` fields in fetch mapper.
3. Add extractor/reconstruction rule only where semantics are deterministic.
4. Add provenance mapping if affiliation/confidence implications exist.
5. Add verification command(s) for import outcome and surfaces contract stability.

---

## 8) Quick Verification Commands

```bash
curl -sS https://torn.geromet.com/api/v1/torn/import-jobs/latest | jq
```

```bash
curl -sS https://torn.geromet.com/api/v1/torn/surfaces/latest | jq '.version, (.series.gymCloud|length)'
```

```bash
sudo journalctl -u happygymstats-api -n 200 --no-pager
```

---

## 9) Source Reference Index

- `src/HappyGymStats.Core/Torn/TornApiClient.cs`
- `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`
- `src/HappyGymStats.Core/Fetch/LogFetcher.cs`
- `src/HappyGymStats.Core/Fetch/PerkLogFetcher.cs`
- `src/HappyGymStats.Core/Fetch/PerkLogTypes.cs`
- `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`
- `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`
- `docs/TORN-API-TOS.md`
