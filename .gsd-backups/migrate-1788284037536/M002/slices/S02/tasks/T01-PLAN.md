---
estimated_steps: 48
estimated_files: 5
skills_used: []
---

# T01: Design provenance entities and wire them into DbContext

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

## Inputs

- `.gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md`
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`
- `src/HappyGymStats.Data/Entities/DerivedGymTrainEntity.cs`

## Expected Output

- `src/HappyGymStats.Data/Entities/ModifierProvenanceEntity.cs`
- `src/HappyGymStats.Data/HappyGymStatsDbContext.cs`

## Verification

dotnet build HappyGymStats.sln && dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~HappyGymStatsDbContextTests"

## Observability Impact

Persisted verification-state rows become a first-class diagnostic surface for missing modifier evidence.
