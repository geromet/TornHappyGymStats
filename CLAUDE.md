# HappyGymStats — Claude Code entrypoint

The shared repository rules live in [`docs/WORKING-AGREEMENT.md`](docs/WORKING-AGREEMENT.md).
Read that first. Do not override those rules here.

Before any mutable GitHub/repository work, treat **issue #140** as canonical live
coordination: read its body + recent comments, discover the actual current default
branch, refresh the target and branch/PR heads, acquire an exact two-phase claim,
reread #140 so the earlier-comment-ID winner is known, and refresh again before
the first mutation. Use the observed head SHA as a CAS token; if it changes
unexpectedly, reconcile rather than blindly pushing or force-pushing. Live repo
state wins stale issue prose or historical handoff notes.

Fleet/manual work never merges into `main` or another default branch. Final
default review/merge belongs only to Gerome's explicitly invoked coding-agent or
human workflow. A child PR merged into a non-default stable branch is not a
terminal handoff unless its useful work remains represented by an open
ultimately-default-destined review surface, is proven incorporated/superseded with
that relationship recorded, or is explicitly abandoned after its unique commits
are assessed. Do not infer missing work from `ahead` alone.

Useful repository routes:

- [`README.md`](README.md) — repository map;
- [`docs/OVERVIEW.md`](docs/OVERVIEW.md) — architecture;
- GitHub issues — authoritative planned work, dependencies, and stop gates;
- `scripts/verify/manifest.tsv` — canonical verifier graph;
- `bash scripts/verify/build-and-test.sh` — source/build/test handoff gate;
- `docs/OPERATIONS-PITFALLS.md` — required reading before remote/deploy work.

Claude-specific notes:

- project skills under `.claude/skills/` route to existing repository mechanisms;
  use them instead of inventing parallel workflow prose;
- repository permissions intentionally allow routine local verification but not
  production/deploy/remote mutations;
- when a T3 operator step is required, hand the command to the user rather than
  attempting to bypass the interactive Cloudflare/SSH/TTY boundary;
- `workspace/` is local supporting material only and is never required authority
  for implementation or acceptance criteria.

Commits and PRs should explain why the change exists and what evidence falsifies
it. Use `Closes #N` only when the current issue acceptance criteria are actually
complete; partial work uses `Refs #N`.
