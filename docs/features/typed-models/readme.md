# Feature: High-Fidelity Typed Models

**Status**: Implemented (R4 + R5, shared-base)
**Created**: 2026-06-13

## Problem Statement

Today the server exposes two model surfaces:

1. **`IElement` / `ISourceNavigator`** — a schema-aware, version-agnostic element tree over raw JSON. Great for FHIRPath, validation, and serialization, but untyped (`element.Children("name")`, stringly-typed navigation).
2. **Hand-written `*JsonNode` facades** (`BundleJsonNode`, `OperationOutcomeJsonNode`, `CapabilityStatementJsonNode`, ...) — thin strongly-typed wrappers over a `System.Text.Json` `JsonObject`. These cover only the ~30 resources/datatypes the server itself touches, and each property is hand-coded.

Neither surface gives application developers (or plugin/extension authors) the **Firely-grade ergonomics** of a full strongly-typed object graph: `patient.Name[0].Family`, compile-time safety, IntelliSense over every element, enums for every value set. Firely delivers that — at the cost of a hard SDK dependency (explicitly rejected in [ADR-2510](../../adr/adr-2510-capability-sourcenode-model.md)) and per-version POCO assemblies that fight our single-wrapper multi-version model.

**The question**: can we offer high-fidelity typed models *without* (a) taking the Firely dependency, (b) losing schema-driven multi-version support, or (c) introducing a second source of truth that breaks JSON round-trip fidelity?

## Constraints

- **No `Hl7.Fhir.*` dependency in Application/DataLayer** (layer rule; ADR-2510).
- **Multi-version must stay first-class** — R4, R4B, R5, STU3, R6 from one runtime, selected per tenant/request.
- **JSON remains the source of truth** — no lossy POCO↔JSON round-trips; extensions, primitive shadow elements (`_birthDate`), and unknown elements must survive.
- **Modular & pluggable** — typed models ship as opt-in NuGet packages, not baked into the core request path. The server must run without them.
- **Reuse existing codegen** — per-version schema providers are already generated from StructureDefinitions (`Ignixa.Specification/Generated/R4CoreSchemaProvider.g.cs`). A POCO generator should be a sibling, not a parallel universe.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [source-generated-poco-facades](investigations/source-generated-poco-facades.md) | Implemented | Generator emits per-version strongly-typed facades *backed by* the existing `JsonObject`/`IElement` runtime — fidelity without a second source of truth. Built as the tenth `ILanguage` over `DefinitionCollection`; shipped for R4. |
| [primitive-fidelity](investigations/primitive-fidelity.md) | Complete | Empirical characterization of `decimal`/date round-trip: untouched JSON is byte-exact, `decimal?` is faithful within `System.Decimal`'s range, dates-as-`string` lossless. Resolved by a raw-`JsonNode` escape-hatch accessor for decimals. |

### Future investigation candidates

- **materialized-poco-graph** — Firely-style POCOs that own their data (object graph, not JSON-backed) with a (de)serializer. Highest ergonomics, but dual source-of-truth and per-version assembly explosion.
- **firely-interop-adapter** — optional `Ignixa.Models.Firely` bridge that adapts Firely POCOs to `ISourceNavigator`/`IElement`. Fastest path to fidelity, reintroduces the rejected dependency; useful only as a migration/interop bridge.
- **runtime-reflection-accessor** — no codegen; map schema to hand-written partial datatype structs at runtime. Low maintenance, partial fidelity.

## Decision

Accepted and implemented across two ADRs: [adr-2606-typed-models](adr-2606-typed-models.md) (source-generated facades as zero-copy views over the JSON/`IElement` runtime — the core model) and [adr-2608-shared-base-models](adr-2608-shared-base-models.md) (shared base + per-version subclasses via inheritance, superseding 2606's one-assembly-per-version packaging). The generator is the tenth `ILanguage` in `codegen/Ignixa.Specification.Generators`, emitting checked-in source via a multi-version classification pass. Shipped for **R4 + R5**: a shared base layer in `Ignixa.Serialization` (namespace `Ignixa.Models`) plus opt-in per-version packages `src/Core/Models/Ignixa.Models.{R4,R5}` that inherit it. Identical types live once; version subclasses add only deltas; cross-version code targets the base (`As<R4.Patient>()` / `AsVersion(FhirVersion)`). Proven by `test/Ignixa.Models.R4.Tests` (47) and `test/Ignixa.Models.Tests` (cross-version + classification, 13); the throwaway spike has been retired. Remaining work is breadth (R4B/STU3/R6 fan-out) and a few edge cases (`contentReference`, enums-in-collections, alias ergonomics), not open design.
