# Codex Cloud for TornHappyGymStats

> **Current capability snapshot — 2026-09-05.** This is operational guidance for the configured Torn Codex Cloud environment, not a permanent statement about the Codex platform. Live repository state, `AGENTS.md`, `docs/WORKING-AGREEMENT.md`, issue #140, current CI, and current OpenAI documentation win when this snapshot becomes stale.

## Purpose

Codex Cloud is an additional remote implementation/proof worker for TornHappyGymStats. The environment is scoped to this repository and is intended to let a remote agent check out a branch, edit code, compile/test it, exercise browser/render proof, and hand work back through GitHub without depending on Gerome's local machine.

It is **not** a separate authority plane. Codex workers must use the same live coordination, branch ownership, evidence, security, and no-default-merge rules as every other fleet lane.

The current verified classification is:

> **CAPABLE EXCEPT POSTGRES**

Codex Cloud can run Torn's ordinary source/build/test gate and real Chromium/render proof. The current Cloud sandbox cannot run Docker containers, so genuine Testcontainers/PostgreSQL proof remains owned by GitHub CI.

## Environment lifecycle

The configured Cloud environment uses two preparation phases:

1. **Setup** — cacheable machine-level preparation such as OS tools, the repository-pinned .NET SDK infrastructure, Docker CLI/daemon binaries, Python/Playwright, Chromium, and browser dependencies.
2. **Maintenance** — runs after the requested branch is checked out when a cached environment is resumed. It reconciles the cache with that exact branch: reads `global.json`, activates/installs the required SDK, restores NuGet packages, repairs browser tooling when needed, and checks Docker binaries.

The current Codex UI states that the maintenance script has network access. Keep branch-dependent downloads/restores in maintenance where practical rather than broadening the autonomous agent's runtime network access merely for dependency installation.

Important environment behavior:

- the selected branch/commit is checked out before agent work;
- cached environments can resume rather than rebuilding everything from scratch;
- setup and later task shells are separate shells, so important values must be made persistent rather than relying only on `export` in setup;
- `global.json` is the source of truth for the active SDK; do not hard-code a stale SDK assumption into task prompts;
- `AGENTS.md` remains the repository entrypoint for safety and current verification commands;
- actual acceptance tests belong **after** the worker's code changes, not in setup/maintenance.

Official environment reference: <https://learn.chatgpt.com/docs/environments/cloud-environment>

## Verified capability smoke test

The capability test ran from a clean checkout at:

- default baseline: `main@f6d686c9706ac2657d9fe30455a8547211993611`;
- Cloud task branch: `work`;
- working directory: `/workspace/TornHappyGymStats`;
- tracked working tree: clean before and after verification.

| Capability | Result | Evidence |
| --- | --- | --- |
| Branch-specific .NET SDK | **PASS** | Active SDK `10.0.106`, exactly matching `global.json`. |
| Canonical `build-and-test.sh` | **PASS** | Exit 0; 14 required verifiers; build 0 warnings / 0 errors; hermetic suite 564 passed / 0 failed / 0 skipped. |
| Playwright/Chromium prerequisites | **PASS** | `.venv` Playwright, Chromium and the screenshot driver present. |
| Browser crypto regression | **PASS** | `.venv/bin/python scripts/verify/browser-crypto-regression.py` exited 0. |
| Rendered Blazor harness | **PASS** | API + Blazor host started and `/war` generated six phone/tablet/desktop light/dark screenshots. |
| Docker CLI + `dockerd` binary | **PASS** | Docker 29.1.3 available. |
| Docker API daemon | **PASS, constrained** | A temporary `vfs`, bridge-less, iptables-disabled daemon reached `docker info`. |
| Basic Docker container execution | **FAIL — sandbox** | `docker run --rm hello-world` failed registering its downloaded layer with `unshare: operation not permitted`. |
| PostgreSQL image execution | **FAIL — sandbox** | `postgres:17-alpine` hit the same image-layer registration failure before container startup. |
| Required Testcontainers/PostgreSQL verifier | **FAIL — sandbox** | 28 selected tests, 28 failed / 0 passed / 0 skipped; Testcontainers surfaced Ryuk image absence downstream of the same layer-registration failure. |
| Repository cleanliness | **PASS** | Final `git status --short` remained empty. |

### Browser note

The `/war` screenshot run completed successfully but logged external-resource errors including `ERR_TUNNEL_CONNECTION_FAILED` and `ERR_CERT_AUTHORITY_INVALID`. They did not prevent the local API/frontend from starting or any of the six screenshots from rendering. Treat similar warnings according to the surface under test; do not automatically treat an unrelated external-resource warning as a rendered-app failure, but do inspect task-relevant console errors.

## Normal Codex verification ladder

For ordinary implementation work, use the repository's current canonical commands rather than reproducing a stale command list from this document.

The present baseline is:

```bash
bash scripts/verify/build-and-test.sh
```

For browser/UX/crypto work as relevant:

```bash
bash scripts/screenshot-board.sh --check
.venv/bin/python scripts/verify/browser-crypto-regression.py
bash scripts/screenshot-board.sh --route <relevant-route>
```

Rendered work should inspect the produced artifacts/states rather than treating command exit alone as visual proof.

Do not weaken, mock, skip, or replace a repository verifier merely because the Cloud sandbox cannot provide one tier.

## PostgreSQL / Docker boundary

The current blocker is not Docker CLI installation and not a Torn test defect.

Codex Cloud can initialize a constrained daemon such as:

```bash
dockerd \
  --host=unix:///var/run/docker.sock \
  --data-root=/tmp/codex-docker-data \
  --exec-root=/tmp/codex-docker-exec \
  --pidfile=/tmp/codex-dockerd.pid \
  --storage-driver=vfs \
  --iptables=false \
  --ip6tables=false \
  --bridge=none
```

and that daemon can answer `docker info`. However, real image registration fails at the host/container namespace boundary:

```text
failed to register layer: unshare: operation not permitted
```

The failure occurs before useful container execution. For the Torn PostgreSQL tier this means:

- `hello-world` cannot start;
- PostgreSQL cannot start;
- Testcontainers' Ryuk image cannot be registered;
- PostgreSQL readiness, mapped-port connectivity and application behavior are never reached.

The Testcontainers integration also expects ordinary container networking, mapped host ports and the default resource reaper. Even if image-layer registration becomes available later, the current `--bridge=none` / disabled-iptables daemon may expose a second networking limitation; that layer has not yet been proven either way.

### Evidence ownership

Until a fresh Cloud capability test proves genuine container execution, **do not spend normal Codex work runs repeatedly retrying Testcontainers as completion evidence**.

The required relational tier remains non-skippable in GitHub CI:

```bash
HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION=1 \
HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS=300 \
bash scripts/verify/s07-postgres-integration.sh
```

A Codex worker may complete local source/build/browser proof and open/update a PR; GitHub CI must supply the missing genuine PostgreSQL/T4 evidence before the work is considered relationally proven.

## GitHub and fleet coordination

Codex Cloud workers are fleet workers, not privileged bypasses.

Before mutable work they must:

1. read `AGENTS.md` and `docs/WORKING-AGREEMENT.md`;
2. read issue #140 body **and recent comments**, plus current target/PR/default state;
3. obey WIP/PR-pressure gates, drain/queue ordering, dependencies, branch ownership and outside-contributor boundaries;
4. acquire the exact overlapping scope using #140's current two-phase `🔒 CLAIM` protocol;
5. reread #140 immediately after claiming; earlier GitHub comment ID wins overlapping claims;
6. refresh once more immediately before the first conflicting mutation;
7. treat the observed remote branch head as a CAS token and stop/reconcile unexpected movement;
8. edit the **same claim comment** to canonical `🔓 RELEASED` when ownership ends;
9. **never merge any branch or PR into `main` or another repository default branch**.

A separate Codex GitHub-plugin smoke test has been prepared to prove that the Cloud worker can create a #140 claim comment and edit that exact comment back to RELEASED. Until that smoke test actually passes, do not assume direct LOCK mutation works merely because the plugin is installed; a Manager/preclaim handoff remains the safe fallback.

Do not give Cloud workers production/deployment/Torn credentials simply to increase autonomy. Production/operator actions, interactive SSH/passkey work and Torn state changes remain outside normal agent authority.

## Scheduling / unattended dispatch

There is currently **no verified recurring scheduler directly attached to this configured Codex Cloud code environment** in the Cloud web setup used for Torn.

Related OpenAI capabilities must not be conflated:

- Codex/ChatGPT automations can schedule work, but project-scoped desktop code automations and web/plugin automations have different execution models.
- OpenAI's official `openai/codex-action@v1` runs `codex exec` on a GitHub Actions runner and requires an `OPENAI_API_KEY`. That is not proof that it launches this configured subscription-backed Cloud environment.
- OpenAI exposes remote trigger APIs for some workspace-agent surfaces, but equivalence to this configured Codex Cloud environment has not been proven.

Issue **#227** is the canonical work package for investigating a low-cost GitHub Actions `schedule` dispatcher. The intended architecture is:

```text
GitHub Actions cron / workflow_dispatch
            ↓
small coordination + trigger dispatcher
            ↓
configured Torn Codex Cloud worker
            ↓
AGENTS.md → #140 claim → work → local proof → PR
            ↓
GitHub CI supplies full proof, including PostgreSQL
            ↓
Codex releases the same #140 claim
```

The dispatcher must not silently switch from subscription/Codex allowance to separately metered API usage. Billing path, idempotency, concurrency, WIP-gate no-op behavior and a kill switch are acceptance criteria in #227.

Official related references:

- Cloud environment: <https://learn.chatgpt.com/docs/environments/cloud-environment>
- GitHub Action: <https://learn.chatgpt.com/docs/github-action>
- Automations: <https://learn.chatgpt.com/docs/automations?surface=app>
- Workspace Agent trigger API research lead: <https://learn.chatgpt.com/workspace-agents/trigger-runs>

## Re-test triggers

Repeat the Cloud capability test when a material platform/environment change could invalidate this snapshot, especially if:

- Codex Cloud gains documented container/nested-virtualization support;
- Docker image execution starts succeeding;
- the environment/base image changes materially;
- Torn changes its required SDK/browser/test topology;
- the GitHub plugin/Cloud trigger surfaces materially change.

Do not re-run expensive Docker/Postgres experiments every ordinary coding task without such a signal.

## Current control-plane references

- #140 — canonical live fleet LOCK / WIP authority.
- #218 — completion/consolidation map.
- #227 — scheduled Codex Cloud dispatcher work package.
- `AGENTS.md` — repository agent entrypoint.
- `docs/WORKING-AGREEMENT.md` — cross-agent safety/evidence contract.
- `scripts/verify/manifest.tsv` — verifier graph.
- `scripts/verify/build-and-test.sh` — canonical ordinary source/build/test gate.
