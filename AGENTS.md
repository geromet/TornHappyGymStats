# HappyGymStats agent entrypoint

Read [`docs/WORKING-AGREEMENT.md`](docs/WORKING-AGREEMENT.md) before changing the
repository. It is the cross-agent source of truth for safety, live task ownership,
evidence tiers, remote/operator boundaries, data provenance, and handoff rules.

## Live coordination before mutation

GitHub issue **#140** is the canonical Agent work coordination LOCK for this
repository. Before selecting mutable work, read its current body and recent
comments. Immediately before a conflicting mutation, refresh #140, the target,
the actual default branch, and any branch/PR heads involved.

Acquire ownership with the repository's two-phase claim protocol: claim the exact
issue/PR/branch/work-package/seam with a unique run token, reread #140 immediately
after posting, and let the earlier GitHub comment ID win any overlap. Refresh once
more before the first mutation. Treat the exact observed branch head as a CAS
token: if it moves unexpectedly, stop and reconcile instead of blindly pushing or
force-pushing.

Current repository/PR state wins over stale issue prose, old PR descriptions, or
historical handoff notes. Obey the live LOCK's WIP gates, queue/drain ordering,
branch ownership, dependencies, and outside-contributor boundaries—not only claim
collisions.

## Default-branch authority and durable handoff

Fleet/manual agents **must never merge into `main` or any repository default
branch**. Final default-branch review and merge belong only to Gerome's explicitly
invoked coding-agent/human workflow.

A pushed branch or a child PR merged into a non-default stable/integration branch
is not, by itself, a completed handoff. Useful work must end in one recorded
terminal state:

1. directly or transitively represented by an **open PR ultimately targeting the
   default branch**;
2. proven incorporated or superseded by a current default-destined review surface,
   with that relationship recorded; or
3. explicitly abandoned after assessing its unique commits and recording why.

Do not use `ahead` alone as proof that an old branch contains missing work; account
for squash merges, rollups, replacement PRs, and explicit supersession. A coherent
stable branch that still contains useful work absent from default needs a live
stable-to-default review surface for Gerome rather than being left hidden merely
to reduce PR count.

Then use:

- [`README.md`](README.md) for the repository map;
- [`docs/OVERVIEW.md`](docs/OVERVIEW.md) for architecture;
- GitHub issues for authoritative planned work and dependency/stop-gate state;
- `scripts/verify/manifest.tsv` for the canonical verifier graph;
- `bash scripts/verify/build-and-test.sh` for the source/build/test gate;
- `docs/OPERATIONS-PITFALLS.md` before touching deploy/SSH/remote-exec code.

Do not treat gitignored `workspace/` material as required project state. A clean
clone must be enough to work safely. Do not run production/deploy/remote mutation
steps on the user's behalf; record them as T3 operator handoff when required.
