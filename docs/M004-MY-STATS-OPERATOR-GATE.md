# M004 My stats operator gate (Keycloak + identity map)

## Audience and outcome

Audience: production operators approving My stats before M004 closure.

Outcome: you can verify `/my-stats` is claim-bound, detect identity setup blockers safely, and record UAT evidence without storing secrets in git.

## Gate prerequisites (stop if any fail)

1. Keycloak login is reachable and user sign-in succeeds.
2. API boundary is reachable and healthy (`/api/v1/torn/health`).
3. Identity map row exists for the authenticated user and maps the correct Keycloak subject.
4. You can access the authenticated page `/my-stats` after sign-in.

If any prerequisite fails, treat this as a **safe UAT blocker** and do not approve closure until resolved.

## Security and evidence rules

- Never commit secrets, tokens, cookies, Authorization headers, or raw session dumps.
- Never paste a real **Torn API key** into tracked files, issues, or PR comments.
- Evidence must use placeholders such as `<redacted-api-key>` and `<redacted-bearer-token>`.
- For failing HTTP responses, store only sanitized excerpts: status code, endpoint path, error code, and a short safe message.

## Smoke checks by scenario

### 1) Signed-out challenge (negative)

Expected behavior:
- Visiting `/my-stats` when signed-out triggers an auth challenge (redirect to sign-in).
- Private API endpoints reject unauthenticated calls with `401`.

Evidence fields:
- `scenario: signed-out`
- `ui: /my-stats challenged to sign-in`
- `api: 401 on /api/v1/torn/surfaces/me and /api/v1/torn/import-jobs/me`

### 2) Signed-in personal cloud happy path

Expected behavior:
- Signed-in user opens `/my-stats` and sees personal cloud content (or clear empty-state if no data).
- Import from `/my-stats` calls `/api/v1/torn/import-jobs/me` and then refreshes `/api/v1/torn/surfaces/me`.

Evidence fields:
- `scenario: signed-in-happy-path`
- `api: /api/v1/torn/import-jobs/me accepted (202 or terminal 200)`
- `api: /api/v1/torn/surfaces/me success (200) or no-data empty state`

### 3) Missing identity map (negative, setup blocker)

Expected behavior:
- Authenticated import or surfaces call returns `409` with `identity_setup_required`.
- UI communicates that identity linking is required.

Operator action:
- Treat as setup blocker and repair identity map (see remediation section).

Evidence fields:
- `scenario: identity-map-missing`
- `api: 409 identity_setup_required`
- `decision: blocked pending identity map repair`

### 4) Identity map mismatch (negative)

Expected behavior:
- Authenticated user whose Keycloak subject does not match mapped owner receives `403 forbidden`.

Evidence fields:
- `scenario: identity-map-mismatch`
- `api: 403 forbidden`
- `decision: blocked pending mapping correction`

### 5) Expired session (negative)

Expected behavior:
- API calls to private endpoints return `401 unauthorized`.
- UI asks user to sign in again.

Evidence fields:
- `scenario: expired-session`
- `api: 401 unauthorized`

### 6) No-data empty state (negative but non-blocking)

Expected behavior:
- `/api/v1/torn/surfaces/me` may return not-found/no personal surface; UI shows empty-state guidance to import first.

Evidence fields:
- `scenario: no-data-empty-state`
- `api: not-found/no-data handled safely`

### 7) API unavailable / gateway failure (negative, blocker)

Expected behavior:
- API unavailable is surfaced as unavailable; reverse proxy failure surfaces as `502`.
- UI shows safe failure text without leaking internals.

Evidence fields:
- `scenario: api-unavailable`
- `api: unavailable or 502 bad gateway`
- `decision: blocked pending service recovery`

### 8) Malformed/failed response handling (negative)

Expected behavior:
- Malformed payloads are treated as safe deserialization failures.
- Evidence contains only sanitized snippets; no secret-bearing payload copies.

Evidence fields:
- `scenario: malformed-response`
- `api: deserialization/safe-failure classification`

## Manual remediation gate (Keycloak and identity map)

Use this only when scenarios 3 or 4 fail.

1. Confirm user can authenticate in Keycloak and capture only non-secret identity metadata required for matching (for example, subject identifier with sensitive sections redacted if policy requires).
2. Verify identity-map storage has exactly one active row for the target anonymous id.
3. Ensure the mapped Keycloak subject matches the authenticated caller subject.
4. If row missing, create the mapping through the approved operational path.
5. If mismatch, correct mapping through the approved operational path and remove stale/incorrect mapping.
6. Re-run signed-in checks for `/api/v1/torn/import-jobs/me` and `/api/v1/torn/surfaces/me`.

Do not copy Keycloak admin secrets, bearer tokens, or Torn API keys into any tracked artifact during remediation.

## UAT evidence template (sanitized)

Use this structure in ticket notes or UAT logs:

```text
operator: <name or on-call alias>
datetime_utc: <timestamp>
environment: <prod|staging>

scenario: <signed-out|signed-in-happy-path|identity-map-missing|identity-map-mismatch|expired-session|no-data-empty-state|api-unavailable|malformed-response>
ui_result: <pass|fail|blocked>
api_endpoint: </api/v1/torn/surfaces/me | /api/v1/torn/import-jobs/me>
http_status: <200|202|401|403|409|502|unavailable>
error_code: <identity_setup_required|forbidden|unauthorized|n/a>
sanitized_excerpt: <short safe message without tokens/keys>
redaction_check: pass (no Torn API key, no bearer token, no cookie dump)

closure_decision: <ready|blocked>
follow_up: <none or ticket id>
```

## Load and shared-resource caution (10x operator volume)

Shared resources at risk:
- Keycloak admin state
- Identity-map rows
- API health endpoints

At higher operator/test volume, the primary risk is human error and secret leakage, not CPU saturation. Mitigation:
- Use placeholders by default.
- Keep evidence minimal and sanitized.
- Avoid parallel manual identity edits for the same account.
- Re-run smoke checks after each correction.

## Closure rule

Approve M004 My stats gate only when:
- signed-out challenge is confirmed,
- signed-in claim-bound `/me` endpoints behave correctly,
- failure modes are safe and categorized,
- evidence is sanitized and secret-safe,
- and any Keycloak/identity-map blockers are resolved or explicitly logged as blockers.
