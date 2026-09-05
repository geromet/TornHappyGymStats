## Verification and UX QA

### Golden user tasks
Create deterministic and human-readable scenarios:
1. Signed-out user understands HGS and signs in.
2. Signed-in user opens current war and identifies the most urgent action.
3. User understands whether war data is current/stale/inferred.
4. User scouts an opponent and sees evidence/sample quality.
5. User enters a hit budget and gets a recommended chain plan.
6. User reviews personal training and opens deeper gym exploration.
7. User connects/replaces/revokes Torn credentials safely.

For each task measure/inspect:
- actions/clicks to useful answer;
- time to first meaningful content;
- keyboard completion;
- error recovery;
- 390/768/1440 layout;
- no unexpected overflow/layout shift.

### Visual regression
Use the existing deterministic render/screenshot approach as a product gate. Store representative stable states, not every possible DTO combination:
- Home signed out / signed in / no-war / active-war.
- War active / stale / no-war / chain danger / holes.
- Scout normal / sparse evidence / error.
- Chain default / no-valid-plan / alternatives / mobile.
- Account disconnected / connected / revoke confirmation/error.

### UX acceptance checklist for every UI PR
- What is the page’s primary user question?
- Is the answer in the first viewport?
- Is there exactly one dominant action where appropriate?
- Is any implementation detail competing with user state?
- Are loading/empty/error/stale/setup distinct?
- Are measured/projected/inferred/freshness labels truthful?
- Does 390px reorder information rather than merely stack it?
- Are keyboard/focus/touch targets correct?
- Does the change preserve performance budgets?
- Was the rendered result actually inspected?

## Priority roadmap

### Phase 1 — Make the product silhouette unmistakable
- Reconcile #96 shell contract with current `main`.
- Finish #95 semantic token/state/accessibility foundation without parallel abstractions.
- Adopt task-grouped navigation and account menu.
- Establish page header, status strip, alert rail, data-status patterns.

### Phase 2 — Build the flagship experience
- Finish #75 decomposition so War can evolve safely.
- Implement #97 command-first War.
- Put chain/action risk and freshness in first viewport.
- Move hub/heartbeat/provider details to Data Status.

### Phase 3 — Fix the first impression
- Implement #101 Home/My Training/Gym Explorer.
- Remove API-key/policy-table/3D-cloud hero from Home.
- Make signed-in Overview contextual.
- Lazy-load 3D.

### Phase 4 — Make planning/intelligence feel premium
- #100 result-first Chain Planner.
- #99 evidence-first Scout.
- Tokenized data visualization and explicit detail disclosure.

### Phase 5 — Trust and account polish
- #98 Account & Connections.
- #103 remaining member/diagnostic separation.
- Reconcile privacy wording across footer, Home, Account, and legal pages.

### Phase 6 — Performance, motion, and finish
- Field Core Web Vitals measurement.
- Render/performance regression gates.
- Microinteraction polish.
- Copy audit.
- Density tuning on actual war-night data.

Repository delivery note: the live coordination issue showed a 6-open-PR frontier against a normal 5-PR ceiling at research time. These UX changes should therefore be folded into existing canonical issues/workstreams rather than creating a swarm of new micro-PRs until the drain gate clears.

## “Best on the internet” acceptance standard

The phrase is subjective, so make it operational. HGS earns that ambition when:

### Immediate comprehension
A new user can explain what HGS does after the signed-out Home screen without seeing setup internals.

### War-time utility
At 390px and 1440x900, an active-war user sees chain/action risk, score/freshness, and useful board content in the first viewport.

### Decision-first tools
Chain, Scout, and Training lead with answers and evidence before raw model/data detail.

### Trust
Every estimate makes its status obvious; stale data is visibly stale; user-facing privacy/credential language is specific and non-contradictory.

### Coherence
Navigation, spacing, typography, surfaces, alerts, empty states, confirmation, provenance, and charts look/behave like one product.

### Accessibility
Core tasks are keyboard-completable, focus is obvious/not obscured, touch targets are practical, contrast meets AA, and motion preference is respected.

### Speed
Core Web Vitals meet “good” thresholds at the 75th percentile in production, and important local interactions feel immediate.

### Distinctiveness
A screenshot without the logo is recognizable as HGS: deep tactical navy, compact status strips, evidence/provenance language, calm alert hierarchy, and disciplined numbers.

### Restraint
There is no “card soup,” no neon-for-neon’s-sake, no fake settings, no internal jargon, no decorative disabled controls, and no data visual that exists merely because it looks technical.

## Recommended design commandments

1. **Decisions before data.**
2. **Action before telemetry.**
3. **Truth before confidence theater.**
4. **Dense before cluttered; focused before minimal.**
5. **One semantic system everywhere.**
6. **Mobile reorders; it does not merely stack.**
7. **Diagnostics are a destination, not decoration.**
8. **Speed is part of visual quality.**
9. **Motion explains; it never distracts.**
10. **Every element must earn its place.**
