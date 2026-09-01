# S05: Frontend Confidence Visualization

**Goal:** Render provenance confidence directly in the frontend surfaces visualization by mapping `gymCloud.confidence` to a deterministic red→green marker gradient and surfacing `gymCloud.confidenceReasons` in point hover copy so operators can understand evidence completeness at a glance.
**Demo:** After this slice, point clouds color by confidence gradient with tooltips explaining evidence coverage and missing sources.

## Must-Haves

- Local pipeline can generate `web/data/surfaces/meta.json` and `web/data/surfaces/latest.json` from this environment before frontend verification runs.
- Gym cloud markers are color-mapped from confidence (0.0–1.0) using a deterministic red→green scale and retain prior geometry/point counts.
- Hover tooltip for each gym point includes confidence value plus human-readable reason list derived from `gymCloud.confidenceReasons` (including fallback reason codes).
- Frontend gracefully handles missing/short/malformed confidence arrays by applying deterministic fallback styling and reason text without breaking plot render.
- Automated verification proves confidence-driven color assignment and tooltip content generation for complete, unresolved, and missing-provenance scenarios.

## Proof Level

- This slice proves: This slice proves: integration
Real runtime required: yes
Human/UAT required: yes (visual spot-check of gradient + tooltip readability).

## Integration Closure

Upstream surfaces consumed: `/api/v1/torn/surfaces/latest` contract from `src/HappyGymStats.Api/SurfacesCacheWriter.cs` + `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs`.
New wiring introduced in this slice: a local publication/run path that materializes `web/data/surfaces/*`, then frontend `web/app.js` confidence rendering.
What remains before the milestone is truly usable end-to-end: nothing for confidence visualization; S06 remains for unresolved provenance reduction workflows.

## Verification

- Runtime signals: API import status endpoint + generated `web/data/surfaces/meta.json` version stamp + frontend status banner.
- Inspection surfaces: `scripts/verify/s05-local-surfaces.sh` command output, files in `web/data/surfaces/`, and browser Plotly trace state.
- Failure visibility: missing pipeline output fails fast before UI work; malformed confidence metadata is visible via fallback tooltip reason (`missing-provenance-record`).
- Redaction constraints: no keys or secrets persisted to frontend artifacts.

## Tasks

- [x] **T01: Add local surfaces publication/verification pipeline for S05** `est:45m`
  ---
estimated_steps: 5
estimated_files: 4
skills_used:
  - test
---

# T01: Add local surfaces publication/verification pipeline for S05

**Slice:** S05 — Frontend Confidence Visualization
**Milestone:** M002

## Description

Create a deterministic local verification script that starts from repo code, runs the API/seed path, and asserts `web/data/surfaces/meta.json` + `latest.json` exist before frontend tasks consume them.

## Failure Modes

| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| Local API run/import path | Fail fast with actionable script error | Timeout exits non-zero with hint to inspect API logs | Fail verification if surfaces files missing required JSON keys |
| Filesystem write to `web/data/surfaces/` | Fail script and report directory/permission issue | N/A | Reject empty or invalid JSON artifacts |

## Load Profile

- **Shared resources**: local SQLite DB + API background process.
- **Per-operation cost**: one import job + one surfaces cache write.
- **10x breakpoint**: import duration, not file assertions.

## Negative Tests

- **Malformed inputs**: missing API key env for import path.
- **Error paths**: import job not producing cache within timeout.
- **Boundary conditions**: empty dataset still must produce valid JSON envelopes.

## Steps

1. Add `scripts/verify/s05-local-surfaces.sh` to run local publication flow and assert output files.
2. Ensure the script validates both file existence and required keys (`version`, `series.gymCloud`).
3. Add a short docs note to run this script before frontend confidence verification.
4. Keep script portable (bash + existing repo tools only).
5. Run and capture pass/fail output.

## Must-Haves

- [ ] Script fails when surfaces artifacts are absent.
- [ ] Script passes only when `web/data/surfaces/meta.json` and `web/data/surfaces/latest.json` are present and parseable.

## Verification

- `bash scripts/verify/s05-local-surfaces.sh`

## Observability Impact

- Signals added/changed: explicit precondition check output for S05 local data readiness.
- How a future agent inspects this: run `bash scripts/verify/s05-local-surfaces.sh`.
- Failure state exposed: precise missing/invalid artifact path.

## Inputs

- `src/HappyGymStats.Api/Program.cs` — surfaces cache location + endpoints.
- `scripts/verify/build-and-test.sh` — style reference for verify scripts.
- `README.md` — local run guidance.

## Expected Output

- `scripts/verify/s05-local-surfaces.sh` — executable local surfaces readiness verifier.
- `web/data/surfaces/meta.json` — local generated metadata artifact.
- `web/data/surfaces/latest.json` — local generated payload artifact.
- `README.md` — short pre-frontend verification note.
  - Files: `scripts/verify/s05-local-surfaces.sh`, `src/HappyGymStats.Api/Program.cs`, `README.md`, `web/data/surfaces/meta.json`, `web/data/surfaces/latest.json`
  - Verify: bash scripts/verify/s05-local-surfaces.sh

- [x] **T02: Implement confidence gradient + reason tooltip transformation in gym cloud renderer** `est:50m`
  ---
estimated_steps: 6
estimated_files: 3
skills_used:
  - frontend-design
  - test
---

# T02: Implement confidence gradient + reason tooltip transformation in gym cloud renderer

**Slice:** S05 — Frontend Confidence Visualization
**Milestone:** M002

## Description

Wire S04 confidence metadata into the static frontend so each gym point color reflects confidence and tooltip copy explains provenance coverage/missing sources.

## Inputs

- `web/app.js` — existing rendering/fetch flow to extend.
- `web/index.html` — chart containers and script wiring.
- `web/data/surfaces/latest.json` — generated local payload from T01.

## Expected Output

- `web/app.js` — confidence-to-color and tooltip transformations wired into gym cloud trace.
- `web/index.html` — optional tooltip/help text adjustments.
- `web/styles.css` — optional legend styling.

## Verification

- `node --test tests/web/confidence-visualization.test.mjs`
  - Files: `web/app.js`, `web/index.html`, `web/styles.css`
  - Verify: node --test tests/web/confidence-visualization.test.mjs

- [x] **T03: Add automated frontend confidence visualization regression tests** `est:40m`
  ---
estimated_steps: 5
estimated_files: 4
skills_used:
  - test
  - verify-before-complete
---

# T03: Add automated frontend confidence visualization regression tests

**Slice:** S05 — Frontend Confidence Visualization
**Milestone:** M002

## Description

Create deterministic tests for gradient mapping, fallback handling, and tooltip reason messaging against generated/fixture payloads.

## Inputs

- `web/app.js` — helper functions and trace construction from T02.
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — upstream confidence contract.
- `web/data/surfaces/latest.json` — local generated payload from T01.

## Expected Output

- `tests/web/confidence-visualization.test.mjs` — frontend visualization contract tests.
- `tests/fixtures/surfaces/latest-confidence-sample.json` — tracked fixture payload.
- `web/app.js` — exported pure helpers for testability (if needed).

## Verification

- `node --test tests/web/confidence-visualization.test.mjs && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"`
  - Files: `tests/web/confidence-visualization.test.mjs`, `tests/fixtures/surfaces/latest-confidence-sample.json`, `web/app.js`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
  - Verify: node --test tests/web/confidence-visualization.test.mjs && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"

## Files Likely Touched

- scripts/verify/s05-local-surfaces.sh
- src/HappyGymStats.Api/Program.cs
- README.md
- web/data/surfaces/meta.json
- web/data/surfaces/latest.json
- web/app.js
- web/index.html
- web/styles.css
- tests/web/confidence-visualization.test.mjs
- tests/fixtures/surfaces/latest-confidence-sample.json
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
