---
estimated_steps: 1
estimated_files: 5
skills_used: []
---

# T01: Inventory current endpoint and extraction anchors for modifier-bearing evidence

Build a grounded inventory of current API route contracts, Torn log fetch entrypoint, and event extractor field heuristics. Write a compact discovery artifact listing concrete anchors and known gaps for personal/faction/company modifier evidence, with assumptions made explicit where Torn-side payloads are not yet represented in local models.

## Inputs

- `src/HappyGymStats.Api/Program.cs`
- `src/HappyGymStats.Api/ImportService.cs`
- `src/HappyGymStats.Core/Torn/TornApiClient.cs`
- `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`

## Expected Output

- `.gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md`

## Verification

test -s .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md && rg -n "## API Endpoints|## Torn Fetch Entry|## Extractor Fields|## Known Gaps" .gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md
