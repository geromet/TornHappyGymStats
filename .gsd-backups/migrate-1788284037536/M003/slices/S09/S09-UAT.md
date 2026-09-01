# S09: Runtime and package reproducibility check — UAT

**Milestone:** M003
**Written:** 2026-05-08T00:35:30.357Z

# S09: Runtime and package reproducibility check — UAT

**Milestone:** M003
**Written:** 2026-05-08

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: S09 is a deploy hardening/documentation slice. Acceptance is proven by deterministic local verifier scripts, docs/project-file inspection, and build/restore execution; it does not require production secrets or a live server to prove the repo contract catches runtime/package drift before deploy.

## Preconditions

- Run from the repository root.
- The .NET SDK pinned by `global.json` is installed; in this checkout that pin is `8.0.126`.
- `rg`, `python3`, and `bash` are available.
- Production credentials and remote server access are not required.
- Docker is optional for this UAT; missing Docker may appear as a warning but must not create a required failure in the S09 verifier.

## Smoke Test

Run:

```bash
bash scripts/verify/s09-runtime-reproducibility.sh
```

Expected: command exits 0 and ends with `RESULT required_failures=0`. Optional environment warnings, such as missing local Docker, are acceptable only when clearly reported as optional warnings.

## Test Cases

### 1. SDK pin and docs contract are visible

1. Run `dotnet --version`.
2. Inspect `global.json` and confirm the resolved SDK matches the pinned `sdk.version`.
3. Run `rg -n "SDK|runtime|linux-x64|self-contained" docs/SETUP.md docs/DEPLOYMENT.md`.
4. **Expected:** `dotnet --version` resolves through `global.json` as `8.0.126` in this checkout, and setup/deployment docs include the S09 SDK/runtime and publish contract markers.

### 2. Package restore policy blocks drift

1. Run `bash scripts/verify/s09-package-restore-policy.sh`.
2. **Expected:** command exits 0, reports no floating/ranged package versions, reports no committed `packages.lock.json` files per documented no-lockfile policy, and confirms `dotnet restore` succeeds.

### 3. Runtime preflight wiring remains in smoke/deploy scripts

1. Run `bash -n scripts/verify/production-smoke.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh`.
2. Run `rg -n "runtime-preflight|dotnet --info|list-runtimes|linux-x64|chmod 755|executable|runtime" scripts/verify/production-smoke.sh scripts/deploy-*.sh`.
3. **Expected:** syntax validation exits 0, production-smoke exposes runtime-preflight markers, and deploy scripts include executable/runtime signals for self-contained publish validation.

### 4. Full S09 verifier proves restore/build compatibility

1. Run `bash scripts/verify/s09-runtime-reproducibility.sh`.
2. **Expected:** required phases pass for tooling, SDK pin/match, docs contract, target frameworks, package policy, `dotnet restore`, `dotnet build --no-restore`, and production-smoke runtime token checks. The final result is `required_failures=0`.

## Edge Cases

### Missing optional Docker locally

1. Run `bash scripts/verify/s09-runtime-reproducibility.sh` on a machine without Docker installed.
2. **Expected:** the verifier may emit `WARN [optional] docker command missing`, but still exits 0 if all required runtime/package/build checks pass.

### Floating package version introduced later

1. Temporarily change a tracked project PackageReference version to a floating/ranged version such as `8.*` in a disposable branch.
2. Run `bash scripts/verify/s09-package-restore-policy.sh`.
3. **Expected:** the verifier exits non-zero and identifies the floating/ranged package reference unless an explicit docs-backed allowlist policy is added.

### SDK mismatch introduced later

1. Temporarily change `global.json` to an SDK version not installed in the environment.
2. Run `bash scripts/verify/s09-runtime-reproducibility.sh`.
3. **Expected:** the verifier exits non-zero during the SDK/tooling preflight before any deploy restart could occur.

## Failure Signals

- `RESULT required_failures` greater than 0 from `scripts/verify/s09-runtime-reproducibility.sh`.
- Non-zero exit from `scripts/verify/s09-package-restore-policy.sh`.
- `dotnet restore` or `dotnet build --no-restore` failure.
- Missing `runtime-preflight`, `SMOKE_EXPECT_RUNTIME`, or `SMOKE_EXPECT_SELF_CONTAINED` tokens in `scripts/verify/production-smoke.sh`.
- Floating/ranged package versions appearing in tracked project files without explicit policy support.
- Docs omitting SDK/runtime/publish or no-lockfile policy sections.

## Not Proven By This UAT

- Live production server state, nginx reachability, systemd status, or real remote deploy execution.
- Docker-enabled Postgres integration runtime; Docker absence is intentionally optional in this local S09 verifier.
- Performance under load or long-term package feed availability.
- Full end-to-end production smoke beyond verifying that runtime preflight tokens are wired into the smoke script.

## Notes for Tester

- The original slice rationale mentioned net10.0, but this checkout actually targets net8.0 across tracked projects. Treat net8.0 as the current authoritative contract unless future project-file changes intentionally migrate frameworks.
- The no-lockfile decision is intentional for now: reproducibility is enforced by pinned package versions and restore verification, not committed `packages.lock.json` files.
- If this UAT fails, start with `scripts/verify/s09-runtime-reproducibility.sh`; it delegates to the package verifier and labels failure phases clearly.
