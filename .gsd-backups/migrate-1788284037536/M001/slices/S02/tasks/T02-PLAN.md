---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T02: Wire DB-backed status endpoints and add restart-safe API endpoint coverage

Update import status routes to use new durable query methods and add `GET /v1/import/{id}` with standard error envelope semantics. Extend API tests to verify: (1) latest status is retrievable from DB-backed history, (2) specific run lookup by id works, (3) unknown id returns `404 not_found`, and (4) status remains queryable after constructing a fresh test client/service instance to simulate restart boundary.

## Inputs

- ``src/HappyGymStats.Api/Program.cs``
- ``src/HappyGymStats.Api/ImportService.cs``
- ``tests/HappyGymStats.Tests/ApiEndpointTests.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Expected Output

- ``src/HappyGymStats.Api/Program.cs``
- ``tests/HappyGymStats.Tests/ApiEndpointTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"
