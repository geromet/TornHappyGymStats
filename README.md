# TornHappyGymStats

A cross-platform toolset for fetching Torn gym log history, reconstructing happy values, exporting analysis-ready datasets, and serving the same derived data through a small SQLite-backed web API.

## What it does

1. **Fetch** — Downloads your full Torn API v2 `/user/log` history (gym & happy category) into a local JSONL dataset. Supports cancel (Ctrl+C) and resume without duplicating records.
2. **Reconstruct** — You enter your current happy value, and the tool traces backwards through quarter-hour regen ticks (+5 per tick) and gym train inversions to compute your happy *before* each gym train. Max-happy changes from property/rentals are applied as a ceiling. Raw data is never modified.
3. **Export** — Writes a CSV with dynamic union-of-keys columns (one column per JSON field observed across all logs) plus 6 derived reconstruction columns for gym train rows.
4. **Serve** — Exposes read-only `/v1` endpoints from a local SQLite database for a future web frontend.
5. **Preview on the web** — Includes a static dashboard skeleton in `web/` that can be deployed to GitHub Pages and pointed at a separately hosted API.

## Quick start

Download the latest release for your platform from the [Releases](https://github.com/geromet/TornHappyGymStats/releases) page.

**Linux/macOS:**
```bash
chmod +x HappyGymStats
./HappyGymStats
```

**Windows:**
```
HappyGymStats.exe
```

You'll need a **Torn FULL access API key** (entered at runtime, never stored on disk).
The API key is only used to fetch relevant logs. It is never stored or shared. All data stays local. The only endpoint used is https://api.torn.com/v2/user/log?cat=25

## Menu actions

| Action | Description |
|--------|-------------|
| **Fetch logs** | Start fetching your log history from the Torn API |
| **Resume fetch** | Continue from where you left off after cancel/close |
| **Reconstruct happy** | Enter current happy → derives happy-before-train for every gym log |
| **Export CSV** | Writes `data/export/userlogs.csv` with all fields + derived columns |
| **Show status** | Shows data paths, last fetched log, checkpoint state |
| **Configure throttle** | Adjust the delay between API requests (default: 1100ms) |

## How reconstruction works

Torn gym train logs contain `happy_used` but not your total happy at the time. Reconstruction works backwards from **right now**:

- You provide your current happy (visible in-game)
- The tool walks backwards through your log history, newest to oldest
- Between events, it subtracts the +5 regen per UTC quarter-hour tick that would have occurred
- At each gym train: `happy_before = happy_after + happy_used`
- Max-happy changes (property gains/losses) are applied as a ceiling with a deterministic "do not unclamp" policy

## Output CSV columns

**Standard columns** (one per unique JSON key across all logs, e.g.):
`id, timestamp, details.title, details.category, data.trains, data.energy_used, data.happy_used, ...`

**Derived columns** (appended for every row, populated for gym trains):
| Column | Description |
|--------|-------------|
| `happy_before_train` | Reconstructed happy immediately before the gym train |
| `happy_after_train` | Happy after the train (before - used) |
| `regen_ticks_applied` | Quarter-hour regen ticks between this and the next event |
| `regen_happy_gained` | Happy gained from regen (+5 per tick) |
| `max_happy_at_time_utc` | Max-happy ceiling at the time of the train |
| `clamped_to_max` | Whether the before-train value was clamped to max |

## Privacy

The app warns you at startup and before export: **your gym logs, happy values, and CSV exports can reveal in-game activity patterns.** Do not share them with anyone you don't fully trust.

## Running from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### CLI

From the project root, use the helper script to build the solution and start the CLI:

```bash
./run.sh
```

Runs launched through `./run.sh` write runtime data to the project-root `data/` directory.

Legacy file workflows can now be migrated into SQLite:

```bash
dotnet run --project src/HappyGymStats -- migrate-legacy-db
```

By default that creates or updates `data/happygymstats.db`. Override with:

- `--db /absolute/path/to/happygymstats.db`
- `HAPPYGYMSTATS_DATABASE=/absolute/path/to/happygymstats.db`

### API

Start the SQLite-backed API locally:

```bash
dotnet run --project src/HappyGymStats.Api
```

By default the API stores its SQLite database at `src/HappyGymStats.Api/data/happygymstats.db`.
Override with either:

- `ConnectionStrings__HappyGymStats=/absolute/path/to/happygymstats.db`
- `HAPPYGYMSTATS_DATABASE=/absolute/path/to/happygymstats.db`

Available read endpoints:

- `GET /v1/health`
- `GET /v1/gym-trains?limit=5`
- `GET /v1/happy-events?limit=5`

The API currently allows cross-origin `GET` requests so the static dashboard can read from it.
That is intentional for this wave's read-only frontend skeleton, but it is **not** a final auth/CORS design for future write/import endpoints.

If you want the API to read the same database generated by `migrate-legacy-db`, point it at that file explicitly with `HAPPYGYMSTATS_DATABASE` or `ConnectionStrings__HappyGymStats`.

### Static web preview

Serve the `web/` folder locally with any static file server:

```bash
python3 -m http.server 8000 --directory web
```

Then open `http://localhost:8000` and set the API base URL to your running API, usually `http://localhost:5047`.
You can also preconfigure it via query string:

```text
http://localhost:8000/?api=http://localhost:5047
```

## Building from source

```bash
dotnet build
dotnet test
bash scripts/publish.sh --target cli   # builds CLI for default RIDs to dist/cli/
bash scripts/publish.sh --target api   # builds API for default RIDs to dist/api/
bash scripts/publish.sh --target all --rid linux-x64 --rid osx-arm64
```

## Deployment

### Frontend (GitHub Pages)

`main` pushes automatically deploy `web/` using `.github/workflows/pages.yml`.

### Backend (`www.geromet.com` via Cloudflare Access SSH)

Use:

```bash
bash scripts/deploy-backend.sh
```

This script:
- publishes `src/HappyGymStats.Api` to `dist/backend-api/`
- connects via your Cloudflare Access SSH tunnel
- uploads to a timestamped release directory
- flips `current` symlink
- restarts the backend systemd service

Default SSH transport matches your setup:

```bash
ssh -i ~/.ssh/id_token2_bio3_hetzner anon@ssh.geromet.com \
  -o ProxyCommand="cloudflared access ssh --hostname ssh.geromet.com"
```

Optional overrides can be placed in `.env.deploy` (kept local):

```bash
DEPLOY_SSH_HOST=ssh.geromet.com
DEPLOY_SSH_USER=anon
DEPLOY_SSH_KEY=~/.ssh/id_token2_bio3_hetzner
DEPLOY_PROXY_COMMAND='cloudflared access ssh --hostname ssh.geromet.com'
DEPLOY_REMOTE_ROOT=/var/www/happygymstats
DEPLOY_REMOTE_SERVICE=happygymstats-api
DEPLOY_CONFIGURATION=Release
DEPLOY_RUNTIME=linux-x64
DEPLOY_USE_SUDO=1
DEPLOY_SUDO_NON_INTERACTIVE=0
DEPLOY_RESTART_SERVICE=1
```

### Sudo behavior

By default, deployment uses interactive `sudo` on the remote host when privileged commands are needed. If remote sudo prompts for a password, enter it in your terminal.

- `DEPLOY_USE_SUDO=1` — use sudo for privileged remote operations (default).
- `DEPLOY_SUDO_NON_INTERACTIVE=0` — allow interactive sudo prompts (default).
- `DEPLOY_SUDO_NON_INTERACTIVE=1` — force non-interactive sudo (`sudo -n`), fail fast if passwordless sudo is not configured.
- `DEPLOY_USE_SUDO=0` — do not use sudo (requires user-writable deploy root + user-manageable service flow).


If backend deploy runs as `anon` and writes to `/var/www/...`, configure passwordless sudo for only the commands used by `scripts/deploy-backend.sh`.

Create `/etc/sudoers.d/happygymstats-deploy` (via `visudo -f /etc/sudoers.d/happygymstats-deploy`) with:

```sudoers
Defaults:anon !requiretty
Cmnd_Alias HGS_DEPLOY = /usr/bin/mkdir, /usr/bin/rsync, /usr/bin/ln, /usr/bin/systemctl
anon ALL=(root) NOPASSWD: HGS_DEPLOY
```

Then validate on the server:

```bash
sudo -l
```

You should see NOPASSWD access for those commands. If your distro places binaries elsewhere, adjust absolute paths (`command -v mkdir rsync ln systemctl`).

## Storage mode after wave 7

- `userlogs.jsonl`, derived JSONL sidecars, and `checkpoint.json` remain the legacy interchange/backup format.
- `migrate-legacy-db` imports those legacy artifacts into SQLite.
- `Export CSV` now prefers SQLite only when the database exists and is at least as fresh as the legacy inputs; otherwise it falls back to legacy files.
- Static web and API work should treat SQLite as the primary read model once the migration snapshot is current.

## GitHub Pages deployment shape

The repository now includes `.github/workflows/pages.yml`, which publishes the `web/` directory to GitHub Pages.

Important boundary:

- **GitHub Pages hosts the frontend only**.
- The .NET API and SQLite database still need a separate host with writable storage.
- The static dashboard expects a configured API base URL; it does not embed secrets or call Torn directly.

## Tech stack

- .NET 8, C# 12
- ASP.NET Core minimal API + EF Core + SQLite
- Static HTML/CSS/JavaScript dashboard for GitHub Pages
- Spectre.Console (interactive CLI)
- System.Text.Json (streaming JSONL processing)
- xUnit integration and dataset tests

## License

See [LICENSE](LICENSE).
