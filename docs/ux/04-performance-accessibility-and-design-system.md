## Performance program

### Budgets
Adopt field targets at the 75th percentile:
- LCP <= 2.5s.
- INP <= 200ms.
- CLS <= 0.1.

Add internal stretch goals for the important authenticated pages after baseline measurement, but do not invent numerical promises before measuring actual deployment.

### High-impact changes
- Remove 3D Plotly from Home initial load.
- Lazy-load Plotly only for Gym Explorer/needed chart tabs.
- Give charts fixed/reserved dimensions to prevent CLS.
- Avoid unnecessary rerenders of large war tables.
- Keep local display interactions off server round trips where feasible.
- Cache/prefetch only where data correctness/freshness semantics remain explicit.
- Virtualize long rosters/tables if real data sizes justify it.
- Use compact SVG/icon assets, no large decorative imagery on operations pages.

### Perceived performance
- Show existing last-known-good data with explicit stale treatment instead of replacing it with a blank spinner where safe.
- Make refresh status visible without disabling the whole page.
- Preserve scroll/filter/selection across refreshes when data identity remains stable.

## Accessibility quality bar

The current focus/reduced-motion foundation is good. Raise the bar with:
- WCAG 2.2 AA as baseline.
- Target sizes meeting WCAG minimum; use ~44px touch targets for primary mobile controls where practical.
- Focus never hidden by sticky headers/drawers.
- Logical headings and landmarks.
- Accessible names for icon controls.
- Status not encoded by color alone.
- Contrast checks for every semantic token and Plotly series.
- Tables that remain understandable when transformed to mobile row layouts.
- Form errors summarized and focused when appropriate.
- No hover-only information.
- Reduced motion preserves all state changes.

## Design-system implementation strategy

Do not build a giant component abstraction layer. The best design system for this codebase is small and semantic.

### Token layer
Extend `AppTheme` / CSS variables with:
- surface hierarchy;
- text hierarchy;
- border hierarchy;
- semantic urgency;
- provenance/freshness;
- numeric/tabular typography;
- control sizes;
- spacing/radius values;
- motion durations/easings.

### Composition layer
Create/reuse only patterns with repeated product meaning:
- `PageHeader`
- `StatusStrip`
- `OperationalAlertRail`
- `Figure` / provenance primitives (already exists)
- shared state primitives (already exists)
- `ConfirmActionDialog` if repeated use proves it
- `DataStatusDrawer`
- dense responsive roster/list pattern
- `ResultHero` only if Chain/other planners genuinely share it

Avoid wrapping `MudButton`, `MudText`, `MudCard`, etc. just for branding. Style/compose them instead.
