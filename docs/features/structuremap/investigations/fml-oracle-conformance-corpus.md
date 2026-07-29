# Investigation: FML Oracle Conformance Corpus

**Feature**: structuremap
**Status**: In Progress
**Created**: 2026-07-28

## Approach

Stand up a **two-tier oracle harness** for `Ignixa.FhirMappingLanguage`, following the pattern already
proven by `OfficialTestSuiteRunner` (FHIRPath), `ValidatorConformanceRunner` (Validation), and
`ShippedSnapshotOracleTests` (snapshot generation): vendor an external corpus authored by someone else,
grade against *their* recorded expectations, and freeze an explicit exclusion list rather than chasing a
raw percentage.

### Tier 1 — behavioural oracle (HL7 `fhir-test-cases`)

`FHIR/fhir-test-cases` ships `r5/structure-mapping/` and `r4b/structure-mapping/`, each with a
`manifest.xml` in the exact shape an oracle runner needs:

```xml
<fml-tests>
  <test name="…/qr2patgender" source="qr.json" map="qr2pat-gender.map" output="qr2pat-gender-res.json" />
  …
</fml-tests>
```

Source instance + map + **expected output produced by the HL7 reference implementation**. That is a
genuine oracle: parse the map, execute it against `source`, compare the produced resource to `output`.

The corpus is already vendored infrastructure-wise — `test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj`
downloads and unzips `testcases.zip` (pinned `FhirTestCasesVersion` 1.7.46) via an MSBuild target. The
same target can be lifted into `Ignixa.FhirMappingLanguage.Tests`.

### Tier 2 — grammar/round-trip oracle (cross-version maps)

`brianpos/fhir-r6-maps` contains **355 `.fml` files** (`R4_R6/maps/StructureMaps` and `R5_R6/maps/StructureMaps`)
— the machine-generated R4→R6 and R5→R6 cross-version maps, destined for
[HL7/fhir-cross-version](https://github.com/HL7/fhir-cross-version). Its HL7-governed destination,
`HL7/fhir-cross-version`, already holds **1,201 `.fml`** (R2↔R3, R3↔R4, R4↔R5, R4B↔R5) and is the better
target — see the Corpus inventory below.

Critically: **neither repo contains source instances or expected outputs.** They are therefore *not*
behavioural oracles. They are large, real-world, adversarially-broad **grammar corpora**, gradeable on:

1. **Parse**: every file parses without error.
2. **Round-trip**: `parse → FmlSerializer → parse` yields an equal AST (extends the existing `RoundTripTests`
   from hand-written snippets to 355 real files).
3. **Structural resolution**: every `then <Group>(…)` and `extends <Group>` resolves within the file or its
   `imports` closure — a cheap static check that needs no expected output.

## Tradeoffs

| Pros | Cons |
|------|------|
| Tier 1 is a *true* oracle — expected outputs recorded by the reference implementation, not by us | Tier 1 is small: 10 R5 cases + 10 R4B cases, and 4 of the R5 cases target CDA (a non-FHIR logical model) |
| Corpus vendoring already solved — the `DownloadFhirTestCases` MSBuild target is copy-pasteable, pinned by version, offline after first restore | Adds a network-dependent first build to a second test project |
| Tier 2 gives 355 files of grammar coverage for zero authoring effort, and is exactly the corpus Ignixa would need for real R4↔R6 conversion | Tier 2 can only grade *parsing*, not *transform semantics* — no fixtures exist |
| Grading is deterministic and offline: no terminology server, no live endpoints | Tier 1 output comparison needs a canonical JSON/XML diff (element ordering, `id` churn, whitespace) — a real chunk of harness work |
| Consistent with ADR-2607's "declare supported scope, freeze exclusions" discipline | Risk of the exclusion list becoming a dumping ground if not policed |
| Failures are diagnostic: each is a named language feature, not a vague score | R4→R6 execution needs `R6CoreSchemaProvider` wiring; there are no `Ignixa.Models.R6` typed models yet (untyped `IElement` path only) |

## Alignment

- [x] Follows architectural layering rules — test-only; `Ignixa.FhirMappingLanguage.Tests` already references
      `Ignixa.FhirMappingLanguage`. Tier 1 execution additionally needs `Ignixa.Specification` (schema provider),
      mirroring `Ignixa.FhirPath.Tests`.
- [x] Developer Experience — `dotnet test` only; corpus auto-downloads once, then offline. No manual fixture curation.
- [x] Specification compliance — this *is* the compliance measurement. Grading against HL7's own recorded outputs.
- [x] Consistent with existing patterns — three precedents in-repo (FHIRPath official suite, validator conformance,
      shipped-snapshot oracle) plus ADR-2607 for how to declare the result honestly.

## Evidence

### Measured baseline: both corpora currently fail to parse, 100%

A throwaway probe was run against the real `MappingParser` (net9.0, this branch), feeding every `.fml`/`.map`
file through `parser.Parse()`:

| Corpus | Files | Parsed | Failed |
|---|---:|---:|---:|
| `FHIR/fhir-test-cases` `r5` + `r4b` `structure-mapping` (`.map`) | 27 | **0** | 27 |
| `brianpos/fhir-r6-maps` (`.fml`) | 355 | **0** | 355 |

This is not a "the harness is hard to build" result — it is a **language-coverage result**. The current
parser handles the hand-written dialect used in `test/Ignixa.FhirMappingLanguage.Tests` and
`src/Core/Ignixa.FhirMappingLanguage/README.md` (single-quoted strings, `map 'url' = 'name'` header) and
nothing that the wild actually ships.

### Root causes — four concrete parser defects, all reproduced

**1. Double-quoted string literals are not string literals** (21 of 27 official cases)

`MappingTokenizer.cs:69,73` matches `QuotedString.SqlStyle` (single-quote) as `StringLiteral`, and
`"…"` as `DelimitedIdentifier`. `MappingGrammar.Map` (line 512) requires `StringLiteral`:

```
cast.map:1  map "http://ahdis.ch/matchbox/fml/cast" = "cast"
→ Failed to parse … at line 1, column 5: unexpected delimitedidentifier `"h…
```

The spec grammar treats `"…"` and `'…'` interchangeably for FML string values. Ignixa accepts only one.
This single defect accounts for ~78% of official-suite parse failures.

**2. Group type-mode annotation `<<type+>>` / `<<types>>` unsupported**

`MappingGrammar.Group` (lines 488–507) goes straight from `extends` to `{`. `LeftAngle`/`RightAngle` tokens
exist in `MappingTokenKind` but are never consumed by the grammar, and there is no `Plus` token at all:

```
ActivityDefinition.map:8   group ActivityDefinition(source src : …, target tgt : …) extends DomainResource <<type+>> {
syntaxshort.map:10         group string(source src : string, target tgt : string) <<type+>> {
Patient_4to6.fml:12        group Patient(source src : PatientR4, target tgt : PatientR6) extends DomainResource <<type+>> {
→ Syntax error (line 12, column 91): unexpected `+`.
```

`<<type+>>` is how the entire cross-version map set declares type-conversion groups. It is unavoidable for
the 355-file corpus.

**3. `%constant` / variable references unsupported**

No `Percent` token kind exists:

```
qr2patfordates.map:9   ext.value as value -> tgt.birthDate = (%value + 5 days) "plus";
→ Syntax error (line 9, column 43): unexpected `%`.
```

**4. R6 `///` metadata header, and `map` header treated as mandatory**

`MappingGrammar.Map` (line 511) requires a leading `map` token. The R6 form replaces it with metadata
declarations, which the tokenizer currently swallows as `LineComment`, leaving no header at all:

```
syntax.map:1     /// url = "http://github.com/FHIR/fhir-test-cases/r5/fml/syntax"
Patient_4to6.fml /// url = 'http://hl7.org/fhir/uv/xver/StructureMap/Patient4to6'
→ Failed to parse … at line 6, column 1: unexpected uses `uses`, expected map
```

### Secondary gaps observed in the corpora (not yet parse-blocking, will be)

- Arithmetic/concatenation inside target FHIRPath: `ext.system = ('urn:uuid:' + r.lower())` (`syntax.map:17`)
  — the FML tokenizer has no `+`, so embedded FHIRPath containing operators dies at the FML layer before
  `Ignixa.FhirPath` ever sees it.
- Wildcard imports: `imports "http://hl7.org/fhir/uv/xver/StructureMap/*4to6"` — used by every cross-version
  map; `ImportResolver` needs glob semantics.
- Date arithmetic with units: `%value + 5 days`.

### Scope notes for Tier 1

Of the 10 R5 manifest cases, 4 produce CDA XML (`qr2cda`, `qr2cdaxsi`, `qr2cd-eval-json`, `qr2cd-eval-fml`)
— transformation into a non-FHIR logical model with `xsi:type` handling. Following ADR-2607's precedent,
these belong on a **frozen exclusion list with rationale** rather than being counted as failures. That
leaves **6 in-scope R5 cases** (all `qr.json` → Patient JSON) plus the R4B set as the initial scored slice.

`qr2cda-eval.json` is notable: the same test is expressed as both `.map` (FML text) and `.json`
(StructureMap resource), which incidentally exercises `StructureMapParser` and `StructureMapBuilder` against
the same expected output.

### Prior art in-repo

| Precedent | Mechanism to reuse |
|---|---|
| `test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj` | `DownloadFhirTestCases` MSBuild target — pinned version, race-safe across TFMs, `.downloaded` marker |
| `test/Ignixa.Validation.Tests/Conformance/ConformanceCaseLoader.cs` + `ValidatorConformanceRunner.cs` | manifest-driven case loading, categorised outcome reporting |
| `test/Ignixa.PackageManagement.Tests/Snapshot/ShippedSnapshotOracleTests.cs` | facet-based comparison instead of raw equality — the right shape for output diffing |
| `docs/adr/adr-2607-validation-oracle-conformance.md` | how to declare the result: supported-scope pass rate + frozen exclusion list |

## Corpus inventory

An exhaustive survey of available FML/StructureMap corpora, classified by **what they can actually
grade**. The distinction that matters is whether a corpus ships *expected outputs produced by a reference
implementation* — without those, a corpus grades grammar, not semantics.

### Tier A — true transform oracles (map + source + reference-produced expected output)

| Corpus | Content | Manifest | Notes |
|---|---|---|---|
| **`FHIR/fhir-test-cases` `r5/structure-mapping/`** | 10 cases, 16 `.map` + fixtures | `manifest.xml` `<fml-tests>` | **The** official oracle. Drives the Java reference validator's own FML suite. 6 FHIR-output cases, 4 CDA-output. |
| **`FHIR/fhir-test-cases` `r4b/structure-mapping/`** | 10 cases, 12 `.map` + fixtures | `manifest.xml` `<fml-tests>` | Same shape, R4B slice. |
| **`ahdis/fhir-mapping-tutorial` `maptutorial/step1..13`** | 25 `.map`, 18 source instances, **36 expected results**, 28 logical `StructureDefinition`s | convention: `map/{m}.map` + `source/{s}.json` → `result/{m}.{s}.json` | Fixtures for the **official spec tutorial** (`hl7.org/fhir/R5/mapping-tutorial.html`, steps 1–13). Filename convention encodes the map × source cross-product, so a runner is trivial. |
| **`ahdis/fhir-mapping-tutorial` `careconnect-to-ukcore/`** | 8 maps, **75 input / 75 expected** pairs | convention: `maps/`, `input/`, `expected/` | Real-world STU3 CareConnect → UK Core extension conversion. Highest-volume behavioural oracle available. |
| **`ahdis/fhir-mapping-tutorial`** misc (`qrtopat`, `qrextract`, `unioncollection`, `rum-example`, `condition`, `csv`, `xml`, `tests/`) | 13 further `.map` | mixed | `tests/` includes `cast.map`, `memberof.map`, `quantity.map`, `stringtocoding*.map` — targeted transform-function cases. |

Caveat on `ahdis/*`: not an HL7-governed repo (it is Matchbox's authors), but the `maptutorial` fixtures
correspond 1:1 to the published spec tutorial and all expected outputs were produced by the reference
engine. Treat as authoritative-by-derivation, not authoritative-by-governance.

### Tier B — FML validation oracle (already wired in this repo)

| Corpus | Content | Manifest |
|---|---|---|
| **`FHIR/fhir-test-cases` `validator/map-general-test{,2}.fml`** | 2 FML files | `validator/manifest.json` → `validator/outcomes/java/R5.map-general-test{,2}-base.json` |

These grade **FML validation diagnostics** (does Ignixa emit the same `OperationOutcome` as the Java
validator for a malformed/edge-case map), not transform output. Critically, `ValidatorConformanceRunner`
in `test/Ignixa.Validation.Tests/Conformance/` **already consumes this exact manifest** — these 2 cases
are near-free once the parser can read the files.

### Tier C — official FML grammar corpora (no expected outputs)

| Corpus | Content | Grades |
|---|---|---|
| **`HL7/fhir-cross-version`** | **1,201 `.fml`** across `R2toR3`, `R3toR2`, `R3toR4`, `R4toR3`, `R4toR5`, `R5toR4`, `R4BtoR5`, `R5toR4B`; plus ~2,230 supporting `ConceptMap`s (2,200 code maps, 13 type maps, 10 resource maps, 6 search-param maps) | parse, round-trip, group/import resolution, ConceptMap `translate()` wiring |
| **`brianpos/fhir-r6-maps`** | 355 `.fml` (R4→R6, R5→R6) | same; staging area destined for `HL7/fhir-cross-version` |

`HL7/fhir-cross-version` is the **HL7-governed** home and is 3.4× larger than the brianpos repo — it is the
better Tier C target, and its bundled ConceptMaps additionally exercise `ConceptMapResolver` /
`TranslateTransform`, which no other corpus does at scale.

### Tier D — serialization oracles (form-equivalence, no execution needed)

`ahdis/fhir-mapping-tutorial` ships each tutorial map in **three forms**: `.map` (FML text), `.json`
(StructureMap resource), `.xml` (StructureMap resource) — 25 / 24 / 25 files respectively, all
reference-produced. That is a direct oracle for `StructureMapBuilder`, `StructureMapParser`, and
`FmlSerializer`:

- `parse(.map)` → build StructureMap → must equal shipped `.json`
- shipped `.json` → `FmlSerializer` → must re-parse to the same AST

`fhir-test-cases` offers one instance of the same idea (`qr2cda-eval.map` and `qr2cda-eval.json` are the
same test), plus `r{4,4b,5}/examples/structuremap-example.json` and `r5/narrative/sm.fml`.

### What does not exist

- No official corpus pairs the **cross-version maps** (Tier C) with source/expected instances. Any R4↔R5 or
  R4↔R6 execution testing must be invariant-graded or self-generated.
- No `<fml-tests>`-style manifest outside `FHIR/fhir-test-cases` (verified by code search) — Tier A
  entries 3–5 need convention-based discovery, not manifest parsing.
- No FML corpus in `HL7/fhir` spec source itself; the tutorial artefacts live only as HTML prose on
  `mapping-tutorial.html`, which is why the `ahdis` fixture repo matters.

### Revised corpus scale

| Grading capability | Cases available |
|---|---:|
| Transform behaviour (true oracle) | **~130** (16 manifest + 36 tutorial + 75 careconnect + misc) |
| FML validation diagnostics | 2 |
| Serialization round-trip vs reference artefacts | ~25 map triples |
| Grammar / parse / structural resolution | **~1,560 `.fml`** (1,201 HL7 + 355 brianpos) |

This is roughly an order of magnitude more behavioural coverage than the first-pass estimate of "6 in-scope
R5 cases", and it changes the Phase 2 value case substantially.

## Verdict

*Pending evaluation.* Preliminary read:

**Yes, and it's more valuable than expected — but the sequencing is inverted from the question asked.**

The question was "can we use these maps to build oracle tests?" The measurement says the maps cannot be
*read* yet, so the first deliverable is not a test harness, it is four parser fixes. Recommended phasing:

1. **Phase 0 — parser conformance (blocking).** Fix double-quoted strings, `<<typeMode>>`, `%constant`,
   `///` metadata headers. Each is small and independently testable. Target: `fhir-test-cases` parse rate
   0/27 → 27/27; `fhir-r6-maps` 0/355 → ≥90%.
2. **Phase 1 — Tier C parse + Tier D round-trip gate.** Cheap, no execution needed. Point it at
   `HL7/fhir-cross-version` (1,201 files, HL7-governed) rather than the brianpos staging repo, and add the
   `.map`/`.json`/`.xml` triple comparison from the tutorial corpus. Land alongside Phase 0 as its
   acceptance test.
3. **Phase 2 — Tier A behavioural oracle.** Two runners: `manifest.xml`-driven for `fhir-test-cases`
   (r5 + r4b), convention-driven for the tutorial + careconnect corpora. ~130 graded cases. CDA-output
   cases on a frozen exclusion list per ADR-2607.
4. **Phase 2b — Tier B, near-free.** Add `map-general-test{,2}.fml` to the existing
   `ValidatorConformanceRunner` case set; the manifest and outcome files are already the ones it reads.
5. **Phase 3 (optional) — cross-version execution.** Wire `R6CoreSchemaProvider` and run the cross-version
   maps over `fhir-test-cases` R4/R5 examples. No oracle exists for the output, so grade on *invariants*
   (no exceptions, result validates against the target version, no data silently dropped) rather than
   equality. The bundled `ConceptMap`s in `HL7/fhir-cross-version` make this the only realistic way to
   exercise `TranslateTransform` at scale.

Open question for Phase 3: generating expected outputs with the Java validator or Firely SDK would turn
Tier 2 into a true oracle, but introduces a build dependency on a reference implementation and risks
encoding another engine's bugs as our spec. Recommend against it initially — invariant-grading is honest
about what it proves.

## Related investigation candidates

- **`fml-r6-grammar-support`** — the Phase 0 work as its own investigation: R6 metadata declarations,
  type-mode annotations, `%` constants, wildcard imports, arithmetic in embedded FHIRPath.
- **`cross-version-transform-pipeline`** — using the R4↔R6 maps as the actual conversion engine for the
  `fhir-compatibility` feature, not just as test data.
- **`fml-differential-testing`** — property-based/differential testing against a second FML engine
  (Matchbox, `brianpos/fml-processor`) instead of a static corpus.
