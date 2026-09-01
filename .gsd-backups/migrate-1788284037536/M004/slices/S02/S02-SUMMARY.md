---
id: S02
parent: M004
milestone: M004
provides:
  - Authenticated My stats import API route `/api/v1/torn/import-jobs/me` bound to caller identity-map anonymousId.
  - Blazor My stats import action/service method using only authenticated `/me` paths.
  - Deterministic API/service tests proving ownership binding, cross-user rejection, endpoint selection, safe setup blockers, and secret redaction.
requires:
  - slice: S01
    provides: Authenticated My stats read path and identity-map/claim ownership conventions consumed by the personal import path.
affects:
  - S03
key_files:
  - src/HappyGymStats.Api/Controllers/ImportController.cs
  - src/HappyGymStats.Core/Import/ImportOrchestrator.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor
  - tests/HappyGymStats.Tests/ApiEndpointTests.cs
  - tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
  - tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
  - .gsd/PROJECT.md
key_decisions:
  - Use 409/identity_setup_required-style setup blocking for missing identity-map state on authenticated import instead of falling through to queueing or exposing lookup details.
  - Keep authenticated personal import additive via `/api/v1/torn/import-jobs/me` and preserve existing public import behavior separately.
  - Move fake auth into the primary test factory configuration to keep authenticated endpoint tests on the same seeded SQLite host.
patterns_established:
  - Claim-bound personal endpoints resolve ownership from auth claims plus identity-map repository state and ignore client-supplied owner fields.
  - Blazor service methods for private My stats flows use `/me` endpoints and typed `ApiFailure` categories so UI states are safe and actionable.
  - Endpoint tests reset both database state and in-memory background orchestrator latest state to avoid cross-test contamination.
observability_surfaces:
  - API structured logs for authenticated import enqueue/rejection include safe endpoint/code/status/jobId/anonymousId metadata and avoid Torn API keys.
  - Blazor logs include endpoint/status/category for My stats import/load failures without API key values.
  - `/api/v1/torn/import-jobs/latest` and `ImportOrchestrator.Latest` remain inspection surfaces for local tests/operators.
  - My stats UI exposes queued, failed, no-identity/setup, and no-data states.
drill_down_paths:
  - .gsd/milestones/M004/slices/S02/tasks/T01-SUMMARY.md
  - .gsd/milestones/M004/slices/S02/tasks/T02-SUMMARY.md
  - .gsd/milestones/M004/slices/S02/tasks/T03-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-09T17:39:32.004Z
blocker_discovered: false
---

# S02: Authenticated My stats import ownership remediation

**My stats now has a claim-bound authenticated import path and UI action that enqueue imports only for the caller’s identity-map anonymousId, with deterministic API/service tests proving ownership rejection and safe failure behavior.**

## What Happened

S02 added the authenticated personal import contract across API, Core, Blazor, and tests. The API now exposes `POST /api/v1/torn/import-jobs/me` behind `Roles.User`; it reads the caller anonymousId and Keycloak subject from claims, verifies the identity-map row belongs to that subject, returns safe setup/blocking errors for missing identity state, returns 403 for subject mismatch, and never honors ownership fields from the request body. `ImportOrchestrator` gained an explicit owner enqueue path so the authenticated endpoint can enqueue against the caller anonymousId while preserving the existing public import flow.

The Blazor My stats page now exposes a personal import action wired through `SurfacesService.StartMyStatsImportAsync`. That service posts only to `/api/v1/torn/import-jobs/me`, sends an ownership-free payload, reloads the personal `/api/v1/torn/surfaces/me` cloud after queueing, and normalizes failures through typed `ApiFailure` categories without echoing Torn API keys. User-facing My stats failure states distinguish unauthorized, forbidden, identity setup required, validation/import failures, no data, and upstream/API unavailable conditions.

The final task stabilized the test assembly so the required filtered gates actually execute. Stale DbContext tests were reconciled with the current schema, WebApplicationFactory fake authentication was moved into primary factory configuration to share the seeded in-memory SQLite state, and `ImportOrchestrator.Latest` is reset during test database reset to prevent cross-test leakage. The resulting deterministic tests prove caller binding, body tampering rejection, missing/invalid identity behavior, endpoint selection, and redaction behavior.

## Verification

Fresh slice verification was run with `gsd_exec` in run `0e1386bd-f701-46de-86f4-1bf055195b2c` and all required checks exited 0:

- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"` passed.
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"` passed.
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"` passed.
- `rg -n "/api/v1/torn/import-jobs/me|/api/v1/torn/surfaces/me|/api/v1/torn/surfaces/latest" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor` passed and confirmed the authenticated My stats import/read routes are present in `SurfacesService`.

The combined deterministic suite includes API ownership-contract cases for invalid claims, missing identity-map state, mismatched Keycloak subject, and request-body owner tampering, plus Blazor service/UI-facing tests for `/me` endpoint selection, typed failure categories, invalid JSON/failed outcome handling, and secret redaction.

## Requirements Advanced

- R003 — Implemented claim-bound authenticated import ownership and Blazor private endpoint selection.

## Requirements Validated

- R003 — Fresh deterministic S02 verification passed all required API/service filters and endpoint scan, proving `/import-jobs/me` ownership binding/rejection paths and Blazor `/me` endpoint usage.

## New Requirements Surfaced

- None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

T03 updated `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`, which was outside the initial expected file list, because stale schema/test contracts prevented the required filtered test suites from compiling. Test-host fake authentication was moved into the primary WebApplicationFactory configuration so authenticated requests share seeded SQLite state. The test reset helper also clears `ImportOrchestrator.Latest` to prevent latest-job state leakage between tests.

## Known Limitations

Live browser/UAT, live Keycloak claim issuance, live Torn import execution, and operator identity-map gate instructions remain deferred to M004/S03. S02 proves the deterministic API/service/UI wiring contract, not production runtime behavior.

## Follow-ups

M004/S03 should run final browser or documented UAT evidence for signed-out challenge, signed-in personal cloud, `/surfaces/me` contract, safe failure states, secret non-leakage, provenance regression safety, and operator Keycloak identity-map remediation instructions.

## Files Created/Modified

- `src/HappyGymStats.Api/Controllers/ImportController.cs` — Added authenticated `/api/v1/torn/import-jobs/me` route with claim/identity-map ownership enforcement and safe rejection behavior.
- `src/HappyGymStats.Core/Import/ImportOrchestrator.cs` — Added explicit owner enqueue path while preserving existing public import queue behavior.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs` — Added My stats import service method targeting `/api/v1/torn/import-jobs/me` and kept personal stats reload on `/surfaces/me`.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs` — Expanded typed failure categories for unauthorized, forbidden, and identity setup required states.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor` — Added import UI action and safe status/failure messaging for personal imports.
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs` — Added authenticated import ownership tests and stabilized fake-auth/test-host state isolation.
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs` — Added Blazor service tests for `/me` endpoint selection, ownership-free payloads, failure classification, and redaction.
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` — Reconciled stale schema assertions so required filtered test suites compile and execute.
- `.gsd/PROJECT.md` — Updated project status with M004/S02 completion state, validated R003, verification snapshot, and remaining S03 follow-up.
