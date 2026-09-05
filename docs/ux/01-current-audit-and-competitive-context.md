## Current-state audit

### What is already strong

#### Centralized theme
`AppTheme.cs` provides one dark palette source with named primary/secondary/semantic colors plus background/surface/text/border roles. This is the correct foundation; do not replace it with page-local colors.

#### Accessibility baseline
`app.css` has a strong 3px `focus-visible` outline and a reduced-motion media query. That is meaningful UX infrastructure. The provenance markers also include text/border cues, not color alone.

#### Shared truthful states
The project has already landed shared Loading, Empty, Setup Required, Error, and Stale primitives and is actively tracking their adoption in issue #95.

#### Provenance semantics
War figures distinguish measured, projected, and inferred. This is a differentiator versus community tools that often present estimates without obvious epistemic status.

#### Product backlog understands many of the right problems
Issues #94 through #101 are directionally excellent: decision-first density, command-first war, result-first chain planning, evidence-first Scout, better Home/Training, and task-based shell/IA. The remaining job is making this one coherent product rather than a set of isolated issue fixes.

### Systemic weaknesses

#### 1. Current shell is route-first, not task-first
The current `MainLayout.razor` uses a temporary drawer containing a flat list: Home, Login, My stats, Player account, War board, Scout opponent, Settings, Chain calculator, Terms, Privacy, Security. Auth-required items show repeated lock icons. The app bar also carries a global Torn City link.

That conflicts with the closed #96 shell contract, which specifies grouped navigation around Command / Intelligence / Planning / Training, a persistent collapsible desktop rail, sparse top bar, account/legal/sign-out in an account menu, role-gated diagnostics, removal of lock-icon clutter, and removal of the global Torn City link.

This is a repository reconciliation gap: current `main` does not reflect the contract of a closed P0 shell issue. Before treating shell design as “done,” reconcile whether the implementation was never merged, was reverted, or the issue was closed prematurely.

#### 2. Home tells the wrong story
The landing page begins with a Torn API Key password field and `Import + Refresh`, immediately followed by a five-column data-policy table. The dominant content below is a 760px 3D gym point cloud.

That makes the product feel like an import/debug/data project rather than a war/training product. It also creates high cognitive load before the user understands what HGS is for.

Issue #101 already describes the right destination: signed-out value proposition + sign-in + data-handling link; signed-in war context + Scout + compact training summary + connection/data health; 3D exploration moved to Gym Explorer and lazy-loaded.

#### 3. Trust copy is too broad
The global footer says “Data stored locally, never shared externally.” The Home policy table separately says “Persistent-forever” and “General public.” These may describe different categories of data, but the UI does not make that distinction. As written, a reasonable reader can perceive a contradiction.

Replace universal privacy slogans with category-specific, truthful copy: what is stored, where, for how long, what becomes public/aggregate, what happens to a Torn API key, and where to revoke it. #98 is the right home for connection/consent language.

#### 4. My Stats is still implementation-shaped
`MyStats.razor` says “authenticated claim-bound stats,” repeats the Torn API key/import flow, and primarily renders another 760px 3D cloud. “Claim-bound” is architecture vocabulary, not player vocabulary.

This should become My Training: concise summaries, trends, 2D analysis, sample/date context, and an explicit route to deeper Gym Explorer. API credentials should live in Account & Connections once #98 owns that flow.

#### 5. Chain Calculator makes the user solve the UI before the model helps them
The current page exposes base respect, hit budget, max chains, minimum/maximum chain length, spend-every-hit, and raw model coefficients `a` and `b` before the primary Search button. The default result is a dense nine-column combinations table with tiny A/B buttons.

The engine can be powerful without making every control equally prominent. Issue #100 correctly calls for result-first planning: essential inputs -> recommendation -> alternatives -> detailed table/advanced model.

#### 6. `/war` is useful but visually prioritizes telemetry over command
The current active-war view starts with KPI cards for Participation, Open targets, Hole alerts, Hub connection, and Heartbeat. The chain-command panel exists deeper inside each faction card. This means the most time-sensitive decision can be below telemetry that is primarily useful for diagnosing the data pipeline.

Issue #97 defines the better hierarchy: chain/action alert, compact score/freshness strip, useful board, holes, then deeper Data Status. It also explicitly calls for replacing KPI-card soup and putting chain state in the first mobile viewport.

#### 7. Generic component geometry dominates the brand
Much of the UI is `MudCard`, `MudPaper`, `MudGrid`, `MudTable`, `MudChip`, and stock spacing. That is a sensible engineering substrate but not a visual identity. The user sees the component library more than the product.

The answer is not to wrap every MudBlazor component. Instead, define a small number of HGS composition patterns that change the page silhouette: page header, status strip, operational alert rail, section frame, result hero, evidence tag, dense roster row, data-status drawer, and responsive page templates.

#### 8. Page width and density are too generic
`MainLayout` puts essentially everything in one `ExtraLarge` container. But different tasks need different geometry:
- War should use nearly the full available viewport.
- Chain/Scout planning can be broad but structured.
- Account/settings should be much narrower.
- Legal/privacy text should have a readable measure.

A universal container creates either wasted space or over-wide reading lines.

#### 9. Mobile is still largely desktop components with breakpoints
The product backlog correctly states that mobile priority is information order, not stacking desktop panels. That needs to become a hard design rule. At 390px, the first screen must answer “what do I need to do?” rather than show a sequence of stacked summary cards.

#### 10. Performance risk is concentrated in charts and server interaction
Home and My Stats render a large Plotly 3D chart. Plotly is valuable for Gym Explorer but expensive as default landing content. `InteractiveServer` also means interaction quality can depend on the server round trip for many UI events.

The experience program should lazy-load 3D, avoid loading it on Home, keep purely visual/local interactions local where feasible, and measure actual INP rather than assuming server-side interactivity feels fast enough.

## Competitive context

### FFScouter War Room
FFScouter exposes a real-time enemy table with status, fair-fight range, location filters, sort controls, refresh state, and optional travel/landing features. It proves that war users value dense current-state filtering. Its opportunity for HGS is not to copy the table; it is to take the same density and add stronger hierarchy, evidence provenance, and integrated planning.

### TornWarBot
TornWarBot foregrounds attendance, chain monitor, live score, retal/territory alerts, Push Tracker, activity heatmap, war log feed, and revive board. The product lesson is that time-sensitive war utility is about surfaced events and warnings, not dashboards full of equally weighted metrics.

### b0torn
b0torn’s current web panel explicitly says its left menu is grouped “the way a faction actually works,” and hides pages that a role cannot use. It offers wars/chains/raids, faction/member tools, money, reports, settings, and sign-out grouped by user task. This is strong external validation for #96’s task-based shell direction.

### RWARFF
RWARFF’s value is context immediacy: its userscript puts travel/landing/hospital/bounty/FF information on Torn’s war page while the website handles account/device/key management. HGS is a standalone web product, but it can learn from that focus on “put the information where the decision happens.”

### Strategic differentiation
HGS should aim to own a combination competitors do not consistently combine:
- operations-first war layout;
- explicit data provenance/freshness;
- evidence-first Scout;
- planning tools tied to the same canonical data model;
- excellent desktop/mobile web interaction;
- member-safe privacy/connection UX;
- an unusually polished visual system.
