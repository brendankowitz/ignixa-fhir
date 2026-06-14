# ADR-2606: High-Fidelity Typed Models as Source-Generated Facades

**Status**: Proposed
**Date**: 2026-06-14
**Feature**: typed-models

## Context

The server exposes two model surfaces: the schema-aware, version-agnostic `IElement`/`ISourceNavigator` runtime, and a small set of hand-written `*JsonNode` facades (`BundleJsonNode`, etc.) that are thin typed wrappers over a `System.Text.Json` `JsonObject`. Neither gives application or plugin authors the Firely-grade ergonomics of a full strongly-typed object graph (`patient.Name[0].Family`, value-set enums, IntelliSense, compile-time element names).

The constraint is that we cannot reach for Firely to get that fidelity. ADR-2510 deliberately keeps `Hl7.Fhir.*` out of the application/data layers, and Firely's architecture fights two things we depend on: its POCOs *own their data* (a second source of truth versus the JSON, with the round-trip-fidelity loss that implies), and it delivers multi-version by shipping one POCO assembly per version in a shared `Hl7.Fhir.Model` namespace — which makes a single process that handles multiple FHIR versions painful. We need fidelity without taking the dependency, without losing schema-driven multi-version support, and without introducing a second source of truth.

## Options Considered

1. **Source-generated POCO facades over JSON** — a Roslyn generator emits per-version strongly-typed partial classes that are zero-copy *views* over the existing `JsonObject`/`IElement` runtime *(viable; validated by spike)*.
2. **Materialized POCO graph** — Firely-style data-owning POCOs with a (de)serializer *(rejected: reintroduces a second source of truth and round-trip-fidelity loss; the problem ADR-2510 avoided)*.
3. **Firely interop adapter** — an optional bridge adapting Firely POCOs to `ISourceNavigator`/`IElement` *(rejected as the primary model: reintroduces the rejected dependency; retained only as an interop/migration aid, and input bridging already exists in `Ignixa.Extensions.FirelySdk5/6`)*.
4. **Runtime reflection accessor** — no codegen, map schema to hand-written datatype structs at runtime *(rejected: partial fidelity, no compile-time safety)*.

## Decision

We will adopt **Option 1**: generate high-fidelity typed models as facades that are views over the underlying JSON, never as data-owning objects. The generator reads the same canonical StructureDefinitions that already produce the per-version schema providers (`Ignixa.Specification/Generated/*CoreSchemaProvider.g.cs`) and emits one assembly per FHIR version (`Ignixa.Models.R4`, `.R4B`, `.R5`, `.Stu3`), each rooted in a **version-distinct namespace**. Because the facades are views and not data, `R4.Patient` and `R5.Patient` can wrap the same node, so cross-version applications — the scenario Firely's shared namespace makes hard — become first-class. The packages are opt-in and never sit on the core request path; the server runs without any of them.

This was the strongest candidate on paper and the only one taken to a prototype. A throwaway spike (`spike/typed-models/`, 21 passing tests) put it through its three hardest parts — primitive/shadow duality (`birthDate` + `_birthDate`), choice types (`value[x]`), and agreement with the runtime — and it held: the typed facade and the schema-aware `IElement`/FHIRPath view agree exactly over the same node, including negative `ofType` discrimination, because there is a single JSON source of truth with nothing to drift. No architectural flaw surfaced. What remains is generator-engineering scope, not an open design question, so the decision is to proceed to an implementation generator rather than run further investigations.

## Consequences

- **Fidelity without the dependency.** Application and plugin authors get Firely-grade typed access while the core stays element-primary, schema-driven, and free of `Hl7.Fhir.*` — honoring ADR-2510.
- **Cross-version becomes first-class.** Version-distinct namespaces plus view-over-JSON let `R4.Patient` and `R5.Patient` coexist in one process and reinterpret the same node; this is the concrete advantage over Firely.
- **The generator carries two accessor templates.** Complex datatypes follow the existing `BaseJsonNode` facade shape; primitives require a separate `PrimitiveElement<T>` backed by `(parent, propertyName)` because a primitive's value and its `_`-shadow are two sibling keys, which `BaseJsonNode` cannot model. This is the largest implementation item and is now de-risked with a working shape.
- **Several contracts must be pinned down in implementation.** A dual constructor/`ResourceTypeRegistry` registration contract so `As<T>()` avoids reflection and string-derived type names; `CA1720` suppression for choice discriminators named after FHIR primitive types under warnings-as-errors; an explicit cache-coherency seam (`InvalidateCaches`) between typed mutation and re-derived `IElement`; and a non-dirtying read view for primitive extensions.
- **Scope and packaging follow-ups remain.** v1 resource/datatype coverage, build-time versus checked-in generated output, real primitive typing fidelity (`FhirDateTime`/`instant` parsing) versus the spike's bare strings, performance versus raw `IElement`, and whether to ship generated common-subset interfaces (`IPatient`) now or defer until a cross-version consumer needs them.
