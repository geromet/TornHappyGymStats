# S04: Accuracy Scoring & Surface Payload — UAT

**Milestone:** M002
**Written:** 2026-05-01T21:21:47.475Z

# S04: Accuracy Scoring & Surface Payload — UAT

**Milestone:** M002  
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice is backend contract/scoring logic; correctness is proven by deterministic cache/API payload assertions and end-to-end integration tests over seeded data.

## Preconditions

- Repository builds successfully on .NET 8.
- Test database fixtures can seed `DerivedGymTrain` and `ModifierProvenance` rows.
- `SurfacesCacheWriter.WriteLatestAsync` writes `latest.json` in test flow.

## Smoke Test

Run:

1. `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"`
2. Confirm the suite passes and includes confidence/reason assertions for surfaces payload.

## Test Cases

### 1. Verified and unresolved provenance project deterministic confidence/reasons

1. Seed at least two derived train points in integration fixture.
2. Seed matching `ModifierProvenance` rows for one verified case and one unresolved case.
3. Run `SurfacesCacheWriter.WriteLatestAsync` via integration test path.
4. Inspect produced `/api/v1/torn/surfaces/latest`-compatible JSON.
5. **Expected:** `gymCloud.confidence` includes deterministic values (e.g., `1.0` for verified, `0.75` for unresolved) aligned by point index.
6. **Expected:** `gymCloud.confidenceReasons` includes stable reason codes (e.g., `source-log`, `missing-faction-record`) aligned to the same indices.
7. **Expected:** Existing additive contract fields (`x`, `y`, `z`, `text`) remain present and unchanged in shape.

### 2. Missing provenance joins emit explicit fallback diagnostics

1. Seed derived train point(s) with no matching `ModifierProvenance` rows.
2. Run `SurfacesCacheWriter.WriteLatestAsync` in integration test flow.
3. Parse emitted latest surfaces payload.
4. **Expected:** Missing join point(s) emit fallback confidence `0.2`.
5. **Expected:** Corresponding reason includes `missing-provenance-record`.
6. **Expected:** Payload still emits full point arrays (no dropped points, no schema break).

## Edge Cases

### Mixed provenance statuses on a single train point

1. Seed multiple provenance rows for one derived train with mixed statuses.
2. Execute cache writer and inspect point output.
3. **Expected:** Confidence computation remains deterministic across runs (same input => same output), reasons deduplicate, and reason ordering is stable.

## Failure Signals

- Integration tests fail on confidence numeric mismatches or reason-code mismatches.
- `gymCloud.confidence` / `gymCloud.confidenceReasons` missing or length-misaligned with point arrays.
- Fallback case omits `missing-provenance-record` or emits non-deterministic values across identical fixtures.

## Not Proven By This UAT

- Frontend rendering quality of red→green gradients/tooltips (covered by S05).
- Operational workflow for resolving missing owner/faction/company data via user guidance/manual overrides (covered by S06).

## Notes for Tester

- Execute test commands sequentially; parallel `dotnet test` runs can contend on `MvcTestingAppManifest.json` and create false-negative file-lock failures.
- Treat the integration assertions as authoritative for contract stability because they validate the exact serialized payload shape consumed by `/api/v1/torn/surfaces/latest`.
