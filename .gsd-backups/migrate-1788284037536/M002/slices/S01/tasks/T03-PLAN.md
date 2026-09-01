---
estimated_steps: 1
estimated_files: 4
skills_used: []
---

# T03: Add automated drift checks for taxonomy anchors

Add a deterministic verification script that validates S01 taxonomy completeness and anchor integrity: required sections exist, referenced endpoints still exist in API program, and mapped extractor field tokens still exist in reconstruction code.

## Inputs

- `.gsd/milestones/M002/slices/S01/research/modifier-provenance-taxonomy.md`
- `src/HappyGymStats.Api/Program.cs`
- `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`

## Expected Output

- `scripts/verify-s01-taxonomy.sh`

## Verification

bash scripts/verify-s01-taxonomy.sh
