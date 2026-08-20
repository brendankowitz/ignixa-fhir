# Resource-backed Firely parity corpus

Status: Current
Reference: GitHub issue #405 and ADR 2608

## Purpose

This corpus is the first verification artifact required by ADR 2608 before Ignixa can replace
Firely 5.11.4 in Microsoft's fhir-server search indexing path. It evaluates shipped search
parameter expressions against real resource-shaped trees, compares the selected elements, and
then compares the resulting search index entries.

An evaluation that throws is recorded separately from one that returns an empty collection.
This distinction is load-bearing: the production indexer contains evaluation failures and emits no
entries, so comparing index sets alone can make a throw and a legitimate empty result look equal.

## Data flow and ownership

`SchemaBasedFhirResourceFaker` produces one Ignixa resource tree. `TypedElementAdapter` exposes that
same tree to Firely, controlling for parser and source-document differences.

The Ignixa path runs end-to-end through the production `ElementSearchIndexer`. The Firely path is
the only test-owned projection: it evaluates with Firely, then feeds the selected values through
the production search-value converters and production min/max marking. It cannot use
`ElementSearchIndexer` directly because that type owns Ignixa expression evaluation rather than a
provider-injected evaluation seam. Keeping evaluation pluggable there would remove the remaining
test-side orchestration.

`Select` identity includes:

- result count and order;
- canonical `InstanceType`;
- CLR carrier category; and
- invariant value, including null.

Index identity is a sorted multiset of parameter URL, serialized value, ordinal position, and
duplicate multiplicity. Cancellation propagates. Other Firely evaluation/conversion failures are
contained as empty index contributions to match production behavior, while remaining visible in
the separate `Select` outcome.

## Sampling strategy

The global cross-product is intentionally not run. Instead the corpus covers:

- every concrete resource type in STU3, R4, R4B, R5, and R6;
- every shipped search parameter applicable to that resource type whose expression both engines
  compile;
- maximum-density deterministic faker output; and
- targeted resources for choice values, cardinalities 0/1/3, compatible and incompatible units,
  calendar quantities, partial-precision temporals, equivalent instants with different offsets,
  and present/absent/contained `resolve()` targets.

The current run contains 733 generated resources plus 55 targeted resources. It performs 19,647
`Select` evaluations per engine and indexes all 788 resources through both paths.

This gives up:

- evaluating expressions against unrelated resource base types;
- evaluation-level comparison for expressions that either engine cannot compile;
- an exhaustive cross-product of all targeted semantic variations;
- arbitrary combinations of optional resource content; and
- custom search parameters not shipped with the supported FHIR packages.

Compile failures remain explicit corpus metadata rather than silently disappearing:

| Version | Shipped parameters | Common distinct expressions | Ignixa compile failures | Firely compile failures |
|---|---:|---:|---:|---:|
| STU3 | 1,246 | 1,170 | 0 | 0 |
| R4 | 1,403 | 1,350 | 0 | 1 |
| R4B | 1,437 | 1,375 | 0 | 0 |
| R5 | 1,242 | 1,207 | 0 | 0 |
| R6 | 1,288 | 1,253 | 0 | 0 |

## Culture coverage

Generated resources and decimal/quantity cases run under `de-DE`, exercising comma-decimal
behavior. Temporal precision and equivalent-offset cases run under `th-TH`, whose Buddhist
calendar shifts the parsed year and therefore discriminates failures that `de-DE` does not.
Culture mutation makes the sweep sequential. Running all resources under both cultures would
double runtime without adding the same discriminating value, so culture coverage is split by the
data each culture stresses.

## Findings

“Non-blocking” means the behavior cannot be reached from a shipped search parameter and therefore
does not block ADR 2608 enablement. It does not mean the behavior is correct or acceptable.

| Root cause | Classification | Blocks enablement | `Select` outcomes | Divergent indexed resources | Evidence |
|---|---|---:|---:|---:|---|
| Typed choice casts over the shared adapter | SearchParameter-reachable | Yes | 8 | 9 | Firely returns empty for populated STU3 `as(DateTime/Date/Uri/String)` choices that Ignixa returns; R4/R4B composite date choice entries also disappear from the Firely projection. |
| Instant versus dateTime carrier | SearchParameter-reachable | Yes | 2 | 2 | R5/R6 Appointment dates are `System.DateTime`/`dateTime` in Firely and `instant` in Ignixa, producing a millisecond range versus a point instant. |
| Firely rejects resource-backed quantity collections | Language construct | No | 100 | 0 | Firely throws `ArgumentException` for resource-backed `min()`, `max()`, `sum()`, `avg()`, and `sort()` cases; Ignixa returns a value or empty collection. |
| Quantity approximate equivalence is asymmetric | Language construct | No | 5 | 0 | In every version, `1 'm' ~ 104 'cm'` is false in Firely and true in Ignixa. The reverse direction is false in both; exact `1 'm' ~ 100 'cm'` is true both ways. |
| Firely rejects resource-backed temporal ordering | Language construct | No | 5 | 0 | Firely throws for `sort()` over resource-backed partial/equivalent-offset dateTime values while Ignixa returns an ordered collection. |
| **Total** |  |  | **120** | **11** | 10 blocking `Select` outcomes, 110 real but non-blocking outcomes; all 11 index divergences are blocking. |

The quantity-equivalence defect is caused by `ValueOrdering.TryAlignUnits` converting into the
left operand's unit and returning a bare decimal. `FhirPathEvaluator.GetDecimalPrecision` then
derives precision from that converted decimal's scale rather than the source quantity's stated
precision. The rounding floor consequently depends on operand order. `~` and `!~` occur zero times
in shipped search parameter expressions across all five versions, which is why this confirmed
off-parity defect is classified as language-only rather than ignored or treated as fixed.

Present, absent, and contained `resolve()` targets agree after Firely's FHIR symbol extensions are
initialized before expression-corpus loading.

## Runtime and conformance floor

On the measured development run, `Select` took 14.624 seconds and index comparison took 7.379
seconds, for 22.003 seconds in the corpus. The focused `dotnet test` command took 40.713 seconds
including incremental build and test-host startup.

The current official HL7 suite result from this repository is **2,890 passed / 10 skipped / 2,900
total**: R4 contributes 935, R4B 933, and R5 1,032 runnable cases after three CDA exclusions. This
corrects both ADR 2608's historical 2,906/2,906 claim and issue #405's expected
2,898 passed / 10 skipped / 2,908 total figure.

## Running the gate

```powershell
dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj `
  -f net10.0 `
  --filter "FullyQualifiedName~ResourceBackedParityCorpusTests"
```

The gate fails on any unclassified divergence, any stale expected root-cause count, or any
classification whose reachability and enablement status disagree.
