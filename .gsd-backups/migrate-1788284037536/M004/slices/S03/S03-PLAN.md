# S03: M004 verification, UAT, and operator gate closure

**Goal:** Close M004 with a tracked final gate that proves the authenticated My stats experience is still claim-bound, safe on failure, documented for operators, and ready for UAT/manual Keycloak remediation when live identity mapping is unavailable.
**Demo:** After this: fresh build/test/browser or documented UAT evidence proves /my-stats signed-out challenge, signed-in personal cloud, /surfaces/me contract, safe failure states, no secret leakage, provenance regression safety, and operator Keycloak identity-map gate instructions.

## Must-Haves

- My stats menu visibility and auth-required routing are pinned by automated checks.
- `/api/v1/torn/surfaces/me` and `/api/v1/torn/import-jobs/me` contracts are re-verified through deterministic tests and static endpoint scans.
- Secret redaction, safe failure classification, missing identity-map setup blockers, and cross-user ownership rejection remain covered in the final gate.
- Provenance-warning regression safety remains included in the milestone gate so private My stats changes do not mask existing public surface diagnostics.
- Operator-facing Keycloak/identity-map gate instructions and a UAT evidence checklist exist in tracked docs and are linked from setup/README surfaces.
- The final verification command is a single executable script that composes build, tests, docs checks, endpoint scans, and provenance checks without needing untracked `.gsd/` artifacts or production secrets.

## Proof Level

- This slice proves: Final-assembly proof. This slice proves deterministic local build/test/docs/static contract closure and provides a documented UAT/operator gate for live Keycloak/manual remediation. Real runtime/browser UAT should be captured when credentials and a live Keycloak identity-map account are available; the automated gate must not overclaim production auth success without that evidence.

## Integration Closure

Upstream surfaces consumed: `src/HappyGymStats.Api/Controllers/SurfacesController.cs`, `src/HappyGymStats.Api/Controllers/ImportController.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`, S02 deterministic API/service tests, and existing provenance/doc verification scripts. New wiring introduced in this slice: an additive final M004 verification script and tracked operator/UAT docs. Nothing remains for M004 local final assembly after the script passes; live production UAT remains an operator-executed evidence step unless credentials are available during execution.

## Verification

- Objective stopping condition: `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~M004FinalGateTests|FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"` passes; `bash scripts/verify/s08-docs-contract.sh` passes; `bash scripts/verify/m004-my-stats-final-gate.sh` passes; the final gate includes static scans for `/my-stats` auth/menu marking plus `/api/v1/torn/surfaces/me` and `/api/v1/torn/import-jobs/me`; and the gate includes provenance regression safety via `scripts/verify/s06-provenance-warnings.sh` without requiring untracked `.gsd/` artifacts or production secrets.

## Tasks

- [x] **T01: Pin the final My stats auth and privacy contract** `est:1h30m`
  Add a deterministic final-gate test file that reads tracked source and exercises existing test-host contracts for the My stats route/menu, `/surfaces/me`, `/import-jobs/me`, endpoint selection, safe failure classification, and secret redaction. Executor skills to load: `api-design`, `tdd`, `verify-before-complete`.
  - Files: `tests/HappyGymStats.Tests/M004FinalGateTests.cs`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`, `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~M004FinalGateTests|FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"

- [x] **T02: Publish operator Keycloak gate and UAT evidence docs** `est:1h`
  Write cold-reader documentation for operators who must validate or repair Keycloak/identity-map readiness before approving My stats, including safe manual remediation steps and a UAT evidence checklist that does not require storing secrets in git. Executor skills to load: `write-docs`, `api-design`, `verify-before-complete`.
  - Files: `docs/M004-MY-STATS-OPERATOR-GATE.md`, `docs/SETUP.md`, `README.md`, `scripts/verify/s08-docs-contract.sh`
  - Verify: bash scripts/verify/s08-docs-contract.sh && test -s docs/M004-MY-STATS-OPERATOR-GATE.md && rg -n "signed-out|identity_setup_required|/api/v1/torn/surfaces/me|/api/v1/torn/import-jobs/me|Torn API key|Keycloak" docs/M004-MY-STATS-OPERATOR-GATE.md

- [x] **T03: Compose the single M004 final verification gate** `est:1h`
  Add one executable final gate script that operators and future agents can run to prove M004 local closure: build/test the final auth contract, scan endpoint wiring, run docs checks, and include provenance-warning regression safety. Executor skills to load: `verify-before-complete`, `tdd`, `write-docs`.
  - Files: `scripts/verify/m004-my-stats-final-gate.sh`, `scripts/verify/s06-provenance-warnings.sh`, `scripts/verify/s08-docs-contract.sh`, `tests/HappyGymStats.Tests/M004FinalGateTests.cs`, `docs/M004-MY-STATS-OPERATOR-GATE.md`
  - Verify: bash scripts/verify/m004-my-stats-final-gate.sh

## Files Likely Touched

- tests/HappyGymStats.Tests/M004FinalGateTests.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
- docs/M004-MY-STATS-OPERATOR-GATE.md
- docs/SETUP.md
- README.md
- scripts/verify/s08-docs-contract.sh
- scripts/verify/m004-my-stats-final-gate.sh
- scripts/verify/s06-provenance-warnings.sh
