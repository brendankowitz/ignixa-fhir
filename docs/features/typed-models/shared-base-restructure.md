# Design: Shared-Base Typed Models (inheritance over JSON views)

**Feature**: typed-models
**Status**: Implemented (R4 + R5) — decision recorded in [ADR-2608](adr-2608-shared-base-models.md)
**Created**: 2026-06-14

## Context

The typed-model generator currently emits one self-contained package per FHIR version (shipped: `Ignixa.Models.R4`). Every type is regenerated per version, so a normative datatype like `Coding` — byte-identical across R4/R4B/R5 — is duplicated in every package, and there is no shared type a cross-version consumer can write against.

Firely solves this with `Hl7.Fhir.Base`: shared/normative types live once, per-version assemblies add the rest. We can do the same, and more cleanly, because **our facades are stateless views over a `JsonObject`** — they own no data, so a base type and a version subclass are just two sets of accessor properties over the same node. Inheritance therefore adds members without any field-layout or data-duplication cost that a POCO model would incur.

This design restructures the typed models into a shared base layer plus per-version subclasses, moves the per-version packages under `src/Core/Models`, and defines how a type is classified as shared vs version-specific.

## Goals

- A shared base layer of typed facades for types that are identical (or additively divergent) across targeted versions.
- Per-version packages that **inherit** the base and add only their deltas.
- Cross-version code can be written against the base type; the version-specific view is an explicit, zero-copy opt-in.
- The core (`Ignixa.Serialization`) stays version-agnostic; all version knowledge is opt-in via the version packages.

## Non-Goals

- Retiring the existing hand-written `*JsonNode` facades (Bundle, OperationOutcome, Parameters, …). They coexist; consolidation is a separate, later migration.
- `value[x]`/binding *semantic* version conversion (an `IVersionConverter` is future work). This design only shares structurally compatible shapes.
- Full resource coverage or full version fan-out in the first cut (see Phasing).

## Architecture & layout

```
Ignixa.Abstractions
  └─ Ignixa.Serialization            (Core)
        • runtime: BaseJsonNode, ResourceJsonNode, MutableJsonList, PrimitiveElement
        • generated SHARED/base facades        → namespace Ignixa.Models
        • version-aware model registry          (see Version resolution)
        • existing hand-written *JsonNode facades (unchanged; coexist)
        └─ src/Core/Models/Ignixa.Models.R4   [opt-in]  → namespace Ignixa.Models.R4
        └─ src/Core/Models/Ignixa.Models.R5   [opt-in]  → namespace Ignixa.Models.R5
```

- Base facades are emitted into the `Ignixa.Serialization` assembly under namespace **`Ignixa.Models`** (reads as the shared layer; physically in Core so it has no extra dependency and the runtime base classes are in scope).
- Per-version packages move from `src/Models` → **`src/Core/Models/Ignixa.Models.{Version}`**, reference `Ignixa.Serialization`, and contain only version-specific subclasses + the version's entry-point helpers and registration.
- No dependency cycle: version packages depend on Serialization; Serialization depends on neither.

## Type classification engine

A pre-pass loads the `DefinitionCollection` for **every targeted version** and classifies each type and each element by **type code + cardinality + binding** (not name alone — identical names can hide a retype/recardinality/rebinding). Three buckets:

| Bucket | Definition | Emission |
|---|---|---|
| **Identical** | element set and every element's type/cardinality/binding match across all targeted versions | Emit **once** as a base type in `Ignixa.Models`. No subclass. Version namespaces may `global using`-alias it for unqualified use. |
| **Additive** | a later version only *adds* elements; all shared elements are identical | `Base.X` holds the common shape; `{Version}.X : Base.X` adds the version-only accessors. |
| **Incompatible** | a *shared* element differs (cardinality `0..1`↔`0..*`, retype, binding/enum change) | `Base.X` **omits** that element; each `{Version}.X` defines it with the version-correct type. Base stays Liskov-substitutable. |

`FhirVersion`-gating is **not** the primary mechanism — because deltas live in subclasses, the base never advertises a member invalid for some version. Gating remains only as a rare, documented escape valve if a future case needs it.

Classification is computed over the **currently-targeted** version set. Adding a divergent version later can move a type from *identical/additive* → *incompatible*, demoting an element from base to per-version. This churn is accepted; it is detected by the classifier and surfaced in generator output (and caught by the regen-drift guard, see Testing).

## Inheritance hierarchy & namespaces

```
ResourceJsonNode → DomainResourceJsonNode → Ignixa.Models.Patient → Ignixa.Models.R4.Patient
                                                                   → Ignixa.Models.R5.Patient
BaseJsonNode → Ignixa.Models.Coding          (identical → used directly, no subclass)
BaseJsonNode → Ignixa.Models.HumanName → Ignixa.Models.R5.HumanName   (only if R5 diverges)
```

- A `DomainResourceJsonNode` base is introduced between `ResourceJsonNode` and resource facades (mirrors FHIR's `DomainResource`).
- Ctor contract flows through the hierarchy: the base declares the `internal (JsonObject)` ctor (resources) / public `(JsonObject, FhirVersion?)` ctor (datatypes); subclasses chain `: base(...)`. `As<T>()`'s reflection fallback binds the concrete subclass's ctor as today.
- Backbone facades follow the same rules recursively (a backbone identical across versions lives in base; a divergent one is per-version).

## Version resolution & entry points

Core parse stays version-agnostic and behaves exactly as today: it lands on `ResourceJsonNode` (or a hand-written facade for resourceTypes registered in the existing `ResourceTypeRegistry`). Typed base/version views are never produced implicitly by parse — they are always reached via an explicit `As<T>()` / entry point below. The existing `ResourceTypeRegistry` is unchanged.

1. **Explicit, compile-time (primary):** `node.As<R4.Patient>()` — the existing zero-copy reinterpret. The type argument carries the version; no enum needed. `As<Ignixa.Models.Patient>()` yields the shared view.
2. **Per-version convenience (in each version package):**
   ```csharp
   R4.Parse<Patient>(json)        // deserialize straight to the R4 view
   node.AsR4<Patient>()           // reinterpret an existing node as its R4 view
   ```
3. **Runtime dispatch by `FhirVersion` (the multi-tenant case):**
   ```csharp
   Ignixa.Models.Patient p = node.AsVersion(FhirVersion.R4);   // returns the base-typed instance for that version
   ```
   Backed by a **version-aware registry** (`(resourceType, FhirVersion) → factory`). Each version package **self-registers** its types on load (via `[ModuleInitializer]` or an explicit `R4Models.Register()`), so the enum API lights up only for referenced version packages. Returns the instance typed as the shared base (`Ignixa.Models.Patient`) with `FhirVersion` stamped; deltas are reached with a further `As<R4.Patient>()`. On a registry **miss** (the owning version package was never referenced/registered) `AsVersion` **throws `InvalidOperationException`** rather than returning the original node with the version stamped — silently handing back a wrong-typed facade would be a correctness bug. A best-effort `node.TryAsVersion(FhirVersion, out var versioned)` is provided for callers that can tolerate a miss: it returns `false` and yields the node unmodified.

A generic `As<T>(FhirVersion)` overload is intentionally **not** provided — `As<R4.Patient>()` already carries the version in the type.

## Generator changes

- Today: per-version invocation. New: a **multi-version pass** — load all configured versions, build the classification model, then emit (a) base types into the Serialization `Ignixa.Models` tree, (b) each version's subclasses, and (c) per-version entry-point helpers + registration.
- A single-version run still works but cannot compute "shared" alone; the shared classification is a build input for that case.
- Checked-in generated output, as today (per ADR-2606's checked-in decision).

## Existing hand-written facades

The ~30 `*JsonNode` facades and the generated base types **coexist** — different names (`BundleJsonNode` vs `Ignixa.Models.Bundle`) and namespaces, no collision. The generator carries a **skip-list** so it never clobbers a server-critical hand-written facade. Consolidating the two (retiring hand-written facades in favour of generated equivalents) is a separate migration, deliberately out of scope to avoid destabilizing the request pipeline.

## Testing

- **Classification unit tests:** a representative type in each bucket lands in the right place (identical→base only; additive→base + subclass members; incompatible→element absent from base, present per-version). Implemented in `test/Ignixa.Models.Tests/ClassificationLockTests.cs` — reflection over the built R4/R5 assemblies (no FHIR-package load): an IDENTICAL type (`Coding`, `Quantity`) has no per-version subclass; a subclassed type's `BaseType` is the shared base (`R4.Patient.BaseType == Ignixa.Models.Patient`); an INCOMPATIBLE element (`Attachment.size`) is absent from the base and typed per-version (`int?` R4 vs `long?` R5).
- **Agreement tests:** base and version facades agree with the schema-aware `IElement`/FHIRPath view over the same node — `test/Ignixa.Models.R4.Tests` (47 tests across runtime-agreement, primitive shadow, choice, backbone, and primitive fidelity incl. `ValueRaw`). After the split, identical types are referenced as `Ignixa.Models.X` (e.g. `Ignixa.Models.Quantity`) and version-specific types fully-qualified as `Ignixa.Models.R4.X` — the entry-point class `Ignixa.Models.R4.R4` shadows a bare `R4` namespace alias, so a namespace alias cannot be used.
- **Cross-version test:** `test/Ignixa.Models.Tests/CrossVersionTests.cs` — `R4.Patient` and `R5.Patient` over the SAME parsed node (`ReferenceEquals` on `MutableNode`); base `Ignixa.Models.Patient` substitutability (a method typed against the base works for both); INCOMPATIBLE divergence (`Attachment.size` `int?`/`long?` + a >`int.MaxValue` value read through R5 that the R4 accessor throws on); and `AsVersion(FhirVersion)` runtime dispatch returning the concrete `R4.Patient`/`R5.Patient` (after `R4.R4.Register()` / `R5.R5.Register()`, since `[ModuleInitializer]` is lazy).
- **Regen-drift guard:** `build/check-typed-model-regen.ps1` (and `.sh`) regenerates `typed-model` and fails if the on-disk generated output changes — covering `src/Core/Ignixa.Serialization/Generated/Models` and `src/Core/Models/Ignixa.Models.{R4,R5}/Generated`. It snapshots the dirs before/after regeneration (commit-state independent), so it works pre-commit and is equivalent to "regenerate, assert no `git diff`" in CI. It also catches classification churn when a divergent version is added. The FHIR packages are cached offline, so it does not hit the network. Keep it OUT of the default fast `dotnet test` run (it invokes the generator); run it in CI or locally via `pwsh build/check-typed-model-regen.ps1`.

## Phasing

1. **R4 + R5** — the minimum that exercises classification and proves the cross-version thesis (base + two subclasses + runtime dispatch + cross-version test).
2. R4B, then STU3 / R6-ballot — each addition re-derives the shared set; expect some base→per-version demotions.

## Open questions / accepted risks

- **Base namespace** `Ignixa.Models` (decided; trivially changeable). Alternative considered: keep base in `Ignixa.Serialization.Models` alongside the hand-written facades — rejected for readability/layer signalling.
- **Shared-set churn** when a divergent version joins — accepted; detected by the classifier and the regen-drift guard, surfaced as a diff to review.
- **`DomainResourceJsonNode` introduction** — new base class in the runtime hierarchy; must not disturb existing `ResourceJsonNode` consumers.
