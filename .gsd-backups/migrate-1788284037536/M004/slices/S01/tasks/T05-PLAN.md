---
estimated_steps: 1
estimated_files: 4
skills_used: []
---

# T05: Fix verification drift and rerun slice gates

Resolve the current slice-level verification failures in an execution-capable unit. Update Blazor DTO/test expectations so SurfacesDatasetMetaDto supports the latest-surface provenance diagnostics contract, update stale HappyGymStatsDbContextTests to the current UserLogEntries/ModifierProvenance schema, ensure API auth tests include the Roles.User claim required by /api/v1/torn/surfaces/me, then rerun all slice gates. Do not weaken the claim-bound /surfaces/me behavior or add PlayerID/user id inputs.

## Inputs

- `.gsd/milestones/M004/slices/S01/S01-PLAN.md`
- `.gsd/milestones/M004/slices/S01/tasks/T01-SUMMARY.md`
- `.gsd/milestones/M004/slices/S01/tasks/T02-SUMMARY.md`
- `.gsd/milestones/M004/slices/S01/tasks/T03-SUMMARY.md`
- `.gsd/milestones/M004/slices/S01/tasks/T04-SUMMARY.md`

## Expected Output

- `All three slice verification commands pass`
- `T05-SUMMARY.md records verification evidence`
- `S01 can then be completed by a complete-slice unit`

## Verification

dotnet build HappyGymStats.sln && dotnet test && scripts/verify/s02-blazor-api-boundary.sh
