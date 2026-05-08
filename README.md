# TornHappyGymStats

HappyGymStats is a Torn telemetry pipeline with a production ASP.NET API, a Blazor frontend, and an AdminPanel surface. The current deployment shape is API + Blazor + AdminPanel backed by Postgres, with Keycloak-protected admin/auth flows.

## Quick links

- Project overview: [docs/OVERVIEW.md](docs/OVERVIEW.md)
- Local setup/run: [docs/SETUP.md](docs/SETUP.md)
- Deployment: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)
- Audit context (2026-05-06-181943): [.gsd/milestones/M003/M003-ROADMAP.md](.gsd/milestones/M003/M003-ROADMAP.md)
- Production smoke verification: [`scripts/verify/production-smoke.sh`](scripts/verify/production-smoke.sh)
- Torn API key ToS disclosure: [docs/TORN-API-TOS.md](docs/TORN-API-TOS.md)

## Minimal verification commands

- `bash scripts/verify/production-smoke.sh`
- `bash scripts/verify/s05-local-surfaces.sh`

## Repo layout

- `src/HappyGymStats.Api` — production API (`/api/v1/torn/*`) and import/surfaces endpoints
- `src/HappyGymStats.Blazor` — primary frontend for production user flows
- `src/HappyGymStats.AdminPanel` — operations/admin surface
- `src/HappyGymStats.Core` — fetch and reconstruction logic
- `src/HappyGymStats.Data` — EF Core data layer (Postgres + provider integration)
- `src/HappyGymStats.Cli` — CLI workflows and local utilities
- `web/` — legacy static frontend artifacts (historical/legacy path)
- `tests/` — unit/integration/web verification suites

## License

See [LICENSE](LICENSE).
