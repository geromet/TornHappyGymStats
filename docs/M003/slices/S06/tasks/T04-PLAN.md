---
estimated_steps: 7
estimated_files: 3
skills_used: []
---

# T04: Categorize manual and diagnostic ops scripts

Why: Some existing scripts prompt humans or ask for pasted output; those are risky in agent-driven deployment and should be clearly manual-only or machine-readable.

Do:
1. Review `scripts/server-create-containers-user.sh`, `scripts/recon-server.sh`, and related ops scripts.
2. Mark interactive/manual scripts clearly, or refactor output to machine-readable phases where appropriate.
3. Remove or replace “paste output back” style instructions from scripts intended for agent execution.
4. Add documentation explaining which scripts mutate server state and require explicit user confirmation.

Done when: operational scripts are categorized as automated deploy, read-only diagnostic, or manual bootstrap.

## Inputs

- `scripts/recon-server.sh`
- `scripts/server-create-containers-user.sh`
- `docs/DEPLOYMENT.md`

## Expected Output

- `scripts/recon-server.sh`
- `scripts/server-create-containers-user.sh`
- `docs/DEPLOYMENT.md`

## Verification

bash -n scripts/recon-server.sh scripts/server-create-containers-user.sh && ! rg -n "Paste full output back|Copy .* manually|ask Claude" scripts/recon-server.sh scripts/server-create-containers-user.sh docs/DEPLOYMENT.md

## Observability Impact

Signals added/changed: script category and safe-use messaging.
How a future agent inspects this: script help text and deployment docs.
Failure state exposed: human-only step is not mistaken for automated deploy failure.
