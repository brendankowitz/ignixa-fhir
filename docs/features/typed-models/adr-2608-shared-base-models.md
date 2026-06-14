# ADR-2608: Shared-Base Typed Models (inheritance over JSON views)

**Status**: Accepted (implemented for R4 + R5)
**Date**: 2026-06-14
**Feature**: typed-models
**Supersedes**: the per-version packaging decision in [ADR-2606](adr-2606-typed-models.md) (which had every version emit a self-contained assembly). The facades-as-views-over-JSON core decision of ADR-2606 stands unchanged.

## Context

ADR-2606 established source-generated typed facades as zero-copy views over the JSON/`IElement` runtime, packaged one self-contained assembly per FHIR version. That packaging duplicates every type in every version — a normative datatype like `Coding`, byte-identical across R4/R4B/R5, is regenerated in each package — and offers no shared type a cross-version consumer can write against. Firely solves this with `Hl7.Fhir.Base` (shared types once, per-version assemblies add the rest).

We can do the same, and more cleanly than Firely, precisely because our facades are **stateless views** — they own no data, so a base type and a version subclass are just two sets of accessor properties over the same `JsonObject`. Inheritance adds members with no field-layout or data-duplication cost. Full design and rationale: [shared-base-restructure](shared-base-restructure.md).

## Decision

Restructure the typed models into a **shared base layer + per-version subclasses**:

1. **Classification.** A multi-version generator pass classifies every type/element by `(type code, cardinality, binding)` into **Identical** (same across all targeted versions), **Additive** (a later version only adds elements), or **Incompatible** (a shared element differs).
2. **Emission.** Identical types are emitted **once** as base facades in `Ignixa.Serialization` under namespace `Ignixa.Models`. Additive/Incompatible types emit a base holding the shared+compatible shape; per-version subclasses (`Ignixa.Models.R4.X : Ignixa.Models.X`) add additive deltas and define Incompatible elements with the version-correct type. Incompatible elements are **omitted from the base**, keeping it Liskov-substitutable. `FhirVersion`-gating is a rare escape valve, not the mechanism.
3. **Layout.** Base rides with `Ignixa.Serialization` (Core); per-version packages move to `src/Core/Models/Ignixa.Models.{Version}` and reference Serialization. No dependency cycle.
4. **Hierarchy.** `ResourceJsonNode → DomainResourceJsonNode → Ignixa.Models.{Resource} → Ignixa.Models.{Version}.{Resource}` (new `DomainResourceJsonNode` runtime base).
5. **Version resolution.** Core parse stays version-agnostic. Three opt-in entry points: explicit `As<R4.Patient>()` (zero-copy, version in the type); per-version helpers (`R4.Parse<T>`, `node.AsR4<T>()`); and `node.AsVersion(FhirVersion)` runtime dispatch backed by a **version-aware registry** (`(resourceType, FhirVersion) → factory`) that each version package self-registers into.

## Consequences

- **True cross-version code** can target the shared base type; viewing a node as another version is a zero-copy `As<T>()`. This is the cross-version advantage over Firely, now concrete.
- **Less duplication**: identical types live once. R4+R5 produced 71 base types (37 base-only, 34 subclassed); 23 elements were Incompatible across R4/R5 (e.g. `Attachment.size` `unsignedInt`→`integer64`, the `value[x]` choice families) and correctly split per version.
- **Classification churn is accepted**: adding a divergent version can demote a base element to per-version. Detected by the classifier and the regen-drift guard.
- **Consumer ergonomics**: identical types are referenced as `Ignixa.Models.X` (the version package's internal `global using` alias does not export); version-specific types as `Ignixa.Models.{Version}.X`. Documented; a `using` alias to a version namespace is blocked by the entry-point class name (`R4`/`R5`) — renaming those to `R4Models`/`R5Models` would restore aliasing (follow-up).
- **`AsVersion` is lazy**: `[ModuleInitializer]` self-registration fires only once a version assembly is loaded; multi-tenant callers relying purely on `AsVersion` must reference the package or call the explicit `Register()`.

## Implementation status

Implemented and verified for **R4 + R5** (2026-06-14): generator multi-version pass + classifier; base layer in `Ignixa.Serialization/Generated/Models`; `src/Core/Models/Ignixa.Models.{R4,R5}`; runtime `DomainResourceJsonNode` + `VersionedModelRegistry` + `AsVersion`. `All.sln` builds 0/0; `test/Ignixa.Models.R4.Tests` (47) and `test/Ignixa.Models.Tests` (cross-version + classification, 13) pass; regen-drift guard (`build/check-typed-model-regen.{ps1,sh}`) clean.

Follow-ups: R4B/STU3/R6 fan-out; rename version entry-point classes for alias ergonomics; `contentReference` resolution; consolidate the hand-written `*JsonNode` facades into the generated base (separate migration).
