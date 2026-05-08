# M003 S04 Blocking Issues — Manual Resolution Guide

This runbook resolves the remaining blocker where S04 route/auth smoke fails because AdminPanel endpoints are unreachable in the runtime environment.

---

## 1) Confirm current failing state (baseline)

Run from repo root:

```bash
bash scripts/verify/s04-adminpanel-route.sh
```

Typical failure pattern when blocked:
- `ROUTE_HEALTH_LOOPBACK_FAIL ... 127.0.0.1:5048`
- `ROUTE_EXTERNAL_UNREACHABLE ... admin.geromet.com`
- `ROUTE_PROTECTED_UNREACHABLE ... admin.geromet.com/admin/api/v1/import-runs`

This confirms the blocker is runtime/environment reachability, not synthetic closure text.

---

## 2) Validate setup script behavior (safe, non-mutating)

```bash
bash -n scripts/setup-adminpanel-server.sh
bash scripts/setup-adminpanel-server.sh --help
bash scripts/setup-adminpanel-server.sh --dry-run
```

Expected: all commands succeed. `--dry-run` should exit before any remote mutation.

---

## 3) Bootstrap or repair AdminPanel nginx route on target host (manual mutation)

If DNS/TLS and host are ready, run:

```bash
DEPLOY_INSTALL_ADMIN_NGINX=1 \
bash scripts/setup-adminpanel-server.sh --execute --confirm-remote-setup
```

What this does:
- stages `infra/nginx-adminpanel.conf`
- installs/links nginx config
- runs `nginx -t`
- reloads nginx

If this fails, fix the reported host-side issue (sudo privilege, missing path, nginx/unit problem), then rerun.

---

## 4) Ensure AdminPanel service is actually up on loopback

On the target host, verify:
- `happygymstats-adminpanel` service is active
- service is listening on `127.0.0.1:5048`
- `http://127.0.0.1:5048/admin/health` returns 2xx

Use your standard systemctl/socket/curl checks.

---

## 5) Re-verify route + auth boundary

Run:

```bash
bash scripts/verify/s04-adminpanel-route.sh
```

Pass criteria:
- loopback health: 2xx
- external health: 2xx
- protected endpoint without auth: **401 or 403**

Interim local-only check (when external DNS/TLS is intentionally not ready):

```bash
ADMINPANEL_ROUTE_LOCAL_ONLY=1 bash scripts/verify/s04-adminpanel-route.sh
```

Note: local-only mode is not final closure evidence for external route/auth behavior.

---

## 6) Re-run production smoke contract checks

```bash
bash -n scripts/verify/production-smoke.sh
bash scripts/verify/s05-production-smoke-contract.sh
```

Then run smoke in the intended environment:

```bash
bash scripts/verify/production-smoke.sh
# or remote mode
SMOKE_MODE=remote bash scripts/verify/production-smoke.sh
```

---

## 7) Verify remediation evidence files exist

```bash
test -s .gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md \
&& test -s .gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md \
&& test -s .gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md \
&& test -s docs/m003-artifact-remediation-evidence.md
```

Expected: command exits 0.

---

## 8) Update S04 evidence after real environment pass

Once Step 5 passes in the target environment, update S04 closure evidence so the terminal state reflects successful route/auth smoke verification rather than temporary reachability failure.

---

## Notes

- This guide is focused on manual/operator steps for environment-level blockers.
- The repo now includes supporting contract artifacts used by recovery work:
  - `scripts/verify/s03-adminpanel-setup.sh`
  - `infra/sudoers-happygymstats`
  - `docs/m003-artifact-remediation-evidence.md`
