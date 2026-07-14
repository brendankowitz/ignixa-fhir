# Investigation: Consolidate Hand-Written Facades

**Feature**: typed-models
**Status**: In Progress
**Created**: 2026-07-09
**Last re-scoped**: 2026-07-14

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

**Re-scope note (2026-07-12) on that assumption:** `Ignixa.Serialization` and `Ignixa.Models.{R4,R5}` are all `IsPackable=true` under `src/Core`, which `.github/workflows/ci.yml` packs and `publish-release.yml` pushes to **public NuGet.org** (not the internal GitHub Packages feed) on every tagged release — this has been happening continuously since `release/0.0.101`, with `release/0.6.19` the most recent tag at time of writing. So this is a genuinely public, versioned package, not a dormant or internal-only one; "no external consumers yet" is a real judgment call about adoption, not about publication reach. Re-affirming the no-shim decision here rather than silently carrying it forward, since it's the one assumption in this doc that isn't independently verifiable by reading code.

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

**Re-scope correction (2026-07-12):** a fresh repo-wide grep (all of `src/` + `test/`, file-count basis, not reference-count) gives materially different numbers for the three Phase 4 types and should supersede the estimates above for sequencing purposes: `OperationOutcomeJsonNode` — **52 files**, including **13 in `Ignixa.Domain`** (the exception hierarchy, not just request/response plumbing — worse than "woven through" suggested); `BundleJsonNode` — **29 files**; `ParametersJsonNode` — **22 files**. `OperationOutcome` is the widest blast radius of the three, not `Bundle` — Phase 4 should do `Parameters` first (narrowest, most contained to operation endpoints), then `Bundle`, and leave `OperationOutcome` for last since a mis-merge there risks destabilizing exception handling across the whole Domain layer, not just one feature area.

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

**Version-pin guard added after PR review:** `VersionAgnosticContractTypes` (the 15-plus-backbone-type
NORMATIVE set that drives the table above) is a claim about a *specific pair* of FHIR core package
versions (`hl7.fhir.r4.core#4.0.1`, `hl7.fhir.r5.core#5.0.0`), not about "R4/R5" as an abstract concept —
a later patch release could change one of these types' shape without changing its version name, and
nothing previously re-verified the claim against what the generator actually loads. `Program.cs`'s
`RunTypedModelMultiVersion` now asserts its `targets` package specs match
`CSharpTypedModelLanguage.VerifiedAgainstPackageSpecs` before generation proceeds, and fails loudly
(exit code 1, no output written) if they don't. This does not replace re-running the structural-signature
probe when package versions do change — it only guarantees that a version bump can't pass through
generation silently without someone being forced to either re-verify or explicitly update the pinned
spec list.

## Reference un-fallback status (implemented)

`Reference`-typed elements previously fell back to a raw `JsonNode`/`JsonArray` accessor (`Reference`
was hard-coded into the generator's `AbstractOrFallbackTypes` set alongside genuinely abstract bases
like `Resource`/`Element`, even though it's a normal concrete datatype with its own generated facade).
Fixed by removing that one entry: every `Reference`-typed element (22 in the current R4/R5 package,
including `Identifier.Assigner`, `Observation.Subject`, `Patient.GeneralPractitioner`) now gets a typed
`Reference?`/`MutableJsonList<Reference>` accessor, and every previously-dropped `Reference` choice
variant (19, including `Extension.value[x]`, `ElementDefinition`'s `default/fixed/pattern[x]`,
`Parameters.parameter.value[x]`) now gets a real `Value{X}Reference` property — which also fixed a
latent bug where switching a choice element's variant never cleared a stale `valueReference` key, since
a dropped variant was never added to the choice's key-clearing list.

This was a hard prerequisite for the Phase 1 `Identifier`/`Reference` hand-facade merge (blocked on
exactly this gap per this doc's evidence section) and is now resolved — Phase 1 is unblocked.

**Not in scope for this fix, still open:** `Resource`-typed elements (`Bundle.Entry.Resource`,
`Parameters.Parameter.Resource`, `OperationOutcome.Contained`, etc.) and `contentReference`-based
recursive elements (`Parameters.Parameter.Part`, `Bundle.Entry.Link`) still fall back — separate,
already-scoped generator work, tracked as its own plan.

## Resource-typed and contentReference accessor status (implemented)

The two remaining generator-fidelity gaps are closed. `Resource`-typed elements (`BundleEntry.Resource`,
`BundleEntryResponse.Outcome`, `Observation.Contained`, `OperationOutcome.Contained`, `Patient.Contained`,
`ParametersParameter.Resource`, `Bundle.Issues`) now resolve to the hand-written
`Ignixa.Serialization.SourceNodes.ResourceJsonNode` runtime base — there is no single generated facade for
"any resource," so this is a deliberate exception to routing through `Ignixa.Models`, not an oversight.
`contentReference`-based elements (`Bundle.Entry.Link`, `Observation.Component.ReferenceRange`,
`Parameters.Parameter.Part`) now resolve to the referenced element's own backbone type name, reusing the
existing backbone-naming rule against a different input rather than inventing a new one.

Required making `ResourceJsonNode`'s `(JsonObject, FhirVersion?)` constructor `public` — this also fixed a
pre-existing, never-exercised latent bug where `MutableJsonList<ResourceJsonNode>` (used today only by the
hand-written `StructureMapJsonNode.Contained`) threw on first access.

`JsonNode fallbacks` in the generator's coverage-downgrade summary is now **0** for the R4/R5 package —
every complex element in scope has a typed accessor. `Reference` choice variants (Plan A) and this task's
fixes together account for all 51 downgrades present when this consolidation effort started (22 Reference
fallbacks, 19 dropped Reference variants, 10 Resource/contentReference fallbacks — the remaining
`value-set enum -> string: 16` downgrades are unrelated: real value-set binding metadata gaps like
`all-languages`, not element-typing gaps, and out of scope for this effort).

## Phase 1 status (in progress): first real merges

`Narrative` and `Extension` are merged — the first two of the 41 hand-written `*JsonNode` facades this
investigation set out to consolidate. `Narrative` needed zero hand-written code (fully generator-covered).
`Extension` needed one small hand-written addition: `Extension.CreateWithRawValueUri(string url, string?
valueUri, FhirVersion? fhirVersion = null)`, an `internal` factory method for the call site
(`SecurityCapabilitySegment.cs`, core CapabilityStatement generation) that needs to set `value[x]` but can
neither know its target FHIR version at compile time (it's multi-tenant, stamping `context.FhirVersion` per
request) nor reference the R4/R5 packages at all (they are deliberately opt-in, not baked into the core
request path — see this doc's Constraints). Every other call site either doesn't touch `value[x]` at all,
or (like test-only infrastructure that has no opt-in-package restriction and chooses to reference R4/R5
directly anyway) constructs the version-specific subclass and uses its typed accessor.

**Revised after PR review (`internal` factory, not `public` instance mutator):** the first shipped version
was `public void SetValueUriRaw(string? value)`, an instance method a caller invoked on an already-constructed
`Extension`. Review flagged that this was reachable by every consumer of `Ignixa.Models`, not just the one
legitimate caller, and — because it bypasses choice-variant clearing by design — nothing stopped a future
caller from invoking it twice, or after a different `value[x]` variant was already set on the same instance,
silently producing spec-invalid FHIR JSON with two `value[x]` keys. The fix changes the shape, not just the
visibility: `CreateWithRawValueUri` always constructs a brand-new `Extension` and sets `valueUri` exactly
once as part of construction, so there is no pre-existing state a call could ever conflict with — the
double-set/dual-variant hazard is structurally unreachable, not just discouraged by a comment. `internal`
narrows this from every consumer of `Ignixa.Models` (the previous `public` design) to the assemblies listed
in `Ignixa.Serialization`'s `InternalsVisibleTo` (see `AssemblyInfo.cs`) — a deliberately curated friend
list of about a dozen assemblies across `Ignixa.Application`, `Ignixa.Api`, `Ignixa.FhirFakes`, the test
projects, and the generated `Ignixa.Models.R4`/`R5` themselves, not "the one assembly with the one real
caller" — `Ignixa.FhirFakes` is a second real caller (see below), and the rest are trusted-but-currently-unused.

**Generalized to `SetValueChoiceRaw`, in a later follow-up (also driven by PR review):**
`CreateWithRawValueUri`'s one-off logic was lifted into `Extension.SetValueChoiceRaw(string
valueElementName, string? value)`, an `internal` instance method that clears any *other* `value[x]` JSON
key before setting the requested one, then `CreateWithRawValueUri` was rewritten to call it. This is safe
to derive generically at the shared base — without R4/R5's enumerated per-version variant list — because
FHIR's `value[x]` wire convention names every choice-type key `"value"` + PascalCase(type name) in every
version, and `Extension` has no other property that begins with `"value"` (only `url`, `id`, `extension`
do not); "remove every existing property whose name starts with `value`, then set the new one" is exactly
equivalent to the generated per-version `SetValueVariant`'s enumerated clear. This upgraded
`CreateWithRawValueUri` from "safe only because it's called once at construction" to "safe because it
always clears," with no behavior change for its one caller. It also let `Ignixa.FhirFakes` — a stable,
published NuGet package (`IsPackable=true`) — drop a `ProjectReference` to `Ignixa.Models.R4` that had
leaked in: `PatientBuilder.WithExtension` previously had to construct `Ignixa.Models.R4.Extension` directly
to get a typed `ValueString` setter, purely because the base `Extension` had none; it now calls
`ext.SetValueChoiceRaw("valueString", value)` on the base type instead, with no R4/R5 reference anywhere
in `Ignixa.FhirFakes`.

**Also renamed in the same review pass:** `Extension`'s nested `extension`-list member was originally
emitted as `Extension2` (a member can't share its enclosing type's name, so the generator's name allocator
fell back to a bare numeral). Extensions are a first-class, heavily-used FHIR concept, not a one-off, so
`Extension2` was a poor consuming experience specifically here. The generator's collision fallback
(`CSharpTypedModelLanguage.MemberNameAllocator.Allocate`) now pluralizes a **list**-typed member on
collision instead of numbering it, so this member is `Extensions`. This is collision-triggered only: every
other (non-colliding) list property in the generated model set — `BundleEntry.Link`, `Patient.Identifier`,
and the rest — is untouched, still singular, still matching its FHIR wire name exactly, so this doesn't
introduce a codebase-wide pluralization convention. Scalar-string collisions (`Reference.Reference` ->
`Reference2`, `Expression.Expression` -> `Expression2`) are unaffected for the same reason a scalar
can't sensibly be pluralized — they keep the numeric fallback.

**Decision recorded:** `ValueString`/`ValueUri` are deliberately *not* re-added as a same-named
hand-written instance property on `Extension`'s shared base, even though the old hand-written
`ExtensionJsonNode` had them. They only exist on the R4/R5 subclasses today (the classifier excludes
`value[x]` from the base — its choice-type union genuinely differs by version, confirmed empirically
during Plan A2). A base-level hand-written version would need a `new` modifier to avoid a build error,
and `new` is compile-time-dispatched: any code holding a base-typed `Extension` reference — the common
case after a merge — would silently get a simpler, non-choice-clearing implementation instead of the
version-correct one the generated subclass already provides. This establishes the pattern for every
future merge in this effort: **when a hand-written member's semantics only make sense for a specific
version, express that as a version-specific accessor on the subclass — never as a same-named hand-written
member on the shared base that could silently shadow the correct generated behavior.**

**A second lesson, found the hard way during this task:** a hand-written member that *dispatches* by
version (rather than shadowing a specific version) cannot simply live on the shared base type either, if
it needs to name the R4/R5 types by identifier — `Ignixa.Serialization` (where the base partial lives) is
a dependency *of* `Ignixa.Models.R4`/`R5`, not the reverse, so referencing them by name from the base is a
circular project reference. And even resolving that wouldn't have been enough here: the actual caller
(`Ignixa.Application`) doesn't reference the R4/R5 packages at all, deliberately, per the "opt-in, not
baked into the core request path" constraint — a hand-written helper that only compiles by depending on
opt-in packages can't be added to a core call site regardless of which project it lives in. The general
resolution: when a core (non-opt-in) call site needs version-specific typed-model behavior it structurally
cannot obtain, add a narrowly-scoped, differently-named, low-level escape hatch on the shared base instead
of trying to give the base type version-dispatch knowledge it isn't allowed to have.

Remaining Phase 1 datatypes: `Identifier`, `Reference`, `Meta` (see the Phased plan section above).
`Identifier`/`Reference` were previously blocked on the generator's `Reference`-typed-element fallback gap
— resolved by Plan A. `Meta` needs its own plan: its deltas are semantic, not structural (hand
`LastUpdated` is `DateTimeOffset?` vs. generated `string?`; hand `Tags`/`Security` are spec-incorrect
`MutablePrimitiveList<string>` vs. generated spec-correct `Coding`-typed lists) — plus `ResourceJsonNode.Meta`
being the `Meta` property on every resource in the codebase makes this a full-suite-regression-review
change, not a contained one.

**Explicit next-step order (2026-07-12):** confirmed on disk that `Identifier`, `Reference`, and `Meta` are
all still hand-written (`Models/IdentifierJsonNode.cs`, `Models/ReferenceJsonNode.cs`,
`Models/MetaJsonNode.cs` all present) — no partial progress on any of the three yet, and no other branch is
mid-flight on them (`worktree-typed-models-facade-consolidation` is superseded, already squash-merged as
`5de82fc5`). Do `Identifier` and `Reference` first, in either order — both are unblocked, low-risk, and
`ResourceTypeRegistry`-free, matching the same shape as the already-shipped `Narrative`/`Extension` merges.
Do `Meta` last and behind its own PR, not bundled with the other two: it's the only Phase 1 item with a
semantic (not just structural) delta, and touching `ResourceJsonNode.Meta` means a full-suite regression
pass regardless of how small the `Meta` type itself looks.

**`Identifier` and `Reference` merged (2026-07-12).** Both followed the `Narrative` pattern more closely
than `Extension`'s: `Identifier` needed zero hand-written code (fully generator-covered, including the
`Type`/`Assigner` complex properties) — `Models/IdentifierJsonNode.cs` is deleted outright, no partial file
added. `Reference` needed exactly one surviving member: `FromResourceTypeAndId(string resourceType, string
id)`, moved into `Models/Reference.cs` as `public partial class Reference` unchanged in signature and
behavior, only rewritten internally to set the generator's collision-renamed scalar (`Reference2`, not
`Reference` — the property collides with the enclosing type name, see the Phase 1 `Extension2`/`Reference2`
naming note above) instead of the hand-written type's `Reference` property.

Before merging, the two API-shape changes this surfaces were checked against real call sites, since they
looked risky on paper:
- `Identifier.Use` changes from a raw `string?` (hand-written) to a typed `IdentifierUse?` enum (generated).
  Zero production call sites read/write `IdentifierJsonNode.Use` today (repo-wide grep) — no actual breakage.
- `Reference.Reference` becomes `Reference.Reference2` (the collision-fallback name, already true of the
  generated type before this merge — see above). A raw `\.Reference\b` grep returns thousands of hits, but
  nearly all are `SearchParamType.Reference`/choice-type `ValueType.Reference` enum members, unrelated to
  this datatype. Real property access on a `ReferenceJsonNode` instance was exactly 4 call sites, all in
  `Ignixa.Application.Features.Metadata.Models` (`ResourceComponentJsonNode.cs` ×3,
  `ReferenceOrCanonicalJsonNode.cs` ×1) — and those all read/write a *different*, unrelated hand-written type
  (`ReferenceOrCanonicalJsonNode`, its own `Reference` property), not `Ignixa.Serialization.Models.ReferenceJsonNode`.
  The only real usage of the merged type's scalar property was internal to `FromResourceTypeAndId` itself,
  which moved with it. `ReferenceJsonNode` as a *type name* had three consumers outside its own file —
  `CompositionJsonNode.cs` (8 property-type declarations, Phase 2 territory, mechanically renamed to
  `Reference`) and `IpsGeneratorService.cs` (3 calls to `FromResourceTypeAndId`, mechanically renamed) — both
  already had `using Ignixa.Models;` in scope, so the rename needed no new imports.

Verified: `dotnet build All.sln` (0 warnings/errors) and the full non-E2E `dotnet test All.sln` suite green,
plus new characterization tests (`test/Ignixa.Models.Tests/IdentifierFacadeTests.cs`,
`ReferenceFacadeTests.cs`) covering round-trip behavior and, for `Reference`, `FromResourceTypeAndId`
(true TDD red→green here, since that method didn't previously exist on the generated type — the round-trip
tests are regression coverage for already-correct generated behavior, same as `NarrativeFacadeTests`).

**`Meta` merged (2026-07-13).** Unlike `Narrative`/`Identifier`/`Reference` (fully generator-covered, zero or
one surviving member), `Meta` hit a real naming collision: the generated `Ignixa.Models.Meta` already has
its own `LastUpdated` (a raw `string?`, per the generator's spec-correct-primitive design), so the
hand-written `DateTimeOffset?` convenience couldn't be re-added under the same name — a property can't be
overloaded by type the way a method can. Resolved (per the repo owner's explicit call, since this is a
real design fork, not a mechanical one) by adding a distinctly-named `LastUpdatedOffset` (`DateTimeOffset?`)
on the merged partial `Meta`, wrapping the generated `LastUpdated` string with the same ISO-8601-UTC
parse/format logic the hand-written type used. This matches the existing repo pattern of DateTimeOffset
convenience wrappers over raw `instant`/`dateTime` strings elsewhere (`CompositionJsonNode.Date`,
`ProvenanceJsonNode.Recorded`, `BundleComponentResponseJsonNode.LastModified` — all still hand-written,
out of scope here) rather than pushing ISO-8601 parsing into the ~23 real call sites that relied on
`DateTimeOffset` semantics (assignment, `.HasValue`, `.Value - .Value` diffs across
`Ignixa.Application`/`Ignixa.DataLayer.SqlEntityFramework` and their tests).

The other three hand-written members needed no such treatment:
- `Tags`/`Security` (hand: spec-incorrect `MutablePrimitiveList<string>`) had **zero real call sites** —
  repo-wide grep for `.Meta.Tags`/`.Meta.Security` found only comments and unrelated same-named symbols
  (`System.Security.*`, `SearchParameterCapabilitySegment.Security`). These deleted cleanly, picking up
  the generated `Tag`/`Security` (`MutableJsonList<Coding>`, spec-correct) as a free correctness fix.
- `Profiles` (hand) → `Profile` (generated, matching the FHIR wire name) had exactly **one** real call
  site (`IpsGeneratorService.cs`), same element type (`MutablePrimitiveList<string>`) — a mechanical rename.
- `VersionId`/`Source`: unchanged name and type on both sides, no call-site impact.

`MetaJsonNode.cs` is deleted outright; the merge lives in `Models/Meta.cs` (`public partial class Meta`,
just the `LastUpdatedOffset` accessor). `ResourceJsonNode.Meta` (the property present on every resource in
the codebase) now returns `Ignixa.Models.Meta` instead of `Ignixa.Serialization.Models.MetaJsonNode`; its
`_cachedMeta` field and constructor call were updated to match. `SourceNodeExtensions.RemoveExtension`
(the one surviving `Meta`-targeting extension method, used for stripping the soft-delete marker extension)
was retargeted from `MetaJsonNode` to `Meta` with no logic change.

Verified: `dotnet build All.sln` (0 warnings/errors); the full non-E2E `dotnet test All.sln` suite green
(the only two failures, in `Ignixa.SqlOnFhir.Tests`, are the pre-existing `sql-on-fhir-tests` submodule not
being initialized in this worktree — confirmed via `git submodule status`, unrelated to this change); the
full `Ignixa.Api.E2ETests` suite green (600 passed, 0 failed, 20 skipped — same skip count as before this
change). New characterization tests: `test/Ignixa.Models.Tests/MetaFacadeTests.cs`, covering
`Profile`/`Tag` round-trips and `LastUpdatedOffset` (parse, UTC-normalizing set, null read, null clear —
true TDD red→green, since that accessor didn't exist before this task).

**Phase 1 complete.** All five datatypes (`Narrative`, `Extension`, `Identifier`, `Reference`, `Meta`) are
now merged into their generated `Ignixa.Models` counterparts. Next up per the Phased plan: Phase 2
(`Composition`, `ConceptMap`, `StructureMap`, `SearchParameter`, `Provenance`, `StructureDefinition`).

## Phase 2 status (in progress): Composition merged, generator prerequisite discovered

Unlike Phase 1, none of the six Phase 2 resources had a generated counterpart to merge into: all six
(`Composition`, `ConceptMap`, `StructureMap`, `SearchParameter`, `Provenance`, `StructureDefinition`) --
plus `CapabilityStatement`, excluded per the Phase 0b decision above -- were still listed in
`CSharpTypedModelLanguage.ReservedBaseTypeNames`, and none were in `Program.cs`'s `ResourceAllowList`.
Phase 0b only proved the `partial`-class pattern on `Bundle`/`Parameters`/`OperationOutcome` (Phase 4's
set); it never touched the Phase 2 set. Each Phase 2 resource therefore needs its own generator
prerequisite step first (un-reserve, allow-list, regenerate, verify no drift on already-generated
resources) before the strip-to-delta merge from Phase 1 applies. Confirmed via a before/after content-hash
snapshot of the generated dirs (see `build/check-typed-model-regen.ps1` for the same technique) that
un-reserving and allow-listing `Composition` alone added exactly the expected new files
(`Composition`/`CompositionAttester`/`CompositionEvent`/`CompositionRelatesTo`/`CompositionSection` and
their R4/R5 subclasses, plus `DocumentRelationshipType`, `ListMode`, `V3ConfidentialityClassification`
enums) with zero changes to `Patient`/`Observation`/`Bundle`/`Parameters`/`OperationOutcome` or shared
datatype output -- the large `git status` diff this produced (~240 files) was entirely a pre-existing
`core.autocrlf=true` normalization artifact (working-tree bytes vs. what a fresh checkout's smudge filter
would produce), not real content drift; `git diff`/`cmp` against `HEAD` confirmed byte-identical content
for every file outside the new-Composition-file list. Also required initializing the `codegen/fhir-codegen`
submodule (`git submodule update --init codegen/fhir-codegen`), which this worktree didn't have checked
out.

**`Composition` merged.** `CompositionJsonNode.cs` is deleted outright -- like `Identifier`, it needed zero
surviving hand-written code -- but the shape of the merge differs from every prior Phase 1 datatype: the
generator's classifier excludes `Composition.subject`/`identifier`/`relatesTo`/`status` from the shared
base entirely, not because of enum-literal drift (the Phase 1 pattern) but because R4 and R5 disagree on
*wire shape*: R4's `subject`/`identifier` are single objects, R5's are lists (0..*); R4's `relatesTo` is a
list of the `CompositionRelatesTo` backbone, R5's is a list of the unrelated `RelatedArtifact` type; R4's
`status` and R5's `status` are separate generated enums (their literal sets differ). There is no
version-agnostic raw accessor that can serve both shapes correctly the way `Extension.SetValueChoiceRaw`
does for `value[x]` (whose wire convention *is* uniform across versions) -- so per this doc's established
policy ("when a hand-written member's semantics only make sense for a specific version, express that as a
version-specific accessor on the subclass, never a same-named hand-written member on the shared base"),
these four fields are `Ignixa.Models.R4.Composition`/`R5.Composition`-only, with no equivalent on the
shared base.

The one real caller, `IpsGeneratorService.cs` (`Ignixa.Application.Features.Experimental.Ips.Generator`),
set all four of these fields directly on the (previously version-agnostic) hand-written
`CompositionJsonNode`, and -- per the "opt-in, not baked into the core request path" constraint this doc
established during the `Extension` merge -- `Ignixa.Application` does not reference the `Ignixa.Models.R4`/
`R5` packages, so it had no way to construct a version-specific `Composition` and no `FhirVersion` context
to decide which one it would need anyway. Resolved by checking what version IPS generation actually
targets: `docs/features/fhir-operations/investigations/ips-generator.md` documents the IPS IG as v2.0.0,
STU2, **permanently R4-based** upstream -- this is not a "the tenant might be R4 or R5" ambiguity, it is a
fixed fact about the IG this feature implements, independent of the server's configured `FhirVersion`.
`Ignixa.Application.csproj` now carries a `ProjectReference` to `Ignixa.Models.R4` -- a narrow, documented
exception to the opt-in-package rule, justified by and scoped to this one IG-pinned feature, not a general
loosening -- and `IpsGeneratorService.cs` constructs `Ignixa.Models.R4.Composition` (via a `using
Composition = Ignixa.Models.R4.Composition;` alias, chosen over a bare `using Ignixa.Models.R4;` because the
file already uses several version-agnostic base types -- `CodeableConcept`, `Coding`, `Reference`,
`CompositionSection` -- that a blanket namespace import would make ambiguous against their `Ignixa.Models`
counterparts). `Date` (a generated raw `string?`, unlike the hand-written type's `DateTimeOffset?`) is
formatted inline at its one call site (`context.GenerationTime.ToString("o")`) rather than adding a
`LastUpdatedOffset`-style convenience property -- unlike `Meta`'s ~23 call sites, this is used exactly once,
so YAGNI favors the inline conversion over a new named accessor. The IPS generator's own
`CodeableConceptJsonNode`/`CodingJsonNode` usages (ad hoc hand-written types embedded inside
`OperationOutcomeJsonNode.cs`, not part of this doc's original 41-file inventory -- a gap in that inventory
worth noting for Phase 4) were switched to the shared, generated `Ignixa.Models.CodeableConcept`/`Coding`
at the same time, since `Composition.Type`/`CompositionSection.Code`/`EmptyReason` are all `CodeableConcept`
on the generated base.

Verified: `dotnet build All.sln` (0 warnings/errors); `build/check-typed-model-regen.ps1` reports no drift
after a fresh regeneration; the dedicated `Ignixa.Application.Experimental.Tests`
(`IpsGeneratorHandlerTests.cs`, 43 tests) and the full non-E2E `dotnet test All.sln` suite green (the same
two pre-existing `sql-on-fhir-tests` submodule failures as the `Meta` merge, unrelated); the full
`Ignixa.Api.E2ETests` suite green (600 passed, 0 failed, 20 skipped, same as before). New characterization
tests: `test/Ignixa.Models.Tests/CompositionFacadeTests.cs`, covering the shared base (`Title`, `Date`,
`Type`, `Author`, `CompositionSection`) and the R4/R5 divergence on `Subject`/`Status`/`Identifier`
(single-object vs. list) directly.

Remaining Phase 2 resources -- `ConceptMap`, `StructureMap`, `SearchParameter`, `Provenance`,
`StructureDefinition` -- each still need their own generator-prerequisite step (this section's technique
generalizes) before their strip-to-delta merges, per the Phased plan's stated order.

**Follow-up resolved: `IpsGeneratorService` no longer references `Ignixa.Models.R4` at all.** Revisited
after the `StructureMap` merge established that FML's genuinely-divergent members (`Group.TypeMode`,
`Source.DefaultValue`, etc.) could all be reached via low-level raw-JSON escape hatches on the shared base
type, with **zero** new package dependency -- the same technique applies here and is strictly better than
the R4-specific `ProjectReference` this doc originally recommended. `IpsGeneratorService` only ever touches
two of Composition's version-divergent fields (`status`, `subject` -- confirmed by re-checking: it never
reads or writes `identifier`/`relatesTo` at all), and only ever writes them, never reads them back. Added
`Composition.SetStatusRaw(string)`/`SetSubjectRaw(Reference)` (internal instance methods on a new
`Models/Composition.cs` partial, matching `Extension.SetValueChoiceRaw`'s exact shape) instead of
constructing `Ignixa.Models.R4.Composition`. `Ignixa.Application.csproj`'s `ProjectReference` to
`Ignixa.Models.R4` is removed; `IpsGeneratorService.cs` now uses the plain `Ignixa.Models.Composition`
base type throughout, no aliasing needed.

**Standing principle going forward (applies to `SearchParameter`/`Provenance`/`StructureDefinition` too):**
avoid taking a dependency on `Ignixa.Models.R4`/`R5` from Application-layer (or any non-opt-in Core) code
at nearly all costs. FHIRPath was evaluated as an alternative for the *read* side of these raw-JSON escape
hatches (this codebase's FHIRPath evaluator does resolve choice-type elements polymorphically off schema
metadata -- confirmed generic, not hardcoded) but rejected: FHIRPath is 100% read-only, so every escape
hatch that also needs to *write* (all of the ones built so far do) would still need raw JSON manipulation
for that half regardless, splitting one conceptual operation across two access patterns and (for
`Ignixa.Serialization`, where these live) a new dependency edge to `Ignixa.FhirPath` that doesn't exist
today. Raw `MutableNode`/`SetProperty` access for both directions -- matching `Extension.SetValueChoiceRaw`,
the direct existing precedent for this exact scenario -- stays the house style: a version-specific
`Ignixa.Models.R4`/`R5` type is reached for only when a resource's real, permanent version is fixed by an
external constraint the feature itself is built against (an IG that only targets one version) rather than
by convenience, and even then, prefer a narrowly-scoped raw setter over the concrete subclass if the real
need turns out to be only a couple of fields (as it did here).

**`ConceptMap` merged.** All four hand-written files (`ConceptMapJsonNode.cs`,
`ConceptMapGroupJsonNode.cs`, `ConceptMapElementJsonNode.cs`, `ConceptMapTargetJsonNode.cs`) are deleted
outright -- zero surviving hand-written code, same as `Identifier`. Unlike `Composition`, this merge
carried no real design fork to resolve: a repo-wide grep found **zero real call sites** for any of the
four hand-written types anywhere outside their own file (no production code, no tests) -- so the
R4/R5 wire-shape divergence the generator's classifier found (`ConceptMap.identifier`,
`ConceptMapGroup.source`/`target`, `ConceptMapGroupElementTarget`'s `equivalence`-in-R4-vs-`relationship`-
in-R5 rename, `ConceptMapGroupUnmapped.mode`) had no caller to reconcile against. Notably, the hand-written
`ConceptMapTargetJsonNode.Relationship` hardcoded the wire key `"relationship"` unconditionally -- correct
for R5, silently wrong for R4 (whose wire key is `"equivalence"`) -- a latent bug that never mattered
because nothing called it. Generator prerequisite followed the same recipe as `Composition`: un-reserve,
allow-list, regenerate, confirm via content-hash diff that only the 38 new `ConceptMap*`-prefixed files
changed (2520 insertions, 0 deletions, 0 unrelated files touched).

Verified: `dotnet build All.sln` (0 warnings/errors); `build/check-typed-model-regen.ps1` reports no drift;
full non-E2E `dotnet test All.sln` green (same 2 pre-existing unrelated submodule failures); full
`Ignixa.Api.E2ETests` green (600/0/20, unchanged). New characterization tests:
`test/Ignixa.Models.Tests/ConceptMapFacadeTests.cs`, covering the shared base (`Url`, `Name`, `Status`,
`Group`/`Element`) and the R4-vs-R5 `equivalence`/`relationship` rename directly on both subclasses.

**`StructureMap` merged** -- by far the largest and most load-bearing Phase 2 resource (9 hand-written
files, ~1300 lines, real production callers in `Ignixa.FhirMappingLanguage`'s FML parser/builder and the
Experimental `$transform` operation). Dispatched a research fork first to map every real call site before
touching anything, since the hand-written type already did extensive hand-rolled R4-vs-R5 runtime dispatch
(`NotSupportedException`/`ArgumentNullException` guards) for several members -- unlike `Composition`, this
wasn't a case of discovering divergence for the first time, but of replacing an already-known-divergent,
already-hand-guarded design with the generated equivalent.

**Key difference from `Composition`'s IPS case: FML is genuinely, deliberately multi-version, not
pinned to one.** `StructureMapParser`/`StructureMapBuilder` take `FhirVersion` as a runtime constructor
parameter and stamp it on every node they build -- there's no single "this feature only ever targets R4"
escape hatch available. This ruled out the Composition playbook (give `Ignixa.Application` a narrow
R4-only package reference) and initially suggested `Ignixa.FhirMappingLanguage` would need `ProjectReference`s
to *both* `Ignixa.Models.R4` and `Ignixa.Models.R5` to construct version-specific subclasses for every
divergent member. That turned out to be unnecessary: every divergent member the codebase actually touches
(`Group.TypeMode`, `Source.DefaultValue`/choice-type, `Target.Context`, `Target`/`Dependent.Parameter`'s
`value[x]`, `Dependent.Variable`/`Parameter`) is reachable through raw JSON manipulation via the *inherited*
`BaseJsonNode.MutableNode` (`internal`, already covered by `Ignixa.FhirMappingLanguage`'s existing
`InternalsVisibleTo` grant) and the public non-generic `SetProperty(string, JsonNode?)` -- neither requires
naming the R4/R5 types at all. `StructureMapExtensions.cs` (already home to hand-rolled version-branching
wrappers before this merge) was extended with this same technique for every divergent member, so
`Ignixa.FhirMappingLanguage` carries **no new package reference** -- the existing wrapper wraps the
generated base type instead of the hand-written one, same shape as `Extension.SetValueChoiceRaw`.

Findings from the pre-work fork, and how each was handled:
- **`Target.ListMode` is a real `0..*` list in both versions**, not the list-vs-scalar bug it initially
  looked like -- the generated base (sourced directly from the real FHIR core package, the authoritative
  source) confirms `MutablePrimitiveList<string>`, matching the hand-written shape exactly. No fix needed;
  the fork's suspicion didn't survive cross-checking against the generator's output.
- **`Group.TypeMode` had a real, previously-unnoticed bug**: `StructureMapBuilder` unconditionally wrote
  `typeMode: "none"` for every version, but R5's `map-group-type-mode` value set dropped `"none"` entirely
  (confirmed: the generated per-version enums are genuinely different types, `R4.MapGroupTypeMode` has
  3 values, `R5.MapGroupTypeMode` has 2) -- so the old code silently wrote spec-invalid data for every R5
  StructureMap it built. Fixed as a side effect of typing this properly: `typeMode` is now written only
  for R4/R4B (required there); R5 omits it (optional there), matching the field's real semantics instead
  of forcing one hand-picked "safe for both" literal that wasn't actually safe for both.
- **Two copies of the `Transform` feature exist; one is dead code.** `Ignixa.Application/Features/Experimental/Transform`
  is registered and live; `Ignixa.Application.Operations/Features/Transform` (near-identical, differs only
  in namespace) is never wired into DI anywhere -- confirmed via `ApplicationServicesRegistration.cs`. Both
  needed mechanical type-rename fixes to keep compiling (the dead copy would otherwise break the build),
  but deleting genuinely-dead code discovered incidentally, outside this merge's stated scope, wasn't done
  here -- flagged as a follow-up cleanup, not resolved.
- **`Target.Parameter`/`Source.DefaultValue`'s `value[x]`/`defaultValue[x]` choice-type surfaces were
  reimplemented using the generated per-version typed accessors internally** (`DefaultValueString`,
  `ValueString`/`ValueInteger`/etc., the generated `ValueType`/`DefaultValueType` discriminators) rather
  than the hand-written type's manual property-key string manipulation -- but the *public* wrapper API
  (`GetValue()`/`GetValueAs<T>()`/`SetValue(suffix, value)`/`GetDefaultValueString()`/`SetDefaultValueString()`)
  is unchanged, so `StructureMapParser.cs`/`StructureMapBuilder.cs` needed only type renames, not logic
  rewrites, at their call sites.
- **`GetDependentVariables()` needed a real behavior fix during verification** (caught by the existing
  `GivenStructureMapWithoutFhirVersion_WhenParsingR5Format_ThenParsesCorrectly` test, not by inspection):
  the hand-written original detected the wire shape (`variable` vs. `parameter` array) by trying one
  accessor and catching `NotSupportedException` when `FhirVersion` wasn't set on the node. The initial
  rewrite branched purely on `FhirVersion` and silently defaulted to R4 behavior when it was null, breaking
  parsing of version-unset R5-shaped input. Fixed by detecting presence of the `parameter` key directly
  (checking the actual JSON) rather than trusting a possibly-absent version tag -- a strictly more robust
  design than either the old or the first-draft-new one.
- **`Ignixa.FhirMappingLanguage`'s own `Expression` AST type collides with the generated `Ignixa.Models.Expression`
  datatype** (used by e.g. `DataRequirement`). Resolved with an explicit `using Expression =
  Ignixa.FhirMappingLanguage.Expressions.Expression;` alias in both `StructureMapParser.cs` and
  `StructureMapBuilder.cs` (an explicit alias wins over either wildcard `using`), rather than qualifying
  every one of the file's dozens of existing bare `Expression` references.

All 9 hand-written files deleted outright (`StructureMapJsonNode.cs` and its 8 nested-type files); no
surviving partial file, matching `Identifier`/`ConceptMap`. The old dedicated hand-written-type test suite
(`test/Ignixa.Serialization.Tests/StructureMapVersionTests.cs`, ~470 lines) tested the hand-written design's
*own* runtime-guard mechanism specifically (its `NotSupportedException`/`ArgumentNullException` messages for
R4-vs-R5 access) -- since that mechanism no longer exists (replaced by the generated types' compile-time
separation), most of it was obsolete by construction rather than portable. Deleted and replaced with
`test/Ignixa.Models.Tests/StructureMapFacadeTests.cs`, covering the shared base round-trip, the R4/R5
`MapGroupTypeMode` enum divergence, and the extension-method wrapper behaviors that do still exist
(`GetDependentVariables`/`AddDependentVariable`/`GetDefaultValueString`/`SetDefaultValueString`/`GetContext`/
`SetContext`/`GetValue`/`GetValueAs`/`SetValue`/`SupportsConstants`/`GetConstantsOrEmpty`). The FML project's
own version-matrix tests (`StructureMapParserVersionTests.cs`, `StructureMapJsonEdgeCasesTests.cs`,
`RoundTripTests.cs`, `StructureMapBuilderVersionTests.cs`) needed only type renames plus, in
`StructureMapBuilderVersionTests.cs`, removing four tests that specifically exercised the now-gone
`VersionAlgorithmString`/`CopyrightLabel`/`Group.TypeMode`-null-guard hand-written behavior (none of those
three fields have any real caller, so -- consistent with the rest of this merge -- they weren't given a
version-agnostic wrapper to test).

Verified: `dotnet build All.sln` (0 warnings/errors); `build/check-typed-model-regen.ps1` reports no drift;
`Ignixa.FhirMappingLanguage.Tests` (535 passed, 0 failed, 1 skipped -- the 1 real regression this merge
introduced, `GetDependentVariables`'s version-detection bug above, was caught by this suite and fixed before
being called done); `Ignixa.Models.Tests` (94 passed), `Ignixa.Models.R4.Tests` (63 passed), the
Transform-related slice of `Ignixa.Application.Tests` (45 passed) all green; full non-E2E `dotnet test
All.sln` green (same 2 pre-existing unrelated submodule failures); full `Ignixa.Api.E2ETests` green
(600/0/20, unchanged).

**`SearchParameter` merged.** Un-reserved and allow-listed via the same recipe as the prior three;
content-hash diff confirmed only 12 new/changed files (`SearchParameter`, `SearchParameterComponent`,
`SearchParamType` on the shared base; R4's `SearchXpathUsage`; R5's `SearchParameterVersionAlgorithmType`/
`SearchProcessingmode`; the two `R{4,5}.cs`/`_GlobalUsings.cs` registration files), all additive except
those two registration files. Unlike `Composition`/`StructureMap`, this one had **no design fork at all**:
every real field the hand-written type exposed (`Name`, `Code`, `Description`, `Url`, `Type`, `Expression`,
`Base`, `Target`) landed on the shared base with full fidelity -- the only element the classifier flagged
incompatible (`language`) is a generic `DomainResource`-level field the hand-written type never touched.
The Phase 0b table's "hard: `component.definition` string↔object" finding didn't reproduce against the
real generator run (same "probe over-predicted, real pipeline under-diverges" pattern already seen with
`Composition`/`Parameters` in Phase 0b) -- `SearchParameterComponent.Definition` is a plain shared-base
`string?` in both versions.

Deleted `SearchParameterJsonNode.cs` outright (zero surviving members, same as `Identifier`/`ConceptMap`).
Three real call sites, all pure construction (never touching a field): `ResourceTypeRegistry.cs`'s factory
map, `ResourceConverter.cs`'s special-cased read path, and one test assertion -- all three retargeted from
`new SearchParameterJsonNode(jsonObject)` to `new SearchParameter(jsonObject)`, calling the generated
type's `protected internal` single-arg constructor (accessible since `ResourceTypeRegistry`/`ResourceConverter`
live in the same `Ignixa.Serialization` assembly). `ResourceTypeRegistry`'s factory leaves `FhirVersion`
unset either way (pre-existing behavior, unchanged by this merge) -- per this doc's earlier analysis, the
newly-added `[CompatibleFhirVersions(R4, R5)]` guard therefore doesn't fire at this construction point; it
only matters if something downstream explicitly tags the node's `FhirVersion` and then attempts an
`.As<T>()` cast, which no test in this repo currently exercises for `SearchParameter` against an
STU3/R4B/R6-tagged node.

Verified: `dotnet build All.sln` (0 warnings/errors); `build/check-typed-model-regen.ps1` reports no drift;
full non-E2E `dotnet test All.sln` green (same 2 pre-existing unrelated submodule failures -- a few
unrelated transient MSBuild/GitVersion/ICU-globalization crashes were hit and cleared on retry during this
verification pass, environment flakiness unrelated to this change); full `Ignixa.Api.E2ETests` green
(600/0/20, unchanged). New characterization tests: `test/Ignixa.Models.Tests/SearchParameterFacadeTests.cs`.

**`Provenance` merged.** Same generator prerequisite recipe; content-hash diff confirmed only 13
new/changed files. The classifier flagged only `Provenance.language` (generic, unused boilerplate) and
`ProvenanceEntity.role` (a nested backbone this codebase's hand-written type never modeled at all --
`Entity` wasn't hand-written) as incompatible. Every field the hand-written type actually used --
`Target` (`MutableJsonList<Reference>`), `Agent` (`MutableJsonList<ProvenanceAgent>`, with `Who`/
`OnBehalfOf` as `Reference?`) -- landed on the shared base using the **already-merged Phase 1 `Reference`
type**, which is strictly richer than the hand-written type's private nested `ReferenceComponent`
(`Reference`/`Display` only, missing `Type`/`Identifier`).

Re-confirmed the `ResourceTypeRegistry`/`.As<T>()` version-gating concern this doc has flagged since Phase
0b, this time against a real, live call site: `ProvenanceHeaderHelper.cs` calls
`resourceNode.As<Provenance>()` on every X-Provenance header a client submits. Traced the actual guard
(`ResourceJsonNode.As<T>()`, `SourceNodes/ResourceJsonNode.cs:240-250`): it only fires when the source
node's `FhirVersion` is set, non-`Unspecified`, and outside the target's `CompatibleFhirVersionsAttribute`
list. `JsonSourceNodeFactory.ParseAsync` (what parses the X-Provenance header) never sets `FhirVersion` on
the result, so this call site is unaffected by adding the attribute -- confirms the `SearchParameter`
finding generalizes, at least for parse-from-raw-JSON call sites that don't explicitly tag a version.

Almost all of the hand-written type's remaining surface (`Recorded` as `DateTimeOffset?`, `AddAgent`,
`SetAgents`, `SetTargets`, the `Target`/`Agent` getters going through a full JSON round-trip via
`JsonSerializer.Deserialize` rather than the zero-copy view the rest of the codebase uses) had **zero real
callers** -- dropped entirely, no wrapper. One real piece of business logic survived:
`AddTarget(resourceType, resourceId, versionId)`, which builds a versioned reference
(`{type}/{id}/_history/{version}`) -- the one real call site is `CreateOrUpdateResourceHandler.cs`'s
X-Provenance auto-fill path. Added as a **public** instance method on a new `Models/Provenance.cs` partial
(public, not `internal`, since -- unlike `Extension.SetValueChoiceRaw`'s narrow-friend-list escape hatch --
this is meant to be part of `Provenance`'s normal cross-assembly API surface, matching `Reference.FromResourceTypeAndId`'s
visibility). `HasTarget` (checked JSON-key presence) had one real caller (the same header helper, guarding
against a client-supplied `target`) and was replaced with `Target.Count > 0` (checks actual reference
count) -- arguably more correct against the FHIR spec's intent than the original key-presence check, since
an explicitly-empty `"target": []` array doesn't really specify a target either.

Verified: `dotnet build All.sln` (0 warnings/errors); `build/check-typed-model-regen.ps1` reports no drift;
full non-E2E `dotnet test All.sln` green (same 2 pre-existing unrelated submodule failures); full
`Ignixa.Api.E2ETests` green (600/0/20, unchanged -- exercises the X-Provenance header path this merge
touches most directly). New characterization tests: `test/Ignixa.Models.Tests/ProvenanceFacadeTests.cs`.

**`StructureDefinition` merged.** Same generator prerequisite recipe; content-hash diff confirmed exactly
14 new/changed files (`StructureDefinition`/`StructureDefinitionContext`/`StructureDefinitionDifferential`/
`StructureDefinitionMapping`/`StructureDefinitionSnapshot`/`StructureDefinitionKind`/`TypeDerivationRule`/
`ExtensionContextType` on the shared base plus R4/R5 subclasses and `StructureDefinitionVersionAlgorithmType`
on R5, plus the four `R{4,5}.cs`/`_GlobalUsings.cs` registration files). `ReservedBaseTypeNames`'s doc
comment now names `CapabilityStatement` as its sole remaining entry, with an added note that it's excluded
from consolidation entirely per the Phase 0b decision (its STU3-specific structural behavior can't be
represented in R4/R5-classified scaffolding), not merely deferred.

Unlike every prior Phase 2 resource, `StructureDefinitionJsonNode` wasn't a `ResourceJsonNode` subclass —
it was a **composition wrapper**: a sealed class holding a private `ResourceNode` (`ResourceJsonNode`)
field, with a private constructor and a static `Parse(string json, ILogger logger) -> T?` factory that
returns `null` and logs a warning on any parse failure or `resourceType` mismatch, rather than throwing.
This is a fundamentally different shape from the `partial class {Name} : {Base}` merge every other Phase 2
resource used, since there's no generated type to make `partial` that already *is* this wrapper — the
migration target is retargeting callers to the generated `ResourceJsonNode`-subclass `StructureDefinition`
directly and preserving the defensive parse-with-logging behavior at each call site instead of inside a
shared wrapper type.

Real usage was narrow: `.Url`, `.Name`, `.Type`, `.Kind`, `.Derivation`, `.ResourceNode`, and `.Parse()`
itself, across four call sites (`ProfileCapabilitySegment.cs`, `SectionMetadataParser.cs`,
`StructureDefinitionBasedStrategy.cs`, `SqlPackageResourceRepository.cs`). Confirmed by reading
`BaseJsonNodeConverter<T>.Read` directly that the generic deserializer does **not** validate `resourceType`
against the target type `T` — it just deserializes into a `JsonObject` and wraps it via
`Activator.CreateInstance`. This means the hand-written wrapper's explicit `resourceType` equality check
was genuine, load-bearing business logic (not defensive boilerplate the generated type already covers),
and had to be preserved at each call site doing a generic `JsonSourceNodeFactory.Parse<StructureDefinition>`,
not silently dropped.

`Kind`/`Derivation` moved from raw string comparisons (the hand-written type's design) to the generated
`StructureDefinitionKind`/`TypeDerivationRule` enums — a real fidelity improvement, matching the same
enum-over-string pattern already established for `Provenance`/`SearchParameter`'s dependents.
`SqlPackageResourceRepository.cs`'s package-classification loop was rewritten to compare
`StructureDefinitionKind.Resource`/`.Logical` and `TypeDerivationRule.Specialization` instead of the
equivalent string literals.

**Design question resolved by explicit user consultation** (not a unilateral call, per the Transformer
Mandate): whether the parse-with-logging-and-resourceType-check helper should be centralized in one shared
location or duplicated inline at each of the two remaining call sites that need it
(`ProfileCapabilitySegment.cs`, `SqlPackageResourceRepository.cs` — `SectionMetadataParser.cs` and
`StructureDefinitionBasedStrategy.cs` only ever receive an already-parsed `StructureDefinition`, they never
parse raw JSON themselves). Decision: **duplicate inline at both call sites**, not centralize behind a new
shared helper — two call sites is below the "third real case" threshold this repo's YAGNI/no-premature-
abstraction convention uses to justify an abstraction, and each site's surrounding try/catch and logging
context already differs slightly (different logger categories, different fallback behavior on `null`).

Verified: `dotnet build All.sln` (0 warnings/errors); `build/check-typed-model-regen.ps1` reports no drift;
`Ignixa.Models.Tests` (105 passed), `Ignixa.Application.Experimental.Tests` (43 passed) both green in
isolation; full non-E2E `dotnet test All.sln` green (the same 2 pre-existing unrelated `Ignixa.SqlOnFhir.Tests`
submodule failures, plus a transient `Ignixa.RepoGuards.Tests` GitVersion.MsBuild native crash hit and
cleared on an isolated re-run — 13/13 passed on both net9.0 and net10.0 — confirming it was environment
flakiness, not a regression from this change); full `Ignixa.Api.E2ETests` green (600 passed, 0 failed, 20
skipped, unchanged). New characterization tests: `test/Ignixa.Models.Tests/StructureDefinitionFacadeTests.cs`,
covering the shared-base round-trip and the `Kind`/`Derivation` enum patterns real code depends on
(`Resource`+`Specialization`, `Logical`).

**Phase 2 complete.** All six resources (`Composition`, `ConceptMap`, `StructureMap`, `SearchParameter`,
`Provenance`, `StructureDefinition`) are merged into their generated `Ignixa.Models` counterparts. Only
`CapabilityStatement` remains reserved — excluded from this effort entirely per the Phase 0b decision, not
a Phase 3 candidate, pending a real `Stu3.CapabilityStatement` type once ADR-2609 ships.

## Phase 4 status (in progress): Parameters merged, first of the three load-bearing resources

Per this doc's own re-scope note (2026-07-12), Phase 4's three resources are sequenced by blast radius:
`Parameters` (22 files, narrowest, contained to operation endpoints) first, then `Bundle` (29 files,
transaction pipeline), with `OperationOutcome` last (52 files including 13 in `Ignixa.Domain`'s exception
hierarchy — a mis-merge there is the highest-risk failure mode of the three).

**Generator prerequisite already done.** Unlike every Phase 2 resource, Phase 0b's `partial`-class
prerequisite work already un-reserved and allow-listed `Bundle`/`Parameters`/`OperationOutcome` and marked
all three `VersionAgnosticContractTypes` (no `CompatibleFhirVersionsAttribute` on the base type) -- so this
merge needed no generator/regeneration step, unlike Composition through StructureDefinition. This also
means the `.As<T>()` version-gating concern this doc tracked through Phase 2 (Provenance/SearchParameter
keeping the attribute since their divergence is real) doesn't apply here: `Parameters`'s base carries no
attribute, so nothing changes for STU3/R4B/R6-tagged nodes reinterpreted through it -- resolved back in
Phase 0b, not something this merge had to re-litigate.

**Different starting shape than any prior merge: composition wrapper split across two hand-written types,
not a single `ResourceJsonNode` subclass.** `ParametersJsonNode.cs` held two classes -- `ParametersJsonNode
: ResourceJsonNode` (the resource itself, with a `Parameter` list and `FindParameter`) and `ParameterJsonNode
: BaseJsonNode` (a single parameter entry, with `Name`, `Part`, `Resource`, `FindPart`, and the `value[x]`
accessor family: `GetValue`/`GetValueAs<T>`/`SetValue`). The generated counterparts already existed with
matching shape (`Ignixa.Models.Parameters`/`ParametersParameter`), so the merge mapped one-to-one: no
design fork like `Composition`/`StructureMap` needed, since `Parameters.Parameter`/`ParametersParameter.Name`/
`.Part`/`.Resource` are pure generator-duplicates (`.Resource` already returns `ResourceJsonNode?` on both
sides, zero behavior change) -- only the `Find*`/`value[x]` accessor family is genuine surviving logic.

**R4/R5 `value[x]` union divergence is real (`ParametersParameterValueType` differs: R5 adds
`Integer64`/`CodeableReference`/`RatioRange`/`Availability`/`ExtendedContactDetail`; R4 has `Contributor`,
dropped in R5) but no real caller needs the per-version typed discriminator.** Every real call site
(`FhirPatchParametersParser.cs`'s FHIRPath Patch parser, `MemberMatchHandler.cs`, the two MCP patch tools,
`ParametersExtensions.cs`) reads/writes `value[x]` exclusively through the generic name-string accessors
(`GetValue()`, `GetValueAs<T>(name)`, `SetValue(name, ...)`) -- the same shape `Extension.SetValueChoiceRaw`
already established as correct for version-uniform-wire-convention/version-divergent-union elements. These
survived as `Models/Parameters.cs`'s `FindParameter`/`FindPart`/`GetValue()`/`GetValue(string)`/
`GetValueAs<T>()`/`GetValueAs<T>(string)`/`SetValue(string, JsonNode)`/`SetValue<T>(string, T)`, moved
verbatim (same non-nullable-annotated signatures as the original -- `Ignixa.Serialization` suppresses
CS8600/8603/8604/8625 project-wide, so matching the original's lax annotations exactly, rather than
"fixing" them to `?`, avoided rippling new nullable-warning obligations into `Ignixa.Application`, which
does not suppress those).

**16 real call sites retargeted**, all mechanical `ParametersJsonNode`→`Parameters`/`ParameterJsonNode`→
`ParametersParameter` renames with no logic changes: `ResourceTypeRegistry.cs`'s factory map (same
non-issue as `SearchParameter`/`Provenance` -- `FhirVersion` is never set at this construction point, so
the new attribute-free base type doesn't change anything here either way); `ParametersExtensions.cs`
(`GetParameterStringValue`/`GetParameterStringValues`/`GetParameterResource<T>`/`GetParameterResources<T>`);
`FhirPatchParametersParser.cs`; `MemberMatchHandler.cs`; `PatchResourceFieldTool.cs`/`PatchResourceTool.cs`;
`OperationEndpoints.cs`/`ImportEndpoints.cs`/`TransformEndpoints.cs`/`SummaryEndpoints.cs`/
`DeIdOperationEndpoints.cs`; `CreateImportJobCommand.cs`/`CompleteJobInput.cs`/`ImportOrchestrationInput.cs`
(bare `StorageDetail` properties); plus five test files (`FhirPatchParametersParserTests.cs`,
`MutableNodeVisibilityTests.cs`, `JsonNodeConverterConstructorTests.cs`, `SmartResourceJsonNodeConverterTests.cs`,
`ResourceJsonNodeAsTests.cs`) -- none held Parameters-specific business logic beyond exercising the generic
`.As<T>()`/smart-parse/constructor-shape mechanics already covered elsewhere in this doc's Phase 0b/Phase 2
work. One assertion needed a real (not just mechanical) fix: `ResourceJsonNodeAsTests.cs`'s
`InvalidCastException`-message check asserted the literal substring `"ParametersJsonNode"`, which the new
message (`"Cannot convert resource of type 'Bundle' to Parameters, expected 'Parameters'"`, since
`targetType.Name` is now `Parameters`, not `ParametersJsonNode`) no longer contains -- updated to assert
`"to Parameters"` instead, preserving the same discriminating check (distinguishing the actual-type clause
from the expected-type clause already covered by a separate assertion).

`ParametersJsonNode.cs` deleted outright. New characterization tests:
`test/Ignixa.Models.Tests/ParametersFacadeTests.cs`, covering the shared-base round-trip and each surviving
accessor (`FindParameter`, `FindPart`, `GetValue`/`GetValueAs<T>`, `SetValue` with both a `JsonNode` and a
primitive value).

Verified: `dotnet build All.sln` (0 warnings/errors); `Ignixa.Models.Tests` (111 passed, 6 new),
`Ignixa.Serialization.Tests` (82 passed), `Ignixa.Application.Tests` (695 passed, 1 pre-existing unrelated
skip), `Ignixa.Api.Tests` (114 passed) all green in isolation; `build/check-typed-model-regen.ps1` reports
no drift (expected -- no generator change this merge, prerequisite already done in Phase 0b); full non-E2E
`dotnet test All.sln` green (the same 2 pre-existing unrelated `Ignixa.SqlOnFhir.Tests` submodule failures);
full `Ignixa.Api.E2ETests` green (600 passed, 0 failed, 20 skipped, unchanged -- exercises the
`$member-match`, `$patch`, `$import`/`$transform`/`$summary`, and `$de-identify` Parameters-consuming paths
this merge touches most directly).

Remaining Phase 4 resources -- `Bundle`, `OperationOutcome` -- each still need their own retargeting pass
per this section's technique (no generator step needed, same as this one), in that order per the
established risk sequencing.

## Bundle merged (Phase 4, second of three)

Same no-generator-step starting position as `Parameters` (Phase 0b already generated `Bundle`/`BundleEntry`/
`BundleEntryRequest`/`BundleEntryResponse`/`BundleEntrySearch`/`BundleLink` as `VersionAgnosticContractTypes`),
but this merge turned out far larger in real scope than `Parameters` -- both because `Bundle` is the
highest-traffic type in the whole request pipeline (transaction/batch processing, search, history,
IPS) and because two of its members hit real, code-changing hazards no type-name grep could surface,
since callers reach them through property navigation (`entry.Search.Mode`, `link.Relation`) rather than
ever naming the underlying type.

**`Bundle.Type` needed the same raw-string escape hatch as `Composition`'s divergent fields, for a
different reason than usual.** The wire shape (`"type"`, a plain string) is identical in both versions --
R5 only *adds* a 10th literal (`"subscription-notification"`) to R4's 9-literal `bundle-type` value set --
but the classifier still pushes `Type` to the R4/R5 subclasses, because R4's and R5's generated `BundleType`
enums are now genuinely different C# types (confirmed via direct diff: 9 identical literals plus R5's
extra one). Initially added `Type`/`BundleType` verbatim to the shared base (byte-identical to the old
hand-written enum for the 9 common literals) since this looked like a safe, common, cross-version-agnostic
subset worth keeping typed -- but this collided at compile time (`CS0108`) with the auto-generated
`Bundle.Type` property already present on `Ignixa.Models.R4.Bundle`/`R5.Bundle` (same name, different return
type). Reverted to `Bundle.GetTypeRaw()`/`SetTypeRaw(string)` -- a plain raw-string pair matching
`Composition.SetStatusRaw`'s shape -- since no real caller in this codebase ever produces or consumes the
R5-only 10th literal, this covers every real usage with zero version-correctness risk. Public (not
internal, unlike `Extension.SetValueChoiceRaw`'s narrow-friend-list escape hatch): `Type` is read/written
broadly across `BundleProcessor.cs`/`BundleResponseBuilder.cs`/`IpsGeneratorService.cs` and a dozen test
projects, not confined to one caller.

**`BundleLink.Relation` hit the identical structural problem, discovered by re-sweeping for member access
rather than type names.** `Relation`'s wire shape is a plain string in both versions too, but R5 tightens
its value-set *binding strength* against `iana-link-relations`, which was enough for the classifier to push
it to the R4/R5 subclasses. The initial call-site analysis (grepping for the type name `BundleLinkJsonNode`)
found only two direct callers and concluded "clean drop-in" -- but a later re-sweep for the *member access
pattern* itself (`\.Relation\b`, independent of what type the receiver is) found **19 more real call sites**
across 7 E2E test files, none of which ever named the type. This is the same blind spot that separately
caught `entry.Search.Mode` (see below) -- a lesson for any future merge in this effort: type-name grep finds
constructors and casts, but member-access grep is required to find every real reader of a property that's
about to change shape. Resolved with `BundleLink.GetRelationRaw()`/`SetRelationRaw(string)`, `internal` this
time (its real footprint -- `HistoryPaginationLinkBuilder.cs`, `StreamingBundleSerializer.cs`, and E2E tests
-- all sit inside the existing `InternalsVisibleTo` grant, unlike `Bundle.Type`'s broader footprint which
also reaches `Ignixa.Serialization.Tests`/`Ignixa.FhirFakes.Tests`/`Ignixa.Application.Experimental.Tests`,
none of which are on that list).

**`BundleEntrySearch.Mode` and `BundleEntryRequest.Method`: real fidelity upgrades (string -> typed enum),
found by the same member-access re-sweep.** Both are version-uniform (no R4/R5 split -- the classifier
placed both directly on the shared base) but change type: hand-written `Mode`/`Method` were raw `string`;
generated are `SearchEntryMode?`/`HttpVerb?`. A grep for the type names `BundleComponentSearchJsonNode`/
`BundleComponentRequestJsonNode` found zero and one real callers respectively -- but re-sweeping for
`.Search.Mode`/`.Search?.Mode` (correcting a first attempt that false-positived on "Models" containing
"Mode" as a substring) found **~30 more real comparisons against string literals** (`"match"`/`"include"`/
`"outcome"`) across nine E2E test files spanning includes, revincludes, sorting, and compartments. Fixed by
calling the existing `GetLiteral()` extension (`this Enum value -> string`, already used throughout the
generator's own `value?.GetLiteral()` setters) at each comparison site rather than rewriting them to compare
enum values directly -- preserves every test's original string-literal assertions with a one-token change.
`Method`'s one real caller (`SearchTestHarness.cs`, building a test-only transaction bundle) was fixed the
other way, since it's a write not a comparison: `Method = "PUT"` (no longer type-checks) became
`Method = HttpVerb.PUT` (the generated enum has literal-exact `PUT`/`POST`/etc. members).

**`BundleEntryResponse.LastModified`: same semantic-not-structural delta `Meta.LastUpdated` already hit.**
Hand-written was `DateTimeOffset?`; generated is a raw `string?`. Confirmed a real caller
(`BundleResponseBuilder.cs`'s `BuildEntryComponent`, setting it from a `DateTimeOffset?`-typed execution
result) needing the typed convenience -- added `LastModifiedOffset` (`DateTimeOffset?`) as a distinctly-named
wrapper, identical shape to `Meta.LastUpdatedOffset`, and retargeted that one call site.

**Two real naming collisions, both from the generated type's name matching something pre-existing --
resolved with `using Alias = Fully.Qualified.Name;`, never by touching the pre-existing type.**
1. `Ignixa.Models.BundleEntryResponse` (generated) vs. `Ignixa.Application.Features.Bundle.BundleEntryResponse`
   (a pre-existing, actively-used execution-result DTO, unrelated to the FHIR wire shape). Aliased as
   `FhirBundleEntryResponse` in `BundleProcessor.cs`/`BundleResponseBuilder.cs` -- the DTO keeps its bare name
   everywhere it's already used as a method parameter type, untouched.
2. `Ignixa.Models.Bundle` (generated) vs. the namespace `Ignixa.Application.Features.Bundle` itself. Any file
   under `Ignixa.Application.Features.*` referencing bare `Bundle` resolves to the *namespace* (`CS0118`),
   because C#'s enclosing-namespace lookup wins over a `using`-imported type of the same simple name --
   this affects every file under that namespace tree, not just ones physically inside
   `Ignixa.Application.Features.Bundle`. Aliased as `FhirBundle` in six files
   (`BundleProcessor.cs`, `BundleResponseBuilder.cs`, `IIpsGeneratorService.cs`, `IpsGeneratorQuery.cs`,
   `IpsGeneratorHandler.cs`, `IpsGeneratorService.cs`). `StreamingBundleSerializer.cs` hit the analogous
   collision for `BundleLink` against a sibling DTO in `Ignixa.Application.Features.Bundle.Serialization` --
   same fix, `FhirBundleLink` alias -- and separately needed its pre-existing (and still necessary)
   `using Ignixa.Serialization.Models;` restored after an over-eager unused-using cleanup, since that
   namespace still holds `CodeableConceptJsonNode`, an ad hoc hand-written type embedded inside
   `OperationOutcomeJsonNode.cs` (Phase 4's last, not-yet-merged resource) that this file's
   `WriteCodeableConcept` helper depends on.

`BundleJsonNode.cs` and its four nested-type files (`BundleComponentJsonNode`, `BundleComponentRequestJsonNode`,
`BundleComponentResponseJsonNode`, `BundleComponentSearchJsonNode`, `BundleLinkJsonNode`) deleted outright.
Surviving logic lives in three new partials: `Models/Bundle.cs` (`GetTypeRaw`/`SetTypeRaw`),
`Models/BundleLink.cs` (`GetRelationRaw`/`SetRelationRaw`), `Models/BundleEntryResponse.cs`
(`LastModifiedOffset`). `BundleComponentJsonNode`'s hand-rolled per-property caching (`_cachedRequest` etc.)
was dropped in favor of the generated type's plain `GetComplexProperty` accessor -- a pure performance
detail with no observable behavior difference, consistent with every prior merge's "strip to the delta"
rule.

Verified: `dotnet build All.sln` (0 warnings/errors); `Ignixa.Models.Tests` (119 passed, 8 new),
`Ignixa.Serialization.Tests` (82 passed), `Ignixa.Application.Tests` (695 passed, 1 pre-existing unrelated
skip), `Ignixa.Api.Tests` (114 passed), `Ignixa.Application.Experimental.Tests` (43 passed),
`Ignixa.FhirFakes.Tests` (1428 passed on both net9.0/net10.0) all green in isolation;
`build/check-typed-model-regen.ps1` reports no drift; full non-E2E `dotnet test All.sln` green (the same 2
pre-existing unrelated `Ignixa.SqlOnFhir.Tests` submodule failures); full `Ignixa.Api.E2ETests` green (600
passed, 0 failed, 20 skipped, unchanged -- exercises the include/revinclude/sort/history/compartment/IPS
paths this merge touches most heavily). New characterization tests:
`test/Ignixa.Models.Tests/BundleFacadeTests.cs`, covering the shared-base round-trip and each raw
escape-hatch/typed-fidelity accessor (`GetTypeRaw`/`SetTypeRaw` including the R5-only literal,
`GetRelationRaw`/`SetRelationRaw`, `BundleEntryRequest.Method`/`BundleEntrySearch.Mode` as typed enums,
`LastModifiedOffset`).

**Standing lesson for `OperationOutcome` (Phase 4's last resource):** type-name grep alone is not sufficient
for merges where callers navigate through the type rather than naming it. Before concluding any member is a
"clean drop-in," re-sweep for the member-access pattern itself (`\.PropertyName\b`) across the whole repo,
not just the declaring type's name -- and watch for regex substring false-positives when the property name
is a common English word fragment (`Mode` inside `Models` cost one wasted grep pass here).

## Verdict

**Recommended.** The single-type `partial`-class merge is strictly better than a parallel-type-plus-rename approach: it removes the registry/call-site atomicity risk entirely (there is only ever one type per resource, so nothing can be "half migrated" at the type-identity level), costs one line in the generator, and turns the remaining work into per-resource, independently reviewable PRs with a natural risk ordering (datatypes → contained resources → Application facades → load-bearing core resources). The two risks that don't go away — enum-literal parity and newly-enforced version gating — are exactly the things Phase 0's parity tests exist to catch before any hand-written code is deleted. Breaking the public type names is accepted; this is pre-release with no external consumers to shim for.
