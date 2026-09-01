---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T02: Persist provenance rows during transactional reconstruction refresh

Wire `ReconstructionRunner` to materialize modifier provenance rows (personal verified baseline plus unresolved faction/company placeholders) and persist them in the same DB transaction that replaces derived trains/events.

## Inputs

- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``src/HappyGymStats.Data/HappyGymStatsDbContext.cs``
- ``src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs``

## Expected Output

- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``
- ``src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"

## Observability Impact

Ensures unresolved dependency evidence is queryable immediately after each run via `ModifierProvenance` and fails as an atomic unit if refresh persistence breaks.
