# S06: Owner/Faction/Company Data Acquisition Workflow — UAT

**Milestone:** M002
**Written:** 2026-05-01T22:00:24.902Z

# S06: Owner/Faction/Company Data Acquisition Workflow — UAT

**Milestone:** M002  
**Written:** 2026-05-01

## UAT Type

- UAT mode: mixed (artifact-driven + live-runtime)
- Why this mode is sufficient: this slice’s value is both runtime payload correctness (`latest.json` warning/diagnostic fields) and operator-facing warning UX behavior in dashboard rendering.

## Preconditions

- Project builds and tests are runnable locally.
- Local API can start and serve `/api/v1/torn/health`.
- `scripts/verify/s06-provenance-warnings.sh` is executable.
- Surfaces artifact path `web/data/surfaces/latest.json` is writable.

## Smoke Test

Run `bash scripts/verify/s06-provenance-warnings.sh` and confirm it completes with `S06 provenance warnings verification passed`.

## Test Cases

### 1. API projects unresolved provenance into deterministic warning payload

1. Run `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests|FullyQualifiedName~ModifierOverride"`.
2. Inspect passing test output for warning projection and malformed-row coverage.
3. **Expected:** all tests pass; unresolved warning records are additive/deterministic; malformed rows are skipped with diagnostics; confidence arrays/reasons remain contract-stable.

### 2. Dashboard renders actionable warning workflow without regressing confidence UX

1. Run `node --test tests/web/confidence-visualization.test.mjs tests/web/provenance-warnings-workflow.test.mjs`.
2. Verify tests covering fallback marker, profile link safety, overflow capping, and manual-override text pass.
3. **Expected:** warning panel behavior is deterministic and actionable; confidence color/tooltip behavior remains unchanged.

### 3. End-to-end local artifact and warning shape verification

1. Run `bash scripts/verify/s06-provenance-warnings.sh`.
2. Confirm baseline surfaces generation succeeds and warning-shape checks execute.
3. **Expected:** script passes with explicit validation of warning payload shape; if no unresolved warnings exist, script reports accepted empty-state behavior rather than failure.

## Edge Cases

### Malformed/invalid warning payload input

1. Execute `node --test tests/web/provenance-warnings-workflow.test.mjs`.
2. **Expected:** UI model falls back to `missing-provenance-record`, suppresses invalid links safely, truncates oversized warning text, and keeps deterministic ordering.

### Invalid or malformed local override source

1. Execute the dotnet filter including `ModifierOverride` tests.
2. **Expected:** loader rejects malformed entries, keeps valid subset, emits parse/read/skip diagnostics, and payload generation continues without crash.

## Failure Signals

- Dotnet test filter reports failures in `DbPipelineIntegrationTests`, `SurfaceSeriesBuilderConfidenceTests`, or `ModifierOverride` tests.
- Node tests fail in warning workflow/fallback/link-safety/capping assertions.
- Verify script fails to generate surfaces artifact, fails warning-shape checks, or cannot reach local API health endpoint.

## Not Proven By This UAT

- Organization-wide governance/auditing lifecycle for manual override ownership beyond local operator workflow.
- Production-scale performance characteristics under very large unresolved-warning volumes (the capped rendering + bounded projection logic is covered by contract tests, not stress deployment telemetry).

## Notes for Tester

Focus on operator outcomes: unresolved provenance must be visible with actionable next steps, and optional manual overrides must enrich guidance only (never mutate persisted provenance history). The accepted empty-warning path is valid and should still show deterministic panel behavior.
