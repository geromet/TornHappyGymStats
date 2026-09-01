# S03: M004 verification, UAT, and operator gate closure — UAT

**Milestone:** M004
**Written:** 2026-05-09T17:56:50.530Z

# S03: M004 verification, UAT, and operator gate closure — UAT

**Milestone:** M004
**Written:** 2026-05-09

## UAT Type

- UAT mode: mixed
- Why this mode is sufficient: S03 is a final verification/operator-gate slice. Deterministic local commands prove build, tests, static auth/endpoint contracts, docs markers, secret redaction, and provenance regression safety. Live runtime/browser acceptance still requires an operator with Keycloak credentials and an identity-map row, so this UAT explicitly separates local closure from production-auth proof.

## Preconditions

- Repository checkout contains the M004 S01/S02 My stats read/import changes and S03 final-gate files.
- .NET SDK resolves through `global.json`.
- No production Torn API key or auth secret is required for local deterministic verification.
- For live/manual UAT only: a running API + Blazor deployment, a Keycloak user, and a matching identity-map row for that user are available.

## Smoke Test

Run `bash scripts/verify/m004-my-stats-final-gate.sh`. Expected: the command exits 0 and prints `M004 final gate passed.`

## Test Cases

### 1. Deterministic final gate passes without production secrets

1. From the repository root, run `bash scripts/verify/m004-my-stats-final-gate.sh` with no Torn API key exported.
2. Observe the gate sections for build, filtered tests, static auth/endpoint scans, docs contract, operator runbook markers, and provenance regression.
3. **Expected:** The command exits 0. Provenance verification uses secretless local artifact mode when `TORN_API_KEY`/`HAPPYGYMSTATS_TORN_API_KEY` are absent. No secret value is printed.

### 2. My stats auth and menu contract remains pinned

1. Run the filtered test command: `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~M004FinalGateTests|FullyQualifiedName~SqliteApiEndpointTests|FullyQualifiedName~BlazorApiFailureTests|FullyQualifiedName~SurfacesServiceFailureClassificationTests"`.
2. **Expected:** The command exits 0. The suite verifies `/my-stats` is auth-required, the menu exposes the My stats entry with an auth-required/lock marker, and Blazor calls the private `/me` endpoints rather than public/global endpoints.

### 3. Claim-bound API contracts reject unsafe ownership paths

1. Run `bash scripts/verify/m004-my-stats-final-gate.sh` or the filtered test command above.
2. Review test output if it fails.
3. **Expected:** Tests cover invalid claim 401 behavior, missing identity map `identity_setup_required`, subject mismatch 403, `/api/v1/torn/import-jobs/me` ignoring client owner tampering, and `/api/v1/torn/surfaces/me` deriving ownership from authenticated claims rather than a request-supplied PlayerID.

### 4. Operator Keycloak/identity-map gate is discoverable

1. Open `README.md` and `docs/SETUP.md`.
2. Follow the link/reference to `docs/M004-MY-STATS-OPERATOR-GATE.md`.
3. **Expected:** The runbook exists and includes signed-out behavior, signed-in personal cloud expectations, `/api/v1/torn/surfaces/me`, `/api/v1/torn/import-jobs/me`, `identity_setup_required`, Torn API key redaction guidance, and Keycloak/manual remediation instructions.

### 5. Live signed-out challenge (operator/runtime UAT)

1. In a deployed or locally running environment with auth configured, open `/my-stats` in a fresh signed-out browser session.
2. **Expected:** The user is challenged/redirected by auth. Private My stats data is not rendered for an anonymous browser session.

### 6. Live signed-in personal cloud (operator/runtime UAT)

1. Sign in as a Keycloak user that has a valid identity-map row.
2. Open `/my-stats`.
3. Trigger/read the personal cloud and, if applicable, queue an import with a Torn API key.
4. **Expected:** Blazor reads `/api/v1/torn/surfaces/me`, import queues through `/api/v1/torn/import-jobs/me`, only the signed-in user's gym cloud appears, and logs/UI evidence do not reveal Torn API key values.

## Edge Cases

### Missing identity-map row

1. Sign in as a Keycloak user without an identity-map row.
2. Open `/my-stats` and attempt the My stats flow.
3. **Expected:** The API reports an identity setup blocker (`identity_setup_required`/409 path) and the operator follows `docs/M004-MY-STATS-OPERATOR-GATE.md` remediation instead of creating ad-hoc ownership mappings.

### Cross-user ownership mismatch

1. Use a user/session whose Keycloak subject does not match the target identity-map owner.
2. Attempt to access or import through the `/me` endpoints.
3. **Expected:** The request is rejected (403 path in deterministic tests); the client cannot override anonymousId/player ownership in the request body.

### Malformed/failed API response

1. Simulate malformed API payload or API failure using the existing Blazor failure tests or a controlled dev endpoint failure.
2. **Expected:** Blazor classifies the failure safely, shows an actionable state, and does not echo Torn API keys or auth secrets.

## Failure Signals

- `scripts/verify/m004-my-stats-final-gate.sh` exits non-zero or does not print `M004 final gate passed.`
- Filtered M004/SQLite/Blazor failure tests fail.
- `scripts/verify/s08-docs-contract.sh` fails, especially on operator gate markers.
- `/my-stats` source loses `[Authorize]` or the menu loses its auth-required lock marker.
- Blazor My stats calls `/api/v1/torn/surfaces/latest` or any owner-selectable endpoint instead of `/api/v1/torn/surfaces/me` and `/api/v1/torn/import-jobs/me`.
- Logs, UI, docs, or test output include raw Torn API key values.

## Not Proven By This UAT

- A real production Keycloak login flow with live credentials; that requires operator-held auth access.
- A live identity-map remediation in production; the runbook defines the process but local deterministic tests cannot mutate production identity state.
- Performance or load behavior of My stats imports under many concurrent users.
- Full test-suite health outside the scoped M004 final gate and related deterministic contracts.

## Notes for Tester

Use the local final gate as the first acceptance signal. Treat live Keycloak/identity-map behavior as an operator evidence step and store only sanitized evidence: timestamps, endpoint names, status/category, user/anonymous identifiers in approved redacted form, and screenshots/log snippets with Torn API keys and tokens removed.
