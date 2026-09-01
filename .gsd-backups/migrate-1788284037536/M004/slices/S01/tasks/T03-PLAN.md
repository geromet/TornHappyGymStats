---
estimated_steps: 1
estimated_files: 2
skills_used: []
---

# T03: Added an auth-protected /my-stats page with claim-bound chart loading states and wired a locked My stats nav link.

Create new auth-protected `/my-stats` page that renders a point cloud like Home but sourced from GetMyStatsAsync, with empty/loading/error states. Update main nav menu to include My stats with lock icon indicator.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/PlayerAccount.razor`

## Expected Output

- `New MyStats page component`
- `Updated nav with auth-required My stats link`

## Verification

dotnet build src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj

## Observability Impact

UI conveys auth and data-load failures explicitly without exposing secrets.
