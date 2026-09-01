---
estimated_steps: 2
estimated_files: 3
skills_used: []
---

# T01: Replace legacy parity placeholder with DB-native pipeline parity assertions

Convert `ExportedDatasetConsistencyTests` from a skipped placeholder into active tests that verify DB-native import→reconstruct→read parity assumptions directly against Core/Data behavior. Ground assertions in SQLite-backed fixtures and reconstruction outputs so parity is defined by durable DB state rather than legacy CLI exports.

Note for executor: keep fixtures tracked in test code (inline seeded rows or generated temp DB data); do not rely on ignored directories or runtime artifacts under `.gsd/`.

## Inputs

- `tests/HappyGymStats.Tests/ExportedDatasetConsistencyTests.cs`
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`
- `src/HappyGymStats.Core/Reconstruction/ReconstructionRunner.cs`
- `src/HappyGymStats.Data/Entities/ImportRunEntity.cs`

## Expected Output

- `tests/HappyGymStats.Tests/ExportedDatasetConsistencyTests.cs`
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ExportedDatasetConsistencyTests|FullyQualifiedName~DbPipelineIntegrationTests"

## Observability Impact

Keeps failure diagnosis DB-first by asserting explicit outcome/error fields and derived row stability when parity assumptions break.
