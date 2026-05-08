# Helper Curl Commands for Torn Log Samples (Underused Log Types)

_Last updated: 2026-05-08_

This is a copy-paste command list for helpers to fetch Torn log examples for selected underused log types.

---

## 0) Setup

Optional helper var for manual curl use:

```bash
export TORN_BASE='https://api.torn.com/v2/user/log'
```

Quick sanity check (manual):

```bash
curl -sS "https://api.torn.com/v2/user/basic?key=<YOUR_KEY>" | jq '{id: .profile.id, name: .profile.name}'
```

---

## 1) Generic reusable command pattern

Raw JSON page for one log type:

```bash
curl -sS "${TORN_BASE}?log=<LOG_TYPE_ID>&limit=100&key=<YOUR_KEY>" | jq
```

Compact sample view:

```bash
curl -sS "${TORN_BASE}?log=<LOG_TYPE_ID>&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, ts: .timestamp, title: .details.title, category: .details.category, typeId: .details.id, data}'
```

---

## 2) Personal log types

5915 Property kick
```bash
curl -sS "${TORN_BASE}?log=5915&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

5916 Property kick receive
```bash
curl -sS "${TORN_BASE}?log=5916&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

5963 Education complete
```bash
curl -sS "${TORN_BASE}?log=5963&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2051 Item finish book
```bash
curl -sS "${TORN_BASE}?log=2051&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2052 Item finish book strength increase
```bash
curl -sS "${TORN_BASE}?log=2052&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2053 Item finish book speed increase
```bash
curl -sS "${TORN_BASE}?log=2053&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2054 Item finish book defense increase
```bash
curl -sS "${TORN_BASE}?log=2054&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2055 Item finish book dexterity increase
```bash
curl -sS "${TORN_BASE}?log=2055&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2056 Item finish book working stats increase
```bash
curl -sS "${TORN_BASE}?log=2056&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2057 Item finish book list capacity increase
```bash
curl -sS "${TORN_BASE}?log=2057&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2058 Item finish book merit reset
```bash
curl -sS "${TORN_BASE}?log=2058&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2059 Item finish book drug addiction removal
```bash
curl -sS "${TORN_BASE}?log=2059&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2120 Item use parachute
```bash
curl -sS "${TORN_BASE}?log=2120&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2130 Item use skateboard
```bash
curl -sS "${TORN_BASE}?log=2130&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2140 Item use boxing gloves
```bash
curl -sS "${TORN_BASE}?log=2140&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

2150 Item use dumbbells
```bash
curl -sS "${TORN_BASE}?log=2150&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

---

## 3) Company log types

6215 Job promote
```bash
curl -sS "${TORN_BASE}?log=6215&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6217 Job fired
```bash
curl -sS "${TORN_BASE}?log=6217&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6260 Company quit
```bash
curl -sS "${TORN_BASE}?log=6260&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6261 Company fire send
```bash
curl -sS "${TORN_BASE}?log=6261&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6262 Company fire receive
```bash
curl -sS "${TORN_BASE}?log=6262&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6267 Company rank change send
```bash
curl -sS "${TORN_BASE}?log=6267&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6268 Company rank change receive
```bash
curl -sS "${TORN_BASE}?log=6268&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6294 Company director change send
```bash
curl -sS "${TORN_BASE}?log=6294&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6295 Company director change receive
```bash
curl -sS "${TORN_BASE}?log=6295&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

---

## 4) Faction log types

6760 Faction tree upgrade set
```bash
curl -sS "${TORN_BASE}?log=6760&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6761 Faction tree upgrade unset
```bash
curl -sS "${TORN_BASE}?log=6761&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6762 Faction tree upgrade restore
```bash
curl -sS "${TORN_BASE}?log=6762&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6763 Faction tree upgrade unset entire branch
```bash
curl -sS "${TORN_BASE}?log=6763&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6764 Faction tree upgrade restore entire branch
```bash
curl -sS "${TORN_BASE}?log=6764&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6765 Faction tree branch select
```bash
curl -sS "${TORN_BASE}?log=6765&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6766 Faction tree war mode
```bash
curl -sS "${TORN_BASE}?log=6766&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6767 Faction tree optimize
```bash
curl -sS "${TORN_BASE}?log=6767&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6800 Faction create
```bash
curl -sS "${TORN_BASE}?log=6800&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6830 Faction change leader
```bash
curl -sS "${TORN_BASE}?log=6830&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6831 Faction change leader receive
```bash
curl -sS "${TORN_BASE}?log=6831&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6832 Faction change leader auto receive
```bash
curl -sS "${TORN_BASE}?log=6832&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6833 Faction change leader auto remove
```bash
curl -sS "${TORN_BASE}?log=6833&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6835 Faction change coleader
```bash
curl -sS "${TORN_BASE}?log=6835&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6836 Faction change coleader noone (legacy)
```bash
curl -sS "${TORN_BASE}?log=6836&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6837 Faction change coleader remove
```bash
curl -sS "${TORN_BASE}?log=6837&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

6838 Faction change coleader receive
```bash
curl -sS "${TORN_BASE}?log=6838&limit=100&key=<YOUR_KEY>" | jq '.log[] | {id, timestamp, details, data}'
```

---

## 5) Batch helper command

```bash
for id in 5915 5916 5963 2051 2052 2053 2054 2055 2056 2057 2058 2059 2120 2130 2140 2150 6215 6217 6260 6261 6262 6267 6268 6294 6295 6760 6761 6762 6763 6764 6765 6766 6767 6800 6830 6831 6832 6833 6835 6836 6837 6838; do c=$(curl -sS "${TORN_BASE}?log=${id}&limit=100&key=<YOUR_KEY>" | jq '.log | length'); echo "log=${id} count=${c}"; done
```

---

## 6) Safety notes

- Do not paste API keys into docs, commits, or issue comments.
- Use automated redacting scripts for sharable payloads:
  - `scripts/helpers/fetch-underused-log-samples.sh` (Linux/macOS)
  - `scripts/helpers/fetch-underused-log-samples.bat` (Windows CMD)
- Torn API terms/usage disclosure: `docs/TORN-API-TOS.md`
