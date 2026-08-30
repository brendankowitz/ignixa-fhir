# Firely 5.11.4 Parity Inventory

Status: Current as of 2026-08-17
Harness: `test/Ignixa.FhirPath.Tests/Evaluation/Parity/`
Context: [ADR 2608](https://github.com/microsoft/fhir-server/blob/personal/bkowitz/ignixa-fhirpath-seam/docs/arch/adr-2608-ignixa-fhirpath-seam.md) (microsoft/fhir-server)

This is the set of behaviours a seam author must know when swapping Ignixa in for Firely behind
`IFhirPathProvider`. It is **not a bug list**. ADR 2608's policy is that where Ignixa is more
spec-compliant it keeps its behaviour and the seam or provider adapts; most of what follows is
Ignixa being more correct, not less.

Entries are ranked by **reachability from a shipped SearchParameter expression**, because those run
on every write through `TypedElementSearchIndexer`. A divergence nothing can reach costs nothing.

---

## Headline

This document describes the original expression-focused R4 inventory. The all-version,
resource-backed enablement gate now lives in
[Resource-backed Firely parity corpus](resource-backed-parity-corpus.md). That follow-up runs
shipped expressions against 788 real resource-shaped inputs, compares CLR carriers as well as
FHIR values, and uses the production Ignixa indexer.

**The inventory is short. Read what the population is before reading that as a finding.**

Across the R4 search parameter corpus - **1,367 distinct expressions x 5 resources = 6,835
evaluations per engine** - the two engines disagree on **6 outcomes arising from 2 root causes**.
Neither cause changes an indexed *value*; one changes an element's declared type, the other changes
*when* an already-broken parameter fails.

Those 6,835 evaluations decompose as follows, and the shape matters more than the divergence count:

| Outcome | Count | What it establishes |
|---|---:|---|
| Both engines returned nothing | 6,752 | Agreement on absence. Weak evidence. |
| Both engines returned the same values | **76** | The only positive evidence in this sweep. |
| Both engines threw | 1 | No values compared. This is the `hasExtension()` subject that makes entry 1's count 4 and not 5. |
| Divergent | 6 | The inventory below. |

**76 is the number of evaluations in this sweep that compared matching non-empty values.** The
corpus is every shipped R4 SearchParameter expression run against five subject resources, so almost
every expression addresses a resource type the subject is not, and answers empty on both engines for
reasons that have nothing to do with either engine. A short inventory over this corpus means "no new
disagreement appeared among the expressions production evaluates on every write" - a regression net.
It is not a measure of how much behaviour has been shown to agree. The volume of matched values
lives in the [resource-backed corpus](resource-backed-parity-corpus.md), at 10,074 across 788
resources.

All four counts are pinned in `KnownDivergences.SearchParameterPopulation`, because until they were,
an evaluation that stopped comparing values and started throwing on both engines left every entry in
this document satisfied and the suite green.

The language-construct corpus (83 expressions, deliberately chosen to target what this branch
changed) produces 58 outcomes, grouped in this document under 6 open root causes. 17 of its 415
evaluations are mutual throws - it deliberately probes operations one engine or the other does not
implement - and those are pinned in `KnownDivergences.ConstructPopulation` for the same reason.
**None of them is reachable from any shipped R4 SearchParameter expression.**
Two entries have since closed: entry 5 (`is` on a multi-item collection), fixed by enforcing the
singleton rule the spec mandates for `is`; and entry 6 (`highBoundary()` at year precision), which
was the one outright Ignixa defect in the list. Entry 6's expression still diverges, but now only by
the timezone offset already documented as entry 8.
Note entry 7 (`Scalar`) never appears in the construct sweep and is pinned by a standalone test
instead, so the signature count in `KnownDivergences.ConstructSignatures` is grouped under fewer
causes than the total.

The practical read: FHIRPath evaluation is not the risky part of the migration. Two items need a
decision before enabling Ignixa; the rest are documentation.

---

## The Firely version this is measured against

**Pinned to Firely 5.11.4**, not the 6.0.1 this repo ships.

ADR 2608 targets 5.11.4 and pins semantics that 6.0.1 changed - most visibly `Scalar`, which calls
`Single()` in 5.11.4 (throws on 2+ results) and returns null in 6. Measuring against 6.0.1 and
calling it "Firely parity" would have compared against an engine the seam is not replacing, and would
have silently hidden divergence #7 below, since Ignixa matches SDK 6 there.

The override is scoped to the one test project and documented at the reference:

```xml
<PackageReference Include="Hl7.Fhir.Base" VersionOverride="5.11.4" />
<PackageReference Include="Hl7.Fhir.R4" VersionOverride="5.11.4" />
```

No Ignixa Core project references `Hl7.Fhir.*` at all, so this creates no version conflict anywhere
else in the solution.

Only `Hl7.Fhir.R4` is referenced. The FHIRPath engine lives in `Hl7.Fhir.Base` and is
version-independent; adding a second model assembly would make `Hl7.Fhir.Model.ModelInfo` ambiguous
between releases. Consequently **the R5-specific `as`/`is` behaviour is not covered here** - see
"Not yet covered".

---

## What is compared, and what is normalised

Compared: result count, each result's `InstanceType`, each result's CLR carrier and invariant value,
and whether evaluation threw. A throw is an outcome, not a test failure - "one engine throws where
the other returns empty" is the exact mechanism ADR 2608 names for turning a conformance gap into
silent index drift.

Two normalisations, both deliberate:

1. **Exception types are recorded but not compared.** The two SDKs have unrelated exception
   hierarchies, so comparing type names would mark every mutually-agreed error as a divergence. Both
   throwing counts as agreement; the types appear in the report.
2. **`InstanceType` has a leading `System.` stripped and is compared case-insensitively.** This
   collapses divergence #3 below, which is otherwise restated on every single operator result and
   accounted for 158 of the original 168 construct rows. It is pinned by its own test instead. The
   rule cannot mask a real type difference - `BackboneElement` vs `Observation.Component` and
   `string` vs `code` both still diverge under it.

---

## Tier 1 - reachable from a shipped SearchParameter

### 1. `hasExtension()` is unimplemented in both engines, and they fail at different times

| | |
|---|---|
| **Repro** | `QuestionnaireResponse.item.where(hasExtension('http://hl7.org/fhir/StructureDefinition/questionnaireresponse-isSubject')).answer.value.ofType(Reference)` |
| **Firely** | Throws `ArgumentException: Unknown symbol 'hasExtension'` - at **compile** time, for every input resource |
| **Ignixa** | Returns **empty** for any resource whose `QuestionnaireResponse.item` path is empty; throws `NotSupportedException: Function 'hasExtension' is not yet implemented` once the filter actually runs |
| **Spec** | Neither is correct. `hasExtension()` is a FHIR-defined FHIRPath function ([FHIR R4 FHIRPath, Additional Functions](https://hl7.org/fhir/R4/fhirpath.html#functions)) and both engines lack it. |
| **Reachable** | **Yes.** `QuestionnaireResponse-item-subject`, the only R4 search parameter using `hasExtension`. |

**Why it matters.** Today under Firely this parameter throws on *every* resource write and
`TypedElementSearchIndexer` swallows it into an empty index entry set - so the parameter has never
worked and nobody noticed. Under Ignixa it returns empty quietly for unrelated resources and throws
only when a real `QuestionnaireResponse` with items is written. ADR 2608 states that in Ignixa mode
evaluation failure is surfaced as a metric and is a bake-in gate rather than a swallowed warning, so
**this parameter will light up that gate on the first QuestionnaireResponse write.** That is a
correct alarm about a pre-existing gap, but it will look like a regression introduced by the
migration.

**Provider action:** implement `hasExtension()` in Ignixa (it is a trivial wrapper over `extension()`
plus `exists()`), or allowlist this parameter in the bake-in gate with this note attached. Implementing
it makes Ignixa strictly better than the engine it replaces.

### 2. Backbone elements are typed by path in Ignixa and as `BackboneElement` in Firely

| | |
|---|---|
| **Repro** | `Observation.component` |
| **Firely** | 3 results, each `InstanceType` = `BackboneElement` |
| **Ignixa** | 3 results, each `InstanceType` = `Observation.Component` |
| **Spec** | Firely follows the StructureDefinition, where the element's declared type code is literally `BackboneElement` ([FHIR R4 Observation](https://hl7.org/fhir/R4/observation.html)). Ignixa's path-derived name is more informative but is not the FHIR type name. **Firely is spec-correct.** |
| **Reachable** | **Yes.** Same values and same count - only the declared type differs. |

**Why it matters.** Values and cardinality are identical, so indexed *content* is unaffected for any
converter that reads `.Value`. The risk is any code that switches on `InstanceType` to select a
converter. In fhir-server the search-value converters are keyed by type, so a converter registered
for `BackboneElement` would not be selected for `Observation.Component`.

**Provider action:** the `TypedElementAdapter` that wraps Ignixa results for the seam should map
path-derived backbone names back to `BackboneElement`. This is adapter work, not engine work - Ignixa
should keep the more informative name internally.

### 3. Operator results are typed in the `System` namespace by Firely and with FHIR primitive names by Ignixa

| | |
|---|---|
| **Repro** | `active and true`; `'a' & 'b'`; `1 + 1`; `birthDate + 1 year`; `1 'mg'` |
| **Firely** | `System.Boolean`, `System.String`, `System.Integer`, `System.Date`, `System.Quantity` |
| **Ignixa** | `boolean`, `string`, `integer`, `date`, `Quantity` |
| **Spec** | FHIRPath defines its primitives in the `System` namespace and requires reflection to report them there ([FHIRPath N1, Types and Reflection](http://hl7.org/fhirpath/N1/#types-and-reflection)). **Firely is spec-correct.** |
| **Reachable** | **Yes, but barely** - one shipped R4 expression returns a bare operator result: `Patient.deceased.exists() and Patient.deceased != false`. |

**Why it matters.** Only expressions whose *top-level* result is an operator result are affected;
every path-valued search parameter returns FHIR elements and is unaffected. Values are identical.

**Provider action:** map `System.*` in the adapter if any converter dispatches on it. Low priority -
one expression, and its value is a boolean either way.

---

## Tier 2 - not reachable from any shipped SearchParameter

Verified by searching the generated R4 definitions: `lowBoundary`/`highBoundary`, the `in` operator,
unary minus on a path, and type-suffixed choice element names have **zero** occurrences.

### 4. `in` with an empty left operand

`gender in ('male' | 'female')` against a resource with no `gender`.
Firely returns `false`; **Ignixa returns empty, which is spec-correct** - FHIRPath specifies that if
the left operand is empty the result is empty ([FHIRPath N1, Operations > Collections](http://hl7.org/fhirpath/N1/#collections-2)).
Keep Ignixa's behaviour; no provider action.

### 5. `is` against a multi-item collection — CLOSED

`name is HumanName` where `name` has 2 entries.

Firely raised an error while Ignixa returned empty for the operator and raised an error for the
equivalent `name.is(HumanName)` function — internally inconsistent, and Firely was correct. The spec
requires an error for both ([FHIRPath N1, Operations > Types](http://hl7.org/fhirpath/N1/#types)).

**Fixed.** `is` now enforces both errors the spec mandates for it — multi-item input and an
unresolvable type identifier — matching `is()` and matching Firely. No version gate was needed:
every `is` in a shipped SearchParameter expression from STU3 through R6 is
`where(resolve() is <ConcreteResource>)`, singleton by construction, so the artifact-compatibility
argument behind the `as` gate does not transfer.

The harness is what closed this entry: it failed with *"pinned divergence(s) no longer occur"* once
the fix landed, rather than silently continuing to assert a divergence that had gone. That is the
mechanism working — the inventory cannot quietly rot.

### 6. `@2012.highBoundary()` returned January, not December

**An Ignixa defect. Fixed.** Firely returns `2012-12-31T23:59:59.999`; Ignixa returned
`2012-01-31T23:59:59.999-12:00`. Month-precision input (`@2012-06`) was handled correctly, which
localised it to year precision.

The cause was a single comparison in `FormatDateTimeHighBoundary`: the month was widened to December
only when the requested output precision was *exactly* month level (`outputPrecision == 6`). The
default `highBoundary()` call asks for full millisecond precision, so that test failed, the
unspecified month stayed at its parsed default of January, and the day component was then computed
as the last day of January. Every other component in the method already used a "this precision or
finer" test (`>= 8`, `>= 10`, `>= 12`); month was the only equality check, and
`FhirTemporal.GetUpperBound` had year precision right all along, so the two disagreed internally.

What remains is only the timezone-offset difference described in entry 8, so this expression's pin
moves into that benign class rather than disappearing. Guarded by
`GivenAYearPrecisionDate_WhenTakingItsHighBoundary_ThenBothEnginesReportDecember` plus a
precision-sweep theory covering coarser and finer input, including February in leap and non-leap
years.

### 7. `Scalar` with 2+ results

**The one ADR 2608 calls out by name.** Firely 5.11.4 throws `InvalidOperationException`; Ignixa
returns null, matching Firely SDK 6.
This is a *seam* concern rather than an engine one: ADR 2608 already derives `Scalar` from `Select`
inside the seam precisely so the provider's own helper is never consulted. **The inventory entry
exists to say that derivation is load-bearing** - if a future refactor delegates `Scalar` to the
provider, ambiguous search parameter definitions stop throwing and start silently returning null.
Related: Firely's `Predicate` returns **true** on empty while its `IsTrue` returns **false**. Ignixa
does not ship *no* `Predicate` - it ships two, and they disagree with each other and with Firely.
`TypedElementExtensions.Predicate` (the FHIRPath engine's own copy) is a pure delegation to `IsTrue`,
so it returns **false** on empty - the opposite of Firely's contract. A second, unrelated `Predicate`
in `Ignixa.DeId.Extensions.FhirPathExtensions` (the one actually bound at the `GeneralizeProcessor`
call site) returns false on empty but **true** on 2+ results, disagreeing with the first Ignixa copy
too. This is a worse failure mode than "no equivalent": a seam author who reached for the engine's
`Predicate` would silently invert the empty case rather than get a compile error. The gap went
unnoticed because the differential test below compares Firely's `Predicate` against
`IgnixaEngine.IsTrue` - the harness's `IgnixaEngine` wrapper exposes no `Predicate` at all, so the
two real Ignixa `Predicate` methods were never in the comparison. Both Firely behaviours are pinned
by tests.

### 8. Boundary functions carry timezone extremes in Ignixa

`birthDate.lowBoundary()` gives Firely `1974-12-25T00:00:00` and Ignixa
`1974-12-25T00:00:00.000+14:00`. Ignixa's form is arguably more correct - a boundary is only a real
instant once the timezone extreme is applied - but it is a different string.
Also note Ignixa returns these as a raw `System.String` value rather than a temporal type.
No provider action while unreachable; revisit if boundary functions ever enter a search parameter.

### 9. Decimal scale differs in boundary and quantity arithmetic

`1.5.lowBoundary()` gives Firely `1.45` and Ignixa `1.45000000`; `2.0 'cm' * 2.0 'm'` gives
`0.04000000 'm2'` and `0.040000 'm2'`. Numerically equal, different scale. Cosmetic at the SQL layer,
which normalises decimal scale on storage.

### 10. Time plus a quantity

`@T10:30:00 + 1 hour`: Firely throws `InvalidOperationException`; **Ignixa returns `11:30:00`, which
is spec-correct** ([FHIRPath N1, Operations > Math](http://hl7.org/fhirpath/N1/#math)). Ignixa is
more capable. Keep.

### 11. Unary minus applied to a path

`- multipleBirthInteger`: Firely returns empty, **Ignixa returns `-2`, which is spec-correct**
(polarity applies to a single-item numeric collection). Ignixa is more capable. Keep.

### 12. Type-suffixed choice element names

`deceasedBoolean`: Firely returns empty, Ignixa resolves it.
FHIR requires polymorphic elements be addressed via `ofType()`, not the type-suffixed name
([FHIR R4 FHIRPath, Polymorphism](https://hl7.org/fhir/R4/fhirpath.html#polymorphism)), so **Firely
is spec-correct and Ignixa is more permissive.** Ignixa accepts a non-conformant path; it never
rejects a conformant one, so nothing breaks. No shipped R4 parameter uses the suffixed form.

> Checked and cleared: 13 R4 parameters use `ActivityDefinition.effectivePeriod` and similar. Those
> are genuine element names, not choice suffixes, and both engines resolve them identically.

### 13. Collection equivalence (`~`) on duplicate items — decided divergence from HAPI

`[a,a,b] ~ [a,b,b]`: **HAPI returns `true`; Ignixa returns `false`.**

HAPI's `opEquivalent` (`FHIRPathEngine.java:2496-2517`) checks collection size plus, for each left
item, whether *some* right item is equivalent to it — a matched right item is never consumed, so
one right item can satisfy more than one left item. Ignixa's `AreCollectionsEquivalent` instead
computes a genuine maximum bipartite matching (Kuhn's algorithm), which requires every right item to
pair with at most one left item. For `[a,a,b] ~ [a,b,b]`, both left `a`s compete for the single
right `a`; no perfect matching exists, so Ignixa answers `false` where HAPI's unconsumed existence
check answers `true`.

The FHIRPath spec (build.fhir.org continuous build, `index.md` lines 3499-3503: same size, "each
item must be equivalent", order-independent) is silent on duplicate multiplicity — it does not say
whether duplicates must pair off one-to-one or merely each find some equivalent partner. Tier 1
(spec) does not settle this; Tier 2 (HAPI) would normally decide it, but see the decision below.

**Reachable: no.** Zero `~` characters appear in any expression across all five generated
`*SearchParameterDefinitions.g.cs` files (STU3, R4, R4B, R5, R6) — verified by grep. No case in the
vendored official FHIRPath suite (`test/Ignixa.FhirPath.Tests/TestData/fhir-test-cases/{r4,r4b,r5}/fhirpath/`)
distinguishes the two semantics either: every `~`/`!~` collection case there (`testEquivalent19`
through `testEquivalent24`(R5) /`testNotEquivalent19`-`21`) compares distinct items, only ever
reordered, never duplicated within one operand. Neither the production surface nor the conformance
suite can tell these two implementations apart.

> **DECIDED 2026-08-21 (user signoff): keep the Kuhn matching.** This is the one place this
> inventory deviates from HAPI where the spec is ambiguous, so the reasoning is recorded rather than
> merely asserted: HAPI's `opEquivalent` reads as an implementation shortcut rather than a considered
> reading of the ambiguity; the inputs that distinguish the two semantics are unreachable from any
> shipped search parameter; and the matching's order-independence is guaranteed by construction where
> an existence check's is not. Downgrading a verified-correct multiset semantics to an existence
> check to match an implementation detail buys nothing and loses that guarantee. If HL7 clarifies the
> spec text, revisit — the trigger is the same spec lines cited above.
>
> Pinned by
> `GivenALeftDuplicateNotConsumedByASingleRightMatch_WhenComparedForEquivalence_ThenIgnixaDivergesFromHapiAndReturnsFalse`
> in `test/Ignixa.FhirPath.Tests/Evaluation/CollectionEquivalenceTests.cs`.

---

## Expectations tested

The brief predicted four findings. Two were confirmed, two refuted.

| Prediction | Verdict |
|---|---|
| `%resource`/`%rootResource` - Ignixa less capable, no parent link on `IElement` | **Refuted.** Both engines agree on `%resource`, `%rootResource` and `%context`, including from inside a Bundle entry (`Bundle.entry.resource.select(%resource.id)`). The underlying premise is true - `IElement` has no parent link, so nothing can *infer* these - but the evaluation-context bridge binds them explicitly and the observable result is identical. **This makes the binding load-bearing rather than redundant**, which is why it has its own test. |
| ~12 constructs signal errors where Firely returns empty | **Refuted in direction.** The asymmetry mostly runs the other way: Firely throws where Ignixa answers (`@T10:30:00 + 1 hour`) or returns empty where Ignixa answers (`- multipleBirthInteger`, `deceasedBoolean`). Only `hasExtension` has Ignixa erroring where Firely does not - and Firely does not return empty there either, it throws earlier. |
| `as` filters element-wise below R5; at R5+ Ignixa throws where Firely does not | **Not covered** - see below. At R4, `name as HumanName`, `name.as(HumanName)` and `name.ofType(HumanName)` all agree. |
| Ignixa's `Scalar`/`IsTrue`/`IsBoolean` differ from Firely's | **Confirmed for `Scalar`** (entry 7). `IsTrue` agrees. `Predicate` is worse than absent - Ignixa ships two same-named methods with different, mutually disagreeing empty/multi-item contracts, neither matching Firely's empty-collection-is-true rule (see entry 7). ADR 2608 derives `Predicate` in the seam because Firely's is `internal`; the divergence is the reason that derivation cannot instead delegate to either Ignixa copy. |

---

## Not covered by the original R4 inventory

The resource-backed follow-up linked above now covers all five versions, populated resources, and
present/absent/contained `resolve()` targets. The following limitations describe only the original
expression-focused harness or remain future work:

- **Custom search parameters.** ADR 2608 already scopes these to bake-in observation rather than a
  pre-merge gate.
- **Firely 6.0.1 vs 5.11.4 as its own axis.** Both are available offline; a second project could
  report where the two Firely versions disagree, which is directly useful when fhir-server upgrades.

---

## Keeping this current

`KnownDivergences.cs` pins every entry above by expression and outcome shape, with a count of how
many subject resources reach it. A new divergence fails
`GivenTheShippedSearchParameterCorpus_...` or `GivenTheChangedConstructs_...` rather than quietly
lengthening this document, and the failure message emits a paste-ready replacement block.

Run the harness:

```
dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj --filter "FullyQualifiedName~FirelyVersusIgnixa"
```

Full reports are written to the test output directory as `firely-parity-searchparam.md` and
`firely-parity-construct.md`.
