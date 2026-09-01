# S02: Authenticated My stats import ownership remediation

**Goal:** Add the authenticated My stats import path and UI action so Torn imports are enqueued into the caller’s identity-map anonymousId, client-supplied ownership cannot redirect imports, and deterministic API/Blazor service tests prove cross-user rejection and safe failure behavior.
**Demo:** After this: My stats exposes an authenticated import action/API path that binds Torn imports to the caller’s identity-map anonymousId, rejects cross-user ownership, and has deterministic API/service tests proving the contract.

## Must-Haves

- `POST /api/v1/torn/import-jobs/me` exists, requires `Roles.User`, resolves ownership from `Claims.AnonymousId` and `IdentityMap`, and never accepts an owning anonymousId from the request body.
- Missing/invalid auth claims return 401, missing identity-map state returns a safe setup/blocking error, and identity-map records owned by another Keycloak subject return 403 without revealing other users’ data.
- Authenticated import enqueueing uses the caller’s anonymousId in `ImportOrchestrator` and preserves existing public import behavior.
- Blazor My stats exposes an import form/action that calls only the authenticated `/me` import endpoint, reloads personal stats after queueing, and keeps Torn API keys out of UI/log/test failure messages.
- Deterministic tests in `tests/HappyGymStats.Tests/ApiEndpointTests.cs` and `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs` prove the ownership contract, endpoint selection, negative cases, and secret-redaction behavior.
- Threat Surface (Q3): abuse risk is request-body ownership tampering, replayed/invalid auth claims, and privilege escalation into another anonymousId; data exposure risk is personal gym rows and encrypted Torn player id metadata, never Torn API keys; input trust boundary is user-supplied API key/public key JSON crossing API, DB, and background import queue.
- Requirement Impact (Q4): R003 is owned and must be proven by API/service tests; R001/R002 are regression constraints because personal import must remain additive and not alter deterministic provenance/confidence behavior; D004 is the governing endpoint/ownership decision.

## Proof Level

- This slice proves: Contract plus local integration across API controller, identity-map repository, import orchestrator enqueue boundary, Blazor service, and My stats UI wiring. No live Torn or Keycloak is required; deterministic WebApplicationFactory/fake-auth and stub HTTP tests are sufficient. Human/UAT is deferred to S03.

## Integration Closure

Upstream surfaces consumed: `src/HappyGymStats.Api/Controllers/ImportController.cs`, `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`, `src/HappyGymStats.Data/Repositories/IdentityMapRepository.cs`, `src/HappyGymStats.Identity/Authentication/Claims.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`, and `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`.

New wiring introduced in this slice: authenticated API route `/api/v1/torn/import-jobs/me`; orchestrator enqueue overload/path for an explicit owner anonymousId; Blazor My stats import action using the authenticated route.

What remains before the milestone is truly usable end-to-end: S03 must run final build/test/browser/UAT evidence against signed-out challenge, signed-in personal cloud, operator Keycloak identity-map instructions, and full milestone regression set.

## Verification

- Objective stopping condition / verification:
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"`
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"`
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"`
- `rg -n "/api/v1/torn/import-jobs/me|/api/v1/torn/surfaces/me|/api/v1/torn/surfaces/latest" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- Observability / diagnostics:
- Runtime signals: API logs should identify authenticated import enqueue attempts by safe job id/status/anonymousId and reason-coded ownership failures; Blazor logs should include endpoint/status/category without Torn API keys.
- Inspection surfaces: `/api/v1/torn/import-jobs/latest` and in-process `ImportOrchestrator.Latest` remain available for local tests/operators; My stats UI shows queued/failed/no-identity/no-data states.
- Failure visibility: ownership failures are distinct status/code paths (401 invalid claim, 403 identity mismatch, 409/404 missing map, 422 validation) with request ids from the existing error envelope.
- Redaction constraints: never log, echo, serialize, or assert Torn API key values; do not expose another user’s anonymousId in error bodies.

## Tasks

- [x] **T01: Add claim-bound authenticated import API contract** `est:2h`
  ---
  estimated_steps: 5
  estimated_files: 6
  skills_used:
    - api-design
    - tdd
    - security-review
    - verify-before-complete
  ---
  Implement the additive authenticated import endpoint that binds every My stats import to the caller’s identity-map anonymousId and rejects cross-user ownership before Torn import work is queued.
  - Files: `src/HappyGymStats.Api/Controllers/ImportController.cs`, `src/HappyGymStats.Api/Models/ImportRequest.cs`, `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`, `src/HappyGymStats.Contracts/Repositories/IIdentityMapRepository.cs`, `src/HappyGymStats.Data/Repositories/IdentityMapRepository.cs`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"

- [x] **T02: Wire My stats import UI to authenticated service path** `est:2h`
  ---
  estimated_steps: 5
  estimated_files: 5
  skills_used:
    - react-best-practices
    - tdd
    - security-review
    - verify-before-complete
  ---
  Add the real My stats import action and Blazor service method that posts to the new authenticated import endpoint, displays safe queue/failure states, and reloads the personal cloud after queueing.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`, `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"

- [x] **T03: Polish My stats ownership failure states and regression proof** `est:1h`
  ---
  estimated_steps: 4
  estimated_files: 5
  skills_used:
    - test
    - verify-before-complete
  ---
  Polish and verify the user-facing My stats ownership failure states so identity-map blockers are visible and safe, then run the combined deterministic regression proof for the slice.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`, `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"

## Files Likely Touched

- src/HappyGymStats.Api/Controllers/ImportController.cs
- src/HappyGymStats.Api/Models/ImportRequest.cs
- src/HappyGymStats.Core/Import/ImportOrchestrator.cs
- src/HappyGymStats.Contracts/Repositories/IIdentityMapRepository.cs
- src/HappyGymStats.Data/Repositories/IdentityMapRepository.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor
- tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
