# Investigation: FML Oracle Conformance Corpus

**Feature**: structuremap
**Status**: Approved
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

| Corpus | Files | Parsed (before) | Parsed (after) |
|---|---:|---:|---:|
| `FHIR/fhir-test-cases` `r5` `structure-mapping` (`.map`) | 15 | **0** | **14** |
| `FHIR/fhir-test-cases` `r4b` `structure-mapping` (`.map`) | 12 | **0** | **11** |
| `FHIR/fhir-test-cases` `validator` (`.fml`) | 2 | **0** | **2** |
| `brianpos/fhir-r6-maps` (`.fml`) | 355 | **0** | *not re-measured* |

The "after" figures are the ratchets asserted by `FmlCorpusParseTests` and `FmlValidatorCorpusParseTests`
against vendored release **1.7.46**, not estimates. The `brianpos` corpus was deliberately **not** vendored:
it is a non-HL7-governed staging area with no expected outputs, so re-measuring it was left to the Phase 1
Tier C work rather than inflating this branch's scope. Its baseline is retained above only as the original
evidence that motivated the parser work.

This was not a "the harness is hard to build" result — it was a **language-coverage result**. The parser as
found handled the hand-written dialect used in `test/Ignixa.FhirMappingLanguage.Tests` and
`src/Core/Ignixa.FhirMappingLanguage/README.md` (single-quoted strings, `map 'url' = 'name'` header) and
nothing that the wild actually ships.

The single remaining parse failure is `qr2cda-eval.map` (present in both versions), which uses a nested
function call as a transform: `evaluate(src, iif(src.is(QuestionnaireResponse),"Hello CDA","badbadbad"))`.
The greedy `FhirPathExpression` fallback cannot expand the inner `iif()` safely because the `RightParen`
that closes it is not in the fallback terminator set. This is explicitly deferred: the file produces CDA
XML output and is excluded from the behavioural oracle regardless.

### Root causes — four concrete parser defects, all reproduced (all now fixed)

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

### Defect closure

Two further defects were found while implementing the fixes above, bringing the total to six. All six are
closed; each has regression tests plus a corpus ratchet as its acceptance test.

| # | Defect | Found | Status |
|---|---|---|---|
| 1 | Double-quoted string literals lexed as `DelimitedIdentifier` | investigation | **Fixed** |
| 2 | Group type-mode `<<types>>` / `<<type+>>` unsupported | investigation | **Fixed** |
| 3 | `%constant` / operator tokens (`+ - % / \| & <= >=`) absent from the tokenizer | investigation | **Fixed** |
| 4 | `///` metadata declarations swallowed as comments; `map` header mandatory | investigation | **Fixed** |
| 5 | Embedded FHIRPath re-serialised from tokens, gluing them together and corrupting the expression | planning | **Fixed** — grammar now reconstructs from source text spans |
| 6 | Parenthesized FHIRPath transforms `-> tgt.x = ('a' + b.lower())` rejected | Task 8 gate | **Fixed** |

Defects 5 and 6 were both surfaced *by the corpus gate itself* rather than by inspection, which is the
main argument for keeping the gate.

Still open, deliberately: nested function calls as a transform argument (`evaluate(src, iif(...))`) — the
one remaining parse failure; wildcard imports (`imports "…/*3to4"` *parses*, but `ImportResolver` has no
glob semantics, so the group closure is never resolved); and unit-bearing date arithmetic (`%value + 5 days`
parses but does not evaluate).

### Measured outcome: the behavioural oracle

`FmlTransformOracleTests` loads both `<fml-tests>` manifests, executes each in-scope case end-to-end
(`MappingParser` → `MappingEvaluator` → JSON) and compares against the reference-produced output using
canonical JSON comparison.

| Metric | Value |
|---|---:|
| Manifest cases (r5 + r4b) | 20 |
| Excluded — CDA/XML output, outside supported scope | 8 |
| **In scope, executed and compared** | **12** |
| Producing matching output | **2** |
| Executed but ratcheted as known evaluator gaps | **10** |

The two passing cases are `qr2patassignment` in each version. The remaining ten reduce to **five distinct
evaluator defects**, each appearing once per version:

| Case | Defect | Issue |
|---|---|---|
| `qr2patgender` | Target alias binds to the source element, not the target resource — output is the QuestionnaireResponse tree under a `patient` key | [#372](https://github.com/brendankowitz/ignixa-fhir/issues/372) |
| `qr2pathumannametwice` | Nested/recursive `then` groups are not evaluated | [#373](https://github.com/brendankowitz/ignixa-fhir/issues/373) |
| `qr2pathumannameshared` | `share` combined with nested `then` groups is not evaluated | [#373](https://github.com/brendankowitz/ignixa-fhir/issues/373) |
| `reference` | `create()` / `reference()` throw `TARGET_RESOURCE_NOT_FOUND` for the `'ext'` target | [#374](https://github.com/brendankowitz/ignixa-fhir/issues/374) |
| `qr2pat-gender-conformstoqr` | FHIRPath `conformsTo()` is a declared capability gap (needs profile-validation infrastructure) | [#375](https://github.com/brendankowitz/ignixa-fhir/issues/375) |

These are **ratcheted, not skipped**. `FmlKnownEvaluatorGaps` asserts each is *still broken*: the moment one
starts producing matching output, its test fails and demands the entry be deleted. That is deliberately the
opposite polarity from `FmlOracleExclusions`, which covers cases that cannot be compared at all. Keeping the
two lists separate prevents the usual failure mode where a hard-but-comparable case quietly migrates onto
the exclusion list to make the number go up.

Supporting counts, measured on both `net9.0` and `net10.0`: `Ignixa.FhirMappingLanguage.Tests` went from
546 to **656 passing, 0 failing, 1 skipped**.

### Tier B is not reachable as a diagnostic oracle

The plan assumed the two official FML validator cases were "near-free" — add them to
`ValidatorConformanceRunner`'s filter and let the existing machinery grade them. That premise was measured
false on four independent counts:

1. **Scope.** `ConformanceCaseAnalysis.IsR4CleanBase` requires `version == "4.0"` *and* a `.json` input.
   Both FML entries omit `version` (absent means current R5, matching their `R5.*` outcome filenames) and
   carry `.fml` inputs. They are excluded twice over, by design.
2. **Engine.** `ValidatorConformanceRunner.TryValidate` calls `JsonNode.Parse` on the input. FML text throws
   `JsonException`, which the runner deliberately scores as `Invalid` — so the cases would appear to pass
   *because JSON parsing failed*. A textbook pass-for-the-wrong-reason.
3. **Grading model.** The runner reduces each case to binary valid/invalid on error count. These outcomes
   are line/column-anchored diagnostics (test1: 2 errors + 2 warnings; test2: 3 errors + 2 information).
4. **Capability.** Reproducing those diagnostics needs StructureMap *semantic* validation Ignixa does not
   have: cross-map group resolution through `imports` (including the wildcard `*4to3` form), cross-version
   **R3** StructureDefinition resolution, element-path validation, default-rule inference, and source/target
   mode cross-checking.

That is multi-week work spanning FML → Specification → Validation, and it grades **2 cases**; the five
evaluator defects above unblock **10**. Tier B was therefore reduced on this branch to what it can honestly
support today — a **parse ratchet** (`FmlValidatorCorpusParseTests`) asserting both files parse and that
their distinguishing constructs survive: `extends Element <<type+>>`, wildcard `imports`, the `map` header,
the `+` operator, and `('urn:uuid:' + r.lower())`. That construct set overlaps the `structure-mapping`
corpus (`ActivityDefinition.map` uses wildcard `imports` and `extends … <<type+>>`; `syntax.map` uses the
parenthesized-FHIRPath transform), so the ratchet is redundant coverage rather than unique coverage — but
it is the only assertion that keeps these two official files from silently rotting, and it costs 4 tests.

The full semantic-validation work is tracked as
[#376](https://github.com/brendankowitz/ignixa-fhir/issues/376), explicitly gated behind #372–#375.

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

### Tier B — FML validation oracle (not reachable as a diagnostic oracle)

| Corpus | Content | Manifest |
|---|---|---|
| **`FHIR/fhir-test-cases` `validator/map-general-test{,2}.fml`** | 2 FML files | `validator/manifest.json` → `validator/outcomes/java/R5.map-general-test{,2}-base.json` |

These grade **FML validation diagnostics** (does Ignixa emit the same `OperationOutcome` as the Java
validator for a malformed/edge-case map), not transform output. `ValidatorConformanceRunner` in
`test/Ignixa.Validation.Tests/Conformance/` consumes this same manifest, which made these 2 cases look
near-free.

**They are not** — see *Tier B is not reachable as a diagnostic oracle* above. Both cases are excluded from
that runner by design, and its JSON-only, binary-graded model would score them green for the wrong reason.
They are covered here as a parse ratchet instead; the diagnostic oracle is tracked as
[#376](https://github.com/brendankowitz/ignixa-fhir/issues/376).

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
| Transform behaviour (true oracle) | **~130** (20 manifest + 36 tutorial + 75 careconnect + misc) |
| FML validation diagnostics | 2 |
| Serialization round-trip vs reference artefacts | ~25 map triples |
| Grammar / parse / structural resolution | **~1,560 `.fml`** (1,201 HL7 + 355 brianpos) |

This is roughly an order of magnitude more behavioural coverage than the first-pass estimate of "6 in-scope
R5 cases", and it changes the Phase 2 value case substantially.

## Verdict

**Partially viable — proven, and the sequencing was inverted from the question asked.**

The question was "can we use these maps to build oracle tests?" The measurement said the maps could not be
*read*, so the first deliverable was not a test harness but six parser fixes. Both halves are now built and
measured.

**What was delivered**

- **Parser conformance.** All six defects closed. Official corpus parse rate **0/27 → 25/27**
  (r5 14/15, r4b 11/12), plus **2/2** on the validator FML files. The single failure, `qr2cda-eval.map`,
  is deferred with a written reason and produces CDA XML that the oracle excludes anyway.
- **Tier A behavioural oracle.** Manifest-driven runner over r5 + r4b. **12 in-scope cases**, of which
  **2 produce output matching the reference implementation** and **10 are ratcheted known evaluator gaps**
  reducing to five distinct defects ([#372](https://github.com/brendankowitz/ignixa-fhir/issues/372)–[#375](https://github.com/brendankowitz/ignixa-fhir/issues/375)).
- **Tier B.** Reduced to a parse ratchet; the diagnostic oracle is infeasible against the existing runner
  and is tracked as [#376](https://github.com/brendankowitz/ignixa-fhir/issues/376).

**Why "partially" and not "viable"**

A 2/12 behavioural pass rate is not a success metric — it is a *baseline*. The honest reading is that the
harness works and the corpus is a good oracle; what it exposed is that `MappingEvaluator` is substantially
less complete than `MappingParser` now is. The value delivered is that those five defects are now
reproducible, named, ratcheted, and individually tracked, instead of unknown. Any of the five can be fixed
independently and the harness will tell you immediately.

**Exclusions, in full**

| Case (both versions) | Rationale |
|---|---|
| `qr2cda` | Targets the CDA logical model and produces XML; Ignixa's transform pipeline emits FHIR JSON only. |
| `qr2cdaxsi` | CDA logical model with `xsi:type` discrimination; XML output out of scope. |
| `qr2cd-eval-json` | CDA logical model target; XML output out of scope. (Also the only case whose `map` is a JSON StructureMap rather than FML.) |
| `qr2cd-eval-fml` | CDA logical model target; XML output out of scope. |

Four cases × two versions = 8 excluded of 20. No case with JSON output is excluded, and a guard test
asserts exactly that — an excluded case must have an XML output file — so the list cannot be used to make
a hard-but-comparable case disappear.

**Remaining phasing**

1. **Evaluator conformance (next).** Fix #372–#375. Each removal from `FmlKnownEvaluatorGaps` is a
   measurable step; the ceiling is 12/12.
2. **Tier C parse gate.** Point at `HL7/fhir-cross-version` (1,201 files, HL7-governed) rather than the
   brianpos staging repo. Cheap: no execution, and the parser is now in a state where it is worth running.
3. **Tier D round-trip.** `.map` / `.json` / `.xml` triple comparison from the tutorial corpus, grading
   `StructureMapParser`, `StructureMapBuilder`, and `FmlSerializer`.
4. **Tier A expansion.** Tutorial + careconnect corpora (~130 graded cases), convention-driven discovery.
   Do this only after #372–#375, or it just multiplies the same five failures.
5. **StructureMap semantic validation** (#376) — gated behind the above.
6. **Cross-version execution (optional).** Wire `R6CoreSchemaProvider` and run the cross-version maps over
   R4/R5 examples. No oracle exists for the output, so grade on *invariants* (no exceptions, result
   validates against the target version, no data silently dropped) rather than equality.

Open question for step 6: generating expected outputs with the Java validator or Firely SDK would turn
Tier C into a true oracle, but introduces a build dependency on a reference implementation and risks
encoding another engine's bugs as our spec. Recommend against it initially — invariant-grading is honest
about what it proves.

## Related investigation candidates

- **`fml-evaluator-conformance`** — closing the five ratcheted evaluator gaps (#372–#375). The highest-value
  follow-up: it is the only work that moves the 2/12 oracle number.
- **`fml-grammar-remainder`** — what Phase 0 did *not* close: nested function calls as transform arguments,
  wildcard imports (`imports "…/*4to6"`), and unit-bearing date arithmetic (`%value + 5 days`).
- **`cross-version-transform-pipeline`** — using the R4↔R6 maps as the actual conversion engine for the
  `fhir-compatibility` feature, not just as test data.
- **`fml-differential-testing`** — property-based/differential testing against a second FML engine
  (Matchbox, `brianpos/fml-processor`) instead of a static corpus.
