# Happy Gym Stats — Frontend & UX North-Star Report

**Audience:** Gerome / TornHappyGymStats product and engineering workflow  
**Date:** 5 September 2026  
**Repository baseline:** `geromet/TornHappyGymStats`, `main` at `93d0be0bf758665ba417dc685700ac22915835c5` according to the live coordination issue at research time.  
**Scope:** What excellent modern frontend/UX actually means, how current Happy Gym Stats compares, what visual/product direction can make it unusually good, and a concrete implementation/verification program.  

## Executive answer

Happy Gym Stats does not need more decoration. It needs a stronger product hierarchy and a much more intentional visual language.

The current application already has ingredients that are unusually good for a community Torn tool: a centralized dark palette, visible keyboard focus, reduced-motion handling, shared loading/error/empty/stale states, and a meaningful measured/projected/inferred provenance vocabulary. Its weakest layer is not backend sophistication; it is what the UI chooses to put first. Current `main` still often presents routes, API-key/import mechanics, dense configuration, generic MudBlazor cards, and operator-ish status before the decision a player came to make.

The north-star should be **Tactical Calm**: a dark, high-trust war operations console that feels fast, deliberate, and almost quiet until something requires action. It should combine the density of a professional operations dashboard with the clarity of a consumer product. The interface should become recognizable as Happy Gym Stats even if every logo is removed.

The biggest changes are:

1. Reconcile the application shell with the task-based information architecture already specified in issue #96: Command, Intelligence, Planning, Training; persistent/collapsible desktop navigation; account/legal/diagnostics out of primary navigation; mobile navigation designed around war-time priority rather than collapsed desktop chrome.
2. Make `/war` the flagship. Chain/action risk first; score/freshness second; useful roster/targets third; alerts fourth; diagnostics last. Replace KPI-card soup with compact strips and rails.
3. Replace the current Home page story. A raw Torn API key field, five-column policy table, and 760px 3D point cloud should not be the first experience. Home should explain value when signed out and summarize current war/training/connection state when signed in. Move 3D exploration into Gym Explorer.
4. Make every decision tool result-first. Chain Planner should show the recommendation before exhaustive combinations or model coefficients. Scout should show evidence-backed conclusions before the detailed roster. Training should show useful trends before the raw 3D cloud.
5. Expand the semantic design system from “a palette” into a full vocabulary: surface levels, typography, spacing, numeric typography, provenance, freshness, urgency, focus, motion, control density, state semantics, and responsive rules.
6. Treat speed as visual design. Target good Core Web Vitals at the 75th percentile (LCP <=2.5s, INP <=200ms, CLS <=0.1), lazy-load Plotly/3D, avoid layout shifts, and keep simple interactions local/instantaneous where feasible.
7. Verify the experience as a product, not just source code: 390/768/1440 rendered states, keyboard flows, accessibility, visual regression, performance budgets, and a small set of golden user tasks.

The result should not look like “MudBlazor with a dark theme.” It should look like an evidence-aware Torn operations instrument.

## What “good frontend design” actually is

### 1. Purpose before aesthetics

Excellent design starts by making the product’s purpose legible. Apple’s 2026 design principles explicitly frame purpose, agency, responsibility, familiarity, flexibility, simplicity, craft, and delight as the foundation of durable design. Their simplicity guidance is especially relevant: simplicity is not minimalism; it is keeping what matters close and letting other things fall away.

For Happy Gym Stats, the design test is therefore not “does this screen look cool?” It is:
- Can a player tell within seconds what matters now?
- Is the next useful action obvious?
- Is important uncertainty visible?
- Can a power user move quickly without drowning a newer user in internals?
- Is the UI calm when nothing is wrong and unmistakable when something is wrong?

### 2. Information hierarchy is the main aesthetic

Nielsen Norman Group’s heuristics remain a useful baseline: show system status, speak the user’s language, preserve control/freedom, use consistent conventions, prevent errors, prefer recognition over recall, support efficiency, remove irrelevant information, and make recovery understandable.

For complex applications, visual quality is primarily a hierarchy problem. Every extra card, caption, technical status, divider, chip, or field competes with the content that matters. “Aesthetic and minimalist” therefore means focused, not empty.

For HGS this means a player opening `/war` should not visually process Hub Connection and Heartbeat at the same level as chain danger. Likewise, someone planning a chain should not have to inspect multiplier coefficients before seeing the best plan.

### 3. Progressive disclosure gives both simplicity and power

Progressive disclosure is a strong fit for Torn tooling: put frequent, decision-driving controls up front and defer rare/advanced controls. It improves learnability and efficiency without removing power.

Examples:
- Chain Planner: hit budget, base respect, goal, presets first; coefficients under “Advanced model settings”.
- War: operational state first; poller/hub details in “Data Status”.
- Scout: conclusions + evidence window first; reconciliation math in row details.
- Account: current connection status + actions first; privacy/version detail expandable.

### 4. The interface must communicate truth, not merely state

Happy Gym Stats has an unusually strong opportunity here because its data can be measured, projected, inferred, stale, or unavailable. That should be a first-class design language rather than a footnote.

The current `FigureKind`-style provenance markers are a genuine strength. Keep them. Extend the same discipline to freshness, coverage, and confidence where the domain actually supports it. Never use “confidence” as decoration or invent percentages the model does not calculate.

### 5. Speed and stability are part of “feel”

The current Core Web Vitals are LCP, INP, and CLS. Google recommends good thresholds at the 75th percentile: LCP <=2.5 seconds, INP <=200 ms, CLS <=0.1. Those are not merely SEO numbers; they correspond to perceived load, responsiveness, and visual stability.

For a war dashboard, responsiveness has an even higher subjective value. Clicking a tab, opening a filter, expanding a row, or toggling local display state should feel immediate. Network/server work should have explicit, localized feedback and should not freeze unrelated UI.

### 6. Accessibility is a quality multiplier

WCAG 2.2 adds criteria around focus not being obscured and target size; the AA target-size minimum is 24x24 CSS pixels (with exceptions), while a product-level standard can be more generous for touch. Keyboard focus must be visible and persistent. State cannot rely only on color.

HGS already has a global `:focus-visible` baseline, reduced-motion CSS, and non-color provenance labels. These are worth preserving and extending. Accessibility should be treated as interaction-quality infrastructure, not compliance garnish.

### 7. Dark UI needs depth, not blackness

The current palette is a good foundation: deep navy background, slightly lighter surfaces, pale primary text, and restrained blue accent. Good dark interfaces build hierarchy with luminance steps, borders, typography, and limited semantic color rather than huge shadows or neon everywhere.

A dark-only product is defensible for an immersive operations console, but it has to be intentional. Contrast must remain comfortable; semantic colors must not become the only state cue; white-ish elements should not flare; and data visualizations need the same semantic tokens as the rest of the interface.

### 8. Delight is the sum of details, not decorative noise

The best-feeling products are satisfying because the whole system is considered: spacing is consistent, copy is concise, transitions clarify state, destructive actions are safe, empty states are useful, and loading does not move the page around.

For HGS, the right emotional target is not playful confetti. It is **confidence under pressure**.
