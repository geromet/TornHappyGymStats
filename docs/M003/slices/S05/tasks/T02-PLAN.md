---
estimated_steps: 8
estimated_files: 2
skills_used: []
---

# T02: Add service nginx and port checks

Why: The reported 502 is a systemd/nginx/API boundary failure; smoke must check these before UI assertions.

Do:
1. Add checks for `happygymstats-api`, `happygymstats-blazor`, and `happygymstats-adminpanel` unit active states.
2. Add nginx syntax/config check in read-only mode.
3. Add port/listening checks for 5047, 5182, and 5048 where feasible.
4. Distinguish missing service from inactive service from command unavailable/no privilege.
5. Keep service names configurable through variables.

Done when: smoke output can identify service/port/nginx causes before HTTP route checks run.

## Inputs

- `.gsd/milestones/M003/slices/S01/S01-SUMMARY.md`
- `.gsd/milestones/M003/slices/S03/S03-SUMMARY.md`
- `infra/happygymstats-api.service`
- `infra/happygymstats-blazor.service`
- `infra/happygymstats-adminpanel.service`

## Expected Output

- `scripts/verify/production-smoke.sh`

## Verification

bash -n scripts/verify/production-smoke.sh && rg -n "happygymstats-api|happygymstats-blazor|happygymstats-adminpanel|nginx -t|5047|5182|5048|is-active|ss " scripts/verify/production-smoke.sh

## Observability Impact

Signals added/changed: systemd/nginx/listening phase in smoke report.
How a future agent inspects this: smoke output phase `services` or equivalent.
Failure state exposed: service missing/inactive, nginx invalid, port not listening.
