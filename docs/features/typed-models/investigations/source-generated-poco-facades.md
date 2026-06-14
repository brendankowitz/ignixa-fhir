# Investigation: Source-Generated POCO Facades

**Feature**: typed-models
**Status**: In Progress
**Created**: 2026-06-13

## Approach

Offer Firely-grade strongly-typed models **as views over the existing JSON/`IElement` runtime**, not as a new data-owning object graph.

A Roslyn **incremental source generator** reads the canonical StructureDefinitions for each FHIR version (the same inputs that already produce `Ignixa.Specification/Generated/R4CoreSchemaProvider.g.cs`) and emits one strongly-typed `partial` class per resource/datatype. Each generated type is a thin, zero-copy facade — exactly the shape of the hand-written `BundleJsonNode` today — backed by the underlying `System.Text.Json.JsonObject` (`MutableNode`). The hand-written facades become the *seed* for what the generator produces at scale.

```csharp
// Generated: Ignixa.Models.R4 package
public sealed partial class Patient : DomainResourceJsonNode
{
    public MutableJsonList<HumanName> Name => GetListProperty<HumanName>("name");
    public AdministrativeGender? Gender { get => GetCode<AdministrativeGender>("gender"); set => ... }
    public FhirDateTime? BirthDate { get => GetPrimitive("birthDate"); set => ... }
    // every element of the R4 Patient StructureDefinition, generated
}

// usage — fidelity + IntelliSense, but JSON is still the source of truth
var patient = resource.As<Patient>();
string? family = patient.Name[0].Family.FirstOrDefault();
patient.Gender = AdministrativeGender.Female;   // writes through to the JsonObject
```

### Packaging — how multi-version stays first-class

Generate **one assembly per FHIR version**: `Ignixa.Models.R4`, `Ignixa.Models.R4B`, `Ignixa.Models.R5`, `Ignixa.Models.Stu3`. This mirrors the *packaging shape* of modern Firely (`Hl7.Fhir.R4` / `.R5` over a shared `Hl7.Fhir.Base`) — but with a decisive difference: our facades are **views**, so the core request path never depends on them. The server runtime stays version-agnostic and schema-driven exactly as it is today; a typed-model package is purely an opt-in ergonomic layer an application or plugin author pulls in for the version(s) they care about.

Version-divergent elements (e.g. R4 `Patient.contact` vs R5) are handled honestly: each version's generated facade reflects *that version's* StructureDefinition. No lossy superset type, no runtime "is this field valid in this version?" guessing — the type system encodes the version.

### Why facades, not data-owning POCOs

The entire design hinges on keeping JSON as the single source of truth (see Evidence — this is the inverse of Firely). The facade reads and writes the same `JsonObject` that `ToElement(schema)`, FHIRPath, validation, and serialization already operate on. Consequences:

- Unknown elements, extensions, and primitive shadow elements (`_birthDate`) survive untouched — no round-trip loss.
- A typed mutation and a FHIRPath patch see the same bytes; no sync problem between an object graph and its JSON.
- `resource.As<Patient>()` is already implemented as a zero-copy reinterpret over the shared `MutableNode` (`ResourceJsonNode.As<T>`). The generator just supplies the `T`s at full coverage.

### Cross-version applications — solving Firely's biggest weakness

Firely puts every version's models in the **same** `Hl7.Fhir.Model` namespace, so `Hl7.Fhir.R4` and `Hl7.Fhir.R5` both declare `Hl7.Fhir.Model.Patient`. Reference both in one project and every `Patient` is ambiguous — you need `extern alias` gymnastics, and even then you're juggling two namespaces that *look* identical. An application that must process R4 **and** R5 resources in the same process (a gateway, a converter, a multi-tenant server with mixed-version tenants) is fighting the framework. This is one of the strongest reasons ADR-2510 kept us off the Firely model.

We have design freedom Firely gave up, and two independent properties make the cross-version story actually pleasant:

**1. Version-distinct namespaces — no collisions.** Root each package at its version: `Ignixa.Models.R4.Patient`, `Ignixa.Models.R5.Patient`. Both can be referenced from one project with zero ceremony:

```csharp
using R4 = Ignixa.Models.R4;
using R5 = Ignixa.Models.R5;

R4.Patient p4 = resource.As<R4.Patient>();
R5.Patient p5 = resource.As<R5.Patient>();   // distinct, unambiguous types
```

A single-version app can still add a `global using Ignixa.Models.R4;` for unqualified `Patient` — but that's opt-in, never forced. **Never** emit into a shared `Ignixa.Models` namespace; the version segment is load-bearing.

**2. Views, not data — the deeper win.** Because facades don't own data, `R4.Patient` and `R5.Patient` can wrap the *same* `JsonObject`. Distinct namespaces alone wouldn't save Firely: their POCOs own data, so a cross-version app holds two separate object graphs and needs a real conversion pass to move between them. For us, "viewing the same resource as another version" is reinterpreting one node:

```csharp
var asR5 = p4.AsVersion<R5.Patient>();   // same bytes, different typed lens
```

This is honest only where the elements are structurally compatible; genuine version migration (renamed/moved elements) is a separate, explicit transform (a future `IVersionConverter` plugin), not an implicit cast. But for the large stable core of FHIR, the reinterpret is free and lossless.

**3. A version-neutral seam for "write once" logic.** Three layers, pick by how version-specific the code is:

| Code shape | Use | Why |
|---|---|---|
| Generic/structural (copy meta, walk extensions, search) | `IElement` / FHIRPath — already version-agnostic | No facade needed; the universal API |
| Common-subset business logic across versions | Generated shared interfaces (e.g. `IPatient` over the elements stable across supported versions) | Write once, resolve to the concrete `R4.Patient`/`R5.Patient` at the boundary |
| Version-specific logic (divergent elements) | `Ignixa.Models.R4.Patient` etc. directly | Full fidelity for that version; the type encodes the version |

Generate the shared interfaces from the *actual intersection* of the StructureDefinitions, not by hand — and keep them to genuinely stable elements (`id`, `meta`, common datatypes) so they don't rot as FHIR evolves. Over-broad common interfaces are a maintenance trap; when in doubt, leave an element out of the interface and force callers to the concrete type.

The net: the thing Firely makes hard — one process, multiple FHIR versions, strongly typed — becomes a first-class scenario, precisely because the core stays version-agnostic and the facades are disposable views keyed by namespace.

## Tradeoffs

| Pros | Cons |
|------|------|
| Firely-grade ergonomics (IntelliSense, compile-time element names, value-set enums) | Large generated surface (~150 resources × N versions) — build-time and assembly-size cost |
| JSON stays the single source of truth — no dual-representation drift, no round-trip loss | Generator must faithfully model FHIR primitive/shadow duality, choice types (`value[x]`), and contained resources |
| Multi-version preserved: core runtime untouched, one facade assembly per version | A type is bound to a version — but version-distinct namespaces + view-over-JSON make mixing R4/R5 in one process trivial, unlike Firely's shared `Hl7.Fhir.Model` |
| **Cross-version apps are first-class**: `R4.Patient` and `R5.Patient` coexist and can wrap the same node (`AsVersion<T>`) | Optional shared common-subset interfaces must be generated from the spec intersection, and kept narrow to avoid rot |
| Fully modular/pluggable: opt-in NuGet per version, server runs without any of it | Real engineering: a robust FHIR POCO generator is the single largest item in this feature |
| No `Hl7.Fhir.*` dependency — honors [ADR-2510](../../adr/adr-2510-capability-sourcenode-model.md) | Generated facades need their own conformance tests to prove fidelity vs the schema |
| Reuses the existing StructureDefinition → codegen pipeline that already emits schema providers | Choice elements and polymorphic references add generator complexity (typed accessors per variant) |

## Alignment

- [x] Follows architectural layering rules — facades live in standalone `Ignixa.Models.*` packages above Core; the Application/DataLayer request path never references them, so no `Hl7.Fhir.*`-style dependency leaks into business logic.
- [x] Developer Experience — opt-in package; zero config to keep current behavior, one `PackageReference` to gain typed models for a version.
- [x] Specification compliance — generated directly from canonical StructureDefinitions per version; fidelity is testable against the schema.
- [x] Consistent with existing patterns — productionizes the hand-written `*JsonNode` facade + `MutableJsonList<T>` + `As<T>()` pattern; sibling to the existing `FhirPathFunctionGenerator` and `*CoreSchemaProvider.g.cs` codegen.

## Evidence

### Current Ignixa model surface (the seed)

- `BundleJsonNode` (`src/Core/Ignixa.Serialization/Models/BundleJsonNode.cs`) is the canonical hand-written facade: typed properties (`Type`, `Total`, `Entry`) are getter/setters over `MutableNode["..."]`, lists come from `GetListProperty<T>`, value sets are nested `enum`s with `[EnumLiteral]`. The generator's job is to emit exactly this, for every resource, from the StructureDefinition.
- `ResourceJsonNode.As<T>()` (`src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs:196`) already does **zero-copy reinterpretation** of the same `JsonObject` into a typed subclass, including a `ResourceTypeRegistry` fast path and a reflection fallback. This is the runtime hook generated facades plug into.
- `IElement` (`src/Core/Ignixa.Abstractions/Structure/IElement.cs`) and `ToElement(ISchema)` give the schema-aware, version-agnostic runtime that FHIRPath/validation/serialization already share. Facades sit *beside* this, over the same `MutableNode` — not replacing it.

### Multi-version is already schema-driven, already generated

- `FhirVersion` (`src/Core/Ignixa.Abstractions/FhirVersion.cs`) enumerates Stu3/R4/R4B/R5/R6.
- `Ignixa.Specification/Generated/` already contains per-version generated providers: `R4CoreSchemaProvider.g.cs`, `R4BCoreSchemaProvider.g.cs`, `R5CoreSchemaProvider.g.cs` (+ `*ValueSetProvider.g.cs`, `*ReferenceMetadata.g.cs`). The StructureDefinition→C# generation pipeline therefore **already exists** and is the natural place to grow a POCO emitter.
- `FhirPathFunctionGenerator` (`src/Core/Ignixa.FhirPath.Generators/`) proves the `IIncrementalGenerator` toolchain and conventions are already in the repo.

### How Firely does it (local checkout: `E:\data\src\firely-net-sdk`) — and why we diverge

Firely is the high-fidelity reference, and studying it sharpens *what not to copy*:

1. **POCOs own their data.** `Model/Generated/Patient.cs` is a `partial class Patient : DomainResource, INotifyPropertyChanged` with private backing fields. `HumanName.cs` shows the primitive/shadow duality made explicit as **two properties**: `FamilyElement` (`List<FhirString>`, carries id/extensions) plus a convenience `Family` (`IEnumerable<string>`) projection. The object graph *is* the source of truth.
2. **Reflection/introspection drives everything.** `[FhirType]` / `[FhirElement("family", Order=50)]` attributes (`Introspection/FhirElementAttribute.cs`, `ClassMapping`, `PropertyMapping`, `ModelInspector`) are read at runtime to serialize and to build the element view.
3. **POCO → element is an adapter.** `ElementModel/PocoElementNode.cs`, `PocoNavigator.cs`, `PocoBuilder.cs` project POCOs *into* `ITypedElement` so FHIRPath/validation can run. The element model is **derived** from the POCO.
4. **Multi-version = one assembly per version.** Modern Firely ships `Hl7.Fhir.R4` / `.R4B` / `.R5` / `.Stu3` — separate generated `Hl7.Fhir.Model` namespaces over a shared `Hl7.Fhir.Base`. You bind a process to one version (the local checkout is the older single-Core, "Generated for FHIR v1.0.2" form of the same idea).

**The divergence that protects our architecture:** Firely is *POCO-primary → element-derived*. Ignixa is *JSON/element-primary → facade-derived*. Adopting Firely's data-owning POCOs would reintroduce a second source of truth (object graph vs JSON), the round-trip-fidelity problems ADR-2510 avoided, and a reflection-heavy serialization path we don't need. By generating **facades over `JsonObject`** instead, we get Firely's *ergonomics* while keeping Ignixa's *element-primary, schema-driven, multi-version-in-one-runtime* core. We borrow Firely's packaging shape (per-version assemblies) and its generator-from-StructureDefinition discipline — not its data model.

### Open questions to resolve before an ADR

- **Primitive/shadow duality**: expose Firely-style paired properties (`BirthDateElement` + `BirthDate`) or a single richer primitive wrapper that lazily reads `_birthDate`?
- **Choice types (`value[x]`)**: generate per-variant accessors (`ValueQuantity`, `ValueString`) plus a discriminated accessor? Confirm alignment with the existing choice-element semantics in `IElement.Children`.
- **Coverage scope for v1**: full resource set, or core resources + common datatypes first (matching the current hand-written set) to bound the initial generator effort?
- **Generator inputs**: reuse the exact StructureDefinition source feeding `*CoreSchemaProvider.g.cs`, and decide build-time generation vs checked-in generated output (as `Ignixa.Specification/Generated/` does today).
- **Performance**: confirm facade allocation/access cost is acceptable vs raw `IElement` on hot paths (facades should never be forced onto the core request path — they are opt-in).
- **Cross-version surface**: do we ship generated common-subset interfaces (`IPatient`) in v1, or defer them and rely on `IElement` + concrete per-version types until a real cross-version consumer asks? Where does `AsVersion<T>` stop being a safe reinterpret and require an explicit `IVersionConverter`?

## Verdict

*Pending evaluation* — strongest candidate so far: it delivers the requested fidelity while preserving the two non-negotiables (single-runtime multi-version, JSON-as-truth) and stays fully opt-in/pluggable. Compare against `materialized-poco-graph` (higher ergonomics, but dual source-of-truth) and `firely-interop-adapter` (fastest, but reintroduces the rejected dependency) before drafting an ADR.
