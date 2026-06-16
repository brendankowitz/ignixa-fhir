# Investigation: Adversarial / Edge-Case Data Generation

**Feature**: fhir-faker
**Status**: In Progress
**Created**: 2026-06-16

## Approach

Add an opt-in **adversarial generation mode** that deliberately emits *valid-but-hostile*
FHIR resources designed to stress-test downstream pipelines, parsers, and validators —
rather than the realistic, well-behaved data the existing layers produce.

Surfaced as a mode/flag on the existing generators (CLI `--edge-cases`, library
`.WithEdgeCases()` on the builders), composable with the current city/scenario/population
options. Candidate edge-case families:

- **Unicode / i18n stress** — CJK, RTL (Arabic/Hebrew), combining marks, emoji, zero-width
  characters, and very long multi-script names. (Directly motivated by the OEM-codepage UTF-8
  bug fixed in #280 — that class of bug is exactly what this mode would have caught.)
- **Temporal boundaries** — leap-year birthdays (Feb 29), year-boundary dates, far-past and
  far-future dates, partial-precision dates (`yyyy`, `yyyy-MM`), timezone extremes.
  (Motivated by the "every patient born July 1 / Jan 1" bug, #281.)
- **String boundaries** — max-length values, empty-but-present strings, whitespace-only,
  control characters, values that look like injection payloads.
- **Cardinality / optionality** — resources with all optional fields omitted, and resources
  packed with every optional field + extensions present.
- **Reference / structural** — deep nesting, contained resources, circular-ish reference
  shapes, unusual-but-legal `forEach`-heavy structures.

The mode stays **deterministic** (seeded, like the rest of the library) so a failing case is
reproducible, and each generated resource can be tagged with the edge-case family it exercises.

## Tradeoffs

| Pros | Cons |
|------|------|
| Turns FhirFakes into a *fuzzing* tool, not just a happy-path generator | Risks generating data that violates the spec if "adversarial" drifts into "invalid" — needs a clear valid/invalid boundary |
| Catches real bugs (the UTF-8 and birthday bugs are proof these matter) | More generation surface to maintain and keep version-aware (STU3→R6) |
| Pairs naturally with the existing `--validate` path: generate hostile → validate → see what your pipeline does | Edge-case "interestingness" is open-ended; needs a curated, documented catalog, not infinite knobs |
| Strong presentation moment ("synthetic data that attacks your assumptions") | Some families (injection-looking strings) could be misread as a security feature; scope as robustness testing |

## Alignment

- [x] Follows architectural layering rules — rides existing builders/generators as a mode, no new layer needed
- [x] Developer Experience (works with minimal setup) — one flag / one builder call
- [ ] Specification compliance — **key open question**: target *valid-but-hostile* by default, with an explicit separate `--invalid` opt-in for negative testing
- [x] Consistent with existing patterns — seeded determinism, version-aware, builder-based

## Evidence

- **No edge-case mode exists today.** Generators (`SchemaBasedFhirResourceFaker`, `PatientBuilder`,
  scenarios, population) all aim at realistic data. Net-new capability.
- **Two recent bugs validate the premise.** The OEM-codepage UTF-8 bug (#280) and the fixed-birthday
  bug (#281) are exactly the failure modes a unicode/temporal edge-case mode would surface — found by
  hand while building the DevDays 2026 demos.
- **The validation path already exists.** `ignixa-fakes ... --validate` validates inline, and the
  `ignixa-validator` CLI supports `--package hl7.fhir.us.core@x` for profile-level checks — so
  "generate adversarial → validate" is a closed loop with no new validation work.
- **A profile system exists** (`Builders/Profiles/`: `IPatientProfile`, US Core / AU Base strategies,
  `INameGenerationStrategy`) — adversarial name/value strategies can plug in as additional strategies
  rather than forking the builders.
- **Determinism is in place** via the seeded Bogus `Randomizer`, so adversarial cases are reproducible.

## Verdict

*Pending evaluation.* Strong promise as a distinct, demoable capability with proven real-world value.
Primary decision to resolve before implementation: the **valid-but-hostile vs intentionally-invalid**
boundary (default to valid-but-hostile; gate invalid behind an explicit flag).

## Alternatives worth investigating

- **Mutation-based generation** — take a valid resource and apply seeded mutations (flip a field to a
  boundary value), rather than generating hostile data from scratch. Smaller surface, reuses existing
  generators.
- **Curated "known-tricky" corpus** — a static, hand-built set of pathological resources shipped as
  test fixtures, instead of a generation mode. Simpler, less flexible.
- **Property-based / fuzz harness** — expose a generator that feeds a property checker (e.g. "round-trip
  parse/serialize is stable", "every generated resource validates"), targeting CI rather than demos.
