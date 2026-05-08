---
estimated_steps: 8
estimated_files: 3
skills_used: []
---

# T01: Define AdminPanel sudoers privilege boundary

Why: The local sudoers file mixes steady-state deploy commands with missing one-time setup needs; the bootstrap privilege model must be explicit before scripting it.

Do:
1. Audit current deploy commands in API/Blazor/AdminPanel scripts and current sudoers entries.
2. Define steady-state deploy permissions separately from one-time setup permissions.
3. Add only narrowly scoped commands needed for installing service/sudoers and enabling AdminPanel: e.g. systemctl daemon-reload/enable/start/status for the specific service, safe file install/copy, visudo validation, and optional nginx commands only if needed by S03.
4. Document which permissions are temporary/bootstrap-only versus permanent deploy permissions.
5. Avoid wildcard service names or broad shell permissions.

Done when: sudoers policy is explicit enough for a reviewer to see exactly what remote privileged operations S03 can perform.

## Inputs

- `infra/sudoers-happygymstats`
- `scripts/deploy-adminpanel.sh`
- `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md`

## Expected Output

- `infra/sudoers-happygymstats`
- `docs/DEPLOYMENT.md`

## Verification

rg -n "happygymstats-adminpanel|daemon-reload|enable|start|visudo|sudoers|NOPASSWD" infra/sudoers-happygymstats docs/DEPLOYMENT.md && ! rg -n "NOPASSWD: ALL|/bin/bash|/usr/bin/bash|sh -c" infra/sudoers-happygymstats

## Observability Impact

Signals added/changed: documented privileged command boundary.
How a future agent inspects this: read sudoers artifact and setup docs.
Failure state exposed: missing specific NOPASSWD command instead of generic sudo failure.
