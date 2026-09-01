---
id: S02
parent: M003
milestone: M003
provides:
  - Explicit Blazor-to-API production loopback boundary for downstream smoke verification.
  - Typed, secret-safe API failure taxonomy for surfaces load and import paths.
  - Focused regression tests and verifier script proving classifier behavior.
requires:
  - slice: S01
    provides: API loopback URL/health semantics and production deploy failure taxonomy consumed by Blazor boundary selection and future smoke checks.
affects:
  - S05
  - S08
key_files:
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json
  - infra/happygymstats-blazor.service
  - docs/DEPLOYMENT.md
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
  - src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor
  - tests/HappyGymStats.Tests/SurfacesServiceFailureClassificationTests.cs
  - tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
  - tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
  - scripts/verify/s02-blazor-api-boundary.sh
  - .gsd/PROJECT.md
key_decisions:
  - Production server-side Blazor uses explicit loopback `ApiBaseUrl` (`http://127.0.0.1:5047`) and fails fast if missing.
  - Blazor surfaces/import failures propagate through typed `ApiFailure` instead of raw `EnsureSuccessStatusCode` exceptions.
  - Latest-surfaces 404 remains a null/no-data UI state, while other HTTP/network/JSON failures classify into actionable categories.
  - Home UI renders fixed category-specific messages rather than raw exception content to reduce leakage risk.
  - Regression tests use fake `HttpMessageHandler` responses for deterministic failure classification without external network dependencies.
patterns_established:
  - Required config + docs + systemd alignment for production runtime boundaries.
  - Typed failure classification object with endpoint/status/category for frontend service calls.
  - Secret-safe UI/log separation: fixed UI copy plus structured diagnostic metadata, never raw request secrets.
  - Dedicated slice verifier script for fast deterministic regression checks.
observability_surfaces:
  - Home.razor structured logs for load/import failures include endpoint, integer status code when present, and failure category.
  - Classified UI alert text distinguishes API unavailable, bad gateway, no cached data, validation/import failure, and malformed payload.
  - `scripts/verify/s02-blazor-api-boundary.sh` validates build plus Blazor API failure classification tests.
  - Production Blazor `ApiBaseUrl` contract is inspectable in appsettings, systemd env, and deployment docs.
drill_down_paths:
  - .gsd/milestones/M003/slices/S02/tasks/T01-SUMMARY.md
  - .gsd/milestones/M003/slices/S02/tasks/T02-SUMMARY.md
  - .gsd/milestones/M003/slices/S02/tasks/T03-SUMMARY.md
  - .gsd/milestones/M003/slices/S02/tasks/T04-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-06T19:41:31.154Z
blocker_discovered: false
---

# S02: Fix Blazor to API production boundary

**Server-side Blazor now calls the production API through an explicit loopback boundary and presents classified, secret-safe surfaces/import failure states instead of opaque 502 errors.**

## What Happened

S02 chose and enforced the production API boundary for the server-side Blazor host: `ApiBaseUrl` is now required at startup, production config and the systemd unit point at `http://127.0.0.1:5047`, and deployment docs explain why server-side Blazor should call loopback instead of routing through the public nginx/Cloudflare path. The slice then replaced raw `EnsureSuccessStatusCode` behavior in the Blazor surfaces path with a typed `ApiFailure` model containing category, endpoint, optional status code, and safe message. `SurfacesService.GetLatestAsync` and `StartImportAsync` now share the same classification path: latest-surfaces 404 remains the existing no-data/null state; 502 maps to bad gateway/reverse proxy failure; connection failures map to API unavailable; 400/422 import failures map to validation/import failure; and malformed JSON maps to deserialization failure. `Home.razor` now renders fixed category-specific operator guidance for both initial surfaces load and import actions, while structured logs include endpoint/status/category and deliberately exclude Torn API keys and other secrets. Regression coverage was added through fake `HttpMessageHandler` tests, plus a dedicated `scripts/verify/s02-blazor-api-boundary.sh` verifier that builds the targeted test project and runs the Blazor API failure-classification suite. During final verification the environment had many stale MSBuild nodeReuse processes causing transient CoreCLR/MSBuild OutOfMemory failures; after build-server cleanup plus stale-node process cleanup, the verifier passed with 9/9 targeted tests.

## Verification

Fresh slice-level verification was run after task completion with `bash scripts/verify/s02-blazor-api-boundary.sh` under constrained MSBuild node reuse (`MSBUILDDISABLENODEREUSE=1`) after cleaning stale MSBuild processes. The final run exited 0 and reported: build succeeded, one test assembly matched, and `BlazorApiFailureTests` passed 9/9 (`Failed: 0, Passed: 9, Skipped: 0, Total: 9`) followed by `==> S02 verify passed`. Earlier failures were environmental only: one CoreCLR creation failure and MSBuild Copy task OutOfMemory errors caused by 85 stale dotnet/MSBuild nodeReuse processes; no source changes were made after the passing verification. Task-level evidence also covered config/documentation grep for the loopback API boundary, `dotnet build`, category marker grep in `Home.razor`, and focused `SurfacesServiceFailureClassificationTests`.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

- None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

T02 added focused `SurfacesServiceFailureClassificationTests` and an aliased Blazor project reference in addition to the originally planned T04 test file; this increased verification depth without expanding runtime scope. Final verifier needed cleanup of stale MSBuild nodeReuse processes due to environment memory pressure before the passing run.

## Known Limitations

The final proof is deterministic local build/test verification plus code/config inspection. Live production browser/nginx/Cloudflare verification of Blazor Home is intentionally left for S05's full-stack smoke command. Pre-existing NU1903 package vulnerability warnings in `System.Security.Cryptography.Xml` remain unrelated to this slice.

## Follow-ups

S05 should call or mirror `scripts/verify/s02-blazor-api-boundary.sh` and add a live Blazor Home check that verifies the loopback API boundary/classified UI behavior through the deployed stack. S08 should include the server-side Blazor loopback boundary and failure taxonomy in current-state docs/API examples.

## Files Created/Modified

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs` — Requires explicit ApiBaseUrl and documents server-side loopback semantics.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json` — Sets production ApiBaseUrl to http://127.0.0.1:5047.
- `infra/happygymstats-blazor.service` — Aligns Blazor service environment with loopback API boundary.
- `docs/DEPLOYMENT.md` — Documents production Blazor loopback API boundary versus development URL usage.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs` — Adds typed API failure category/status/endpoint/safe-message model.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs` — Classifies surfaces load/import HTTP, network, JSON, and import outcome failures.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor` — Renders actionable category-specific UI errors and logs endpoint/status/category metadata.
- `tests/HappyGymStats.Tests/SurfacesServiceFailureClassificationTests.cs` — Adds focused service-level classifier and redaction tests.
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs` — Adds comprehensive fake-handler regression tests for Blazor API classification.
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` — Adds Blazor test project reference/aliasing needed for classifier tests.
- `scripts/verify/s02-blazor-api-boundary.sh` — Adds deterministic slice verification script for file presence, build, and targeted tests.
- `.gsd/PROJECT.md` — Refreshes project status to include S02 delivery and verification snapshot.
