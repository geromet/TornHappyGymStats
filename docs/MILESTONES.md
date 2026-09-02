# War-command milestones (M007+)

Derived from the hand-off pack in `data/V2/`. This is a fresh milestone breakdown for
the work that remains after the live war board (hand-off M1) and scouting (hand-off M2)
were built as GSD milestones M004–M006.

## Ground rules for reading this

- **Numbering.** GSD owns a live milestone registry using `M001`–`M006`. This document
  continues that sequence from `M007` so nothing collides. Each entry cross-references
  the hand-off document it is based on: `→ data/V2/handoff/NN`.
- **Authoritative source.** `data/V2/` is the current pack. `data/` (no `V2`) is a stale
  subset — only `handoff/00-brief.md` differs, and `data/` is missing hand-off docs
  05–11 entirely. Cite `data/V2/...` paths only.
- **This plan does not touch GSD.** `GSD/STATE.md` / `GSD/ROADMAP.md` are the external
  tool's state and are edited there, not here. `GSD/STATE.md` is currently stale (still
  shows M006/S02 executing though PR #30 merged) — left as-is deliberately.
- **Two standing non-goals, every milestone below:**
  1. **No game actions, ever.** No code path in M007–M013 may issue a state-changing
     request to Torn — no auto-attack, refill, travel, or scripted click. Links are
     plain anchors a human clicks. (`data/V2/handoff/00-brief.md`, Hard constraints.)
  2. **The `Ecies` scheme must not be reused for the war key vault.** It encrypts to a
     client-held public key so the server *cannot* decrypt — useless for a key the
     server must use unattended. M009 uses envelope encryption keyed off
     `WAR_KEY_MASTER`. (`data/V2/handoff/07-milestone-4-member-linking.md`.)

## Gates — stop-and-report points, not ordinary tasks

| Gate | Milestone | Rule |
|---|---|---|
| Chain-endpoint lookup sweep | M008 | Run `chain` / `chainreport` / `chains` selections and record what they return **before** designing any timer UI. Decides whether the lapse timer is real or inferred. |
| FF-formula validation | M010 | Compare `FF = min(3, 1 + (8/3)·def/att)` against FFScouter's own fair-fight figure across a full roster. If they disagree, the 0.75× targeting rule is wrong — **M010 halts** until understood. Record the outcome in `data/V2/reference/scoring-formula.md` either way. |
| Gear-tracker spike | M012 | First task is a written finding, not a build. Determine whether enemy gear is obtainable through the documented API at all. A documented dead end is an acceptable, complete outcome. |
| Backtest vs naive baseline | M013 | The harness must retrodict finished wars better than "extrapolate current score rates linearly". If it does not, **stop** and publish that. The comparison is published whether or not it is favourable. |

---

## M007 — Conformance sweep: close M1/M2 acceptance gaps

**Why it exists.** M006 was marked COMPLETE, but a check against hand-off M1 and M2
acceptance criteria (`data/V2/handoff/04`, `data/V2/handoff/05`) found stated
deliverables that were not built or were built differently. This milestone closes them
before new features stack on top. Slices are independent; size is set by the findings
below. Two findings were re-scoped once checked against the tree: S01 (lump detection
existed as a dampener, not real detection) and S03 (`OpenTarget` holes already existed
but were mis-gated) — see each slice.

**Branch layout.** The slices land as a **stack**, not on `main` yet: `feat/m007-s01-…`
off `main` (carries this plan doc), `feat/m007-s02-…` off S01, etc. Look for completed
slices on their branch, not `main`, until the stack is merged.

### S01 — Ranked-war lump detection rework  *(→ data/V2/handoff/05)*  — DONE (branch `feat/m007-s01-lump-detection`)

The shipped `OpponentMemberProfile.LumpAdjustedScorePerWar` is the **median of per-war
score** — an outlier dampener. Hand-off M2 specifies actual milestone-lump detection,
and the data to do it exactly as written is present:
`RankedWarReportMemberEntity.Attacks` exists (per-war attack count), so the memory note
claiming "report rows carry no per-attack granularity" is wrong.

Build to the spec:
- Per member per war: `residual = score − (attacks × faction_median_score_per_attack)`,
  where the baseline is the median of every `(member, war)` score/attack for the faction
  (zero-attack wars excluded). Exposed as `FactionScoutProfile.MedianScorePerAttack`.
- If `residual` matches a `ChainEngine.MilestoneBonuses` value within a tolerance, flag
  that war as a probable milestone lump: drop it from the per-war median
  (`LumpAdjustedScorePerWar`), subtract the matched bonus from that war before taking the
  median of per-war score/attack (`LumpAdjustedScorePerAttack`), count it
  (`LumpWarCount`). Roster is ranked by `LumpAdjustedScorePerAttack`.
- Surface **both** figures — raw (`AverageScorePerAttack`, `RawMedianScorePerWar`,
  min/max) and lump-adjusted — because "who lands crossing hits" is itself worth knowing.
- **Fixture test:** DerDoruk's war-48377 row must be flagged, and his lump-adjusted
  score/attack must land near the faction median rather than ~3× it. Known case, known
  right answer (`data/V2/reference/data-layer.md`, correction section).

**As built — two deliberate deviations from a literal reading of the spec:**
- **Tolerance is `|residual − bonus| ≤ 12% of bonus`, not literal rounding.** The
  baseline is the *faction* median, so an above-median member's residual drifts by
  `≈ attacks × (their rate − faction rate)` before any lump. Literal rounding misses real
  lumps on strong members; too loose discards a strong-but-lumpless member's best war and
  understates the opponent. Tests pin both edges.
- **Only bonuses ≥ 100 are matched** (chain milestones 250+). A residual the size of the
  10/20/40/80 bonuses is within ordinary per-war variance against a faction-median
  baseline and would false-positive normal above-average wars. **Known blind spot:** a war
  that crossed several milestones at once has a residual near a *sum* of bonuses and is
  not detected. Both choices are `const`s in `OpponentProfileEngine`, retunable, and
  should get a real flag-rate measurement against a full roster in **S05**.

### S02 — `TornRateLimiter`  *(→ data/V2/handoff/04, task 3; data/V2/handoff/03 "Rate limiting")*  — DONE (branch `feat/m007-s02-rate-limiter`, stacked on S01)

`Core/Torn/TornRateLimiter` — per-key token bucket, default 80/min ceiling (below Torn's
100), continuous fractional refill, `TimeProvider`-injected for deterministic tests.
- **Per-key dimension is a non-reversible hash** (`KeyIdentity` = SHA-256 first 8 bytes,
  hex) so the raw key never becomes a dictionary key or reaches a log.
- **Priority shedding** via per-priority token reserves — `TornRequestPriority`
  `Roster (0) < WarState (10%) < AttacksFull (25%) < Other (40%)`; as the bucket drains,
  low priority is refused first, rosters last.
- **`code 5` / HTTP 429 back-off**: `ReportThrottled` drains the bucket and opens an
  exponential-backoff window (base 2s, ×2 per consecutive hit, cap 2min) with ±50%
  jitter; `ReportSuccess` clears the escalation once outside the window.
- `TryAcquire` (non-blocking, returns `RetryAfter`) + `AcquireAsync` (waits via the
  `TimeProvider`, honours cancellation, re-checks ≥ every 5s).
- Wired into `TornApiClient` as an **optional** ctor dep (`TornRateLimiter? = null`, so
  the ~8 `new TornApiClient(http)` test call sites still compile); every `GetAsync` path
  acquires before send and reports throttle/success. Per-endpoint priority: live
  `wars`/`warfareranked` → `WarState`, `attacks` → `AttacksFull`, history backfill +
  `rankedwarreport` + player-id/user-log → `Other`. Registered `AddSingleton` in both
  `Api` and `WarPoller` `Program.cs`.
- 11 tests: ceiling exhaustion, continuous refill + cap, full shedding order, per-key
  isolation, back-off window blocks all priorities, consecutive-throttle escalation,
  `ReportSuccess` reset, `KeyIdentity` stability/non-exposure, `AcquireAsync` wait +
  cancellation.

**Not done here:** the poll *scheduler* (5s cycle, per-linked-member `attacksfull`) —
that's M009/M010. This slice is the limiter primitive + universal wiring only.

### S03 — Open-slot holes  *(→ data/V2/handoff/04, "Definition of a hole")*  — DONE (branch `feat/m007-s03-open-slot-holes`, stacked on S02)

**Premise was wrong.** `WarHoleKind.OpenTarget` already existed, was derived, mapped to
the DTO, and asserted in two tests — the S01-planning grep (`hole|coverage|idle`) just
couldn't match the string `OpenTarget`. So this slice is a **behavioural fix to a
mis-gated derivation**, not an additive one.

Two conflations fixed in `WarStateDerivationEngine.DeriveHoles`:
- Open-target holes were emitted **only when our own faction had ≥ 1 idle attacker**
  (`if (opponent is null || idleMembers.Length == 0) continue;`). The hand-off makes an
  open slot a first-class board object — "who is free" and "who is available to hit" are
  the same question — so it must not depend on our idlers. Gate removed.
- The target filter excluded `!member.IsIdleAttacker`, silently dropping idle enemies —
  who are *prime* targets. Removed; an idle enemy is now both faction A's idle-attacker
  hole and faction B's open slot.
- Hospitalised / abroad enemies were already excluded (they aren't `Available`); kept,
  with a fixture proving a hospitalised enemy is a regenerating slot, not a hole.

New fields (additive, so the shipped `CoverageRatio` = roster participation is untouched
and re-labelled on the board, not repurposed):
- `WarDerivedFactionState.OpenTargetCount` — attackable members of the opposing faction.
- `WarDerivedFactionState.TargetCoverageRatio` — the hand-off's coverage ratio:
  attackable enemies ÷ this faction's available attackers. **Proxy** — the denominator
  should be "members with energy", which needs tier-1 key data (M009); until then it is
  the available-member count. Labelled as a proxy in the model and on the board.
- `WarDerivedState.OpenTargetCount` — board-wide total.
- Board: top "Coverage ratio" tile → "Participation"; new "Open targets" tile; per-faction
  card gains a correctly-defined "Coverage ratio" (= `TargetCoverageRatio`).

**KNOWN INCOMPLETE:** the hand-off's open slot is "attackable ... with **no live claim**
against them". Claims arrive with **M010** (`ClaimTarget` on `WarHub`). Until then every
attackable target is reported; M010 must add a claim filter in `DeriveHoles` (comment
left at the call site).

Tests: the two fixture tests reworked to the corrected behaviour (war-48377 shape → 4
holes incl. 2 open slots from the side with available members), plus new focused tests —
open slots without our idlers, hospitalised/abroad enemy is not a slot, idle enemy *is* a
slot, `TargetCoverageRatio` math. w03 + w04 verifiers green.

### S04 — Faction-level scout profile  *(→ data/V2/handoff/05, "Faction-level profile")*  — DONE (branch `feat/m007-s04-faction-scout-profile`, stacked on S03)

`FactionScoutProfile` gained the faction summary the hand-off names, all computed in
`OpponentProfileEngine.BuildFactionMetrics` from stored history + report rows:
- **`WinRate`** + `WarsWithKnownOutcome` — wars won ÷ wars with a recorded
  `WinnerFactionId`.
- **`TypicalTargetScore`** — the scouted faction's *own* median final score = what an
  opponent must outscore to beat them (`data/V2/reference/data-layer.md`, "Against a 7300
  target"). Not `max(both finals)` — a timeout win can end behind on raw points.
- **`PointsPerHour`** (`decimal?`) — median of the scouted faction's own final score ÷
  war duration; `null` when no war carries both.
- **`TypicalRosterSize`** — median distinct members fielded per war.
- **`Top5ScoreShare` / `Top10ScoreShare`** — **per-war** top-5 / top-10 share of that
  war's points, median across wars. The hand-off's "top 5 produced 60%" is a single-war
  (48377) figure; an all-time aggregate would drift upward with history length as
  long-tenured members accumulate. A high value makes lockdown viable — M010 consumes it.

Every metric **degrades to `0` / `null`** when the history rows are sparse (no winner,
no finals, no end time) — a real condition for a lightly-backfilled war, and asserted.
Score attribution follows which side the scouted faction was on in each row
(`FactionId` vs `OpponentFactionId`).

DTOs (`FactionScoutDto` in both `Api/Models` and `Blazor/Models`) + mapper updated. The
scout page gains a second summary row: Record, Typical target score, Score pace, Scoring
concentration (top-5 / top-10). Existing "Roster size" card now shows `TypicalRosterSize`.

Tests: 6 engine tests (record/pace with the scouted faction on either side, per-war
concentration, concentration-is-not-an-all-time-aggregate, roster-size median, graceful
degradation) + 1 SQLite-backed `WarScoutServiceTests` proving the metrics survive the EF
round-trip.

### S05 — `w04-scouting-contract` verify script

GSD's `scripts/verify/w04-war-api-hub-board.sh` covers the board, not scouting. Hand-off
M2 calls for `w04-scouting-contract.sh`: backfill resumability, no war id stored twice,
faction profile renders from stored data with **no live Torn calls**, lump flag fires on
the DerDoruk fixture. Wire it into `build-and-test.sh`.

---

## M008 — Chain command  *(→ data/V2/handoff/06)*

Live chain tracking with milestone countdown and **crossing-hit reservation**, chain
watchers, and a filler-target policy. This is early on purpose: the chain multiplier
`max(1, 0.25·log₁₀(n) + 0.75)` runs to 2× and multiplies every hit's war score, the
maths is already in `ChainEngine` (confirmed against 54 records to 0.005), and it needs
no third-party data.

- **S01 — chain-endpoint lookup sweep (GATE).** Run the `chain` / `chainreport` /
  `chains` selections; record results in `data/V2/reference/data-layer.md`. Decides
  timer source before any UI is designed around a timer that may not exist.
- **S02 — `ChainTracker` in `Core/War`.** Pure. Given chain length, war-target
  availability, and the bonus table → multiplier, next milestone, hits remaining,
  reservation state, forfeited value. Tested against `ChainEngine.BonusTable` and the
  54-record multiplier fixture; the two must never disagree.
- **S03 — chain timer source.** Real endpoint if S01 found one; otherwise derived from
  the timestamp of the most recent hit, **labelled "inferred" on screen**.
- **S04 — `ChainAlert` on `WarHub`.** Fires on reservation-window entry and on the timer
  dropping below threshold.
- **S05 — chain panel on the Blazor board.** Length, multiplier, next milestone + hits
  remaining, value of the next milestone in points, value forfeited if the crossing
  lands outside ("landing 1000 outside costs 640"), timer/inferred-equivalent,
  war-targets-only banner, attackable war-target count.
- **S06 — chain watchers.** Planner-assigned, persisted per war (a war role, not a
  global role).
- **S07 — filler-target policy.** When war targets are exhausted: propose outside
  targets to sustain the chain, but filler must stop short of a crossing (chain at 997,
  no war target free → advise *wait or revive*, not *hit three randoms*). Show the
  `war = 1` vs `war = 2` score trade honestly.
- **S08 — `scripts/verify/w05-chain-contract.sh`.**

Out of scope: target *selection* among eligible targets — that is M010.

---

## M009 — Member linking and the key vault  *(→ data/V2/handoff/07)*

Identity, consent, secrets, and the tier-1 data they unlock (`/v2/user/bars`,
`/v2/user/cooldowns`, `/v2/user/attacksfull`). Has a **compliance gate before any code
ships**.

- **S01 — compliance gate.** `docs/TORN-API-TOS.md` in the repo **and live on the site**;
  active, timestamped, versioned member consent recorded **before the first key row is
  written**. `ConsentRecordEntity` (`PlayerId`, `DocumentVersion`, `AcceptedAtUtc`,
  `Purpose`) + migration. Storing a key while the published disclosure says keys are not
  stored is a breach — the exposure is the faction's.
- **S02 — `WarKeyVault` in `Core/War`.** Envelope encryption, master key from
  `WAR_KEY_MASTER`, per-key data keys, AES-256-GCM, reusing the wire-format conventions
  in `Encryption/KeyWrapping.cs` — **not** `Ecies`. Security tests first: decrypted only
  inside the call that uses it, never held in a field/static/closure; never logged,
  returned, or in an exception; revocation is immediate and **also deletes that member's
  identifiable readings**; a `code 2` key is marked invalid and not retried.
- **S03 — `StoredApiKeyEntity` + migration.**
- **S04 — linking endpoints and page.** Validate with `/v2/user/basic`; `profile.id`
  must match the account's claimed Torn identity. **Refuse a Full-access key** with an
  explanation — asking for less than offered is the whole point of the disclosure.
- **S05 — client methods.** `GetUserBarsAsync`, `GetUserCooldownsAsync`,
  `GetUserAttacksFullAsync`, with fixtures.
- **S06 — poller extension.** Per-linked-member polling inside the existing rate budget
  (M007 S02), priority below rosters and war state.
- **S07 — scoped bearer token.** Carries anonymous id + war role only, short-lived,
  refreshable, revocable independently of the Torn key. **Not a Torn API key** — never
  stored together, never named alike in code/logs/UI.
- **S08 — data tiers become real.** Linked / stale / inference-only badges wired to
  actual key state; coverage percentage on the board ("attack visibility: 12 of 71
  members (17%)"). An estimate and a fact must not look the same on screen.
- **S09 — `scripts/verify/w06-key-vault-contract.sh`**, including negative tests: key
  unreadable by any role incl. admin; revocation deletes readings (queried afterward);
  Full key refused; no key in any log at any level (greps captured output of a failing
  call).

---

## M010 — Targeting, λ*, and hit calling  *(→ data/V2/handoff/08)*

The assignment engine the project is named for. Fifth on purpose — it depends on
FFScouter and on the still-unverified fair-fight formula.

- **S01 — FFScouter client.** `/api/v1/get-stats`, 205 targets/batch, **20 req/min per
  IP**, 5-min server cache. Its own rate limiter keyed by service, not by API key.
  Refresh at the cache boundary, not per poll.
- **S02 — FF-formula validation (GATE).** Compare the formula against FFScouter's own
  fair-fight figure across a full roster. Disagreement halts the milestone. Outcome
  recorded in `data/V2/reference/scoring-formula.md` regardless.
- **S03 — `TargetScorer` in `Core/War`.** Pure. Implements `g_ij` and the rule directly:
  **assign the weakest enemy whose stats are ≥ 0.75× the attacker's; if none clears it,
  take the strongest available.** Carries provenance (exact vs estimated stats) through
  to output — a two-estimate assignment has a wider error bar than a known-attacker one,
  and the board must say so.
- **S04 — hospital-duration estimator.** Empirical distribution from accumulated
  `status.until` observations since M004. Until the sample is large enough, observed
  median with sample size shown. `h_j` is unmeasured — do not model damage.
- **S05 — `SlotScheduler`.** Board capacity `Σ_j t/h_j` vs our supply
  `Σ_i (energy_i + refills_i)/25`. When capacity binds, say "target-limited" on screen —
  the instinctive "everyone refill" response is then wrong.
- **S06 — λ* panel.** `λ* = (R_them·v_us²) / (R_us·v_them²)`, all four inputs already
  collected. Clamp it (volatile early when `v` is small-sample) and show the clamp.
  Planner overrides: manual λ, multiplier on λ*, per-target pin/ban — all visible with
  who set them and when.
- **S07 — `r̂_j` estimation with lump exclusion.** Exclude milestone lumps from the
  denial-rate estimate or a lump-catcher gets ranked top threat and sent the whole
  lockdown effort.
- **S08 — stealth attribution + post-war grader.** Invert the score formula on
  unattributed hits to recover attacker stats; intersect with availability, last-action
  cadence, stat band, energy budget. Name a suspect only when constraints isolate one;
  list candidates otherwise; always keep an unattributed residual so totals reconcile.
  Grade against `rankedwarreport` after every war (corpus exists from M006).
- **S09 — claims and hit calling over `WarHub`.** `ClaimTarget` / `ReleaseTarget`,
  advisory lock with TTL. Claiming is `Roles.User`; overriding another's claim is
  `war-planner`. A claim on a hospitalised enemy queues against `status.until`. Two
  members cannot hold a live claim on the same target.
- **S10 — endgame lockdown mode.** Entered with hysteresis when `t_them < t_us`. A mode
  switch (covering, not rate-maximising), not a parameter change.
- **S11 — `scripts/verify/w07-targeting-contract.sh`.**

---

## M011 — The userscript  *(→ data/V2/handoff/09)*

A thin Tampermonkey / TornPDA userscript overlaying the board's output onto Torn's own
pages. **Thin is the design** — all state, maths, and persistence stay server-side; two
implementations of the scoring formula will drift and one will be wrong during a war.

- **S01 — build scaffold.** `src/HappyGymStats.Userscript`, npm + esbuild, single
  `.user.js` with Tampermonkey header, version stamp. No CDN runtime dependency (TornPDA
  and strict CSP block it).
- **S02 — in-script settings panel.** TornPDA has no Tampermonkey menu API, so build a
  self-injected panel from the start. Token entry with a **Torn-key-confusion guard**:
  pasting a Torn API key into the token field gives a clear, specific error.
- **S03 — hub client.** Reconnect + full-state REST fallback on connect. Hub needs
  `.AllowCredentials()` with an explicit origin list including `https://www.torn.com` —
  SignalR will not negotiate against the existing wildcard `ReadApi` CORS policy.
- **S04 — injection layer.** `MutationObserver`, idempotent re-injection, stable
  selectors. Torn's front end is React 19; injected nodes are removed on re-render.
- **S05 — war-page roster overlay.** Per enemy row: assignment + by whom, FF estimate +
  provenance badge, hospital countdown + revivable flag, claim/release button, one-line
  assignment reason.
- **S06 — floating panels.** Chain (length, multiplier, hits to next milestone,
  war-targets-only banner) and own-state (energy, cooldowns if linked), holes count.
- **S07 — attack-result page.** Next suggested target so a member can chain without
  returning to the roster.
- **S08 — "Torn — War Roster Tools" v5.1.0 inventory and selective port.** Inventory
  every feature first; anything the server can compute moves server-side; anything that
  needed scraping or an undocumented endpoint is **dropped** and recorded as an open
  question in `data/V2/reference/data-layer.md`. Do not port as-is.
- **S09 — version check** against the server's minimum supported version on connect.

Acceptance: works in Tampermonkey **and** TornPDA; survives React re-renders; makes
**zero** requests to Torn beyond what the page itself does (verified on the network tab);
no token/key in the console at any level.

---

## M012 — Comms, timeline, and the strategy map  *(→ data/V2/handoff/10)*

The leadership layer. Built on the existing hub — no new transport, no third-party
service.

- **S01 — comms.** War-scoped channels + a planners-only channel, membership follows war
  roles. **Orders** are a distinct type from chat: author, target (member / group /
  faction), body, acknowledgement state, planner sees who has read. Retention: war + 30
  days, then delete (verified by a test). Do **not** try to replace Discord — build only
  the part it cannot do (tracked, acknowledged, target-bound orders).
- **S02 — mention push** to the userscript overlay.
- **S03 — timeline event assembly.** Pure, testable, from data already stored: score
  changes, chain milestones crossed, hospitalisations, visible attacks, orders
  issued/acknowledged, assignment changes. No new ingest.
- **S04 — timeline UI.** Scrubable, dual-faction score curves (Plotly, house style).
  Scrub to a moment → board shows state at that moment. This is the debrief tool as well
  as the live one.
- **S05 — location resolver.** Every member on both rosters → exactly one of: Torn,
  in-transit (origin, destination, aircraft class from `plane_image_type`), or a named
  destination. Complete partition from `status` — no guessing.
- **S06 — strategy map.** Inline SVG with a pan/zoom layer (not a mapping library —
  eleven fixed locations, no geography). Tokens per member, faction-coloured,
  state-badged. Abroad is a real battlefield boundary.
- **S07 — drawing layer.** Leader draws arrows/zones on an SVG overlay; each becomes an
  order attached to the enclosed members and pushes through comms as an acknowledged
  order. Persisted per war (survives reconnect). `war-leader` to draw, `war-planner` to
  view, `Roles.User` **denied** — the one view where restricted access is the point.
- **S08 — flight-time table.** Built empirically from observed `Traveling` → `Abroad`
  transitions in snapshot history. Not hard-coded from a wiki. ETA shown with
  confidence / sample size until the sample is large. FFScouter `player-flights`
  (premium, 100/min, single-target) is a cross-check for members already `Traveling` /
  `Abroad` only.
- **S09 — gear-tracker spike (GATE).** Written finding into
  `data/V2/reference/data-layer.md`: is enemy gear obtainable through the documented API
  at all? If only by scraping an attack-log page, it is **out of scope** and that is
  stated plainly — a feature that cannot be built within the constraints is a finding,
  not a failure.
- **S10 — `scripts/verify/w08-comms-map-contract.sh`.**

---

## M013 — The Investigator  *(→ data/V2/handoff/11)*

A backend service that proposes **strategies**, simulates each forward, and lets planners
compare them. The most speculative part of the project — do not start it until M008 and
M010 are shipped and used in a real war. A simulator calibrated against nothing is a
confident random-number generator.

- **S01 — backtest harness (build FIRST, alone).** Replay finished wars from the M006
  corpus: reconstruct starting state, run forward, compare predicted vs actual final
  scores and duration, produce a calibration curve. Zero permissions, offline,
  CI-runnable. See the parallel track below — this slice is unblocked today.
- **S02 — the gate.** Report whether the simulator beats a naive baseline
  ("current score rates extrapolate linearly"). **Stop here if not.** Baseline
  comparison is written into the harness so the gate is measured, not judged, and is
  published whether or not it is favourable.
- **S03 — simulator core.** Forward-simulate from a state, stepping with
  `Core/War/ScoreCalculator` — **the same class the live engine uses**, never a
  reimplementation. Acceptance requires there be no second implementation of the formula
  anywhere in the solution.
- **S04 — uncertainty models**, one per row, each with its own test: their activity
  pattern, our participation rate, FFScouter stat error, hospital duration, and the
  stealth-attribution **suspect distribution** (never collapsed to a point guess).
- **S05 — policy space + coarse search.** A strategy is a point in a small named space
  (λ multiplier, target rule, chain policy, energy policy, lockdown set size,
  participation push, discipline flags). Coarse grid + local refinement — search, do not
  enumerate.
- **S06 — outcome clustering + diversity selection.** Cluster candidates by outcome
  distribution; present only materially different ones. Three genuine options beat thirty
  near-duplicates.
- **S07 — assumption + tripwire extraction per strategy.** Lead with the load-bearing
  assumption and the condition that falsifies it, not a win probability.
- **S08 — planner UI.** Comparison view, selection, **live tripwire monitoring** once a
  strategy is chosen.
- **S09 — post-war counterfactual report.** What the unchosen strategies predicted, so
  the tool earns or loses trust on its record. Every proposal logged with its inputs.
- **S10 — `scripts/verify/w09-investigator-contract.sh`.**

Governance: the Investigator proposes, a planner decides, **nothing auto-executes**.

---

## Parallel track — backtest harness, pullable forward now

Hand-off M1 flags the backtest harness as "worth arguing for early, once the board
works": zero permissions, uses `rankedwarreport` over historical wars, fully independent
of every other milestone. The board (M004/M005) and the war corpus (M006) both exist, so
**M013 S01–S02 are unblocked today**. Running them now either kills M013 early (a real
finding, cheaply bought) or validates the concept before M008–M012 are built on the
assumption that it works. Recommend scheduling M013 S01–S02 alongside M007 rather than
holding them at position 7.

Everything else keeps the pack's ordering. Its two load-bearing sequencing arguments —
chain (M008) before targeting (M010), and the M013 gate — are backed by numbers, and
`data/V2/handoff/00-brief.md` says not to override them.

---

## Dependency summary

```
M007 (conformance sweep) ─┬─> M008 (chain) ──────────────┐
                          │                              ├─> M013 (investigator)
M013 S01–S02 (backtest) ──┘   M009 (linking) ─> M010 ────┤       ▲ gated on S02
   can start now              (key vault)     (targeting) │
                                                          │
                             M010 ─> M011 (userscript) ───┤
                             M008 + M011 ─> M012 (comms/map)
```

- M007 has no blockers; slices land as a branch stack (S01 → S02 → S03 → …) but do not
  depend on each other's behaviour.
- M008 needs M007 S02 (`TornRateLimiter`) and its own S01 lookup-sweep gate.
- M009 needs the compliance gate (S01) before any other slice.
- M010 needs M009 (partly — tier-1 exact stats) and FFScouter; gated on S02.
- M011 needs M009 tokens and M010 output.
- M012 needs M004 (board) and M011 (overlay).
- M013 needs the M006 corpus (have it) and M010's engine; gated on S02.
