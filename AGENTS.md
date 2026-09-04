# HappyGymStats agent entrypoint

Read [`docs/WORKING-AGREEMENT.md`](docs/WORKING-AGREEMENT.md) before changing the
repository. It is the cross-agent source of truth for safety, task ownership,
evidence tiers, remote/operator boundaries, data provenance, and handoff rules.

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
