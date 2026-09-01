# Modifier Provenance Taxonomy (S01/T02)

## Purpose

This artifact defines the provenance taxonomy used by downstream scoring work to reason about modifier evidence quality across personal, faction, and company contexts. It is grounded in the source anchors captured in T01 (`endpoint-log-anchor-inventory.md`) and preserves the slice boundary: API/CLI -> Core -> Data+Visualizer.

## Source Anchors

- T01 research artifact: `.gsd/milestones/M002/slices/S01/research/endpoint-log-anchor-inventory.md`
- API contract anchors: `src/HappyGymStats.Api/Program.cs`
- Import/fetch anchors: `src/HappyGymStats.Api/ImportService.cs`, `src/HappyGymStats.Core/Torn/TornApiClient.cs`
- Extractor anchors: `src/HappyGymStats.Core/Reconstruction/LogEventExtractor.cs`

## Key Scope Requirements

| Scope | Required Key Ownership | Minimum Access Expectation | Current State (T01) | Owner for Gap Closure |
|---|---|---|---|---|
| Personal | User-owned API key for the subject account | Access to `v2/user/log` stream currently filtered by `cat=25` | Confirmed in import entrypoint and Torn client fetch flow | Core/Data team (already implemented baseline fetch) |
| Faction | API key with faction-visible log access for the same subject context | Ability to fetch faction-related modifier events and correlate with user timeline | Not explicitly modeled in current import URI or extraction paths | Integrations + product owner to define compliant acquisition path |
| Company | API key with company-visible log access for the same subject context | Ability to fetch company-related modifier events and correlate with user timeline | Not explicitly modeled in current import URI or extraction paths | Integrations + product owner to define compliant acquisition path |

## Taxonomy Matrix

| Evidence Dimension | Scope | Endpoint / Fetch Anchor | Payload Field Candidates | Log Category/ID Knowledge | Confidence Impact | Anchor(s) |
|---|---|---|---|---|---|---|
| Gym train happy usage | Personal | `FetchOptions.Default("https://api.torn.com/v2/user/log?cat=25")` -> `GetUserLogPageAsync` | `data.happy_used`, `timestamp`, `id` | **Category:** cat=25 confirmed at fetch entry. **Log IDs:** event-level IDs observed but semantic mapping not yet enumerated | High positive confidence when present (direct numeric signal for reconstruction) | T01 sections: Torn Fetch Entry, Extractor Fields |
| Max happy transition | Personal | Same as above | `data.maximum_happy_after`, `data.maximum_happy_before`, fallback numeric parse in `data` + title tokens | **Category:** cat=25 confirmed. **Log IDs:** hypothesized subgroup of max-happy records; requires row-level cataloging | Medium-high confidence (direct fields high; title/numeric heuristic medium) | T01 sections: Torn Fetch Entry, Extractor Fields |
| Overdose modifier loss | Personal | Same as above | `data.happy_decreased`, title token for drug class (`ecstasy|ketamine|lsd|pcp|shrooms|speed|xanax`) | **Category:** cat=25 confirmed. **Log IDs:** hypothesized overdose subset not cataloged by ID yet | Medium confidence (rule-based classification from title + delta) | T01 sections: Torn Fetch Entry, Extractor Fields |
| Generic happy delta (non-gym/non-overdose) | Personal | Same as above | `data.happy_increased`, `data.happy_decreased`, optional `details.category`/`details.title` | **Category:** cat=25 confirmed. **Log IDs:** unknown; broad bucket pending normalization | Medium-low confidence (ambiguous causality) | T01 sections: Torn Fetch Entry, Extractor Fields |
| Faction modifier contribution | Faction | No confirmed endpoint/fetch in current import flow | Candidate fields unknown in local code; likely timeline + modifier-type fields in faction-visible logs | **Category/ID:** hypothesized only (no local anchor yet) | Confidence penalty when absent; cannot currently distinguish true-zero from missing-scope | T01 sections: Known Gaps (scope attribution, taxonomy absent) |
| Company modifier contribution | Company | No confirmed endpoint/fetch in current import flow | Candidate fields unknown in local code; likely timeline + modifier-type fields in company-visible logs | **Category/ID:** hypothesized only (no local anchor yet) | Confidence penalty when absent; cannot currently distinguish true-zero from missing-scope | T01 sections: Known Gaps (scope attribution, taxonomy absent) |

## Confidence Impact Mapping

Use these weights as deterministic guidance for S02/S03 scoring integration (subject to later calibration once additional evidence classes are implemented):

| Evidence State | Confidence Delta | Rationale |
|---|---:|---|
| Direct personal numeric field present (e.g., `happy_used`, `maximum_happy_after`) | +0.30 | Strong, explicit quantitative anchor in current extractor model |
| Personal heuristic classification required (title-token / fallback numeric inference) | +0.15 | Useful but less reliable than explicit schema fields |
| Personal category observed but no usable modifier field present | +0.00 | Signal exists but does not increase confidence in modifier attribution |
| Expected personal event missing in interval with otherwise dense logs | -0.10 | Mild uncertainty from potential partial capture or parser miss |
| Faction scope required by model but unavailable (no eligible key/fetch path) | -0.25 | Material blind spot; unobserved faction effects can bias attribution |
| Company scope required by model but unavailable (no eligible key/fetch path) | -0.25 | Material blind spot; unobserved company effects can bias attribution |
| Both faction and company scopes unavailable | -0.40 | Compounding provenance incompleteness across non-personal domains |

## Open Unknowns

1. **Faction/company fetch contract is undefined in code**
   - We have no confirmed import URL/category mapping comparable to current personal `v2/user/log?cat=25` usage.
   - Dependency owner: Integrations + product owner to define permitted endpoint/key model.

2. **Log ID catalog has not been materialized**
   - Per-event `id` is captured by `TornApiClient`, but we do not yet maintain a deterministic ID->modifier semantic table.
   - Dependency owner: Core reconstruction to add extraction-time taxonomy tagging.

3. **Scope attribution is absent from read models**
   - Current public API surfaces reconstruction outputs without explicit provenance-scope completeness metadata.
   - Dependency owner: Core + API boundary work in downstream slices.

4. **Heuristic durability is unproven**
   - Title-token and fallback-number heuristics may drift with upstream payload changes.
   - Dependency owner: Drift-check automation (T03) and future fixture-backed parser tests.

## Layering Boundary Guardrail

This taxonomy is a contract artifact only. It does not introduce cross-layer calls or data-path shortcuts. Any implementation that operationalizes these rows must preserve API/CLI -> Core -> Data+Visualizer boundaries defined in S01.
