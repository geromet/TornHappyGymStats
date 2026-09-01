---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T03: Cover surfaces endpoint under Postgres

Why: Surfaces endpoint behavior is directly tied to the reported frontend error and should be tested under the production provider too.

Do:
1. Extend Postgres integration tests to configure a temporary surfaces cache directory.
2. Assert missing `latest.json` returns structured 404 from `/api/v1/torn/surfaces/latest`.
3. Add a present-cache case by writing valid `latest.json`/`meta.json` or invoking cache writer with seeded rows if practical.
4. Assert content type and basic JSON envelope.
5. Avoid requiring a Torn API key.

Done when: provider tests cover surfaces no-cache and present-cache behavior without external API calls.

## Inputs

- `src/HappyGymStats.Api/Controllers/SurfacesController.cs`
- `scripts/verify/s05-local-surfaces.sh`

## Expected Output

- `tests/HappyGymStats.Tests/PostgresApiIntegrationTests.cs`

## Verification

dotnet test --filter "PostgresApiIntegration"

## Observability Impact

Signals added/changed: provider test for no-cache vs present-cache endpoint behavior.
How a future agent inspects this: Postgres integration test output.
Failure state exposed: endpoint 500/misconfigured cache instead of structured 404/200.
