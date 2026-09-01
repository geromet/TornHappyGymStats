---
estimated_steps: 41
estimated_files: 5
skills_used: []
---

# T02: Wire My stats import UI to authenticated service path

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

Failure Modes (Q5):
| Dependency | On error | On timeout | On malformed response |
|------------|----------|------------|------------------------|
| Authenticated import API | Map 401/403/missing-map/422/5xx to typed `ApiFailure` categories and safe user text | Surface API unavailable/timeout using existing category pattern; leave API key out of messages | Throw deserialization failure with endpoint `/api/v1/torn/import-jobs/me` and no secret leakage |
| My stats personal cloud reload | Keep existing no-data/API-failure states and do not claim import success | Leave queued/importing message visible until reload completes or fails | Use existing `GetMyStatsAsync` deserialization path |

Load Profile (Q6):
- Shared resources: Blazor server circuit state, API HttpClient, and existing import queue.
- Per-operation cost: one POST to enqueue plus one personal cloud reload after queueing; no polling loop required in this slice.
- 10x breakpoint: repeated clicks can duplicate enqueue attempts; UI should disable the import button while `_importing` is true.

Negative Tests (Q7):
- Malformed inputs: empty/whitespace API key is blocked client-side or classified as validation without calling arbitrary ownership fields.
- Error paths: 401, 403, 409/404 setup blocker, 422 validation, 502/API unavailable, failed import status, and invalid JSON all produce safe messages.
- Boundary conditions: successful 202/200 status clears the secret-bearing field and reloads My stats without switching back to global `/surfaces/latest`.

Steps:
1. Add `StartMyStatsImportAsync` to `SurfacesService` using endpoint `/api/v1/torn/import-jobs/me` and payload `{ apiKey }` or `{ apiKey, fresh = true }` only; do not send anonymousId/playerId/owner fields.
2. Extend `ApiFailure` classification only if needed to distinguish forbidden and identity setup blockers while preserving existing endpoint/status/category fields.
3. Add an import form/card to `MyStats.razor` with API-key input, import button, disabled/importing state, safe status messages, and automatic `LoadSurfacesAsync` refresh after queueing.
4. Keep `GetMyStatsAsync` as the personal cloud read path and ensure My stats never calls `/api/v1/torn/surfaces/latest` for private data.
5. Extend `BlazorApiFailureTests` with stub handler assertions that the service posts to `/api/v1/torn/import-jobs/me`, never includes anonymousId, handles failed status/invalid JSON/401/403/validation without leaking the provided API key, and preserves existing global import tests if retained.

Must-Haves:
- [ ] My stats has a visible authenticated import action bound to the `/me` import service method.
- [ ] The service request body contains the Torn API key and import intent only, not ownership fields.
- [ ] UI and service failure messages are typed, actionable, and secret-redacted.
- [ ] Personal stats reload uses `GetMyStatsAsync` after queueing and never the global surface endpoint.

Verification:
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"`
- `rg -n "StartMyStatsImportAsync|/api/v1/torn/import-jobs/me|my-gym-cloud-chart" src/HappyGymStats.Blazor tests/HappyGymStats.Tests`

Observability Impact:
- Signals added/changed: My stats logs import failures with endpoint/status/category only and exposes visible queued/failed/no-identity/no-data states.
- How a future agent inspects this: `BlazorApiFailureTests` verifies endpoint selection and secret redaction; browser/UAT in S03 can inspect the visible import state.
- Failure state exposed: setup/auth/API/deserialization/import-failed states become distinguishable in My stats instead of collapsing into a generic load failure.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`
- `src/HappyGymStats.Api/Controllers/ImportController.cs`

## Expected Output

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/ApiFailure.cs`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/MyStats.razor`
- `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~BlazorApiFailureTests"

## Observability Impact

Adds user-visible My stats import state and safe endpoint/status/category logging for API/service failures without exposing Torn API keys.
