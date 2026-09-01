# S06: Documentation alignment for DB-native anonymous model

**Goal:** Align repository documentation with the implemented DB-native import/reconstruction/read architecture, including anonymous aggregate API semantics and operational constraints observed in integration tests.
**Demo:** README/API docs describe actual DB-native behavior, no-auth aggregate model, and operational constraints.

## Must-Haves

- README describes DB-native import→reconstruct→read behavior as the canonical runtime path, not legacy CSV-export parity.
- API docs/examples cover `/v1/import`, `/v1/import/latest`, `/v1/import/{id}`, `/v1/gym-trains`, and `/v1/happy-events`, including the lifecycle nuance for latest status called out by S05.
- Documentation explicitly states current no-auth model, read/write surface boundaries, and deferred hardening items so consumers do not assume stronger guarantees than implemented.
- Documentation verification commands pass (docs are present, non-empty, and include required endpoint/constraint sections).

## Proof Level

- This slice proves: contract

## Integration Closure

Consumes tested API/runtime semantics from S05 integration coverage and closes the milestone’s external contract boundary by ensuring published docs match those executable contracts.

## Verification

- Documentation adds explicit operator-facing guidance for diagnosing import lifecycle and durable status behavior by referencing the tested API status surfaces rather than implicit expectations.

## Tasks

- [x] **T01: Rewrite README architecture and API sections for DB-native anonymous model** `est:45m`
  Skills to load: `write-docs`, `verify-before-complete`.

Refresh the top-level README so its behavior claims match the implemented API+DB runtime. Replace legacy framing that prioritizes CSV export with DB-native import/reconstruction/read semantics, document no-auth aggregate exposure, and preserve explicit boundaries about what is intentionally deferred (auth/write hardening, CORS tightening). Include an explicit note that `/v1/import/latest` reflects durable DB-backed run history plus current lifecycle timing nuances observed in hosted tests.
  - Files: `README.md`, `.gsd/milestones/M001/slices/S05/S05-SUMMARY.md`, `src/HappyGymStats.Api/Program.cs`, `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
  - Verify: rg -n "GET /v1/import/latest|GET /v1/import/\{id\}|GET /v1/gym-trains|GET /v1/happy-events|no-auth|CORS|DB-native|SQLite" README.md && test -s README.md

- [x] **T02: Align API request examples and docs verification with current endpoint contract** `est:30m`
  Skills to load: `write-docs`, `verify-before-complete`.

Update the API request/example doc surface to match current endpoint contract and ordering, including import status retrieval by id, pagination semantics on read endpoints, and practical operator notes for validating behavior quickly. Add lightweight executable doc-verification commands so future changes can mechanically confirm required sections/endpoints remain documented.
  - Files: `src/HappyGymStats.Api/HappyGymStats.Api.http`, `README.md`, `src/HappyGymStats.Api/Program.cs`
  - Verify: rg -n "POST .*\/v1\/import|GET .*\/v1\/import\/latest|GET .*\/v1\/import\/\{id\}|limit=|cursor=" src/HappyGymStats.Api/HappyGymStats.Api.http && rg -n "import/latest|import/\{id\}|no-auth|deferred" README.md

## Files Likely Touched

- README.md
- .gsd/milestones/M001/slices/S05/S05-SUMMARY.md
- src/HappyGymStats.Api/Program.cs
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- src/HappyGymStats.Api/HappyGymStats.Api.http
