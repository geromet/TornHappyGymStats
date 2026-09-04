# HappyGymStats — Claude Code entrypoint

The shared repository rules live in [`docs/WORKING-AGREEMENT.md`](docs/WORKING-AGREEMENT.md).
Read that first. Do not duplicate or override those rules here.

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
