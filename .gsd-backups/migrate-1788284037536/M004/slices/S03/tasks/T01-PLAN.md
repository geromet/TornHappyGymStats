---
estimated_steps: 4
estimated_files: 6
skills_used:
  - api-design
  - tdd
  - verify-before-complete
---

# T01: Pin the final My stats auth and privacy contract

Add a deterministic final-gate test file that reads tracked source and exercises existing test-host contracts for the My stats route/menu, `/surfaces/me`, `/import-jobs/me`, endpoint selection, safe failure classification, and secret redaction. Executor skills to load: `api-design`, `tdd`, `verify-before-complete`.

Steps:
1. Add `M004FinalGateTests` with RED-first assertions for `/my-stats` authorization/menu auth-required marking and Blazor `/me` endpoint selection.
2. Reuse or expose existing SQLite authenticated API fixture patterns without changing the production auth contract.
3. Add negative assertions for invalid claim, missing identity map, subject mismatch, body ownership tampering, invalid JSON, and secret redaction.
4. Update the menu marker only if the current UI lacks a visible auth-required indication that a test can pin.

Must-Haves:
- Tests assert behavior through public HTTP/service/static source boundaries, not private implementation details.
- Tests do not read `.gsd/` or other ignored files and do not print Torn API keys or raw private identity values.

Failure Modes (Q5): API test-host errors fail with safe status/body excerpts; auth/identity-map fixture errors assert 401/409/403 rather than enqueueing; malformed Blazor responses assert typed deserialization failures.
Load Profile (Q6): shared resources are SQLite test state and `ImportOrchestrator.Latest`; reset helpers must prevent test-state contamination.
Negative Tests (Q7): malformed auth claim, missing identity-map row, mismatched subject, ownership tampering, invalid JSON, 401/403/409 categories, and secret strings absent from failure messages.

## Inputs

- `src/HappyGymStats.Api/Controllers/SurfacesController.cs`
- `src/HappyGymStats.Api/Controllers/ImportController.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`

## Expected Output

- `tests/HappyGymStats.Tests/M004FinalGateTests.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Layout/MainLayout.razor`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~M004FinalGateTests|FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"

## Observability Impact

Signals added/changed: automated assertions make existing safe failure states and redaction guarantees inspectable in one filtered test run. Future agents inspect with the filtered `dotnet test` command. Failure state exposed: test names identify whether the break is menu auth marking, claim binding, identity setup, endpoint selection, or redaction.
