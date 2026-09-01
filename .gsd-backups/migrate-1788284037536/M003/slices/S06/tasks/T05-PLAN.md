---
estimated_steps: 8
estimated_files: 6
skills_used: []
---

# T05: Add deployment script contract verifier

Why: S06 changes several scripts and needs a deterministic local verifier to catch drift without touching production.

Do:
1. Add a verifier that syntax-checks all deploy/setup/smoke scripts.
2. Assert no duplicate hardcoded SSH proxy/key literals remain outside shared config unless explicitly allowed.
3. Assert release/symlink activation tokens remain present in API/Blazor/Admin deploys.
4. Assert AdminPanel deploy references setup guidance for missing service.
5. Assert smoke hook/reference exists.

Done when: local script-contract verification can be run before any remote deploy.

## Inputs

- `scripts/deploy-config.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`
- `scripts/deploy-containers.sh`

## Expected Output

- `scripts/verify/s06-deploy-script-contract.sh`

## Verification

bash scripts/verify/s06-deploy-script-contract.sh

## Observability Impact

Signals added/changed: deploy script drift verifier.
How a future agent inspects this: run S06 verifier.
Failure state exposed: duplicated config, missing smoke hook, broken syntax.
