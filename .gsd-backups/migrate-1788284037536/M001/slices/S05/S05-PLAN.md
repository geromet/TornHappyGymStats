# S05: DB-native parity tests for import→reconstruct→read flow

**Goal:** Replace legacy CLI export parity coverage with DB-native end-to-end tests that prove import status durability and read-model consistency across import→reconstruct→read behavior.
**Demo:** Test suite validates DB-native end-to-end behavior without relying on legacy CLI export parity tests.

## Must-Haves

- Legacy disabled parity placeholder is removed and replaced by runnable DB-native parity tests.
- Test coverage exercises import status reads (`/v1/import/latest`, `/v1/import/{id}`) and derived read endpoints (`/v1/gym-trains`, `/v1/happy-events`) from DB-backed fixtures.
- Verification command for this slice passes using only DB-native test targets.

## Proof Level

- This slice proves: integration

## Integration Closure

Closes the milestone testing boundary by validating the end-to-end DB-native contract at API and pipeline test surfaces, removing reliance on deprecated CLI/export parity assumptions.

## Verification

- Parity failures surface through deterministic xUnit failures tied to ImportRuns outcomes and derived endpoint payload identity, giving future agents direct diagnostics in test output and DB-backed test fixtures.

## Tasks

- [x] **T01: Replace legacy parity placeholder with DB-native pipeline parity assertions** `est:45m`
  Convert `ExportedDatasetConsistencyTests` from a skipped placeholder into active tests that verify DB-native import→reconstruct→read parity assumptions directly against Core/Data behavior. Ground assertions in SQLite-backed fixtures and reconstruction outputs so parity is defined by durable DB state rather than legacy CLI exports.

Note for executor: keep fixtures tracked in test code (inline seeded rows or generated temp DB data); do not rely on ignored directories or runtime artifacts under `.gsd/`.
  - Files: `tests/HappyGymStats.Tests/ExportedDatasetConsistencyTests.cs`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ExportedDatasetConsistencyTests|FullyQualifiedName~DbPipelineIntegrationTests"

- [x] **T02: Add API-level end-to-end DB-native parity test for import status and derived reads** `est:1h`
  Extend API integration coverage to prove the full DB-native contract exposed to consumers: import run history is readable from DB-backed endpoints and derived read endpoints remain coherent with reconstructed data. Add one end-to-end test path that seeds raw/import state, runs reconstruction where needed, then validates `/v1/import/latest`, `/v1/import/{id}`, `/v1/gym-trains`, and `/v1/happy-events` expectations without CLI export dependencies.

Document any assumptions in test naming/comments so future slices can evolve docs from executable truth.
  - Files: `tests/HappyGymStats.Tests/ApiEndpointTests.cs`, `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`, `src/HappyGymStats.Api/Program.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests"

## Files Likely Touched

- tests/HappyGymStats.Tests/ExportedDatasetConsistencyTests.cs
- tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
- tests/HappyGymStats.Tests/ApiEndpointTests.cs
- src/HappyGymStats.Api/Program.cs
