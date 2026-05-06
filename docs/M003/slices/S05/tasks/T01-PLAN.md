---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T01: Create production smoke script framework

Why: The smoke script needs shared primitives so each check reports consistently and safely.

Do:
1. Create a read-only production smoke script with `--help`, clear defaults, and override variables.
2. Add helper functions for phase headings, pass/fail/warn output, command execution, HTTP status checks, and optional checks.
3. Ensure script never echoes secrets or env file contents.
4. Decide local-vs-remote mode: either runs on server locally, or SSHes with read-only commands using deploy config; document the intended mode.
5. Make exit behavior deterministic: required check failure exits non-zero; optional unavailable checks warn.

Done when: the script framework can host checks without duplicating error logic.

## Inputs

- `scripts/deploy-config.sh`
- `scripts/verify/s01-api-production-contract.sh`
- `scripts/verify/s04-adminpanel-route.sh`

## Expected Output

- `scripts/verify/production-smoke.sh`

## Verification

bash -n scripts/verify/production-smoke.sh && bash scripts/verify/production-smoke.sh --help && rg -n "PASS|FAIL|WARN|required|optional|secret|TOKEN|KEY" scripts/verify/production-smoke.sh

## Observability Impact

Signals added/changed: shared smoke output format and exit semantics.
How a future agent inspects this: run `bash scripts/verify/production-smoke.sh --help`.
Failure state exposed: structured phase failures.
