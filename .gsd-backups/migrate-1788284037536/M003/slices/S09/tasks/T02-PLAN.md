---
estimated_steps: 7
estimated_files: 4
skills_used: []
---

# T02: Add runtime preflight checks

Why: The smoke/preflight layer should catch runtime mismatch before systemd restarts a broken service.

Do:
1. Add runtime checks to production smoke or a dedicated preflight helper: `dotnet --info`/`--list-runtimes` when relevant, publish runtime expectation, architecture.
2. For self-contained deploys, verify published executable exists and is executable where deploy scripts can check it.
3. Make missing runtime/SDK failures actionable but avoid requiring server SDK if self-contained runtime is enough.
4. Keep checks read-only.

Done when: preflight can identify runtime/SDK mismatch or missing executable before service restart.

## Inputs

- `.gsd/milestones/M003/slices/S05/S05-SUMMARY.md`
- `scripts/verify/production-smoke.sh`
- `scripts/deploy-backend.sh`

## Expected Output

- `scripts/verify/production-smoke.sh`
- `scripts/deploy-backend.sh`
- `scripts/deploy-frontend.sh`
- `scripts/deploy-adminpanel.sh`

## Verification

bash -n scripts/verify/production-smoke.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "dotnet --info|list-runtimes|linux-x64|chmod 755|executable|runtime" scripts/verify/production-smoke.sh scripts/deploy-*.sh

## Observability Impact

Signals added/changed: runtime/preflight phase.
How a future agent inspects this: smoke/preflight output.
Failure state exposed: missing runtime/SDK/executable permission.
