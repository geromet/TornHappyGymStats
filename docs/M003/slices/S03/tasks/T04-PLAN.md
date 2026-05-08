---
estimated_steps: 8
estimated_files: 3
skills_used: []
---

# T04: Verify AdminPanel loopback health after setup

Why: Setup is only useful if it proves the service is alive on the loopback port that S04/nginx will consume.

Do:
1. Add post-setup `systemctl status` or `is-active` check for AdminPanel.
2. Add loopback `/admin/health` check against `http://127.0.0.1:5048/admin/health` on the server.
3. Distinguish service inactive from port unavailable from HTTP non-2xx.
4. Add a local verifier for script syntax/static tokens and service file consistency.
5. Document that running the remote setup mutates server state and requires explicit user confirmation when executed by an agent.

Done when: S03 has a local verifier and remote setup would produce clear health proof after installation.

## Inputs

- `src/HappyGymStats.AdminPanel/Controllers/AdminHealthController.cs`
- `infra/happygymstats-adminpanel.service`
- `scripts/setup-adminpanel-server.sh`

## Expected Output

- `scripts/setup-adminpanel-server.sh`
- `scripts/verify/s03-adminpanel-setup.sh`
- `docs/DEPLOYMENT.md`

## Verification

bash scripts/verify/s03-adminpanel-setup.sh

## Observability Impact

Signals added/changed: AdminPanel setup health check.
How a future agent inspects this: setup output or S03 verifier.
Failure state exposed: inactive service, failed loopback health, missing route.
