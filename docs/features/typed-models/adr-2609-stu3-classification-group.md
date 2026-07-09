# ADR-2609: STU3 as an Isolated Classification Group

**Status**: Proposed
**Date**: 2026-07-09
**Feature**: typed-models
**Amends**: [ADR-2608](adr-2608-shared-base-models.md)'s classification model — introduces the concept of a
**classification group** (a subset of targeted versions classified together, producing its own shared
base). ADR-2608 itself is unchanged for R4/R5; this ADR does not supersede it, it composes with it.

## Context

STU3 (FHIR 3.0.2) support for legacy/backwards-compatibility applications was investigated as a
straightforward extension of ADR-2608: add STU3 to the same multi-version classification pass R4/R5
already share. A spike (`RunTypedModelMultiVersion`'s `targets` temporarily extended to `[R4, R5, STU3]`,
output redirected to scratch, not committed) measured the actual cost:

| | R4+R5 only (shipped) | R4+R5+STU3 (same pass) |
|---|---|---|
| Base-only/shared types | 31 | 15 |
| Per-version subclassed types | 40 | 58 |
| Cross-version incompatible elements | 30 | 59 |
| R4 subclass count | 28 | 45 |
| R5 subclass count | 36 | 52 |
| Dropped choice variants (Reference-typed) | 17 | 30 |
| JsonNode fallbacks (Reference-typed) | 26 | 37 |

Merging STU3 into the same pass costs R4/R5 **52% of their currently-shared base** (31→15 types). Most of
that is not STU3 complexity leaking in — it's collateral damage: `TypedModelClassifier.MergeType`
requires **all** targeted versions to agree for an element to stay in the shared base
(`presentEverywhere && distinctSignatures.Count == 1`), so a third, structurally divergent version
demotes elements R4 and R5 still agree with each other on, duplicating identical R4/R5 code into
separate per-version subclasses purely because STU3 joined the group.

STU3 diverging this heavily from R4/R5 is not surprising: even Microsoft's vendored `fhir-codegen`
tooling (`codegen/fhir-codegen/src/Microsoft.Health.Fhir.CodeGen/Language/Firely/CSharpFirely2.cs`)
special-cases STU3 structurally in multiple places for its own Firely-SDK generator — synthetic elements
"pulled from STU3", STU3-specific choice-type patching — evidence that raw STU3 `StructureDefinition`s
don't cleanly fit the same shape assumptions R4/R5 do, independent of anything in this codebase.

Two alternatives to a merged pass were considered and rejected:

- **Convert STU3→R4 on ingest, then use existing R4 facades.** Not buildable from what's vendored: the
  `Microsoft.Health.Fhir.CrossVersion` project's `Convert_30_50/` only covers *conformance* resources
  (CapabilityStatement, ValueSet, CodeSystem, StructureDefinition, SearchParameter, ConceptMap — used
  to normalize old spec packages for the package loader), not a single clinical resource. A real
  STU3→R4 clinical converter (handling `Patient.animal`, the `Medication*` restructure, code-system
  moves, etc.) is its own product-scale effort, inherently lossy, and out of scope here.
- **A narrower, hand-maintained STU3 package.** Inverts what the legacy persona actually needs: legacy
  EHR integrations touch unpredictable resources, so a narrow allow-list guarantees the one resource an
  integrator needs is the one missing. Hand-written code also forfeits the regen-drift guard entirely.

## Decision

Classify STU3 in its **own isolated classification group** — a second, independent
`TypedModelClassifier` + `ExportMultiVersion` pass alongside the existing `{R4, R5}` group, not merged
into it:

1. **`{R4, R5}` classification is untouched.** Same base layer, same output, same 31/40 split as today.
2. **`{STU3}` is classified alone**, with the **same full sweep** R4/R5 get — `GenerateAllDatatypes = true`
   and the full resource allow-list, not a narrower cut. Within a one-version group there is nothing to
   diverge from, so every type is trivially "Identical" and the coverage-degradation numbers above simply
   don't apply to a solo group; there is no collateral-damage mechanism to guard against.
3. **No shared base type with R4/R5.** `Ignixa.Models.Stu3.Patient` inherits directly from the SDK
   runtime base (`DomainResourceJsonNode`), the same position `Ignixa.Models.Patient` occupies today —
   it is a sibling of the R4/R5 shared base, not a descendant. Consumers reach it via
   `resource.As<Ignixa.Models.Stu3.Patient>()` or `resource.AsVersion(FhirVersion.Stu3)`; neither
   mechanism requires shared-base inheritance (`As<T>()` is a generic reflection-based reinterpret,
   `AsVersion`/`VersionedModelRegistry` dispatches by `FhirVersion` key via the same
   `[ModuleInitializer]` self-registration pattern `R4.Register()`/`R5.Register()` already use).
4. **The boundary is enforced, not just documented**, by the `As<T>()` version guard shipped ahead of
   this decision (`CompatibleFhirVersionsAttribute` + the check in `ResourceJsonNode.As<T>()`): an
   STU3-tagged node reinterpreted through the shared `Ignixa.Models.Patient` type (or an R4/R5-tagged
   node through `Stu3.Patient`) throws `InvalidCastException` rather than silently misreading the wrong
   shape. This guard is version-agnostic and already active for R4/R5 regardless of this ADR.
5. **Golden-file agreement tests against real STU3 wire-format JSON are a merge gate, not follow-up
   polish.** The package loader normalizes STU3 through 30-to-50 conformance conversion before the
   generator sees it, so the generator effectively classifies "STU3 through R5-shaped structures," not
   raw STU3. A test suite analogous to `test/Ignixa.Models.R4.Tests` — asserting the generated STU3
   facades agree with real STU3 server output, not just with the normalized `DefinitionCollection` — is
   required before this ships, to catch any place that normalization step drifts from actual STU3 wire
   format.
6. **Naming/positioning stays in metadata, not the namespace.** The package is
   `Ignixa.Models.Stu3` at `src/Core/Models/Ignixa.Models.Stu3`, following the exact conventions R4/R5
   already use — no "legacy"-branded namespace or type names (namespaces outlive positioning; code that
   migrates off STU3 later reads worse with a pejorative baked into every type name for its lifetime).
   Maintenance-mode status is signaled via the package description ("supported for backwards
   compatibility and migration; new development targets R4/R5"), a distinct or frozen stability tier
   from R4/R5's, and a support-policy line on the docs site.
7. **Migration is app-owned, not new infrastructure.** Because facades are views, `Stu3.Patient` and
   `R4.Patient` can coexist over two different nodes in one process. The platform's offer to this
   persona is native, byte-faithful STU3 read/write plus that coexistence — migration is an explicit
   app-level transform between two typed views, documented with a worked example. Automated conversion
   (`IVersionConverter`, already flagged as future work in
   [shared-base-restructure](shared-base-restructure.md)) is a separate investigation if real demand
   materializes, not something to build speculatively alongside this.

## Consequences

**Positive:**
- R4/R5 consumers see zero change — the 52%-of-base regression the spike measured never happens.
- STU3 consumers get full generated typed coverage (all resources, all datatypes) rather than a
  hand-maintained subset that's guaranteed to miss whatever resource a given legacy integration needs.
- The classifier's regen-drift guard, structural lock tests, and downgrade-summary observability all
  continue to apply to STU3 output exactly as they do for R4/R5 today — no separate tooling.
- The version-mismatch failure mode this whole investigation started from (accidentally reading STU3
  data through an R4/R5-shaped accessor, or vice versa) is a thrown exception, not a silent wrong read.

**Negative:**
- No shared base type between STU3 and R4/R5: code cannot be written once against a common type and
  passed either an STU3 or an R4/R5 facade. Given how much STU3 diverges from R4/R5 on currently-shared
  elements (per the spike), this sharing was mostly illusory anyway, but it is a real, visible API
  difference from the R4/R5 experience.
- Adds a second "classification group" concept to the generator that ADR-2608 didn't need — a small but
  real increase in generator complexity (two passes, two output-cleaning invocations, two sets of
  console summaries) instead of one.
- Requires new STU3 wire-format golden-file tests before shipping — real, non-trivial test-authoring
  work, not a config flag.
- If a second legacy version is ever added (R4B, R6) that shares meaningful structure with STU3 but not
  with R4/R5, the "one version per isolated group" framing would need revisiting — not a blocker today
  (STU3 is the only version in this position), but worth flagging as a boundary condition of this design.

## Implementation status

Not yet implemented. Prerequisite already shipped ahead of this decision: the `As<T>()` version guard
(`CompatibleFhirVersionsAttribute`, `src/Core/Ignixa.Serialization/CompatibleFhirVersionsAttribute.cs`;
enforcement in `ResourceJsonNode.As<T>()`; codegen emission in `CSharpTypedModelLanguage.RenderClass`) —
active for R4/R5 today, and the mechanism this ADR's isolation model relies on for enforcement rather
than documentation. Remaining work if accepted: generator support for classifying/emitting a second
group (`RunTypedModelMultiVersion` currently assumes one flat `targets` list feeding one classifier
pass); `Ignixa.Models.Stu3` package; STU3 wire-format golden-file test suite; docs-site positioning per
item 6 above.
