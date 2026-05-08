---
estimated_steps: 7
estimated_files: 4
skills_used: []
---

# T04: Add runtime reproducibility verifier

Why: The runtime/package contract needs a single local verifier that can be run before deployment.

Do:
1. Add a S09 verifier that checks SDK version, target frameworks, absence/justification of floating versions through the package policy verifier, restore, build, and runtime preflight tokens.
2. Include helpful failure messages.
3. Keep it independent from production secrets and remote access.
4. Do not mask required failures with `|| true`; optional environment checks must be reported as warnings inside the script.

Done when: runtime/package reproducibility has deterministic local proof.

## Inputs

- `docs/SETUP.md`
- `docs/DEPLOYMENT.md`
- `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`
- `scripts/verify/s09-package-restore-policy.sh`

## Expected Output

- `scripts/verify/s09-runtime-reproducibility.sh`

## Verification

bash scripts/verify/s09-runtime-reproducibility.sh

## Observability Impact

Signals added/changed: reproducibility verifier.
How a future agent inspects this: run S09 verifier.
Failure state exposed: missing docs, loose versions without justification, failed restore/build, missing runtime preflight tokens.
