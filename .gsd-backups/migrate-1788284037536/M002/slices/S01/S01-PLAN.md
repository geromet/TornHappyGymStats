# S01: S01

**Goal:** Establish a validated Torn endpoint/log taxonomy that maps personal/faction/company modifier evidence to required key scopes, expected payload fields, and confidence impact, while enforcing the updated layering boundary (API/CLI -> Core -> Data+Visualizer).
**Demo:** After this slice, we have a validated endpoint/log taxonomy matrix with required key scopes, known log IDs/categories, expected payload fields, and confidence impact mapping.

## Must-Haves

- A single taxonomy artifact captures endpoints, categories/log IDs, required fields, trust level, and confidence effect per evidence source.
- The taxonomy is grounded in current repository contracts (existing API endpoints, import URL shape, and extractor field usage).
- Verification commands fail if required sections are missing or mapped anchors drift from source code.
- Slice artifacts explicitly preserve the post-refactor architecture boundary to prevent cross-layer regressions.

## Proof Level

- This slice proves: contract

## Integration Closure

This slice closes the discovery contract used by S02/S03 and anchors the post-refactor layering boundary (API/CLI -> Core -> Data+Visualizer) so upcoming slices do not reintroduce cross-layer coupling.

## Verification

- Adds deterministic drift checks that fail fast when taxonomy anchors diverge from code, improving operator visibility for planning-to-code drift.

## Tasks

- [x] **T01: Inventory current endpoint and extraction anchors for modifier-bearing evidence** `est:45m`
  Build a grounded inventory of current API route contracts, Torn log fetch entrypoint, and event extractor field heuristics. Write a compact discovery artifact listing concrete anchors and known gaps for personal/faction/company modifier evidence, with assumptions made explicit where Torn-side payloads are not yet represented in local models.
  - Files: `src/HappyGymStats.Api/Program.cs`, `src/HappyGymStats.Api/ImportService.cs`, `src/HappyGymStats.Core/Torn/TornApiClient.cs`, `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`, `.gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md`
  - Verify: test -s .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md && rg -n "## API Endpoints|## Torn Fetch Entry|## Extractor Fields|## Known Gaps" .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md

- [x] **T02: Author the modifier provenance taxonomy and confidence impact matrix** `est:1h`
  Produce the slice deliverable taxonomy matrix covering personal/faction/company evidence dimensions, required key scopes, expected Torn payload field candidates, known log categories/IDs (confirmed vs hypothesized), and confidence impact mapping used by upcoming scoring work. Link each matrix row to concrete anchors from T01 and call out unresolved dependency owners for faction/company data acquisition.
  - Files: `.gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md`, `.gsd/milestones/M002/slices/S01/S01-PLAN.md`, `.gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md`
  - Verify: test -s .gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md && rg -n "## Taxonomy Matrix|## Confidence Impact Mapping|## Key Scope Requirements|## Open Unknowns" .gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md

- [x] **T03: Add automated drift checks for taxonomy anchors** `est:35m`
  Add a deterministic verification script that validates S01 taxonomy completeness and anchor integrity: required sections exist, referenced endpoints still exist in API program, and mapped extractor field tokens still exist in reconstruction code.
  - Files: `scripts/verify-s01-taxonomy.sh`, `.gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md`, `src/HappyGymStats.Api/Program.cs`, `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`
  - Verify: bash scripts/verify-s01-taxonomy.sh

## Files Likely Touched

- src/HappyGymStats.Api/Program.cs
- src/HappyGymStats.Api/ImportService.cs
- src/HappyGymStats.Core/Torn/TornApiClient.cs
- src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs
- .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md
- .gsd/milestones/M002/slices/S01/S01-PLAN.md
- .gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md
- scripts/verify-s01-taxonomy.sh
