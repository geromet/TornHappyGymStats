---
estimated_steps: 25
estimated_files: 7
skills_used: []
---

# T03: Repair M003 validation and requirement coverage boundaries

Create or refresh the milestone validation artifact with exact requirement scope: M003 has zero active requirements in `.gsd/REQUIREMENTS.md`, R001/R002 are already validated by M002/S06, and S10/S11 must not claim to revalidate them without new evidence.

skills_used: write-docs, verify-before-complete

Steps:
1. Read `.gsd/REQUIREMENTS.md` and confirm there are zero active requirements and only historical validated R001/R002 entries.
2. Extend `docs/m003-artifact-remediation-evidence.md` with a requirement-coverage note that R001/R002 are M002 historical proof and out of scope for M003 unless requirements are explicitly remapped.
3. Call `gsd_validate_milestone` with a truthful verdict for the current state after S10: blocker artifacts remediated, requirement coverage clarified, and S11 still required for live full-stack deployment evidence.
4. Ensure `.gsd/milestones/M003/M003-VALIDATION.md` has explicit sections for success criteria, slice delivery audit, cross-slice integration, requirement coverage, verification classes, verdict rationale, and remediation/follow-up.
5. Complete S10 only after fresh verification proves required artifacts exist and the unsupported requirement claim is absent.

Must-haves:
- `.gsd/milestones/M003/M003-VALIDATION.md` exists and is non-empty.
- Validation references S10 artifact repair and S11 live evidence as distinct proof classes.
- Requirement coverage names R001/R002 only as historical validated M002 requirements/out-of-scope for M003, not as M003 validated evidence.
- S10 task summary records no requirements advanced/validated/invalidated unless the requirements file itself changed.

Failure Modes:
| Dependency | On error | On timeout | On malformed response |
|------------|----------|-----------|----------------------|
| GSD validation writer | Preserve the failed payload and write no completion claim; retry only after fixing malformed markdown fields. | Retry once and record the timeout in known issues. | Treat missing required validation sections as malformed output and call the writer again with complete fields. |

Negative Tests:
- Malformed inputs: missing `.gsd/REQUIREMENTS.md` or absent validation artifact blocks completion.
- Error paths: validation text that says M003 validates R001/R002 without proof fails grep-based checks.
- Boundary conditions: S11 not complete means validation must not overclaim final full-stack production proof.

Verification:
- `test -s .gsd/milestones/M003/M003-VALIDATION.md`
- `grep -q "R001" .gsd/milestones/M003/M003-VALIDATION.md && grep -q "R002" .gsd/milestones/M003/M003-VALIDATION.md && grep -qi "out of scope\|M002" .gsd/milestones/M003/M003-VALIDATION.md`
- `! grep -Ei "M003.*validat(es|ed).*R00[12]|R00[12].*validat(es|ed).*M003" .gsd/milestones/M003/M003-VALIDATION.md`

## Inputs

- `.gsd/REQUIREMENTS.md`
- `.gsd/milestones/M003/M003-ROADMAP.md`
- `docs/m003-artifact-remediation-evidence.md`
- `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`
- `.gsd/milestones/M003/slices/S04/S04-SUMMARY.md`
- `.gsd/milestones/M003/slices/S05/S05-SUMMARY.md`

## Expected Output

- `.gsd/milestones/M003/M003-VALIDATION.md`
- `docs/m003-artifact-remediation-evidence.md`

## Verification

test -s .gsd/milestones/M003/M003-VALIDATION.md && grep -q "R001" .gsd/milestones/M003/M003-VALIDATION.md && grep -q "R002" .gsd/milestones/M003/M003-VALIDATION.md && grep -qi "out of scope\|M002" .gsd/milestones/M003/M003-VALIDATION.md && ! grep -Ei "M003.*validat(es|ed).*R00[12]|R00[12].*validat(es|ed).*M003" .gsd/milestones/M003/M003-VALIDATION.md

## Observability Impact

Adds a milestone validation artifact that future agents can inspect to distinguish artifact remediation, requirement coverage, and remaining live deployment evidence.
