---
estimated_steps: 59
estimated_files: 8
skills_used: []
---

# T01: Consolidate runtime primitive ownership into HappyGymStats.Core

---
estimated_steps: 7
estimated_files: 8
skills_used:
  - tdd
  - verify-before-complete
---

# T01: Consolidate runtime primitive ownership into HappyGymStats.Core

**Slice:** S01 — Finalize module ownership boundaries
**Milestone:** M001

## Description

Remove duplicate runtime primitive implementations from the CLI project so only Core owns fetch/reconstruction/path primitives while preserving current CLI/API behavior and compile compatibility.

## Failure Modes

| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| .NET build graph (`HappyGymStats.csproj`, `HappyGymStats.Core.csproj`) | Restore source files from git and re-apply changes incrementally to isolate missing symbols or namespace drift. | Not applicable (local build step). | Fail fast on compiler ambiguity/missing-type errors; update usings/constructor signatures to match Core contracts. |
| Existing CLI command flows (`src/HappyGymStats/Program.cs`) | Keep command behavior unchanged; if runtime behavior changes, rollback and rewire only ownership boundaries. | Not applicable. | Add targeted unit/integration assertions in T02 before finalizing to catch regressions. |

## Load Profile

- **Shared resources**: Build/test runner process memory and project references.
- **Per-operation cost**: One full solution compile plus selective namespace/type rewiring.
- **10x breakpoint**: N/A for runtime load; primary risk is maintenance churn from duplicate code reintroduction.

## Negative Tests

- **Malformed inputs**: N/A (no new external input surface).
- **Error paths**: Compile with removed files to ensure no unresolved symbol fallbacks remain.
- **Boundary conditions**: Verify both API and CLI composition roots still resolve same namespaces after source removal.

## Steps

1. Inventory duplicate ownership targets (`LogFetcher`, `ReconstructionRunner`, `AppPaths`, `Checkpoint`) across `src/HappyGymStats` and `src/HappyGymStats.Core`.
2. Remove duplicate source files from `src/HappyGymStats/Fetch`, `src/HappyGymStats/Reconstruction`, and `src/HappyGymStats/Storage/Models` that are already provided by Core.
3. Update `src/HappyGymStats/Program.cs` usings/construction paths to align with remaining Core-owned implementations without changing feature behavior.
4. Validate API composition root (`src/HappyGymStats.Api/Program.cs`) still consumes shared primitives only and has no hidden local fallback.
5. Ensure project references remain minimal and explicit (`HappyGymStats` and API depend on Core/Data, not duplicate source ownership).
6. Run `dotnet build` and fix ownership-related compile errors only (avoid scope creep).
7. Capture resulting ownership boundary in comments or tests added in T02, not ad-hoc documentation.

## Must-Haves

- [ ] Duplicate runtime primitive implementations are removed from CLI-local source tree where Core already provides them.
- [ ] CLI and API compile against shared Core primitives with no ambiguous type resolution.

## Verification

- `dotnet build`
- `dotnet test --filter "FullyQualifiedName~ModuleOwnership|FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ApiEndpointTests"`

## Observability Impact

- Signals added/changed: compile failures now clearly indicate ownership regressions instead of silent duplicate divergence.
- How a future agent inspects this: run `dotnet build` and inspect type/file ownership in errors.
- Failure state exposed: duplicate ownership or unresolved dependency is visible as deterministic build/test failures.

## Inputs

- `.gsd/milestones/M001/M001-ROADMAP.md` — slice demo and ownership objective.
- `src/HappyGymStats/Program.cs` — CLI composition root currently consuming duplicated primitives.
- `src/HappyGymStats.Api/Program.cs` — API composition root boundary check.
- `src/HappyGymStats/Fetch/LogFetcher.cs` — duplicate implementation candidate.
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs` — duplicate implementation candidate.
- `src/HappyGymStats/Storage/AppPaths.cs` — duplicate implementation candidate.
- `src/HappyGymStats/Storage/Models/Checkpoint.cs` — duplicate implementation candidate.
- `src/HappyGymStats.Core/Fetch/LogFetcher.cs` — canonical ownership target.

## Expected Output

- `src/HappyGymStats/Program.cs` — rewired to rely on Core-owned primitives only.
- `src/HappyGymStats/Fetch/LogFetcher.cs` — removed if duplicate of Core.
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs` — removed if duplicate of Core.
- `src/HappyGymStats/Storage/AppPaths.cs` — removed if duplicate of Core.
- `src/HappyGymStats/Storage/Models/Checkpoint.cs` — removed if duplicate of Core.
- `src/HappyGymStats/HappyGymStats.csproj` — updated only if compile-item ownership needs explicit adjustment.

## Inputs

- `.gsd/milestones/M001/M001-ROADMAP.md`
- `src/HappyGymStats/Program.cs`
- `src/HappyGymStats.Api/Program.cs`
- `src/HappyGymStats/Fetch/LogFetcher.cs`
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs`
- `src/HappyGymStats/Storage/AppPaths.cs`
- `src/HappyGymStats/Storage/Models/Checkpoint.cs`
- `src/HappyGymStats.Core/Fetch/LogFetcher.cs`

## Expected Output

- `src/HappyGymStats/Program.cs`
- `src/HappyGymStats/HappyGymStats.csproj`
- `src/HappyGymStats/Fetch/LogFetcher.cs`
- `src/HappyGymStats/Reconstruction/ReconstructionRunner.cs`
- `src/HappyGymStats/Storage/AppPaths.cs`
- `src/HappyGymStats/Storage/Models/Checkpoint.cs`

## Verification

dotnet build && dotnet test --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~ApiEndpointTests"

## Observability Impact

Build/test diagnostics become deterministic ownership boundary checks: duplicate type or missing type errors directly identify broken module ownership.
