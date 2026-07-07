# ADR 2607: FHIR R6 Ballot2 -> Ballot4 Upgrade

## Status

Accepted

> Implemented on `feature/fhir-r6-ballot4`. R6 remains a preview/limited-support version per FHIR's own versioning policy; this ADR documents the upgrade path and its residual gaps, not a claim of full R6 conformance.

## Context

R6 support in this repo was pinned to `hl7.fhir.r6.core#6.0.0-ballot2` (2024-08-13). Ballot3 shipped 2025-04-03 and ballot4 shipped 2025-12-18; both were skipped in favor of jumping straight to ballot4, the current ballot. R6 is still pre-normative — HL7 makes no compatibility guarantee between ballots — so this is not a routine dependency bump: the spec's shape changes materially between ballots, and the vendored codegen tooling this repo uses to generate schemas, value sets, search parameters, and compartment definitions from the FHIR package needs to be able to parse whatever the target ballot contains.

## Decision

Bump the R6 package pin from `6.0.0-ballot2` to `6.0.0-ballot4` in the three codegen entry points that reference it, and regenerate all derived artifacts (core schema, reference metadata, value set provider, search parameters, compartments, code systems) against the new package. This required three supporting changes, each forced by a problem discovered while trying to do the "simple" version bump:

### 1. Submodule remote repointed

The vendored `fhir-codegen` submodule's git remote moved from `github.com/microsoft/fhir-codegen` to `github.com/FHIR/fhir-codegen` (same project, 301-redirects — it moved under HL7's FHIR GitHub org). Updated `.gitmodules` accordingly.

### 2. Submodule advanced 432 commits (pinned commit could not parse ballot4 at all)

The previously pinned commit (`173e7fd3`, 2024-09-27) crashed with `InvalidCastException` on `DeviceAlert` — a genuinely new-in-R6 resource type, confirmed against the official `hl7.org/fhir/6.0.0-ballot4/` pages, not a bad-data artifact. Advancing the submodule to `origin/main` tip required a mechanical `using`-namespace rename in our own generator code (upstream renamed `Microsoft.Health.Fhir.CodeGen*` -> `Fhir.CodeGen.*`) and a `net8.0` -> `net9.0` TFM bump.

### 3. Submodule repointed at a personal fork carrying a tolerant-parsing patch

Even at tip, `fhir-codegen` has no native R6 model support: the entire downstream pipeline (`DefinitionCollection.AddResource`, every generator) pattern-matches on R5 *concrete* POCO types. An R6 POCO (the `Hl7.Fhir.R6` SDK package exists and is already referenced in a test project) is a different runtime type and would be silently dropped (`Loaded 0 resources`) — worse than crashing, because it fails quietly. Unlike STU3/R4/R4B, there is no R6->R5 converter to bridge this.

**Decision (Option C, user-approved):** rather than a large architectural rewrite to add native R6 model support, make R6 parsing *tolerant* — skip unrecognized ballot4-only elements/resources via a `DeserializationFailedException.PartialResult` recovery path (the brief's originally-assumed `IgnoreUnknownMembers` setting does not exist in this Firely SDK version) instead of crashing on them. This fix lives on branch `r6-tolerant-parsing` of a personal fork, `brendankowitz/fhir-codegen`; the submodule (`.gitmodules`, path `codegen/fhir-codegen`) now points at that fork/branch instead of upstream. An upstream PR is intended but **not yet opened** — deferred until this approach is proven out here.

**Residual limitation (explicit, accepted trade-off, not an oversight):** R6-only content that has no representation in the tolerant-parsing recovery path is silently absent from the generated schema. The one confirmed instance is `SearchParameter.aliasCode` (2 occurrences in the ballot4 package), which does not appear in `R6CoreSchemaProvider.g.cs`. The same class of gap could recur for other never-before-ballot4 elements not yet exercised by generation. This is consistent with — not a violation of — R6's "Preview / Limited support" status.

### 4. Own-code fixes for codegen crashes unrelated to the fork

Regenerating search parameters and compartments surfaced 4 `InvalidCastException`s in code we own (`codegen/Ignixa.Specification.Generators/`): `CSharpSearchParameterLanguage.cs` (`SearchParameter.Base`, `.Target`, `.Type`) and `CSharpCompartmentLanguage.cs` (`CompartmentDefinition.Code` x2, and its `Resource` backbone's `.Code`). Root cause: these Firely SDK convenience properties cast raw codes to enums (`VersionIndependentResourceTypesAll`, `SearchParamType`) baked into the pinned `Hl7.Fhir.R5` 5.13.1 SDK, which predates ballot4-new codes (`DeviceAlert`, `SearchParamType`'s new `resource` value) and throws on access. Fixed by reading the raw string via each property's `*Element`/`ObjectValue` instead of the typed accessor — found via a systematic reflection-based audit of every `Code<T>`-backed property these generators read (two rounds of reactive one-crash-at-a-time discovery proved too slow to be worth continuing).

This fix is applied **unconditionally**, not R6-gated: verified safe for R4/R4B/R5/STU3 by decompiling the SDK to confirm `ObjectValue` and the typed enum's `.ToString()` produce identical strings for every pre-existing code.

Two ballot4-only codes were **hand-added** to Ignixa's own shared, version-agnostic normative enums — `SearchParamType.Resource` and `CompartmentType.Group` — rather than regenerating those enum files from R6, because regenerating them risked silently changing the enum for R4/R4B/R5/STU3 consumers too. `CompartmentType.Group` specifically cannot come from a mechanical regeneration at all under any approach: ballot4's own `CompartmentDefinition-group.json` uses `code: "Group"`, but ballot4's own `CodeSystem-compartment-type.json` (meant to enumerate valid codes) omits it — a confirmed inconsistency in HL7's own ballot4 package content, not a tooling gap.

## Structural Changes Found

Regenerating against ballot4 surfaced the real scope of spec drift since ballot2 — confirmed genuine against the official `hl7.org/fhir/6.0.0-ballot4/*` pages (404s for removed resources) and independent web corroboration, not tooling artifacts:

- **~30 whole resources removed from FHIR core**, moved to HL7 "incubator IGs" as part of R6's core-simplification effort: `TestScript`, `TestReport`, `MedicationKnowledge`, `Citation`, `GraphDefinition`, `ChargeItem`, `ChargeItemDefinition`, `Permission`, `VerificationResult`, `Linkage`, `SupplyDelivery`, `SupplyRequest`, `ConditionDefinition`, `ClinicalImpression`, `EncounterHistory`, `EvidenceReport`, `DeviceUsage`, `DeviceDispense`, `FormularyItem`, `InventoryItem`, `InventoryReport`, `BiologicallyDerivedProductDispense`, `MolecularSequence`, `GenomicStudy`, several `Substance*` variants, `Transport`, `Contributor`, `ImmunizationRecommendation`, `ImmunizationEvaluation`, plus their backbone sub-elements. This alone shrank `R6CoreSchemaProvider.g.cs` from 255,261 to 209,780 lines and reduced the value set provider from 804 to 748 value sets (160 removed / 104 added, consistent with the same resource churn — e.g. Citation/GenomicStudy-related sets removed, `DeviceAlert` category/signalType and new Dosage-related sets added).
- **New content added:** `DeviceAlert` (new resource); a new structured `Dosage` model (`DosageDetails` / `DosageCondition` / `DosageSafety`) replacing the legacy flat `Dosage` type on `dosageInstruction` and equivalents, which also changed cardinality from array to a single object; several new `RelatesTo` backbone elements; `Claim.patient` renamed to `Claim.subject` as part of FHIR's ongoing R5->R6 "patient -> subject" harmonization.
- **Enum/valueset gaps requiring hand fixes:** `SearchParamType` gained a `resource` value; `CompartmentType` needed a hand-added `Group` member (see Decision, point 4 — ballot4's own CodeSystem is internally inconsistent with its own CompartmentDefinition here).
- **3 FhirFakes scenario builders had ballot2-era assumptions that no longer held** once the ballot4 schema was in place (production bugs, not test drift, in `src/Core/Ignixa.FhirFakes/`):
  - `ImmunizationState` typed `doseNumber`/`seriesDoses` as string, grouped with R5; ballot4 makes these `CodeableConcept`.
  - `MedicationOrderState` built the legacy `Dosage` array shape; ballot4's `dosageInstruction` is now a single `DosageDetails` object.
  - `ConditionClinicalStatus`/`ConditionEndState` defaulted to `"resolved"` with no version awareness; ballot4 pruned the `condition-clinical` valueset to just `active`/`inactive`/`unknown`.
  All three were fixed, scoped to R6 only, with the full FhirFakes suite (1425 tests) passing on both TFMs afterward.

## Consequences

**Positive:**

- R6 support tracks the current ballot (ballot4) instead of a ballot that is over a year stale, with all derived artifacts (schema, value sets, search parameters, compartments, code systems) regenerated consistently from it.
- The systematic reflection-based audit of `Code<T>`-backed enum casts in `codegen/Ignixa.Specification.Generators/` is a durable fix, not a patch: it protects every future ballot bump (and R4/R4B/R5/STU3 regens) from the same class of crash, not just this one.
- The FhirFakes fixes are real production bug fixes surfaced by more accurate schema, not test-only churn — R6 fake-data generation is now correct for Immunization dose fields, MedicationRequest dosage shape, and Condition clinical status under ballot4.

**Negative / accepted trade-offs:**

- The submodule now tracks a personal fork (`brendankowitz/fhir-codegen#r6-tolerant-parsing`), not upstream `FHIR/fhir-codegen`. This is a **temporary** state pending an upstream PR that has not yet been opened. Future maintainers must not treat this as a permanent fork relationship.
- **CI-availability risk from the fork dependency:** `.github/workflows/ci.yml` runs `actions/checkout` with `submodules: recursive` on nearly every job, so this fork/branch is cloned unconditionally on almost every CI run — not just jobs that touch codegen. If the fork becomes unavailable (account deleted, branch force-pushed or removed, repo made private, etc.), the submodule checkout fails and **every CI job breaks**, not just codegen-related work. This is an accepted risk for now, not a hidden one — flagged here explicitly rather than only being discoverable by reading `.gitmodules`.
- R6-only content that the tolerant-parsing recovery path cannot represent is **silently absent** from the generated schema, not flagged. Confirmed instance: `SearchParameter.aliasCode` (2 occurrences in ballot4). This is a known, accepted gap consistent with R6's preview status — **not a defect to chase in a future task** without a deliberate decision to invest in native R6 model support (which would be a materially larger undertaking: an R6->R5 converter or R6-native pipeline support, neither of which exists today).
- Future ballot bumps should use this ADR as a template, and — critically — should check the vendored codegen tool's currency against the target ballot's actual resource set *before* assuming a version-string bump alone is sufficient. This upgrade's Task 3/3b detour (432-commit-stale submodule, no native R6 support, no R6->R5 converter) is exactly the failure mode a future bump should check for early rather than discover reactively.

## References

- Submodule: `.gitmodules` (`codegen/fhir-codegen` -> `brendankowitz/fhir-codegen.git`, branch `r6-tolerant-parsing`)
- Own-code enum-cast fixes: `codegen/Ignixa.Specification.Generators/CSharpSearchParameterLanguage.cs`, `codegen/Ignixa.Specification.Generators/CSharpCompartmentLanguage.cs`
- Hand-added enum members: `SearchParamType.Resource`, `CompartmentType.Group` (shared, version-agnostic normative enums)
- Generated artifacts: `R6CoreSchemaProvider.g.cs` and sibling generated files under the R6 specification output
- FhirFakes fixes: `src/Core/Ignixa.FhirFakes/` (`ImmunizationState`, `MedicationOrderState`, `ConditionClinicalStatus`/`ConditionEndState`)
- FHIR spec: [R6 ballot4](https://hl7.org/fhir/6.0.0-ballot4/), [FHIR versioning policy](https://hl7.org/fhir/versions.html)
