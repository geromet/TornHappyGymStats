# S06: Owner/Faction/Company Data Acquisition Workflow

**Goal:** Deliver an operator-first acquisition workflow that turns unresolved owner/faction/company provenance into actionable guidance in the UI and supports optional local manual overrides for faction/company identifiers without breaking deterministic confidence semantics.
**Demo:** After this slice, users can view actionable warnings with profile links and optionally enter manual faction/company overrides.

## Must-Haves

- ## Demo
- After this slice, users can open the dashboard and see actionable unresolved-provenance warnings with profile links for owner/faction/company contexts, and can optionally supply local faction/company override mappings that improve warning specificity on subsequent refreshes.
- ## Must-Haves
- Surface unresolved provenance reason codes as explicit operator warnings (not hidden in raw payload fields).
- Each warning includes concrete next action(s) and at least one deep-link target when an identifier is known.
- Optional override input path exists for faction/company identifiers, is clearly marked local/manual, and is non-destructive to stored provenance rows.
- Warning rendering remains deterministic for missing mappings and preserves existing `missing-provenance-record` fallback behavior.
- ## Threat Surface
- **Abuse**: Tampered override input could inject invalid IDs or excessively large payloads to degrade UI behavior.
- **Data exposure**: No secrets; only Torn IDs/reason codes already available in local dataset. Avoid echoing arbitrary user-provided strings into HTML unsafely.
- **Input trust**: Override file/input is untrusted and must be schema-validated + bounded before use.
- ## Requirement Impact
- **Requirements touched**: R001, R002.
- **Re-verify**: end-to-end unresolved provenance diagnostics in surfaces payload + UI warning presentation + fallback behavior.
- **Decisions revisited**: D003 (deterministic frontend contract testing strategy).
- ## Proof Level
- This slice proves: integration
- Real runtime required: yes
- Human/UAT required: yes
- ## Verification
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests"`
- `node --test tests/web/confidence-visualization.test.mjs tests/web/provenance-warnings-workflow.test.mjs`
- `bash scripts/verify/s06-provenance-warnings.sh`
- ## Observability / Diagnostics
- Runtime signals: unresolved warning count and reason-code histogram emitted in surfaces metadata/summary logs.
- Inspection surfaces: `/api/v1/torn/surfaces/latest` warning fields, `web/data/surfaces/latest.json`, and verify script output.
- Failure visibility: invalid override parse errors and skipped-entry counts are explicit in diagnostics output.
- Redaction constraints: only non-secret Torn IDs; no token/session/user secret material persisted.
- ## Integration Closure
- Upstream surfaces consumed: `ReconstructionRunner` provenance reason codes, API surfaces cache payload, frontend `web/app.js` hover + rendering pipeline.
- New wiring introduced in this slice: warning projection and optional override merge path from local override source to rendered guidance.
- What remains before the milestone is truly usable end-to-end: nothing for operator warning workflow; broader override governance/auditing remains out-of-scope for M002.

## Proof Level

- This slice proves: integration

## Integration Closure

Consumes persisted unresolved provenance rows from S03 and confidence projection contract from S04/S05, then wires them into actionable UI warnings plus optional local override hints without mutating provenance persistence semantics.

## Verification

- Adds explicit unresolved diagnostics and override-parse visibility so future agents can localize why warnings persist (missing source data vs invalid override mapping).

## Tasks

- [x] **T01: Project unresolved provenance into operator warning records in API/cache** `est:1h`
  Implement a deterministic warning projection layer that groups unresolved modifier provenance by scope/log, carries reason codes, and includes actionable link targets where IDs are available.

### Failure Modes (Q5)
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| SQLite `ModifierProvenance` query | Return empty warning collection and emit explicit diagnostic count=0 with reason `query-failed` in logs/tests | Keep existing cache payload and flag stale-warning state | Skip malformed rows (invalid scope/status) and increment skipped-row diagnostic |
| Surfaces cache serialization | Fail cache write and preserve previous artifact | N/A (local write) | Reject invalid warning object schema in tests |

### Load Profile (Q6)
- **Shared resources**: DB read connection + in-memory grouping during cache build.
- **Per-operation cost**: one additional provenance scan/group pass per cache generation.
- **10x breakpoint**: memory growth in warning aggregation if unresolved rows surge; guard with bounded per-log warning records.

### Negative Tests (Q7)
- **Malformed inputs**: unknown scope/status rows are ignored with diagnostics.
- **Error paths**: DB access exception does not crash import service loop; warning payload degrades safely.
- **Boundary conditions**: zero unresolved rows yields empty warnings array, not null.

### Steps
1. Add API/Core model for `provenanceWarnings` payload items keyed by derived log/scope/reason.
2. Extend `SurfacesCacheWriter` (or adjacent projection path) to compute warnings from unresolved provenance rows and attach actionable links using known IDs/placeholders.
3. Add/extend integration tests to verify deterministic warning cardinality, ordering, and reason semantics across mixed datasets.

### Must-Haves
- [ ] Warning payload is additive and backward-compatible for existing consumers.
- [ ] Reason-code fidelity remains 1:1 with persisted provenance rows.

### Verification
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests"`
- `grep -q "provenanceWarnings" web/data/surfaces/latest.json || true` (artifact check via verify script)

### Observability Impact
- Signals added/changed: unresolved warning totals by reason/scope.
- How a future agent inspects this: inspect generated surfaces JSON + integration test assertions.
- Failure state exposed: skipped malformed provenance rows and fallback reasons are explicit.

### Inputs
- `.gsd/milestones/M002/slices/S03/S03-SUMMARY.md` — unresolved placeholder/reason semantics from prior slice
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — current surfaces payload projection
- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs` — confidence reason behavior contract
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — integration verification baseline

### Expected Output
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — warning projection wiring
- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs` — optional payload contract extensions
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — deterministic warning assertions
  - Files: `src/HappyGymStats.Api/SurfacesCacheWriter.cs`, `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `tests/HappyGymStats.Tests/SurfaceSeriesBuilderConfidenceTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests"

- [x] **T02: Add optional local faction/company override ingestion with strict validation** `est:1h`
  Add a bounded, optional override source (tracked config/sample + parser) that maps unresolved faction/company placeholders to operator-provided IDs/links for guidance only, without mutating stored provenance records.

### Failure Modes (Q5)
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| Override file read | Continue with no overrides and emit `override-read-failed` diagnostic | N/A | Reject malformed entries, keep valid subset, expose skipped count |
| JSON parser | Use empty override set and emit parse error | N/A | Validate schema fields and ignore unknown/invalid keys |

### Load Profile (Q6)
- **Shared resources**: file I/O + small in-memory dictionary.
- **Per-operation cost**: one file read/parse per cache refresh.
- **10x breakpoint**: oversized override file; enforce max entries and max field length.

### Negative Tests (Q7)
- **Malformed inputs**: bad JSON, missing required keys, unknown scope.
- **Error paths**: missing file path and unreadable file degrade gracefully.
- **Boundary conditions**: duplicate keys resolve deterministically (last-write-wins or explicit rejection, documented in tests).

### Steps
1. Define override schema and loader utility in Core/API layer with strict validation and bounded limits.
2. Wire loader into warning projection so warnings can display richer action hints when override exists.
3. Add focused tests for parser validation, duplicate handling, and graceful fallback when file absent.

### Must-Haves
- [ ] Overrides are optional and never required for normal payload generation.
- [ ] Override usage is explicitly marked as local/manual in warning metadata.

### Verification
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierOverride|FullyQualifiedName~DbPipelineIntegrationTests"`

### Observability Impact
- Signals added/changed: override loaded/skipped entry counts.
- How a future agent inspects this: verify script + test output + warning metadata flags.
- Failure state exposed: parse/validation failures surfaced as deterministic diagnostics.

### Inputs
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — warning projection integration point
- `src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs` — reason/scope constants
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — end-to-end contract tests

### Expected Output
- `src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs` — validated override loader
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — override-aware warning enrichment
- `tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs` — parser and fallback coverage
- `web/data/surfaces/modifier-overrides.sample.json` — tracked sample override file
  - Files: `src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs`, `src/HappyGymStats.Api/SurfacesCacheWriter.cs`, `tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs`, `web/data/surfaces/modifier-overrides.sample.json`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierOverride|FullyQualifiedName~DbPipelineIntegrationTests"

- [x] **T03: Render actionable warning workflow in dashboard and add end-to-end verify script** `est:50m`
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
  - Files: `web/app.js`, `tests/web/provenance-warnings-workflow.test.mjs`, `tests/web/confidence-visualization.test.mjs`, `scripts/verify/s06-provenance-warnings.sh`
  - Verify: node --test tests/web/confidence-visualization.test.mjs tests/web/provenance-warnings-workflow.test.mjs && bash scripts/verify/s06-provenance-warnings.sh

## Files Likely Touched

- src/HappyGymStats.Api/SurfacesCacheWriter.cs
- src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
- tests/HappyGymStats.Tests/SurfaceSeriesBuilderConfidenceTests.cs
- src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs
- tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs
- web/data/surfaces/modifier-overrides.sample.json
- web/app.js
- tests/web/provenance-warnings-workflow.test.mjs
- tests/web/confidence-visualization.test.mjs
- scripts/verify/s06-provenance-warnings.sh
