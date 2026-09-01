# S02: Authenticated My stats import ownership remediation — UAT

**Milestone:** M004
**Written:** 2026-05-09T17:39:32.004Z

# S02: Authenticated My stats import ownership remediation — UAT

**Milestone:** M004
**Written:** 2026-05-09

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice’s acceptance contract is primarily API/service ownership behavior and Blazor endpoint selection. The deterministic WebApplicationFactory and Blazor service tests exercise the claim-bound route, identity-map rejection paths, request body tampering resistance, and redacted failure handling without requiring live Torn or Keycloak. Human/browser runtime acceptance remains explicitly deferred to S03.

## Preconditions

- Repository dependencies restore successfully in the local .NET environment.
- Test project `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj` can compile.
- Fake authenticated test clients can supply caller anonymousId and Keycloak subject headers/claims.
- No live Torn API key or live Keycloak realm is required.

## Smoke Test

Run:

```bash
dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"
```

**Expected:** Command exits 0. The combined deterministic suite passes API and Blazor service ownership/failure tests.

## Test Cases

### 1. Authenticated import binds to caller identity map

1. Seed an identity-map row for caller anonymousId and Keycloak subject.
2. POST to `/api/v1/torn/import-jobs/me` with fake authenticated `Roles.User` claims.
3. Include only Torn import inputs in the body; do not provide any owner field.
4. **Expected:** Request is accepted/enqueued, and `ImportOrchestrator.Latest.AnonymousId` equals the caller anonymousId from the identity map.

### 2. Request body ownership tampering is ignored

1. Seed the caller identity-map row.
2. POST to `/api/v1/torn/import-jobs/me` with a body that attempts to include another owner/anonymousId/playerId-style field.
3. **Expected:** The import still enqueues for the authenticated caller anonymousId, never the supplied body owner.

### 3. Missing or invalid auth claims fail safely

1. POST to `/api/v1/torn/import-jobs/me` without the required authenticated caller claim state.
2. **Expected:** API returns 401. No import is queued and no other user data or anonymousId is revealed.

### 4. Missing identity map produces setup blocker

1. Authenticate as a user whose anonymousId claim does not resolve to an identity-map row.
2. POST to `/api/v1/torn/import-jobs/me`.
3. **Expected:** API returns a safe setup/blocking error (`identity_setup_required`, represented by the implemented 409/404 classification paths). No import is queued, and the response does not reveal another user’s identity state.

### 5. Cross-subject identity-map mismatch is rejected

1. Seed an identity-map row for an anonymousId owned by Keycloak subject A.
2. Authenticate as subject B while presenting that anonymousId.
3. POST to `/api/v1/torn/import-jobs/me`.
4. **Expected:** API returns 403. No import is queued, and the response body does not disclose subject A’s private data.

### 6. Blazor My stats uses only private `/me` endpoints

1. Exercise `SurfacesService.StartMyStatsImportAsync` in tests with a capturing HTTP handler.
2. Inspect request path and JSON body.
3. **Expected:** Request path is `/api/v1/torn/import-jobs/me`; body includes the Torn key/fresh import inputs only and excludes anonymousId/playerId/owner fields.
4. Exercise My stats reload path.
5. **Expected:** Personal stats reload uses `/api/v1/torn/surfaces/me`, not `/api/v1/torn/surfaces/latest`.

### 7. Secrets are redacted from failure handling

1. Configure the service/API test to return validation, unauthorized, forbidden, identity setup, bad gateway, invalid JSON, or failed import outcomes.
2. Use a sentinel Torn API key value in the request input.
3. **Expected:** Failure classification is typed and actionable, but logs/exceptions/test-visible messages do not contain the sentinel key.

## Edge Cases

- 401 invalid/missing claims: no queueing and no private identity leakage.
- 403 subject mismatch: no queueing and no target owner details in response.
- 404/409 missing identity-map state: classified as identity setup required so UI can block safely.
- 422 validation/import failure: surfaced as typed import/validation failure with the Torn key redacted.
- 502/API unavailable/malformed JSON: classified as service/API failure rather than exposing raw payloads or secrets.
- Import orchestrator latest state is reset in tests so `/import-jobs/latest` assertions are deterministic.

## Not Proven By This UAT

- Live Keycloak login and production claim issuance are not proven here; S03 owns signed-in/signed-out browser/UAT evidence.
- Live Torn API import success and background job completion against real Torn data are not proven here.
- Production operator identity-map repair workflow is not proven here; S03 owns operator gate closure.
- Performance under concurrent personal import load is not measured in this slice.
