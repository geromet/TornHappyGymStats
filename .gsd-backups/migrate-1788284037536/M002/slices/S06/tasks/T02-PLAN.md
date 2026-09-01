---
estimated_steps: 36
estimated_files: 4
skills_used: []
---

# T02: Add optional local faction/company override ingestion with strict validation

Add a bounded, optional override source (tracked config/sample + parser) that maps unresolved faction/company placeholders to operator-provided IDs/links for guidance only, without mutating stored provenance records.

### Failure Modes (Q5)
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| Override file read | Continue with no overrides and emit `override-read-failed` diagnostic | N/A | Reject malformed entries, keep valid subset, expose skipped count |
| JSON parser | Use empty override set and emit parse error | N/A | Validate schema fields and ignore unknown/invalid keys |

### Load Profile (Q6)
- **Shared resources**: file I/O + small in-memory dictionary.
- **Per-operation cost**: one file read/parse per cache refresh.
- **10x breakpoint**: oversized override file; enforce max entries and max field length.

### Negative Tests (Q7)
- **Malformed inputs**: bad JSON, missing required keys, unknown scope.
- **Error paths**: missing file path and unreadable file degrade gracefully.
- **Boundary conditions**: duplicate keys resolve deterministically (last-write-wins or explicit rejection, documented in tests).

### Steps
1. Define override schema and loader utility in Core/API layer with strict validation and bounded limits.
2. Wire loader into warning projection so warnings can display richer action hints when override exists.
3. Add focused tests for parser validation, duplicate handling, and graceful fallback when file absent.

### Must-Haves
- [ ] Overrides are optional and never required for normal payload generation.
- [ ] Override usage is explicitly marked as local/manual in warning metadata.

### Verification
- `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierOverride|FullyQualifiedName~DbPipelineIntegrationTests"`

### Observability Impact
- Signals added/changed: override loaded/skipped entry counts.
- How a future agent inspects this: verify script + test output + warning metadata flags.
- Failure state exposed: parse/validation failures surfaced as deterministic diagnostics.

### Inputs
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — warning projection integration point
- `src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs` — reason/scope constants
- `tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs` — end-to-end contract tests

### Expected Output
- `src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs` — validated override loader
- `src/HappyGymStats.Api/SurfacesCacheWriter.cs` — override-aware warning enrichment
- `tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs` — parser and fallback coverage
- `web/data/surfaces/modifier-overrides.sample.json` — tracked sample override file

## Inputs

- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``src/HappyGymStats.Core/Reconstruction/HappyReconstructionModels.cs``
- ``tests/HappyGymStats.Tests/DbPipelineIntegrationTests.cs``

## Expected Output

- ``src/HappyGymStats.Core/Reconstruction/ModifierOverrideLoader.cs``
- ``src/HappyGymStats.Api/SurfacesCacheWriter.cs``
- ``tests/HappyGymStats.Tests/ModifierOverrideLoaderTests.cs``
- ``web/data/surfaces/modifier-overrides.sample.json``

## Verification

dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierOverride|FullyQualifiedName~DbPipelineIntegrationTests"

## Observability Impact

Exposes override parse/load diagnostics and manual-source marker for warning enrichment.
