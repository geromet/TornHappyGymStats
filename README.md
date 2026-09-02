# TornHappyGymStats

HappyGymStats is a Torn telemetry pipeline with a production ASP.NET API, a Blazor frontend, and an AdminPanel surface. The current deployment shape is API + Blazor + AdminPanel backed by Postgres, with Keycloak-protected admin/auth flows.

## Quick links

- Live Project: [HappyGymStats](https://torn.geromet.com/)
- Project overview: [docs/OVERVIEW.md](docs/OVERVIEW.md)
- Local setup/run: [docs/SETUP.md](docs/SETUP.md)
- Deployment: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)
- My stats operator gate (M004): [docs/M004-MY-STATS-OPERATOR-GATE.md](docs/M004-MY-STATS-OPERATOR-GATE.md)
- Audit context (2026-05-06-181943): [.gsd/milestones/M003/M003-ROADMAP.md](.gsd/milestones/M003/M003-ROADMAP.md)
- Production smoke verification: [`scripts/verify/production-smoke.sh`](scripts/verify/production-smoke.sh)
- Torn API key ToS disclosure: [docs/torn-api/terms-of-service.md](docs/torn-api/terms-of-service.md)

## Minimal verification commands

- `bash scripts/verify/m004-my-stats-final-gate.sh`
- `bash scripts/verify/production-smoke.sh`
- `bash scripts/verify/s05-local-surfaces.sh`

## Repo layout

- `src/HappyGymStats.Api` — production API (`/api/v1/torn/*`), import/surfaces endpoints, and the SignalR war hub
- `src/HappyGymStats.Blazor` — primary frontend for production user flows
- `src/HappyGymStats.AdminPanel` — operations/admin surface
- `src/HappyGymStats.Core` — Torn fetch, reconstruction, and war-engine logic
- `src/HappyGymStats.Data` — EF Core data layer (Postgres + provider integration)
- `src/HappyGymStats.Contracts` — shared entities and repository interfaces
- `src/HappyGymStats.Identity` — Keycloak auth extensions
- `src/HappyGymStats.Encryption` — ECIES pseudonymization primitives
- `src/HappyGymStats.WarPoller` — Torn war polling host
- `web/data/surfaces/` — generated local surfaces cache artifacts (gitignored) used by local verification scripts
- `tests/` — unit/integration verification suites
- `scripts/verify/` — bash gate harness (the project's CI equivalent)

## License

See [LICENSE](LICENSE).
