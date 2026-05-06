# M003 Handoff — Production deploy recovery and refactor hardening

## Current state

Milestone `M003` is planned but not executed.

- Milestone title: **Production deploy recovery and refactor hardening**
- Status: active
- Slices: 9 pending
- Tasks: 40 pending
- No implementation tasks have started.
- No task or slice has been marked complete.

The full GSD planning bundle has been copied into `docs/M003/` for agents that cannot read `.gsd/`.

## Source artifacts

Read these first:

1. `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md` — original audit report and findings.
2. `docs/M003/M003-ROADMAP.md` — milestone roadmap and dependency graph.
3. `docs/M003/slices/S01/S01-PLAN.md` — first executable slice.
4. `docs/M003/slices/S01/tasks/T01-PLAN.md` — first concrete task.

The canonical GSD artifacts remain under `.gsd/milestones/M003/`. The `docs/M003/` copy is for visibility outside this sandbox.

## What happened in this session

- Copied the audit report from `.gsd/audits/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md` to `docs/2026-05-06-181943-we-did-a-big-refactor-update-your-knowle.md`.
- Created milestone `M003` from the audit.
- Planned all 9 slices and all 40 tasks using GSD planning tools.
- Reviewed the generated plans for execution quality.
- Patched two plan defects before handoff:
  - `S04/T02` no longer masks nginx verification failure with `|| true`; it now requires `scripts/verify/s04-adminpanel-nginx-config.sh`.
  - `S09` no longer masks required runtime/package verification with `|| true`; package policy now goes through explicit verifier scripts.
- Added explicit remote-mutation confirmation language to the AdminPanel nginx install task.

## Next action

Start execution at:

`docs/M003/slices/S01/tasks/T01-PLAN.md`

Canonical GSD path:

`.gsd/milestones/M003/slices/S01/tasks/T01-PLAN.md`

Task: **Declare API production environment contract**.

Concrete first step:

1. Read:
   - `infra/happygymstats-api.service`
   - `scripts/deploy-backend.sh`
   - `scripts/deploy-config.sh`
   - `docs/DEPLOYMENT.md`
   - `src/HappyGymStats.Api/Infrastructure/AppConfiguration.cs`
   - `src/HappyGymStats.Api/appsettings.json`
2. Define the production env contract without committing secret values:
   - `HAPPYGYMSTATS_CONNECTION_STRING` or `ConnectionStrings__HappyGymStats`
   - `ProvisionalToken__SigningKey`
   - `HAPPYGYMSTATS_SURFACES_CACHE_DIR`
   - `ASPNETCORE_ENVIRONMENT`
   - `ASPNETCORE_URLS`
3. Keep the task conservative: update service/deploy/docs contract first; only change API code if missing-env diagnostics are actually inadequate.

## Why this next

The reported user-visible bug is Blazor receiving `502 Bad Gateway` from surfaces/import calls. The most likely cause is not Blazor rendering code; it is the production boundary between nginx, the API service, Postgres/config, and the surfaces cache. `S01` makes that boundary explicit and verifiable before `S02` changes Blazor behavior.

## Important constraints

- Do not print, commit, or log secret values.
- Do not ask the user to manually edit `.env` or paste secrets. If a command needs secrets, use secure env collection where available.
- Do not run outward-facing remote mutation commands without explicit user confirmation. This especially applies to:
  - installing sudoers files,
  - installing systemd units,
  - enabling/restarting remote services,
  - installing/reloading nginx routes,
  - pushing to remotes or touching external services.
- Local syntax/static verification is allowed.
- When running `dotnet run` verification scripts with fixed `ASPNETCORE_URLS`, use `--no-launch-profile`; launch profiles can override the URL.

## Known plan caveats already handled

- `S04/T02` originally had an nginx command ending in `|| true`; this was patched out.
- `S09/T01` and `S09/T03` originally had masked checks and broad globs; these were patched to use direct docs checks, concrete likely files, and explicit verifier scripts.

## Open threads

- The docs still likely contain stale SQLite/static-frontend language until `S08` executes.
- The production server may currently have only a narrow NOPASSWD `rsync` permission installed. `S03` plans a safe bootstrap path but must not be run remotely without confirmation.
- The actual 502 root cause has not been reproduced in this session; the audit inferred likely failure classes from code/config.
- `docs/M003/` is a copy. If GSD plans are later changed, refresh the docs copy.

## Do not

- Do not skip straight to Blazor UI changes before `S01` establishes API health and config checks.
- Do not hide verification failures with `|| true` in scripts.
- Do not broaden sudoers permissions with wildcards or shell access.
- Do not treat SQLite-only tests as proof of production Postgres startup behavior.
- Do not rely on checked-in placeholder production settings such as `changeme` connection strings or signing keys.
