# S03: S03 — UAT

**Milestone:** M002
**Written:** 2026-05-01T21:13:00.536Z

# S03: S03 — UAT

**Milestone:** M002
**Written:** 2026-05-01

## UAT Type

- UAT mode: artifact-driven
- Why this mode is sufficient: This slice is a reconstruction/persistence backend change with deterministic DB/test assertions; correctness is best proven via integration and schema tests rather than manual UI interaction.

## Preconditions

- Test project builds successfully.
- SQLite-backed integration tests can create and query the EF database.
- Reconstruction pipeline test fixtures include derived train samples.

## Smoke Test

Run `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"` and confirm all tests pass, proving end-to-end reconstruction writes provenance rows.

## Test Cases

### 1. Per-train provenance persistence is complete and scoped

1. Execute `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~DbPipelineIntegrationTests"`.
2. Inspect assertions that query `ModifierProvenance` rows by `DerivedGymTrainLogId`.
3. **Expected:** For each derived train, three provenance rows exist (personal, faction, company) with deterministic scope mapping and expected statuses.

### 2. Unresolved faction/company diagnostics are machine-stable

1. Execute `dotnet test tests/HappyGymStats.Tests/HappyGymStats.Tests.csproj --filter "FullyQualifiedName~ModifierProvenanceSchemaTests|FullyQualifiedName~DbPipelineIntegrationTests"`.
2. Inspect assertions for unresolved faction/company reason codes and status values.
3. **Expected:** Missing dependency paths persist unresolved rows with stable reason codes (including unknown-faction / unknown-company placeholders) and do not drift across runs.

## Edge Cases

### Missing owner dependency context during reconstruction

1. Run the integration test path that exercises absent faction/company dependency data.
2. **Expected:** Reconstruction still persists provenance rows transactionally; personal remains verified, faction/company remain unresolved with explicit reason codes rather than missing rows.

## Failure Signals

- `DbPipelineIntegrationTests` failures indicating missing/extra provenance rows per derived train.
- Status/reason-code assertion failures for unresolved faction/company scopes.
- Full-suite regressions after reconstruction transaction changes.

## Not Proven By This UAT

- API payload confidence scoring and red→green mapping behavior (belongs to S04).
- Frontend gradient rendering/tooltip UX (belongs to S05) and live operator override UX (belongs to S06).

## Notes for Tester

- Prefer test assertions over ad-hoc DB inspection; these tests encode the canonical contract expected by downstream slices.
- If a failure occurs, inspect `ReconstructionRunner` provenance materialization and transaction refresh boundaries first.
