# Investigation: Absorb Parsing, Drop `fhir-codegen` Dependency

**Feature**: codegen
**Status**: In Progress
**Created**: 2026-07-05

## Approach

Replace the vendored `fhir-codegen` submodule's FHIR-package-parsing responsibility with a small, purpose-built loader we own, so our 9 `ILanguage` generator implementations (`codegen/Ignixa.Specification.Generators/CSharp*.cs`) no longer depend on `fhir-codegen`'s `PackageLoader`/`DefinitionCollection`, and — transitively — no longer depend on the vendored Firely SDK's typed, enum-backed FHIR model at all.

Concretely: write a loader that reads a `hl7.fhir.{version}.core` package's `StructureDefinition`, `SearchParameter`, `ValueSet`, `CodeSystem`, and `CompartmentDefinition` JSON files directly (via `System.Text.Json`, no Firely SDK POCOs in the path), extracting exactly the fields our generators actually consume (name, cardinality, type references, binding strength/valueset URL, search parameter type/base/target/expression, compartment resource/code lists, code system concepts) into a small internal model shaped like — but not the same type as — `fhir-codegen`'s `DefinitionCollection`. Each `CSharp*Language.cs` generator would then read from our own model instead of `fhir-codegen`'s. Once every generator is migrated and cross-checked against current output (see Alignment below), drop the `third-party/fhir-codegen` submodule entirely.

This does **not** propose reimplementing FHIR validation, StructureMap/conversion, or anything beyond what today's 9 generators actually read.

## Tradeoffs

| Pros | Cons |
|------|------|
| Eliminates the recurring "Firely SDK's baked-in enum predates the current ballot" crash class permanently — confirmed root cause of 4 separate `InvalidCastException`s during the R6 ballot4 upgrade (`docs/adr/adr-2607-fhir-r6-ballot4-upgrade.md`), and the reason `SearchParameter.aliasCode` is silently unrepresentable today | Real, non-trivial engineering effort: a working (if narrowly-scoped) FHIR package parser, migrated across all 9 generator languages, cross-validated against 5 FHIR versions |
| Drops the `third-party/fhir-codegen` submodule dependency entirely — no more submodule staleness (this upgrade found it 432 commits behind), no more personal-fork CI-availability risk (this upgrade's accepted, documented trade-off) | Loses `fhir-codegen`'s general-purpose robustness for anything outside this repo's actual usage (arbitrary IGs, non-core packages, StructureMap-based conversion) — acceptable per this repo's actual scope (only ever runs against `hl7.fhir.{version}.core`), but a real boundary to respect going forward |
| Strong, proven prior art already exists in this exact codebase to build on (see Evidence) — this is not starting from zero | `fhir-codegen` genuinely does more than our generators use (compartment inheritance resolution, cross-version StructureMap conversion, etc.) — a naive read of "what do we use today" risks under-scoping if generator requirements grow later |
| Removes the entire class of "Firely SDK enum vs. this ballot's codes" bugs, since our own model has no enum at all — every FHIR code becomes a plain string, matching the `*Element`/`ObjectValue` raw-string fix pattern this upgrade already proved out, just applied from the start instead of patched in reactively | Any future FHIR structural change our generators DO need to react to (e.g. a new element kind) becomes our own parsing bug to fix, not an upstream tool's — full ownership, full responsibility |
| No `extern alias`/multi-SDK-version juggling ever needed — we're not carrying any versioned Firely SDK dependency for parsing at all | Need genuine confidence the snapshot-generation gap (see Evidence) never bites — verified true today, but is a standing constraint on scope (core packages only), not a permanent guarantee |

## Alignment

- [x] Follows architectural layering rules — codegen is already a separate solution (`codegen/IgnixaCodegen.sln`) isolated from the main solution's Central Package Management specifically to avoid dependency conflicts (`codegen/README.md`); replacing its parsing internals doesn't change that boundary.
- [x] Developer Experience — a from-scratch, purpose-built parser with no enum-casting can only be *simpler* to reason about for contributors than debugging `InvalidCastException`s inside a vendored SDK's generated model, which is what this upgrade repeatedly required.
- [x] Specification compliance — no change to what's generated (same 9 output languages, same public artifact shapes), only how the input package is read.
- [x] Consistent with existing patterns — directly extends the `ISourceNavigator`/`JsonNodeSourceNode` pattern this repo already uses at runtime for exactly the same "avoid Firely SDK's typed model" reason (see Evidence). This is not a novel direction for this codebase; it's applying an already-adopted, already-proven pattern to a place that hasn't gotten it yet.

## Evidence

### Prior art already in this codebase for exactly this problem

This repo has already solved the "don't couple to Firely SDK's typed model" problem once, at the **runtime** layer:

- `src/Ignixa.SourceNodeSerialization/ElementModel/JsonNodeSourceNode.cs` implements `ISourceNavigator` as a schema-less, pure-`System.Text.Json.Nodes` FHIR resource navigator — no Firely SDK POCOs, no enum casts, no per-version model coupling. It already handles the FHIR-JSON-specific quirks a naive `JsonNode` walk would miss: shadow-property pairing (`birthDate`/`_birthDate`), choice-type suffix matching (`value[x]` → `valueString`/`valueCode`/etc.), and primitive content-vs-value distinction.
- `docs/features/architecture/investigations/jsonobject-based.md` (**Status: Complete**, fully implemented) documents this exact migration — moving resource manipulation off Firely SDK POCOs onto `JsonObject`-based traversal — for the runtime request-handling path.
- `docs/features/fhir-compatibility/investigations/isourcenode-consolidation.md` (**Status: Complete**) documents `ISourceNode` → `ISourceNavigator` renaming with an explicit parsing-vs-semantics separation, the same conceptual split this investigation's proposed loader needs (parse raw JSON → hand generators a small semantic model, no Firely types in between).
- `docs/features/architecture/investigations/core-shims.md` (**Status: Viable**, not yet implemented) proposes the general architectural direction — a zero-Firely-dependency `Ignixa.Abstractions.Core` with SDK interop pushed into an opt-in shim layer — of which this investigation would be a codegen-specific instance.

**Implication:** a build-time replacement parser should mirror or directly reuse `JsonNodeSourceNode`'s approach (and, where the exact same FHIR-JSON quirks apply, potentially the class itself or a shared helper) rather than write a JSON walker from scratch. This is proven-in-production code in this exact codebase, not an unproven approach.

### Actual parsing surface required (scoping the effort honestly)

`fhir-codegen`'s `DefinitionCollection` (`codegen/fhir-codegen/src/Fhir.CodeGen.Lib/Models/DefinitionCollection.cs`, ~3300 lines) parses `StructureDefinition`, `SearchParameter`, `ValueSet`, `CodeSystem`, and `CompartmentDefinition` from a package. The 9 generator languages under `codegen/Ignixa.Specification.Generators/` mostly do straightforward field extraction (element name/cardinality/type references, binding strength + valueset URL, search-parameter type/base/target/expression, compartment resource/code lists, code-system concepts) — not deep semantic processing.

**One genuine risk, confirmed and scoped:** `DefinitionCollection.TryGenerateMissingSnapshots()` invokes Firely SDK's `SnapshotGenerator` to compute a `StructureDefinition`'s flattened "snapshot" from its "differential" when a snapshot is absent — a real algorithm (type-hierarchy resolution, constraint merging), not simple field extraction. **Verified directly:** `hl7.fhir.r6.core`'s own `StructureDefinition-Patient.json` (and core-package StructureDefinitions generally) ships **both** `snapshot` and `differential` already populated. Since this codegen pipeline only ever runs against `hl7.fhir.{version}.core` — never third-party IG/profile packages, which more commonly omit snapshots — `TryGenerateMissingSnapshots()` is very likely a no-op for every case this repo's codegen actually exercises today.

**This must be an explicit, stated scope boundary of any implementation**, not an assumption baked in silently: a replacement parser is safe to skip snapshot generation entirely *as long as* this pipeline stays scoped to base FHIR core specs. If this codegen pipeline is ever extended to generate from third-party IGs/profiles (not part of any current plan), snapshot generation would need to be revisited.

Not yet audited (worth a closer pass before committing to an implementation plan, not blocking for this investigation): field-by-field depth of `ValueSet`/`CodeSystem`/`CompartmentDefinition` parsing beyond what's listed above.

## Verdict

*Pending evaluation.*

## Other approaches worth investigating (noted, not yet written up)

This is the first investigation for the `codegen` feature area; three cheaper or differently-scoped alternatives are worth their own investigations before a final ADR:

1. **Status quo, patch reactively.** Keep depending on `fhir-codegen`, open the pending upstream PR for the tolerant-parsing fix (`brendankowitz/fhir-codegen#r6-tolerant-parsing`), and keep fixing our own generator code's `Code<T>` accesses one crash at a time as future ballots exercise new enum-backed properties. Cheapest option; the recurring-toil and silent-content-gap risks (`SearchParameter.aliasCode`-style) remain.
2. **Build a real `R6→R5` converter inside `fhir-codegen`**, mirroring the existing `Converter_43_50` family, so R6 content flows through the same R5-canonical pipeline every other version uses. Investigated and rejected as a first response during the ballot4 upgrade (dozens of files, and down-conversion still can't represent genuinely-new R6 content) — but still a smaller commitment than this investigation's full parser replacement, if the goal is "make `fhir-codegen` itself R6-correct" rather than "stop depending on it."
3. **Make `fhir-codegen` genuinely R6-model-aware via `extern alias`**, threading real R6 POCOs through `DefinitionCollection` and every generator instead of R5-canonicalizing. The architecturally "correct" fix within `fhir-codegen`'s own design, and the largest of the three — full ballot-fidelity without ever dropping the tool, but the most invasive change to third-party code we'd need to maintain indefinitely.
