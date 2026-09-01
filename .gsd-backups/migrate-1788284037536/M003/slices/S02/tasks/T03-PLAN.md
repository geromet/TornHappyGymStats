---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T03: Render actionable Blazor failure states

Why: The UI should translate classified service failures into operator-usable messages without exposing implementation noise or secrets.

Do:
1. Update `Home.razor` load and import catch paths to render category-specific messages.
2. Add structured logging fields for category, endpoint, and status code where available.
3. Ensure no API key is included in log scope/message/exception data.
4. Preserve current no-data message when surfaces latest returns 404.
5. Keep user-facing wording short but actionable: e.g. API unavailable, bad gateway from reverse proxy, no cached surfaces yet, import rejected.

Done when: the reported 502 messages are replaced by explicit bad-gateway/API-boundary diagnostics.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`
- `.gsd/milestones/M003/slices/S02/tasks/T02-SUMMARY.md`

## Expected Output

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor`

## Verification

dotnet build && rg -n "Failed to load surfaces data|Bad Gateway|bad gateway|endpoint|status|ApiFailure" src/HappyGymStats.Blazor/HappyGymStats.Blazor/Components/Pages/Home.razor

## Observability Impact

Signals added/changed: structured log fields and explicit UI alert categories.
How a future agent inspects this: browser UI alert and application logs.
Failure state exposed: safe category/status/endpoint rather than raw exception text.
