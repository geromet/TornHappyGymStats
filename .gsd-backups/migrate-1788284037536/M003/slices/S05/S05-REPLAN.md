# S05 Replan

**Milestone:** M003
**Slice:** S05
**Blocker Task:** T05
**Created:** 2026-05-07T19:21:29.415Z

## Blocker Description

Slice closer verification found production-smoke.sh cannot be safely completed: reviewer found required surfaces/latest can pass on 404/no-cache, and security found env-configurable values interpolated into host/SSH shell command strings, enabling local/remote command injection if SMOKE_* inputs are untrusted. The closer cannot edit source in the complete-slice tools-policy, so remediation must be executed as a new task.

## What Changed

Added T06 remediation task to harden production-smoke.sh before slice completion. Completed T01-T05 remain preserved; T06 must fix the surfaces 404 required-failure semantics, quote/validate env-derived command arguments or avoid shell-string execution, add framework phase coverage to the contract verifier, and rerun slice-level verification plus review/security checks.
