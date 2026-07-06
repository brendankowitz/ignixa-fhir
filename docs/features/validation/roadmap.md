# Validation Implementation Roadmap

**Status**: Active
**Branch**: `feat/validation-implementation`
**Created**: 2026-07-06

The umbrella plan for taking Ignixa validation from "Core Complete" to a conformance-measured,
production-solid FHIR validation library. It collects the existing investigations under
`investigations/` into a phased sequence and records the cross-project learnings that motivated it.

---

## Goal

A validation library whose correctness is **measured against the official HL7 test suite**, not
asserted. Every phase is gated by a movement in the conformance pass rate, and every remaining gap is
visible as a triage bucket rather than a guess.

## Guiding principle — measure first, then build

Adopted from the `rh-validator` (Rust) conformance model. We do **not** build features against
intuition; we build the yardstick first, read where we land, and let the triage buckets order the
work. Concretely:

- The full official suite is **observational** (`[Explicit]` / not a CI gate). It prints a pass rate.
- CI gates only a **curated, promoted subset** of deterministic tests, behind a blocking toggle
  (observe → enforce rollout).
- Progress is tracked with a **triage report bucketed by subsystem** (`snapshot`, `slicing`,
  `invariant`, `terminology`, `primitive`, `json`, …) — **not** an allowlist / xfail manifest.

## Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| Base branch | Fork from `main` | Clean base. Baseline will run **without** PR #286 (tree-context/`resolve()`), so the first number understates real capability — merge #286 in, then re-baseline. |
| Conformance oracle | **Java primary + Firely cross-check** | HL7 Java validator outcomes cover 908/913 cases (canonical reference impl); `firely-sdk-current` (169 cases) is the .NET-semantics sanity check. |
| Comparison granularity | Boolean valid/invalid first (`errorCount == 0`), graduate to exact error/warning counts | Matches the Rust harness ramp; avoids drowning in message-text mismatches on day one. |
| Test data source | Reuse the already-vendored `test/Ignixa.FhirPath.Tests/TestData/fhir-test-cases/` (pinned copy) | The full `FHIR/fhir-test-cases` repo — including `validator/manifest.json` (913 cases) — is already in the tree. No live download. |

---

## Phase 1 — Conformance harness + baseline

**Objective:** wire up the official validator test cases we already have and produce a first pass rate.

The `validator/` slice of the vendored `fhir-test-cases` repo (`validator/manifest.json`, 436 KB,
913 cases) is currently consumed by **nothing**. Phase 1 fixes that by mirroring the existing
`OfficialTestSuiteRunner.cs` pattern (which already drives the FHIRPath official suite).

Scope:

1. `ValidatorConformanceRunner` in `Ignixa.Validation.Tests` — loads `manifest.json`, filters
   runnable cases, runs `ValidateResourceHandler`/`ValidationSchema`, compares verdict to the
   reference outcome.
2. Manifest POCO + `JsonConverter` for the dual outcome shape (inline `{errorCount,…}` **or** path
   into `validator/outcomes/`).
3. Reference-oracle loader: Java outcome (primary), `firely-sdk-current` (cross-check).
4. Triage CSV/report, bucketed by subsystem, written to test output.
5. Curated CI subset (`[Trait("Category","Conformance")]`) + full-suite `[Explicit]` run.

**Exit criteria:** a committed baseline pass rate + a triage report that orders Phases 2–3.
See `investigations/conformance-harness.md`.

### Baseline — 2026-07-06 (first slice)

Runner: `test/Ignixa.Validation.Tests/Conformance/ValidatorConformanceRunner.cs`, validating at
`ValidationDepth.Full` against the Java reference. Slice: **187 R4 clean-base cases** (no IG
packages / supporting / explicit profiles), boolean valid/invalid. Base = `main` (no PR #286).

**Pass rate: 63.6% (119/187).** Skew: **54 over-strict** (we reject, ref accepts) vs **14
under-strict** (we accept, ref rejects). Over-strict dominates — good, it's mostly a handful of
shared root causes, ranked:

| Cause | ~Cases | Notes |
|---|---|---|
| `txt-1/txt-2` narrative invariants — `htmlChecks()` FHIRPath function unimplemented | ~14 | Engine **throws** on the missing function; `FhirPathInvariantCheck` surfaces the throw as a validation **error**. An engine limitation must not produce a spurious error — skip/warn instead. Highest-value single fix. |
| `pat-1` invariant mis-evaluation ("SHALL at least contain a contact's details…") | ~9 | Firing/failing where the reference passes. Real invariant-eval bug. |
| `bdl-*` Bundle-entry invariants | ~7 | Need `%resource`/`resolve()` context — **exactly PR #286 (tree-context)**. Validates the roadmap: merge #286 and re-baseline. |
| Over-strict terminology (unresolvable snomed/loinc codes) | ~8 | We error; the reference warns on codes it can't resolve. Align severity. |
| `ele-1` spurious empties + `vsd-1` valueset invariant | ~7 | |
| json5 `//` comment parsing | 2 | Reference honors `allow-comments`; we throw. |

Under-strict (missed errors) is mostly the known gaps: extension-definition validation, terminology,
a few invariants. Deferred slices: IG-package cases, `supporting`/`profile` cases, non-R4 versions.

### Progression (R4 clean-base, 187 cases)

| Step | Pass | Over-strict | Under-strict | Note |
|---|---|---|---|---|
| Baseline (`main`) | 63.6% | 54 | 14 | |
| + unevaluable-invariant → warning (`53f4c58`) | 62.6% | 48 | 22 | `htmlChecks()` etc. no longer spurious errors |
| + element-scoped invariant altitude (`23301c8`) | 61.5% | 18 | 54 | `pat-1`/`bdl-5`/`inv-1`/`vsd-1` fire at owning element |
| + PR #286 tree-context merged & seeded (`efb7850`) | 64.7% | 19 | 47 | `resolve()`/`%resource` catch broken local refs |
| + nested-resource fragment re-scoping (`1027232`) | 63.6% | 18 | 50 | `#payer` in `parameter.resource` resolves in its own scope |

**The real scoreboard is the split, not the headline.** Over-strict (valid resources we wrongly
reject — a validator's worst failure) fell **54 → 19 (−65%)**. Under-strict rose because removing
spurious errors *unmasked* pre-existing terminology/semantic gaps that were coincidentally rejecting
the right resources for the wrong reasons — those are honest Phase 2/3 work (terminology, snapshot,
extension-versioning), not regressions. Architecture rationale recorded in
[ADR 2607: Forward-Only Nodes with Descending Context Scopes](../../adr/adr-2607-forward-only-validation-context.md).

## Phase 2 — Differential → snapshot generation

**Objective:** build our own snapshot generator, informed by the Rust `ElementMerger`.

The gap: Ignixa has **no** differential→snapshot generation. `StructureDefinitionSchemaBuilder`
consumes already-flattened `IType.Children`; profile composition **concatenates** check lists
(`ValidationSchema.Compose`) rather than merging differentials. Any IG that ships differential-only
StructureDefinitions, and any runtime-authored profile, validates incorrectly and silently.

Rust reference (`rh-foundation/src/snapshot/generator.rs` + `merger.rs`): recursively resolves
`baseDefinition`, merges differential onto base via `ElementMerger::merge_elements`, uses a
pre-existing snapshot as-is when present, detects circular dependencies via a `visited` set.

**Decision (locked): build our own `ElementMerger`** — not Firely's `SnapshotGenerator`. No
`Hl7.Fhir.*` coupling in Core/package layers, full control, Rust-informed. Firely's generator is kept
in the **test layer only** as a differential oracle. The seam is isolated: `StructureDefinitionTypeAdapter`
is snapshot-only today, so we insert a generation step upstream of it and leave the schema builder,
checks, and resolver untouched.

Milestones: M1 base-merge constraint tightening (no slicing) → M2 slicing/extension element insertion
(feeds slicing) → M3 type expansion + edge cases. Measured by **enabling the deferred profile/package
conformance slice** — Phase 2's payoff shows up there, not on clean-base.

**Exit criteria:** differential-only profiles validate correctly; the profile/package conformance
slice runs. Detailed plan: [differential-snapshot-generation](investigations/differential-snapshot-generation.md).

## Phase 3 — Gap completion

**Objective:** close the remaining correctness gaps for a solid, full library. Ordered by the Phase-1
triage buckets; the list below is the candidate set.

| Gap | Current state | Notes |
|---|---|---|
| Slicing / discriminators | Not implemented (`SlicingMetadata` captured, unused) | In-progress investigation. Blocked on discriminator-`type` field in codegen + `conformsTo()` stub. |
| `conformsTo()` / `memberOf()` / `validateVS()` | `NotSupportedException` stubs | Unblocks `profile` slicing discriminators. Needs profile-validation infra + tree-context. |
| Terminology completeness | Membership-only; `$lookup`/`$expand`/`$translate`/`$subsumes` stubbed | **Decision locked:** (1) error-vs-warn severity — never error on an unverifiable binding; (2) expanded local valuesets; (3) remote TX server API as fallback. Local-first per Rust; membership LRU. Biggest lever on the current slice. Detailed plan: [terminology-completeness](investigations/terminology-completeness.md). |
| Primitive value validation | Loose `TypeCheck` on non-choice primitives | Empty strings + impossible dates pass. Strict `FhirPrimitiveValidator` wired only into `ChoiceElementCheck`. Cheap fix. |
| `$validate` mode handlers | DELETE/CREATE/UPDATE integrity checks are TODOs | Referential integrity, uniqueness, immutability. |
| Extensible/Preferred bindings | Warning-only | No real extensible enforcement beyond membership warnings. |

## Cross-cutting research (not phase-gated)

- **Engine architecture: check-composition vs FHIRPath-invariant-unification.** Current Ignixa uses
  ~20 composable `IValidationCheck` types. An alternative compiles a StructureDefinition into a bag of
  FHIRPath assertions (cardinality as `x.count() >= 1`, fixed as `x = 'v'`, discriminator predicates).
  Our two docs disagree on whether HAPI does this (`slicing-discriminators.md` says "HAPI compiles
  checks to FHIRPath"; `reference-implementations.md` says HAPI is Schematron/XML and recommends
  Firely's compiled-assertion-tree). **Reconcile against real source before betting architecture.**
  Suspicion: the "everything as FHIRPath" story is closer to research validators + Firely's assertion
  model than to mainstream HAPI `InstanceValidator` (imperative snapshot-walking). To be created:
  `investigations/engine-architecture.md`.
- **Performance.** `rh-validator` went 82 ms → 170 µs/resource almost entirely via LRU caching; the
  single biggest win (−99.3%) was a ValueSet-membership cache keyed `(valueset, system, code)`. Native
  code fast-paths for the 3 hottest invariants (`ele-1`, `ext-1`, `per-1`) skip the FHIRPath engine.
  Defer until we have a baseline to profile against.

---

## Investigation index

| Investigation | Status | Phase |
|---|---|---|
| [validation-architecture](investigations/validation-architecture.md) | Merged | Foundation |
| [architecture-overview](investigations/architecture-overview.md) | Complete | Foundation (ref) |
| [integration-summary](investigations/integration-summary.md) | Complete | Foundation (ref) |
| [parity-analysis](investigations/parity-analysis.md) | Complete | Foundation (ref) |
| [hapi-message-format](investigations/hapi-message-format.md) | Complete | Foundation (ref) |
| [reference-implementations](investigations/reference-implementations.md) | Complete | Cross-cutting (engine arch) |
| [codegen-requirements](investigations/codegen-requirements.md) | Complete | Phase 2/3 input |
| [two-tier-architecture](investigations/two-tier-architecture.md) | Viable | Foundation |
| [depth-refactor](investigations/depth-refactor.md) | In Progress | Foundation cleanup |
| [tree-context-scoping](investigations/tree-context-scoping.md) | Viable (PR #286) | Phase 3 enabler |
| [slicing-discriminators](investigations/slicing-discriminators.md) | In Progress | Phase 3 |
| [primitive-value-validation-gap](investigations/primitive-value-validation-gap.md) | Viable | Phase 3 |
| conformance-harness | Landed (`ValidatorConformanceRunner`) | Phase 1 |
| [differential-snapshot-generation](investigations/differential-snapshot-generation.md) | Planned (own `ElementMerger`) | Phase 2 |
| [terminology-completeness](investigations/terminology-completeness.md) | Planned (local + severity + remote) | Phase 3 |
| engine-architecture | To create | Cross-cutting |

## Reference: what we learned from `rh-validator` (Rust)

Source: `C:\Src\rh\crates\rh-validator` (+ `rh-foundation`, `rh-fhirpath`).

- **Conformance model** (Phase 1): downloads/caches `FHIR/fhir-test-cases`, runs ~570 R4 cases,
  boolean valid/invalid comparison, triage CSV bucketed by subsystem, curated CI subset behind a
  blocking toggle. No allowlist. *We copy this — but keep our pinned vendored copy, not their live
  `master` download (reproducibility).*
- **Snapshot generation** (Phase 2): `rh-foundation` merges differential onto recursively-resolved
  base; pre-existing snapshot used as-is; circular-dep detection.
- **FHIRPath invariants**: LRU-cached parse (errors cached too); native fast-paths for `ele-1`/
  `ext-1`/`per-1`.
- **Terminology**: local-first (resolve against local packages), remote only as a fallback for
  locally-undecidable required bindings.
- **Where Rust is *behind* Ignixa** (don't copy): R4-only (single `FhirVersion`), untyped
  `serde_json::Value`, shallow UCUM (no grammar engine), compose-`filter` ValueSets not locally
  decidable, no parallel batch. Ignixa's multi-version, typed-`IElement` architecture is more
  ambitious.

See [ADR-2510: Three-Tier Validation Architecture](../../adr/adr-2510-validation-architecture.md).
