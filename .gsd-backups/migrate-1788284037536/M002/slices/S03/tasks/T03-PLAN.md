---
estimated_steps: 1
estimated_files: 3
skills_used: []
---

# T03: Add integration tests for provenance persistence and unresolved dependency diagnostics

Add/extend integration coverage to prove reconstruction writes provenance rows and unresolved faction/company reason codes, and that full test suite remains green.

## Inputs

- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs``
- ``src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs``

## Expected Output

- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``
- ``tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ModifierProvenanceSchemaTests" && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj

## Observability Impact

Locks in an inspection path where future agents can query reconstruction-produced provenance states and quickly localize failures to extraction/reconstruction/persistence boundaries.
