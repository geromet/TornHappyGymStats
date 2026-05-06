# S02: Fix Blazor to API production boundary

**Goal:** Choose and enforce the production API boundary for server-side Blazor, then improve failure classification so users and operators can tell what broke. This directly addresses the reported surfaces 502 messages.
**Demo:** Blazor can load surfaces without 502, and UI errors distinguish API down, nginx bad gateway, 404 no-cache, and import failure.

## Must-Haves

- The production `ApiBaseUrl` strategy is explicit: either loopback API URL for server-side Blazor or deliberately same-origin public nginx URL with documented rationale.
- `SurfacesService` or its equivalent includes response classification rather than surfacing only raw `EnsureSuccessStatusCode` messages.
- UI distinguishes no cached data (404) from API unavailable/nginx 502 from validation/import failure.
- Logs include endpoint, status code, and failure category, but never include Torn API keys or secrets.
- The import action and initial surfaces load both use the same classification path.
- Verification proves Blazor home can load or display an accurate no-data state through the chosen production boundary.
- Regression coverage exists for the response classifier or service behavior where practical.

## Proof Level

- This slice proves: Frontend/backend integration proof through the Blazor server runtime. Unit-level classifier tests are required but not sufficient; final proof must exercise the Blazor surfaces load path against the selected API boundary.

## Integration Closure

Upstream surfaces consumed: S01 API runtime contract and health gate semantics; `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs`; `SurfacesService`; `Home.razor`; API health/surfaces/import routes.
New wiring introduced: Blazor-specific API boundary and response classification for surfaces/import calls.
What remains before milestone end-to-end: S05 must include the Blazor home and API boundary in the full production smoke command.

## Verification

- Runtime signals: classified UI error messages, structured server logs with endpoint/status/category, and deploy/smoke checks for Blazor home reachability.
- Inspection surfaces: Blazor UI alert text, app logs, `SurfacesService` tests, browser/network checks where available.
- Failure visibility: API unavailable, nginx 502, missing surfaces cache 404, validation/import failure, malformed JSON/deserialization failure.
- Redaction constraints: Torn API key must never be logged, echoed, included in exception messages, or captured in UI diagnostics.

## Tasks

- [ ] **T01: Standardize Blazor production API base URL** `est:45m`
  Why: Server-side Blazor currently uses `ApiBaseUrl` from appsettings, which points to the public site. The production boundary should be explicit so the app does not accidentally route through Cloudflare/nginx when loopback is intended.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.Development.json`, `infra/happygymstats-blazor.service`, `docs/DEPLOYMENT.md`
  - Verify: rg -n "ApiBaseUrl|127.0.0.1:5047|torn.geromet.com|localhost" src/HappyGymStats.Blazor/HappyGymStats.Blazor infra/happygymstats-blazor.service docs/DEPLOYMENT.md && dotnet build

- [ ] **T02: Classify API failures in SurfacesService** `est:1.5h`
  Why: Raw `EnsureSuccessStatusCode` exceptions produce opaque UI messages and hide the difference between bad gateway, no data, validation failure, and malformed backend responses.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs`
  - Verify: dotnet build && rg -n "BadGateway|NotFound|ApiFailure|EnsureSuccessStatusCode|apiKey" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor

- [ ] **T03: Render actionable Blazor failure states** `est:1h`
  Why: The UI should translate classified service failures into operator-usable messages without exposing implementation noise or secrets.
  - Files: `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs`
  - Verify: dotnet build && rg -n "Failed to load surfaces data|Bad Gateway|bad gateway|endpoint|status|ApiFailure" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor

- [ ] **T04: Test Blazor API failure classification** `est:1.5h`
  Why: Classifier behavior should survive refactors, and S02 needs mechanical verification before browser/production smoke exists.
  - Files: `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `tests/HappyGymStats.Tests/BlazorApiFailureTests.cs`, `scripts/verify/s02-blazor-api-boundary.sh`
  - Verify: bash scripts/verify/s02-blazor-api-boundary.sh

## Files Likely Touched

- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Program.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.json
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/appsettings.Development.json
- infra/happygymstats-blazor.service
- docs/DEPLOYMENT.md
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Services/SurfacesService.cs
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/Models/SurfacesDtos.cs
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
- tests/HappyGymStats.Tests/BlazorApiFailureTests.cs
- scripts/verify/s02-blazor-api-boundary.sh
