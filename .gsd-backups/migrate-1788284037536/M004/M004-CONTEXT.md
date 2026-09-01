# M004: My stats page with auth-scoped gym cloud

**Gathered:** 2026-05-09
**Status:** Ready for planning

## Project Description

HappyGymStats is a Blazor + ASP.NET Core API application for importing Torn gym logs, reconstructing training/happy data, and visualizing gym performance. M004 adds the first private player-facing stats experience: a dedicated authenticated **My stats** page centered on the logged-in player’s own gym point cloud, backed by auth-scoped API contracts rather than the current global/all-rows surfaces cache.

## Why This Milestone

M003 hardened the production API/Blazor/deploy/auth foundation. M004 turns that groundwork into something personally useful: the first real player/app owner can sign in, import their own Torn data from the My stats page, and see a gym cloud that belongs only to them.

This milestone exists now because the project already has most of the necessary primitives — Keycloak/OIDC auth, identity-map records, claim-scoped gym-train reads, import orchestration, and point-cloud construction logic — but those primitives have not yet been integrated into a private “my data” experience.

## User-Visible Outcome

### When this milestone is complete, the user can:

- Sign in and open a dedicated **My stats** page.
- Import their own Torn data from the My stats page.
- See a personal gym point cloud scoped to their authenticated identity, not the global/all-rows cloud.
- See clear, safe messages when login, identity mapping, import, data availability, or API availability prevents the page from showing stats.

### Entry point / environment

- Entry point: Blazor route such as `/my-stats`, linked from the app navigation.
- Environment: local dev / browser, with deterministic local tests as the completion proof.
- Live dependencies involved: Keycloak/OIDC auth contract, PostgreSQL-backed identity/log data, API auth claims, Blazor UI, Torn import flow. Live Keycloak/Torn production smoke is not required to complete this milestone.

## Completion Class

- Contract complete means: deterministic tests prove the new authenticated import endpoint, new authenticated personal gym-cloud endpoint, ownership binding, API response shapes, and failure/error contracts.
- Integration complete means: Blazor My stats calls the authenticated API path, uses the logged-in identity/identity map as source of truth, imports against the caller’s scoped dataset, and renders only that caller’s cloud.
- Operational complete means: user-safe failure states and structured logs distinguish login required, forbidden/wrong user, missing identity map, no gym rows, import failure, API unavailable, and malformed payload without echoing Torn API keys or other secrets.

## Final Integrated Acceptance

To call this milestone complete, we must prove:

- A signed-in user with an identity-map anonymousId and seeded gym rows can open My stats and receive/render only their personal gym cloud.
- A signed-in user can start an import from My stats through an authenticated import endpoint that binds ownership to the caller’s identity-map anonymousId and never accepts arbitrary ownership from the client.
- A second user or mismatched identity cannot read or import into the first user’s data scope.
- Empty/error states are explicit and safe for: not signed in, missing identity map, no imported gym data, import failure, API unavailable, and malformed API payload.
- What cannot be simulated if this milestone is to be considered truly done: auth scoping and import ownership binding cannot be hand-waved with UI-only mocks; they must be proven against API/controller/service boundaries with deterministic tests. Live Torn/Keycloak production execution is intentionally deferred beyond M004’s completion bar.

## Architectural Decisions

### Dedicated authenticated My stats page

**Decision:** Add a dedicated authenticated My stats page rather than changing Home or overloading Player account.

**Rationale:** Home currently serves the public/global import + global gym cloud experience, and Player account currently serves claim diagnostics. A dedicated My stats route keeps the private player experience clear and avoids mixing public/global behavior with auth-scoped personal data.

**Alternatives Considered:**
- Upgrade Player account — faster insertion point, but the route and current content are identity-diagnostic rather than stats-focused.
- Adaptive Home page — one URL for anonymous and authenticated modes, but it risks confusing public/global data with private user data.

---

### Identity map is the ownership source

**Decision:** Use the existing identity map / Keycloak-sub-to-anonymousId mapping as the source of truth for deciding which Torn/gym data belongs to the logged-in user.

**Rationale:** The codebase already has `IdentityController`, `IdentityMapRepository`, Keycloak auth, `Claims.AnonymousId`, and scoped gym-train reads. Reusing that ownership model keeps M004 aligned with existing auth groundwork and avoids inventing another account/data-linking scheme.

**Alternatives Considered:**
- Claim provisional import as the main My stats path — reuses existing anonymous import pieces but creates an awkward two-step flow for an already logged-in user.
- Fresh ownership supplied by the client — rejected because the client must never be able to choose arbitrary ownership for personal stats.

---

### Authenticated import endpoint for My stats

**Decision:** Add a new authenticated import endpoint for My stats that binds the import run to the caller’s identity-map anonymousId.

**Rationale:** The user chose “Import from My stats” and “Use identity map.” The cleanest way to satisfy both is a `[Authorize]` endpoint that reads caller identity from claims/identity map, accepts the Torn API key, and enqueues the import into the caller’s data scope without accepting an arbitrary anonymousId from the client.

**Alternatives Considered:**
- Anonymous import then claim — functional but awkward for the main logged-in flow.
- Reuse the global public import endpoint — lower backend churn, but weak ownership semantics and too much risk of global/latest state leaking into a personal feature.

---

### New authenticated my-cloud API contract

**Decision:** Add a new authenticated personal gym-cloud API contract, such as `/api/v1/torn/my/gym-cloud`, that returns only the caller’s point cloud arrays and metadata.

**Rationale:** The current `/api/v1/torn/surfaces/latest` endpoint serves a global file-cache surface and strips detailed fields for the current Blazor DTO. My stats needs a private data boundary where the API derives the caller’s anonymousId from auth context and returns only that user’s cloud. A “my” endpoint avoids route-level ownership input and makes privacy behavior easier to test.

**Alternatives Considered:**
- Extend `/api/v1/torn/gym-trains/{anonymousId}` — reuses existing authorization but mixes paged list semantics with whole-cloud semantics.
- Per-user surfaces cache — consistent with existing global surfaces architecture, but introduces cache invalidation, path/privacy, and operational complexity that is too large for the thin M004 slice.

---

### Local contract proof is enough for M004

**Decision:** M004 is complete when local deterministic tests prove the contracts, integration, and failure behavior; live Keycloak/Torn production smoke is not required.

**Rationale:** The milestone’s main risk is architecture and privacy correctness, not whether external services are reachable during planning. Local fixtures and fake-auth tests can prove ownership binding, isolation, API shape, and UI states reliably.

**Alternatives Considered:**
- Production-like smoke — useful later, but unnecessarily expands M004 beyond its thin personal slice.
- Real Torn import plus stats as the only completion gate — highest confidence but would make live external service availability part of this milestone’s definition of done.

## Error Handling Strategy

My stats should use explicit, user-safe failure states and structured operator/agent logs.

- Not signed in: show login prompt / redirect behavior; do not call private stats endpoints as anonymous.
- Forbidden or wrong user: API returns 403 without revealing whether another user’s data exists; UI shows a generic access-denied message.
- Missing identity map: UI explains that no player data is connected yet and offers the My stats import path if that is the intended bootstrap path.
- No imported gym data: show an empty personal cloud state with a clear import CTA.
- Import failure: show a safe failure message; do not echo the Torn API key or raw secret-bearing request details.
- API unavailable / reverse proxy / malformed payload: reuse or extend the typed `ApiFailure` classification style established in M003 so UI messages and logs remain distinguishable.
- Logging: include endpoint, status code, category, and safe identity/import identifiers where useful; never log Torn API keys.
- Retry policy: M004 does not need a new UI retry framework. Import should use existing backend Torn retry/backoff behavior; the UI can allow the user to retry after a failed import.

## Risks and Unknowns

- Exact my-cloud DTO shape — needs planning: x/y/z arrays are required, but metadata, labels, confidence, and provenance warning fields need a stable additive contract.
- Identity-map bootstrap behavior — decide whether authenticated import creates an identity map when missing, requires an existing mapping, or introduces a small mapping bootstrap step.
- Point-cloud source — current `SurfaceSeriesBuilder` can build gym cloud arrays from `GymLogEntry` rows, but repository/service support is currently global for cloud-style reads and per-user for paged gym-train reads.
- Privacy regression — accidental use of global `surfaces/latest` or global import latest state would undermine the milestone.
- UI/server auth bridging — Blazor server-side auth and API bearer/cookie forwarding must be handled deliberately so My stats calls authenticate correctly in local tests and production-like configuration.

## Existing Codebase / Prior Art

- `src/HappyGymStats.Api/Controllers/IdentityController.cs` — existing authenticated identity map endpoints, including `/api/v1/identity/me`, public-key storage, and provisional claim flow.
- `src/HappyGymStats.Api/Controllers/GymTrainsController.cs` — existing `GET /api/v1/torn/gym-trains/{anonymousId}` endpoint checks `Claims.AnonymousId` against route ownership.
- `src/HappyGymStats.Data/Repositories/IdentityMapRepository.cs` — existing persistence layer for Keycloak-sub-to-anonymousId mapping and provisional claims.
- `src/HappyGymStats.Data/Repositories/UserLogEntryRepository.cs` — existing per-user gym-train paging plus global gym log reads used for surface building.
- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs` — existing gym point-cloud construction from gym log rows, including confidence/reason metadata.
- `src/HappyGymStats.Api/Controllers/SurfacesController.cs` — current global surfaces endpoint; useful prior art but not the correct privacy boundary for My stats.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs` — existing Blazor OIDC/cookie auth, cascading authentication state, and API base URL configuration.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor` — current public/global import + global gym cloud UI; should not be treated as the My stats page.
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/PlayerAccount.razor` — existing authenticated claim diagnostics page; useful for auth patterns but not the desired My stats entrypoint.
- `tests/HappyGymStats.Tests/ApiEndpointTests.cs` and `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs` — prior deterministic local testing patterns for API contracts and Blazor failure classification.

## Relevant Requirements

- R001 — M004 must preserve the validated deterministic gym/provenance surface behavior while introducing a private personal cloud surface.
- R002 — M004 should not regress the modifier provenance confidence contract; if confidence/reason metadata appears in the personal cloud, it must remain additive and deterministic.
- New requirement to record during planning: authenticated users must only access/import/render their own gym stats through identity-map scoped APIs.

## Scope

### In Scope

- Dedicated authenticated My stats route and navigation entry.
- New authenticated My stats import endpoint bound to the caller’s identity-map anonymousId.
- New authenticated personal gym-cloud endpoint, likely under a `my` route, that derives ownership from auth context.
- Repository/service support for building a per-user gym cloud from the caller’s gym rows.
- Blazor service/model additions for the personal gym-cloud contract.
- My stats loading, empty, import-in-progress/import-failed, API-failed, malformed-payload, and no-data states.
- Deterministic local tests proving auth scoping, data isolation, contract shape, import ownership binding, and UI/service failure classification.

### Out of Scope / Non-Goals

- Sharing stats with other users.
- Admin views or operator dashboards.
- Global-vs-personal comparison.
- Advanced analytics, recommendations, or training optimization advice.
- Production live Keycloak/Torn smoke as a completion requirement.
- Replacing the existing public/global Home surface in this milestone.
- Per-user surfaces file-cache infrastructure unless planning discovers the direct endpoint is not viable.

## Technical Constraints

- The client must never supply arbitrary ownership for My stats import or cloud reads.
- The personal cloud endpoint must derive user scope from authenticated claims / identity map.
- No Torn API keys or secrets may be echoed to UI logs, server logs, browser console, or test output.
- The current global `/api/v1/torn/surfaces/latest` cache must not be reused as the My stats privacy boundary.
- Tests must be deterministic and local; they should not require live Torn or live Keycloak.
- API changes should be additive where possible to avoid breaking existing public/global endpoints.
- Blazor API failure handling should remain typed/classified, consistent with M003 patterns.

## Integration Points

- Keycloak/OIDC auth — supplies logged-in identity and role/claim context.
- Identity map — maps the authenticated user to the anonymousId that owns imported Torn/gym data.
- Import orchestrator / Torn import flow — imports data from a Torn API key into the caller’s scoped dataset.
- User log repository / gym rows — source data for personal gym-cloud generation.
- SurfaceSeriesBuilder — prior point-cloud construction logic that can likely be reused for per-user cloud projection.
- Blazor My stats UI — calls authenticated API, renders cloud, and presents explicit safe failure states.
- Tests/WebApplicationFactory/fake auth — local proof surface for auth scoping and isolation.

## Testing Requirements

Testing must be local and deterministic.

- API/controller tests:
  - authenticated user can fetch only their own personal gym cloud;
  - unauthenticated user receives 401/appropriate auth behavior;
  - mismatched/forbidden identity cannot fetch another user’s data;
  - missing identity map and no gym rows return distinct, documented responses;
  - authenticated import endpoint binds to caller identity and does not accept client-supplied anonymousId;
  - import failure returns a safe structured error without secret leakage.
- Service/repository tests:
  - per-user gym-cloud projection includes only rows for the requested/caller anonymousId;
  - global rows from other users do not affect personal cloud output;
  - empty row sets produce the expected empty/no-data contract.
- Blazor/service tests:
  - My stats service classifies 401/403/not-found/no-data/API-unavailable/malformed-payload distinctly;
  - page/component behavior shows the correct empty/error/import states where practical within existing test approach.
- Regression tests:
  - existing global surfaces and gym-train endpoints remain compatible unless intentionally changed by a later plan.

## Acceptance Criteria

- A dedicated My stats page exists and requires authentication.
- My stats has an import action that uses a new authenticated import API path bound to the caller’s identity-map anonymousId.
- The API exposes a new authenticated personal gym-cloud contract that does not require the client to pass an anonymousId.
- The personal cloud contains only the logged-in user’s gym points.
- Tests prove that a second user’s rows are excluded and cannot be accessed through My stats contracts.
- My stats does not use the global `surfaces/latest` endpoint as its privacy boundary.
- Missing identity map, no gym data, import failure, API unavailable, and malformed payload have explicit safe UI/service handling.
- No Torn API key or secret appears in logs, UI messages, browser console assertions, or test output.
- The milestone remains a thin personal slice: no sharing, admin views, global-vs-personal comparison, or advanced analytics.

## Open Questions

- What exact fields should the new my-cloud DTO expose beyond x/y/z arrays — safe labels, count, latest timestamp, confidence, confidence reasons, provenance warnings, or only minimal metadata?
- Should authenticated import create an identity-map entry if one is missing, or should missing identity map block the import with an explicit setup/claim state?
- Should personal cloud include confidence/provenance metadata immediately, or should M004 ship x/y/z first and leave richer interpretability to a follow-up?
- Should the route name be `/api/v1/torn/my/gym-cloud`, `/api/v1/me/gym-cloud`, or another convention aligned with existing API naming?
