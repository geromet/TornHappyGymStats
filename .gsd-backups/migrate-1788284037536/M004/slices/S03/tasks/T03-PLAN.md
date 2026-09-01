---
estimated_steps: 4
estimated_files: 5
skills_used:
  - verify-before-complete
  - tdd
  - write-docs
---

# T03: Compose the single M004 final verification gate

Add one executable final gate script that operators and future agents can run to prove M004 local closure: build/test the final auth contract, scan endpoint wiring, run docs checks, and include provenance-warning regression safety. Executor skills to load: `verify-before-complete`, `tdd`, `write-docs`.

Steps:
1. Create `scripts/verify/m004-my-stats-final-gate.sh` with strict shell settings and labeled sections.
2. Compose scoped build/test, `M004FinalGateTests`, existing S02 API/Blazor tests, docs verifier, endpoint/auth static scans, and `scripts/verify/s06-provenance-warnings.sh`.
3. Ensure output never echoes Torn API keys, Keycloak tokens, or raw private player identity values.
4. Link the command from `README.md` if it is not discoverable after T02.

Must-Haves:
- A single command gives pass/fail evidence for the full M004 local final gate.
- The script exits non-zero on any missing endpoint marker, auth marker, docs marker, redaction test, or provenance regression failure.

Failure Modes (Q5): failing build/test/static/doc/provenance sections exit non-zero with the section label and first failing command; timeouts are not swallowed.
Load Profile (Q6): shared resources are local CPU/disk, NuGet cache, SQLite tests, and temp surfaces/provenance artifacts; keep filters scoped so the final gate remains practical.
Negative Tests (Q7): missing `/me` endpoints, missing `[Authorize]`, missing menu marker, missing operator doc markers, redaction failures, provenance verifier failure, and filtered test failures.

## Inputs

- `tests/HappyGymStats.Tests/M004FinalGateTests.cs`
- `scripts/verify/s06-provenance-warnings.sh`
- `scripts/verify/s08-docs-contract.sh`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `docs/M004-MY-STATS-OPERATOR-GATE.md`

## Expected Output

- `scripts/verify/m004-my-stats-final-gate.sh`
- `README.md`

## Verification

bash scripts/verify/m004-my-stats-final-gate.sh

## Observability Impact

Signals added/changed: a single command labels which M004 closure class failed: build, auth contract tests, Blazor endpoint scan, docs/operator gate, secret redaction, or provenance regression. Future agents inspect by running the script and reading its labeled sections. Failure state exposed: the first failing command and section label without printing secrets.
