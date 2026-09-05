# Torn faction-members contract for war intelligence

_Last pinned: 2026-09-05 against Torn OpenAPI 6.6.1_

## Purpose

Issue #86 needs bounded opponent activity/travel observations from Torn faction member data. This file pins the provider contract that later sampling code may rely on so API drift is reviewable. It does **not** add polling, persistence, UI, or a second tracker subsystem.

Authoritative source: Torn API v2 OpenAPI (`https://www.torn.com/swagger/openapi.json`), version 6.6.1, base URL `https://api.torn.com/v2`.

## Endpoint

`GET /faction/{id}/members`

The OpenAPI contract describes this endpoint as requiring a **public access key** and returning `FactionMembersResponse`.

For #86, sample this endpoint only on a separately bounded cadence through the existing shared Torn request-budget/rate-limiter path. Do not fold it into the 5-second live-war polling budget and do not create a parallel flight/activity daemon.

## Fields #86 may consume

Each `FactionMember` exposes the member identity plus `last_action` and `status`. The observation pipeline should retain only fields needed for the documented activity/travel derivations and provenance.

### `last_action`

Required fields in `UserLastAction`:

- `status`: `Online`, `Idle`, or `Offline`;
- `timestamp`: Unix seconds (`int32`);
- `relative`: provider display text.

The timestamp is the machine source for activity recency. `relative` is not a stable clock or parsing contract.

### `status`

Required fields in `UserStatus`:

- `description`: provider status text;
- `details`: string or null;
- `state`: a known status enum or another provider string;
- `until`: Unix seconds (`int32`) or null;
- `color`: provider display value.

Current documented status enum values include `Abroad`, `Awoken`, `Dormant`, `Fallen`, `Federal`, `Hospital`, `Jail`, `Okay`, and `Traveling`.

`plane_image_type` is optional and is documented as populated only while `state == Traveling`; current values are `private_jet`, `light_aircraft`, and `airliner`.

## Mapping guardrails

- Capture what Torn actually returned; missing values are unknown, not zero activity or "in Torn".
- Do not infer a destination, plane type, return time, or attackable time from absent fields.
- `until` may be null. A travel/status ETA exists only when the authoritative payload or a separately documented deterministic derivation supports one.
- A newer authoritative status observation supersedes an older estimate.
- `last_action.status` describes observed activity recency; it is not evidence of sleep/work biography or opponent intent.
- `Traveling` is not synonymous with inactive.
- Retain source observation time/freshness on derived output.
- Only future observations should populate the activity history. Do not synthesize historical samples from the current roster.

## Sanitized review fixture

`tests/fixtures/war/faction-members-sanitized.json` is synthetic data shaped from this contract. IDs, names, timestamps, and text are examples only; the fixture contains no production/player data and must not be treated as a captured Torn response.

The fixture intentionally includes:

1. an online member with an ordinary `Okay` status;
2. a traveling member with authoritative `until` plus conditional `plane_image_type`;
3. an abroad/offline member with no timing, proving missing timing cannot manufacture an ETA.

## Scope boundary

Pinning this contract satisfies only the API-shape evidence prerequisite for the remaining #86 observation work. It does not by itself satisfy #86's bounded sampler, deduplication/retention, T4 persistence proof, identity-bound readiness API, or rendered T2 planner UX.
