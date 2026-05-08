---
estimated_steps: 9
estimated_files: 3
skills_used: []
---

# T02: Create AdminPanel systemd setup script

Why: AdminPanel deploy assumes the service exists, but no script installs the service file or enables it.

Do:
1. Create an idempotent setup script that uses shared deploy config.
2. Copy or rsync `infra/happygymstats-adminpanel.service` to a remote staging path, then install to `/etc/systemd/system/happygymstats-adminpanel.service` with privileged command(s).
3. Run `systemctl daemon-reload`.
4. Enable and start/restart the AdminPanel service.
5. Make every remote mutation phase named and fail-fast.
6. Include dry-run/help output that states exactly what will change.

Done when: the script can be read and syntax-checked as the canonical way to install AdminPanel systemd prerequisites.

## Inputs

- `scripts/deploy-config.sh`
- `infra/happygymstats-adminpanel.service`
- `scripts/deploy-adminpanel.sh`

## Expected Output

- `scripts/setup-adminpanel-server.sh`

## Verification

bash -n scripts/setup-adminpanel-server.sh && bash scripts/setup-adminpanel-server.sh --help && rg -n "daemon-reload|enable|happygymstats-adminpanel.service|/etc/systemd/system|systemctl" scripts/setup-adminpanel-server.sh

## Observability Impact

Signals added/changed: named setup phases for service install, daemon reload, enable/start.
How a future agent inspects this: run `bash scripts/setup-adminpanel-server.sh --help` or read phase output.
Failure state exposed: failed upload/install/reload/enable/start.
