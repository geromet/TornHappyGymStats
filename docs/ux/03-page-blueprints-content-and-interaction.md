## Page-by-page redesign

### Home / Overview

#### Signed out
The signed-out screen should feel like a product, not a setup form.

Suggested structure:
1. Strong proposition: “War intelligence and training insight you can trust.”
2. Short supporting line: live war operations, evidence-first scouting, planning, personal training.
3. Primary CTA: **Sign in**.
4. Secondary link: **How your data is handled**.
5. A compact product preview (not fake live data) showing the kinds of decisions HGS supports.
6. Small proof/trust section: measured/projected/inferred, privacy, no Torn automation.

Do not ask for a Torn API key before explaining value.

#### Signed in
Overview becomes a personalized launchpad:
- Current/next war status + Live War CTA.
- Critical chain/readiness alert if relevant.
- Scout next opponent if known.
- Training snapshot (recent sample count/trend, no unsupported optimization claim).
- Connection/data-health compact status.
- Recent activity only if it helps continue work.

If no active war exists, do not fill the page with zero cards; shift visual priority to planning/training.

### Live War
This should be the signature experience.

First viewport desktop:
- Operational alert rail at top: chain danger / stale data / critical hole.
- Compact score strip: both faction scores, delta/progress, chain, last updated/freshness.
- Main board: faction/roster state with enough columns to act, not everything the DTO contains.
- Right rail or drawer: holes/coming-out alerts.

Secondary:
- projections/attacks-to-finish with provenance;
- deeper member metrics;
- Data Status (hub connection, heartbeat, source, diagnostics).

At 390px:
- chain/action card first;
- score/freshness single compact row;
- next actionable members/targets;
- alert list;
- detail accordions.

The page should never make “Hub connection” visually equal to “chain about to lapse.”

### Opponent Scout
Turn it into a pre-war briefing:
- faction identity + evidence window/sample sufficiency;
- 3–5 plain-language conclusions supported by current aggregates;
- provenance/freshness next to each conclusion;
- search/filter controls;
- compact threat roster;
- expandable row detail for raw-vs-adjusted/reconciliation information.

Avoid unsupported “how to fight them” recommendations until strategy validation exists.

### Chain Planner
Layout:
1. Intent row: hit budget, base respect, goal/preset.
2. Primary result hero: best practical plan, expected respect, hits used, milestone summary.
3. 3–5 alternatives with clear tradeoffs (“+10 hits, +0.8% respect”, etc.).
4. Compare flow using explicit named selections, not unexplained A/B buttons.
5. “Show all combinations” for exhaustive table.
6. “Advanced model settings” collapsed with reset to verified defaults.
7. Curve/milestones as explanation, not the main answer.

This implements progressive disclosure directly.

### My Training
Replace “claim-bound stats” and 3D-first layout with:
- sample/date/freshness context;
- recent training summary;
- trend cards only when meaningful;
- 2D plots with understandable axes;
- filters for stat/gym/time/happiness if supported;
- a clear “Open Gym Explorer” action.

Do not say “optimal” or “best” unless the model genuinely validates that claim.

### Gym Explorer
This is where the 3D cloud belongs. Make it excellent:
- lazy-load Plotly;
- public/personal mode if policy allows;
- filter panel;
- sample count + data window;
- reset camera;
- fullscreen;
- clear legend/axis labels;
- optional saved view later only if there is real need.

### Account & Connections
Make one authoritative place for identity, Torn connection, consent/privacy, and sign-out.

The UI should answer:
- Am I signed in?
- Is Torn connected?
- Which Torn identity is connected?
- What does HGS need the key for?
- What permissions are required?
- Is it stored, and how?
- When was it last verified?
- How do I replace/revoke it?

Do not redistribute credential fields across Home and Training once this exists.

### Settings / Preferences
Only show choices that truly persist and change user experience. Remove developer configuration and disabled decorative controls. Prefer no Settings page over a fake one.

### Privacy / Terms / Security
Move these out of primary navigation into the account/help/footer hierarchy. Use readable line length (~65–80 characters), clear headings, and concise summaries at the top.

## Content design and language

Replace internal language with player language:
- “repository-backed public war state” -> “Live war status”
- “authenticated claim-bound stats” -> “Your training history”
- “Import + Refresh” -> contextual “Refresh data” / “Connect Torn”
- “API base URL” -> remove from member UI
- raw provider/service names -> Data Status/Admin only

Use status copy that answers what happened and what to do next. Error messages should be plain-language and actionable; empty is not error; stale is not current.

Buttons should describe outcomes, not technical operations.

## Interaction design rules

1. One primary action per visual section.
2. Critical actions use explicit labels; icon-only is for universally understood utility.
3. Destructive actions require a shared confirmation pattern and explain consequences.
4. Keep user context after errors; do not reset form state unnecessarily.
5. Use skeletons/placeholders only when they preserve layout and the content shape is predictable.
6. Loading feedback should be local to the operation when possible.
7. Never use disabled controls as a roadmap.
8. Do not surface feature flags or implementation status to ordinary users.
9. Filters should update quickly and preserve current selection/navigation context.
10. Keyboard order follows visual order.
