---
estimated_steps: 8
estimated_files: 11
skills_used: []
---

# T01: Document .NET SDK and runtime contract

Why: The solution targets `net10.0` and production deploys self-contained apps; future agents need a clear runtime/SDK contract before changing scripts.

Do:
1. Inspect project target frameworks, global/tool config, deploy publish runtime, and server smoke assumptions.
2. Document the required local SDK, publish target runtime, and whether the server needs shared runtime for self-contained deploys.
3. Decide whether to add `global.json` or an equivalent version contract.
4. Avoid changing framework versions unless explicitly necessary.
5. Keep verification scoped to intended contract files, not the whole repository; a token appearing somewhere in source code does not prove docs are current.

Done when: runtime expectations are discoverable before build/deploy, and required docs tokens are verified without masked failures.

## Inputs

- `HappyGymStats.sln`
- `dotnet-tools.json`
- `src/HappyGymStats.Api/HappyGymStats.Api.csproj`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`
- `src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj`

## Expected Output

- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`
- `global.json`

## Verification

dotnet --version && rg -n "net10.0|SDK|runtime|linux-x64|self-contained" docs/SETUP.md docs/DEPLOYMENT.md

## Observability Impact

Signals added/changed: documented SDK/runtime expectations.
How a future agent inspects this: docs and optional global.json.
Failure state exposed: missing SDK/runtime before deploy.
