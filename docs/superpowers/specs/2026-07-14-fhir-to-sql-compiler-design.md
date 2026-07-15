# Ignixa.Search.Sql — Design

**Date:** 2026-07-14
**Status:** Proposed
**Area:** `src/Core/Ignixa.Search/`, new `src/Core/Ignixa.Search.Sql/`, `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/`, and (later) `microsoft/fhir-server` `Microsoft.Health.Fhir.SqlServer`

## Executive recommendation

Build a FHIR-search-to-SQL compiler as a new core library, `Ignixa.Search.Sql`, consuming a semantic
expression IR produced by `Ignixa.Search` and emitting parameterized T-SQL against the search-index
schema that Ignixa and `microsoft/fhir-server` already share.

Three stages, deterministic throughout:

```
Resolve   semantic IR → SymbolTable        (async once, up front)
Lower     semantic IR → QueryPlan          (a CTE graph; normalization rules apply here)
Emit      QueryPlan   → SqlAst → (sql, SqlParameter[])
```

There is no cost model, no memo, no statistics, and no plan-family machinery. SQL Server keeps its
optimizer; the compiler chooses only the FHIR-specific relational *shape*, then hands SQL Server a
well-formed parameterized statement to optimize.

The compiler is a pure function. No `async`, no `DbContext`, no EF, no I/O. That is what makes it
testable without a database and consumable by two hosts.

## The design principle: the un-braiding test

> *"Make things as simple as possible, but not simpler."*

Simplicity is not size. Two designs can be small for opposite reasons: one separates its concerns well,
the other deletes the places where concerns could be separated. Only the first is simple. The second is
*complected* — the concerns are still all present, braided into one place where none of them can be
reasoned about, changed, or tested independently.

This distinction is the whole design, so it is stated as a rule rather than left to taste:

> **Every layer must un-braid a named pair of concerns. If you cannot name the pair in one sentence,
> cut the layer.**

The rule cuts in both directions, which is the point. It rejects layers that separate concerns which
were never braided (*more complex than possible*), and it rejects collapsing layers whose concerns
genuinely are distinct (*simpler than possible*).

Applied to every layer that was considered:

| Layer | Concerns it un-braids | Verdict |
|---|---|---|
| SQL AST | escaping/injection ←→ SQL syntax | **Keep** |
| `Lower` → CTE graph | storage layout ←→ FHIR meaning | **Keep** |
| Semantic IR | FHIR meaning ←→ column addressing | **Keep** |
| `Resolve` → SymbolTable | symbol lookup (I/O) ←→ tree traversal | **Keep** |
| `Normalize` as its own stage | *nothing* — rules over one representation | **Cut** |
| Logical/physical plan split | *nothing* — one logical fact has exactly one correct realization | **Cut** |
| `OverflowConvention` knob | *nothing* — it would encode a bug as a supported option | **Cut** |
| Cost model / memo | *nothing* — it **adds** a concern | **Cut** |

Every kept layer has a one-sentence answer. Every cut layer has none. Arguments from stage count or
line count are not admissible: they would readmit a cost model the moment someone made it feel cheap.

### A sharpened argument for cutting the logical/physical split

An earlier draft cut this split on the grounds that *"the schema dictates the table."* That phrasing was
sloppy and verification exposed it: **the DDL does not dictate the storage convention.**
`TextOverflow NVARCHAR(MAX) NULL` says nothing about whether it holds the value's remainder or the whole
value, and two competent teams read it two ways (see *The storage-convention divergence*).

But the split still fails the test, for a stronger reason. A logical/physical layer would exist to
express *one logical fact having several valid physical realizations*. Here there is exactly **one**
correct realization — fhir-server's, because Ignixa requires data compatibility with it. Ignixa's variant
is a defect, not an alternative. A second IR would exist to parameterize a choice that must not be
offered.

The pair that *is* real — *FHIR meaning ←→ storage layout* — is already un-braided by `Lower`, whose
whole job is to be the single owner of the convention that nothing owned when Ignixa drifted from it.
**Cut stands; reason sharpened.**

This exchange is recorded rather than quietly fixed, because the sequence is the lesson: a plausible
architectural argument survived review, was falsified by reading two row generators, and its
*replacement* — "parameterize the convention in the catalog" — was itself wrong, because it would have
encoded a bug as a supported option. Prefer verification to plausibility, then check what the
verification actually implies.

### Why this rule, and not "fewer lines"

`personal/rojo/new-sql-parser` (see Prior art) reaches ~3,640 lines where this design will reach more.
That comparison is not decisive, and reasoning from it is a trap in both directions.

Its `StringSqlParser.BuildWhereClause` is twelve lines:

```csharp
var escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
...
"exact" => $"{tableName}.Text{(escapedValue.Length > 256 ? "Overflow" : "")}{suffix} = N'{escapedValue}' COLLATE Latin1_General_100_CS_AS",
```

Those twelve lines simultaneously decide: FHIR modifier semantics; injection defense; storage layout
(`Text` vs `TextOverflow`); collation policy; composite component addressing; table aliasing; and SQL
text syntax. Seven concerns. It is small, and it is the opposite of simple.

**The evidence that this is a structural objection rather than an aesthetic one is that the defects sit
exactly on the seams:**

- **LIKE metacharacters are unescaped.** The escaping concern does not know the LIKE concern exists — it
  escapes `'` but not `%`, `_`, or `[`. So `name:contains=100%` treats `%` as a wildcard. Two concerns
  that were never introduced to each other.
- **The storage-layout decision reads an escaped string.** `escapedValue.Length > 256` chooses the
  column, but `escapedValue` has already been mutated by the escaping concern (quote-doubling inflates
  length). The write path routes to `Text`/`TextOverflow` on the *raw* length. Any value where raw and
  escaped length straddle the threshold — a long-enough name containing an apostrophe, and FHIR is full
  of `O'Brien` — reads the wrong column and **silently returns no rows**. No error, no exception, a
  wrong empty result.

That second defect is not carelessness. "How long is the user's string" and "how do I escape quotes" are
facts that should never have been able to touch. In a design with a `Lower` stage, they could not.

This is what the rule is for.

## Context

### Two servers, one schema, one AST

Ignixa is a rewrite of `microsoft/fhir-server`. Both use the same search-index schema —
`ReferenceSearchParam`, `TokenSearchParam`, `DateTimeSearchParam`, `TokenText`,
`ReferenceTokenCompositeSearchParam` and siblings map 1:1 between Ignixa's EF entities and fhir-server's
`dbo.*` tables.

`Ignixa.Search` is a port of fhir-server's search stack (every file carries the Microsoft MIT header;
`InMemory/SearchQueryInterpreter.cs` records "Ported from microsoft/fhir-server"). The expression ASTs
are near-identical: `Expression`, `BinaryExpression`, `ChainedExpression`, `IncludeExpression`,
`InExpression<T>`, `UnionExpression`, `FieldName`, `IExpressionVisitor`, `ExpressionRewriter`,
`NotReferencedExpression`, `SortExpression`. fhir-server adds Cosmos/SMART compartment rewriters; Ignixa
adds `PatientEverythingExpression`.

Same schema plus same AST is what makes one compiler serving both servers realistic — with one
qualification that turns out to matter a great deal.

### The storage-convention divergence

**"Both servers use the same schema" is true of the DDL and false of the storage convention.** Verified
on both write paths:

**fhir-server** — `StringSearchParamListRowGenerator.cs`:

```csharp
if (searchValue.String.Length > _indexedTextMaxLength)
{
    indexedPrefix = searchValue.String.Substring(0, _indexedTextMaxLength);  // Text         = first 256
    overflow      = searchValue.String;                                       // TextOverflow = FULL STRING
}
```

**Ignixa** — `StringSearchParameterRowGenerator.cs`:

```csharp
record.SetString(3, textValue.Substring(0, StringColumnMaxLength));  // Text         = first 256
record.SetString(4, textValue.Substring(StringColumnMaxLength));     // TextOverflow = REMAINDER
```

fhir-server stores the **whole value** in `TextOverflow`, keeping `Text` as a redundant prefix so the
index can still seek (`StringOverflowRewriter`: *"we also check the Text column to allow an index seek"*).
Ignixa stores only the **remainder**, and reconstitutes with `Text + TextOverflow` on read.

Each server is internally consistent, so neither has a bug *on its own terms*. But **Ignixa's data is
unreadable by fhir-server and vice versa**, and Ignixa's stated requirement is to be data-compatible with
fhir-server.

**Therefore this is not a design choice to parameterize. fhir-server's convention is authoritative and
Ignixa's remainder-write is a defect.** It is latent rather than live — self-consistent within Ignixa —
and it surfaces the moment data is shared, migrated, or read by the other server.

Three consequences, in order of importance:

1. **This is the design's central premise, proven by a real defect.** Two ports of the same system, same
   DDL, same AST — silently diverged on storage layout because *nothing owns it*. The convention lives in
   a row generator on one side and a rewriter on the other, and they never had to agree. The DDL
   underdetermines it: `TextOverflow NVARCHAR(MAX) NULL` does not say whether it holds the remainder or
   the whole value, and two competent teams read it two ways. The argument for giving `Lower` that job is
   not hypothetical; it is a post-mortem.
2. **The compiler hardcodes the correct convention; it must not expose it as a catalog knob.** A
   `OverflowConvention` setting would make a bug into a supported configuration — precisely the
   "make invalid state unrepresentable" failure. One convention, one owner, no switch.
3. **Fixing it is a prerequisite with a migration**, not a compiler concern. Ignixa's existing rows carry
   the remainder convention; correcting the writer without reindexing would break long-string search.
   This belongs with `worktree-sql-datalayer-architecture`, which is already consolidating exactly these
   conventions (`StringStorage`, the 128→256 threshold, collation convergence). Tracked separately from
   this design.

Note on scope: the performance difference between the two is narrow and is *not* the issue. It bites only
for *search values* over 256 characters, and `:contains` scans regardless because of its leading
wildcard. The material problem is data compatibility.

### Both servers have the same disease

| | fhir-server | Ignixa |
|---|---|---|
| Query generator | `SqlQueryGenerator.cs` — 1,934 lines | `SearchParameterQueryGenerator.cs` — 2,113 lines |
| Search service | `SqlServerSearchService.cs` | `SqlEntityFrameworkSearchService.cs` — 1,329 lines |
| Pipeline | 22 ordered rewrites, hand-sequenced | ad-hoc `switch` dispatch, no visitor |
| Emission target | raw T-SQL via mutable emitter state | EF Core `IQueryable` |

Ignixa's variant has three specific defects, and they are what this design removes:

1. **It bypasses its own front-end.** A repo-wide grep for `IExpressionVisitor|ExpressionRewriter<` hits
   only `src/Core/Ignixa.Search/**` and one test file. `SearchExpressionQueryBuilder` re-implements
   dispatch by hand with `switch` + type tests, so adding an AST node yields a runtime
   `NotSupportedException` rather than a compile error.
2. **`InExpression<T>` defeats that dispatch and falls back to reflection.**
   `SearchParameterQueryGenerator.ProcessExpressionAsync` reflects over the generic argument and supports
   only `InExpression<string>` over `FieldName.TokenCode`; everything else throws. Meanwhile
   `CompartmentSearchRewriter` *emits* `InExpression` as an OR→IN optimization. The visitor already
   solves this via `TOutput VisitIn<T>(InExpression<T>, TContext)` — that method exists and is unused.
3. **Resolution happens during traversal, asynchronously.** `GetSearchParamIdAsync` /
   `GetResourceTypeIdAsync` perform I/O mid-walk. A compiler resolves symbols up front.

### The motivating bug

`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/CompartmentSearchProblem.txt` (3,289 lines) records a
compartment search where the old server's hand-written CTE-per-reference-parameter SQL completes and the
EF-generated equivalent times out. The problem is not that EF emits *wrong* SQL — it is that there is no
way to tell EF to emit *that shape*. Owning a plan IR and a SQL AST means emitting the shape because we
chose it.

This case becomes a named regression test (see Testing).

## Prior art considered

### 1. The fhir-server north-star (`2026-07-10-fhir-search-sql-compiler-design.md`)

On branch `brendankowitz-simplify-sql-data-layer`. Proposes a seven-plan staged compiler ending in a
bounded cost-based memo optimizer with statistics, plan families, shape hashing, and differential shadow
execution.

**Adopted:** the staged-compiler diagnosis; semantic-IR-first; FHIR meaning defined once.

**Rejected, by the un-braiding test:** the memo, cost model, statistics, and plan families — they
un-braid nothing and add a concern. The logical/physical split — the schema dictates the table, so there
is nothing to separate.

**Reused directly:** its Plan 1 is already implemented on that branch
(`SearchParameterPredicateExpression`, `LegacyExpressionLowerer`, `ParseSemantic`, plus tests), written
against `Microsoft.Health.Fhir.Core`. Under this design it is **ported to `Ignixa.Search`**, the assembly
that survives.

### 2. `personal/rojo/new-sql-parser` — a parallel experiment, deliberately left running

An independent spike in `microsoft/fhir-server` taking the opposite approach: parse the query string
straight to SQL text, no IR. `ISqlParser.Parse(name, value, options) → string?`. One parser per
search-param type, each building its own CTE with a `StringBuilder`. Deletes `SqlQueryGenerator`, all 22
rewriters, and all query generators — 108 files, 3,640 added against 19,325 deleted.

**This branch is not a competitor to be merged or blocked. It is a probe, and it is more useful running
than stopped.** The simple parameter types are where the direct approach looks best (`StringSqlParser`
is 24 lines). The information is in the tail: `ReversedChainSqlParser` is already 262 lines,
`ChainedSqlParser` 188, `SortSqlParser` 151. The question it will answer — and we will not have to pay
to answer — is whether composites, `:iterate`, sort-with-continuation, and compartment stay tractable, or
whether `ParserOptions` grows into a context bag and ordering coupling reappears between parsers. That is
the point where "no IR" either survives contact with FHIR's essential complexity or quietly rebuilds an
IR badly. Either outcome is real evidence.

**Adopted from it:**

- **Per-type decomposition.** One small class per search-param type, each owning its table and its
  predicate construction. This delivers "adding a feature is a local change" without ceremony, and this
  design keeps it — as `Lower` rules rather than string builders.
- **The CTE-threading model.** `ParserOptions` carrying `CteName` / `LastCteName` / `ChainLevel` /
  `ParentIsForwardChain` is a legible way to chain CTEs, and it confirms the non-timing-out compartment
  shape is directly expressible. This design reifies it as the `QueryPlan` CTE graph.
- **The proof that the 22-pass rewrite chain is not load-bearing.** It can be deleted entirely and the
  E2E suite still passes. That is evidence unobtainable by reading code, and it justifies aggressive
  layer removal.

**Not adopted:**

- **String-concatenation emission with no parameterization.** Zero `SqlParameter`, zero `@p` placeholders
  in the entire folder. Consequences: plan-cache defeat (distinct literals ⇒ distinct SQL text ⇒ distinct
  plans, while `QueryPlanReuseChecker` exists in `main` precisely because plan reuse is a known
  production concern); unescaped LIKE metacharacters; and injection defense by convention. See *Why this
  rule, and not "fewer lines"* for why these are seam defects rather than oversights.
- **No unit tests.** 108 files deleted including roughly 10,000 lines of rewriter and generator unit
  tests; zero test files added. The headline "−15,685 lines" is therefore substantially "deleted the test
  suite" — production code is closer to −9k/+3.6k. The honest comparison is not 3.6k versus this design;
  it is 3.6k-plus-what-it-still-owes.
- **Forking FHIR semantics.** SQL would parse query strings to SQL while Cosmos still goes through
  `Expression` — two independent implementations of what `:missing`, `:not`, partial-precision dates, and
  reference forms mean, with nothing to catch drift.

### 3. PR #332 — `brendankowitz-investigate-search-parser-superpower`

Open, mergeable, 69 files, 14,293 additions. Replaces the parser's interleaved ad-hoc parsing (~66
branches mixing parsing, schema resolution, validation, and expression construction) with two phases:
handwritten span scanners emitting immutable `SearchKeySyntax` / `SearchValueSyntax` records, then
schema-aware binders (`SearchKeyBinder`, `SearchExpressionBinder`) that are the only layer touching
`SearchParameterInfo`.

**Integrated as step 1.** It passes the un-braiding test twice over — scanning ←→ schema resolution, and
syntax ←→ binding — and it is the half of a compiler that is hardest to retrofit later.

It also brings instrumentation this work otherwise lacks: `SearchParserOldVsNewParityTests.cs` (730
lines, a differential old-vs-new harness) and `ExpressionParserCharacterizationTests.cs` (126 lines). Its
"freeze the old implementation as `Legacy*` in production code, unwired from DI, as a two-line rollback
lever" pattern is adopted wholesale.

**The gap it does not close:** its binders still lower straight to the field-level AST (`FieldName` +
`BinaryExpression`), which is exactly where comparator, modifier, typed value, and SearchParameter
identity are destroyed. Closing that is step 2.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Consumers | Ignixa **and** `microsoft/fhir-server` | Same schema, same AST. One definition of FHIR search meaning. |
| Ambition | Deterministic, **no cost model** | Un-braids nothing; adds a concern. SQL Server optimizes better anyway. |
| fhir-server adoption | fhir-server **adopts `Ignixa.Search` wholesale** | One AST, no adapter, no duplicated semantics. Consistent with the in-flight Ignixa SDK migration. |
| Semantic IR home | `Ignixa.Search` | It *is* the front-end. The compiler depends on it, not the reverse. |
| Package name | `Ignixa.Search.Sql` | See below. |

### Why `Ignixa.Search.Sql`

Names considered and rejected:

- **`Ignixa.FhirToSqlCompiler`** — collides conceptually with the existing, entirely unrelated
  `Ignixa.SqlOnFhir` (the ViewDefinition spec). Two adjacent `src/Core/` packages both containing "Sql"
  and "Fhir" is a permanent source of confusion. Also breaks convention: Ignixa core packages are domain
  nouns (`Ignixa.Search`, `Ignixa.Validation`, `Ignixa.Serialization`, `Ignixa.FhirPath`), not
  directional transformations.
- **`Ignixa.Search.Ast`** — `Ignixa.Search` already *has* an AST (the `Expression` hierarchy). This name
  reads as "the AST of Ignixa.Search", i.e. the thing that already exists in the parent package.
- **`Ignixa.Search.SqlAst`** — disambiguates correctly, but names the part for the whole. The AST is the
  package's most interesting internal structure, not its purpose. It would not survive the package
  growing a plan cache, and consumers would `using Ignixa.Search.SqlAst;` then call `Compile()`.

`Ignixa.Search.Sql` names the capability — the SQL backend of `Ignixa.Search` — families with its only
dependency, cannot be confused with `Ignixa.SqlOnFhir`, and gives `Ignixa.Search.Cosmos` as the obvious
sibling should it ever exist. `Ast` is retained where it is accurate: as a namespace.

### Why the semantic IR earns its keep — without Cosmos

The tempting justification is cross-backend parity. That justification is weak *for Ignixa today*,
because Ignixa has no Cosmos backend; it would make the layer contingent on step 11, the least certain
step in the plan.

The real justification is available now, in this repo, with no Cosmos anywhere: **physical decisions
currently live inside semantic rewrite passes.**

The clearest case is string overflow. fhir-server has a `StringOverflowRewriter` (plus a
`LegacyStringOverflowRewriter` — 325 and 317 lines of tests respectively) whose entire job is choosing
between the `Text` and `TextOverflow` columns. That is a *storage layout* decision expressed as a
*rewrite of the semantic expression tree*. Rojo's spike makes the same decision inline in a string
interpolation. Both are the same category error at opposite extremes: a physical concern with nowhere
principled to live, smeared into whatever layer is nearby — and in the spike's case, producing the
silent-wrong-column defect described above.

With a semantic IR and a real `Lower` stage, that decision has one home: it is a lowering rule, in the
only layer that knows about columns, testable in isolation.

This connects directly to in-flight work. `worktree-sql-datalayer-architecture` is currently
consolidating storage conventions (`StringStorage`, `TokenCodeStorage`, the 128→256 overflow threshold
correction, collation convergence). Those commits are discovering empirically that storage conventions
need a single owner. `Lower` is that owner. The two efforts should meet.

Cross-backend parity remains a genuine benefit — it is simply a bonus, not the case.

## Goals

- Define FHIR search semantics once; make SQL/Cosmos parity an invariant rather than a hope.
- Emit the CTE shapes known to perform, deterministically and by choice.
- Make injection and plan-cache defeat *unrepresentable*, not merely avoided.
- Give storage-layout decisions exactly one home.
- Make adding a search feature a local, compile-checked change.
- Migrate incrementally with the legacy path live and one DI swap away.

## Non-goals

- Redesigning the search-index schema.
- Cost-based optimization, statistics, memo search, or plan families.
- Replacing SQL Server's optimizer.
- A general-purpose SQL builder for arbitrary queries.
- Cosmos code generation. Cosmos consumes the semantic IR via the existing compatibility lowering and is
  otherwise untouched.

## Architecture

### Package layering

```
Ignixa.Search                                (existing, published, stable)
   syntax → bind → SEMANTIC IR → legacy Expression (compat lowering)
        │
        ├─► Ignixa.Search.Sql                (NEW — pure library; no EF, no ASP.NET, no I/O)
        │      Resolve → Lower → Emit
        │        │
        │        ├─► Ignixa.DataLayer.SqlEntityFramework    (FromSqlRaw / Dapper)
        │        └─► Microsoft.Health.Fhir.SqlServer        (SqlCommand)
        │
        └─► Ignixa.DataLayer.InMemoryIndex   (SearchQueryInterpreter — unchanged)
```

### Data flow

```
HTTP query string
  │
  ▼  Ignixa.Search  ── PR #332 ──────────────────────────────
SearchKeySyntaxParser / SearchValueSyntaxParser     (span scanners, no schema)
  │   → SearchKeySyntax, SearchValueSyntax          (immutable, positioned)
  ▼
SearchKeyBinder / SearchExpressionBinder            (ONLY schema-aware layer)
  │   → BoundParameterKey / BoundChainKey / BoundIncludeKey
  ▼  ── ported from fhir-server Plan 1 ─────────────────────
SEMANTIC IR: SearchParameterPredicateExpression
  │   retains SearchParameterInfo · SearchComparator · SearchModifier · typed ISearchValue
  │
  ├─► LegacyExpressionLowerer → field-level Expression
  │        ├─► InMemoryIndex SearchQueryInterpreter    unchanged
  │        └─► Cosmos (fhir-server)                    unchanged
  │
  ▼  Ignixa.Search.Sql  ── NEW ──────────────────────────────
Resolve   → SymbolTable
Lower     → QueryPlan
Emit      → SqlAst → (sql, SqlParameter[])
```

### Public surface

```csharp
CompiledQuery Compile(SemanticQuery query, SymbolTable symbols, SqlCatalog catalog);

sealed record CompiledQuery(
    string Sql,
    IReadOnlyList<SqlParameter> Parameters,
    PlanShapeHash Shape);
```

Pure and synchronous. All I/O — resolving `SearchParamId`, `ResourceTypeId` — happens in `Resolve`,
before `Compile`, producing a `SymbolTable`.

### The stages

| Stage | Input → Output | Un-braids |
|---|---|---|
| **Resolve** | semantic IR → `SymbolTable` | symbol lookup (I/O) ←→ tree traversal. Kills mid-traversal `await`. |
| **Lower** | semantic IR → `QueryPlan` | storage layout ←→ FHIR meaning. Owns tables, columns, overflow, collation, CTE shape. Normalization rules (OR→`IN`, date folding, CTE coalescing) are rules *here*, not a stage. |
| **Emit** | `QueryPlan` → `SqlAst` → text + params | escaping ←→ SQL syntax. Deterministic: same plan ⇒ byte-identical SQL. |

### The plan IR is a CTE graph

Every CTE yields `(ResourceTypeId, ResourceSurrogateId)` — the currency the working SQL already speaks,
as seen throughout `CompartmentSearchProblem.txt`.

```csharp
QueryPlan {
    IReadOnlyList<CteDefinition> Ctes;      // the match graph
    CteRef                       Match;     // its root
    IReadOnlyList<IncludeStage>  Includes;  // NOT CTEs — stages over the match set
    SortSpec?                    Sort;
    PageSpec                     Page;      // TOP + continuation
}

CteDefinition =
    Source    (table, SearchParamId, column predicates)
  | Intersect (CteRef, CteRef)          // AND
  | Union     (CteRef[], All|Distinct)  // OR
  | Except    (CteRef, CteRef)          // :not, _not-referenced
  | ChainJoin (CteRef, direction, ...)  // chain / reverse chain
```

This exists as data — not as a stage for its own sake — because the CTE list and its dependency order
must be known before rendering. Computing them *during* emission is precisely the mutable-emitter-state
disease that makes `SqlQueryGenerator` 1,934 lines.

`Includes` sits deliberately **outside** `Ctes`: includes are not predicates and not CTEs in the match
graph, and modelling them as such is what forces include-seeding logic to contaminate it. Keeping them
separate is the structural admission that they are a different kind of thing.

### `Lower` is three tiers, and the boundary is load-bearing

The per-type rule pattern works because it reflects the domain: each search-param type maps to exactly
one table, and the types do not interact — `StringSearchParam` knows nothing about `TokenSearchParam`.
That independence is real, not imposed.

**It does not extend to structure, and pretending it does is precisely how `Lower` becomes the next
`SqlQueryGenerator`.** A chain is not a rule about `StringSearchValue`; it wraps other predicates.
`_include` is not a predicate at all. Sort affects the outer query, not any CTE. Forcing these into
`ILoweringRule<T>` grows the context object a field per special case — which is how the spike's
`ParserOptions` accreted `CteName` / `LastCteName` / `ChainLevel` / `ParentIsForwardChain` / `Sort` /
`SortQuerySecondPhase` / `SortContinuationToken`. Same trap, different door.

| Tier | Responsibility | Expected size |
|---|---|---|
| **Leaf rules** | `ILoweringRule<TSearchValue>` → `Source` CTE. One per param type. Owns table, column, storage convention, collation. | 7 × ~40 lines |
| **Structural lowering** | One visitor over the semantic tree. `And`→`Intersect`, `Or`→`Union`, `Not`→`Except`, `Chained`→`ChainJoin`. Dispatches leaves to tier 1. | small — *if* tier 1 is complete |
| **Result-shape stages** | `_include` / `_revinclude` / `:iterate`, sort, continuation. Produce `IncludeStage` / `SortSpec` / `PageSpec`, never CTEs. | the hard part |

The evidence that tier 3 is genuinely separate is that **both prior designs discovered it independently,
from opposite directions**. fhir-server needed `IncludeRewriter`, `IncludeMatchSeedRewriter`, and
`IncludesOperationRewriter` as distinct passes (1,677 + 431 + 440 lines of tests between them). The spike
needed `IncludeSqlParser` and `RevIncludeSqlParser` as separate classes outside its per-type parsers. Two
architectures that agree on nothing else agree that includes do not decompose per-type. That is worth
believing in advance rather than rediscovering.

### `Lower` owns storage convention — and does not offer a choice

The storage convention is not a catalog knob. There is one correct convention (fhir-server's), so
`Lower` encodes it directly:

```csharp
// TextOverflow holds the FULL value; Text is a redundant 256-char prefix retained so the
// index can still seek. Ignixa's remainder-write is a defect, tracked separately -- do not
// add a convention switch here to accommodate it.
SqlColumnRef column = value.Length > TextInlineMax
    ? Column.PrefixSeek(StringSearchParam.Text, StringSearchParam.TextOverflow)
    : StringSearchParam.Text;
```

`SqlCatalog` describes tables, columns, max lengths, and collations — facts the DDL states. It does
**not** describe convention, because offering `OverflowConvention.Remainder | .FullValue` would make a
data-compatibility bug into a supported configuration. One convention, one owner, no switch. That is the
entire point: the convention had no owner, so it drifted.

### `Explain()` is a first-class output

`QueryPlan` renders to a readable, SQL-shaped form — read-only, no parser, no round-trip:

```
cte0 = StringSearchParam[202]  Text = @p0 collate CS_AS
cte1 = TokenSearchParam[41]    Code = @p1
root = Intersect(cte0, cte1)   top 10
```

This is the north-star's "every stage explainable and independently testable" delivered for roughly 80
lines, and it doubles as the **golden-test format**: assert on the *plan*, not on SQL text, so tests
survive emitter changes and read as intent rather than as a diff of generated strings. SQL-text goldens
remain, but only for the emitter's own tests.

Deliberately **not** a parseable DSL. A textual IR would require a parser and printer in the middle of
the compiler and a round-trip nothing needs — concrete syntax where abstract syntax is wanted. It would
also import SQL's semantics (three-valued logic, NULL propagation, coercion) into a layer that must hold
FHIR's. PR #332's benchmarks are the practical footnote: Superpower-style parsing was rejected there at
5–13× slower and 3–9× more allocation precisely because query strings are parsed fresh per request; an IR
parsed per request would re-buy that cost for nothing.

### The SQL AST invariant

The single most important decision in this design:

> **No SQL AST node can carry an unparameterized user value.**

`SqlLiteral` accepts only schema-derived integers (`SearchParamId`, `ResourceTypeId`, surrogate-ID
bounds). Every user-supplied value enters through `SqlParameterRef`. There is no other door.

Injection stops being something to remember and becomes something that cannot be expressed. Two
properties follow:

- **LIKE escaping happens exactly once.** The `SqlLike` node owns it: `%`, `_`, `[` are escaped into the
  *parameter value*, emitting `LIKE @p0 ESCAPE '\'`. One implementation, one test — not a rule seven
  parsers must each remember, which is how the spike lost it.
- **Plan reuse is structural.** `name=Smith` and `name=Trudeau` emit byte-identical SQL differing only in
  parameter values, so SQL Server reuses the plan and `QueryPlanReuseChecker` keeps working.

`PlanShapeHash` derives from the existing `Expression.AddValueInsensitiveHashCode`, implemented on every
AST node and currently consumed by nothing. This design gives it its first real consumer.

## Following the code

The most common complaint about the current SQL generation is that it cannot be followed: expression
parsing, then expression rewriting, then a long series of SQL visitors and generators.

**The root cause is not "too many layers" — it is that the stages all have the same type, so they are
invisible.** `Expression` goes in and `Expression` comes out, 22 times. The `Expression` at pass 1 means
FHIR semantics; the one at pass 22 means SQL shape. Same type, different meaning, nothing marking which
you hold. So "where am I in the pipeline?" is unanswerable from the code in front of you — you must
mentally execute passes 1..n-1 — and nothing stops SQL concerns leaking into FHIR layers, which is how
`SqlCompartmentSearchRewriter` came to live in `Core`.

**Every stage here changes type**, so the type in your hand answers the question:

| Stage | Type in hand | What is *unreachable* here |
|---|---|---|
| Scan | `SearchValueSyntax` | the schema |
| Bind | `BoundParameterKey` | columns |
| Semantic | `SearchParameterPredicateExpression` | tables |
| Lower | `QueryPlan` / `CteDefinition` | SQL text |
| Emit | `SqlAst` | unparameterized user values |

Not convention — reachability.

### Worked example: `GET /Patient?name:exact=Smith`

```csharp
// 1. Scan (#332) -- no schema reachable
SearchKeySyntax   { Name: "name", Modifier: "exact" }
AtomicValueSyntax { Value: "Smith" }

// 2. Bind (#332) -- the only schema-aware layer
BoundParameterKey { Parameter = SearchParameterInfo("name", Type: String),
                    Modifier  = new SearchModifier(SearchModifierCode.Exact) }

// 3. Semantic IR (Plan 1) -- no table, no column, no collation: not reachable from this type
new SearchParameterPredicateExpression(
    parameter:      nameParam,
    modifier:       new SearchModifier(SearchModifierCode.Exact),
    comparator:     SearchComparator.Eq,
    componentIndex: null,
    value:          new StringSearchValue("Smith"));   // typed ISearchValue, not object

// 4. Resolve -- all I/O, once
SymbolTable { ("Patient","name") -> SearchParamId 202,  "Patient" -> ResourceTypeId 103 }
```

Then the one file you read to answer "what does `:exact` do?":

```csharp
sealed class StringLoweringRule : ILoweringRule<StringSearchValue>
{
    private const int TextInlineMax = 256;

    public CteDefinition Lower(SearchParameterPredicateExpression predicate, LoweringContext ctx)
    {
        var value    = ((StringSearchValue)predicate.Value).String;
        var exact    = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Exact;
        var contains = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Contains;

        // TextOverflow holds the FULL value; Text is a redundant 256-char prefix kept so the index
        // can seek. Keyed on the RAW value length -- escaping happens in Emit and is not visible here.
        SqlColumnRef column = value.Length > TextInlineMax
            ? Column.PrefixSeek(StringSearchParam.Text, StringSearchParam.TextOverflow)
            : StringSearchParam.Text;

        var collation = exact ? Collation.CaseSensitive : Collation.CaseInsensitive;

        Predicate p = (exact, contains) switch
        {
            (true,  _)    => Predicate.Equal(column, ctx.Parameter(value), collation),
            (false, true) => Predicate.Like(column, ctx.Parameter(value), Match.Contains,   collation),
            _             => Predicate.Like(column, ctx.Parameter(value), Match.StartsWith, collation),
        };

        return CteDefinition.Source(StringSearchParam.Table,
                                    ctx.Symbols.SearchParamId(predicate.Parameter), p);
    }
}
```

Three properties carry the whole argument:

- **`ctx.Parameter(value)` returns a `SqlParameterRef`.** There is no other way to get a user string into
  a predicate. Not discipline — no API.
- **`Predicate.Like` takes `Match.Contains`, not a pattern.** The rule never writes `%`. It states *what*
  to match; `Emit` decides how to say it. That is exactly the seam the spike lost.
- **The overflow branch reads `value.Length` — the raw value.** It *cannot* read an escaped string:
  escaping has not happened and is not reachable. The silent-wrong-column defect is unrepresentable.

Emitted:

```sql
SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
FROM dbo.StringSearchParam
WHERE SearchParamId = 202
  AND Text = @p0 COLLATE Latin1_General_100_CS_AS      -- @p0 = "Smith"
```

And for `name:contains=100%`, with **no change to `StringLoweringRule`**:

```sql
  AND Text LIKE @p0 ESCAPE '\' COLLATE Latin1_General_100_CI_AI   -- @p0 = "%100\%%"
```

The escaping happened once, inside `SqlLike`, and the lowering rule never knew it existed.

### The same answer today

To learn what `:exact` does you read: `SearchParameterExpressionParser` → `SearchValueExpressionBuilderHelper`
→ the 22-pass chain in `SqlServerSearchService.CreateDefaultSearchExpression` (working out which passes
touch strings) → `StringOverflowRewriter` (and `LegacyStringOverflowRewriter`) →
`SearchParamTableExpressionQueryGeneratorFactory` → `StringQueryGenerator` → `SqlQueryGenerator` →
`SqlCommandSimplifier`. Eight hops, six-plus files, and nothing tells you the list is complete — you grep
for `FieldName.String` and hope. In Ignixa the equivalent is one region inside a 2,113-line file.

### The test this makes possible

```csharp
[Fact]
public void ExactModifier_UsesCaseSensitiveCollation()
{
    var plan   = Lower("Patient?name:exact=Smith");
    var source = Assert.IsType<SourceCte>(plan.Match);
    Assert.Equal(Collation.CaseSensitive, source.Predicate.Collation);
}
```

No database, no SQL string matching, no EF. The equivalent today means hand-building an `Expression`
tree, running the rewrite chain, invoking the generator factory, and asserting on emitted SQL text or a
LINQ expression tree.

### Honest caveats

- **More types is a real up-front cost.** A newcomer meets five representations instead of one. The
  defense is not that five is fewer than one — it is that you only learn the layer you are changing,
  because the boundaries are enforced. Today you must understand the whole pipeline to change any of it.
  Someone's first day is nonetheless worse.
- **Steps 2–9 are the confusing period, not the clear one.** `LegacyExpressionLowerer` means two
  representations coexist — semantic → legacy `Expression` → InMemory/Cosmos, *and* semantic → plan →
  SQL. During migration the codebase is strictly harder to follow than it is today. That is the price of
  not doing a big-bang cutover, and it is a real cost, not a rounding error.
- **The live risk is `Lower` becoming the new `SqlQueryGenerator`.** Nothing structural prevents it
  growing to 2,000 lines. The three-tier split is the mitigation, but tiers 1 and 2 are a convention, not
  an invariant — and the convention will hold worst exactly where it matters most: chains, includes, and
  `:iterate` are inherently cross-cutting. That is where both prior designs got hard, and where the
  spike's probe will produce the most useful evidence.

## Migration and verification

**Freeze, do not delete.** The current `SearchParameterQueryGenerator` moves to `Legacy*` in production
code — `public`, unwired from any DI container. Rollback is a two-line DI swap plus redeploy, no config
flag. This is #332's pattern, already validated here.

**Two verification levels, deliberately asserting different things:**

| Level | Needs DB? | Asserts |
|---|---|---|
| Golden **plan** (`Explain()`) | No | query string → expected `QueryPlan`. The primary format: reads as intent and survives emitter changes. |
| Golden SQL | No | plan → exact SQL text + parameter list. Emitter tests only. Locks determinism. |
| Differential results | Yes | the same query through the legacy EF path and the compiled path returns **identical result sets**. |

Splitting golden-plan from golden-SQL is itself an application of the rule: *what we decided* and *how we
render it* are separate concerns, and a test that conflates them fails for both reasons at once.

The SQL text *should* differ from legacy EF output — that is the point of the work. Only results must
match.

**Cutover** is a single flag, `UseCompiledSearch`, default off, then on. No canary or shape-family
rollout: determinism means a compiled query either works or does not, and golden tests establish which
before deploy.

## Testing

Beyond the golden corpus and differential suite, three invariant tests each make a bug class
unrepresentable:

1. **No user value appears in SQL text.** For every query in the corpus, assert each user-supplied value
   appears in `Parameters` and is absent from `Sql`.
2. **Value-insensitive shape stability.** `date=2013&name=Smith` and `date=2014&name=Trudeau` must emit
   identical `Sql`, differing only in parameter values. The plan-reuse guarantee, tested without a
   database.
3. **Visitor exhaustiveness.** Adding an AST node must be a *compile* error at the compiler, not a
   runtime `NotSupportedException`.

Two named regression tests, each derived from a real defect:

- The `CompartmentSearchProblem.txt` case, asserting the emitted CTE shape.
- **Storage-layout independence from escaping:** a value whose raw and quote-escaped lengths straddle the
  overflow threshold (e.g. a 256-character name containing an apostrophe) must resolve to the same column
  the indexer wrote to. This is the spike's silent-wrong-column defect, encoded so it cannot recur.

**Precondition — the front-end is currently untested.** `ExpressionParser`,
`SearchParameterExpressionParser`, and `SearchOptionsBuilder` are the three largest hand-written files in
`Ignixa.Search` and have no unit tests; `CompartmentSearchRewriter`, `DateTimeEqualityRewriter`, and
`ValueInsensitiveEquals` have zero coverage. There is no `Ignixa.Search.Tests` project. Landing #332
supplies the parity harness and characterization tests; that must happen **before** any refactor.

## Project shape

```
src/Core/Ignixa.Search.Sql/
  Symbols/         SymbolTable, ISymbolResolver
  Catalog/         SqlCatalog, TableDescriptor, ColumnDescriptor
  Plan/            QueryPlan, CteDefinition, plan nodes
  Lower/           lowering + normalization rules (one per search-param type)
  Ast/             SqlAst nodes
  Emit/            SqlEmitter
  IFhirSqlCompiler.cs
  CompiledQuery.cs
```

- `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>` — matches `Ignixa.Search`.
- `<Nullable>enable</Nullable>` — deliberately unlike `Ignixa.Search` (`disable`); a new project should
  not inherit that debt.
- `<IsPackable>true</IsPackable>`.
- No EF reference. No ASP.NET reference.
- A matching `test/Ignixa.Search.Sql.Tests/` project.

## Sequencing

One branch (`feature/fhir-to-sql-compiler`), one commit per unit, kept separate so history stays
reviewable and bisectable.

1. Land PR #332 — front-end (syntax + bind) and parity harness.
2. Port Plan 1 semantic IR from fhir-server's `brendankowitz-simplify-sql-data-layer` into
   `Ignixa.Search`. `LegacyExpressionLowerer` keeps InMemory and Cosmos alive unchanged.
3. Project skeleton + `SqlCatalog` + `SymbolTable` + `Resolve`.
4. SQL AST + emitter + golden harness (trivial queries only).
5. Plan IR + `Lower` **tier 1** (leaf rules) for the seven base parameter types (string, token, date,
   number, quantity, uri, reference), plus **tier 2** (structural lowering). Encodes the storage
   conventions that `worktree-sql-datalayer-architecture` is consolidating — **blocked on** the
   `TextOverflow` convention fix and the drift audit (see Risks).
   `Explain()` lands here; golden tests assert on plans from this point on.
6. Chain and reverse chain.
7. Include, revinclude, iterate.
8. Compartment (the `CompartmentSearchProblem` case), sort, continuation.
9. Wire into `Ignixa.DataLayer.SqlEntityFramework` behind `UseCompiledSearch`; differential suite.
10. `microsoft/fhir-server`: adopt `Ignixa.Search`, wire the compiler, retire `SqlQueryGenerator`.

## Risks and prerequisites

**Steps 1–9 are Ignixa-only; step 10 is most of the work and all of the cross-repo risk.** The "both
servers" decision is in practice *designed for both, delivered to Ignixa first, adopted by fhir-server
last*. Named explicitly so step 10 is not estimated as one bullet.

**`Ignixa.Search` must absorb fhir-server's semantics before step 10.** It lacks
`SmartCompartmentSearchExpression` and `SmartCompartmentSearchRewriter` (a shipped SMART-on-FHIR feature)
and has no home for the Cosmos compartment rewriter. This prerequisite has no owner today.

**Ignixa's `StringSearchParam` write convention is a data-compatibility defect and must be fixed with a
reindex.** `StringSearchParameterRowGenerator` writes the value's *remainder* to `TextOverflow`;
fhir-server writes the *full value*. Ignixa's requirement is compatibility with fhir-server data, so
fhir-server's convention is authoritative. Correcting the writer without reindexing would break
long-string search against existing rows, so this is a migration, not a code change. It is a
**prerequisite for step 5** (the string lowering rule encodes one convention and must encode the correct
one), and it belongs to `worktree-sql-datalayer-architecture`. Whether other search-param types have
drifted the same way is **unchecked** — `TokenText`, the composite `*Overflow` columns, and the token
128→256 threshold that branch is already correcting are the obvious candidates. That audit should precede
step 5.

**Step 10 intersects `personal/rojo/new-sql-parser`.** Both retire `SqlQueryGenerator` and the rewrite
chain, in structurally opposite ways; they cannot both land. This is an organizational question, not a
technical one, and it is not resolvable by this document. It needs a conversation before step 10 is
planned. The two branches have complementary weaknesses — the spike has no parameterization and defeats
plan reuse; this design has no proof it is worth its additional size — and the merged answer (its
per-type decomposition, a real SQL AST) is better than either alone. Meanwhile the spike runs as a probe,
and what it discovers in chains/includes/composites/sort is free evidence for step 10 either way.

**Adding `SearchParameterPredicateExpression` to `IExpressionVisitor` is a binary-breaking change** to a
package marked `stable` with `IsPackable=true`. Mitigation: ship the new visitor method as a default
interface method throwing `NotSupportedException` for external implementors, plus a major version bump.
Not a silent break.

**`SearchParameterInfo` is mutable and embedded in AST nodes.** `Description`, `IsSearchable`,
`IsSupported`, `IsPartiallySupported`, `SortStatus`, `OverridesUrl` have public setters, and
`SortStatus` / `IsSupported` affect behaviour while participating in no equality. AST immutability is not
transitive. `PlanShapeHash` must not close over mutable state; it keys off `Url` + `Code` + `Type` only.

**`BinaryExpression.Value` is typed `object`.** Leaf value types must be recovered from `FieldName` +
`SearchParamType`. The semantic IR (step 2) fixes this by retaining typed `ISearchValue`; until it lands,
the compiler cannot be written against the field-level AST.

**`Ignixa.Search` depends on `Microsoft.AspNetCore.Http.Abstractions`** (via `QueryParameterParser`
consuming `IQueryCollection`), dragging a web dependency into what should be a pure front-end. Not
blocking; the new project must not inherit it.

## Relationship to existing work

- **`worktree-sql-datalayer-architecture`** (this repo) — SQL storage convention consolidation and
  composite structure preservation. Not colliding, but **converging**: its storage conventions are what
  `Lower` will own (step 5). Its phase numbering is unrelated to this document's steps.
- **`brendankowitz-simplify-sql-data-layer`** (fhir-server) — holds the north-star spec and the Plan 1
  implementation ported in step 2. Its remaining plans 2–7 are superseded by this document.
- **`personal/rojo/new-sql-parser`** (fhir-server) — an independent parallel probe. Deliberately left
  running; see Prior art and Risks.
