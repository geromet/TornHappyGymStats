---
estimated_steps: 1
estimated_files: 4
skills_used: []
---

# T01: Added authenticated claim-bound GET /api/v1/torn/surfaces/me and caller-scoped gym log retrieval with unauthorized handling for missing/invalid anonymous_id claims.

Implement authenticated `GET /api/v1/torn/surfaces/me` in API. Resolve caller anonymous_id claim, return 401 when claim missing/invalid, and project only caller gym rows into chart payload shape. Extend repository contracts/implementation as needed for caller-scoped gym cloud retrieval. Keep route claim-bound and do not accept PlayerID/user id inputs.

## Inputs

- `.gsd/workflows/features/260509-2-add-a-my-stats-page-to-the-blazor-projec/CONTEXT.md`
- `.gsd/workflows/features/260509-2-add-a-my-stats-page-to-the-blazor-projec/PLAN.md`
- `src/HappyGymStats.Api/Infrastructure/HappyGymStatsClaimsTransformer.cs`
- `src/HappyGymStats.Api/Controllers/GymTrainsController.cs`

## Expected Output

- `Compiled API changes with new endpoint contract`
- `Repository contract + implementation for caller-scoped gym log retrieval`

## Verification

dotnet build src/HappyGymStats.Api/HappyGymStats.Api.csproj && dotnet test --filter "FullyQualifiedName~Api|FullyQualifiedName~Identity|FullyQualifiedName~GymTrains"

## Observability Impact

Explicit unauthorized/forbidden responses for auth/mapping failures; no sensitive identity leakage in payload/logs.
