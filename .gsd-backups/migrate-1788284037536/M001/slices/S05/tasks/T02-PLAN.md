---
estimated_steps: 2
estimated_files: 3
skills_used: []
---

# T02: Add API-level end-to-end DB-native parity test for import status and derived reads

Extend API integration coverage to prove the full DB-native contract exposed to consumers: import run history is readable from DB-backed endpoints and derived read endpoints remain coherent with reconstructed data. Add one end-to-end test path that seeds raw/import state, runs reconstruction where needed, then validates `/v1/import/latest`, `/v1/import/{id}`, `/v1/gym-trains`, and `/v1/happy-events` expectations without CLI export dependencies.

Document any assumptions in test naming/comments so future slices can evolve docs from executable truth.

## Inputs

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
- `src/HappyGymStats.Api/Program.cs`
- `src/HappyGymStats.Api/ImportService.cs`

## Expected Output

- `tests/HappyGymStats.Tests/ApiEndpointTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests"

## Observability Impact

Ensures endpoint parity regressions fail with request/response-level assertions (including outcome/reconstruction status fields) that expose contract drift quickly.
