---
estimated_steps: 1
estimated_files: 4
skills_used: []
---

# T01: Implement deterministic confidence projection in surfaces cache generation

Add backend projection logic that reads `ModifierProvenance` per derived train and computes a stable confidence score plus reason-code set for each gym point emitted to surfaces payload. Preserve existing series shape while adding confidence metadata as additive fields, and codify scoring rules directly from persisted verification status/reason semantics established in S03.

## Inputs

- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs``
- ``src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs``
- ``src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs``

## Expected Output

- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~Surface|FullyQualifiedName~Surfaces|FullyQualifiedName~DbPipelineIntegrationTests"

## Observability Impact

Signals added/changed: confidence score and reason-code arrays serialized into surfaces payload.
How a future agent inspects this: read generated `latest.json` and validate point-level confidence fields match provenance statuses.
Failure state exposed: unmatched provenance rows produce deterministic fallback reason entries instead of silent omission.
