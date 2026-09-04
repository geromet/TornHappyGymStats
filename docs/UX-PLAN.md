# UX plan — U001 onwards

The feature milestones (`docs/MILESTONES.md`, M007–M013) say what the site
should *do*. Nothing says what it should be like to use. The board grew panel by
panel and no one has looked at it whole. This is that plan.

## Numbering

`U001`–`U00n`, so it cannot collide with the `M0nn` feature milestones. Slices
are independent unless stated. Each names its acceptance in terms a person can
check on a screen, not a test name — where a test *can* pin it, the slice says so.

## The standing rule

**An estimate and a fact must never look the same.**

This is already written down for one case: hand-off 07 requires it of the
data-tier badges, and M008 S03 made the chain timer say "inferred". But it was
applied panel by panel, so most of the board still renders extrapolations in the
same typeface as measurements. That is the subject of U001, and it is first
because it changes what people *believe*, not how the site looks.

## Looking at it

`bash scripts/screenshot-board.sh` (menu: **Look at it → Screenshot the war
board**) boots the app locally with development auth and the seeded war, shoots
phone / tablet / desktop, and stops both hosts. Output lands in
`workspace/tmp/screenshots/` and is gitignored — regenerate, never commit.

Playwright lives in `.venv/` with its own Chromium under `~/.cache/ms-playwright`.
No sudo, and no browser anyone uses personally is involved.

This exists because U001 shipped a caption reading "Last hit (inferred) inferred"
and an operator diagnostic inside an error banner — both invisible in the source,
both obvious in the first rendered frame. **A UX slice is not done until someone
has looked at it**, and that someone should not have to be the operator.

---

## U001 — The honest-signal pass  *(DONE 2026-09-04)*

**The problem, concretely.** `War.razor` renders all of these side by side, in
the same style, with nothing distinguishing them.

The useful question is not how many arithmetic steps produced a number. It is
**would this number change if you polled again right now with nothing else
having happened?**

| Kind | Test | On the board |
|---|---|---|
| **Measured** | Poll again, same answer. | Score, chain length, coverage ratio, an exact chain deadline |
| **Projected** | Depends on a *window*. The same war state gives a different number after a lull. | Score rate, ETA to win, attacks to finish |
| **Inferred** | From a proxy. More polling will not fix it; a linked key or better data might. | Hole severity, the inferred chain timer, opponent profiles |

Three kinds, not four. "Unavailable" is orthogonal — it is the absence of a
figure, not a kind of figure, and it already has its own handling via
`IsAvailable`.

Note what this reclassifies: coverage ratio is `openTargets / availableMembers`,
arithmetic over two measured values, and it is **Measured** — polling again gives
the same answer. Score rate is arithmetic over two measured samples and is
**Projected**, because the window is part of the answer. An earlier draft of this
plan had those in different categories on the basis of how derived they looked,
which is exactly the confusion the three-way test removes.

Today, three Projected figures and two Inferred ones render as plain numbers. On
a war night "ETA 00:12:00" is read as a fact and planned against.

**What this slice does.**

1. One shared vocabulary, defined once and used everywhere: **measured**,
   **projected**, **inferred**. No panel invents its own word.
2. One reusable component that renders a figure with its provenance, so the
   marker cannot be forgotten by whoever writes the next panel.
3. Apply it to every figure in the table above.
4. Keep the *reason* reachable where one exists — in a tooltip, not the primary
   line. The DTOs carry `Diagnostic` strings, but those were written as operator
   diagnostics (`"Chain deadline reported by Torn (2026-09-03 21:16:18Z)"`) and
   are not war-night copy. Curated per-kind wording goes on screen; the raw
   diagnostic stays available underneath.

**Acceptance.**
- No figure on the war board that is an estimate renders identically to a measurement.
- The chain timer's existing exact/inferred distinction survives unchanged — it is
  the model, not an exception.
- A verifier pins the **component**, asserting every figure-rendering site in
  `War.razor` goes through it. Pinning a list of forbidden words instead would
  fire on the inferred timer's correct, already-shipped `~mm:ss ago (±30s)`
  format — a verifier that makes you weaken the thing it protects.

**Scope: `War.razor` only.** Opponent profiles live in `WarScout.razor`, a
separate 240-line page with its own formatting helpers; applying the vocabulary
there doubles the slice, so it is **U001b** and follows immediately. The shared
component is built here and reused there.

**Explicitly not in this slice.** Colour, spacing, typography. This is about
which of three categories a number is in.

**Delivered.** `Components/Shared/FigureKind.cs` holds the vocabulary and the
rule that measured figures carry *no* marker — marking everything is the same as
marking nothing, and the default a reader assumes is "this is a fact".
`Components/Shared/Figure.razor` renders label, value, optional note and the
marker, with the raw `Diagnostic` reachable in the tooltip and never on the
primary line. Every figure on `War.razor` routes through it; hole severity is
marked at panel level because every row there is inferred the same way and
per-row markers would be noise.

`scripts/verify/u001-honest-signal.sh` pins it, wired into `build-and-test.sh`
and the operator console. It pins the component, the three-word vocabulary, the
measured-figures-carry-no-marker rule, and that markers do not rely on colour
alone. Prose bindings are exempt **by name** rather than by loosening the
pattern, so a new binding has to be classified deliberately.

**Verified by running it**, not by reading the source. The app was started
locally (API + Blazor, `HAPPYGYMSTATS_DEV_AUTH=1`, the `DevelopmentWarSeed` data)
and the rendered DOM was read back. Two defects showed up that source review had
missed:

- the chain-timer caption rendered **"Last hit (inferred) inferred"** — the label
  and the new marker each said it;
- the "chain about to lapse" alert printed the raw operator diagnostic
  (*"Last qualifying hit ~280s ago (inferred from score polls, ±570s)"*) on the
  loudest surface on the board, which is exactly what U001 said belonged in a
  tooltip.

Both fixed. Density on a faction card is three marked figures out of eleven,
which reads as emphasis rather than noise.

**Still open:** colour, contrast and spacing. Reading the DOM shows structure and
copy; it does not show what the marker looks like next to a number. That needs
eyes on a screen, and is U006's business.

**How to look at it yourself:**

```bash
ASPNETCORE_ENVIRONMENT=Development HAPPYGYMSTATS_DEV_AUTH=1 \
  ASPNETCORE_URLS=http://localhost:5047 dotnet run --project src/HappyGymStats.Api --no-launch-profile &
ASPNETCORE_ENVIRONMENT=Development HAPPYGYMSTATS_DEV_AUTH=1 ApiBaseUrl=http://localhost:5047 \
  ASPNETCORE_URLS=http://localhost:5137 dotnet run --project src/HappyGymStats.Blazor/HappyGymStats.Blazor --no-launch-profile &
# then open http://localhost:5137/war
```

Both hosts need `HAPPYGYMSTATS_DEV_AUTH=1`: with it set only on the frontend, the
board renders *"War board unavailable. Authentication is required"*, because the
dev-header principal has no access token to forward to the API.

---

## U001b — The same pass over `WarScout`

Opponent profiles are the most heavily inferred figures in the product —
lump-adjusted medians over past wars, with a detection tolerance that is itself a
tuned constant. They currently render as plain numbers. Uses the component from
U001; no new vocabulary.

---

## U002 — Empty, first-run and error states

**The problem.** `Faction.razor` is 33 lines and still a placeholder. Several
panels render blank rather than saying why they are blank, and a failed API call
surfaces as the framework's error page with a request ID and nothing else — which
happened during this session's own sign-in debugging.

**What it does.**
- Every panel that can be empty says which of these it is: not yet loaded, nothing
  to show, needs sign-in, or failed — and what the reader can do.
- `Error.razor` gets a human sentence and a route back, keeping the request ID for
  correlation but not leading with it.
- The `Faction` placeholder either says plainly that the feature is not built, with
  what it will show, or leaves the nav.

**Acceptance.** No blank panel with no explanation; no raw framework error page on
a route a signed-in user can reach.

---

## U003 — Mobile and the war-night layout

**The problem, now measured rather than assumed.** At 390px the board is
**8322px tall** — roughly twenty screens. Nothing overflows horizontally (the
MudTable breakpoint does stack), so this is not a broken layout; it is a
priority problem. The chain command panel, the one thing that is time-critical,
sits about 40% of the way down, below four summary cards, the faction header and
six figures. On a war night the reader scrolls past everything that can wait to
reach the thing that cannot.

**What it does.** Establishes an order for narrow screens: alert banner and chain
command first, then holes, then score and roster. Summary cards collapse into a
single row of numbers. The member table becomes a list rather than a stacked
label/value pair per cell — currently each member costs six rows.

**What it does.** Establishes what the board must do at 390px: which panels come
first, what collapses, what is dropped. The chain command and the alert banner are
the two things that must survive at any width — they are the time-critical ones.

**Acceptance.** Screenshots at 390px and 1440px, from the operator. No horizontal
scrolling on the board at 390px.

**Depends on U001** — deciding what to drop is easier once every figure states its
own weight.

---

## U004 — Sign-in and the admin surface

**The problem.** `Login.razor` is the path everyone takes and it has had no
attention. Tonight's Keycloak work produced several distinct failures that all
looked identical to a user: an opaque error page.

**What it does.** Distinguishes "not signed in", "signed in but not an
administrator" (the gate returns a flat 403 with a text body today), and "sign-in
itself failed" — the last being the only one that is the site's fault.

---

## U005 — Contrast, focus and keyboard paths

Cheap now, expensive later. Contrast on the alert colours, visible focus rings,
labels on icon-only controls, and a keyboard path to the admin toggle and refresh.

**Acceptance.** Keyboard-only pass through the war board reaches every control.

---

## U006 — Visual coherence

Last on purpose. Typography scale, spacing rhythm, and the alert/severity palette
applied consistently. Doing this before U001–U005 would be decorating a house
whose rooms are still being moved.

**Two concrete defects found by screenshotting, waiting here:**

1. **Every muted caption renders bright pink.** `MainLayout.razor` defines
   `TextSecondary = "#b8e1ff"` (pale blue), but 48 call sites use
   `Color="Color.Secondary"`, which selects the *Secondary palette colour* —
   MudBlazor's default `#FF4081` — not the secondary *text* colour. So "Faction
   ID 111", "Available members who are swinging", "50.0% of available attackers"
   and the footer are all pink. It reads as alarm everywhere, which directly
   undercuts U001: if everything shouts, a marker that shouts says nothing.
   The correct fix is at the call sites (muted text is not the secondary palette),
   not defining `Secondary` to match, because a few of those 48 use it as a
   deliberate accent.

2. **There is no light theme.** `MudThemeProvider IsDarkMode="true"` is
   hardcoded, so `prefers-color-scheme` is ignored and the `--theme light`
   screenshots are identical to the dark ones. Either honour the OS preference
   or drop the pretence and document that the site is dark-only.

---

## Order and why

U001 first: it changes what people believe about the numbers, and it is the one
with a correctness argument rather than a taste argument. U002 next because empty
states are where new users land. U003 because war nights are the actual use. Then
U004, U005, U006.

U001 and U002 need no screenshots to start — they are content and logic. U003
onward need the operator's eyes.
