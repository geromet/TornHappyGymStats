---
estimated_steps: 36
estimated_files: 4
skills_used: []
---

# T03: Render actionable warning workflow in dashboard and add end-to-end verify script

Expose provenance warnings in the web dashboard with clear action copy, profile links, and manual-override messaging, then lock behavior with JS tests and a slice verify script.

### Failure Modes (Q5)
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| `latest.json` warnings payload | Render empty-state warning panel and preserve existing graph rendering | N/A | Fallback to `missing-provenance-record` message and continue rendering |
| Browser-side link construction | Omit broken link and show plain identifier text | N/A | Escape invalid URL parts to avoid injection |

### Load Profile (Q6)
- **Shared resources**: client-side DOM updates and tooltip rendering.
- **Per-operation cost**: O(unresolved warnings) DOM nodes.
- **10x breakpoint**: warning list UI clutter/perf; enforce display cap + overflow note.

### Negative Tests (Q7)
- **Malformed inputs**: missing warnings array, invalid link target, oversized warning text.
- **Error paths**: empty payload still renders stable dashboard.
- **Boundary conditions**: many warnings render capped list with deterministic ordering.

### Steps
1. Extend `web/app.js` data transform/render pipeline to include a dedicated warnings section with actionable copy and links.
2. Add Node test coverage for warning rendering, fallback behavior, and manual-override indicator text.
3. Create `scripts/verify/s06-provenance-warnings.sh` to generate/check local surfaces artifacts and assert warning workflow fields are present.

### Must-Haves
- [ ] Users can identify missing owner/faction/company context and next action from dashboard alone.
- [ ] Existing confidence color/tooltip behavior remains unchanged for non-warning paths.

### Verification
- `node --test tests/web/confidence-visualization.test.mjs tests/web/provenance-warnings-workflow.test.mjs`
- `bash scripts/verify/s06-provenance-warnings.sh`

### Observability Impact
- Signals added/changed: warning panel count and fallback markers visible in rendered UI/data.
- How a future agent inspects this: run node tests and verify script; inspect generated `latest.json` warning nodes.
- Failure state exposed: malformed warning payloads produce explicit fallback copy.

### Inputs
- `web/app.js` — existing confidence render/tooltip logic
- `tests/web/confidence-visualization.test.mjs` — existing deterministic frontend contract tests
- `scripts/verify/s05-local-surfaces.sh` — prior slice verification baseline

### Expected Output
- `web/app.js` — actionable warning rendering workflow
- `tests/web/provenance-warnings-workflow.test.mjs` — warning UI behavior tests
- `scripts/verify/s06-provenance-warnings.sh` — executable slice verification

## Inputs

- ``web/app.js``
- ``tests/web/confidence-visualization.test.mjs``
- ``scripts/verify/s05-local-surfaces.sh``

## Expected Output

- ``web/app.js``
- ``tests/web/provenance-warnings-workflow.test.mjs``
- ``scripts/verify/s06-provenance-warnings.sh``

## Verification

node --test tests/web/confidence-visualization.test.mjs tests/web/provenance-warnings-workflow.test.mjs && bash scripts/verify/s06-provenance-warnings.sh

## Observability Impact

Makes warning/fallback state directly inspectable in UI and verification artifacts.
