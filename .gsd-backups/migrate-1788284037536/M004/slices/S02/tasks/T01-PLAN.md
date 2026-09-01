---
estimated_steps: 41
estimated_files: 6
skills_used: []
---

# T01: Add claim-bound authenticated import API contract

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

Failure Modes (Q5):
| Dependency | On error | On timeout | On malformed response |
|------------|----------|------------|------------------------|
| Auth claims / IdentityMap DB lookup | Return existing error envelope with 401 for missing/invalid claim, 403 for Keycloak subject mismatch, and safe setup/blocking status for missing map | Honor request cancellation and do not enqueue | Treat invalid GUID claim as 401; do not fall back to request body ownership |
| ImportOrchestrator enqueue boundary | Return safe import failure/validation envelope; do not expose API key | Keep current queued/running semantics and no duplicate active import behavior | Reject empty API key before enqueue; ignore any anonymousId-shaped JSON fields |

Load Profile (Q6):
- Shared resources: identity-map DB query, existing single-slot import queue, and existing Torn import background pipeline.
- Per-operation cost: one identity-map lookup plus one enqueue; no Torn network call on the request thread.
- 10x breakpoint: existing single import slot/queue is the limiter; endpoint must fail/return current active status safely without DB ownership drift.

Negative Tests (Q7):
- Malformed inputs: missing `apiKey`, whitespace `apiKey`, invalid `anonymous_id` claim, and request JSON containing another `anonymousId`.
- Error paths: unauthenticated/missing claim, missing identity-map row, identity-map row with different `KeycloakSub`, and already-running import behavior if the existing orchestrator exposes it.
- Boundary conditions: mapped identity with no public key still queues into caller anonymousId; existing public `/api/v1/torn/import-jobs` tests still pass unchanged.

Steps:
1. Add `[HttpPost("me")]` plus `[Authorize(Roles = Roles.User)]` to `ImportController`; resolve `Claims.AnonymousId` and `ClaimTypes.NameIdentifier`, then load the identity-map row for that anonymousId.
2. Enforce ownership: invalid/missing claim is 401, missing map is a safe setup/blocking error, and `KeycloakSub` mismatch is 403; never read owner identity from `ImportRequest` or arbitrary JSON.
3. Add an `ImportOrchestrator` enqueue path/overload that accepts the caller-owned anonymousId explicitly for authenticated imports while preserving current public fresh/resume behavior.
4. Pass the identity-map public key when available so encrypted Torn player id storage continues to work for the mapped anonymousId.
5. Extend API endpoint tests with fake auth and seeded identity-map rows proving caller binding, cross-user rejection, missing-map behavior, request-body ownership tampering ignored, and unchanged public import validation.

Must-Haves:
- [ ] Authenticated `/me` import can only enqueue with the caller’s mapped anonymousId.
- [ ] Client-supplied anonymousId/player ownership fields cannot change import ownership.
- [ ] Cross-user or stale identity-map ownership is rejected before enqueue.
- [ ] Existing public import endpoint remains backward compatible.

Verification:
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"`
- Confirm new tests in `tests/HappyGymStats.Tests/ApiEndpointTests.cs` assert `ImportOrchestrator.Latest.AnonymousId` equals the caller anonymousId after `/api/v1/torn/import-jobs/me`.

Observability Impact:
- Signals added/changed: safe structured logs for authenticated import accepted/rejected outcomes should include endpoint, status/code, job id when queued, and anonymousId only where already part of the safe internal identity boundary.
- How a future agent inspects this: API test output plus `/api/v1/torn/import-jobs/latest` / `ImportOrchestrator.Latest` show queued status without exposing the API key.
- Failure state exposed: distinct 401/403/missing-map/422 outcomes make auth mapping blockers diagnosable without revealing other users’ ownership.

## Inputs

- `src/HappyGymStats.Api/Controllers/ImportController.cs`
- `src/HappyGymStats.Api/Models/ImportRequest.cs`
- `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`
- `src/HappyGymStats.Contracts/Repositories/IIdentityMapRepository.cs`
- `src/HappyGymStats.Data/Repositories/IdentityMapRepository.cs`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Expected Output

- `src/HappyGymStats.Api/Controllers/ImportController.cs`
- `src/HappyGymStats.Core/Import/ImportOrchestrator.cs`
- `src/HappyGymStats.Contracts/Repositories/IIdentityMapRepository.cs`
- `src/HappyGymStats.Data/Repositories/IdentityMapRepository.cs`
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests"

## Observability Impact

Adds safe status/code distinctions and log points for authenticated import ownership failures while keeping Torn API keys out of response bodies, logs, and test assertions.
