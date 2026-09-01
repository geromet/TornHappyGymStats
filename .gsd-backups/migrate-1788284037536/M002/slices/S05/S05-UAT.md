# S05: S05 — UAT

**Milestone:** M002
**Written:** 2026-05-01T21:44:19.588Z

# S05: S05 — UAT

**Milestone:** M002
**Written:** 2026-05-01

## UAT Type

- UAT mode: mixed
- Why this mode is sufficient: this slice combines runtime artifact generation (`web/data/surfaces/*`) with frontend transformation behavior, so both script-driven runtime checks and artifact-driven/frontend test validation are required.

## Preconditions

- `TORN_API_KEY` or `HAPPYGYMSTATS_TORN_API_KEY` is set in environment.
- Repo dependencies are restored (`dotnet` + Node test runtime available).
- Working directory is project root.

## Smoke Test

Run `bash scripts/verify/s05-local-surfaces.sh` and confirm it finishes with `S05 local surfaces verification passed` and no JSON key errors.

## Test Cases

### 1. Local surfaces publication readiness

1. Run `bash scripts/verify/s05-local-surfaces.sh`.
2. Wait for API health, import enqueue, and cache wait stages to complete.
3. **Expected:** command exits 0 and reports valid surfaces artifacts with required `series.gymCloud` contract.

### 2. Confidence gradient and tooltip generation contract

1. Run `node --test tests/web/confidence-visualization.test.mjs`.
2. Inspect test output for gradient endpoint mapping, clamping, tooltip reasons, and fallback cases.
3. **Expected:** all 7 tests pass, including fixture-driven `missing-provenance-record` fallback behavior.

### 3. Upstream contract compatibility

1. Run `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"`.
2. Confirm confidence/provenance pipeline tests pass.
3. **Expected:** test run passes with 0 failures, proving frontend assumptions match persisted surface contract behavior.

## Edge Cases

### Missing or malformed confidence metadata

1. Execute frontend contract tests (`node --test tests/web/confidence-visualization.test.mjs`) that include absent/short confidence arrays and missing reason arrays.
2. **Expected:** rendering helpers clamp/fallback deterministically and tooltip reasons include `missing-provenance-record` instead of failing plot construction.

## Failure Signals

- `scripts/verify/s05-local-surfaces.sh` exits non-zero with explicit stage errors (API health, import request, timeout, or JSON key validation).
- Frontend test failures in color mapping or tooltip copy (`node --test` non-zero).
- Upstream integration filter failures indicating confidence payload contract drift.

## Not Proven By This UAT

- Visual/aesthetic readability on every browser/device combination (requires manual UI spot-check in live browser session).
- Reduction of unresolved provenance rates (handled by S06 acquisition workflow, not S05 visualization).

## Notes for Tester

- If verifier fails at health stage while API logs show a different port, ensure script includes `dotnet run --no-launch-profile` behavior (included in this slice).
- Empty datasets are valid for this slice; UAT checks contract shape/fallback messaging, not non-empty point counts.
