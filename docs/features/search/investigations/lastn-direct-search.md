# Investigation: Direct $lastn Search

**Feature**: search
**Status**: Rejected
**Created**: 2026-08-28

## Problem Statement

FHIR `$lastn` returns the most recent N Observations in each equivalent
`Observation.code` group. It is not an ordinary search with `_sort` and `_count`:
the candidate set is filtered using normal Observation search parameters, then a
grouping and tie-inclusive per-group limit are applied.

Ignixa should implement this behavior in `Ignixa.Search` and
`Ignixa.Search.Sql`, without adding another special query path to
`Ignixa.DataLayer.SqlEntityFramework`.

## Approach

### 1. Model `$lastn` separately from ordinary `SearchOptions`

Add an operation-specific model in `Ignixa.Search`, for example:

```csharp
public sealed record LastNSearchOptions(
    SearchOptions Filters,
    int Maximum,
    SearchParameterInfo CodeParameter,
    SearchParameterInfo EffectiveDateParameter);
```

A dedicated builder should:

1. Extract `max` before invoking `SearchOptionsBuilder`, because `max` is an
   operation parameter rather than an Observation search parameter.
2. Default `max` to `1` and reject zero, negative, malformed, or
   server-disallowed values.
3. Build the remaining parameters through the existing Observation search
   parser, preserving all supported predicates, modifiers, access constraints,
   and lenient/strict handling.
4. Resolve the version-specific `Observation.code` and `Observation.date`
   definitions through `ISearchParameterDefinitionManager`.
5. Validate the operation's required subject and code/category inputs.

This should be a separate type rather than optional fields on `SearchOptions`.
An old or unsupported search backend must not be able to receive `$lastn`
options, ignore the grouping fields, and silently execute an ordinary search.

R4, R4B, and R5 require a `patient` or `subject` parameter and either `category`
or a search parameter whose FHIRPath contains a code element. The current
`SearchParameterInfo` records the broad search type but not the FHIR element
types returned by its expression. Exact validation therefore needs one of:

- generated `ContainsCodeElement` metadata on `SearchParameterInfo`; or
- a version-aware classifier that evaluates the parameter expression against
  the schema provider.

Name-based tests such as `code.StartsWith("code")` are not sufficient for custom
search parameters.

### 2. Add a terminal `$lastn` shape to `Ignixa.Search.Sql`

Add a compiler entry point that accepts `LastNSearchOptions`. It should compile
`Filters` through the existing Resolve -> Lower pipeline, then attach a
resolved `LastNSpec` containing:

- the Observation resource type id;
- the code search parameter id;
- the effective-date search parameter id; and
- the validated per-group maximum.

The lowered plan should expose `$lastn` as a closed terminal result shape,
parallel to matches, counts, and includes pages. It should not be represented
as:

- an `Expression`, because expressions describe membership in the candidate
  set and `$lastn` transforms that set after filtering; or
- `SearchPaging`, because `max` applies independently to every code group and
  is not a page size.

`QueryPlanValidator` must validate plans built directly, while `Lower` retains
equivalent input-level validation with caller-facing messages. This preserves
the compiler's existing public-plan invariant.

### 3. Emit a post-filter grouping and ranking pipeline

The normal match CTE remains the authoritative candidate set. Authorization,
tenant scoping, resource visibility, `_lastUpdated`, and all ordinary
Observation filters therefore run before `$lastn` shaping.

The emitted SQL should conceptually add these stages:

```sql
candidate
  -> coded_membership
  -> equivalent_code_components
  -> canonical_code_group
  -> effective_time
  -> ranked
  -> final result
```

The final ranking must use:

```sql
RANK() OVER (
    PARTITION BY CodeGroup
    ORDER BY EffectiveStart DESC
)
```

and retain rows where `rank <= @max`.

`ROW_NUMBER()` is incorrect because it truncates Observations tied at the
boundary. `DENSE_RANK()` is also incorrect: a tie before the boundary can cause
it to return another distinct effective time beyond the first N sorted
positions. `RANK()` implements "top N, including every Observation tied with
the Nth result."

The outer result may add the canonical group key and surrogate id as
deterministic ordering keys. FHIR does not define the order of equivalent code
groups, so choosing a stable order is permitted. The group key must not be part
of the window's effective-time rank.

### 4. Implement exact code equivalence

FHIR grouping is more than `PARTITION BY SystemId, Code`. Multiple codings on
one `Observation.code` are translations, and equivalence is transitive:

```text
Observation A: [a]
Observation B: [a, b]
Observation C: [b, c]

One group: [a, b, c]
```

The current direct SQL schema contains the required raw information:

- `TokenSearchParam` has one row per coding, keyed by resource and search
  parameter.
- `TokenText` contains the fallback text.
- `DateTimeSearchParam` contains the effective range and sort flags.

For coded Observations, the first implementation should derive code-to-code
edges from codings that occur on the same candidate resource, compute their
transitive components, and select a stable canonical code for each component.
For a text-only `Observation.code`, use `TokenText` only when the resource has
no `TokenSearchParam` row for the code parameter. Apply a case-sensitive
collation because the specification treats differently-cased text as different
groups.

A shortcut that selects one coding per Observation or independently partitions
each coding is not compliant and can duplicate resources or split a transitive
group.

Query-time transitive closure is the principal performance risk. It is viable
as a compiler prototype because `$lastn` is subject-scoped and additionally
requires category or code intent, but it needs live SQL Server benchmarks. If
the closure is not bounded enough, the fallback is a materialized
code-equivalence mapping in `Ignixa.DataLayer.SqlServer.Database`, maintained
by the direct indexing path. It should not be implemented in the old EF search
service.

### 5. Keep initial result controls explicit

The specification does not define how ordinary result paging, `_include`, or
`_revinclude` compose with `$lastn`. The initial compiler shape should fail
loudly for:

- `_sort`, because `$lastn` owns the within-group ordering;
- `_count` and continuation tokens, until group-aware paging semantics are
  designed; and
- `_include` and `_revinclude`, until it is decided whether they seed from the
  post-group result and how their budgets are represented.

Other supported Observation filter parameters should continue to compose
through the ordinary search expression.

The returned `Bundle.total` should be the number of rows after grouping and
tie-inclusive limiting. Published examples use this interpretation, although
the formal operation definition does not separately define total or paging
semantics.

The handling of Observations without `effective[x]` remains an explicit design
decision. The current specification orders by effective time but does not
define a fallback. The implementation must not silently substitute
`meta.lastUpdated` or truncate all missing-effective ties without documenting
and testing that policy.

### 6. Keep production wiring out of the old EF layer

`Ignixa.Search.Sql` is currently an alpha compiler and is not registered in a
production search data layer. This investigation can make `$lastn` fully
representable and exhaustively tested in the direct compiler, but serving
`GET /Observation/$lastn` also requires:

- an Application query/handler and Minimal API route;
- capability advertisement for `Observation/$lastn`; and
- a direct `Ignixa.DataLayer.SqlServer` search adapter that executes
  `CompiledSearch` and materializes the returned resource ids.

Those surfaces should call the new typed compiler entry point. They should not
add `$lastn` branches to `SqlEntityFrameworkSearchService`.

## Tradeoffs

| Pros | Cons |
|------|------|
| Keeps operation semantics explicit and impossible for an old backend to silently ignore | Adds an operation-specific Search model and compiler shape |
| Reuses the existing parser, predicate CTE graph, authorization, and symbol resolution | Exact transitive code grouping is substantially harder than a simple SQL partition |
| `RANK()` preserves the specification's boundary ties | Tie expansion means the result can exceed `max` per group |
| Existing direct SQL tables contain code, text, and effective-date inputs | Query-time connected-component calculation may be expensive |
| Keeps all user values parameterized and emitted SQL deterministic | Group-aware paging and includes need separate designs |
| Avoids any new feature logic in the old EF search layer | The direct SQL compiler is not yet wired into production execution |

## Alignment

- [x] Follows architectural layering rules
- [x] Developer Experience (works with minimal setup)
- [x] Specification compliance (if applicable)
- [x] Consistent with existing patterns

## Evidence

### FHIR specification

| Version | Status and relevant behavior |
|---------|------------------------------|
| STU3 | Defines type-level `Observation/$lastn`, `max: positiveInt`, default 1, grouping by code, recent-first order, and an empty search set for no matches. Its narrative does not contain the later required-input and translation/tie detail. |
| R4 | Trial Use, FMM 3. Requires subject plus category or a code-bearing search parameter. Defines transitive code equivalence and returning every Observation tied at the `max` boundary. |
| R4B | Same formal contract and Trial Use/FMM 3 status as R4. |
| R5 | Same formal contract and Trial Use/FMM 3 status as R4/R4B. |
| Current build | Marks the operation normative and clarifies that code-group order is unspecified and within-group order is by effective time. Ignixa does not yet target this version, but the clarification removes ambiguity from the intended ordering. |

Authoritative references:

- [STU3 formal definition](https://hl7.org/fhir/STU3/operation-observation-lastn.html)
- [R4 narrative](https://hl7.org/fhir/R4/observation-operation-lastn.html)
- [R4 formal definition](https://hl7.org/fhir/R4/operation-observation-lastn.json.html)
- [R4B formal definition](https://hl7.org/fhir/R4B/operation-observation-lastn.json.html)
- [R5 formal definition](https://hl7.org/fhir/R5/operation-observation-lastn.json.html)
- [Current build narrative](https://build.fhir.org/observation-operation-lastn.html)

The specification also states:

- results represent distinct real-world Observations, not historical versions
  of the same resource;
- `entered-in-error` is included unless the request explicitly filters status;
  and
- no matches return an empty `searchset` without an error.

`SearchOptions.ResourceVersionTypes.Latest` already supplies the required
current-version baseline, and the ordinary search parser does not add an
implicit status filter.

### Current Ignixa search architecture

- `src/Core/Ignixa.Search/Parsing/SearchOptionsBuilder.cs` parses ordinary
  filters, sorts, includes, totals, and paging controls into `SearchOptions`.
- `src/Core/Ignixa.Search/Models/SearchOptions.cs` contains ordinary search
  configuration and has a completeness guard in
  `CompilationContextMappingTests`.
- `src/Core/Ignixa.Search.Sql/Compilation/CompilationContext.cs` is the mapping
  boundary from Search into the SQL compiler.
- `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` creates the candidate match CTE
  before attaching terminal sort, paging, include, and count behavior.
- `src/Core/Ignixa.Search.Sql/Ast/ResultShape.cs` is a closed hierarchy for
  mutually exclusive terminal result forms.
- `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` and
  `ShapeEmitter.cs` dispatch and emit those terminal forms.
- `src/Core/Ignixa.Search.Sql/Lowering/PatientEverythingLoweringRule.cs`
  demonstrates operation-specific lowering, but `$lastn` differs because it
  must preserve normal search filters and transform their result.
- `docs/features/search/investigations/search-sql-decomposition.md` records the
  byte-identical SQL, parameter-order, and public-plan validation invariants
  that a new shape must preserve.

### Direct SQL schema

- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/TokenSearchParam.sql`
  stores `(SystemId, Code, CodeOverflow)` per resource and search parameter.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/TokenText.sql`
  stores token text but uses a case-insensitive collation, so exact text-group
  comparison needs an explicit case-sensitive collation.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/DateTimeSearchParam.sql`
  stores effective date ranges and the `IsMin`/`IsMax` sort markers.
- `src/Core/Ignixa.Search.Sql/Builders/AggregatedSortKeyEmitter.cs` already emits
  grouped derived tables.
- `src/Core/Ignixa.Search.Sql/Builders/ShapeEmitter.cs` already uses a window
  function for include truncation, but the compiler has no existing
  `RANK()`, `ROW_NUMBER()`, or `PARTITION BY` abstraction.

### Live SQL Server benchmark

The repeatable, opt-in fixture is
`test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/LastNSqlBenchmarkTests.cs`.
It runs when `RUN_LASTN_BENCHMARK=1` and `TEST_SQL_CONNECTION_STRING` points
at a live database. The fixture deploys the current schema, seeds all data in
a transaction, performs five warm-up executions, measures 30 warm executions,
captures actual execution plans with `SET STATISTICS XML`, and rolls the data
back.

The measured workload contains:

- 10,000 current Observation candidates for one patient/category-shaped result
  set;
- 400 independent code groups;
- one, two, or three codings per Observation;
- explicit `a -> b -> c -> d` transitive bridges within every group; and
- one effective-date row per Observation.

The accepted exact closure representation assigns a dense numeric node id to
each full `(SystemId, Code + CodeOverflow)` identity, stores one mutable
component label per node, and repeatedly propagates the minimum neighboring
label until convergence. This replaces the all-root reachability relation,
whose storage can grow as O(V²), with O(V) component-label storage plus the
required membership and edge relations.

Measurement on 2026-08-29 UTC used SQL Server 2025 Enterprise Developer
17.0.1125.2, database compatibility level 170, 16 logical CPUs, and 65,484 MB
visible memory:

| Metric | Result |
|--------|--------|
| Warm-up executions | 5 |
| Measured warm executions | 30 |
| P50 | 430.208 ms |
| P95 | 625.414 ms |
| Maximum | 634.397 ms |
| Candidate rows | 10,000 |
| Coded-membership rows | 19,999 |
| Code nodes / component labels | 1,600 / 1,600 |
| Distinct code edges | 4,800 |
| Final components | 400 |
| Command timeout | None (30-second command timeout) |
| Actual-plan spill marker | None |

Command:

```powershell
$env:TEST_SQL_CONNECTION_STRING = 'Server=localhost;Database=LastNFinalReview;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False'
$env:RUN_LASTN_BENCHMARK = '1'
dotnet test test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj --filter "FullyQualifiedName~LastNSqlBenchmarkTests" --framework net10.0 --no-restore --logger "console;verbosity=detailed"
```

The 625.414 ms P95 is 6.25 times the sub-100 ms target. A second exact
identity-mapping variant that avoided the wide `DENSE_RANK` sort removed spill
risk but regressed to 927.872 ms P95; it was not retained. The failure is not
the former quadratic closure table: component-label cardinality remains
exactly one row per node. The dominant cost is deriving and joining the
query-time identity/membership graph for every request.

### External prior art

- [Microsoft FHIR Server issue #1694](https://github.com/microsoft/fhir-server/issues/1694)
  remains the feature request for `$lastn`; there is no Microsoft SQL or Cosmos
  implementation to port.
- [HAPI FHIR `$lastn` documentation](https://hapifhir.io/hapi-fhir/docs/server_jpa/lastn.html)
  describes an Elasticsearch-backed side index and explicitly does not support
  multiple codings correctly. It is useful evidence for the value of a
  dedicated grouping index, but not a compliant SQL implementation to copy.
- LinuxForHealth FHIR has no `$lastn` operation module.

### Expected implementation touchpoints

`Ignixa.Search`:

- operation-specific options and builder;
- required-input and `max` validation;
- version-aware resolution of code/effective search parameters; and
- focused parsing tests, including custom code-bearing parameters.

`Ignixa.Search.Sql`:

- compiler overload and compilation-context input;
- symbol collection for the implicit code/effective parameters;
- resolved `LastNSpec` and closed result shape;
- lowerer and public-plan validation;
- a dedicated `LastNEmitter`;
- plan explanation and diagnostics; and
- SQL grammar, golden, lowering, and end-to-end compilation tests.

Required test cases include:

1. Default `max=1` and explicit positive `max`.
2. Normal Observation filters applied before grouping.
3. Single coding, multiple codings, transitive coding bridges, and text-only
   codes.
4. Equal effective times at, before, and after the boundary, proving `RANK()`
   rather than `ROW_NUMBER()` or `DENSE_RANK()`.
5. Distinct current resources only.
6. Empty candidate set.
7. Case-sensitive text groups and code overflow.
8. Unsupported paging, sorting, and include combinations fail loudly.
9. Authorization constraints remain inside the candidate set.
10. Emitted SQL parses through ScriptDom and remains deterministic.

Live SQL Server tests and query-plan benchmarks are required before accepting
the query-time transitive-closure design.

## Alternative Investigations

1. **materialized-observation-code-groups** - Maintain a direct SQL Server
   code-equivalence mapping during indexing and make `$lastn` a simple indexed
   partition/rank query.
2. **lastn-group-aware-paging** - Define a stable continuation token and
   flattened ordering across otherwise unordered code groups.
3. **lastn-includes** - Define whether includes seed from the post-group result
   and how include pagination composes with tie expansion.

## Verdict

**Rejected for production execution.**

The operation-specific Search model and terminal Search.Sql shape are sound,
and the component-label propagation algorithm is the accepted bounded exact
closure representation for this prototype. The direct query-time design does
not meet the required latency target: its measured warm P95 is 625.414 ms
against a sub-100 ms target.

Do not weaken transitive grouping or ship this query-time path. Continue with
the documented `materialized-observation-code-groups` investigation: maintain
an exact code-equivalence mapping during direct indexing, then make `$lastn`
an indexed partition/rank query over that mapping. Production wiring remains
blocked until that design demonstrates the same semantics and meets the
latency target.
