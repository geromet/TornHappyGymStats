# Codex Cloud for TornHappyGymStats

> **Capability snapshot — 2026-09-05; control-plane guidance updated 2026-09-06.** The capability measurements below describe the configured Torn Codex Cloud environment at the time they were observed. Current authority, access, handoff and scheduling guidance is maintained separately in this document. Live repository state, `AGENTS.md`, `docs/WORKING-AGREEMENT.md`, issue #140, current CI, and current OpenAI documentation win when any snapshot becomes stale.

## Purpose

Codex is TornHappyGymStats' primary implementation workhorse and the designated final PR reviewer/merger. The configured Codex Cloud environment gives that worker a remote repository checkout where it can edit code, compile/test it, exercise browser/render proof, and hand work back through GitHub without depending on Gerome's local machine.

Fleet/scout automations are not default-branch mergers. They may prepare work and, after the complete live readiness gate passes, request one deduplicated account-authored `@codex` final-review/merge handoff. The request itself is not evidence that Codex could or did merge; GitHub merged state and resulting default incorporation are the authority.

Codex Cloud is **not** a separate authority plane. Workers must use the same live coordination, branch ownership, evidence, security, outside-contributor and protection rules as every other lane. Credential availability does not bypass those rules.

The current verified capability classification is:

> **CAPABLE EXCEPT POSTGRES**

Codex Cloud can run Torn's ordinary source/build/test gate and real Chromium/render proof. The capability smoke test found that the Cloud sandbox could not run Docker containers, so genuine Testcontainers/PostgreSQL proof remains owned by GitHub CI until a fresh capability test proves otherwise.

## Environment lifecycle

The configured Cloud environment uses two preparation phases:

1. **Setup** — cacheable machine-level preparation such as OS tools, the repository-pinned .NET SDK infrastructure, Docker CLI/daemon binaries, Python/Playwright, Chromium, browser dependencies, and GitHub CLI/authentication setup.
2. **Maintenance** — runs after the requested branch is checked out when a cached environment is resumed. It reconciles the cache with that exact branch: reads `global.json`, activates/installs the required SDK, restores NuGet packages, repairs browser tooling when needed, and checks Docker binaries.

The current Codex UI states that the maintenance script has network access. Keep branch-dependent downloads/restores in maintenance where practical rather than broadening autonomous runtime access merely for dependency installation.

Important environment behavior:

- the selected branch/commit is checked out before agent work;
- cached environments can resume rather than rebuilding everything from scratch;
- setup and later task shells are separate shells, so important state must be persisted rather than relying only on `export` in setup;
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

The observed blocker is not Docker CLI installation and not a Torn test defect.

The 2026-09-05 smoke environment could initialize a constrained daemon such as:

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

and that daemon could answer `docker info`. However, real image registration failed at the host/container namespace boundary:

```text
failed to register layer: unshare: operation not permitted
```

The failure occurred before useful container execution. For the Torn PostgreSQL tier this meant:

- `hello-world` could not start;
- PostgreSQL could not start;
- Testcontainers' Ryuk image could not be registered;
- PostgreSQL readiness, mapped-port connectivity and application behavior were never reached.

The Testcontainers integration also expects ordinary container networking, mapped host ports and the default resource reaper. Even if image-layer registration becomes available later, the observed `--bridge=none` / disabled-iptables daemon may expose a second networking limitation; that layer was not proven either way.

### Evidence ownership

Until a fresh Cloud capability test proves genuine container execution, **do not spend normal Codex work runs repeatedly retrying Testcontainers as completion evidence**.

The required relational tier remains non-skippable in GitHub CI:

```bash
HAPPYGYMSTATS_REQUIRE_POSTGRES_INTEGRATION=1 \
HAPPYGYMSTATS_POSTGRES_START_TIMEOUT_SECONDS=300 \
bash scripts/verify/s07-postgres-integration.sh
```

A Codex worker may complete local source/build/browser proof and open/update a PR; GitHub CI must supply the missing genuine PostgreSQL/T4 evidence before relational work is considered proven.

## GitHub access and fleet coordination

Codex Cloud workers are fleet workers, not privileged bypasses.

### Saved GitHub authentication — prepared, new-worker execution not yet proven

Gerome has configured a GitHub token in the Codex environment **secrets** and supplied a complete replacement `torn-codex-setup.sh`; that script has passed Bash syntax validation. Installation plus successful authenticated execution in a newly created worker are **not yet evidenced**.

The supplied setup path:

- installs `gh`;
- accepts `CODEX_GITHUB_TOKEN`, then `GH_TOKEN`, then `GITHUB_TOKEN` in that priority order;
- authenticates `gh` through stdin during setup;
- configures Git to use the `gh` credential helper;
- adds a credential-free `origin` only if one is missing;
- reads Torn #140 as a setup check.

Environment secrets themselves disappear before the worker phase. The design deliberately persists `gh` credentials/config for the same OS user and `HOME`; depending on `gh`/keyring availability this may fall back to plaintext storage. Never request, print, dump, commit, echo into logs, or place the credential in a Git remote URL.

A new worker should begin by using the **saved** authentication rather than expecting the original secret environment variable to still exist:

```bash
gh api repos/geromet/TornHappyGymStats/issues/140 --jq '{number,title}'
```

Then read the live #140 body and all relevant recent comments plus the exact target/default/PR state. GitHub access requires worker network access to `api.github.com` and Git operations require `github.com`. If access fails, distinguish missing saved authentication from network denial from insufficient repository permission without exposing credentials. Public reads, a successful setup script, or a connector-published response are not authenticated-write proof.

Issue **#227** owns the remaining new-worker access proof. Before mutable work, a newly authenticated worker must demonstrate the normal two-phase lifecycle on a narrow diagnostic scope: create a #140 claim, reread and win by comment ID, then edit that **same** comment to `🔓 RELEASED`. Failure to create or edit ownership fails closed; do not substitute stale snapshots, Manager preclaims, Claude, or uncoordinated mutation.

### Mutable-work contract

Before mutable work a worker must:

1. read `AGENTS.md` and `docs/WORKING-AGREEMENT.md`;
2. read issue #140 body **and recent comments**, plus current target/PR/default state;
3. obey WIP/PR-pressure gates, drain/queue ordering, dependencies, branch ownership and outside-contributor boundaries;
4. acquire the exact overlapping scope using #140's current two-phase `🔒 CLAIM` protocol;
5. reread #140 immediately after claiming; earlier GitHub comment ID wins overlapping claims;
6. refresh once more immediately before the first conflicting mutation;
7. treat the observed remote branch head as a CAS token and stop/reconcile unexpected movement;
8. edit the **same claim comment** to canonical `🔓 RELEASED` when ownership ends.

Fleet/scout lanes must never merge any branch or PR into a repository default. Do not give Cloud workers production/deployment/Torn credentials merely to increase autonomy. Production/operator actions, interactive SSH/passkey work and Torn state changes remain outside normal agent authority.

## Codex final review / merge handoff

Gerome designates Codex as the final PR reviewer/merger; Claude is no longer the merger.

Torn also performs additional automatic Codex scans when PRs are created. A scan request, task acceptance, reaction, silence, or earlier-head result is not current-head approval. Before a final-merge request, inspect the actual current-head scan/review result, required CI/evidence tiers, review threads, linked acceptance/dependencies, PR base/head and live #140 ownership. If a required scan has not visibly completed, classify the PR `WAITING FOR SCAN` with the exact missing result rather than treating silence as a pass.

When the full gate passes, the normal handoff owner may make **one** deduplicated account-authored PR conversation request beginning `@codex`, with deterministic marker:

```text
codex-final-merge:<owner/repo>:<pr>:<head-sha>
```

The handoff transfers ownership only after its own exact #140 claim is released. Codex must then acquire its own exact scope, recheck head/base/checks/scans/threads/dependencies, and merge only if permissions and repository protections permit. Never bypass protections. A changed head requires fresh proof and a new request only after prior worker ownership is resolved.

An `@codex` request is not merge evidence. Follow through by verifying GitHub's merged state and resulting default incorporation. If Codex cannot authenticate, claim, or merge, record the concrete access blocker once on the canonical access/human-attention surface; do not loop mentions, fall back to Claude, or let a fleet automation perform the merge.

## Trigger evidence and deferred scheduling

The trigger question is no longer open-ended research:

- PR **#233 is merged** and contains the bounded manual-only Actions diagnostic workflow.
- Both an account-authored mention and an actual `github-actions[bot]` mention have been observed to start Codex Cloud workers.
- Issue **#232 is closed completed** as a bounded diagnostic outcome.
- The same-token NO-OP is verified in Actions run `33998009222`, job `101392610776`: token `torn-cloud-smoke-0006-020` was already present on #200 as comment `5555426288`, and the compose/post path was skipped rather than posting a duplicate trigger.

These observations prove the bounded trigger/dedup behavior. They do **not** prove authenticated worker mutation, final result publication/account attribution, merge permission, or a safe recurring scheduler.

Issue **#227** remains open for the exact remaining access/result/account proof and for recurring-dispatch requirements **only when Gerome reopens that scope**. Actions scheduling is currently deferred. Do not:

- re-run broad trigger research or the completed #232 smoke merely because an older comment called a rerun pending;
- enable cron/schedules or dispatch repeated blocked workers;
- add/change credentials or settings from fleet automation;
- silently substitute `openai/codex-action` / separately metered API execution;
- describe bot-author filtering as the blocker;
- claim that adding the setup script or reading a public issue proves worker write/merge access.

When Gerome later reopens recurring scheduling, #227 must still cover pre-dispatch live LOCK/WIP no-op, durable task identity, worker-lifecycle exclusion, ambiguous-submission reconciliation, explicit billing/account attribution and a kill switch before any schedule is relied upon.

Official related references:

- Cloud environment: <https://learn.chatgpt.com/docs/environments/cloud-environment>
- GitHub Action: <https://learn.chatgpt.com/docs/github-action>
- Automations: <https://learn.chatgpt.com/docs/automations?surface=app>
- Workspace Agent trigger API research lead: <https://learn.chatgpt.com/workspace-agents/trigger-runs>

## Re-test triggers

Repeat the Cloud capability test when a material platform/environment change could invalidate the capability snapshot, especially if:

- Codex Cloud gains documented container/nested-virtualization support;
- Docker image execution starts succeeding;
- the environment/base image changes materially;
- Torn changes its required SDK/browser/test topology.

Re-test the GitHub access boundary separately when the saved-credential setup is deployed to a new worker or GitHub/network permissions change. Do not infer access from the older unauthenticated worker result after a new setup, and do not infer successful write/merge authority from read access alone.

Do not re-run expensive Docker/Postgres experiments every ordinary coding task without a material signal.

## Current control-plane references

- #140 — canonical live fleet LOCK / WIP / handoff authority.
- #218 — dated completion/consolidation aid; live code and GitHub state win.
- #223 — canonical tracked-Markdown remediation inventory.
- #227 — current Codex Cloud authenticated-worker/result/account proof; recurring scheduling is deferred until Gerome reopens it.
- #232 — **closed completed** bounded Actions-bot trigger/dedup diagnostic.
- #233 — **merged** manual-only trigger-smoke workflow and contract tests.
- `AGENTS.md` — repository agent entrypoint.
- `docs/WORKING-AGREEMENT.md` — cross-agent safety/evidence contract.
- `scripts/verify/manifest.tsv` — verifier graph.
- `scripts/verify/build-and-test.sh` — canonical ordinary source/build/test gate.
