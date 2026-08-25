# Resource-Backed FHIRPath Reverse-Bridge Parity Corpus

## Purpose

This corpus characterizes reverse-bridge interoperability. Its input topology is:

```text
JSON -> Ignixa parser -> IElement
                          |-- Ignixa FHIRPath
                          `-- TypedElementAdapter -> Firely FHIRPath
```

That makes it sensitive to Ignixa evaluation and to Ignixa-to-Firely adapter behavior. It is not the ADR 2608 Phase 3 enablement gate. In particular, Firely is evaluated over an adapted Ignixa tree rather than the native Firely POCO used by fhir-server on ingress.

The Phase 3 topology is:

```text
JSON -> Firely parser -> Firely POCO/ITypedElement
                         |-- Firely provider
                         `-- ToIgnixaElement -> Ignixa provider
                                                |
                                      TypedElementAdapter
                                                |
                                     fhir-server indexer
```

The input direction is Firely-to-Ignixa, while Ignixa results still cross the reverse adapter on output. The complete gate therefore belongs in fhir-server, which owns the provider seam and final indexing pipeline. This repository supplies reusable fixtures, engine characterization, and a small native-Firely Phase 3 slice.

## Recommended Verification Structure

Keep three suites with explicit ownership rather than treating one topology as universal:

1. **Phase 3 gate in fhir-server.** Parse one native Firely POCO, run both providers from that input, return Ignixa results through `TypedElementAdapter`, and compare the final production index entries.
2. **Native-engine corpus.** Parse the same JSON independently with each engine and classify parser-attributable differences separately from evaluator differences.
3. **Reverse-bridge contract suite in Ignixa.** Retain this corpus to cover JSON-to-Ignixa input and `TypedElementAdapter` consumption by Firely callers.

The projects `Ignixa.FhirPath.Phase3.Stu3.Tests`, `Ignixa.FhirPath.Phase3.R4B.Tests`, and `Ignixa.FhirPath.Phase3.R5.Tests`, plus `R4NativeFirelyPhase3SliceTests` in `Ignixa.FhirPath.Tests`, implement only narrow slices of the first suite. They use native Firely POCO input and production Ignixa search-value converters, but they do not replace fhir-server's end-to-end gate. Separate compilations are required because the Firely version model assemblies export conflicting model and extension identities, including an ambiguous `ModelInfo`; process isolation is a consequence of the separate test assemblies rather than the underlying constraint.

## Corpus Shape

The expression corpus reuses `SearchParameterExpressionCorpus`. Expressions are loaded from the shipped STU3, R4, R4B, R5, and R6 search parameters, and comparison includes only expressions compiled by both engines.

The resource corpus contains:

- 733 seeded resources from `SchemaBasedFhirResourceFaker`, one for every supported resource type and FHIR version;
- 55 targeted `Observation` resources covering empty, singleton, and three-item collections; populated choice types; partial-precision temporals; equal instants in different offsets; quantity units; and reference resolution;
- present, absent, and contained `resolve()` targets; and
- direct language sensitivity cases for quantity equivalence and temporal ordering.

The sampling rule is:

> Evaluate every mutually compilable generated search-parameter expression against every generated or targeted resource whose base type can match the expression, then add focused semantic variants for behaviors that generated resources do not reliably reach.

This deliberately avoids the global expression-by-resource Cartesian product. The reduction keeps the corpus suitable for CI, but it is a coverage decision rather than an exhaustive claim.

### What This Gives Up

- Expressions are not run against resources whose base type cannot match, so accidental cross-type behavior is not sampled.
- The generated resources favor breadth over combinatorial depth.
- `NormalizeGeneratedTemporals` replaces generated temporal values with fixed values. Partial precision and offset variation therefore come only from targeted `Observation` fixtures, not all 733 generated resources.
- Faker collections contain one or two items. Cardinality three and order-sensitive collection behavior come only from targeted `Observation` fixtures.
- Choice, temporal, quantity, and reference edge cases are Observation-centric rather than repeated across every resource that can carry them.
- The quantity set covers `m`/`cm` but not `mg`/`kg`.
- The resource named `CalendarQuantity` carries a UCUM-like `year` unit; it does not exercise FHIRPath calendar-duration keywords.
- Malformed JSON and parser-recovery differences are outside this reverse-bridge corpus.
- Firely and Ignixa use independently implemented `resolve()` resolvers. They agree for the three targeted reference shapes, but no shared implementation prevents future drift.
- Firely 5.11.4 has no native R6 model package, so this repository cannot run the native-Firely Phase 3 slice for R6.

## Comparison Semantics

Each evaluation records one of three distinct outcomes:

- values returned;
- an empty collection returned; or
- an exception thrown.

Throwing is never normalized to empty. This prevents `ElementSearchIndexer` exception handling from hiding evaluator failures behind two equal empty index-entry sets.

Returned elements are compared in order by:

- collection count;
- `InstanceType`;
- the CLR carrier type of `Value`; and
- the rendered value.

Index comparison canonicalizes production search values, including date ranges, number ranges, quantity ranges, and min/max flags. The Ignixa side uses the real `ElementSearchIndexer`. The Firely reference projection evaluates with Firely and reuses production Ignixa converters and min/max marking.

The production-indexer test is intentionally a smoke test: it proves that the corpus can drive the production indexer and produce a known `status` entry. It does not claim exact drift detection against the Firely reference projection.

## Culture Coverage

Culture runs are split by the semantics each culture discriminates:

- `de-DE` covers decimal parsing and separator-sensitive quantity behavior.
- `th-TH` covers calendar-shifting temporal behavior.

The split avoids running the entire corpus twice while preserving targeted checks for the measured failure modes. Culture is restored after every run.

## Findings

The reverse corpus evaluated 19,647 expression/resource pairs per engine over 788 resources. It found 120 `Select` divergences and 11 resources with index divergences. The final verification measured 27.434 seconds in-process on .NET 9 and 29.506 seconds on .NET 10, with respective test-command wall times of 28.945 and 30.951 seconds. Those aggregate counts describe the reverse-bridge topology only.

### What the 19,647 evaluations actually establish

| Outcome | Count |
|---|---:|
| Both engines returned nothing | 9,453 |
| Both engines returned the same values | **10,074** |
| Both engines threw | 0 |
| Divergent | 120 |

The 10,074 is the count the conformance claim rests on, and it is floored rather than pinned so it can only be satisfied by holding or gaining evidence. It used to be derived by subtracting the other three from the total, which meant a both-threw counter that stopped incrementing would have inflated it and made its own floor easier to satisfy; all four are now counted where they are observed. The index half compares 10,743 Firely and 10,753 Ignixa canonicalized entries, each floored per engine.

### What the index half cannot see

The index comparison runs Firely for `Select` only. Everything downstream - the search parameter definitions, `InferSearchParamTypeFromFhirType`, `GetSearchValueTypeForSearchParamType` and the converter manager - is a single set of Ignixa objects that `SearchIndexParityHarness` constructs once and hands to both indexers. When both sides skip an element, that is one object making one decision, not two implementations agreeing, so **no entry-list comparison over this corpus can detect a gap in the converter pipeline.**

Capturing the production indexer's contained failures - it catches per search parameter, logs and continues, and the harness previously gave it a null logger - surfaced 302 of them per sweep, split by what the corpus can adjudicate:

| Class | Count | Status |
|---|---:|---|
| Contained throws (`ExpectedIgnixaEvaluationFailures`) | 1 | Adjudicable. Ignixa's `NotSupportedException` for `hasExtension()`, already pinned on the `Select` side. |
| Classification skips (`ExpectedIgnixaConverterPipelineSkips`) | 301 | Recorded, not adjudicable. |

Of the 301, 229 are converter-manager misses and **186 are `canonical` under 46 shipped SearchParameters** - 45 `Reference`-typed plus `MessageHeader-event`. Ignixa registers `canonical` against `UriSearchValue` only, so those 46 parameters index nothing: `QuestionnaireResponse-questionnaire`, `MeasureReport-measure`, `StructureDefinition-base`, `PlanDefinition-definition`, the `instantiates-canonical` family across nine resource types, the `-depends-on` family, and eight `ConceptMap` parameters among them.

microsoft/fhir-server, which this indexer was ported from, additionally ships `CanonicalToReferenceSearchValueConverter`, `IdToReferenceSearchValueConverter`, `IdentifierToStringSearchValueConverter` and `ReferenceToUriSearchValueConverter`. The first closes the 186. That is `Ignixa.Search` production work tracked separately as release-blocking, not a FHIRPath change; the counts are pinned exactly here so that when the converters land this corpus says by how much they moved.

The earlier `8 Select / 9 indexed` typed-choice count and `2 Select / 2 indexed` instant count are not valid Phase 3 numbers. Native Firely probes split each bucket into production-confirmed, harness-only, and unverifiable portions:

| Class | Phase 3 status | Evidence |
|---|---|---|
| STU3 capitalised casts | Confirmed production divergence: 11/11 evaluator, 10 final-index | Every shipped mis-cased primitive cast returns empty in Firely and its populated primitive in Ignixa. Ten non-composite parameters produce an Ignixa index entry and no Firely entry; the composite is the independently pinned double-empty case below. |
| R4 `code-value-date` capitalised cast | Confirmed production evaluator divergence; Ignixa composite index confirmed, Firely's inferred | Native `Observation.valueDateTime` with `value.as(DateTime) \| value.as(Period)` returns empty in Firely and one `dateTime` in Ignixa. The real Ignixa indexer emits the composite entry shown below; Firely's missing entry is inferred from its empty date component, not observed — see the section below. |
| R4B `code-value-date` capitalised cast | Confirmed production evaluator divergence; Ignixa composite index confirmed, Firely's inferred | The same native-POCO probe produces the same evaluator and composite outcome under R4B, with the same inferred Firely half. |
| Broader R4/R4B adapter-mediated choice cases | Harness artifacts; original counts remain invalid | Properly cased native choice casts agree. The reverse corpus's broader count came from presenting Ignixa choice metadata to Firely, not from native Firely input. |
| R5 instant/dateTime carrier | Confirmed production divergence | Native `Appointment.start` with `(start \| requestedPeriod.start).first()` returns Firely `System.DateTime` and Ignixa `instant`; Firely indexes a one-second range while Ignixa indexes a point. |
| R6 instant/dateTime carrier | Not production-confirmed | Firely 5.11.4 ships no native R6 model package, so the required native Firely input probe cannot be built. |

No aggregate Phase 3 divergence count is claimed from these slices. They characterize confirmed classes and controls, not the full fhir-server gate.

Since `804e678e`, Ignixa matches type identifiers with exact ordinal casing on every version and enables only an explicit legacy alias set below R5, failing open when the version is unknown. The STU3/R4/R4B differences are therefore deliberate and bounded: they track the FHIR release text changing from R4/R4B's `as()` allowance to R5's narrower `ofType()` allowance. They are not unconditional case-insensitive matching. R5 and R6 ship no capitalised alias casts.

### STU3 Capitalised Casts

The list below was derived by a case-sensitive scan of the generated STU3 definitions rather than copied from an earlier count. Native Firely input confirmed every shipped capitalised alias:

| Search parameter | Relevant shipped expression |
|---|---|
| `clinical-date` | `RiskAssessment.occurrence.as(DateTime)` |
| `CommunicationRequest-occurrence` | `CommunicationRequest.occurrence.as(DateTime)` |
| `DeviceRequest-event-date` | `DeviceRequest.occurrence.as(DateTime) \| DeviceRequest.occurrence.as(Period)` |
| `Observation-code-value-date` component | `value.as(DateTime) \| value.as(Period)` |
| `Observation-value-date` | `Observation.value.as(DateTime) \| Observation.value.as(Period)` |
| `Patient-death-date` | `Patient.deceased.as(DateTime)` |
| `Goal-start-date` | `Goal.start.as(Date)` |
| `Goal-target-date` | `Goal.target.due.as(Date)` |
| `Observation-value-string` | `Observation.value.as(String)` |
| `ConceptMap-source-uri` | `ConceptMap.source.as(Uri)` |
| `ConceptMap-target-uri` | `ConceptMap.target.as(Uri)` |

The scan yields `DateTime` ×6, `Date` ×2, `String` ×1, and `Uri` ×2.
For all 11 occurrences, Firely returns empty and Ignixa returns the populated primitive. Replacing only the cast target with the native lowercase FHIR spelling makes both providers return the same value. The committed STU3 slice pins the `String` and both `Date` divergences, their non-empty lowercase controls, and the two resulting date index entries:

```fhirpath
Observation.value.as(String) // Firely empty; Ignixa "parity"
Observation.value.as(string) // both return "parity"
Goal.start.as(Date)           // Firely empty; Ignixa 2024-06-15
Goal.start.as(date)           // both return 2024-06-15
```

This is a deliberate pre-R5 compatibility rule, not general case-insensitive matching. Firely is spec-correct under the base identifier-resolution rule: `String` resolves to `System.String`, which is distinct from `FHIR.string`. Ignixa additionally applies version-gated aliases to track the pre-R5 FHIR release allowance and shipped artifacts. The System aliases follow the release text; the two STU3 `Uri` aliases are bounded artifact errata with no System-type basis. Strict parity still treats the resulting index change as blocking.

A naive scan also finds `as(Quantity)` seven times in STU3 and 19 times in each of R4 and R4B. Those casts are intentionally excluded from the alias count: `Quantity` is the genuine PascalCase FHIR type and matches it directly, while FHIR's date primitive is lowercase `date`, making capitalised `Date` an alias for the distinct `System.Date`. The spelling `Quantity` is genuinely ambiguous between the FHIR and System models and is resolved by the normal FHIR-first rule; it is not a mis-cased FHIR primitive.

### Why `Select` Comparison Is Mandatory: STU3 Double-Empty

The STU3 `Observation-code-value-date` composite is the concrete case where final index equality gives a false answer. Firely drops the composite because `value.as(DateTime)` returns empty. Ignixa selects the date successfully, but its production indexer independently drops the same composite because the referenced `Observation-code` component cannot be resolved. Both providers therefore finish with an empty `SearchIndexEntry` set for unrelated reasons. `Stu3CompositeDoubleEmptyTests` pins all four links in that chain: Firely's empty date component, Ignixa's populated date component, the unresolved code component, and the absent production Ignixa composite entry.

An index-only corpus would report agreement and conceal both failures. Comparing `Select` outcomes exposes the evaluator divergence before the independent component-resolution defect erases it. This is why empty, thrown, and value-returning evaluations must remain distinct even when their final index sets happen to match.

### R4/R4B `code-value-date`

R4 and R4B each ship one capitalised primitive alias, in the date component of `code-value-date`:

```fhirpath
value.as(DateTime) | value.as(Period)
```

Against a native Firely `Observation` with `valueDateTime: "2024-06-15T08:00:00Z"`, Firely returns empty and Ignixa returns one `dateTime`. The lowercase control, `value.as(dateTime) | value.as(Period)`, returns the same date from both providers. The real Ignixa R4 indexer emits:

```text
(http://loinc.org|29463-7) $ (2024-06-15T08:00:00+00:00)
```

Firely's empty date component makes the composite incomplete, so its missing final entry is inferred from the pinned evaluator outcome; this repository does not own Firely's final production index pipeline. `R4NativeFirelyPhase3SliceTests` pins the shipped expression, the non-empty lowercase control, and the production Ignixa composite. `Ignixa.FhirPath.Phase3.R4B.Tests` independently pins the same three outcomes against the native R4B model. Firely 5.11.4 model assemblies prevent adding R4 and R4B to the same compilation, which is why R4B has an isolated test host.

### R5 Instant Carrier

For a native R5 `Appointment` at `2024-06-15T08:00:00Z`, Firely's result carrier is `System.DateTime`; the production date converter emits:

```text
[2024-06-15T08:00:00.0000000Z, 2024-06-15T08:00:00.9999999Z]
```

Ignixa preserves FHIR `instant`; the production instant converter emits:

```text
[2024-06-15T08:00:00.0000000Z, 2024-06-15T08:00:00.0000000Z]
```

This difference survives native Firely input and the Phase 3 output adapter.

### Choice Metadata Round Trip

The STU3 slice records the corrected seam-reachable output shape. The committed test pins the final `TypedElementAdapter` output, not each intermediate representation:

```text
Native Firely:
  Name=value
  InstanceType=string
  Definition.Type=[Quantity, CodeableConcept, string, ...]

Firely -> Ignixa:
  InstanceType=string
  Type.Info.Name=string

TypedElementAdapter output:
  Name=string
  InstanceType=string
  Definition.Type=[string]
```

`IgnixaElementAdapter.TypeAdapter` selects the declared choice type matching `InstanceType`; selecting the first declaration produced `Quantity` for this string value. The tested string index converter already dispatched correctly on `InstanceType`, but preserving the concrete type also makes `Name`, `Definition.Type`, and downstream POCO conversion consistent.

The change is on the Firely-to-Ignixa **input** path, so its reach is wider than the output seam above. `TypeInfo.Primitive` is derived from the selected type name, so for every Firely-sourced choice element whose selected type is primitive it moves from `None` to that primitive — `String` for the `valueString` traced here — and `Info.IsPrimitive` from `false` to `true`. `Ignixa.DeId`'s `ElementExtensions.IsPrimitiveType`/`IsPrimitiveElement` read `Info.IsPrimitive` to decide whether to treat a node as a leaf or recurse into it, and `Ignixa.FhirPath.Visitors.FhirPathType.TypeName` returns `Type.Info.Name`; both see the corrected value. Each move is toward the truth — a `valueString` genuinely is a primitive `string` — and `FirelySdkInteropTests` pins `Info.Primitive` on both the matched and the fallback branch so the shift stays deliberate.

### Reverse-Corpus Language Findings

These findings remain real, but do not block search-parameter enablement because the relevant constructs are absent from shipped search-parameter expressions:

| Class | Reverse `Select` divergences | Classification | Root cause or adjudication |
|---|---:|---|---|
| Quantity collections rejected by Firely | 100 | Language construct | 75 cases indicate Firely/shared-adapter structural limitations; 25 incompatible/calendar-unit cases still require specification adjudication. |
| Quantity `~` asymmetry | 5 | Language construct, confirmed defect | Ignixa converts into the left unit and derives precision from the converted decimal scale, making `~` non-commutative. |
| Temporal ordering rejected by Firely | 5 | Language construct | Firely rejects partial or mixed-offset temporal ordering that Ignixa evaluates; specification adjudication remains separate from search-parameter reachability. |

The quantity sensitivity control remains explicit:

```fhirpath
1 'm' ~ 104 'cm'  // Ignixa true, Firely false
104 'cm' ~ 1 'm'  // false in both
1 'm' ~ 100 'cm'  // true in both directions
```

Non-blocking means only that the defect is not reachable from the generated search-parameter expressions. It does not mean the behavior is correct.

## Production Seams and Generator Behavior

The parity work introduced a narrow testability seam:

- `SearchIndexerFactory.CreateIndexingComponents` exposes production converter and resolver construction internally so the reference projection does not reimplement Ignixa conversion policy.
- `ElementSearchIndexer.MarkMinAndMaxValues` became internal so the same production min/max logic can be reused.
- `InternalsVisibleTo` grants only the parity test assembly access to those internals.

Those three changes do not alter runtime indexing behavior.

`SchemaBasedFhirResourceFaker` is different: selecting a concrete choice schema before generation changes production faker output. The 1,428 faker tests pass, but payloads and seeded random sequences can change for consumers that pin generated data by seed. This is a real generator-behavior and determinism change, not merely a test seam.

## Official HL7 Conformance Baseline

The authoritative baseline lives in
[Official Test Suite Integration](investigations/official-test-suite-integration.md); this section
records it so a reader of the parity corpus does not have to leave the page, and defers to that
document on any discrepancy.

**Measured 2026-08-24**, `fhir-test-cases` **1.7.46** pinned per suite file by SHA-256, under
`--filter "Category=OfficialTestSuite"`:

```text
2,884 passed / 0 failed / 16 skipped with named reasons / 3 excluded by scope (CDA)
2,900 executed of 2,903 cases in the corpus
```

The figures previously recorded here — `2,896 passed / 4 skipped / 2,900 total`, audited down to
`2,887 genuinely asserted / 9 unsupported pass-throughs / 4 skipped` — are superseded. The audit was
directionally right and its arithmetic was the best available at the time, but it was a hand
subtraction performed on top of an instrument that could not produce the number: the runner caught
`NotSupportedException` and returned, so xunit recorded a pass. That was not limited to the nine
functions the audit identified — an unimplemented *binary operator* was laundered the same way, which
was demonstrated by deleting the `xor` arm from the engine and watching the pre-fix runner still
report a fully green suite. `Passed` now means passed, and the 9 pass-throughs are 9 of the 16
recorded skips.

The provenance concern raised here is also closed. The `.downloaded` marker now records both the
package version and the archive SHA-256, `VerifyFhirTestCasesProvenance` checks them at test time,
and `_suiteFileHashes` pins the SHA-256 of each extracted `tests-fhir-{r4,r4b,r5}.xml` — so the
checkout can now prove which upstream archive produced its cases, including against a hand edit to
the gitignored extracted tree. Issue #405's 2,908-case figure remains unreconcilable and should not
be cited.

State the filter with the number. On one commit against one corpus, passed counts of
**2,884 / 2,890 / 2,896** and totals of **2,900 / 2,906 / 2,912** are all reproducible, varying only
by `--filter`; a figure published without one says nothing.

Four of the 16 skips are version-policy skips (`testPlusDate19` and `testFHIRPathAsFunction21`, on R4 and R4B each). That count was 10 until six `testQuantity9`/`testQuantity10` skips were found to be stale - they passed on R4, R4B and R5 while still being skipped for a Fhir.Metrics limitation that no longer applied to them. Each of the four is now routed through `SkipUnlessTheCaseWouldNowPass`, which runs the case and fails if it passes, so that figure cannot drift the same way again. The other 12 are keyed to a typed not-supported marker and retire through a different guard - see the authoritative document.

## Running the Suites

Run the reverse corpus:

```powershell
dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj `
  --filter "FullyQualifiedName~ResourceBackedParityCorpusTests|FullyQualifiedName~ResourceBackedQuantitySensitivityTests|FullyQualifiedName~IgnixaProductionIndexerSmokeTests"
```

Run the native-Firely Phase 3 slices:

```powershell
dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj `
  --filter "FullyQualifiedName~R4NativeFirelyPhase3SliceTests"
dotnet test test/Ignixa.FhirPath.Phase3.Stu3.Tests/Ignixa.FhirPath.Phase3.Stu3.Tests.csproj
dotnet test test/Ignixa.FhirPath.Phase3.R5.Tests/Ignixa.FhirPath.Phase3.R5.Tests.csproj
```
