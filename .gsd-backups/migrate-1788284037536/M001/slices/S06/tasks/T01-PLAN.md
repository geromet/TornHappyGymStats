---
estimated_steps: 2
estimated_files: 4
skills_used: []
---

# T01: Rewrite README architecture and API sections for DB-native anonymous model

Skills to load: `write-docs`, `verify-before-complete`.

Refresh the top-level README so its behavior claims match the implemented API+DB runtime. Replace legacy framing that prioritizes CSV export with DB-native import/reconstruction/read semantics, document no-auth aggregate exposure, and preserve explicit boundaries about what is intentionally deferred (auth/write hardening, CORS tightening). Include an explicit note that `/v1/import/latest` reflects durable DB-backed run history plus current lifecycle timing nuances observed in hosted tests.

## Inputs

- ``README.md``
- ``.gsd/milestones/M001/slices/S05/S05-SUMMARY.md``
- ``src/HappyGymStats.Api/Program.cs``
- ``tests/HappyGymStats.Tests/ApiEndpointTests.cs``

## Expected Output

- ``README.md``

## Verification

rg -n "GET /v1/import/latest|GET /v1/import/\{id\}|GET /v1/gym-trains|GET /v1/happy-events|no-auth|CORS|DB-native|SQLite" README.md && test -s README.md
