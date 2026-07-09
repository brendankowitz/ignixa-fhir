# ADR-2606: High-Fidelity Typed Models as Source-Generated Facades

**Status**: Accepted (implemented for R4)
**Date**: 2026-06-14
**Feature**: typed-models
**Note**: the per-version packaging here (one self-contained assembly per version) is **superseded by [ADR-2608](adr-2608-shared-base-models.md)** (shared base + per-version subclasses). The facades-as-views core decision below stands.

## Context

The server exposes two model surfaces: the schema-aware, version-agnostic `IElement`/`ISourceNavigator` runtime, and a small set of hand-written `*JsonNode` facades (`BundleJsonNode`, etc.) that are thin typed wrappers over a `System.Text.Json` `JsonObject`. Neither gives application or plugin authors the Firely-grade ergonomics of a full strongly-typed object graph (`patient.Name[0].Family`, value-set enums, IntelliSense, compile-time element names).

The constraint is that we cannot reach for Firely to get that fidelity. ADR-2510 deliberately keeps `Hl7.Fhir.*` out of the application/data layers, and Firely's architecture fights two things we depend on: its POCOs *own their data* (a second source of truth versus the JSON, with the round-trip-fidelity loss that implies), and it delivers multi-version by shipping one POCO assembly per version in a shared `Hl7.Fhir.Model` namespace — which makes a single process that handles multiple FHIR versions painful. We need fidelity without taking the dependency, without losing schema-driven multi-version support, and without introducing a second source of truth.

## Options Considered

1. **Source-generated POCO facades over JSON** — a Roslyn generator emits per-version strongly-typed partial classes that are zero-copy *views* over the existing `JsonObject`/`IElement` runtime *(viable; validated by spike)*.
2. **Materialized POCO graph** — Firely-style data-owning POCOs with a (de)serializer *(rejected: reintroduces a second source of truth and round-trip-fidelity loss; the problem ADR-2510 avoided)*.
3. **Firely interop adapter** — an optional bridge adapting Firely POCOs to `ISourceNavigator`/`IElement` *(rejected as the primary model: reintroduces the rejected dependency; retained only as an interop/migration aid, and input bridging already exists in `Ignixa.Extensions.FirelySdk5/6`)*.
4. **Runtime reflection accessor** — no codegen, map schema to hand-written datatype structs at runtime *(rejected: partial fidelity, no compile-time safety)*.

## Decision

We will adopt **Option 1**: generate high-fidelity typed models as facades that are views over the underlying JSON, never as data-owning objects. The generator emits one assembly per FHIR version (`Ignixa.Models.R4`, `.R4B`, `.R5`, `.Stu3`), each rooted in a **version-distinct namespace**, from the same canonical FHIR definitions that already produce the per-version schema providers (`Ignixa.Specification/Generated/*CoreSchemaProvider.g.cs`). The *generator mechanism* itself is a second decision — taken below in **Generator Mechanism** — because the investigation's "Roslyn incremental source generator" framing does not match how this repo actually generates from the specification. Because the facades are views and not data, `R4.Patient` and `R5.Patient` can wrap the same node, so cross-version applications — the scenario Firely's shared namespace makes hard — become first-class. The packages are opt-in and never sit on the core request path; the server runs without any of them.

This was the strongest candidate on paper, was proven by a throwaway spike, and has since been **implemented**. The spike put it through its three hardest parts — primitive/shadow duality (`birthDate` + `_birthDate`), choice types (`value[x]`), and agreement with the runtime — and it held: the typed facade and the schema-aware `IElement`/FHIRPath view agree exactly over the same node, including negative `ofType` discrimination, because there is a single JSON source of truth with nothing to drift. No architectural flaw surfaced. The generator was then built per the **Generator Mechanism** decision below, the spike was retired, and the proof tests now live against the shipped package.

## Generator Mechanism

Choosing facades-over-JSON settles *what* to emit. *How* to emit it is a separate decision, and the investigation got it wrong: it proposed a **Roslyn `IIncrementalGenerator` that reads the canonical StructureDefinitions**. That framing does not survive contact with the repo.

The repo already has a complete offline codegen pipeline that the investigation overlooked:

- **`codegen/fhir-codegen/`** — a vendored fork of Microsoft's `fhir-codegen`. `PackageLoader` pulls the canonical HL7 packages (`hl7.fhir.r4.core#4.0.1`, …) and produces a `DefinitionCollection`: the full StructureDefinition model, backbones, choices, bindings, and references included.
- **`codegen/Ignixa.Specification.Generators/`** — a console app (`Program.cs`) hosting **ten `ILanguage` plugins** (`CSharpCoreSchemaLanguage`, `CSharpValueSetLanguage`, `CSharpSearchParameterLanguage`, `CSharpInvariantLanguage`, `CSharpNarrativeTemplateLanguage`, …). Each consumes the same `DefinitionCollection` and writes a **checked-in `*.g.cs`** into `src/Core/.../Generated`. The 10–12 MB `*CoreSchemaProvider.g.cs` files are produced exactly this way.

So the established pattern is **offline tool → `ILanguage` over `DefinitionCollection` → checked-in `.g.cs`**, not a consumer-build source generator. Two viable mechanisms follow.

### Option A — New `ILanguage` plugin over `DefinitionCollection` *(recommended)*

Add `CSharpTypedModelLanguage.cs` as a sibling to `CSharpCoreSchemaLanguage`, plus a `case "typed-model"` in `Program.cs`. It walks the same `DefinitionCollection` and emits the per-version facade source (`Ignixa.Models.R4/*.g.cs`, …), checked into the repo. The spike's hand-written facades are the emit template.

- **Pros:** richest input model — backbones (`Patient.Contact`), `value[x]` choices, value-set bindings, primitive types, and reference targets are all already present and already walked for the schema provider; co-generates with the value-set enums (`CSharpValueSetLanguage` already emits `AdministrativeGender` — the model generator *references* them rather than re-inventing, removing the spike's hand-written enum); it is the tenth instance of a pattern the repo runs nine times today, so loader, model, and emit conventions already exist.
- **Cons:** the generator (not the output) depends on fhir-codegen, which uses Firely SDK 5.10.2. This is an **offline build-time dependency in `codegen/`**, never a runtime or consumer reference — exactly the status the schema providers already have — so ADR-2510's ban on `Hl7.Fhir.*` in the application/data layers is untouched.

### Option B — Tool that walks the runtime `IFhirSchemaProvider` / `CoreType` model

Skip fhir-codegen at generation time. A standalone tool references `Ignixa.Specification`, instantiates `R4CoreSchemaProvider`, and walks its `CoreType` graph — which already carries `primitive`, `binding`, `isCollection`, `isChoiceElement`, `types[]`, `referenceTargets`, and `childrenFactory` (recursion) — emitting the same checked-in facades.

- **Pros:** no FHIR-package download and no Firely even at build time; a single already-distilled source of truth that the runtime itself consumes, so the facades cannot disagree with the schema provider by construction.
- **Cons:** `CoreType` is a *distilled subset* of the StructureDefinition. Anything it dropped — element-level documentation for XML-doc comments, exact JSON property-name casing for choice variants, summary/constraint detail — is unavailable, so this path risks reverse-engineering from lossy-for-this-purpose data.

### Recommendation

**Option A.** It is the repo's existing, proven mechanism; it has the fullest fidelity (backbones and bindings are the spike's two biggest gaps, and both are first-class in `DefinitionCollection`); and it lets the model generator reuse the value-set enums instead of duplicating them. This also **decides the "build-time vs checked-in" open question in favor of checked-in output** — consistent with every other generated artifact, and mandatory given the size (≈150 resources × 5 versions): regenerating that on every consumer build, as an `IIncrementalGenerator` would, is a non-starter. Option B stays on the table only if taking a Firely-SDK build dependency in `codegen/` is later judged unacceptable even for offline use.

What this mechanism resolves from the investigation's open questions: **BackboneElement** generation (modeled and already walked), **value-set enums** (reuse, don't re-emit), and **primitive `T` selection / choice variants** (`FhirPrimitive` + `isChoiceElement` + `types[]` are in the model). What it does *not* touch — these are generator-independent and remain implementation work: decimal/`instant`/partial-date round-trip fidelity (the spike's bare-`string` shortcut), the two emit templates (`PrimitiveElement<T>` vs `BaseJsonNode`), and the `AsVersion<T>` / `InvalidateCaches` cache-coherency contracts.

## Implementation status

Built and verified for **R4** (2026-06-14):

- **Generator** — `CSharpTypedModelLanguage` (+ `TypedModelGenerationContext`, `CSharpTypedModelConfig`) is the tenth `ILanguage` in `codegen/Ignixa.Specification.Generators`, invoked via the `typed-model` mode in `Program.cs`. It reads the same `DefinitionCollection` as the schema providers and emits checked-in facade source.
- **Package** — `src/Models/Ignixa.Models.R4/` (opt-in, `alpha` stability, multi-targets `net9.0;net10.0`, references only `Ignixa.Serialization`/`Ignixa.Abstractions`). Checked-in generated output under `Generated/`: **60 facade types, 47 enums (23 choice discriminators + 24 value-set enums), 791 properties**, one type per file.
- **Coverage realised** — the three accessor templates (complex/`MutableJsonList`, primitive/`PrimitiveElement`, `value[x]` choice), value-set enums for `required`+expandable bindings (reusing `[EnumLiteral]`/`EnumUtility`; un-expandable bindings such as `Patient.language` stay `string`), recursive datatypes (`Extension`), `Reference`, and flattened BackboneElement facades (`PatientContact`, …). Only **4** elements fall back to raw `JsonNode` — all genuinely out of scope: `contained` (`Resource`), `Narrative.div` (`xhtml`), and one `contentReference` (`ObservationComponent.referenceRange`).
- **Fidelity** — characterized empirically ([primitive-fidelity](investigations/primitive-fidelity.md)): untouched JSON round-trips byte-exact; `decimal?` preserves trailing zeros within `System.Decimal`'s ~28–29 sig-digit range; dates/instants as `string` are lossless. The generator emits a raw-`JsonNode` escape-hatch accessor (`…Raw`) alongside `decimal?` for values beyond decimal's range.
- **Tests** — `test/Ignixa.Models.R4.Tests` (runtime/FHIRPath agreement, primitive shadow, choice, backbone, decimal fidelity + escape-hatch). The throwaway spike has been retired.

Open follow-ups: multi-version fan-out (R4B/R5/STU3/R6 — one generator run + sibling csproj each); `contentReference` resolution (reuse the sibling facade instead of `JsonNode`); enums inside collections; `AsVersion<T>` reinterpret + the safe-vs-`IVersionConverter` boundary; cross-version common-subset interfaces.

## Consequences

- **Fidelity without the dependency.** Application and plugin authors get Firely-grade typed access while the core stays element-primary, schema-driven, and free of `Hl7.Fhir.*` — honoring ADR-2510.
- **Cross-version becomes first-class.** Version-distinct namespaces plus view-over-JSON let `R4.Patient` and `R5.Patient` coexist in one process and reinterpret the same node; this is the concrete advantage over Firely.
- **The generator carries two accessor templates.** Complex datatypes follow the existing `BaseJsonNode` facade shape; primitives require a separate `PrimitiveElement<T>` backed by `(parent, propertyName)` because a primitive's value and its `_`-shadow are two sibling keys, which `BaseJsonNode` cannot model. This is the largest implementation item and is now de-risked with a working shape.
- **Several contracts must be pinned down in implementation.** A dual constructor/`ResourceTypeRegistry` registration contract so `As<T>()` avoids reflection and string-derived type names; `CA1720` suppression for choice discriminators named after FHIR primitive types under warnings-as-errors; an explicit cache-coherency seam (`InvalidateCaches`) between typed mutation and re-derived `IElement`; and a non-dirtying read view for primitive extensions.
- **The generator is the tenth `ILanguage`, not new infrastructure.** Adopting Option A means the facade emitter slots into the existing `codegen/Ignixa.Specification.Generators` console app alongside the nine plugins that already produce checked-in `.g.cs`. The loader, the `DefinitionCollection` model, and the emit/output conventions exist; what is genuinely new is the two facade emit templates and the value-set-enum reuse wiring.
- **Scope follow-ups remain.** v1 resource/datatype coverage, real primitive typing fidelity (`FhirDateTime`/`instant` parsing) versus the spike's bare strings, performance versus raw `IElement`, and whether to ship generated common-subset interfaces (`IPatient`) now or defer until a cross-version consumer needs them. (Build-time-vs-checked-in is now decided: checked-in, per **Generator Mechanism**.)
