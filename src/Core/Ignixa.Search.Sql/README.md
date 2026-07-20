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
implements. The output is an immutable `SymbolTable`. This is the *only* stage that touches the outside
world; it does all lookups up front so the later stages can be pure, synchronous functions.

`ISymbolResolver` is the whole seam between the compiler and your storage:

```csharp
public interface ISymbolResolver
{
    Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken);
    Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken);
}
```

The package itself has no EF or ASP.NET dependency and does no database access of its own.

### 2. Lower — build the plan

`Lower.Run` turns the bound tree into a `QueryPlan`. The plan is a **graph of CTEs**, not a tree of
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
SymbolTable symbols = await Resolve.RunAsync(
    expression: searchExpression,
    includes: [],
    revIncludes: [],
    sort: [],
    resolver: resolver,
    targetResourceType: "Patient",
    cancellationToken: cancellationToken);

// 2. Lower — pure. Produce the query plan.
QueryPlan plan = Lower.Run(
    expression: searchExpression,
    symbols: symbols,
    targetResourceType: "Patient",
    includes: [],
    revIncludes: [],
    includeLimit: 0,
    sort: [],
    sortPhase: SortPhase.Valued,
    page: null);

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
| **Leaf types** | string, token (code), reference, uri, number, quantity, date | see gaps below |
| **Comparators** | `eq ne gt lt ge le sa eb` | on date / number / quantity |
| **Composites** | token-token, token-number-number, token-string, token-quantity, token-date, reference-token | |
| **Resource columns** | `_id`, `_type`, `_lastUpdated` | lifted into an outer `WHERE` |
| **Chaining** | forward and reverse chains, any nesting depth | 10-level depth guard |
| **Includes** | `_include`, `_revinclude`, `:iterate` | topologically ordered |
| **Compartment search** | membership over the reference table | grouped by membership parameter |
| **Sort & paging** | `_sort` (up to 3 keys), keyset pagination, two-phase missing-value sort | |
| **Counting** | `_summary=count` / `_total=accurate` | `COUNT_BIG(DISTINCT …)` |
| **Missing** | `:missing` for leaf and composite parameters | |

## What's not implemented yet

These intentionally **throw** rather than emit a subtly-wrong query:

- System-qualified tokens (`system|code`, including `|code`) — needs a `SystemId` resolver.
- Quantity `system` / `code` matching — needs `SystemId` / `QuantityCodeId` resolution.
- URI `:above` / `:below` hierarchical matching.
- The `:ap` (approximately) comparator — needs a tolerance / "now" input the pure stages don't carry.
- Absolute / external references (a non-null reference `BaseUri`).
- String `:contains` / exactly-inline-width `:exact` on values that overflow the inline column — the IR
  can't yet search both the inline and overflow columns at once.

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
