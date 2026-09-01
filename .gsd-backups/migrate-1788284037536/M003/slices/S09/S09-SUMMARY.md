---
id: S09
parent: M003
milestone: M003
provides:
  - Documented and pinned .NET SDK/runtime contract for current net8.0 project reality.
  - Mechanical package restore reproducibility policy and verifier.
  - Runtime preflight and executable validation wiring for deploy/smoke hardening.
  - Single local S09 verifier suitable for predeploy and future CI use.
requires:
  - slice: S05
    provides: Canonical production smoke script and runtime assumptions that S09 extended with runtime-preflight markers.
  - slice: S06
    provides: Shared deploy script conventions that S09 extended with runtime/executable validation.
  - slice: S08
    provides: Current setup/deployment docs that S09 updated with runtime/package reproducibility contract.
affects:
  - Milestone validation and any future deploy-bound slice should use scripts/verify/s09-runtime-reproducibility.sh before publishing/restarting services.
key_files:
  - global.json
  - docs/SETUP.md
  - docs/DEPLOYMENT.md
  - scripts/verify/production-smoke.sh
  - scripts/deploy-config.sh
  - scripts/deploy-backend.sh
  - scripts/deploy-adminpanel.sh
  - scripts/deploy-frontend.sh
  - scripts/verify/s09-package-restore-policy.sh
  - scripts/verify/s09-runtime-reproducibility.sh
  - src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj
  - .gsd/PROJECT.md
key_decisions:
  - Pin SDK resolution with root global.json to the installed 8.0.126 SDK rather than documenting a net10.0 assumption that does not match the tracked project files.
  - Use self-contained linux-x64 publish/executable validation for backend/AdminPanel deploy hardening instead of requiring a server-installed runtime for those services.
  - Keep the lockfile policy explicit as no committed packages.lock.json files, enforced by no floating/ranged PackageReference versions and restore/build verification.
  - Make scripts/verify/s09-runtime-reproducibility.sh delegate package policy to scripts/verify/s09-package-restore-policy.sh so there is one authoritative package drift gate.
patterns_established:
  - Local predeploy verifier with explicit PASS/FAIL/WARN phases and `RESULT required_failures=0` summary.
  - Runtime preflight separates self-contained and runtime-dependent expectations instead of treating missing dotnet as universally fatal.
  - Package restore policy is documented and mechanically checked rather than implied by project-file convention.
  - Verifier composition delegates specialized checks to a single authoritative script instead of duplicating policy logic.
observability_surfaces:
  - scripts/verify/s09-runtime-reproducibility.sh prints phase-scoped PASS/FAIL/WARN diagnostics and a required_failures summary.
  - scripts/verify/s09-package-restore-policy.sh reports floating/ranged package policy, lockfile policy, and restore outcome.
  - scripts/verify/production-smoke.sh includes runtime-preflight tokens and runtime expectation variables for deploy smoke diagnostics.
  - Deploy scripts log runtime contract and executable validation for backend/AdminPanel publish artifacts.
drill_down_paths:
  - .gsd/milestones/M003/slices/S09/tasks/T01-SUMMARY.md
  - .gsd/milestones/M003/slices/S09/tasks/T02-SUMMARY.md
  - .gsd/milestones/M003/slices/S09/tasks/T03-SUMMARY.md
  - .gsd/milestones/M003/slices/S09/tasks/T04-SUMMARY.md
duration: ""
verification_result: passed
completed_at: 2026-05-08T00:35:30.356Z
blocker_discovered: false
---

# S09: Runtime and package reproducibility check

**S09 made .NET SDK/runtime and package restore behavior explicit, mechanically verified, and wired into deploy/smoke preflight so runtime drift is caught before service restarts.**

## What Happened

S09 closed the final hardening gap for deployment reproducibility. The initial plan assumed net10.0/EF Core 10 risk, but task execution verified the concrete checkout reality: tracked projects currently target net8.0 and backend/admin deploys publish self-contained linux-x64 artifacts. The slice documented that actual contract in docs/SETUP.md and docs/DEPLOYMENT.md, added root global.json to pin SDK resolution to the installed 8.0.126 SDK, and clarified how self-contained publishes affect server runtime expectations.

The deploy and smoke layer now reports runtime intent instead of hiding it. scripts/verify/production-smoke.sh gained a runtime-preflight phase that declares expected runtime ID and self-contained/runtime-dependent mode, inspects dotnet host/runtime details when relevant, and fails required checks only when the declared mode requires a missing runtime. Backend and AdminPanel deploy scripts now chmod and validate their self-contained publish executables before upload/activation via shared helpers in scripts/deploy-config.sh; the frontend deploy documents static-asset runtime expectations for consistency.

Package reproducibility is now explicit and enforced. docs/SETUP.md records the no-lockfile decision, disallows floating/ranged PackageReference versions by default, and points to scripts/verify/s09-package-restore-policy.sh as the authoritative gate. That verifier found a real restore determinism problem (FSharp.Core downgrade drift) and the slice fixed it with a minimal PackageReference Update in src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj rather than bundling unrelated upgrades.

Finally, scripts/verify/s09-runtime-reproducibility.sh provides a single local predeploy proof for future agents: tool presence, global.json SDK match, docs contract markers, tracked project target frameworks, package policy, dotnet restore, dotnet build --no-restore, and production-smoke runtime preflight token wiring. It uses explicit PASS/FAIL/WARN output, avoids false passes from required checks, and treats local Docker absence as an optional environment warning.

## Verification

Fresh slice-level verification was run after the final project status refresh using gsd_exec command 34ae7b54-fe6b-49b1-b929-4ffebf41293b. `bash scripts/verify/s09-package-restore-policy.sh` exited 0 and reported no floating/ranged package versions, no packages.lock.json files present per documented policy, and successful dotnet restore. `bash scripts/verify/s09-runtime-reproducibility.sh` exited 0 with `RESULT required_failures=0 optional_warnings=1`; required checks passed for dotnet/rg/python tooling, global.json SDK pin and match (`8.0.126`), docs contract sections, all discovered tracked projects targeting net8.0, delegated package policy, dotnet restore, dotnet build --no-restore, and production-smoke runtime-preflight tokens. The only warning was optional local Docker CLI absence, which is not required for this local runtime/package verifier.

Earlier in the same closeout pass, the task-level plan checks also passed: `dotnet --version` returned 8.0.126 and docs contained SDK/runtime/linux-x64/self-contained markers; deploy/smoke scripts passed bash syntax checks and contained runtime/executable markers; the package policy verifier passed; and the runtime reproducibility verifier passed.

## Requirements Advanced

None.

## Requirements Validated

None.

## New Requirements Surfaced

- None.

## Requirements Invalidated or Re-scoped

None.

## Operational Readiness

None.

## Deviations

The plan/rationale referenced net10.0 and several Blazor/AdminPanel project paths that are not present in this checkout. Execution aligned the contract to the observed tracked project reality: net8.0 project files under src/tests and self-contained linux-x64 deploy artifacts. The slice also modified src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj to resolve a restore downgrade blocker found by the new package policy verifier.

## Known Limitations

S09 proves local runtime/package/build reproducibility and deploy-preflight wiring, not live production server state. Docker absence is only an optional warning in the local verifier, so Docker-backed provider tests still require the S07 Docker-enabled lane. The no-lockfile policy is explicit but may need revisiting if the project later requires bit-for-bit NuGet dependency graph locking.

## Follow-ups

Consider adding a CI job that runs `bash scripts/verify/s09-runtime-reproducibility.sh` on every deploy-bound change. Revisit the no-lockfile decision if future dependency drift or supply-chain policy requires committed `packages.lock.json` files.

## Files Created/Modified

- `global.json` — Pins .NET SDK resolution to the concrete installed SDK used by this checkout.
- `docs/SETUP.md` — Documents SDK/runtime contract and package restore/no-lockfile policy.
- `docs/DEPLOYMENT.md` — Documents deployment runtime/publish contract and self-contained linux-x64 expectations.
- `scripts/verify/production-smoke.sh` — Adds runtime-preflight diagnostics and expected runtime/self-contained contract tokens.
- `scripts/deploy-config.sh` — Adds shared executable validation helper used by deploy scripts.
- `scripts/deploy-backend.sh` — Logs runtime contract, chmods published binary, and validates executable artifact before deploy.
- `scripts/deploy-adminpanel.sh` — Logs runtime contract, validates executable artifact, and enforces deployed binary executable permission.
- `scripts/deploy-frontend.sh` — Documents static-asset runtime expectations in deploy preconditions.
- `scripts/verify/s09-package-restore-policy.sh` — Enforces no floating/ranged package versions, no-lockfile policy, and successful restore.
- `scripts/verify/s09-runtime-reproducibility.sh` — Provides the single local S09 SDK/framework/package/restore/build/runtime-preflight verifier.
- `src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj` — Adds targeted FSharp.Core update override to remove restore downgrade drift.
- `.gsd/PROJECT.md` — Refreshes project status to include S09 completion state, truths, verification, and remaining follow-ups.
