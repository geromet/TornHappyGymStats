---
estimated_steps: 37
estimated_files: 4
skills_used: []
---

# T01: Project unresolved provenance into operator warning records in API/cache

Implement a deterministic warning projection layer that groups unresolved modifier provenance by scope/log, carries reason codes, and includes actionable link targets where IDs are available.

### Failure Modes (Q5)
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| SQLite `ModifierProvenance` query | Return empty warning collection and emit explicit diagnostic count=0 with reason `query-failed` in logs/tests | Keep existing cache payload and flag stale-warning state | Skip malformed rows (invalid scope/status) and increment skipped-row diagnostic |
| Surfaces cache serialization | Fail cache write and preserve previous artifact | N/A (local write) | Reject invalid warning object schema in tests |

### Load Profile (Q6)
- **Shared resources**: DB read connection + in-memory grouping during cache build.
- **Per-operation cost**: one additional provenance scan/group pass per cache generation.
- **10x breakpoint**: memory growth in warning aggregation if unresolved rows surge; guard with bounded per-log warning records.

### Negative Tests (Q7)
- **Malformed inputs**: unknown scope/status rows are ignored with diagnostics.
- **Error paths**: DB access exception does not crash import service loop; warning payload degrades safely.
- **Boundary conditions**: zero unresolved rows yields empty warnings array, not null.

### Steps
1. Add API/Core model for `provenanceWarnings` payload items keyed by derived log/scope/reason.
2. Extend `SurfacesCacheWriter` (or adjacent projection path) to compute warnings from unresolved provenance rows and attach actionable links using known IDs/placeholders.
3. Add/extend integration tests to verify deterministic warning cardinality, ordering, and reason semantics across mixed datasets.

### Must-Haves
- [ ] Warning payload is additive and backward-compatible for existing consumers.
- [ ] Reason-code fidelity remains 1:1 with persisted provenance rows.

### Verification
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests"`
- `grep -q "provenanceWarnings" web/data/surfaces/latest.json || true` (artifact check via verify script)

### Observability Impact
- Signals added/changed: unresolved warning totals by reason/scope.
- How a future agent inspects this: inspect generated surfaces JSON + integration test assertions.
- Failure state exposed: skipped malformed provenance rows and fallback reasons are explicit.

### Inputs
- `.gsd/milestones/M002/slices/S03/S03-SUMMARY.md` — unresolved placeholder/reason semantics from prior slice
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — current surfaces payload projection
- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs` — confidence reason behavior contract
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — integration verification baseline

### Expected Output
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — warning projection wiring
- `src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs` — optional payload contract extensions
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — deterministic warning assertions

## Inputs

- ``.gsd/milestones/M002/slices/S03/S03-SUMMARY.md``
- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Expected Output

- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``src/HappyGymStats.Core/Reconstruction/SurfaceSeriesBuilder.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests|FullyQualifiedName~SurfaceSeriesBuilderConfidenceTests"

## Observability Impact

Adds machine-readable unresolved warning diagnostics into cache generation path.
