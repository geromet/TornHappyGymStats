## North-star visual direction: Tactical Calm

### Brand character
**Tactical, not militaristic. Scientific, not sterile. Dense, not cluttered. Dark, not gloomy. Fast, not frantic.**

The visual metaphor is a professional operations instrument, not a “gaming dashboard” full of neon gradients, glass panels, and glowing borders.

### Canvas and surfaces
Keep deep navy as the signature. Build clear depth with 3–4 luminance levels and mostly 1px borders. Use shadows rarely.

Proposed starting tokens (to be contrast-tested, not copied blindly):
- App canvas: `#080D18`
- Navigation: `#0B1220`
- Surface 1: `#101A2D`
- Surface 2 / raised: `#15223A`
- Surface 3 / selected: `#1B2A46`
- Border subtle: `#24324B`
- Text primary: `#F2F7FF`
- Text secondary: `#A9B8CE`
- Accent: keep the existing `#58A6FF`
- Success: ~`#69C77B`
- Warning: ~`#F4B740`
- Danger: ~`#F06464`
- Info/provenance: ~`#48BFE3`

Do not use accent blue for ordinary muted prose. Semantic color should communicate interaction or state.

### Typography
Use one modern UI sans family and a disciplined type scale. The family matters less than the metrics and hierarchy; keeping Roboto is acceptable if weights/line-height are tuned. If changing, prefer a performant self-hosted variable font or system stack rather than a decorative family.

Recommended roles:
- Marketing/home display: 32–40px desktop, 28–32px mobile, semibold.
- Page title: 24–28px.
- Section title: 16–18px semibold.
- Body: 14–16px with comfortable line-height.
- Dense table/roster: 13–14px only where scanning density justifies it.
- Labels/captions: 12–13px, but preserve contrast.
- All operational numbers: tabular numerals; medium/semibold; align by decimal/digit where useful.

Avoid uppercase except compact overlines/status labels. Avoid thin weights.

### Spacing
Adopt a small predictable scale: 4, 8, 12, 16, 24, 32, 48. Most product surfaces should live in 8–24px. Use large empty space primarily on signed-out/marketing Home, not in war operations.

### Corners and borders
- Main panels: 10–12px radius.
- Controls: 8–10px.
- Small tags/chips: pill only if they are genuinely tag-like.
- 1px subtle borders are the default separation.
- Avoid nesting cards inside cards inside cards.

### Elevation
In dark mode, elevation should come from slightly lighter surface + border + occasional soft shadow. Do not make every card float.

### Motion
Motion should explain state and preserve context:
- 120–180ms for hover/focus/expand transitions.
- 180–240ms for drawers/panels.
- Small opacity/translate changes only; avoid scale-bounce for serious operational UI.
- Never animate critical numbers continuously.
- Respect `prefers-reduced-motion` (already present).

### Icons
Use icons as fast recognition aids, not as mystery controls. Icon-only controls require accessible names/tooltips and generous hit areas. Reuse one icon family (Material is fine) consistently.

### Data visualization
Data visuals should inherit the semantic system:
- same background/surface colors;
- same typography;
- semantic/provenance colors;
- quiet gridlines;
- strong hover/focus detail;
- no hardcoded page-specific A/B colors unless they are tokenized and accessible;
- compact explanatory labels outside the plotting area where possible.

## Application shell specification

### Desktop
Use a persistent left rail that can collapse. Suggested hierarchy:

**Command**
- Overview
- Live War

**Intelligence**
- Opponent Scout

**Planning**
- Chain Planner
- Faction Readiness (only once real #86-backed workflow exists)

**Training**
- My Training
- Gym Explorer

Bottom/account area:
- Account & Connections
- Preferences (only real preferences)
- Data Status / Diagnostics (role gated)
- Privacy / Terms
- Sign out

Do not show Login as permanent navigation when authenticated. Do not show lock icons beside every protected page; authorization should determine visibility/access behavior.

### Top bar
Keep it sparse:
- current page/context;
- optional global live-war pulse if a war is active;
- command/search shortcut later if genuinely useful;
- avatar/account trigger.

Remove the Torn City link from the global app bar. When a contextual link to Torn is useful (attack target, faction, player), put it next to that entity/action.

### Page header
Every normal page should start with:
- concise title;
- one sentence of user-facing context only if needed;
- at most one visually dominant primary action;
- optional freshness/provenance on the right.

Do not use implementation phrases like “repository-backed public war state” or “authenticated claim-bound stats.”

### Responsive behavior
Use container-aware composition where useful. Mobile is a separate priority order:
- operational alert/action first;
- score/essential status second;
- primary task content third;
- filters/details collapse into sheets/drawers;
- account/legal/diagnostics remain secondary.

Aim for controls around 40px desktop and ~44px touch height as a product standard, while maintaining WCAG’s minimum target requirements.
