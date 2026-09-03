# War-command milestones (M007+)

Derived from the hand-off pack in `workspace/V2/`. This is a fresh milestone breakdown for
the work that remains after the live war board (hand-off M1) and scouting (hand-off M2)
were built as GSD milestones M004–M006.

## Ground rules for reading this

- **Numbering.** GSD owns a live milestone registry using `M001`–`M006`. This document
  continues that sequence from `M007` so nothing collides. Each entry cross-references
  the hand-off document it is based on: `→ workspace/V2/handoff/NN`.
- **Authoritative source.** `workspace/V2/` is the current pack. The older copy at
  `workspace/handoff/` (no `V2`) is a stale subset — only `00-brief.md` differs, and it is
  missing hand-off docs 05–11 entirely. Cite `workspace/V2/...` paths only.
- **This plan does not touch GSD.** `STATE.md` / `ROADMAP.md` under
  `workspace/archive/GSD/` are the external tool's state and are edited there, not here.
  `STATE.md` is currently stale (still shows M006/S02 executing though PR #30 merged) —
  left as-is deliberately.
- **Two standing non-goals, every milestone below:**
  1. **No game actions, ever.** No code path in M007–M013 may issue a state-changing
     request to Torn — no auto-attack, refill, travel, or scripted click. Links are
     plain anchors a human clicks. (`workspace/V2/handoff/00-brief.md`, Hard constraints.)
  2. **The `Ecies` scheme must not be reused for the war key vault.** It encrypts to a
     client-held public key so the server *cannot* decrypt — useless for a key the
     server must use unattended. M009 uses envelope encryption keyed off
     `WAR_KEY_MASTER`. (`workspace/V2/handoff/07-milestone-4-member-linking.md`.)

## Gates — stop-and-report points, not ordinary tasks

| Gate | Milestone | Rule |
|---|---|---|
| Chain-endpoint lookup sweep | M008 | Run `chain` / `chainreport` / `chains` selections and record what they return **before** designing any timer UI. Decides whether the lapse timer is real or inferred. |
| FF-formula validation | M010 | Compare `FF = min(3, 1 + (8/3)·def/att)` against FFScouter's own fair-fight figure across a full roster. If they disagree, the 0.75× targeting rule is wrong — **M010 halts** until understood. Record the outcome in `workspace/V2/reference/scoring-formula.md` either way. |
| Gear-tracker spike | M012 | First task is a written finding, not a build. Determine whether enemy gear is obtainable through the documented API at all. A documented dead end is an acceptable, complete outcome. |
| Backtest vs naive baseline | M013 | The harness must retrodict finished wars better than "extrapolate current score rates linearly". If it does not, **stop** and publish that. The comparison is published whether or not it is favourable. |

---

## M007 — Conformance sweep: close M1/M2 acceptance gaps

**Why it exists.** M006 was marked COMPLETE, but a check against hand-off M1 and M2
acceptance criteria (`workspace/V2/handoff/04`, `workspace/V2/handoff/05`) found stated
deliverables that were not built or were built differently. This milestone closes them
before new features stack on top. Slices are independent; size is set by the findings
below. Two findings were re-scoped once checked against the tree: S01 (lump detection
existed as a dampener, not real detection) and S03 (`OpenTarget` holes already existed
but were mis-gated) — see each slice.

**Branch layout.** The slices landed as a stack of branches and are now **merged to
`main`** (2026-09-03); the branches are gone. Per-slice notes below still say "branch
`feat/m007-…`" — read those as "in `main`, delivered by that slice".

### S01 — Ranked-war lump detection rework  *(→ workspace/V2/handoff/05)*  — DONE (branch `feat/m007-s01-lump-detection`)

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
  right answer (`workspace/V2/reference/data-layer.md`, correction section).

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
  not detected. Both choices are `const`s in `OpponentProfileEngine`, retunable. A real
  flag-rate measurement against a full roster is **deferred** (no local data) — see S05.

### S02 — `TornRateLimiter`  *(→ workspace/V2/handoff/04, task 3; workspace/V2/handoff/03 "Rate limiting")*  — DONE (branch `feat/m007-s02-rate-limiter`, stacked on S01)

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

### S03 — Open-slot holes  *(→ workspace/V2/handoff/04, "Definition of a hole")*  — DONE (branch `feat/m007-s03-open-slot-holes`, stacked on S02)

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

### S04 — Faction-level scout profile  *(→ workspace/V2/handoff/05, "Faction-level profile")*  — DONE (branch `feat/m007-s04-faction-scout-profile`, stacked on S03)

`FactionScoutProfile` gained the faction summary the hand-off names, all computed in
`OpponentProfileEngine.BuildFactionMetrics` from stored history + report rows:
- **`WinRate`** + `WarsWithKnownOutcome` — wars won ÷ wars with a recorded
  `WinnerFactionId`.
- **`TypicalTargetScore`** — the scouted faction's *own* median final score = what an
  opponent must outscore to beat them (`workspace/V2/reference/data-layer.md`, "Against a 7300
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

### S05 — scouting-contract verify script  *(→ workspace/V2/handoff/05)*  — DONE (branch `feat/m007-s05-scouting-verify`, stacked on S04)

`scripts/verify/w05-scouting-contract.sh` (the hand-off's `w04-` name collides with GSD's
existing `w04-war-api-hub-board.sh`, so it takes the next w-number; **chain's verifier
shifts to `w06`**). Follows the w01–w04 pattern: required files present → hand-off
acceptance criteria pinned to named tests (no war id stored twice, backfill resumable +
inert-while-disabled, ingest idempotent, DerDoruk / war-48377 lump fixture, both lump
tolerance edges, profile built only from captured history) → source-only boundary check
that the scouting read path (`OpponentProfileEngine`, Core `WarScoutService`,
`WarScoutController`, `WarScout.razor`) references no `TornApiClient` / `HttpClient` /
`api.torn.com` / centrifugo → targeted test run (59 tests). Wired into
`scripts/verify/build-and-test.sh`.

**Deferred (needs data, not code):** the real-roster flag-rate check for the lump
tolerance. No backfilled war history exists locally; the tolerance is fixture-validated
only. The script carries a `KNOWN GAP` note with the check to run once a populated DB
exists (expect a low single-digit % of `(member, war)` rows flagged).

---

## M008 — Chain command  *(→ workspace/V2/handoff/06)*

Live chain tracking with milestone countdown and **crossing-hit reservation**, chain
watchers, and a filler-target policy. This is early on purpose: the chain multiplier
`max(1, 0.25·log₁₀(n) + 0.75)` runs to 2× and multiplies every hit's war score, the
maths is already in `ChainEngine` (confirmed against 54 records to 0.005), and it needs
no third-party data.

- **S01 — chain-endpoint lookup sweep (GATE).** — **BLOCKED ON USER.** Needs a live
  Limited key + network to `api.torn.com`, which this environment doesn't have. Run the
  `chain`, `chainreport`, `chains` selections against `/v2/faction/lookup`, paste what
  each returns, and it gets recorded in `workspace/V2/reference/data-layer.md`. Until then S03
  (timer source) cannot start; S02/S04–S08 do not depend on it.
- **S02 — `ChainTracker` in `Core/War`.** — DONE (branch `feat/m008-s02-chain-tracker`,
  off the M007 stack). Pure `static ChainTracker.Evaluate(chainLength,
  attackableWarTargetCount, reservationWindowHits = 5)` → `ChainTrackerState`:
  `CurrentMultiplier` (= `max(1, ChainEngine.DefaultA·log10(n) + DefaultB)`, referencing
  the engine constants so the two can't drift — asserted against
  `SigmaMult(n) − SigmaMult(n−1)` across a length sweep), `NextMilestone` (smallest
  `ChainEngine.Milestones` entry strictly above `n` — agrees with `CumBonus` treating
  "at a milestone" as already banked), `HitsToNextMilestone`, `NextMilestoneBonus` /
  `ForfeitedValueIfCrossedOutside` (from `ChainEngine.MilestoneBonuses`),
  `IsInReservationWindow` (`hits ≤ window`, inclusive — 995 in, 994 out), and a
  `ChainBoardMode` eligibility verdict: `OutsideTargetsAllowed` / `WarTargetsOnly` (in
  window + war targets) / `HoldForWarTarget` (in window, none — wait/revive, forfeit
  named in `Reason`) / `SustainWithFiller` (out of window, none). No timer — that is S03.
  16 tests / 32 cases. **`w06` verifier will be S08.** No I/O, so no gate dependency.
- **S03 — chain timer source.** — DONE (branch `feat/m008-s03-chain-command`, stacked on
  S02). S01's live endpoint is still blocked, so this is the **inferred** fallback:
  `Core/War/ChainLapseInference.Infer(perFactionChainSeries, now)` scans the *full*
  (un-bounded, not the 8-sample score-rate window) `WarScoreSampleEntity` history for
  the last sample where that faction's chain rose, and reports
  `SecondsSinceLastIncrease` / `SecondsUntilLapse` with a `SampleSpacingSeconds` error
  bar (poll interval defaults to 30 s). **Honest-output rule:** `Confidence.None` /
  `SecondsUntilLapse = null` in two cases — (a) the chain never rises in the held history
  (last hit older than the data), and (b) the chain rose then dropped/reset and has not
  climbed since (the last increase belonged to a chain that has since lapsed — otherwise
  the countdown would walk down and raise a false "about to lapse" alert for a dead
  chain). Never a confident full timer. The board renders "~mm:ss ago (±Ns)", not a
  ticking clock. Chain command is derived for **our faction only** (the poller stamps
  each sample with its own faction id) — the enemy card shows no orders and no alert.
  - **4th unverified assumption.** `workspace/V2/handoff/00-brief.md`'s ledger names three
    unknowns (FF formula, hospital duration, energy model). `ChainLapseInference`
    `.TornChainLapseTimeoutSeconds = 300` adds a fourth, *not* pre-blessed by the brief.
    Named const + comment pointing at the ledger + a loud test
    (`ChainLapseInference_timeout_constant_is_challengeable`). When S01's sweep lands,
    replace the inference with the real `timeout` field and delete the const.
  - `ChainEngine.BonusTable`'s `Timer` column ("25 minutes" at 250, …) is the *milestone*
    time allowance (cumulative limit to *reach* the next milestone), **not** the
    gap-between-hits lapse timeout — the two are not conflated anywhere; the comment on
    the const spells out the difference.
- **S04 — chain alert level.** — DONE (same branch). `ChainAlertLevel`
  (`None` < `ReservationWindow` < `TimerRunningLow`) computed by
  `ChainTracker.AlertLevel(state, timer)` — timer-about-to-lapse
  (≤ `AlertTimerLowSeconds = 90`, and only for `Confidence.Inferred`) outranks the
  window. It rides in the **broadcast war state** on `WarDerivedFactionState.ChainAlert`,
  not a distinct hub event: a per-watcher `ChainAlert` push (handoff task 4) needs
  `WarHub` per-war groups + per-user targeting, which M1 task 10 listed but the board
  shipped without. Deferred with S06.
- **S05 — chain panel on the Blazor board.** — DONE (same branch). Per-faction card on
  `War.razor`: multiplier, next milestone + hits + bonus, "Landing chain N outside costs
  X points", inferred last-hit / to-lapse line with the ± spacing, mode chip
  (`Outside targets locked` / `Wait or revive — do not filler` / `Filler OK — scores
  ~half`), and an alert banner (error on `TimerRunningLow`, warning on
  `ReservationWindow`). Flows Core → `WarChainCommandDto` (flattened, API + Blazor
  mirror) → `WarDtoMapper.ToChainCommandDto`.
- **S06 — chain watchers.** — **DEFERRED** (not built). A planner-assigned, per-war
  watcher role only does something once alerts can be pushed to *specific* users;
  `WarHub` has no per-war groups and no per-user targeting (M1 task 10), so a
  `ChainWatcherEntity` + migration now would persist assignments nothing can deliver to.
  Blocked on the same hub-groups work as S04's distinct event. Revisit when the hub
  grows targeted delivery (likely alongside M010 hit-calling).
- **S07 — filler-target policy.** — DONE **as board advice** (same branch); the
  target-*proposal* half is **M010** (handoff 06 "Out of scope: target selection").
  Delivered: `ChainTracker.ChainBoardMode` already encodes the eligibility verdict, and
  the S05 panel surfaces the honest trade — chain in the window with no attackable war
  target → `HoldForWarTarget`, advice *"Wait or revive — filler would carry the chain
  across and forfeit N points"*, never "hit three randoms"; out of the window with no
  war target → `SustainWithFiller`, "scores roughly half (`war = 1`, not `2`)". Pinned
  by `At_995_with_no_war_target_the_advice_is_wait_not_filler_and_names_the_cost` and
  `Derive_chain_command_holds_for_a_war_target_when_none_is_attackable_in_the_window`.
- **S08 — `scripts/verify/w06-chain-contract.sh`.** — DONE (same branch). Pins 8 named
  acceptance tests (multiplier-vs-`ChainEngine`, reservation window, honest inferred
  timer, challengeable timeout const, alert priority, both S07 hold-advice tests, the
  wiring test) + 4 `War.razor` board literals + a Core/public-war boundary guardrail on
  `ChainTracker.cs` / `ChainLapseInference.cs` (no `TornApiClient` / `HttpClient` /
  `api.torn.com` / transport). Wired into `scripts/verify/build-and-test.sh`.

**M008 status:** S02–S05, S07 (board half), S08 are **merged to `main`** (2026-09-03).
The `feat/m007-*` / `feat/m008-*` branches this document used to point at have been
deleted; read the code on `main`. S01 stays BLOCKED ON USER (live Limited key +
`api.torn.com`); S06 DEFERRED (hub groups).

The old "278/281, 3 pre-existing SQLite / pending-migration failures" note is stale: the
suite is green on `main` (342 on this branch, which adds M009 S02's tests).

The three Postgres integration tests had never executed in their life — running them
turned up three defects in the harness itself, fixed in PR #42 (merged 2026-09-03). They
now run for real against a container, so a green suite finally says something about the
Npgsql path. They still skip silently where no container runtime is available; check for
`Skipped: 0` before treating that tier as evidence.

Out of scope: target *selection* among eligible targets — that is M010.

---

## M009 — Member linking and the key vault  *(→ workspace/V2/handoff/07)*

Identity, consent, secrets, and the tier-1 data they unlock (`/v2/user/bars`,
`/v2/user/cooldowns`, `/v2/user/attacksfull`). Has a **compliance gate before any code
ships**.

- **S01 — compliance gate.** `docs/torn-api/terms-of-service.md` in the repo **and live on the site**;
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
- **S09 — `scripts/verify/w07-key-vault-contract.sh`**, including negative tests: key
  unreadable by any role incl. admin; revocation deletes readings (queried afterward);
  Full key refused; no key in any log at any level (greps captured output of a failing
  call).

---

**M009 status (2026-09-03):** S02 built on `feat/m009-s02-war-key-vault` — `WarKeyVault`
in `Core/War`, 17 tests, `scripts/verify/w07-key-vault-contract.sh` wired into
`build-and-test.sh`. Envelope encryption, AES-256-GCM, master key from `WAR_KEY_MASTER`,
`[1 version][12 nonce][ct][16 tag]` framing. It does **not** copy `KeyWrapping`'s header:
that frame leads with iterations + salt because its key comes from a password via PBKDF2,
and `WAR_KEY_MASTER` is already key material, so those two fields would describe nothing.
`PlayerId` + `Purpose` are bound as AES-GCM associated data, so a blob moved between rows
fails authentication instead of decrypting into the wrong member. The only way to a
plaintext key is the `UseKey` callback — there is no method that returns one.

**S01 is the blocker, and it is worse than the plan assumed.** Handoff 07 says the
disclosure "has been rewritten … to state plainly that war keys are stored encrypted". It
had not been. `docs/torn-api/terms-of-service.md` still said *"API key is not stored and
not shared"* and named Full Access as the level requested. Storing a key against that
published text is precisely the breach handoff 07 warns about, and the exposure is the
faction's. A 2.0.0 draft covering the three key usages is now in the repo, marked
NOT YET PUBLISHED. **It needs the operator to review and publish it, and members to
actively accept it, before S03/S04 may write a single key row.**

Remaining slices unbuilt: S01 consent record, S03 entity + migration, S04 linking
endpoints (also needs `/v2/user/basic`), S05–S08. `w07` prints these as explicitly
unverified rather than passing over them.

---

## M010 — Targeting, λ*, and hit calling  *(→ workspace/V2/handoff/08)*

The assignment engine the project is named for. Fifth on purpose — it depends on
FFScouter and on the still-unverified fair-fight formula.

- **S01 — FFScouter client.** `/api/v1/get-stats`, 205 targets/batch, **20 req/min per
  IP**, 5-min server cache. Its own rate limiter keyed by service, not by API key.
  Refresh at the cache boundary, not per poll.
- **S02 — FF-formula validation (GATE).** Compare the formula against FFScouter's own
  fair-fight figure across a full roster. Disagreement halts the milestone. Outcome
  recorded in `workspace/V2/reference/scoring-formula.md` regardless.
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
- **S11 — `scripts/verify/w08-targeting-contract.sh`.**

---

## M011 — The userscript  *(→ workspace/V2/handoff/09)*

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
  question in `workspace/V2/reference/data-layer.md`. Do not port as-is.
- **S09 — version check** against the server's minimum supported version on connect.

Acceptance: works in Tampermonkey **and** TornPDA; survives React re-renders; makes
**zero** requests to Torn beyond what the page itself does (verified on the network tab);
no token/key in the console at any level.

---

## M012 — Comms, timeline, and the strategy map  *(→ workspace/V2/handoff/10)*

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
  `workspace/V2/reference/data-layer.md`: is enemy gear obtainable through the documented API
  at all? If only by scraping an attack-log page, it is **out of scope** and that is
  stated plainly — a feature that cannot be built within the constraints is a finding,
  not a failure.
- **S10 — `scripts/verify/w09-comms-map-contract.sh`.**

---

## M013 — The Investigator  *(→ workspace/V2/handoff/11)*

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
- **S10 — `scripts/verify/w10-investigator-contract.sh`.**

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
`workspace/V2/handoff/00-brief.md` says not to override them.

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
