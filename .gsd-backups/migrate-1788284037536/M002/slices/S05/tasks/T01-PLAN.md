---
estimated_steps: 48
estimated_files: 5
skills_used: []
---

# T01: Add local surfaces publication/verification pipeline for S05

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

## Inputs

- ``src/HappyGymStats.Api/Program.cs``
- ``scripts/verify/build-and-test.sh``
- ``README.md``

## Expected Output

- ``scripts/verify/s05-local-surfaces.sh``
- ``web/data/surfaces/meta.json``
- ``web/data/surfaces/latest.json``
- ``README.md``

## Verification

bash scripts/verify/s05-local-surfaces.sh

## Observability Impact

Adds deterministic local precondition check so missing surfaces artifacts are diagnosed before UI test execution.
