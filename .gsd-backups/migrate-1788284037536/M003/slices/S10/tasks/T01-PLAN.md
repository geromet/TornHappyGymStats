---
estimated_steps: 30
estimated_files: 9
skills_used: []
---

# T01: Recover S03/S04/S05 closure tasks from real verifier evidence

Execute or repair the missing terminal tasks that left S03/S04/S05 with complete slice status but pending task counts, then persist evidence-backed task summaries through GSD completion tools and record the evidence in a durable remediation document.

skills_used: api-design, design-an-interface, grill-me, tdd, verify-before-complete, write-docs

Steps:
1. Inspect the three missing task plans and their referenced source/verifier files; treat the old blocker text as a symptom, not evidence.
2. Run the planned verifier commands for S03/T05, S04/T04, and S05/T06. Use `gsd_exec` for noisy commands and capture command, exit code, and summary evidence.
3. If a verifier fails because the source script contract regressed, fix the smallest tracked source/script defect needed for the planned verifier to pass, then re-run.
4. Call `gsd_task_complete` for S03/T05, S04/T04, and S05/T06 with the fresh evidence. Do not hand-edit task checkboxes.
5. Write `docs/m003-artifact-remediation-evidence.md` with the commands run, pass/fail outcome, task summary paths, and proof-level limitation that live production proof belongs to S11.

Must-haves:
- `.gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md` exists and contains fresh verification for AdminPanel setup privilege boundary/diagnostics.
- `.gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md` exists and contains fresh verification for AdminPanel route/auth smoke checks.
- `.gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md` exists and contains fresh verification for production-smoke security and required surfaces semantics.
- `docs/m003-artifact-remediation-evidence.md` contains the evidence inventory consumed by later tasks.
- Any source/script change is limited to what a failing verifier proves necessary.

Failure Modes:
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| Existing shell verifiers | Fix the smallest source/script contract regression or record a real blocker in the task summary; do not mark complete on failure. | Re-run with bounded timeout and preserve partial output in the task summary. | Treat ambiguous PASS/WARN output as a verifier defect; make the verifier's pass/fail semantics explicit before completing. |

Load Profile:
- Shared resources: local shell, dotnet/npm/tooling invoked by existing verifiers.
- Per-operation cost: bounded script/static verification; no production network mutation.
- 10x breakpoint: verifier runtime/noisy output, mitigated by `gsd_exec` summaries.

Negative Tests:
- Malformed inputs: missing referenced scripts must fail verification rather than be skipped.
- Error paths: non-zero verifier exit blocks `gsd_task_complete`.
- Boundary conditions: a task already completed by a prior recovery attempt should be handled idempotently by GSD completion tools.

Verification:
- `test -s .gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md && test -s .gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md && test -s .gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md`
- `test -s docs/m003-artifact-remediation-evidence.md`
- Re-run the verifier commands cited in each new task summary and confirm exit code 0.

## Inputs

- `.gsd/milestones/M003/slices/S03/tasks/T05-PLAN.md`
- `.gsd/milestones/M003/slices/S04/tasks/T04-PLAN.md`
- `.gsd/milestones/M003/slices/S05/tasks/T06-PLAN.md`
- `scripts/setup-adminpanel-server.sh`
- `scripts/verify/s03-adminpanel-setup.sh`
- `scripts/verify/s04-adminpanel-route.sh`
- `scripts/verify/production-smoke.sh`
- `scripts/verify/s05-production-smoke-contract.sh`

## Expected Output

- `.gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md`
- `.gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md`
- `.gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md`
- `docs/m003-artifact-remediation-evidence.md`

## Verification

test -s .gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md && test -s .gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md && test -s .gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md && test -s docs/m003-artifact-remediation-evidence.md

## Observability Impact

Adds drill-down task summaries and a durable evidence inventory with fresh command evidence, exit codes, known limitations, and explicit blocker state for future agents.
