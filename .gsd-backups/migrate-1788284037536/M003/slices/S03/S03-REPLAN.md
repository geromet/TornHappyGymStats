# S03 Replan

**Milestone:** M003
**Slice:** S03
**Blocker Task:** T04
**Created:** 2026-05-06T19:52:24.892Z

## Blocker Description

Slice-close reviewer/security audit found blocking issues in the AdminPanel setup artifacts after all planned tasks were marked complete: infra/sudoers-happygymstats still grants broad root-capable file mutation primitives (bare install/chown/chmod/rm/ln/rsync/find), setup health-check curl failure classification is bypassed under set -e before curl_exit is captured, the first-run path does not emit a distinct missing-privilege diagnostic when only narrow rsync sudo exists, and the staged sudoers validate/install flow has a TOCTOU gap. The complete-slice tools policy blocks source edits, so remediation must happen in a follow-up execute-task unit before slice completion.

## What Changed

Added T05 as a remediation task to harden the sudoers boundary and setup diagnostics before S03 can be closed. Existing completed tasks are preserved; S03 remains open until T05 passes and the slice is re-closed.
