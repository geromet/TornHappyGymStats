---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T02: Add integration tests for surfaces confidence payload contract and failure semantics

Create/extend tests that execute import/reconstruction/cache-write flow and assert `/api/v1/torn/surfaces/latest`-compatible JSON includes confidence values and stable reason codes for both verified and unresolved provenance scenarios. Ensure contract remains deterministic and additive for existing consumers.

## Inputs

- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs``

## Expected Output

- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests" && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj

## Observability Impact

Signals added/changed: explicit test assertions for unresolved-reason propagation and confidence fallback behavior.
How a future agent inspects this: run filtered `DbPipelineIntegrationTests` and inspect assertion failures for missing/incorrect reason codes.
Failure state exposed: test output pinpoints whether regression is projection logic, payload serialization, or provenance join mismatch.
