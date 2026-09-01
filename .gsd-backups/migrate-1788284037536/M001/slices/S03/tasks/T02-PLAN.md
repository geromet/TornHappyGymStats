---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T02: Add API-level regression coverage for no-empty-window contract

Extend API integration coverage to assert read endpoints still return previously committed derived data when a reconstruction refresh attempt fails, matching the slice demo at consumer-facing boundary.

## Inputs

- ``tests/HappyGymStats.Tests/ApiEndpointTests.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``src/HappyGymStats.Api/Program.cs``
- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``

## Expected Output

- ``tests/HappyGymStats.Tests/ApiEndpointTests.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ApiEndpointTests|FullyQualifiedName~DbPipelineIntegrationTests"

## Observability Impact

Confirms externally visible read consistency and provides failing test diagnostics tied to concrete endpoints and DB-backed rows.
