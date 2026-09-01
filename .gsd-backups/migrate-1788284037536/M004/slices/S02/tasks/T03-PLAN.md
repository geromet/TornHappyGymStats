---
estimated_steps: 38
estimated_files: 5
skills_used: []
---

# T03: Polish My stats ownership failure states and regression proof

---
estimated_steps: 4
estimated_files: 5
skills_used:
  - test
  - verify-before-complete
---
Polish and verify the user-facing My stats ownership failure states so identity-map blockers are visible and safe, then run the combined deterministic regression proof for the slice.

Failure Modes (Q5):
| Dependency | On error | On timeout | On malformed response |
|------------|----------|------------|------------------------|
| API ownership/failure responses | Show setup/access-denied/import-failed messages without leaking another user’s data or Torn API key | Preserve visible retry/import affordance and log endpoint/status/category | Treat invalid payload as a contract failure and show malformed response copy |
| dotnet test runner / WebApplicationFactory | Capture failing test names and fix ownership/UI wiring instead of weakening assertions | Re-run with narrower filters to localize API vs Blazor path | Treat JSON/deserialization failures as contract regressions |

Load Profile (Q6):
- Shared resources: in-memory SQLite test host, Blazor stub HttpClient tests, and user-visible My stats state.
- Per-operation cost: local test execution plus static endpoint scan.
- 10x breakpoint: test duration, not runtime system load; keep filters targeted but include enough regression coverage.

Negative Tests (Q7):
- Malformed inputs: verify test coverage includes invalid claim, whitespace API key, ownership tampering, and invalid JSON.
- Error paths: verify user-safe handling for 401, 403, missing map/setup blocker, validation, failed import status, API unavailable/502, and personal cloud reload failures.
- Boundary conditions: verify public import endpoint and personal surfaces endpoint still pass their existing tests.

Steps:
1. Review My stats messages added in T02 and ensure setup blocker, access denied, import failed, API unavailable, no-data, and malformed-response states are distinct and non-secret-bearing.
2. Add or adjust Blazor service tests to assert the final message/category mapping for setup/access-denied/import failure states introduced by the authenticated import path.
3. Run the combined M004-focused test filter covering `SqliteApiEndpointTests`, `BlazorApiFailureTests`, and `SurfacesServiceFailureClassificationTests`; fix regressions without weakening ownership or redaction assertions.
4. Perform static checks with `rg` to ensure My stats service uses `/api/v1/torn/import-jobs/me`, reads private stats from `/api/v1/torn/surfaces/me`, and does not route private imports through the public import endpoint or private reads through `/surfaces/latest`.

Must-Haves:
- [ ] My stats displays distinct, safe ownership/setup/access/import failure states.
- [ ] API and Blazor tests prove claim-bound enqueue, cross-user rejection, authenticated endpoint selection, and no ownership fields in request body.
- [ ] Combined regression command passes without weakening existing public/global endpoint behavior.
- [ ] Static endpoint scan confirms private My stats paths stay on `/me` and `/surfaces/me`.

Verification:
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"`
- `rg -n "/api/v1/torn/import-jobs/me|/api/v1/torn/surfaces/me|/api/v1/torn/surfaces/latest" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`

Observability Impact:
- Signals added/changed: verifies and tightens visible ownership/failure states rather than adding a new backend signal.
- How a future agent inspects this: final test command and endpoint scan identify whether the break is API ownership, Blazor endpoint selection, user-facing state mapping, or error classification.
- Failure state exposed: setup/access denied/import/API/malformed states are distinguishable to the user and to logs without exposing secrets.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Expected Output

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"

## Observability Impact

Tightens visible My stats failure-state diagnostics and verifies endpoint-selection/redaction proof without adding new backend runtime surfaces.
