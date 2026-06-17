# Feature: FHIR Faker

Test data generation library for creating realistic FHIR resources across versions.

## Investigations

| Investigation | Status | Date | Description |
|---------------|--------|------|-------------|
| [layered-architecture](investigations/layered-architecture.md) | Proposed | 2025-12-02 | 4-layer architecture design (Resources → Scenarios → Lifecycles → Populations) |
| [scenario-generation](investigations/scenario-generation.md) | Proposed | 2025-12-02 | Advanced scenario generation with state machines and temporal logic |
| [validation-issues](investigations/validation-issues.md) | Viable | 2025-12-02 | Choice type violations and cross-version field compatibility issues |
| [cross-version-compatibility](investigations/cross-version-compatibility.md) | Viable | 2025-12-02 | Schema-driven approach for STU3/R4/R4B/R5/R6 support |
| [enhancement-proposals](investigations/enhancement-proposals.md) | Viable | 2025-12-11 | PatientBuilder and ObservationBuilder enhancements for E2E testing |
| [scenario-builder](investigations/scenario-builder.md) | Viable | 2025-12-05 | CareTeam support, custom identifiers, StateId pattern |
| [state-id-pattern](investigations/state-id-pattern.md) | Viable | 2025-12-05 | Cross-state resource references using StateId |
| [improvements-summary](investigations/improvements-summary.md) | Complete | 2025-12-02 | Implementation summary of cross-version compatibility work |
| [adversarial-data-generation](investigations/adversarial-data-generation.md) | In Progress | 2026-06-16 | Edge-case/fuzz data as a first-class type — extensible category catalog + seeded decorator pipeline, validity measured & bucketed |

## Overview

The FHIR Faker library generates synthetic FHIR resources for testing and development purposes. It follows a layered architecture:

1. **Layer 1: Random Valid Resources** - Schema-driven resource generation
2. **Layer 2: Scenarios** - Temporal sequences of clinical events
3. **Layer 3: Patient Lifecycles** - Full patient journeys from birth to death
4. **Layer 4: Population Generation** - Realistic demographic distributions

## Current Status

- Layer 1: 100% complete (SchemaBasedFhirResourceFaker; density control; seeded generation)
- Layer 2: 75% complete (ScenarioBuilder with 15+ state types; PatientBuilder seeding and edge-case decoration)
- Layer 3: 5% complete (concept only)
- Layer 4: 0% complete (planned)
- Edge-case subsystem: complete (`unicode`, `temporal`, `string` families; extensible catalog; seeded pipeline; manifest output; CLI integration via `--edge-cases`, `--seed`, `--include-invalid`)

## Key Components

- `SchemaBasedFhirResourceFaker` - Core resource generator; exposes `Density` property (`Minimal` / `Realistic` / `Maximize`) and a seeded constructor for reproducible output
- `PatientBuilder` / `PatientBuilderFactory` - Fluent builder with `WithSeed(int)` for reproducible generation and `WithEdgeCases(int? seed, IEnumerable<string>? selectors)` for opt-in edge-case perturbation
- `EdgeCaseCatalog` - Extensible registry of edge-case strategies; create the default set via `EdgeCaseCatalog.CreateDefault()`; select by family (`unicode`, `temporal`, `string`) or category (`unicode.rtl`, `temporal.leap-year`, etc.)
- `EdgeCasePipeline` - Seeded decorator that walks the schema-typed element tree and applies one eligible strategy per leaf; emits a `MutationManifest` for replay
- `MutationManifest` / `MutationRecord` - Structured record of every applied mutation (category, path, before, after, description); serialises to JSON via `ToJson()`
- `GenerationDensity` enum - `Minimal` (required only, default), `Realistic` (currently identical to Minimal), `Maximize` (all optional elements populated)
- `ScenarioBuilder` - Fluent API for clinical scenarios
- `ImmunizationState` - Gold standard for version-aware resource generation
- `FhirVersionHelper` - Cross-version compatibility utilities

## Related ADRs

- ADR-faker-layered-architecture.md (original)
- ADR-faker-scenario-generation.md (original)

## See Also

- [E2E Testing Feature](../e2e-testing/readme.md) - Uses FhirFakes for test data
- [Validation Feature](../validation/readme.md) - Validates generated resources
