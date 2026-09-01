# S02: Fix Blazor to API production boundary — UAT

**Milestone:** M003
**Written:** 2026-05-06T19:41:31.155Z

# UAT: S02 Blazor API production boundary and failure classification

## UAT Type

Operator acceptance and diagnostic-behavior UAT for the server-side Blazor surfaces page and import action. This UAT validates the intended production boundary, UI messaging contract, and log metadata contract using a deployed or staging-like Blazor host pointed at an API boundary that can be controlled or simulated.

## Preconditions

- Blazor host is deployed/running with `ApiBaseUrl` configured to `http://127.0.0.1:5047` for production.
- API service can be started/stopped or proxied to produce controlled responses on `/api/v1/torn/surfaces/latest` and `/api/v1/torn/surfaces/import`.
- Operator can view Blazor server logs.
- Use a non-production Torn API key or test value for import-path checks; never paste real secrets into screenshots or bug reports.

## Test Cases

1. **Production boundary configuration**
   - Step: Inspect Blazor runtime config/systemd environment for `ApiBaseUrl`.
   - Expected: Production value is `http://127.0.0.1:5047`; startup fails if the key is absent rather than silently falling back to localhost or the public domain.

2. **Healthy latest-surfaces load**
   - Step: Start API with a valid latest surfaces cache and load the Blazor home page.
   - Expected: Home renders surfaces data normally, with no 502/raw `EnsureSuccessStatusCode` text shown.

3. **No cached data / 404 path**
   - Step: Configure API latest-surfaces endpoint to return 404 for missing cache and reload Home.
   - Expected: UI shows the no-data state (`No surfaces data found. Run an import first.` or equivalent), not an API-down or bad-gateway alert.

4. **API unavailable path**
   - Step: Stop the API service or point loopback to an unavailable port, then reload Home.
   - Expected: UI displays the API-unavailable category message telling the operator to check the API service/loopback endpoint. Logs include category, endpoint, and no status code when no HTTP response exists.

5. **Reverse proxy / 502 path**
   - Step: Return HTTP 502 from the configured boundary for latest-surfaces.
   - Expected: UI displays the Bad Gateway/reverse-proxy diagnostic, distinct from generic API unavailable. Logs include endpoint `/api/v1/torn/surfaces/latest`, status `502`, and bad-gateway category.

6. **Malformed JSON path**
   - Step: Return HTTP 200 with malformed JSON from latest-surfaces.
   - Expected: UI displays a malformed API payload/deserialization diagnostic, not a no-data or bad-gateway message. Logs include endpoint/status/category.

7. **Import validation failure path**
   - Step: Trigger import with API returning 400 or 422 validation/import rejection.
   - Expected: UI displays validation/import failure guidance. Logs include import endpoint, status, and import/validation category. Torn API key value is absent from UI, exception text, and logs.

8. **Successful import path**
   - Step: Trigger import with API returning a successful import outcome.
   - Expected: UI reports success and subsequent latest-surfaces load uses the same service path/classification infrastructure.

## Edge Cases

- A 500/503 response should classify as backend/API failure rather than 502 reverse-proxy failure.
- A failed import outcome in a syntactically valid success response should classify as import failure.
- Any error path must preserve redaction: no Torn API key, bearer token, connection string, or secret config value in the UI or logs.

## Not Proven By This UAT

- Full production internet path through real nginx/Cloudflare and a browser is deferred to S05's full-stack smoke script.
- Load/performance behavior under concurrent users is not covered.
- Real Postgres startup/migration behavior is not covered here; S07 owns provider-backed integration coverage.
- AdminPanel routing and health behavior are owned by S03/S04.

