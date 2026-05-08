---
estimated_steps: 7
estimated_files: 5
skills_used: []
---

# T02: Add shared deploy preconditions

Why: Each deploy script should fail before publishing when required files, commands, privileges, or setup state are missing.

Do:
1. Add shared precondition helpers where practical: required local files, required commands, remote service existence, remote root writability/activation permissions, and known one-time setup prerequisites.
2. Apply helpers to backend, frontend, and AdminPanel deploys without changing release/symlink activation semantics.
3. Make AdminPanel deploy detect missing service/setup and point to the S03 setup script rather than failing at restart with a generic systemctl error.
4. Avoid broad remote writes in prechecks.

Done when: normal deploy scripts clearly distinguish missing setup from publish/deploy failures.

## Inputs

- `.gsd/milestones/M003/slices/S01/S01-SUMMARY.md`
- `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`

## Expected Output

- `scripts/deploy-config.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`

## Verification

bash -n scripts/deploy-config.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "precheck|precondition|required|setup-adminpanel-server|is-active|systemctl status" scripts/deploy-*.sh scripts/deploy-config.sh

## Observability Impact

Signals added/changed: precondition phase in deploy output.
How a future agent inspects this: run deploy with `--help` or precheck mode if added.
Failure state exposed: missing service/setup/privilege/local file before publish.
