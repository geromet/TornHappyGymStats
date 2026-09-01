---
estimated_steps: 7
estimated_files: 6
skills_used: []
---

# T03: Wire deploy flow to production smoke

Why: Deploy scripts should not duplicate smoke logic, but they should make the canonical post-deploy verification obvious and optionally automatic.

Do:
1. Add a shared post-deploy hook or message that points to `scripts/verify/production-smoke.sh`.
2. Optionally support `DEPLOY_RUN_SMOKE=1` or similar to run the smoke script after all deploys.
3. Ensure individual deploy scripts do not claim full success when post-deploy smoke fails if the smoke hook is enabled.
4. Keep the smoke script read-only and separately executable.

Done when: deployment and smoke verification are connected without copy-pasted checks diverging.

## Inputs

- `.gsd/milestones/M003/slices/S05/S05-SUMMARY.md`
- `scripts/verify/production-smoke.sh`
- `scripts/deploy.sh`

## Expected Output

- `scripts/deploy.sh`
- `scripts/deploy-config.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`

## Verification

bash -n scripts/deploy.sh scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "production-smoke|DEPLOY_RUN_SMOKE|smoke" scripts/deploy*.sh

## Observability Impact

Signals added/changed: deploy completion includes smoke next-step or result.
How a future agent inspects this: deploy output and smoke output.
Failure state exposed: post-deploy assembled-stack failure after successful publish.
