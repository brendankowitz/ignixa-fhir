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
> - The public API (`Resolve` / `Lower` / `SqlBuilder`, the AST records, `ISymbolResolver`) **may change
>   without notice** between alpha releases.
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

## How it works

Compilation is three pure stages plus a build-time catalog. Only the first stage does I/O:

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
                                          EmittedSql  (SQL text + @pN parameters)
```

### 1. Resolve — the only I/O

`Resolve.RunAsync` walks the expression tree once, collects every search parameter and resource type it
references, and resolves them all through `ISymbolResolver` — a single interface your data layer
implements. The output is a `ResolvedSymbols` — an immutable `SymbolTable` plus the parameters the
resolver could not find, reported rather than silently dropped. This is the *only* stage that touches the
outside world; it does all lookups up front so the later stages can be pure, synchronous functions.

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

`Lower.Run` turns the bound tree into a `LoweredPlan` — a `QueryPlan` plus provenance linking each CTE
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

`SqlBuilder.Run` renders the `QueryPlan` to `EmittedSql` — the SQL text and its bound parameters. SqlBuilder is a
dumb, deterministic renderer: it never inlines a user value (those are all `@pN`), fully parenthesizes
every `AND`/`OR` so precedence never depends on context, and keeps the keyset-seek predicate in lockstep
with the `ORDER BY` so pagination can't drift.

### The catalog

`SqlCatalog` describes the tables and columns the compiler targets. Those facts are **source-generated at
build time from the real schema DDL** by `Ignixa.Search.Sql.Generators`, so the compiler and the database
can't disagree about column names, types, lengths, or collations. The catalog describes the schema only —
storage conventions (e.g. which column an over-long string lands in) live in the lowering rules.

## Quick start

The compiler consumes the bound expression tree produced by `Ignixa.Search`'s parser and hands you back
SQL plus parameters to execute however you like.

```csharp
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;

// 0. Parse a FHIR search into a bound expression tree (via Ignixa.Search).
//    e.g. Patient?name=Smith
Expression searchExpression = ParsePatientNameEquals("Smith");

// 1. Resolve — the only async / I/O step. `resolver` is your ISymbolResolver.
//    Returns the symbol table plus any parameters the resolver could not find, so a caller can
//    explain the failure instead of hitting a KeyNotFoundException later during lowering.
ResolvedSymbols resolved = await Resolve.RunAsync(
    expression: searchExpression,
    includes: [],
    revIncludes: [],
    sort: [],
    resolver: resolver,
    targetResourceType: "Patient",
    cancellationToken: cancellationToken);

// 2. Lower — pure. Produce the query plan, plus provenance linking each CTE to the IR node
//    that produced it.
LoweredPlan lowered = Lower.Run(
    expression: searchExpression,
    symbols: resolved.Symbols,
    targetResourceType: "Patient",
    includes: [],
    revIncludes: [],
    includeLimit: 0,
    sort: [],
    sortPhase: SortPhase.Valued,
    page: null);

QueryPlan plan = lowered.Plan;

// Inspect the plan shape (great for tests / debugging):
Console.WriteLine(plan.Explain());

// 3. Emit — pure. Render to parameterized T-SQL.
EmittedSql emitted = SqlBuilder.Run(plan);

Console.WriteLine(emitted.Sql);            // the T-SQL text, with @p0, @p1, ...
foreach (var p in emitted.Parameters)      // the values to bind
    Console.WriteLine($"{p.Name} = {p.Value}");
```

`emitted.Sql` returns `(ResourceTypeId, ResourceSurrogateId)` rows — or
`(…, IsMatch, IsPartial)` when the plan includes `_include`/`_revinclude`, or a single
`COUNT_BIG` when the plan is count-only. You bind `emitted.Parameters` and execute against your database
(e.g. `SqlCommand`, Dapper, or EF Core raw SQL).

## What's supported

| Area | Supported | Notes |
|------|-----------|-------|
| **Boolean composition** | AND, OR, `:not` | Intersect / Union / Except CTEs |
| **Leaf types** | string (incl. `TextOverflow`), token (bare code, `system\|code`, `\|code`, `system\|`, incl. `CodeOverflow`), reference (relative, same-server absolute, and external; resource version not part of identity), uri (exact, segment-aware `:above` / `:below`), number, quantity (with `system`/`code` identity, including explicitly-absent `\|code`), date | see gaps below |
| **Comparators** | `eq ne gt lt ge le sa eb ap` | on date / number / quantity; `eq` is containment and `ne` its exact complement; `:ap` is overlap against bounds widened by `max(precision, 10%)` — see [Approximate (`:ap`) matching](#approximate-ap-matching) |
| **Composites** | token-token, token-number-number, token-string, token-quantity, token-date, reference-token | |
| **Resource columns** | `_id`, `_type`, `_lastUpdated` | lifted into an outer `WHERE` |
| **Chaining** | forward and reverse chains, any nesting depth | 10-level depth guard |
| **Includes** | `_include`, `_revinclude`, `:iterate` | topologically ordered |
| **Compartment search** | membership over the reference table | grouped by membership parameter |
| **Sort & paging** | `_sort` (up to 3 keys), keyset pagination, two-phase missing-value sort | |
| **Counting** | `_summary=count` / `_total=accurate` | `COUNT_BIG(DISTINCT …)` |
| **Missing** | `:missing` for leaf and composite parameters | |

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

The widened range is inclusive on both ends: `LowValue >= value - tolerance AND HighValue <= value +
tolerance`. For `quantity`, this numeric range predicate always comes first; a qualified `system` then
contributes a `SystemId` equality, and a qualified `code` then contributes a `QuantityCodeId` equality —
the same system-then-code order every other quantity comparator already uses.

### Date and `_lastUpdated` — 10% of the distance to a single captured reference instant

A date `:ap` search has no "current time" of its own to compare against — it needs a reference instant
supplied from outside the pure `Lower` stage. That instant is captured **exactly once per compilation**:

- `SearchCompiler.CompileAsync` / `CompileWithTimeProviderAsync` call `TimeProvider.GetUtcNow()` a single
  time up front (before `Resolve` even runs) and pass the one resulting value through to `Lower.Run`'s
  `approximationReferenceTime` parameter. A caller invoking `Lower.Run` directly must supply that same
  parameter explicitly; omitting it while a `:ap` date predicate is present throws
  `InvalidOperationException` rather than silently reading the system clock.
- Because the instant is captured once and reused for every `:ap` predicate in the same compilation, two
  compilations against the same supplied instant (the same `TimeProvider`, or the same explicit
  `approximationReferenceTime`) always produce byte-identical SQL and parameter values — the determinism
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

1. `ReferenceSearchValueParser` collapses an absolute URL whose base equals this server's base to
   `ReferenceKind.Internal` with a null `BaseUri`. The same parser runs on both the index path and the
   query path, so the two forms converge on one representation before reaching SQL.
2. A bare relative search value is `ReferenceKind.InternalOrExternal` and emits *no* `BaseUri` predicate,
   so it matches a stored row whether or not that row carries a base.

Two consequences follow. First, the server base comes from `IFhirBaseUriProvider` — the request context
in-request, falling back to configured `Fhir:BaseUri` for background work such as reindex and `$import`.
If that fallback is unset or disagrees with what the request path produces, background-indexed rows will
disagree with request-indexed ones about which references are internal. Second, normalization applies
only to rows written after this change; references already stored with a self-referencing absolute base
keep it and need a reindex to become findable by their relative form.

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

- **Functional core, imperative shell.** `Lower` and `SqlBuilder` are pure functions of
  `(IR, SymbolTable, SqlCatalog)`. All I/O happens in `Resolve`, up front.
- **Parameterize everything user-supplied.** SQL text never contains a literal user value.
- **Fail loud, never silently wrong.** An unsupported case throws with an actionable message; the
  compiler never degrades a query into one that returns the wrong rows.
- **The schema is generated, not typed by hand.** The catalog comes from the real DDL, so drift is a
  build error, not a runtime surprise.
- **Deterministic output.** Same plan → byte-identical SQL, which makes golden tests trustworthy.

## Package layout

| Namespace | What's in it |
|-----------|--------------|
| `Ignixa.Search.Sql.Symbols` | `Resolve`, `SymbolTable`, `ISymbolResolver`, the tree-walking collector |
| `Ignixa.Search.Sql.Lowering` | `Lower`, the structural/leaf contexts, and the per-type lowering rules |
| `Ignixa.Search.Sql.Ast` | `QueryPlan`, `CteDefinition`, `Predicate`, `PlanExplainer`, and the SQL value types (the plan data model) |
| `Ignixa.Search.Sql.Builders` | `SqlBuilder` (QueryPlan → parameterized T-SQL) and its `EmittedSql` result |
| `Ignixa.Search.Sql.Catalog` | `SqlCatalog` and its table/column descriptors (data generated from DDL) |

## Related packages

- **Ignixa.Search** — the FHIR search parser and indexer; produces the bound expression tree this
  compiler consumes.
- **Ignixa.Search.Sql.Generators** — the build-time source generator that produces `SqlCatalog` from the
  schema DDL (referenced as an analyzer, not a runtime dependency).
- **Ignixa.DataLayer.SqlEntityFramework** — the SQL data layer that owns the search-index schema and
  provides an `ISymbolResolver` implementation.

## License

MIT — see the LICENSE file in the repository root.
