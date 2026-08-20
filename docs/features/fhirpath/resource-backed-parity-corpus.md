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

The projects `Ignixa.FhirPath.Phase3.Stu3.Tests` and `Ignixa.FhirPath.Phase3.R5.Tests` implement only a narrow slice of the first suite. They use native Firely POCO input and production Ignixa search-value converters, but they do not replace fhir-server's end-to-end gate. Separate test hosts are required because Firely 5.11.4 version model assemblies export conflicting model and extension identities and cannot safely coexist in one process.

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

The earlier `8 Select / 9 indexed` typed-choice count and `2 Select / 2 indexed` instant count are not valid Phase 3 numbers. Native Firely probes split each bucket into production-confirmed, harness-only, and unverifiable portions:

| Class | Phase 3 status | Evidence |
|---|---|---|
| STU3 typed-choice casing | Confirmed production divergence | Native `Observation.valueString` with `Observation.value.as(String)` returns empty in Firely and `string\|parity` in Ignixa; Firely indexes nothing while Ignixa indexes `parity`. |
| R4/R4B choice indexing | Confirmed harness artifact | Native `valueDateTime: "2012-01"` with `(Observation.value as dateTime) \| (Observation.value as Period)` returns one dateTime and the same January 2012 range from both providers. |
| R5 instant/dateTime carrier | Confirmed production divergence | Native `Appointment.start` with `(start \| requestedPeriod.start).first()` returns Firely `System.DateTime` and Ignixa `instant`; Firely indexes a one-second range while Ignixa indexes a point. |
| R6 instant/dateTime carrier | Not production-confirmed | Firely 5.11.4 ships no native R6 model package, so the required native Firely input probe cannot be built. |

No aggregate Phase 3 divergence count is claimed from these slices. They characterize two confirmed classes and one control, not the full fhir-server gate.

### STU3 Choice-Type Casing

Firely 5.11.4 matches the native lowercase `string` type name case-sensitively. Ignixa accepts the shipped STU3 `String` spelling case-insensitively:

```fhirpath
Observation.value.as(String) // Firely empty; Ignixa "parity"
Observation.value.as(string) // both return "parity"
```

The lowercase control proves that the slice discriminates on casing rather than resource construction or expression routing. Inspection found the same uppercase/lowercase pattern in shipped expressions for `DateTime`/`dateTime`, `Date`/`date`, and `Uri`/`uri`, but only the string case has been measured with native Firely input.

Enabling Ignixa would begin indexing a value that Firely currently drops. That blocks ADR 2608's strict parity criterion even though Ignixa's case-insensitive behavior is arguably more useful.

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

The STU3 slice also records a seam-reachable output defect:

```text
Native Firely:
  Name=value
  InstanceType=string
  Definition.Type=[Quantity, CodeableConcept, string, ...]

Firely -> Ignixa:
  InstanceType=string
  Type.Info.Name=Quantity

TypedElementAdapter output:
  Name=Quantity
  InstanceType=string
  Definition.Type=[Quantity]
```

The loss begins in `IgnixaElementAdapter.TypeAdapter`, which selects the first declared choice type. `TypedElementAdapter.Name` then exposes the collapsed type name. The tested string index converter dispatches on `InstanceType`, so this is not index-blocking for that case. It remains Phase 3 seam-reachable and can block callers that inspect `Name` or `Definition.Type`, or convert returned results to POCOs.

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

The current checkout's raw official-suite result is:

```text
2,890 passed / 10 skipped / 2,900 total
```

Nine of the 2,890 reported passes catch `NotSupportedException` and return successfully. The audited interpretation is therefore:

```text
2,881 genuinely asserted / 9 unsupported pass-throughs / 10 skipped
```

The `.downloaded` marker records neither source version nor content hash, so the checkout cannot prove which upstream archive produced its 2,900 cases. Forensics independently verified `fhir-test-cases` 1.7.46 at SHA-256 `D89CEC2BD3A22D9968AE91EFCB460B7FAA0802802840E7AC99A0A9D65B091302`, but issue #405's 2,908-case figure cannot be reconciled from repository provenance. Raw xUnit totals must not be presented as 2,890 supported conformance assertions.

## Running the Suites

Run the reverse corpus:

```powershell
dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj `
  --filter "FullyQualifiedName~ResourceBackedParityCorpusTests|FullyQualifiedName~ResourceBackedQuantitySensitivityTests|FullyQualifiedName~IgnixaProductionIndexerSmokeTests"
```

Run the native-Firely Phase 3 slices:

```powershell
dotnet test test/Ignixa.FhirPath.Phase3.Stu3.Tests/Ignixa.FhirPath.Phase3.Stu3.Tests.csproj
dotnet test test/Ignixa.FhirPath.Phase3.R5.Tests/Ignixa.FhirPath.Phase3.R5.Tests.csproj
```
