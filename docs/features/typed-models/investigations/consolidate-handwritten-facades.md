# Investigation: Consolidate Hand-Written Facades

**Feature**: typed-models
**Status**: Proposed
**Created**: 2026-07-09

> Triggered by [PR #319](https://github.com/brendankowitz/ignixa-fhir/pull/319) ("Generate typed model facades for all FHIR resources"), which deletes the `ReservedBaseTypeNames` guard in `CSharpTypedModelLanguage.cs` that previously stopped the generator from emitting a base facade for any resource that already has a hand-written `*JsonNode` facade (`Bundle`, `OperationOutcome`, `Parameters`, `Provenance`, `SearchParameter`, `CapabilityStatement`, `StructureDefinition`, `StructureMap`, `ConceptMap`, `Composition`). This is the "separate migration" [adr-2608-shared-base-models](../adr-2608-shared-base-models.md) flagged as a follow-up: *"consolidate the hand-written `*JsonNode` facades into the generated base."*

## Approach

**Single-type merge via `partial`, not a parallel type + rename.**

Today the generator emits a self-contained sealed-in-practice class (`Ignixa.Models.Patient : DomainResourceJsonNode`, not `partial`). The naive reading of PR #319 lets that continue: for the 10 previously-reserved resources, a *second*, differently-named type now compiles alongside the hand-written one (`Ignixa.Models.Bundle` next to `Ignixa.Serialization.Models.BundleJsonNode`) — two facades over the same JSON, no relationship between them.

Instead:

1. **Generator change**: emit `partial class {Name} : {Base}` (one-line change — add the `partial` modifier).
2. **Move, don't duplicate**: relocate each hand-written `*JsonNode` file into the generated type's namespace (`Ignixa.Models`) and rename the class to match exactly (`BundleJsonNode` → `Bundle`, in `Ignixa.Models`). Partial parts must share namespace *and* type name.
3. **Strip to the delta**: delete every member from the hand-written file that the generator now also produces (properties, enums, constructors, the `ResourceType = "..."` assignment). What survives is only genuine business logic that isn't a StructureDefinition-derived accessor — e.g. `ParametersJsonNode.FindParameter/GetValueAs/SetValue`, `ProvenanceJsonNode.AddTarget/AddAgent`, `StructureDefinitionJsonNode.Parse/GetSnapshotElements`, `ReferenceJsonNode.FromResourceTypeAndId`, the `StructureMap*` `value[x]` helpers.
4. **Base class stays in the generated part** (only one partial declaration may specify the base list): `Bundle : ResourceJsonNode`, `Patient : DomainResourceJsonNode`, etc. — generator classification already gets this right (verified: Bundle is a plain `Resource`, not `DomainResource`, in both today's hand-written base and FHIR's actual model).

This collapses what would otherwise be a two-type migration (generated `Ignixa.Models.Bundle` vs. hand-written `BundleJsonNode`, with `ResourceTypeRegistry` and every `is`/`As<T>()` call site needing to flip from one to the other atomically) into a same-type edit. There is never a window where two types both claim to represent `Bundle` — `ResourceTypeRegistry` only ever points at one type because there is only one type.

**Breaking change, accepted deliberately.** The rename (`Ignixa.Serialization.Models.BundleJsonNode` → `Ignixa.Models.Bundle`) changes the public type name and namespace. Internal call sites are a mechanical rename (or bridged with a compile-time-only `global using BundleJsonNode = Ignixa.Models.Bundle;` alias to avoid touching them). External NuGet consumers referencing the old name break — accepted as a normal breaking change under the pre-release versioning model (no `[Obsolete]` forwarding shim); confirmed with the repo owner (2026-07-09) that this feature has no external consumers yet worth shimming for.

## Tradeoffs

| Pros | Cons |
|------|------|
| One type per resource — no dual-dispatch window, no atomicity requirement between `ResourceTypeRegistry` and call sites | Every hand-written file needs manual triage: which members are generator-duplicates (delete) vs. genuine business logic (keep) |
| Generated members are strictly higher fidelity (generated `Bundle` gains `Identifier`, `Timestamp`, `Signature` the hand-written version never had) | Namespace/name move is a breaking change for anything outside this repo referencing the old type names |
| Small, mechanical generator change (`partial` keyword) enables the whole migration | Enum-literal parity is not automatically checked — hand-rolled `switch` literal tables (e.g. `OperationOutcomeJsonNode.IssueType`, ~30 literals) must be verified byte-identical against generated `[EnumLiteral]` enums before deletion |
| Fixes an existing layering smell as a side effect: the 9 `*JsonNode` facades under `src/Application/Ignixa.Application/Features/Metadata/Models/` move into Core (`Ignixa.Models`), where resource modeling belongs | Generated types carry `[CompatibleFhirVersions]`, enforced by `As<T>()`; hand-written types had no such check — migration can surface new `InvalidCastException`s on version-tagged nodes that previously passed silently |
| Confirmed viable pattern: hand-written and generated facades already share the identical runtime shape (`GetProperty`/`SetProperty`/`GetListProperty` over the same `MutableNode`, dual `(JsonObject)` / `(JsonObject, FhirVersion?)` constructors) | Highest-risk resources (`Bundle`, `OperationOutcome`, `Parameters`) are load-bearing across the entire REST transaction pipeline — must go last, each behind its own PR and full E2E run |

## Alignment

- [x] Follows architectural layering rules — completes the move of resource/datatype modeling into Core (`Ignixa.Models`), removing it from Application (`Ignixa.Application.Features.Metadata.Models`).
- [x] Developer Experience — one canonical type per resource; no "which `Bundle` do I use" ambiguity.
- [x] Specification compliance — consolidated facades gain full StructureDefinition-derived fidelity (fields the hand-picked hand-written subset never had).
- [x] Consistent with existing patterns — reuses the `partial class` idiom already standard for generator-augmented types in this codebase; no new mechanism introduced.

## Evidence

### Already-duplicated today (predates PR #319)

`GenerateAllDatatypes = true` shipped earlier and already produces `Ignixa.Models.Extension/Identifier/Meta/Reference/Narrative` alongside their hand-written `*JsonNode` counterparts (verified: exactly these 5 datatypes have both a hand-written and a generated facade today — `CodeableConcept`/`Coding` were never hand-written, so they aren't consolidation candidates, just already-generated) — the two-sources-of-truth problem PR #319 widens for resources already exists for datatypes. This makes these 5 the natural, lowest-risk starting phase (no `ResourceTypeRegistry` involvement, small surface).

### Structural parity (verified against real code)

Hand-written `src/Core/Ignixa.Serialization/Models/BundleJsonNode.cs` (`Ignixa.Serialization.Models.BundleJsonNode : ResourceJsonNode`) and generated `src/Core/Ignixa.Serialization/Generated/Models/Patient.cs` (`Ignixa.Models.Patient : DomainResourceJsonNode`) use the identical runtime pattern: `GetProperty<T>`/`SetProperty`, `GetListProperty<T>`, nested `[EnumLiteral]` enums, and the same dual-constructor shape (`(JsonObject)` internal, `(JsonObject, FhirVersion?)` public). Generated classes are not currently `partial`.

### Call-site blast radius (per resource, from repo-wide grep)

- `OperationOutcome` — woven through the Domain exception hierarchy, 20+ files.
- `Parameters` — operation endpoints, `$patch`, import/export.
- `Bundle` — ~90 references across ~30 files: transaction pipeline, search, IPS.
- `Composition`, `ConceptMap`, `StructureMap` — usage localized to IPS/terminology/FML features, not core request path.
- `SearchParameter`, `Provenance`, `StructureDefinition` — moderate, contained usage.

### Full hand-written facade inventory (41 files, verified by direct listing)

- `src/Core/Ignixa.Serialization/Models/` (32 files): the 10 reserved resources' top-level and nested BackboneElement types (`BundleComponentJsonNode`, `BundleLinkJsonNode`, `ConceptMapElementJsonNode`, `StructureMap*JsonNode` ×8, etc.) plus 5 datatypes (`ExtensionJsonNode`, `IdentifierJsonNode`, `MetaJsonNode`, `NarrativeJsonNode`, `ReferenceJsonNode`).
- `src/Application/Ignixa.Application/Features/Metadata/Models/` (9 files): `CapabilityStatementJsonNode` and its nested components — layered in Application today, should live in Core post-migration.
- Runtime base classes (`BaseJsonNode`, `DomainResourceJsonNode`, `IMutableJsonNode`, `ResourceJsonNode`, 4 files, not counted above) are **not** migration candidates — they are what both hand-written and generated facades derive from.

## Phased plan

1. **Phase 0**: merge PR #319 with a doc note steering server code to keep using `*JsonNode` until each resource is migrated; generator change to emit `partial`; add round-trip parity tests (hand-written vs. generated output over identical JSON) for the 10 reserved resources before any deletion.
2. **Phase 1 (low risk)**: the 5 datatypes already duplicated — `Extension`, `Identifier`, `Meta`, `Narrative`, `Reference`. No `ResourceTypeRegistry` involvement.
3. **Phase 2 (contained resources)**: `Composition`, `ConceptMap`, `StructureMap`, then `SearchParameter`, `Provenance`, `StructureDefinition`.
4. **Phase 3 (Application-layer facades)**: replace the 9 `Metadata/Models/*JsonNode` files with generated `Ignixa.Models.CapabilityStatement` and friends — resolves the layering smell simultaneously.
5. **Phase 4 (load-bearing, last)**: `OperationOutcome`, `Parameters`, `Bundle` — one PR per resource, full E2E run each.

## Version scope

This migration covers **R4 and R5 only** — the only versions with generated typed models today (`src/Core/Models/Ignixa.Models.{R4,R5}`). `FhirVersion` also enumerates `Stu3`, `R4B`, and `R6`, but:

- STU3 has no generated typed models yet — [adr-2609-stu3-classification-group](../adr-2609-stu3-classification-group.md) (classifying STU3 as its own isolated group) is still **Proposed**, not implemented.
- R4B and R6 generation has not been investigated at all (open follow-up candidates per the [feature readme](../readme.md)).

Consolidating the hand-written facades for those versions is therefore **blocked on generator work that doesn't exist yet**, not a decision this investigation can make. Tracked explicitly as a follow-up: once ADR-2609 (or an R4B/R6 equivalent) ships generated models for a version, that version's facades become eligible for the same `partial`-class consolidation described here — no new design needed, just the phased plan re-run against that version's generated surface.

**`ResourceTypeRegistry` is global and version-blind — this is a real constraint on Phase 2/4, not just a documentation gap.** `src/Core/Ignixa.Serialization/ResourceTypeRegistry.cs` is a single `Dictionary<string, Func<JsonObject, ResourceJsonNode>>` with no `FhirVersion` parameter, and it only covers 5 of the 10 reserved resources: `Parameters`, `Bundle`, `OperationOutcome`, `Provenance`, `SearchParameter` (`CapabilityStatement` and the rest are constructed directly by Application-layer code, not via this registry). The version guard actually lives in `ResourceJsonNode.As<T>()` (`src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs:208`): it checks the *node's* `FhirVersion` against the *target type's* `[CompatibleFhirVersionsAttribute]` and throws `InvalidCastException` on mismatch — but only when `FhirVersion` is set and not `Unspecified`, and only for version-marked target types. Hand-written facades carry no `CompatibleFhirVersionsAttribute`, so they're exempt from this check today; generated facades are tagged (e.g. `[CompatibleFhirVersions(R4, R5)]` on `Patient`).

Consequence: once `Bundle`/`Parameters`/`OperationOutcome`/`Provenance`/`SearchParameter` are merged into their R4/R5-tagged generated types, `.As<T>()` calls against an STU3/R4B/R6-tagged node **start throwing** where they previously succeeded — a genuine behavior change for those versions, not merely "no generated model to migrate to yet." Phase 2 (`Provenance`, `SearchParameter`) and Phase 4 (`Bundle`, `Parameters`, `OperationOutcome`) must resolve this explicitly before merging — e.g. by leaving those specific merged types unmarked (no `CompatibleFhirVersionsAttribute`) until STU3/R4B/R6 generation exists, trading away the version guard to preserve today's permissive behavior. **Phase 0 and Phase 1 (datatypes) are unaffected**: `ResourceTypeRegistry` only dispatches top-level resources via `JsonNodeConverter`, never nested datatypes, so `Extension`/`Identifier`/`Meta`/`Narrative`/`Reference`/`CodeableConcept`/`Coding` carry no registry or version-guard risk — confirming datatypes as the correct first increment.

## Phase 0b status (implemented): normative contract types

Before merging any load-bearing resource facade, a classifier structural-signature probe (`MergeType`,
the same logic `TypedModelClassifier` uses for real generation) was run across `{R4, R5, STU3, R4B, R6}`
for the 15 candidate consolidation types, to separate genuinely version-agnostic types from ones whose
agnosticism was only ever an accident of staying hand-written. Verdict graded by wire-shape misread
hazard: enum-literal drift and additive/absent elements are near-identical (read as null, safe); retypes,
cardinality flips, and object-vs-string changes are hard divergence.

| Type | R4/R5 | +STU3 | +R4B | +R6 (ballot2) | Verdict |
|---|---|---|---|---|---|
| Narrative | Identical | Identical | Identical | Identical | NORMATIVE |
| Reference | Identical | additive only | Identical | Identical | NORMATIVE |
| Meta | Identical | wire-same | Identical | Identical | NORMATIVE |
| Identifier | Identical | enum drift only | Identical | Identical | NORMATIVE |
| Extension | value[x] drift | value[x] drift | value[x] drift | value[x] drift | NORMATIVE |
| Bundle | enum/additive drift | enum drift | clean (tracks R4) | clean (tracks R5) | NORMATIVE |
| Parameters | value[x] drift only | value[x] subset | clean | clean | NORMATIVE |
| OperationOutcome | enum drift only | enum drift only | clean | clean | NORMATIVE |
| Provenance | R5 additive | **hard**: `agent.who`/`entity.what` choice-type change, `activity` retype | clean | additive | NOT-NORMATIVE |
| SearchParameter | R5 additive | **hard**: `component.definition` string↔object | clean | clean | NOT-NORMATIVE |
| StructureDefinition | soft | **hard**: `context` retype | clean | clean | NOT-NORMATIVE |
| CapabilityStatement | soft | **hard, massive**: 22 incompatible elements, 3 STU3-only backbones | clean | enum drift | NOT-NORMATIVE |
| StructureMap | **hard within R4/R5**: `source.defaultValue[x]` shape change | worse | tracks R4 | tracks R5 | NOT-NORMATIVE |
| ConceptMap | **hard within R4/R5**: `equivalence`→`relationship` rename, cardinality/restructure | worse | tracks R4 | tracks R5 | NOT-NORMATIVE |
| Composition | **hard within R4/R5**: cardinality flips, backbone→type change, `attester.mode` retype | worse | tracks R4 | tracks R5 | NOT-NORMATIVE |

**8 NORMATIVE, 7 NOT-NORMATIVE.** R4B tracked R4 with zero new hard divergence across all 15 types; R6
(ballot2) tracked R5 the same way — STU3 is the sole gatekeeper, and neither "undetermined" version
in the original open question turned out to be undetermined.

**Correction found while implementing this phase:** the table above came from a standalone probe that
linked the classifier's source directly, outside the real `RunTypedModelMultiVersion` pipeline. Running
the actual generator against the real R4/R5 packages (Task 1) found genuine, not-metadata-only R4/R5
divergence for `Bundle` (`Bundle.issues` is an R5-only field), `Parameters`
(`Parameters.parameter.value[x]`'s choice-type union differs: R5 adds `Integer64`/`CodeableReference`/
`RatioRange`/`Availability`/`ExtendedContactDetail`, R4's `Contributor` variant isn't in R5), and enum
growth on `BundleType`/`IssueSeverity`/`IssueType`. This does **not** overturn the NORMATIVE verdict for
these three: FHIR's own multi-version classifier only ever places an element in the shared base when
every classified version agrees on its exact shape, so `Bundle.issues` and the diverging `value[x]`
members are excluded from the base and live only in per-version subclasses (`Ignixa.Models.R4.Bundle`,
`Ignixa.Models.R5.Bundle`, etc.) — the base remains a genuinely safe, conservative common subset for any
version, subclasses included. What it DID require fixing: `CSharpTypedModelLanguage`'s attribute-gating
logic must suppress `CompatibleFhirVersionsAttribute` only on the unmarked set's **base** type, never on
its per-version subclasses — subclasses exist specifically to hold the elements that differ, so they
must keep enforcing the guard. See Task 1 Step 3 for the corrected implementation and Task 2 for the
regression test that locks this in (`GivenR4TaggedNode_WhenAsR5Bundle_ThenStillThrows`).

**Shipped (this phase):** `CSharpTypedModelLanguage` un-reserves `Bundle`/`Parameters`/`OperationOutcome`
from `ReservedBaseTypeNames` and `Program.cs`'s `ResourceAllowList` (they are now generated for the first
time, still unused) and omits `CompatibleFhirVersionsAttribute` for the base type of all 8 NORMATIVE
types via a new `VersionAgnosticContractTypes` set — per-version subclasses of these types, where the
classifier emits any, keep their attribute. This does **not** merge the three hand-written resource
facades yet — it only makes the generated counterparts exist and stay permissive, so that merge (a
separate, larger plan — each of `BundleJsonNode`/`ParametersJsonNode`/`OperationOutcomeJsonNode` has
multiple nested hand-written types and several call sites, comparable in shape to the Phase 1a `Extension`
merge but larger) doesn't regress `As<T>()` for STU3/R4B/R6-tagged nodes when it happens.

**Decision for the 7 NOT-NORMATIVE types:**
- `Provenance`, `SearchParameter`, `StructureDefinition`, `StructureMap`, `ConceptMap`, `Composition`:
  proceed with consolidation in a future phase, but **keep** `CompatibleFhirVersionsAttribute(R4, R5)`
  on the merged type. Their divergence is real (not an artifact of staying hand-written), so `As<T>()`
  throwing for an STU3-tagged node reinterpreted through one of these is correct behavior — the same
  guard ADR-2609 relies on for `Patient`. STU3 typed access to these arrives via ADR-2609's `Stu3.*`
  types, not a shared base.
- `CapabilityStatement`: **excluded from consolidation entirely**, not just deferred pending STU3
  generation. The Application-layer facades (`ResourceComponentJsonNode` and siblings) don't merely
  tolerate STU3 — they implement STU3-specific structural behavior (STU3-only backbones, retyped
  elements) the R4/R5-classified scaffolding cannot represent. Revisit only once ADR-2609 ships and a
  real `Stu3.CapabilityStatement` exists to hold that logic instead.

## Verdict

**Recommended.** The single-type `partial`-class merge is strictly better than a parallel-type-plus-rename approach: it removes the registry/call-site atomicity risk entirely (there is only ever one type per resource, so nothing can be "half migrated" at the type-identity level), costs one line in the generator, and turns the remaining work into per-resource, independently reviewable PRs with a natural risk ordering (datatypes → contained resources → Application facades → load-bearing core resources). The two risks that don't go away — enum-literal parity and newly-enforced version gating — are exactly the things Phase 0's parity tests exist to catch before any hand-written code is deleted. Breaking the public type names is accepted; this is pre-release with no external consumers to shim for.
