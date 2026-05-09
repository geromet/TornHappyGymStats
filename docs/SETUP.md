# Setup

This setup guide reflects the current production shape: **API + Blazor + AdminPanel + Postgres + Keycloak**.

- This repository contains the API and shared libraries.
- Blazor and AdminPanel are operational runtime peers; their service contracts are documented here even if their source trees are not present in this checkout.

## Prerequisites

- .NET 8 SDK (8.0.126 currently pinned via repository `global.json`)
- Docker (for Postgres/Testcontainers verification paths)
- `curl`, `bash`, and `rg`

## .NET SDK/runtime contract (M003 S09)

- All tracked projects currently target `net8.0`.
- Local build/test/restore should use the SDK selected by `global.json` (repo root).
- Deployment publish scripts target `linux-x64` and use `dotnet publish --self-contained true` for API/AdminPanel.
- Because deploy artifacts are self-contained, the server does **not** need a preinstalled shared ASP.NET/.NET runtime for those services; it still needs systemd/nginx/service prerequisites documented in deployment smoke checks.

## Secret handling (required)

- Never commit `.env` values, tokens, keys, or connection strings.
- Store secrets outside git-tracked files.
- Use env var **names** only in docs/scripts.
- For agent-driven setup, collect secrets with secure collection tooling and write only to approved env destinations.

## Required environment variable names

At minimum, operators should provision these env vars for API/runtime parity:

- `HAPPYGYMSTATS_CONNECTION_STRING`
- `ConnectionStrings__HappyGymStats` (alias used by API config)
- `ProvisionalToken__SigningKey`
- `HAPPYGYMSTATS_SURFACES_CACHE_DIR`
- `ASPNETCORE_URLS`
- `ASPNETCORE_ENVIRONMENT` (or `DOTNET_ENVIRONMENT`)

Optional/flow-specific:

- `TORN_API_KEY` (or `HAPPYGYMSTATS_TORN_API_KEY`) for import/surfaces workflows
- `HAPPYGYMSTATS_SKIP_POSTGRES_INTEGRATION=1` to intentionally skip Postgres integration tests

## Build and test

```bash
dotnet build
dotnet test
```

## Package restore reproducibility policy (M003 S09)

- **Lockfile decision:** no `packages.lock.json` files are committed right now.
- **Determinism strategy:** reproducibility is enforced by pinned package versions in tracked `.csproj` files plus pinned SDK in `global.json`.
- **Floating/range versions:** not allowed unless explicitly allowlisted with a written justification.
- **Enforcement:** run `bash scripts/verify/s09-package-restore-policy.sh` to check policy, validate lockfile decision, and run a concrete `dotnet restore`.

If a floating version is intentionally introduced later, update both:
1. this section with package name + justification, and
2. the verifier allowlist in `scripts/verify/s09-package-restore-policy.sh`.

### Postgres provider verification tier

```bash
bash scripts/verify/s07-postgres-integration.sh
```

This tier is the production-provider proof and may return explicit `SKIP` when Docker is unavailable.

## Run API locally

```bash
dotnet run --project src/HappyGymStats.Api
```

### When pinning a URL/port

If you set `ASPNETCORE_URLS` for deterministic verification, use `--no-launch-profile` so `launchSettings.json` does not silently override your binding:

```bash
ASPNETCORE_URLS="http://127.0.0.1:5181" \
HAPPYGYMSTATS_SURFACES_CACHE_DIR="$(pwd)/web/data/surfaces" \
dotnet run --no-launch-profile --project src/HappyGymStats.Api
```

## Blazor and AdminPanel local run contract

Blazor and AdminPanel service names and routes are part of the deployment contract:

- `happygymstats-blazor` → loopback `127.0.0.1:5182` (public `/`)
- `happygymstats-adminpanel` → loopback `127.0.0.1:5048` (public `/admin/*`)

If you have those projects in a separate checkout, run them there with pinned `ASPNETCORE_URLS` and `--no-launch-profile` (same warning as API). If you do not have local source, validate the contract via smoke checks (below).

## Local verification scripts and commands

### API + surfaces contract (local)

```bash
bash scripts/verify/s05-local-surfaces.sh
```

This script:
1. Starts API with deterministic loopback URL.
2. Enqueues an import.
3. Verifies local surfaces cache `meta.json` and `latest.json` shape.

### Production-shape smoke contract (local or remote)

```bash
bash scripts/verify/production-smoke.sh
```

Remote mode:

```bash
SMOKE_MODE=remote bash scripts/verify/production-smoke.sh
```

### Docs/runtime drift checks

```bash
bash scripts/verify/s05-production-smoke-contract.sh
bash scripts/verify/s06-deploy-script-contract.sh
```

## Operator quick checks

```bash
# API health (local loopback)
curl -fsS http://127.0.0.1:5047/api/v1/torn/health

# External API health (through nginx)
curl -fsS https://torn.geromet.com/api/v1/torn/health

# Admin health (external)
curl -fsS https://admin.geromet.com/admin/health
```

If these fail, use `scripts/verify/production-smoke.sh` first for categorized diagnostics before changing deployment state.
