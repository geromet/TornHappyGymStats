---
estimated_steps: 43
estimated_files: 4
skills_used: []
---

# T02: Create and validate EF migration for provenance schema

---
estimated_steps: 5
estimated_files: 4
skills_used:
  - best-practices
  - test
---

# T02: Create and validate EF migration for provenance schema

**Slice:** S02 — Modifier Provenance Data Model
**Milestone:** M002

## Description
Materialize the model changes as an EF migration and update model snapshot so schema evolution is deterministic and reviewable. This task closes the DB deployment contract for S02.

## Failure Modes
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| `dotnet ef` tooling | fix model inconsistencies and regenerate migration | retry once after clean build; fail with captured command output | inspect generated SQL/migration for unintended column/table changes and correct model |

## Load Profile
- **Shared resources**: migration application lock on SQLite DB file
- **Per-operation cost**: one schema migration execution
- **10x breakpoint**: repeated dev resets if migration is non-idempotent or drifts from snapshot

## Negative Tests
- **Malformed inputs**: migration should fail if required columns are missing from generated model
- **Error paths**: downgrade path removes added schema cleanly
- **Boundary conditions**: empty DB can apply full migration chain including new migration

## Steps
1. Scaffold migration for provenance model changes in HappyGymStats.Data/Migrations.
2. Review generated Up/Down for only intended schema operations.
3. Update model snapshot consistency.
4. Apply migrations on ephemeral DB via test path/ensure-created compatibility check.
5. Run targeted tests to prove schema presence and constraints.

## Must-Haves
- [ ] Migration Up/Down are reversible and scoped to provenance additions.
- [ ] Snapshot aligns with DbContext and no accidental drift remains.

## Verification
- `dotnet ef migrations add AddModifierProvenanceModel --project src/HappyGymStats.Data --startup-project src/HappyGymStats.Api --no-build`
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"`

## Inputs
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs` — updated model from T01
- `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs` — migration baseline snapshot

## Expected Output
- `src/HappyGymStats.Data/Migrations/*_AddModifierProvenanceModel.cs` — new schema migration
- `src/HappyGymStats.Data/Migrations/*_AddModifierProvenanceModel.Designer.cs` — migration designer metadata
- `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs` — updated snapshot

## Inputs

- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`
- `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs`

## Expected Output

- `src/HappyGymStats.Data/Migrations/2026*_AddModifierProvenanceModel.cs`
- `src/HappyGymStats.Data/Migrations/2026*_AddModifierProvenanceModel.Designer.cs`
- `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs`

## Verification

dotnet ef migrations add AddModifierProvenanceModel --project src/HappyGymStats.Data --startup-project src/HappyGymStats.Api --no-build && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"
