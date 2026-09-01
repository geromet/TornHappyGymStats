---
estimated_steps: 1
estimated_files: 2
skills_used: []
---

# T01: Implement transactional derived dataset swap in ReconstructionRunner

Wrap derived-table refresh in a single database transaction so delete+insert is all-or-nothing. Add a deterministic test seam for injecting a failure between clear and insert to prove rollback preserves last-good data.

## Inputs

- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``src/HappyGymStats.Data/HappyGymStatsDbContext.cs``

## Expected Output

- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"

## Observability Impact

Makes rollback/failure state diagnosable via stable derived row counts + persisted ImportRuns failure outcome, avoiding transient empty-state ambiguity.
