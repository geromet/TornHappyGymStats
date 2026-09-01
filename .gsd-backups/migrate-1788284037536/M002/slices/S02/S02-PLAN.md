# S02: Modifier Provenance Data Model

**Goal:** Model and persist time-bounded modifier provenance states so reconstruction can represent verification completeness for personal, faction, and company evidence.
**Demo:** After this slice, DB schema supports time-bounded modifier provenance and verification states for personal/faction/company contributions.

## Must-Haves

- Database schema includes explicit provenance entities/columns for personal/faction/company modifier evidence with validity windows and verification status.
- EF model + migrations apply cleanly and can persist/read provenance rows linked to derived gym train records.
- Automated tests prove required constraints (time bounds, scope enum/status values, and relationship wiring) and failure-path visibility for unresolved verification states.

## Proof Level

- This slice proves: contract

## Integration Closure

Data layer contract closed: persistence model and migration contract are verified; runtime reconstruction wiring is intentionally deferred to S03.

## Verification

- Adds inspectable provenance persistence surface in SQLite so unresolved verification states can be queried deterministically during reconstruction/API debugging.

## Tasks

- [x] **T01: Design provenance entities and wire them into DbContext** `est:75m`
  ---
estimated_steps: 6
estimated_files: 5
skills_used:
  - design-an-interface
  - best-practices
---

# T01: Design provenance entities and wire them into DbContext

**Slice:** S02 — Modifier Provenance Data Model
**Milestone:** M002

## Description
Define the canonical persistence shape for modifier provenance across personal/faction/company scopes, including time-bounded intervals and verification lifecycle fields. This task establishes the schema contract S03 will consume.

## Failure Modes
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| EF Core model/migrations | fail fast during migration generation and fix model annotations before proceeding | N/A (local operation) | reject ambiguous enum/text shape and normalize to constrained values |

## Load Profile
- **Shared resources**: SQLite table/index scans on provenance joins
- **Per-operation cost**: one additional row write per reconstructed interval plus indexed lookup by scope/time
- **10x breakpoint**: unindexed interval lookups causing slow reconstruction joins

## Negative Tests
- **Malformed inputs**: empty owner/faction/company identifiers should be rejected by required-field constraints where applicable
- **Error paths**: insert with invalid verification status should fail via constrained domain mapping
- **Boundary conditions**: adjacent time windows (end == next start) remain representable without overlap corruption

## Steps
1. Add provenance entity/entities under Data/Entities with scope, subject identifiers, valid-from/valid-to, verification status, and reason fields.
2. Register new DbSet(s) and modelBuilder constraints/indexes in HappyGymStatsDbContext.
3. Add relationship key(s) from provenance rows to derived train identity surface intended for S03 usage.
4. Ensure converters/column types preserve UTC semantics for interval bounds.
5. Add concise XML/comments documenting contract intent for downstream slices.
6. Build to confirm model compiles before migration scaffolding.

## Must-Haves
- [ ] Data model supports personal/faction/company scope explicitly.
- [ ] Model captures verification state plus machine-readable reason for unresolved evidence.

## Verification
- `dotnet build HappyGymStats.sln`
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"`

## Observability Impact
- Signals added/changed: persisted verification status/reason rows become queryable diagnostics.
- How a future agent inspects this: SQL query against provenance table keyed by scope and time window.
- Failure state exposed: unresolved/missing evidence is explicit in persisted status fields.

## Inputs
- `.gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md` — validated scope taxonomy and confidence-impact mapping
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs` — existing EF model configuration baseline
- `src/HappyGymStats.Data/Entities/DerivedGymTrainEntity.cs` — existing derived record surface to anchor provenance linkage

## Expected Output
- `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs` — new persistence contract for provenance intervals
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs` — DbSet + model constraints/index wiring
  - Files: `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`, `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`, `src/HappyGymStats.Data/Entities/DerivedGymTrainEntity.cs`, `src/HappyGymStats.Data/Entities/ImportRunEntity.cs`, `src/HappyGymStats.Data/HappyGymStatsDbContextFactory.cs`
  - Verify: dotnet build HappyGymStats.sln && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"

- [x] **T02: Create and validate EF migration for provenance schema** `est:45m`
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
  - Files: `src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs`, `src/HappyGymStats.Data/Migrations`, `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`, `src/HappyGymStats.Api/Program.cs`
  - Verify: dotnet ef migrations add AddModifierProvenanceModel --project src/HappyGymStats.Data --startup-project src/HappyGymStats.Api --no-build && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"

- [x] **T03: Add schema contract tests for provenance states and interval constraints** `est:60m`
  ---
estimated_steps: 6
estimated_files: 4
skills_used:
  - tdd
  - test
---

# T03: Add schema contract tests for provenance states and interval constraints

**Slice:** S02 — Modifier Provenance Data Model
**Milestone:** M002

## Description
Add targeted tests that assert the new provenance schema behaves as required: scope coverage, verification-state persistence, interval-bound correctness, and diagnostic visibility of unresolved states.

## Failure Modes
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| SQLite in-memory test database | fail test with explicit assertion context and seed diagnostics | reduce fixture volume and rerun locally; investigate hanging query/index mismatch | assert strict column/value expectations to catch malformed persistence |

## Load Profile
- **Shared resources**: in-memory SQLite connection in test host
- **Per-operation cost**: small seed + query batches
- **10x breakpoint**: excessive fixture size causing slow CI; keep focused fixtures

## Negative Tests
- **Malformed inputs**: invalid scope/status values cannot be persisted through the intended contract path
- **Error paths**: unresolved verification reason remains present and queryable for low-confidence rows
- **Boundary conditions**: open-ended intervals (`ValidToUtc` null) and bounded intervals both round-trip correctly

## Steps
1. Extend DbContext test suite with provenance table existence/index checks.
2. Add round-trip tests for personal/faction/company rows including status + reason fields.
3. Add tests for interval-boundary persistence (null end, bounded end, UTC normalization).
4. Add unresolved-state diagnostic query assertion used by downstream slices.
5. Run targeted tests and full test pass for regression safety.

## Must-Haves
- [ ] Tests fail if schema drops provenance state or interval fields.
- [ ] Tests prove unresolved verification is inspectable for operator/debug workflows.

## Verification
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests|FullyQualifiedName~ModifierProvenance"`
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`

## Observability Impact
- Signals added/changed: regression tripwires for unresolved verification-state persistence.
- How a future agent inspects this: run targeted DbContext tests to validate provenance diagnostics contract.
- Failure state exposed: missing/incorrect status-reason persistence fails with explicit assertion names.

## Inputs
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` — existing DB schema test harness
- `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs` — new provenance contract from T01
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs` — finalized model wiring from T01/T02

## Expected Output
- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs` — expanded schema contract coverage
- `tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs` — dedicated provenance behavior tests
  - Files: `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`, `tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs`, `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`, `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`
  - Verify: dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests|FullyQualifiedName~ModifierProvenance" && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj

## Files Likely Touched

- src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs
- src/HappyGymStats.Data/HappyGymStatsDbContext.cs
- src/HappyGymStats.Data/Entities/DerivedGymTrainEntity.cs
- src/HappyGymStats.Data/Entities/ImportRunEntity.cs
- src/HappyGymStats.Data/HappyGymStatsDbContextFactory.cs
- src/HappyGymStats.Data/Migrations/HappyGymStatsDbContextModelSnapshot.cs
- src/HappyGymStats.Data/Migrations
- src/HappyGymStats.Api/Program.cs
- tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs
- tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs
