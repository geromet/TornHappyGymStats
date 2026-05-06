---
estimated_steps: 9
estimated_files: 11
skills_used: []
---

# T03: Pin or document package restore policy

Why: Loose package references like `MudBlazor` `8.*` can change restores over time and make deploy builds non-reproducible. The policy should be mechanically checked, not hidden behind `|| true`.

Do:
1. Inventory package versions across concrete project files.
2. Pin loose versions where practical, or document why a floating version remains.
3. Decide whether to enable lock files (`packages.lock.json`, per-project lockfiles, or no lockfiles) for restore reproducibility.
4. If lock files are introduced, update restore/build guidance and verify CI/local behavior.
5. Do not bundle unrelated dependency upgrades.
6. Add an explicit verifier script for package restore policy. If floating versions remain, the verifier must require a matching documented allowlist/justification; it must not silently pass.

Done when: package restore behavior is intentional, documented, and verified by `scripts/verify/s09-package-restore-policy.sh`.

## Inputs

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/HappyGymStats.Blazor.Client.csproj`
- `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`

## Expected Output

- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/HappyGymStats.Blazor.Client.csproj`
- `docs/SETUP.md`
- `scripts/verify/s09-package-restore-policy.sh`

## Verification

bash scripts/verify/s09-package-restore-policy.sh

## Observability Impact

Signals added/changed: package restore policy is visible in project files/docs and enforced by a verifier.
How a future agent inspects this: run package policy verifier and inspect restore logs.
Failure state exposed: floating package drift, missing allowlist justification, lockfile mismatch, restore failure.
