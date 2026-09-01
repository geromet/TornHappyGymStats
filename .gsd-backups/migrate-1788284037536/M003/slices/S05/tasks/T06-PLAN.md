---
estimated_steps: 1
estimated_files: 2
skills_used: []
---

# T06: Harden production smoke security and required surfaces semantics

Remediate slice-closer blockers in the production smoke script. Change surfaces/latest 404/no-cache handling from required PASS to required FAIL while keeping actionable failure text. Eliminate or mitigate env-controlled shell command injection in local and remote modes by validating SMOKE_* inputs and safely quoting dynamic command arguments, or by replacing shell-string execution with fixed script bodies/argv-safe calls where practical. Add the documented framework phase to s05-production-smoke-contract.sh. Re-run the contract verifier and static checks proving the prior blocker signatures are gone. Because the script can execute over SSH, preserve read-only behavior and do not print env contents or secrets.

## Inputs

- `scripts/verify/production-smoke.sh`
- `scripts/verify/s05-production-smoke-contract.sh`
- `M003/S05 closer subagent findings`

## Expected Output

- `Hardened production-smoke.sh`
- `Updated s05-production-smoke-contract.sh`
- `Passing verification evidence`
- `Resolved reviewer/security blocker notes`

## Verification

bash -n scripts/verify/production-smoke.sh && bash scripts/verify/s05-production-smoke-contract.sh && gsd_exec/static equivalent proving: surfaces 404 no longer passes, documented framework phase is covered, and env-derived URL/service/container hint values are either validated and shell-quoted or not interpolated into bash -lc strings unsafely. Then repeat reviewer/security assessment before slice completion.
