---
id: T03
parent: S09
milestone: M003
key_files:
  - docs/SETUP.md
  - scripts/verify/s09-package-restore-policy.sh
  - src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj
key_decisions:
  - (none)
duration: 
verification_result: mixed
completed_at: 2026-05-07T20:17:11.828Z
blocker_discovered: false
---

# T03: Documented and enforced package restore reproducibility policy with a strict verifier, and fixed FSharp.Core downgrade restore drift via an explicit version override.

**Documented and enforced package restore reproducibility policy with a strict verifier, and fixed FSharp.Core downgrade restore drift via an explicit version override.**

## What Happened

I adapted the stale plan paths to the repository’s current project layout and inventoried all tracked .csproj package references. I added a new package restore policy section to docs/SETUP.md that explicitly records the lockfile decision (no committed packages.lock.json files), disallows floating/ranged versions by default, and points to a single enforcement script. I created scripts/verify/s09-package-restore-policy.sh to make policy observable and enforceable: it scans tracked csproj files for floating/range package versions, requires docs-backed allowlist handling when floating versions are intentional, verifies the lockfile decision is documented and matched on disk, and runs dotnet restore with failure output surfaced. Initial verifier execution exposed a real restore failure (NU1605 FSharp.Core downgrade) unrelated to floating packages but directly relevant to restore determinism. I fixed that with a minimal restore-policy-aligned change in src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj using PackageReference Update for FSharp.Core at 8.0.400, then reran verification successfully.

## Verification

Ran bash scripts/verify/s09-package-restore-policy.sh after implementing the verifier and policy docs; it now passes all checks including restore. Also ran bash -n scripts/verify/s09-package-restore-policy.sh to validate script syntax. A first verifier run failed with NU1605/NU1504 and was used as failure visibility proof before the targeted FSharp.Core override fix.

## Verification Evidence

| # | Command | Exit Code | Verdict | Duration |
|---|---------|-----------|---------|----------|
| 1 | `bash scripts/verify/s09-package-restore-policy.sh` | 1 | ❌ fail | 3430ms |
| 2 | `bash scripts/verify/s09-package-restore-policy.sh` | 1 | ❌ fail | 1840ms |
| 3 | `bash scripts/verify/s09-package-restore-policy.sh` | 0 | ✅ pass | 2305ms |
| 4 | `bash -n scripts/verify/s09-package-restore-policy.sh` | 0 | ✅ pass | 40ms |

## Deviations

The plan referenced Blazor .csproj inputs that are not present in this checkout (only obj artifacts exist there), so I applied the same restore-policy contract to the concrete tracked projects in src/tests and documented this local path mismatch. I also touched src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj to resolve a restore downgrade blocker discovered by the new verifier.

## Known Issues

None.

## Files Created/Modified

- `docs/SETUP.md`
- `scripts/verify/s09-package-restore-policy.sh`
- `src/HappyGymStats.Visualizer/HappyGymStats.Visualizer.fsproj`
