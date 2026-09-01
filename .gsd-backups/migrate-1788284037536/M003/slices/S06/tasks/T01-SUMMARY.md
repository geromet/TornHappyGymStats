---
id: T01
parent: S06
milestone: M003
key_files:
  - scripts/deploy-config.sh
  - scripts/deploy-containers.sh
key_decisions:
  - (none)
duration: 
verification_result: passed
completed_at: 2026-05-07T19:24:57.980Z
blocker_discovered: false
---

# T01: Added shared deploy config helpers and implemented a container deploy script that uses them with explicit non-secret precondition help.

**Added shared deploy config helpers and implemented a container deploy script that uses them with explicit non-secret precondition help.**

## What Happened

Executed T01 against local reality where `scripts/deploy-config.sh` and `scripts/deploy-containers.sh` were missing. Created `scripts/deploy-config.sh` as a source-only shared config module that loads `.env.deploy`, centralizes SSH/sudo defaults, exposes shared SSH helpers (`deploy_ssh_tty`, `deploy_ssh_pipe`), and maps smoke SSH defaults from deploy defaults. Created `scripts/deploy-containers.sh` to source shared config, remove hardcoded SSH construction, upload compose config, validate remote docker/env preconditions, and execute non-interactive `docker compose pull/up` using configurable variables. Implemented `--help` output that documents required local/remote preconditions by variable name and state only (no secret values). Tightened help redaction to show `<set|unset>` for key/proxy rather than raw values.

## Verification

Ran the task-specified verification contract end-to-end: shell syntax check passed, `--help` executed successfully and showed machine-checkable preconditions and shared connection summary, and regex guard confirmed container deploy script no longer contains hardcoded legacy SSH strings.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash -n scripts/deploy-containers.sh` | 0 | ✅ pass | 2ms |
| 2 | `bash scripts/deploy-containers.sh --help` | 0 | ✅ pass | 7ms |
| 3 | `! rg -n "id_token2_bio3_hetzner|cloudflared access ssh|anon@ssh\.geromet\.com" scripts/deploy-containers.sh` | 0 | ✅ pass | 2ms |

## Deviations

Task plan referenced files that did not exist in this checkout; implemented the intended artifacts at the planned paths while preserving deploy behavior through configurable defaults.

## Known Issues

`infra/docker-compose.yml` is referenced by default in the new container deploy script but is not present in this checkout; actual deploy requires providing that file or overriding `DEPLOY_CONTAINERS_LOCAL_COMPOSE_FILE`.

## Files Created/Modified

- `scripts/deploy-config.sh`
- `scripts/deploy-containers.sh`
