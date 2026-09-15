# Ignixa.Search.Sql

**A FHIR-search-to-SQL compiler.** It takes a bound FHIR search expression tree and produces
parameterized, deterministic T-SQL against the Ignixa search-index schema — the same
`StringSearchParam` / `TokenSearchParam` / `ReferenceSearchParam` / composite tables the Ignixa SQL
data layer writes to.

Think of it as a small, purpose-built compiler: a FHIR search is the source language, T-SQL is the
target, and everything in between is expressed as explicit, testable data structures rather than string
concatenation buried in a query builder.

> ## ⚠️ Alpha
>
> This package ships as a pre-release (`-alpha`) and is **experimental**.
>
> - It is **not yet wired into any production data layer** — it runs in parallel to the shipping SQL
>   search backend, not in place of it.
> - The public API (`ISearchSqlCompiler` / `SearchSqlCompiler`, `SearchPlan`, the AST records,
>   `ISymbolResolver`) **may change without notice** between alpha releases. The pipeline stages it
>   orchestrates — `Resolve`, `Lower`, `SqlBuilder` — are now internal.
> - Generated SQL is covered by exhaustive unit and golden tests, but end-to-end execution against a
>   live SQL Server is not yet part of continuous integration.
>
> Use it to experiment, review, and contribute — don't build production on it yet.

## Why it exists

The shipping search backend hand-dispatches on expression types and builds SQL imperatively. That works,
but the SQL it emits is hard to see, hard to test in isolation, and hard to change with confidence. This
compiler exists to make SQL generation **explicit and inspectable**:

- **You can see the plan before it becomes SQL.** `QueryPlan.Explain()` prints a compact, human-readable
  plan you can pin in a golden test.
- **The SQL is deterministic.** The same plan always emits byte-identical SQL, so a golden test on the
  emitted text is stable.
- **It is injection-safe by construction.** Every user-supplied value becomes a bound `@pN` parameter;
  only schema-resolved integer ids are ever interpolated as literals.
- **Unsupported cases fail loudly.** Where a feature isn't implemented yet, the compiler throws with an
  actionable message instead of silently emitting a wrong-scope or always-empty query.

## Installation

```bash
dotnet add package Ignixa.Search.Sql --prerelease
```

The `--prerelease` flag is required while the package is in alpha.

## How it works inside

Compilation is three stages plus a build-time catalog. Only the first stage does I/O. All three are
**internal** — the boundary a consumer sees is `CreatePlanAsync` (which runs the first two) and
`Compile` (which runs the third). The stage *names* still matter to a caller, though:
`SearchCompilationFailure.Stage` reports exactly these values — `Resolve`, `Lower`, `Emit`, plus `Build`
for anything that rejects the caller's input before the query is examined (query-string parsing, and
mapping a supplied `SearchOptions`) — so a failed compile points at the stage that rejected it.

```
                         ┌─────────────────────────────────────────────┐
   bound FHIR search     │                                             │
   Expression tree  ───► │  1. Resolve   walk the tree, resolve every  │  ◄── ISymbolResolver
   (from Ignixa.Search)  │               search-parameter and          │      (your data layer)
                         │               resource-type name to its     │
                         │               integer id  ── the only I/O   │
                         │                     │                        │
                         │                     ▼   SymbolTable          │
                         │  2. Lower     turn the tree into a           │  ◄── SqlCatalog
                         │               QueryPlan: a graph of CTEs     │      (generated from DDL)
                         │               plus result-shape modifiers    │
                         │                     │                        │
                         │                     ▼   QueryPlan            │
                         │  3. Emit      render the plan to             │
                         │               parameterized T-SQL            │
                         │                     │                        │
                         └─────────────────────┼────────────────────────┘
                                               ▼
                                   CompiledSearch  (SQL text + @pN parameters)
```

### 1. Resolve — the only I/O

Internally, the first stage walks the expression tree once, collects every search parameter and resource
type it references, and resolves them all through `ISymbolResolver` — a single interface your data layer
implements. It produces an immutable symbol table plus the parameters the resolver could not find,
reported rather than silently dropped. This is the *only* stage that touches the outside world, which is
why `CreatePlanAsync` is the compiler's one asynchronous entry point: it does all lookups up front so the
later stages can be pure, synchronous functions.

`ISymbolResolver` is the whole seam between the compiler and your storage:

```csharp
public interface ISymbolResolver
{
    Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken);
    Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken);
    Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken);
    Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken);
}
```

The package itself has no EF or ASP.NET dependency and does no database access of its own.

### 2. Lower — build the plan

The lowering stage turns the bound tree into a `QueryPlan`, plus provenance linking each CTE
back to the IR node that produced it. The plan is a **graph of CTEs**, not a tree of
inline joins: every AND becomes a set intersection, every OR a union, `:not` a set subtraction, a chain a
join through the reference table, and so on — each as its own named CTE, so any node can reference any
other at any depth. Resource-column filters (`_id` / `_type` / `_lastUpdated`) are lifted out into a
single outer `WHERE`. Includes, sort, keyset paging, and count-only are additive result-shape modifiers
on top.

Lowering is organised in two tiers, enforced as types rather than convention:

- **Structural tier** (`StructuralContext`) owns the CTE graph — it dispatches leaves and combines their
  results with intersect/union/except.
- **Leaf tier** (`LeafContext`) sees only symbol lookups and value parameterization. A leaf rule
  *cannot* see or affect the rest of the plan, so a per-type rule (string, token, date, quantity, …)
  stays small and self-contained.

### 3. Emit — render the SQL

The emit stage renders the `QueryPlan` to SQL text and its bound parameters — the `CompiledSearch` that
`Compile()` hands back. It is a dumb, deterministic renderer: it never inlines a user value (those are all `@pN`), fully parenthesizes
every `AND`/`OR` so precedence never depends on context, and keeps the keyset-seek predicate in lockstep
with the `ORDER BY` so pagination can't drift.

### The catalog

`SqlCatalog` describes the tables and columns the compiler targets. Those facts are **source-generated at
build time from the real schema DDL** by `Ignixa.Search.Sql.Generators`, so the compiler and the database
can't disagree about column names, types, lengths, or collations. The catalog describes the schema only —
storage conventions (e.g. which column an over-long string lands in) live in the lowering rules.

## Quick start

```csharp
var compiler = new SearchSqlCompiler(resolver, optionsBuilder);

// Phase 1 — build, resolve, lower. Asynchronous: resolving symbols is the only I/O.
SearchPlan plan = await compiler.CreatePlanAsync("Patient", parameters, cancellationToken: cancellationToken);

// Optional: inspect or rewrite the plan before any SQL exists.
Console.WriteLine(plan.Query.Explain());
plan = plan with { Query = plan.Query with { Top = 50 } };

// Phase 2 — emit. Synchronous: lowering and emission are pure.
CompiledSearch compiled = plan.Compile();
```

When a query-shape problem should be data rather than an exception, use the `Try` pair:

```csharp
var result = await compiler.TryCreatePlanAsync("Patient", parameters, cancellationToken: cancellationToken);
if (!result.Succeeded)
{
    logger.LogWarning("Search failed at {Stage}: {Message}", result.Failure.Stage, result.Failure.Message);
    return;
}
```

Diagnostics are opt-in and off by default:

```csharp
var options = new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Full };
var plan = await compiler.CreatePlanAsync("Patient", parameters, options, cancellationToken);
foreach (var parameter in plan.Diagnostics!.Parameters)
{
    Console.WriteLine($"{parameter.Key}: {parameter.Outcome}");
}
```

`CreatePlanFromOptionsAsync` (and its `Try` counterpart) is the entry point for callers that already hold
a built `SearchOptions` and so skip query-string parsing entirely; that overload needs no
`ISearchOptionsBuilder`.

`compiled.Sql` returns `(ResourceTypeId, ResourceSurrogateId)` rows — or
`(…, IsMatch, IsPartial)` when the plan includes `_include`/`_revinclude`, or a single
`COUNT_BIG` when the plan is count-only. You bind `compiled.Parameters` and execute against your database
(e.g. `SqlCommand`, Dapper, or EF Core raw SQL).

### Two defaults that will surprise you

Neither `_count` nor the include budget is inferred from the query string. Both are caller decisions, and
both default to *not fetching*:

| You wrote | You get by default | To change it |
|-----------|--------------------|--------------|
| `?_count=10` | an **uncapped** statement — `SearchOptions.MaxItemCount` is deliberately not forwarded, because callers transform it (`MaxItemCount + 1`, to detect "has more") before a search runs | `Shape = new ResultShape.Matches(new SearchPaging.Keyset(Top: n))` |
| `?_include=…` | **zero** include rows — `IncludeLimit` defaults to `0`, which emits `TOP (1)` per stage: enough to report *whether* includes exist, not to return any | `IncludeLimit = n` |

> **If you pre-incremented `Top` for has-more detection, say so.** A cap built as `MaxItemCount + 1` must be
> paired with `TopIncludesProbeRow: true`, i.e. `new SearchPaging.Keyset(Top: n + 1, TopIncludesProbeRow: true)`.
> The compiler cannot infer it — `Top: 11` meaning eleven rows and `Top: 11` meaning ten rows plus a lookahead
> are indistinguishable — and without the flag, `_include`/`_revinclude` stages seed from the probe row, so the
> bundle carries included resources for a match you are about to trim. The OFFSET/FETCH equivalent is
> `OffsetSpec.ProbeExtraRow`.

`IncludeLimit` always over-fetches one row so truncation is detectable: the extra row comes back flagged
`IsPartial` and the caller trims it. There is no uncapped setting.

Counting is the same: `_summary=count` and `_total=accurate` are Bundle metadata that the compiler does not
read. Set `Shape = new ResultShape.Count.AllMatches()` yourself when you want the `COUNT_BIG`.
`CompilationContextMapping.NotApplicable` lists every `SearchOptions` property in this category with the
reason, and `SearchCompilationDiagnostics` reports the resolved values at `Full` diagnostics.

## Result shape and paging

`SearchPlanOptions.Shape` is a closed hierarchy rather than a bag of flags, so the combinations that have no
meaning cannot be written down:

```csharp
new ResultShape.Matches();                          // default -- the match set, unpaged
new ResultShape.Matches(new SearchPaging.Keyset(Top: 50));                 // TOP and/or a keyset seek
new ResultShape.Matches(new SearchPaging.Keyset(Top: 51, TopIncludesProbeRow: true)); // ...50 rows plus a has-more probe
new ResultShape.Matches(new SearchPaging.Offset(new OffsetSpec(100, 50))); // OFFSET/FETCH, if you need it
new ResultShape.Count.AllMatches();                 // one COUNT_BIG, no ORDER BY, no TOP
new ResultShape.Count.CurrentSortPhase();           // ...restricted to the segment the sort names
new ResultShape.IncludesPage(Resume: null);         // $includes: the include stages only, as one stream
```

`CreateLastNPlanAsync(LastNSearchOptions)` produces the operation-specific
`ResultShape.LastN`. The existing match CTE remains the candidate set; the terminal
shape then computes transitive components across co-occurring
`Observation.code` codings, assigns each component its stable identity node
ordinal, and emits coded groups before case-sensitive text-only groups. Within a
group, dated observations are newest first with `RANK()` tie expansion. Missing
effective dates follow dated rows and use descending surrogate id only among the
missing rows. Ordinary sorting, paging, `_include`, and `_revinclude` are rejected.

This is a **CTE-only prototype**: a single read-only statement against existing
search tables, without temporary tables, table variables, or deployed objects.
Code equivalence is candidate-local, so excluded bridge Observations do not
merge groups. Recursive simple-path traversal can grow exponentially on highly
connected graphs; use small datasets, not a production latency target.
`MAXRECURSION 0` allows chains beyond 100 hops, while visited-node tracking
terminates cycles. Normal command timeout and cancellation remain applicable.

Paging hangs off `Matches` because that is the only shape that pages. A count reads the whole match set, and
an includes page carries its own boundary in `IncludesPage.Resume` — so neither has a second paging
coordinate that could contradict it.

Which segment of a two-phase sort you read is `SearchPlanOptions.SortPhase`, not a paging property: it
filters the match set, so it applies under either paging mechanism and with no paging at all.
`Keyset.Boundary` is the seek boundary, and it only means anything within the phase that produced it, so the
two are set together.

### Two-phase missing-value sort

FHIR lets you `_sort` on a parameter a resource may not have. The sort key lives in a side table, so no
single seekable statement can order "rows with a value" ahead of "rows without one". The compiler instead
emits one statement per phase and **the caller drives the sequence**:

```csharp
var options = new SearchPlanOptions
{
    SortPhase = phase,
    Shape = new ResultShape.Matches(new SearchPaging.Keyset(Top: pageSize, Boundary: boundary)),
};
```

- `SortPhase.Valued` (the default) inner-joins the primary sort key: rows that have a value, ordered by it.
- `SortPhase.MissingPrimary` emits `NOT EXISTS` on that key: rows that lack a value, ordered by the secondary
  keys if there are any, then surrogate id.

Page through `Valued` until it runs out, then restart at the first page of
`SortPhase.MissingPrimary` and page that to exhaustion. Only the *primary* key is
phased; secondary keys are always left-joined tie-breakers with a sentinel, so they need no second pass.

`_lastUpdated`, `_type` and `_id` sort on non-nullable resource columns, so they have no `MissingPrimary`
segment and need only one pass.

Counting works the same way: `ResultShape.Count.AllMatches` counts the whole match set and ignores any sort
the plan carries, while `ResultShape.Count.CurrentSortPhase` counts only the rows the phase reaches -- which
is what a caller totalling the two phases separately needs.

## What's supported

| Area | Supported | Notes |
|------|-----------|-------|
| **Boolean composition** | AND, OR, `:not` | Intersect / Union / Except CTEs |
| **Leaf types** | string (incl. `TextOverflow`), token (bare code, `system\|code`, `\|code`, `system\|`, incl. `CodeOverflow`), reference (relative, same-server absolute, and external; resource version not part of identity), uri (exact, segment-aware `:above` / `:below`), number, quantity (with `system`/`code` identity, including explicitly-absent `\|code`), date | see gaps below |
| **Comparators** | `eq ne gt lt ge le sa eb ap` | on date / number / quantity — see [Comparator semantics](#comparator-semantics) |
| **Composites** | token-token, token-number-number, token-string, token-quantity, token-date, reference-token | |
| **Resource columns** | `_id`, `_type`, `_lastUpdated` | lifted into an outer `WHERE` |
| **Chaining** | forward and reverse chains, any nesting depth | 10-level depth guard |
| **Includes** | `_include`, `_revinclude`, `:iterate` | topologically ordered; budget is `IncludeLimit`, default 0 |
| **Compartment search** | membership over the reference table | grouped by membership parameter |
| **Sort & paging** | `_sort` (up to 3 keys), keyset pagination, [two-phase missing-value sort](#two-phase-missing-value-sort) | caller drives the phases |
| **Counting** | `ResultShape.Count` | `COUNT_BIG(DISTINCT …)`; the caller sets the shape — `_summary=count` / `_total` are not read |
| **Missing** | `:missing` for leaf and composite parameters | |

## Comparator semantics

Every ranged type stores a `[low, high]` pair — `LowValue`/`HighValue` for number and quantity,
`StartDateTime`/`EndDateTime` for date — and every prefix is a relation between that stored range and
the parameter's own range `[S, E]`, exactly as the [FHIR search prefix
table](https://hl7.org/fhir/search.html#prefix) defines it. Number, quantity, and date share one set of
relations:

| Prefix | Spec relation | Predicate |
|--------|---------------|-----------|
| `eq` | parameter range fully contains the stored range | `low >= S AND high <= E` |
| `ne` | exact negation of `eq` | `low < S OR high > E` |
| `gt` | the range above `E` overlaps the stored range | `high > E` |
| `ge` | `[S, +∞)` overlaps the stored range | `high >= S` |
| `lt` | the range below `S` overlaps the stored range | `low < S` |
| `le` | `(-∞, E]` overlaps the stored range | `low <= E` |
| `sa` | stored range starts strictly after the parameter range | `low > E` |
| `eb` | stored range ends strictly before the parameter range | `high < S` |
| `ap` | overlap against widened bounds | `low <= E' AND high >= S'` |

Two column choices are easy to get wrong and invisible in testing. `gt` names `high`, not `low`: on a
row storing `[5, 50]`, `gt10` must match, because part of the row's range does exceed 10. Comparing
`low > 10` there is the relation `sa` denotes, not `gt`. Likewise `lt` names `low`, and comparing
`high < S` is `eb`. **Neither collapse is observable on a point-valued row** (`low == high`), which is
what every plain `valueQuantity` or number indexes to — so only a row storing a genuine `Range`
element separates them. `RangeComparatorSemanticsTests` evaluates the lowered predicates against such
rows for that reason.

What `[S, E]` is depends on the prefix, not on the type:

- `eq`/`ne` widen a decimal by its implied-decimal-precision modifier (`5.4` → `[5.35, 5.45]`), and take
  a date's own partial-precision interval (`2013` → the whole year).
- `ap` widens further — see [Approximate (`:ap`) matching](#approximate-ap-matching).
- The ordering comparators do **not** widen a decimal. The spec is explicit: for `lt`/`le`/`gt`/`ge`
  "the implicit precision of the number is ignored, and they are treated as if they have arbitrarily
  high precision", so `gt100` means greater than exactly 100 and `S = E = value`.

## String parameter matching across inline and overflow storage

`StringSearchParam` stores values in two columns:

- **`Text`** — a `nvarchar(256)` inline column that holds the value when it fits; at 256 characters or
  fewer the full value is here and `TextOverflow` is `NULL`.
- **`TextOverflow`** — an `nvarchar(MAX)` overflow column that holds the complete value when it exceeds
  256 characters; `Text` then stores only the first 256 characters of the value.

Lowering selects the correct predicate shape based on the search value's length versus the inline
width (256). Both shapes are injection-safe: all user values are bound as `@pN` parameters, never
inlined.

### `:exact` — case-sensitive equality (`Latin1_General_100_CS_AS`)

| Search value length | Predicate shape |
|---------------------|-----------------|
| ≤ 256 characters    | `TextOverflow IS NULL AND Text = @p0 COLLATE Latin1_General_100_CS_AS` |
| > 256 characters    | `TextOverflow = @p0 COLLATE Latin1_General_100_CS_AS` |

The `TextOverflow IS NULL` guard on the short-value branch prevents a false-positive match when a
stored value overflowed and its 256-character `Text` prefix happens to equal the shorter search value.
For a search value exceeding 256 characters, only an overflowed row can ever contain it, so a direct
equality on `TextOverflow` suffices with no guard needed.

### `:contains` — case- and accent-insensitive LIKE (`Latin1_General_100_CI_AI`)

| Search value length | Predicate shape |
|---------------------|-----------------|
| ≤ 256 characters    | `(TextOverflow IS NULL AND Text COLLATE … CI_AI LIKE @p0 ESCAPE '\') OR TextOverflow COLLATE … CI_AI LIKE @p1 ESCAPE '\'` |
| > 256 characters    | `TextOverflow COLLATE … CI_AI LIKE @p0 ESCAPE '\'` |

For short values the dual-column shape searches both storage locations: the `Text` branch (guarded by
`TextOverflow IS NULL`) matches non-overflowed rows; the `TextOverflow` branch matches overflowed rows
through the complete stored value. Both branches receive the same escaped `%…%` pattern bound to two
separate parameters (`@p0` and `@p1`) — one for each `LIKE`. LIKE metacharacters (`%`, `_`, `[`, `\`)
in the search value are escaped and bound as parameters, never inlined, so user-supplied wildcards are
treated as literals.

For a search value exceeding 256 characters, any matching stored value must have overflowed, so a
single `TextOverflow LIKE` is sufficient.

### Default prefix matching (no modifier)

Unmodified string queries use `LikeMatch.StartsWith` with the CI_AI collation and an `ESCAPE '\'`
clause. Lowering selects the column based on the search value's length:

| Search value length | Predicate shape |
|---------------------|-----------------|
| ≤ 256 characters    | `Text COLLATE … CI_AI LIKE @p0 ESCAPE '\'` |
| > 256 characters    | `TextOverflow COLLATE … CI_AI LIKE @p0 ESCAPE '\'` |

For a prefix of 256 characters or fewer, `Text` is the correct target: `Text` holds the first 256
characters of every stored value, so any stored value whose complete value starts with a prefix of
that length will have that prefix captured verbatim in `Text`. No `TextOverflow IS NULL` guard is
needed — a row that did overflow still has the correct prefix in `Text`, so `Text LIKE` already
returns the right set. For a prefix exceeding 256 characters, only an overflowed row can contain
it, so `TextOverflow LIKE` is the correct target. In both cases the complete logical value is
searched correctly. The search value is escaped and bound as `@p0`; user-supplied LIKE metacharacters
(`%`, `_`, `[`, `\`) are treated as literals.

## Approximate (`:ap`) matching

`:ap` on `number`, `quantity`, `date`, and `_lastUpdated` widens the search value by a fixed **10%
tolerance** before comparing, per the FHIR search specification's guidance for the "approximately"
comparator. Lowering is a pure function everywhere else in this compiler, so `:ap`'s tolerance never
reads an ambient clock or random source; every input it needs is threaded through explicitly.

### Number and quantity — `max(implied precision, 10% of value)`

The tolerance is `max(implied-precision modifier, abs(value) * 0.10)`:

- **Implied-precision floor.** The implied-precision modifier is the same half-a-trailing-digit tolerance
  FHIR's `eq`/`ne` already use for decimal implied precision (e.g. `1` → `0.5`; `100.00` → `0.005`) — it
  stops a low-precision value's 10% tolerance from being narrower than the value's own implied precision.
- **10% floor.** For any value large enough that 10% of it exceeds the implied-precision modifier, the
  10% figure wins.

`:ap` is **overlap** against the widened bounds, not containment: `LowValue <= value + tolerance AND
HighValue >= value - tolerance`. That is deliberately looser than `eq`, which is containment
(`LowValue >= value - modifier AND HighValue <= value + modifier`) — every row `eq` matches, `ap` also
matches, but not the reverse. For `quantity`, this numeric range predicate always comes first; a qualified `system` then
contributes a `SystemId` equality, and a qualified `code` then contributes a `QuantityCodeId` equality —
the same system-then-code order every other quantity comparator already uses.

### Date and `_lastUpdated` — 10% of the distance to a single captured reference instant

A date `:ap` search has no "current time" of its own to compare against — it needs a reference instant
supplied from outside the pure `Lower` stage. That instant is captured **exactly once per compilation**:

- `SearchSqlCompiler` reads `TimeProvider.GetUtcNow()` a single time up front — before the internal
  `Resolve` stage runs — and threads that one value through lowering as the reference instant for every
  `:ap` date predicate in the compilation. The clock is the compiler's own `TimeProvider`: the one passed
  to its constructor, defaulting to `TimeProvider.System`. A caller pins the instant by supplying a fixed
  `TimeProvider`; nothing inside lowering ever reads an ambient clock.
- Because the instant is captured once and reused for every `:ap` predicate in the same compilation, two
  compilations that observe the same instant (the same `TimeProvider` returning the same `GetUtcNow()`)
  always produce byte-identical SQL and parameter values — the determinism
  guarantee above extends to `:ap` exactly as it does to every other comparator.

The tolerance is `max(precision, abs(referenceInstant - midpoint) / 10)`, where `midpoint` is the search
value's own `[Start, End]` interval midpoint and `precision` is that interval's own width (already
resolving FHIR partial-date precision). The widened interval is `[Start - tolerance, End + tolerance]`.

The precision floor is a deliberate deviation from a literal reading of the spec's 10% guidance, which
the spec explicitly permits ("systems may choose other values where appropriate"). Without it the
proportional term goes to zero as the search value approaches the reference instant, so `date=ap<today>`
— the most likely real-world `:ap` query — would silently degenerate into exact `eq`. The floor mirrors
the `precision_modifier` term numeric `:ap` already used.

Endpoints that would fall outside `DateTimeOffset`'s range saturate at `MinValue`/`MaxValue` rather than
throwing, matching how numeric `:ap` saturates at the decimal bounds: `date=ap0001-01-01` is legal user
input and must compile.

- **`date`** compares the widened interval against the stored `[StartDateTime, EndDateTime]` pair with an
  overlap test: `StartDateTime <= widenedEnd AND EndDateTime >= widenedStart`.
- **`_lastUpdated`** has no interval column of its own — it targets the single point column
  `ResourceSurrogateId` — so both widened endpoints are converted through the same surrogate-id encoding
  every other `_lastUpdated` comparator uses, then compared as a lower-then-upper range:
  `ResourceSurrogateId >= widenedLowerSurrogateId AND ResourceSurrogateId <= widenedUpperSurrogateId`.

  The upper bound is the *last* surrogate id in its millisecond, not the first. `ResourceSurrogateId`
  encodes `msSince0001 * 80000 + uniquifier`, where the database allocates the uniquifier from a sequence
  declared `MAXVALUE 79999`. Comparing an upper bound against the bare millisecond floor would match only
  the row that happened to draw uniquifier 0, dropping up to 79,999 resources written in that
  millisecond. Every `_lastUpdated` comparator — not just `:ap` — is expressed against the closed range
  `[floor, floor + 79999]`.

These claims describe what `Resolve` and `Lower` (and the SQL `Emit` renders from them) do; they say
nothing about execution against a live database — see the alpha notice above.

## Known limitations

**Chain / include / revinclude traversal remains local-only.** The `ChainJoin` and `IncludeStage`
emitters hard-code `rsp.BaseUri IS NULL`, so they follow only references whose `BaseUri` is null
in the stored index. Phase 2 external-reference leaf matching enables searching for stored leaf
references with a non-null `BaseUri`, but it does not enable fetching or traversing resources hosted
on an external FHIR server.

**Reference reconciliation depends on the base URI being resolvable, and only applies going forward.**
The spec requires that "a relative reference resolving to the same value as a specified absolute URL, or
vice versa, qualifies as a match". That holds here through two mechanisms working together:

1. `ReferenceSearchValueParser` collapses an absolute URL whose base is one of this server's bases to
   `ReferenceKind.Internal` with a null `BaseUri`. The same parser runs on both the index path and the
   query path, so the two forms converge on one representation before reaching SQL.
2. A bare relative search value is `ReferenceKind.InternalOrExternal` and emits *no* `BaseUri` predicate,
   so it matches a stored row whether or not that row carries a base.

Two consequences follow. First, the server bases come from `IFhirBaseUriProvider.IsServiceBaseUri`, which
answers over a *set* rather than one scalar. One tenant answers to two bases — the deployment root
(`https://host/`) and the tenant-scoped base (`https://host/tenant/1/`) — and this server hands out
absolute links in both forms depending on which route a request used. Recognising only one made a
reference ingested via `/Patient` invisible to an absolute search issued via `/tenant/1/Patient`. The set
is derived by `FhirServiceBaseUriResolver` from the tenant, never from the incoming route, so the request
path, bundle entries, reindex and `$import` all reach the same answer. `Fhir:BaseUri` supplies the
deployment root; when it is set, the request `Host` header is ignored for this purpose, and when it is
unset background indexing has no base at all and will disagree with request-indexed rows. Second,
normalization applies only to rows written after this change; references already stored with a
self-referencing absolute base keep it and need a reindex to become findable by their relative form.

**Unknown terminology values lower to `Predicate.False`, not a resolution error.** When a
system-qualified token or quantity carries a `system` or quantity `code` that has no database row,
`Resolve` stores the known-miss, `Lower` lowers that individual predicate to `Predicate.False`, and
`Emit` renders `1 = 0` for that branch in the WHERE clause, and `Explain` prints the same `1 = 0` so a
plan and its SQL read alike in a trace. Normal Boolean composition still applies
on the surrounding query: AND with the false predicate makes that conjunction empty; OR may still
return matches from its other branches; negating the false predicate yields its complement (the full
target-resource set for that predicate's scope). Resolver I/O failures still propagate unchanged —
only a confirmed "not found" result produces the `1 = 0` path.

## Design principles

- **Functional core, imperative shell.** Lowering and emission are pure functions of the resolved
  symbols, the plan, and the catalog; the only I/O is resolving symbols, done once up front. The public
  API draws its two-phase seam on exactly that line: `CreatePlanAsync` is asynchronous because it
  resolves, and `Compile` is synchronous because emitting a plan is pure.
- **Parameterize everything user-supplied.** SQL text never contains a literal user value.
- **Fail loud, never silently wrong.** An unsupported case throws with an actionable message; the
  compiler never degrades a query into one that returns the wrong rows.
- **The schema is generated, not typed by hand.** The catalog comes from the real DDL, so drift is a
  build error, not a runtime surprise.
- **Deterministic output.** Same plan → byte-identical SQL, which makes golden tests trustworthy.

## Package layout

The pipeline stages (`Resolve`, `Lower`, `SqlBuilder`) are internal; this table lists the public types a
consumer reaches, grouped by namespace.

| Namespace | Public surface |
|-----------|----------------|
| `Ignixa.Search.Sql` | `SearchSqlCompiler` / `ISearchSqlCompiler`, `SearchPlan`, `CompiledSearch`, `SearchPlanOptions`, `SearchPlanResult` / `SearchCompilationResult`, `SearchCompilationFailure` / `SearchCompilationException`, and the diagnostics types (`SearchCompilationDiagnostics`, `QueryPlanTrace`, `CteProvenance`, `ImplicitParameter`, `CompilationStage`, `SearchDiagnosticsLevel`) |
| `Ignixa.Search.Sql.Symbols` | `ISymbolResolver` — the one seam your data layer implements |
| `Ignixa.Search.Sql.Ast` | `QueryPlan` and the plan data model — `CteDefinition`, `Predicate`, `PageSpec`, `SortSpec`, `SortPhase`, `ResultShape`, `SearchPaging`, `KeysetContinuationToken`, `KeysetPosition`, `PlanExplainer`, and the SQL value types |
| `Ignixa.Search.Sql.Builders` | `EmittedSqlParameter` (the bound `@pN` values on `CompiledSearch`) and `SqlTextRange` |
| `Ignixa.Search.Sql.Catalog` | `SqlCatalog` and its `TableDescriptor` / `ColumnDescriptor` (data generated from DDL) |

## Related packages

- **Ignixa.Search** — the FHIR search parser and indexer; produces the bound expression tree this
  compiler consumes.
- **Ignixa.Search.Sql.Generators** — the build-time source generator that produces `SqlCatalog` from the
  schema DDL (referenced as an analyzer, not a runtime dependency).
- **Ignixa.DataLayer.SqlEntityFramework** — the SQL data layer that owns the search-index schema and
  provides an `ISymbolResolver` implementation.

## License

MIT — see the LICENSE file in the repository root.
