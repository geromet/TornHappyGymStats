---
estimated_steps: 2
estimated_files: 4
skills_used: []
---

# T01: Add deterministic reconstruction benchmark harness and synthetic fixture generator

Create a repeatable benchmark path that exercises the real reconstruction pipeline against SQLite with a large synthetic log dataset. This closes the biggest execution risk first: without a deterministic harness, later baseline numbers are not trustworthy or comparable across runs.

Use existing Core runtime entrypoints (ReconstructionRunner and AppPaths) rather than introducing test-only reconstruction logic so measurements represent production code paths.

## Inputs

- ``.gsd/milestones/M001/slices/S03/S03-SUMMARY.md``
- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj``

## Expected Output

- ``tests/HappyGymStats.Tests/Performance/ReconstructionPerformanceBenchmarkTests.cs``
- ``tests/HappyGymStats.Tests/TestUtilities/SyntheticLogFixtureBuilder.cs``
- ``tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ReconstructionPerformanceBenchmarkTests"

## Observability Impact

Adds benchmark-result emission (duration + row/event counters) into a stable artifact file under test output for later inspection and regression comparison.
