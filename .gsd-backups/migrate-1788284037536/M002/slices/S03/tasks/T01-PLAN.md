---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T01: Add provenance reconstruction model and reason-code contract

Define Core-side provenance output records and deterministic unresolved reason constants so reconstruction can emit personal/faction/company evidence states in a stable shape consumed by Data persistence and later API confidence scoring.

## Inputs

- ``src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs``
- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``tests/HappyGymStats.Tests/HappyTimelineReconstructorBehaviorTests.cs``

## Expected Output

- ``src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs``
- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``tests/HappyGymStats.Tests/HappyTimelineReconstructorBehaviorTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyTimelineReconstructorBehaviorTests"

## Observability Impact

Adds machine-readable provenance status/reason outputs to reconstruction result objects so downstream persistence and diagnostics can inspect unresolved dependency causes without inferring from missing rows.
