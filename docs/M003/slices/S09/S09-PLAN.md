# S09: Runtime and package reproducibility check

**Goal:** Reduce deployment flakiness from ambiguous runtime/package assumptions, especially around `net10.0`, EF Core 10, and loose package versions.
**Demo:** The repo documents and verifies the expected .NET runtime/SDK and package restore behavior for deploys.

## Must-Haves

- Expected .NET SDK/runtime version for build and deployment is documented.
- Server runtime availability check is included in smoke or deploy preflight where appropriate.
- Loose package references such as `MudBlazor` `8.*` are either pinned or explicitly justified with restore reproducibility guidance.
- Package restore/build behavior is documented for local and deploy environments.
- Any lockfile or no-lockfile decision is explicit.
- Verification proves build/test/deploy preflight can detect missing runtime/SDK before a service restart.
- No unrelated dependency upgrades are bundled into this slice unless required to make restore deterministic.
- Verification commands do not use `|| true` to convert required runtime/package checks into false passes; optional checks must be encoded explicitly in verifier scripts.

## Proof Level

- This slice proves: Build/deploy preflight proof. This is a hardening slice; proof comes from deterministic version checks and successful build/restore verification.

## Integration Closure

Upstream surfaces consumed: S05 smoke script, project files, deploy scripts, docs from S08.
New wiring introduced: runtime/package preflight and reproducibility documentation.
What remains before milestone end-to-end: none; this is final hardening after smoke behavior is known.

## Verification

- Runtime signals: preflight output for .NET SDK/runtime, package restore, pinned/loose package policy, and build compatibility.
- Inspection surfaces: preflight script, docs, project files, build output.
- Failure visibility: missing SDK/runtime, unsupported target framework, restore nondeterminism, loose package drift.
- Redaction constraints: no secrets; package/source URLs are fine, credentials are not.

## Tasks

- [ ] **T01: Document .NET SDK and runtime contract** `est:45m`
  Why: The solution targets `net10.0` and production deploys self-contained apps; future agents need a clear runtime/SDK contract before changing scripts.
  - Files: `HappyGymStats.sln`, `dotnet-tools.json`, `src/HappyGymStats.Api/HappyGymStats.Api.csproj`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/HappyGymStats.Blazor.Client.csproj`, `src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj`, `src/HappyGymStats.Data/HappyGymStats.Data.csproj`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `docs/SETUP.md`, `docs/DEPLOYMENT.md`, `global.json`
  - Verify: dotnet --version && rg -n "net10.0|SDK|runtime|linux-x64|self-contained" docs/SETUP.md docs/DEPLOYMENT.md

- [ ] **T02: Add runtime preflight checks** `est:1h`
  Why: The smoke/preflight layer should catch runtime mismatch before systemd restarts a broken service.
  - Files: `scripts/verify/production-smoke.sh`, `scripts/deploy-backend.sh`, `scripts/deploy-frontend.sh`, `scripts/deploy-adminpanel.sh`
  - Verify: bash -n scripts/verify/production-smoke.sh scripts/deploy-backend.sh scripts/deploy-frontend.sh scripts/deploy-adminpanel.sh && rg -n "dotnet --info|list-runtimes|linux-x64|chmod 755|executable|runtime" scripts/verify/production-smoke.sh scripts/deploy-*.sh

- [ ] **T03: Pin or document package restore policy** `est:1.5h`
  Why: Loose package references like `MudBlazor` `8.*` can change restores over time and make deploy builds non-reproducible. The policy should be mechanically checked, not hidden behind `|| true`.
  - Files: `src/HappyGymStats.Api/HappyGymStats.Api.csproj`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj`, `src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/HappyGymStats.Blazor.Client.csproj`, `src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj`, `src/HappyGymStats.Data/HappyGymStats.Data.csproj`, `src/HappyGymStats.Core/HappyGymStats.Core.csproj`, `src/HappyGymStats.Identity/HappyGymStats.Identity.csproj`, `src/HappyGymStats.Encryption/HappyGymStats.Encryption.csproj`, `tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj`, `docs/SETUP.md`, `scripts/verify/s09-package-restore-policy.sh`
  - Verify: bash scripts/verify/s09-package-restore-policy.sh

- [ ] **T04: Add runtime reproducibility verifier** `est:45m`
  Why: The runtime/package contract needs a single local verifier that can be run before deployment.
  - Files: `scripts/verify/s09-runtime-reproducibility.sh`, `scripts/verify/s09-package-restore-policy.sh`, `docs/SETUP.md`, `docs/DEPLOYMENT.md`
  - Verify: bash scripts/verify/s09-runtime-reproducibility.sh

## Files Likely Touched

- HappyGymStats.sln
- dotnet-tools.json
- src/HappyGymStats.Api/HappyGymStats.Api.csproj
- src/HappyGymStats.Blazor/HappyGymStats.Blazor/HappyGymStats.Blazor.csproj
- src/HappyGymStats.Blazor/HappyGymStats.Blazor.Client/HappyGymStats.Blazor.Client.csproj
- src/HappyGymStats.AdminPanel/HappyGymStats.AdminPanel.csproj
- src/HappyGymStats.Data/HappyGymStats.Data.csproj
- tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj
- docs/SETUP.md
- docs/DEPLOYMENT.md
- global.json
- scripts/verify/production-smoke.sh
- scripts/deploy-backend.sh
- scripts/deploy-frontend.sh
- scripts/deploy-adminpanel.sh
- src/HappyGymStats.Core/HappyGymStats.Core.csproj
- src/HappyGymStats.Identity/HappyGymStats.Identity.csproj
- src/HappyGymStats.Encryption/HappyGymStats.Encryption.csproj
- scripts/verify/s09-package-restore-policy.sh
- scripts/verify/s09-runtime-reproducibility.sh
