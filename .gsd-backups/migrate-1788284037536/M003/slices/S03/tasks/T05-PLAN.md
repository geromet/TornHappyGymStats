---
estimated_steps: 1
estimated_files: 4
skills_used: []
---

# T05: Harden AdminPanel setup privilege boundary and diagnostics

Fix the blocking slice-close review findings before completing S03. Narrow infra/sudoers-happygymstats so it does not grant bare root file-mutator commands (especially install, chown, chmod, rm, ln, rsync, find); use exact argument/path-scoped commands or a constrained root-owned helper. Fix scripts/setup-adminpanel-server.sh health-check classification so curl failures are captured despite set -e and service-inactive/port-unavailable/http-non-2xx paths are reachable. Add a distinct missing-privilege preflight/error for the likely first-run server state where only narrow rsync sudo is installed. Close the staged sudoers validation/install TOCTOU gap, for example by checksum-verifying the staged artifact immediately before install or moving validate+install into a constrained privileged helper. Strengthen scripts/verify/s03-adminpanel-setup.sh so static verification fails on bare privileged mutator grants and confirms the health-check errexit guard/missing-privilege diagnostics.

## Inputs

- `Slice-close reviewer/security findings from complete-slice S03`
- `infra/sudoers-happygymstats`
- `scripts/setup-adminpanel-server.sh`
- `scripts/verify/s03-adminpanel-setup.sh`
- `docs/DEPLOYMENT.md`

## Expected Output

- `Hardened sudoers policy with no bare root file-mutator grants`
- `Setup script with reachable classified health-check failures under set -e`
- `Distinct missing-privilege failure output for missing bootstrap sudo permissions`
- `Verifier coverage for narrowed sudoers and diagnostics`

## Verification

bash -n scripts/setup-adminpanel-server.sh && bash scripts/setup-adminpanel-server.sh --help && bash scripts/setup-adminpanel-server.sh --dry-run && bash scripts/verify/s03-adminpanel-setup.sh && ! rg -n "NOPASSWD: (/usr/bin/|/bin/)?(install|chown|chmod|rm|ln|rsync|find)$|NOPASSWD: ALL|/bin/bash|/usr/bin/bash|sh -c" infra/sudoers-happygymstats
