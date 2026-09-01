---
estimated_steps: 9
estimated_files: 3
skills_used: []
---

# T03: Install and validate AdminPanel nginx route

Why: Adding a local nginx file is not enough; setup/deploy needs to place it, validate config, and reload nginx safely.

Do:
1. Extend the appropriate setup script from S03 or create a dedicated admin nginx setup phase.
2. Stage and install the nginx config to the intended remote sites-available/sites-enabled or conf.d path.
3. Run `nginx -t` before reload.
4. Reload nginx only after validation passes.
5. Keep script idempotent and make route install optional/configurable if DNS is not ready.
6. Do not run the remote setup/install command without explicit user confirmation; local syntax/static verification is allowed.

Done when: AdminPanel route install follows the same safe pattern as service/sudoers setup, and the script clearly gates remote mutation.

## Inputs

- `.gsd/milestones/M003/slices/S03/tasks/T04-SUMMARY.md`
- `scripts/setup-adminpanel-server.sh`
- `infra/nginx-adminpanel.conf`

## Expected Output

- `scripts/setup-adminpanel-server.sh`
- `docs/DEPLOYMENT.md`

## Verification

bash -n scripts/setup-adminpanel-server.sh && rg -n "nginx -t|reload nginx|systemctl reload nginx|nginx-adminpanel|sites-enabled|conf.d|explicit user confirmation|remote setup" scripts/setup-adminpanel-server.sh docs/DEPLOYMENT.md

## Observability Impact

Signals added/changed: nginx validation/reload setup phase.
How a future agent inspects this: setup output and deployment docs.
Failure state exposed: failed config validation vs failed reload vs missing DNS.
