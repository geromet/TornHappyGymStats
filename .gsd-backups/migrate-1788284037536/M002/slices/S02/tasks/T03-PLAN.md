---
estimated_steps: 47
estimated_files: 4
skills_used: []
---

# T03: Add schema contract tests for provenance states and interval constraints

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

## Inputs

- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`
- `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`

## Expected Output

- `tests/HappyGymStats.Tests/HappyGymStatsDbContextTests.cs`
- `tests/HappyGymStats.Tests/ModifierProvenanceSchemaTests.cs`

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests|FullyQualifiedName~ModifierProvenance" && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj

## Observability Impact

Adds executable failure-path checks guaranteeing unresolved provenance status remains diagnosable.
