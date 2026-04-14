# TornHappyGymStats

A cross-platform console tool that fetches your Torn gym log history, reconstructs your happy value before each gym train, and exports everything to an analysis-ready CSV.

## What it does

1. **Fetch** — Downloads your full Torn API v2 `/user/log` history (gym & happy category) into a local JSONL dataset. Supports cancel (Ctrl+C) and resume without duplicating records.
2. **Reconstruct** — You enter your current happy value, and the tool traces backwards through quarter-hour regen ticks (+5 per tick) and gym train inversions to compute your happy *before* each gym train. Max-happy changes from property/rentals are applied as a ceiling. Raw data is never modified.
3. **Export** — Writes a CSV with dynamic union-of-keys columns (one column per JSON field observed across all logs) plus 6 derived reconstruction columns for gym train rows.

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

## Building from source

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build
dotnet test                           # 57 tests
bash scripts/publish-all.sh            # builds all 6 RIDs to dist/
```

## Tech stack

- .NET 8, C# 12
- Spectre.Console (interactive CLI)
- System.Text.Json (streaming JSONL processing)
- xUnit (57 tests including E2E pipeline tests)

## License

See [LICENSE](LICENSE).
