---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T03: Add safe sudoers install validation

Why: Installing sudoers unsafely can lock out deployment or widen privileges unexpectedly; the setup path needs a validated atomic install pattern.

Do:
1. Extend the setup script to stage `infra/sudoers-happygymstats` remotely.
2. Validate with `visudo -cf` against the staged file before installing.
3. Install to a deterministic `/etc/sudoers.d/` path with restrictive permissions.
4. Preserve idempotency: unchanged file should not be treated as failure.
5. Make validation failure stop before activation.

Done when: sudoers installation is part of setup and cannot activate invalid syntax.

## Inputs

- `scripts/setup-adminpanel-server.sh`
- `infra/sudoers-happygymstats`

## Expected Output

- `scripts/setup-adminpanel-server.sh`
- `infra/sudoers-happygymstats`

## Verification

bash -n scripts/setup-adminpanel-server.sh && rg -n "visudo|sudoers.d|chmod 440|install|happygymstats" scripts/setup-adminpanel-server.sh infra/sudoers-happygymstats

## Observability Impact

Signals added/changed: sudoers syntax validation phase and install phase.
How a future agent inspects this: setup output and `visudo` command in script.
Failure state exposed: invalid sudoers vs missing privilege vs install failure.
