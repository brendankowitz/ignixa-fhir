# Search Query Conformance Coverage

**Date:** 2026-07-21
**Status:** Approved design
**Scope:** Portable FHIR 4.0.1, 4.3.0, and 5.0.0 P0/P1 search-query coverage in `Ignixa.TestScript.Suites`

## Problem

The consolidated TestScript corpus has broad single-feature search coverage, but it does not cover
several portable query shapes observed in production workloads:

- deterministic `_lastUpdated` boundaries and ranges;
- bounded history paging followed through the server-provided `next` link;
- `_elements` and `_summary` projections;
- realistic combinations of ordinary, chained, control, sort, and count parameters; and
- one request that combines direct includes with multiple iterate branches.

The gap is conformance evidence, not a request to change search behavior. The implementation adds or
extends TestScripts. The focused opaque-URL regression passed against existing behavior, so no
TestScript engine, model, parser, or evaluator production change is required.

## Decision

Use focused, additive suite files:

| File | Change |
|---|---|
| `Search/last-updated.json` | Add a POST/server-assigned-id lifecycle with deterministic lower-bound, upper-bound, bounded-range, and contradictory-range searches. |
| `CRUD/history.json` | Extend the existing history graph to at least three versions and add `_count=1`, `Bundle.type`, and opaque next-link traversal coverage. |
| `Search/projection.json` | Add `_elements` and `_summary` response-shape coverage. |
| `Search/query-composition.json` | Add a realistic DiagnosticReport query with decoys and independently useful parameter combinations. |
| `Search/includes.json` | Reuse the existing graph for separate iterate-branch visibility and direct-plus-iterated deduplication queries. |

All new test objects carry test-level `fhirVersions` applicability for `4.0,4.3,5.0`. Capability
gates remain test-local except for suite lifecycle requirements. Query composition uses one existing
pre-setup suite `requiresCapability` expression that combines exact
`CapabilityStatement.fhirVersion` prefixes with its lifecycle requirements; it does not add a root
`fhirVersions` extension. A broad server-level feature gate that hides partial search implementations
is rejected.

### Alternatives rejected

1. **One large search-workload script.** It would reduce fixture duplication but make a single
   unsupported capability skip unrelated evidence and make failures hard to attribute.
2. **Copy implementation-specific regression suites.** That would preserve vendor behavior rather
   than test the portable FHIR contract.
3. **Change the engine while authoring the suites.** The opaque-URL regression passed: existing
   variable substitution already returns an operation URL verbatim, so production evaluator changes
   are unnecessary.

## Assertion policy

### Hard assertions

An assertion is hard when all of the following are true:

1. the behavior is required by the applicable FHIR release or by an explicitly declared target
   profile;
2. the target CapabilityStatement advertises the exact interaction and every capability that is both
   representable and intentionally gated. Search result parameters are never gated on — history
   `_count`, projection `_elements`/`_summary`, include `_total`, and query-composition `_sort`
   and `_count` alike, per gating rule 5; and
3. fixtures make the expected result deterministic without relying on server-global data, wall-clock
   timing, or an implementation-specific ordering tie-breaker.

Hard failures contribute to conformance failure.

For PUT fixture validation in projection and query composition, each acceptable exact status is a
`warningOnly` member of an `assertionAnyOfGroup`. Member warnings represent alternatives rather than
advisory conformance: the group outcome is hard and fails when none of its members match.

### Warning-only assertions

Use `warningOnly` when base FHIR permits variation, CapabilityStatement has no precise declaration
for the behavior, or the check records an implementation/workload expectation rather than portable
conformance. Specifically:

- repeated occurrences of the same search parameter are warning-only unless a target profile
  explicitly fixes the combination semantics;
- `_include:iterate` hop assertions and deduplication assertions that depend on an iterated path are
  warning-only unless a target profile explicitly requires iterate support;
- conditional SUBSETTED implications are warning-only because the applicable normative language says
  servers SHOULD add the tag when content is omitted;
- supported history sort ordering and `_summary=count` response behavior remain hard, while
  unsupported or server-specific rejection expectations such as `_sort=_id` remain warning-only; and
- repeated-range query-composition HTTP 200 and Bundle shape remain hard; only its repeated-key
  match-set, membership, criteria, exclusion, and order conclusions are warning-only.

Do not weaken a portable response status, Bundle type, fixture identity, page-size maximum, or
declared include-mode assertion merely because one server currently fails it.

## Fixture isolation and determinism

- Every fixture uses an identifier system under `http://ignixa.io/testscript/suite/` and values unique
  to its suite.
- Searches include the suite identifier whenever the query shape allows it. Tests never infer their
  expected count from all resources of a type on the server.
- Stable client-assigned ids are used where update-create is already the suite convention.
- `_lastUpdated` checks use fixed broad bounds, not timestamps captured during the run:
  `ge2000`, `lt2000`, `ge2000` with `lt2999`, and `ge2000` with `lt2000`.
- Sort assertions use fixture values that are unique for the chosen sort parameter. No assertion
  depends on unspecified ordering between equal values.
- Teardown removes every persistent fixture created by the new coverage.

## Suite design

### 1. `Search/last-updated.json`

POST one suite-scoped Patient, capture its server-assigned id as `responseId`, and delete
`Patient/${responseId}` during teardown. The suite gate requires Patient `create` and `delete`.
Search by its identifier plus `_lastUpdated` using four deterministic query shapes:

| Query shape | Expected result |
|---|---|
| `identifier=http://ignixa.io/testscript/suite/last-updated\|LAST-UPDATED-PAT1&_lastUpdated=ge2000` | The fixture is present. |
| `identifier=http://ignixa.io/testscript/suite/last-updated\|LAST-UPDATED-PAT1&_lastUpdated=lt2000` | The fixture is absent and no unrelated resource is admitted. |
| `identifier=http://ignixa.io/testscript/suite/last-updated\|LAST-UPDATED-PAT1&_lastUpdated=ge2000&_lastUpdated=lt2999` | One entry has both the captured fixture id and the suite identifier inside the broad range. |
| `identifier=http://ignixa.io/testscript/suite/last-updated\|LAST-UPDATED-PAT1&_lastUpdated=ge2000&_lastUpdated=lt2000` | No match is returned for the contradictory range. |

Each search uses an exact `responseCode` assertion for HTTP 200 and requires a `searchset` Bundle.
Positive membership assertions correlate the resource id and suite identifier on the same entry so
an unrelated Patient cannot satisfy the test accidentally. The single-bound
match-set assertions are hard; the repeated-key match-set assertions follow the warning-only policy
below.

The two repeated-key cases also record the server's repeated-parameter behavior. Their result
assertions are warning-only on an unprofiled target because base search parameter repetition can
vary by parameter definition and server declaration. If a future conformance profile explicitly
requires AND semantics for repeated `_lastUpdated`, gate on that profile and promote those result
assertions to hard without changing the workload.

The test-level capability gate requires Patient search plus Patient `identifier` and
`_lastUpdated` search parameters. It must not gate on unrelated date parameters or on a generic
"search supported" predicate.

### 2. Extend `CRUD/history.json`

Retain the existing system/type/instance history, sort, response-status, `_since`, `_before`, and
summary tests. Extend the instance-history fixture used for portable paging so it has at least three
versions, created by a create followed by two actual content changes.

Add a portable paging flow:

1. request `Patient/${id}/_history?_count=1`;
2. assert exact `responseCode` 200, resource type `Bundle`, `Bundle.type = 'history'`, and
   `entry.count() <= 1`;
3. assert a `link` with `relation = 'next'` exists because at least three versions exist;
4. extract `link.where(relation = 'next').url` into a TestScript variable;
5. extract the first page's `entry.first().resource.meta.versionId` as `histAFirstVersionId`;
6. hard-assert that page 1 contains an entry;
7. use the extracted next-link value as the entire URL of the next operation, without parsing, decoding,
   appending, or reconstructing it; and
8. hard-assert that the followed page has exact `responseCode` 200, is a non-empty history Bundle, respects the
   one-entry maximum, and has a first-entry version id different from `histAFirstVersionId`.

The next-link URL is opaque. Following it is not continuation-token coverage: the suite never
inspects or asserts the query keys embedded in that URL. This distinction keeps the standard paging
contract in scope while leaving proprietary continuation-token formats out of scope.
History `_count` is a control parameter, so the paging test gates on `history-instance` plus the
`patch` interaction its fixture versions depend on, and does not require `_count` as an advertised
`searchParam`. The extra versions are built with FHIRPath Patch rather than PUT because the Patient
is created with a server-assigned id, which a static fixture body cannot be made to match. They live
in `setup` and are deliberately unasserted. When the setup phase fails — through a failed assertion
*or* an operation that throws — every test in the file is abandoned, and `ConformanceReportMapper`
republishes them all under the setup phase's own status, so they reach the matrix as failures rather
than as skips. Asserting the patches would hand a server that rejects PATCH the power to take down
the six tests that never patch. A patch accepted but not applied is still caught, by the hard
version-count, next-link and sort-order assertions in the four tests that consume the versions.
Setup asserts `id.exists()` on the create because a server may legitimately answer 201 with an empty
body, which would leave `histAId` unset and make the following patch throw on URL resolution.

The focused evaluator regression exposes an absolute next URL through variable extraction and uses
`${nextUrl}` as the complete second `operation.url`. It proves the request provider receives that URL
unchanged. The regression passed, so production evaluator code remains unchanged. The stale note in `Search/pagination.json`
claiming next-link traversal needs an engine enhancement is superseded by this design and has been
corrected to point at the history suite instead.

The supported ascending/descending history sort assertions and `_summary=count` total/no-entry
assertions remain hard. Only unsupported or server-specific rejection expectations, including
`_sort=_id` and the far-future `_before` rejection, remain warning-only.

### 3. `Search/projection.json`

Use the fixed Patient id `ignixa-projection-pat1`, with `active`, `name`, `gender`, `communication`,
and a narrative to distinguish each projection. Setup PUTs the fixture to
`Patient/ignixa-projection-pat1` using update-create, teardown deletes that URL, and the suite-level
capability gate requires Patient `updateCreate` plus `delete`.

Setup captures the PUT as `setup-response` and contains no assertions. Before the five projection
queries, a dedicated first test gated to `fhirVersions` `4.0,4.3,5.0` validates that source with one
hard-outcome `assertionAnyOfGroup`. Its two `warningOnly` members are correlated by `sourceId` and
accept only exact `responseCode` `200` or exact `responseCode` `201`; there is no broad `okay`, `202`,
or `204` alternative. Alternative groups are parser-supported in test actions rather than setup, so
this placement requires no production parser/evaluator or other engine change.

| Query | Hard portable checks |
|---|---|
| `_id=ignixa-projection-pat1&_elements=active` | Exact HTTP 200 searchset; the fixed Patient id and requested `active=true` are present. |
| `_id=ignixa-projection-pat1&_summary=text` | Exact HTTP 200 searchset; the fixed Patient and narrative are present. |
| `_id=ignixa-projection-pat1&_summary=data` | Exact HTTP 200 searchset; the fixed Patient and fixture data fields are present. |
| `_id=ignixa-projection-pat1&_summary=true` | Exact HTTP 200 searchset; the fixed Patient and known Patient summary fields are present. |
| `_id=ignixa-projection-pat1&_summary=count&_lastUpdated=ge2000` | Exact HTTP 200 searchset; `total=1`, no match-mode entries exist, and every entry that is present is an `OperationOutcome` with `search.mode='outcome'`. |

Every projection search is isolated with `_id=ignixa-projection-pat1`; the identifier element is
fixture data, not the search-scoping mechanism. Requested and mandatory fields remain hard.
Absence of unrequested, ordinary, narrative, or non-summary fields is warning-only because projection
controls permit servers to return extra content.

When a response omits ordinary resource content, the suite records the exact SUBSETTED implication
using:

`http://terminology.hl7.org/CodeSystem/v3-ObservationValue|SUBSETTED`

Each implication is expressed as “the probed field is present, or the exact SUBSETTED tag exists.”
Every such conditional is warning-only, not hard, because the applicable normative language is
SHOULD. The system and code remain exact even though assertion strength is advisory.

The first four projection-query test-level gates require only Patient `search-type`. `_elements` and `_summary` are
search controls, not advertised `searchParam` entries. The count composition additionally requires
resource- or system-level `_lastUpdated` advertisement because `_lastUpdated` is an ordinary common
search parameter.

### 4. `Search/query-composition.json`

Create Practitioner and DiagnosticReport fixtures under a unique identifier system:

- two reports satisfy every filter and have distinct `issued` values;
- one decoy fails `status`;
- one decoy fails `code`;
- one decoy references a Practitioner whose identifier does not match; and
- all fixtures otherwise resemble the matching reports closely enough that each predicate is
  independently necessary.

Setup contains exactly seven PUT operations with unique `responseId` values and no assertions. The
first test phase, gated to `fhirVersions` `4.0,4.3,5.0`, validates those captured setup responses
before either query test runs. Each response has its own `assertionAnyOfGroup` with exactly two
`warningOnly` member assertions correlated by `sourceId`: exact `responseCode` `200` and exact
`responseCode` `201`. A group fails hard if neither member matches; `202`, `204`, and broad `okay`
alternatives are not accepted. This proves both matches and every decoy were established before
search assertions evaluate membership or exclusion.

The validation is a test phase because parser-supported alternative assertion groups are available
in test actions, not setup actions. This placement uses existing parser/evaluator behavior and
requires no production parser or evaluator change.

The hard, single-bound query is:

```text
DiagnosticReport
  ?status=final
  &code=http://loinc.org|24323-8
  &results-interpreter:Practitioner.identifier=http://ignixa.io/testscript/suite/query-composition|QUERY-PRAC-TARGET
  &_lastUpdated=ge2000
  &_sort=issued
  &_count=10
```

It must assert HTTP 200, `Bundle.type = 'searchset'`, exactly the two expected match entries, both
entries with `search.mode = 'match'`, non-vacuous status/code/Practitioner-reference criteria, their
order by the unique `issued` values, and exclusion of every decoy. The operation asserts the exact
HTTP status code `200`. `_count=10` is present as part of the composed workload, not as pagination
coverage: the decoys are excluded by the query itself, so membership and exclusion hold at any page
size. Standard next-link traversal remains covered by history.

The hard test runs only when the CapabilityStatement narrowly declares DiagnosticReport search,
`status`, `code`, `results-interpreter`, `issued`, and `_lastUpdated`, plus
Practitioner `identifier` for the typed chain. Per gating rule 5 it does not gate on `_sort` or
`_count`. The typed chain spelling remains exactly
`results-interpreter:Practitioner.identifier` in all three target releases.

Add a workload variant that repeats `_lastUpdated` with `ge2000` and `lt2999` while retaining the
other predicates. Its exact HTTP 200 and response-shape checks stay hard, but all repeated-range
match-set, membership, criteria, exclusion, and order expectations are warning-only unless a profile
gate fixes repeated-parameter semantics.

Before setup, the suite uses the existing `requiresCapability` evaluator with this exact gate:

```text
(fhirVersion.startsWith('4.0') or fhirVersion.startsWith('4.3') or fhirVersion.startsWith('5.0'))
and rest.resource.where(type='Practitioner').updateCreate = true
and rest.resource.where(type='Practitioner').interaction.where(code='delete').exists()
and rest.resource.where(type='DiagnosticReport').updateCreate = true
and rest.resource.where(type='DiagnosticReport').interaction.where(code='delete').exists()
```

This is one root `requiresCapability` expression over `CapabilityStatement.fhirVersion` and lifecycle
support, not a root `fhirVersions` extension. Together with the parser-supported first test phase,
it requires no engine/model/parser/evaluator change.

### 5. Extend `Search/includes.json`

Reuse the existing Observation -> Patient -> Organization/Practitioner graph in two requests. The
first request isolates iterate-branch visibility by deliberately omitting the direct performer
include:

```text
Observation
  ?_id=ignixa-inc-obs1
  &_include=Observation:subject
  &_include:iterate=Patient:organization
  &_include:iterate=Patient:general-practitioner
  &_total=accurate
```

Its HTTP status, searchset type, Observation match, direct Patient subject include, and
`Bundle.total = 1` are hard behind the exact search and subject-include gates. `_total=accurate`
remains in the request, but `_total` is a control parameter and does not require CapabilityStatement
`searchParam` advertisement. Organization and Practitioner presence with `search.mode = 'include'`
records visibility through the two Patient iterate branches, but those branch-dependent assertions
are warning-only without an explicit profile requiring the hops.

The second request combines the direct subject and performer includes with both iterate branches:

```text
Observation
  ?_id=ignixa-inc-obs1
  &_include=Observation:subject
  &_include=Observation:performer
  &_include:iterate=Patient:organization
  &_include:iterate=Patient:general-practitioner
  &_total=accurate
```

The direct include and total assertions are hard when the CapabilityStatement declares the exact
Observation subject/performer includes:

- the Observation appears once with `search.mode = 'match'`;
- the Patient, Organization, and Practitioner appear with `search.mode = 'include'`;
- `Bundle.total = 1`, excluding all included resources; and
- direct-include resources are not duplicated independently of any iterate-path assumption.

Organization and Practitioner presence in this second response does not prove either iterate branch
executed because the direct performer include already reaches the same resources. The assertion that
each logical resource appears at most once across the direct and potentially iterated paths is
therefore warning-only without an explicit profile requiring the hops. A profile that requires both
branches may promote the branch-visibility and cross-path deduplication assertions to hard. Existing
single-hop warning-only iterate tests remain in place. The existing graph is sufficient because the
first request makes its targets branch-only by query shape; fixture semantics do not need to change.

## Capability-gate rules

1. Gate at the smallest test scope that can express the requirement.
2. Require the resource-level interaction and exact ordinary search-parameter codes used by the
   request. Never a search result parameter — see rule 5.
3. Do not require CapabilityStatement `searchParam` advertisement for history `_count`, projection
   `_elements`/`_summary`, or include `_total`; these are the specific cases rule 5 generalises.
4. Projection `_summary=count&_lastUpdated=ge2000` still requires resource- or system-level
   `_lastUpdated` advertisement; the exception applies to `_summary`, not the ordinary common
   parameter composed with it.
5. Never gate on `_sort`, `_count`, `_total`, `_summary`, `_elements`, `_include`, `_revinclude`,
   or `_contained` as advertised `searchParam` codes. These are search result parameters, not
   `SearchParameter` resources, so no conformant server declares them and such a gate is
   unsatisfiable against any server — it silently disables the test rather than skipping it for a
   real capability gap. Rule 3 is a narrower restatement of this for three specific suites. It is
   stated separately because an earlier revision of this design made the opposite call for
   query-composition and left that suite inert. `ConformanceSuiteExtensionGuardTests` enforces
   this list; keep the two in step. For includes, the correct element is `searchInclude` /
   `searchRevInclude`, not `searchParam`.
6. For includes, accept the exact advertised include or wildcard only where the existing suite
   already treats wildcard as satisfying that declaration.
7. Do not infer repeated-key semantics or iterate support from generic search support.
8. Do not use Ignixa implementation identity as a capability gate.
9. Use a profile canonical only when that profile actually states the stronger behavior. No new
   profile is invented by this effort.
10. A missing CapabilityStatement remains subject to the engine's existing fail-open policy; this
   design does not change evaluator semantics.

## Explicit exclusions

The following observed patterns are not portable P0/P1 coverage and must not enter these files:

- `_source`;
- Microsoft bulk delete operations;
- proprietary continuation-token parameters or token contents;
- malformed telemetry keys;
- legacy or vendor-specific custom parameter spellings.

The history next-link test is still in scope because it treats `Bundle.link.url` as opaque and uses
only the standard `relation = 'next'` contract.

## Verification strategy

Implementation uses the existing TestScript engine:

1. **Static suite checks:** parse every changed JSON TestScript and run repository guards for allowed
   extension URLs, supported FHIR-version declarations, and gates that name a search result parameter
   and so can never be satisfied (rule 5). If worktree execution exposes repository
   root discovery as the blocker, use the proven-minimal TDD change only: focused `RepoRootTests`
   cover `.git` as a file and as a directory, and `RepoRoot` accepts either marker.
2. **Engine unit boundary:** the absolute variable-extracted URL regression passes; keep evaluator and
   HTTP-provider production code unchanged.
3. **Targeted conformance execution:** run the five affected scripts against each available R4,
   R4B, and R5 test target. Hard assertions must pass; warning-only observations must be reported
   without failing the run.
4. **Project tests:** run the focused `Ignixa.TestScript.Tests` and suite guard projects.
5. **Layered integration:** run the API E2E conformance slice, then the full existing conformance
   matrix only after targeted runs are clean.

No implementation is accepted on JSON parsing alone. Conversely, a warning-only workload mismatch
does not justify an engine change without an independently reproducible correctness defect.

## Traceability

| Priority | Portable observed pattern | Existing coverage | Planned coverage and assertion strength |
|---|---|---|---|
| P0 | `_lastUpdated` broad lower bound | Date parameters exist, but no dedicated common-parameter suite. | `Search/last-updated.json`: POST fixture captured by server-assigned id and included; hard behind exact gate. |
| P0 | `_lastUpdated` broad upper bound | None dedicated. | `Search/last-updated.json`: POST fixture excluded from match entries; hard behind exact gate. |
| P0 | `_lastUpdated` bounded range | None dedicated. | New file: broad `ge2000` + `lt2999`; the same-entry captured-id-plus-suite-identifier membership expression is warning-only unless profile-gated because the key repeats. |
| P0 | Contradictory `_lastUpdated` range | None dedicated. | New file: `ge2000` + `lt2000`; empty-result expectation warning-only unless profile-gated. |
| P0 | History page-size maximum with `_count=1` | `CRUD/history.json` tests history contents but not bounded paging. | Extend history; `entry.count() <= 1` hard. |
| P0 | History Bundle type | Existing tests assert only resource type `Bundle`. | Extend history; `Bundle.type = 'history'` hard. |
| P0 | Follow opaque history `next` link | `Search/pagination.json` explicitly does not follow links. | Extend history; extract and follow unchanged; hard after focused engine-unit proof. |
| P0 | Non-empty, distinct history pages | Existing history coverage does not prove the followed page advances. | Extract `histAFirstVersionId`; hard-assert `entry.exists()` on both pages and a different first version id on page 2. |
| P0 | `_elements=active` projection | None dedicated. | `Search/projection.json`; setup response is hard-validated first by one source-correlated `assertionAnyOfGroup` containing only exact warning-only 200/201 members; fixed `_id` scope and requested/mandatory fields hard; unrequested-field absence warning-only. |
| P0 | `_summary=text` | History has `_summary=count` only. | New projection file; fixed `_id` scope and narrative presence hard; ordinary-field absence warning-only. |
| P0 | `_summary=data` | None dedicated. | New projection file; fixed `_id` scope and data presence hard; narrative absence warning-only. |
| P0 | `_summary=true` | None dedicated. | New projection file; fixed `_id` scope and summary-field presence hard; non-summary-field absence warning-only. |
| P0 | `_summary=count` combined with `_lastUpdated` | History count is not a resource search and is not suite-scoped. | New projection file; exact HTTP 200, `total=1`, no match entries, and only outcome-mode `OperationOutcome` entries hard behind Patient search plus resource- or system-level `_lastUpdated` advertisement. |
| P0 | Multi-predicate typed-chain query with sort and count | Features are covered separately across search scripts. | `Search/query-composition.json`; seven unique setup response ids are validated in the first test by seven hard-outcome `assertionAnyOfGroup` groups, each containing only exact warning-only 200/201 alternatives correlated by `sourceId`; `_count=10` single-bound composition returns both uniquely sorted matches and excludes every decoy behind narrow hard gates; count is workload composition, not paging coverage. |
| P0 | Direct subject and performer includes in one request | Direct includes are covered separately. | Extend includes with the second query; direct resources, modes, direct-path deduplication, and accurate total hard. |
| P1 | Repeated same-parameter range behavior | Interval/date suites exercise values, not this common-parameter workload. | Last-updated and composition range variants; match-set expectation warning-only unless profile-gated. |
| P1 | Conditional SUBSETTED tagging | None dedicated. | Projection file; exact system/code implications are warning-only because the normative requirement is SHOULD. |
| P1 | Two `_include:iterate` branches in one request | Existing includes has single iterate branches, warning-only. | Branch-visibility query omits direct performer include; hop-specific assertions warning-only without profile. |
| P1 | Deduplication across direct and iterated paths | Self-reference and direct dedup cases exist, but not cross-path branches. | Second direct-plus-iterated query; direct-path dedup hard, cross-path dedup warning-only without profile. |
| P1 | History sort and summary behavior | Existing `CRUD/history.json` coverage. | Keep supported ascending/descending sort and `_summary=count` behavior hard; keep only unsupported/server-specific rejection expectations such as `_sort=_id` warning-only. |

## Normative references

- FHIR R4 search: <https://hl7.org/fhir/R4/search.html>
- FHIR R4B search: <https://hl7.org/fhir/R4B/search.html>
- FHIR R5 search: <https://hl7.org/fhir/R5/search.html>
- FHIR R4 REST history: <https://hl7.org/fhir/R4/http.html#history>
- FHIR R4B REST history: <https://hl7.org/fhir/R4B/http.html#history>
- FHIR R5 REST history: <https://hl7.org/fhir/R5/http.html#history>
- FHIR R4 Bundle: <https://hl7.org/fhir/R4/bundle.html>
- FHIR R4B Bundle: <https://hl7.org/fhir/R4B/bundle.html>
- FHIR R5 Bundle: <https://hl7.org/fhir/R5/bundle.html>
- DiagnosticReport search parameters:
  [R4](https://hl7.org/fhir/R4/diagnosticreport.html#search),
  [R4B](https://hl7.org/fhir/R4B/diagnosticreport.html#search), and
  [R5](https://hl7.org/fhir/R5/diagnosticreport-search.html)

## Completion criteria

- Changes are confined to the two approved docs, seven suite files, and three test files; evaluator,
  model, and parser production files remain unchanged. The seven suites are the five named above plus
  two touched by rule-5 remediation: `Search/chaining-and-sort.json`, whose `_sort`, `_summary` and
  `_total` gates had left three tests inert, and `Search/pagination.json`, whose note claiming
  next-link traversal needed an engine enhancement this effort disproved. The three test files are
  the focused `VariableExtractorTests.cs` regression, the `RepoRoot.cs`/`RepoRootTests.cs` worktree
  guard, and `ConformanceSuiteExtensionGuardTests.cs`, which gained the rule-5 enforcement.
- Every new hard assertion is deterministic and narrowly capability-gated.
- Every warning-only assertion states why it is not portable hard conformance.
- All five explicit exclusion groups remain absent.
- The traceability table has no unmapped portable P0/P1 pattern.
- No placeholder, custom legacy spelling, proprietary token assertion, or implementation-specific
  success criterion remains.
