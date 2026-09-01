# S10: Remediate blocker summaries and requirement coverage

**Goal:** Replace the synthetic blocker closure for S03/S04/S05 with reliable evidence-backed closure artifacts, repair the inconsistent pending-task counts that caused those blocker summaries, and make M003 validation/requirement coverage explicit that R001/R002 are historical M002 requirements and not evidence produced by M003.
**Demo:** After this: S03/S04/S05 have reliable replacement closure evidence from task artifacts or are explicitly re-executed, and M003 requirement coverage states exactly which requirements are in scope without claiming unsupported R001/R002 evidence.

## Must-Haves

- S03, S04, and S05 no longer have slice summaries whose primary content is `# BLOCKER — auto-mode recovery failed`.
- The previously missing closure tasks have evidence-backed task summaries: `.gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md`, `.gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md`, and `.gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md`.
- `docs/m003-artifact-remediation-evidence.md` captures the S03/S04/S05 closure evidence, verifier commands, and proof-level limitations for fresh validators.
- Replacement S03/S04/S05 slice summaries cite their task drill-down paths and verification commands instead of claiming closure from planning-lane write attempts.
- `.gsd/milestones/M003/M003-VALIDATION.md` exists and states the current validation truthfully: S10 repairs artifact/coverage evidence, S11 is still required for live full-stack deployment proof, and M003 does not validate or advance R001/R002.
- Mechanical verification commands prove the blocker summaries are gone, required task summaries exist, and validation coverage does not claim unsupported R001/R002 evidence.

## Proof Level

- This slice proves: Artifact and contract remediation proof. This slice proves that GSD closure artifacts and validation coverage are internally consistent and mechanically checkable. It does not prove live production deployment health; that operational proof remains in S11.

## Integration Closure

Consumes S03/S04/S05 task plans, existing task summaries, verifier scripts, S09 runtime/package context, `.gsd/REQUIREMENTS.md`, and the M003 roadmap. Introduces replacement GSD closure/validation artifacts and a durable remediation evidence document. No application runtime entrypoint changes are expected unless a verifier in T01 exposes a real source defect. Full end-to-end production or production-like smoke evidence remains explicitly open for S11.

## Verification

- The slice improves agent-facing observability by replacing opaque `# BLOCKER` slice summaries with drill-down paths, commands run, pass/fail evidence, known limitations, and validation coverage language that future milestone validation can audit without re-deriving history.

## Tasks

- [x] **T01: Recover S03/S04/S05 closure tasks from real verifier evidence** `est:1h`
  Execute or repair the missing terminal tasks that left S03/S04/S05 with complete slice status but pending task counts, then persist evidence-backed task summaries through GSD completion tools and record the evidence in a durable remediation document.
  - Files: `.gsd/milestones/M003/slices/S03/tasks/T05-PLAN.md`, `.gsd/milestones/M003/slices/S04/tasks/T04-PLAN.md`, `.gsd/milestones/M003/slices/S05/tasks/T06-PLAN.md`, `scripts/setup-adminpanel-server.sh`, `scripts/verify/s03-adminpanel-setup.sh`, `scripts/verify/s04-adminpanel-route.sh`, `scripts/verify/production-smoke.sh`, `scripts/verify/s05-production-smoke-contract.sh`, `docs/m003-artifact-remediation-evidence.md`
  - Verify: test -s .gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md && test -s .gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md && test -s .gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md && test -s docs/m003-artifact-remediation-evidence.md

- [x] **T02: Replace blocker slice summaries with audited closure narratives** `est:1h`
  Use the evidence inventory from T01 to write replacement S03/S04/S05 slice summaries that tell the actual closure story and expose remaining limitations without blocker boilerplate.
  - Files: `docs/m003-artifact-remediation-evidence.md`, `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`, `.gsd/milestones/M003/slices/S04/S04-SUMMARY.md`, `.gsd/milestones/M003/slices/S05/S05-SUMMARY.md`, `.gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md`, `.gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md`, `.gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md`
  - Verify: ! grep -R "^# BLOCKER" .gsd/milestones/M003/slices/S03/S03-SUMMARY.md .gsd/milestones/M003/slices/S04/S04-SUMMARY.md .gsd/milestones/M003/slices/S05/S05-SUMMARY.md && grep -q "drill_down_paths" .gsd/milestones/M003/slices/S03/S03-SUMMARY.md && grep -q "drill_down_paths" .gsd/milestones/M003/slices/S04/S04-SUMMARY.md && grep -q "drill_down_paths" .gsd/milestones/M003/slices/S05/S05-SUMMARY.md

- [ ] **T03: Repair M003 validation and requirement coverage boundaries** `est:45m`
  Create or refresh the milestone validation artifact with exact requirement scope: M003 has zero active requirements in `.gsd/REQUIREMENTS.md`, R001/R002 are already validated by M002/S06, and S10/S11 must not claim to revalidate them without new evidence.
  - Files: `.gsd/REQUIREMENTS.md`, `.gsd/milestones/M003/M003-ROADMAP.md`, `.gsd/milestones/M003/M003-VALIDATION.md`, `docs/m003-artifact-remediation-evidence.md`, `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`, `.gsd/milestones/M003/slices/S04/S04-SUMMARY.md`, `.gsd/milestones/M003/slices/S05/S05-SUMMARY.md`
  - Verify: test -s .gsd/milestones/M003/M003-VALIDATION.md && grep -q "R001" .gsd/milestones/M003/M003-VALIDATION.md && grep -q "R002" .gsd/milestones/M003/M003-VALIDATION.md && grep -qi "out of scope\|M002" .gsd/milestones/M003/M003-VALIDATION.md && ! grep -Ei "M003.*validat(es|ed).*R00[12]|R00[12].*validat(es|ed).*M003" .gsd/milestones/M003/M003-VALIDATION.md

## Files Likely Touched

- .gsd/milestones/M003/slices/S03/tasks/T05-PLAN.md
- .gsd/milestones/M003/slices/S04/tasks/T04-PLAN.md
- .gsd/milestones/M003/slices/S05/tasks/T06-PLAN.md
- scripts/setup-adminpanel-server.sh
- scripts/verify/s03-adminpanel-setup.sh
- scripts/verify/s04-adminpanel-route.sh
- scripts/verify/production-smoke.sh
- scripts/verify/s05-production-smoke-contract.sh
- docs/m003-artifact-remediation-evidence.md
- .gsd/milestones/M003/slices/S03/S03-SUMMARY.md
- .gsd/milestones/M003/slices/S04/S04-SUMMARY.md
- .gsd/milestones/M003/slices/S05/S05-SUMMARY.md
- .gsd/milestones/M003/slices/S03/tasks/T05-SUMMARY.md
- .gsd/milestones/M003/slices/S04/tasks/T04-SUMMARY.md
- .gsd/milestones/M003/slices/S05/tasks/T06-SUMMARY.md
- .gsd/REQUIREMENTS.md
- .gsd/milestones/M003/M003-ROADMAP.md
- .gsd/milestones/M003/M003-VALIDATION.md
