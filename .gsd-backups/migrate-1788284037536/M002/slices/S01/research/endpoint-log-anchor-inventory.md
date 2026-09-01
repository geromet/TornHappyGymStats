# Endpoint & Log Anchor Inventory (S01/T01)

## Scope

This inventory records the current local anchors for modifier-bearing evidence discovery before provenance modeling work begins. It captures what is present in code today and what remains implicit/unknown for personal, faction, and company modifier reconstruction.

## API Endpoints

Anchors from `src/HappyGymStats.Api/Program.cs`:

- `POST /api/v1/torn/import-jobs`
  - Starts import execution by enqueuing `ImportService.Enqueue(apiKey, fresh)`.
  - Returns queued/running/terminal import status via `ImportStatusDto`.
  - Key scope implication: currently single API key per import request; no explicit endpoint-level distinction for personal vs faction/company data ownership.
- `GET /api/v1/torn/import-jobs/latest`
  - Exposes latest in-memory import lifecycle state (`ImportService.Latest`).
- `GET /api/v1/torn/health`
  - Connectivity/health only; no modifier semantics.
- `GET /api/v1/torn/surfaces/meta` and `GET /api/v1/torn/surfaces/latest`
  - Reads cached surface artifacts; this is the downstream surface where future confidence/provenance outputs will be emitted.
- `GET /api/v1/torn/gym-trains`
  - Paginated read model (`DerivedGymTrains`) with happy-before/after and regeneration fields.
- `GET /api/v1/torn/happy-events`
  - Paginated read model (`DerivedHappyEvents`) with event type, delta, and notes.

Endpoint taxonomy observation:
- Current public API is reconstruction-output-centric (gym trains/happy events/surfaces), not raw Torn-log-taxonomy-centric.
- There is no endpoint that exposes raw modifier evidence classes or source-key scope completeness yet.

## Torn Fetch Entry

Anchors from `src/HappyGymStats.Api/ImportService.cs` and `src/HappyGymStats.Core/Torn/TornApiClient.cs`:

- Import entrypoint uses:
  - `FetchOptions.Default(new Uri("https://api.torn.com/v2/user/log?cat=25"), TimeSpan.FromMilliseconds(1100))`
  - This indicates current fetch scope is user logs with category filter `cat=25`.
- Torn fetch transport behavior (`TornApiClient.GetUserLogPageAsync`):
  - Injects `key=...` query parameter into absolute Torn URL.
  - Requires JSON root with `log` array and `_metadata.links` paging object.
  - Paginates backward via `_metadata.links.prev` preferentially.
  - Captures per-log minimum fields:
    - `id`
    - `timestamp`
    - `details.title` (optional)
    - `details.category` (optional)
    - `Raw` full JSON payload clone.
  - Handles retryability signals for HTTP 429/5xx/timeout and Torn error signatures.

Fetch taxonomy observation:
- Existing code captures raw payloads and top-level title/category hints, which is sufficient to bootstrap a modifier taxonomy matrix in later tasks.
- Current fetch URI and parser are user-log focused; faction/company-owner-specific coverage is not explicitly modeled in this layer.

## Extractor Fields

Anchors from `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`:

Primary extraction heuristics (all rooted in case-insensitive lookup under `data` object):

- Gym train signal:
  - `data.happy_used` => `GymTrainEvent(HappyUsed)`
- Max happy signal:
  - Preferred: `data.maximum_happy_after` + optional `data.maximum_happy_before`
  - Heuristic fallback: if title includes "max" + "happy", infer from first 1–2 numeric fields in `data`
- Overdose signal:
  - Title contains `overdose` + `data.happy_decreased` > 0
  - Drug mapping from title token (`ecstasy|ketamine|lsd|pcp|shrooms|speed|xanax`) to percent loss
- Generic happy delta signal:
  - `data.happy_increased` and/or `data.happy_decreased`
  - Excludes records already classified as gym train or recognized overdose

Defensive/quality anchors:

- JSON parse failures counted (`JsonParseFailures`) instead of throwing.
- Missing `data` object counted (`MissingDetailsCount`).
- Numeric bounds checks tracked (`NumericOutOfRangeCount`).
- Output currently focused on happy reconstruction events, not modifier provenance dimensions.

## Known Gaps

1. **Modifier class taxonomy not yet encoded**
   - No first-class event types for personal/faction/company modifier changes, activations, or expirations.

2. **Scope attribution missing**
   - No model fields tie extracted evidence to key scope (`personal` vs `faction` vs `company`) or data-owner identity.

3. **Log-ID/category matrix absent**
   - While `details.category` and raw JSON are captured, there is no persisted matrix of log IDs/categories → modifier semantics/confidence impact.

4. **Confidence impact mapping absent**
   - Reconstruction outputs do not yet carry provenance completeness or confidence gradients tied to missing evidence classes.

5. **Assumption boundaries (explicit)**
   - Assumption A: Torn user log payloads needed for modifier inference are present in/derivable from current `cat=25` stream.
   - Assumption B: `details.title`, `details.category`, and raw `data` subfields are sufficiently stable to bootstrap deterministic taxonomy anchors.
   - Assumption C: faction/company dependencies may require additional owner-linked fetch strategies beyond current single-key user-log flow.

## Next-Slice Readiness Notes

- The current anchor set is adequate for constructing a deterministic endpoint/log taxonomy matrix in subsequent tasks.
- Follow-up slices should persist a machine-checkable mapping (endpoint/fetch/extractor anchor references + expected payload keys + confidence impact) and add drift detection against these source anchors.
