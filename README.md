# TornHappyGymStats

HappyGymStats ingests Torn log data, builds derived gym/happy timelines, stores the result in SQLite, and exposes it through a small API plus static dashboard.

## Quick links

- Project overview: [docs/OVERVIEW.md](docs/OVERVIEW.md)
- Local setup/run: [docs/SETUP.md](docs/SETUP.md)
- Deployment: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)
- Torn API key ToS disclosure: [docs/TORN-API-TOS.md](docs/TORN-API-TOS.md)

## Repo layout

- `src/HappyGymStats.Cli` — CLI workflows
- `src/HappyGymStats.Api` — backend API (`/api/v1/torn/*`)
- `src/HappyGymStats.Core` — fetch/reconstruction logic
- `src/HappyGymStats.Data` — SQLite + EF Core
- `web/` — static frontend
- `tests/` — test suite

## License

See [LICENSE](LICENSE).
