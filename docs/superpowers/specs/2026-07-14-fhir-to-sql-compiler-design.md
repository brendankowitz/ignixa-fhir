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

Two things should happen before the first line of it is written, and they are independent of whether it is
ever built:

1. **Fix the `TextOverflow` write convention.** It is not a latent inconsistency; it is a **live
   silent-wrong-results bug** against the `Ignixa.DataLayer.SqlEntityFramework` package's advertised
   zero-data-migration promise. It needs a reindex. See *The storage-convention divergence*.
2. **Run step 0 as a four-arm factorial** — the compartment case, on hand-built semantic IR. It needs
   neither PR #332 nor the migration. Run as an A/B it proves nothing, because "compiler vs EF" confounds
   CTE shape with literal-vs-parameter `SearchParamId` and with plan-cache keying; the arm that matters is
   the residual only *shape* explains. There is a live possibility that most of the compartment win is an
   `EF.Constant` away and needs no compiler at all. See *Step 0 — the proving increment*.

Both are cheap, both are independent of this design's fate, and the second may retire its headline
argument. That ordering is deliberate.

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
| Logical/physical plan split | *nothing `Lower` doesn't already own* — YAGNI, not impossibility | **Cut** |
| `OverflowConvention` knob | *nothing* — it would encode a bug as a supported option | **Cut** |
| Cost model / memo | *nothing* — it **adds** a concern | **Cut** |

Every kept layer has a one-sentence answer. Every cut layer has none. Arguments from stage count or
line count are not admissible: they would readmit a cost model the moment someone made it feel cheap.

### A sharpened argument for cutting the logical/physical split

An earlier draft cut this split on the grounds that *"the schema dictates the table."* That phrasing was
sloppy and verification exposed it: **the DDL does not dictate the storage convention.**
`TextOverflow NVARCHAR(MAX) NULL` says nothing about whether it holds the value's remainder or the whole
value, and two competent teams read it two ways (see *The storage-convention divergence*).

But the split still fails the test, for a stronger reason — though the reason needs stating more carefully
than an earlier draft did. A logical/physical layer would exist to express *one logical fact having
several valid physical realizations*.

For the **storage convention**, there is exactly **one** correct realization — fhir-server's, because
Ignixa promises data compatibility with it. Ignixa's variant is a defect, not an alternative. A second IR
would exist to parameterize a choice that must not be offered.

**That argument is drawn from one case, and it does not generalize.** Elsewhere, one logical fact genuinely
does have several valid realizations: `:not` can be `EXCEPT`, `NOT EXISTS`, or an anti-join;
OR-across-values can be `UNION` or an `IN` list; sort can be filter-then-sort or fhir-server's two-phase
strategy. This design *does* choose among those — "OR→`IN`, date folding, CTE coalescing" are exactly such
choices — it simply hardcodes each as a deterministic rule in `Lower` rather than exposing it. So the
honest claim is not "there is nothing to separate," but:

> **`Lower` is a fixed heuristic optimizer.** It makes every shape choice once, at authoring time, and
> offers no knob.

That is a real trade, and defining it away would be the same move this document criticizes elsewhere. The
upside is determinism, golden plans, and no cost model to maintain. The downside is that when a hardcoded
shape regresses on skewed data there is no plan-family escape hatch **by design**; the fix is to change the
rule and ship. Worth it — SQL Server still optimizes the statement we hand it, and the alternative is the
memo machinery cut above. But note what a logical/physical split would actually have bought here: not the
knob, only a *place to put* one. The case against it is YAGNI, not impossibility.

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

**fhir-server** — `Microsoft.Health.Fhir.SqlServer/Features/Storage/TvpRowGeneration/Merge/StringSearchParamListRowGenerator.cs:25-30`:

```csharp
if (searchValue.String.Length > _indexedTextMaxLength)
{
    indexedPrefix = searchValue.String.Substring(0, _indexedTextMaxLength);  // Text         = first 256
    overflow      = searchValue.String;                                       // TextOverflow = FULL STRING
}
```

**Ignixa** — `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs:81-85`:

```csharp
record.SetString(3, textValue.Substring(0, StringColumnMaxLength));  // Text         = first 256
record.SetString(4, textValue.Substring(StringColumnMaxLength));     // TextOverflow = REMAINDER
```

fhir-server stores the **whole value** in `TextOverflow`, keeping `Text` as a redundant prefix so the
index can still seek (`StringOverflowRewriter`: *"we also check the Text column to allow an index seek"*).
Ignixa stores only the **remainder**, and reconstitutes with `Text + TextOverflow` on read.

Each server is internally consistent, so neither has a bug *on its own terms*. But **Ignixa's data is
unreadable by fhir-server and vice versa**, and data compatibility here is not an aspiration to be traded
off — it is the package's headline promise
(`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/README.md:3-8`):

> compatible with Microsoft FHIR Server schema (v60-v96). Enables migration from `microsoft/fhir-server`
> **without data migration**. […] **Zero data migration**: Point to your existing database and start
> using Ignixa.

**Therefore this is not a design choice to parameterize. fhir-server's convention is authoritative and
Ignixa's remainder-write is a defect** — and the defect is **live, not latent**.

Follow that README. Point Ignixa at an existing fhir-server database holding a 300-character name.
fhir-server wrote `Text` = the first 256 characters and `TextOverflow` = all 300. Ignixa's read path
reconstitutes `Text + TextOverflow` (`Search/SearchParameterQueryGenerator.cs:1583,1608,1624`), producing
a 556-character string: the prefix glued to the whole value. The `LIKE` cannot match it. **The search
returns no rows — no error, no exception, a silently wrong empty result**, for every string over 256
characters. Zero data migration is the advertised feature; this defect is inside it.

So the divergence is not merely evidence *for* the design's premise. It is a shipped bug with a reindex
attached, and it is worth fixing on its own merits whether or not this compiler is ever built.

Three consequences, in order of importance:

1. **This is the design's central premise, proven by a real defect.** Two ports of the same system, same
   DDL, same AST — silently diverged on storage layout because *nothing owns it*. The convention lives in
   a row generator on one side and a rewriter on the other, and they never had to agree. The DDL
   underdetermines it: `TextOverflow NVARCHAR(MAX) NULL` does not say whether it holds the remainder or
   the whole value, and two competent teams read it two ways. The argument for giving `Lower` that job is
   not hypothetical; it is a post-mortem.

   Two details sharpen this. First, it is not only the DDL that underdetermines the convention — the
   *documentation written specifically to explain it* does too. `StringSearchParameterRowGenerator.cs:20-30`
   is a ten-line class remark titled "Text storage strategy for FHIR string search"; it covers the 256-char
   split, the `:exact` rationale, and the query-time collation policy, and it never states whether
   `TextOverflow` holds the remainder or the whole value. Prose does not own conventions either.

   Second, the same disease is visible in a single constant. fhir-server *derives* the threshold from the
   schema — `_indexedTextMaxLength = (int)VLatest.StringSearchParam.Text.Metadata.MaxLength`
   (`StringSearchParamListRowGenerator.cs:14`) — so it tracks the DDL automatically. Ignixa hardcodes
   `const int StringColumnMaxLength = 256` (`StringSearchParameterRowGenerator.cs:35`). They agree today,
   because `Text nvarchar(256)`
   (`Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql/Tables/StringSearchParam.sql:6`) — but one of
   them agrees by construction and the other agrees by someone remembering. That is the whole thesis in
   one constant, and it is a cleaner example than the overflow bug itself.

   Third, and most starkly: **Ignixa ships a stored procedure it never calls.**
   `Resources/97.sql:1470` defines `dbo.CreateResourceSearchParamStats`, which creates the filtered
   statistics per `(ResourceTypeId, SearchParamId)` that let the optimizer cost a search predicate off
   per-parameter density instead of a blended table histogram. **It has zero C# callers repo-wide.**
   fhir-server invokes it on every search, cached process-locally (`SqlServerSearchService.cs:508`,
   `:2991-3011`). So an Ignixa-created database has **no filtered statistics at all**; only a
   fhir-server-migrated one does — created by the other server, for a database Ignixa now owns. The
   convention arrived in the DDL, nothing in the code owned it, and it silently stopped happening. Same
   disease, third instance, and this one costs query plans rather than correctness. (Minor ongoing drift:
   fhir-server's sproc now takes `@ReferenceResourceTypeId`; Ignixa's v97 signature does not.)
2. **The compiler hardcodes the correct convention; it must not expose it as a catalog knob.** A
   `OverflowConvention` setting would make a bug into a supported configuration — precisely the
   "make invalid state unrepresentable" failure. One convention, one owner, no switch.
3. **Fixing it is a prerequisite with a migration**, not a compiler concern — and it should be scheduled
   on the bug's urgency, not on this design's. Ignixa's existing rows carry the remainder convention;
   correcting the writer without reindexing would break long-string search. This belongs with
   `worktree-sql-datalayer-architecture`, which is already consolidating exactly these conventions
   (`StringStorage`, the 128→256 threshold, collation convergence). Tracked separately from this design,
   and **not gated behind it**: the fix is worth landing even if every other word in this document is
   rejected.

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
   solves this via `TOutput VisitIn<T>(InExpression<T>, TContext)` — a method the SQL layer never uses.
   (Precisely: it is declared on the interface and implemented by exactly one visitor,
   `InMemory/SearchQueryInterpreter.cs:254`, whose body is
   `throw new NotImplementedException("InExpression is not yet supported for in-memory search.")`. The
   dispatch mechanism is built and wired; nothing has ever put anything through it.)
3. **Resolution happens during traversal, asynchronously.** `GetSearchParamIdAsync` /
   `GetResourceTypeIdAsync` perform I/O mid-walk. A compiler resolves symbols up front.

### The motivating bug

`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/CompartmentSearchProblem.txt` (3,289 lines) records a
compartment search where the old server's hand-written CTE-per-reference-parameter SQL completes and the
EF-generated equivalent times out. The problem is not that EF emits *wrong* SQL — it is that there is no
way to tell EF to emit *that shape*. Owning a plan IR and a SQL AST means emitting the shape because we
chose it.

This case becomes a named regression test (see Testing).

#### The confound: the working query is not only a different shape

**This document's motivating evidence is not clean — the two queries differ in plan strategy as well as
shape.** Stating the mechanism precisely matters, because the obvious reading of the artifact is wrong.

**What the artifact does *not* show.** `CompartmentSearchProblem.txt:855` reads `OPTION (RECOMPILE)`, and
`:856` reads `-- execution timeout = 180 sec.` **Neither was sent to SQL Server.** That file is captured
*logger output*, not a captured command. `SqlServerSearchService.cs:1723-1726` appends both lines to a
logging `StringBuilder` — never to the command — with the comment *"enables query compilation with
provided parameter values in debugging"*: it exists so a human can paste the logged text into SSMS and get
a sniffed plan. fhir-server confirms this classification itself in `StripQueryPreambleLines`
(`SqlServerSearchService.cs:981-1028`), which strips `OPTION (RECOMPILE)` and `-- execution timeout` as
*"diagnostic preamble lines"* before Query Store text matching. **Do not conclude that the working query
ran with `RECOMPILE`. It did not.**

**What the artifact *does* show, which matters more.** Line 847 carries
`/* HASH LZDrhy6nd9yShjFabAXdEq4o+LXDgDsQAAsbbZEr2/0= params=@p0 */` — emitted by
`SqlQueryGenerator.AddParametersHash` into the **real** command text, because `ReuseQueryPlans` defaults
to **`false`** (`Registration/FhirSqlServerConfiguration.cs:16`). That comment makes the SQL text a
function of the *parameter values*, so each distinct value set gets its own cache entry and its own plan
compiled against its own values. (`SqlQueryHashCalculator.RemoveParametersHash` exists solely to undo this
for Query Store matching — which is only necessary because the hash really is in the query.)

So the plan-strategy difference is real, but it is **per-value cached plans, not per-execution
recompilation**. The distinction is the whole design space: `RECOMPILE` pays compilation on every
execution; the hash pays once per distinct value set and then caches. Meanwhile Ignixa's EF read path has
**no plan strategy at all** — no hints, no interceptor, no tagging (verified exhaustively; the only hinted
SQL in the repo is Microsoft's auto-generated `Resources/97.sql`, serving *write*-path stored procedures
that EF search never calls) — and it passes `SearchParamId` as a parameter, so one plan is shared across
every search parameter of a given type.

**Therefore the cheapest experiment in this document is not step 0 — it is step −1:**

> Give the existing EF compartment query a value-specific plan and re-run the timing-out case.
> `OPTION (RECOMPILE)` via a `DbCommandInterceptor` is the cheapest lever, since EF cannot readily
> reproduce the hash trick. It is not what fhir-server does, but it isolates the same variable.

If the timeout disappears, this document's motivating bug was substantially a plan problem wearing a shape
problem's clothes, the honest first fix is roughly ten lines in an interceptor, and the case for owning a
plan IR must be re-argued on other grounds. If it does not, the shape argument stops being an assumption
and becomes a measured result — and step 0 proceeds with the strongest evidence available.

This is a few hours of work and it gates the justification for everything else here. It was not run before
this document was written; it should be run before this document is accepted. Inheriting a comparison with
an uncontrolled variable is exactly the sloppiness *The design principle* refuses to tolerate elsewhere —
and the first draft of this very section misread a log dump as a query, which is the same failure at one
remove.

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

#### The grammar above is incomplete: it has no Resource-table seed

Every producer in that grammar reads a *search-parameter* table. Nothing produces "the resources of type
T", and `Except` needs exactly that as a left side. `Patient?name:missing=true` is "all Patients EXCEPT
those with a name row" — and the left side is unrepresentable.

This is verified, not theoretical. fhir-server has a dedicated node kind for it,
`SearchParamTableExpressionKind.All`, whose emitter
(`Features/Search/Expressions/Visitors/QueryGenerators/SqlQueryGenerator.cs:761-766`) is:

```sql
SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1
FROM dbo.Resource
WHERE <history clause> AND <deleted clause> AND <optional resource-column predicate>
```

It is a node rather than a special case because **four** distinct constructs need it:

| Construct | Seed site |
|---|---|
| `:missing=true` | `MissingSearchParamVisitor.cs:44` — `Kind.All` |
| `_id` and other Resource-column predicates | `ResourceColumnPredicatePushdownRewriter.cs:58` |
| `_include` match seeding | `IncludeMatchSeedRewriter.cs:38` |
| `:not` as the sole parameter | `NotExpressionRewriter.cs:37-45` — seeded `Kind.Normal`; see below |

**The `_id` row is the one that matters, because it means the leaf-type list is wrong rather than merely
short.** "String, token, date, number, quantity, uri, reference" are the *search-param tables*. But `_id`,
`_lastUpdated`, and `_type` are **Resource columns**, and no `ILoweringRule<TSearchValue>` over a
search-param table can express them. fhir-server says so in a comment
(`ResourceColumnPredicatePushdownRewriter.cs:56-57`):

> *"There is a predicate over `_id`, which is on the Resource table but not on the search parameter
> tables. So the first table expression should be an `All` expression […]"*

— and structurally, it keeps these in a **separate `ResourceTableExpressions` collection** on its root,
distinct from `SearchParamTableExpressions`, with a dedicated
`ResourceTableSearchParameterQueryGenerator`. `QueryPlan` as sketched above has no equivalent field and
no equivalent rule tier. This is a missing *concept* — resource-level predicates — not a missing case.

Note also what the seed carries: `AppendHistoryClause` and `AppendDeletedClause`
(`SqlQueryGenerator.cs:750-751`). Where `IsHistory` / `IsDeleted` attach in this design is unspecified,
and a seed that omits them is not slow — it is **wrong**. The reorderer also ranks `All` highest
(`SearchParamTableExpressionReorderer.cs:29-31`, priority 20) so it narrows before joins, which is a
shape decision `Lower` would have to own.

**Why this was missed is itself the argument.** In the EF path the seed is *ambient*:
`SqlEntityFrameworkSearchService.cs:304-305` builds `baseQuery = _context.Resources.Where(r => !r.IsHistory
&& !r.IsDeleted)` and threads it as a parameter through every call, so every negation reaches for it and
finds the Resource table already in scope. A CTE graph makes each CTE standalone — which is correct — and
the ambient left side evaporates. **The gap is an artifact of doing the right thing**, which is precisely
why it must be reified rather than assumed.

**Resolution: two leaf producers, each carrying its own invariants.**

```csharp
CteDefinition =
    ParamSource    (SearchParamTable table, short SearchParamId, Predicate p)  // SearchParamId REQUIRED
  | ResourceSource (short? ResourceTypeId, Predicate? p)                       // dbo.Resource; IsHistory/IsDeleted baked in
  | Intersect (CteRef, CteRef)
  | Union     (CteRef[], All|Distinct)
  | Except    (CteRef, CteRef)
  | ChainJoin (CteRef, direction, ...)
```

The rejected alternative is instructive. Generalizing the existing node — `Source(SqlTable, short?
SearchParamId, Predicate?)` — is more uniform and matches fhir-server's own model
(`NotReferencedQueryGenerator.Table => VLatest.Resource`). **Reject it:** a nullable `SearchParamId` makes
`Source(TokenSearchParam, null, …)` representable, and that node *is* fhir-server's live `:not` defect,
reified into the new grammar's type system (`HandleTableKindNormal:648-651` with a null predicate emits
`FROM dbo.TokenSearchParam WHERE IsHistory = 0` — every row, every `SearchParamId`). This document already
rejected `OverflowConvention` for exactly this reason. Same call, different bug.

The split buys two unrepresentable defect classes: `ParamSource` cannot omit its `SearchParamId`, and
`ResourceSource` cannot omit the version filter — so the naive-seed-forgets-`IsHistory` bug stops being
something to remember.

`Patient?name:missing=true` lowers to `cte0 = ResourceSource(103)`,
`cte1 = Except(cte0, ParamSource(StringSearchParam, 202))`.

**Tiering:** `ResourceSource` is both, and that is fhir-server's own split rather than a fudge. As a
*leaf*, `_id` / `_type` / `_lastUpdated` get ordinary tier-1 rules that happen to address a different
table. *Seed synthesis* is tier-2 structural — the `Not`→`Except` rule asks whether the `Except` has a
left and synthesizes one if not, mirroring fhir-server's `if (i == 0)`. Only the structural visitor knows
the siblings.

**Cost, and the rule that keeps it off the plan.** `Resource.sql` (and Ignixa's `97.sql:627-630`,
identical) already defines exactly the seed's column list as a filtered, covering, partition-aligned
index:

```sql
CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId
    ON dbo.Resource(ResourceTypeId, ResourceSurrogateId) WHERE IsHistory = 0 AND IsDeleted = 0
    ON PartitionScheme_ResourceTypeId(ResourceTypeId);
```

A range scan in one partition, not a heap scan. (The `SurrgateId` typo is real, in both repos.) It remains
O(resources of type T), which is inherent to the question. The mitigation is a normalization rule
fhir-server already ships: **reorder so negations run last**
(`SearchParamTableExpressionReorderer.cs:29-38` scores `All` at 20, `Missing` at −10, `Not` at −15, and
`SqlServerSearchService.cs:2312-2314` runs reorder *before* the seeding visitors). So
`Patient?gender=male&name:missing=true` never seeds — the seed is a fallback for the degenerate
sole-negation query, not a tax on every negation.

**Two open decisions this document must make rather than inherit:**

1. Whether Resource-column predicates become a `ResourceSource` CTE or an outer `WHERE`. fhir-server chose
   the outer `WHERE` via a two-part root (`SearchParamTableExpressionQueryGeneratorFactory.cs:103-106`
   returns `null` for Resource-column params; `SqlRootExpressionRewriter` routes them to
   `ResourceTableExpressions`). `QueryPlan` currently cannot say either.
2. **Pin `:not` semantics before step 5.** Compiling `:not` per spec is a deliberate, silent behaviour
   change to a shipped Microsoft product once step 10 lands. That needs a decision with a name on it, and
   the upstream bug is worth reporting regardless of what this design does.

#### A found bug, and what it argues

While confirming the above, a divergence surfaced that is worth recording because it is the same disease
as the storage convention. fhir-server seeds `:not` with `Kind.Normal` (`NotExpressionRewriter.cs:44`),
which emits `FROM <searchParamTable>` (`SqlQueryGenerator.cs:637-651`) — **not** `Kind.All`'s
`FROM Resource`. So when `:not` is the first or only parameter, a resource with zero rows in that param
table is absent from the seed and silently dropped. The FHIR spec is explicit that it must be returned:

> *"Tests whether the value in a resource does not match the specified parameter value. **Note that this
> includes resources that have no value for the parameter.**"*

Ignixa is accidentally the more spec-faithful of the two: its EF path starts from `_context.Resources` and
does `.Except(innerMatchingIds)` (`Search/SearchParameterQueryGenerator.cs:1512-1533`), so the universe is
always the Resource table and no seed is needed. Neither repo tests the case — every `:not` E2E test pins
`_tag=` (e.g. `test/Ignixa.Api.E2ETests/Search/DataTypes/TokenSearchTests.cs:155`), which supplies a
resource-level predicate and suppresses the seed path entirely. Traced statically; not executed.

The argument this makes is the document's own: the correct universe for a negation is a *storage-shape*
decision, it had no owner in either implementation, and it drifted — invisibly, into a spec violation, in
the codebase this design treats as authoritative. `Lower` owning the seed node is what makes that
decision reviewable in one place.

### `Lower` is three tiers, and the boundary is load-bearing

The per-type rule pattern works because it reflects the domain — but an earlier draft stated the premise
too strongly, and composites falsify half of it.

The draft said: *"each search-param type maps to exactly one table, and the types do not interact —
`StringSearchParam` knows nothing about `TokenSearchParam`. That independence is real, not imposed."*
The first half holds. The second half is false: in `TokenStringCompositeSearchParam` the token and string
types do not merely know about each other, **they share a row** — `SystemId1, Code1, Text2,
TextOverflow2, CodeOverflow1` (`TokenStringCompositeSearchParam.sql:6-10`). A rule emitting the string
component must address `Text2`, not `Text`, and can learn that only from the composite containing it.

The honest premise:

> **Each search parameter maps to exactly one table, and one predicate lowers to one `ParamSource` over
> it. Component types within a composite share a row, so they interact through *column addressing* — but
> not through *semantics*. That interaction is confined to the catalog's role→column binding, and it is
> the only place tier-1 types meet.**

That restatement is worth making in the document's own idiom, because it names the pair being un-braided
— **predicate semantics ←→ column addressing** — which the original premise denied existed. The composite
path is precisely where denying it produced shipped bugs (below).

**It does not extend to structure, and pretending it does is precisely how `Lower` becomes the next
`SqlQueryGenerator`.** A chain is not a rule about `StringSearchValue`; it wraps other predicates.
`_include` is not a predicate at all. Sort affects the outer query, not any CTE. Forcing these into
`ILoweringRule<T>` grows the context object a field per special case — which is how the spike's
`ParserOptions` accreted `CteName` / `LastCteName` / `ChainLevel` / `ParentIsForwardChain` / `Sort` /
`SortQuerySecondPhase` / `SortContinuationToken`. Same trap, different door.

| Tier | Responsibility | Expected size |
|---|---|---|
| **Leaf rules** | `ILoweringRule<TSearchValue>` → `ParamSource` / `ResourceSource` CTE. One per param type. Owns table, column *role*, storage convention, collation. | 7 × ~40 + 6 × ~20, plus ~200 of catalog role-mapping — see *Composites* |
| **Structural lowering** | One visitor over the semantic tree. `And`→`Intersect`, `Or`→`Union`, `Not`→`Except`, `Chained`→`ChainJoin`. Dispatches leaves to tier 1. | small — *if* tier 1 is complete |
| **Result-shape stages** | `_include` / `_revinclude` / `:iterate`, sort, continuation. Produce `IncludeStage` / `SortSpec` / `PageSpec`, never CTEs. | the hard part |

The evidence that tier 3 is genuinely separate is that **both prior designs discovered it independently,
from opposite directions**. fhir-server needed `IncludeRewriter`, `IncludeMatchSeedRewriter`, and
`IncludesOperationRewriter` as distinct passes (1,677 + 431 + 440 lines of tests between them). The spike
needed `IncludeSqlParser` and `RevIncludeSqlParser` as separate classes outside its per-type parsers. Two
architectures that agree on nothing else agree that includes do not decompose per-type. That is worth
believing in advance rather than rediscovering.

### The tier boundary is a type, not a convention

An earlier draft left the split as a naming discipline, and admitted under *Honest caveats* that "tiers 1
and 2 are a convention, not an invariant." That admission was the design contradicting itself. Everywhere
else this document replaces convention with reachability — `Lower` cannot see SQL text, `Emit` cannot see
an unparameterized user value, a leaf rule cannot read an escaped string. At the one place the document
names as its **worst** risk, it fell back to "please don't."

Apply the same trick. Give each tier its own context type, and the boundary enforces itself:

```csharp
// Tier 1: can mint parameters and column predicates. Cannot see or allocate a CteRef.
sealed class LeafContext
{
    public SymbolTable Symbols { get; }
    public SqlParameterRef Parameter(string value);
    public SqlParameterRef Parameter(long value);
    // no CteRef; no Intersect/Union/Except; no access to sibling predicates
}

// Tier 2: the only place CteDefinitions are constructed.
sealed class StructuralContext
{
    public CteRef Lower(SearchParameterPredicateExpression leaf);   // dispatches to tier 1
    public CteRef Intersect(CteRef a, CteRef b);
    public CteRef Union(IReadOnlyList<CteRef> parts, UnionKind kind);
    public CteRef Except(CteRef left, CteRef right);
    public CteRef ChainJoin(CteRef inner, ChainDirection direction, ...);
}

// Tier 3: receives the finished match graph read-only; may only append result-shape stages.
sealed class ResultShapeContext
{
    public CteRef Match { get; }                 // read-only handle
    public void AddInclude(IncludeStage stage);
    public void SetSort(SortSpec sort);
    public void SetPage(PageSpec page);
    // cannot construct or mutate a CteDefinition
}
```

A chain rule now *cannot* be written inside a leaf rule: `LeafContext` has no `CteRef` to chain. Include
seeding *cannot* contaminate the match graph: `ResultShapeContext` cannot construct a CTE. And the
`ParserOptions` failure mode the spike demonstrated — `CteName` / `LastCteName` / `ChainLevel` /
`ParentIsForwardChain` / `Sort` / `SortQuerySecondPhase` / `SortContinuationToken` accreting onto one bag
— has nowhere to accrete, because there is no single context object to grow. Adding a `CteRef` to
`LeafContext` to make some chain case work is then a visible, reviewable admission that the tier boundary
was drawn wrong. It is not a quiet slide.

This does not stop `Lower` from being *large* — tier 3 can still sprawl, and probably will. It stops
`Lower` from being *braided*, which is the failure mode `SqlQueryGenerator` actually exhibits. Size is not
the disease; see *The design principle*.

### Composites: the interaction is column addressing, and it is where drift actually happened

Composites were absent from an earlier draft — from the leaf table, from step 5's type list, and from the
tier discussion. They are six more tables (`TokenToken`, `TokenQuantity`, `TokenString`, `TokenDateTime`,
`TokenNumberNumber`, `ReferenceToken`), so **step 5's list should read thirteen types, not seven.**

**Same-row costs nothing, and could not be bought any other way.** A composite is one parameter in one
table, and both servers conjoin every component predicate into a single `WHERE` over a single alias —
there is no join, so there is nothing to enforce. fhir-server's entire mechanism is a delegating generator
holding the *unmodified* base-type generators (`CompositeQueryGenerator.cs:11-35`), and
`TokenQuantityCompositeQueryGenerator.cs` is twenty lines. Component addressing is one line —
`AppendColumnName(...)` appends `componentIndex + 1` to the base column name
(`SearchParameterQueryGenerator.cs:347-350`), so `TokenQueryGenerator` passes
`VLatest.TokenSearchParam.SystemId` and `SystemId1` comes out.

This settles the grammar question: **a composite is a `ParamSource` whose `Predicate` is a conjunction.**
No third producer, no change to `Intersect`, no change to `Union`. And the tempting alternative — a
tier-2 `SameRow(CteRef…)` combinator joining component CTEs — is not a trade-off but an **impossibility**:
the composite tables have no row identity to join on. `IXC_TokenQuantityCompositeSearchParam` is
`(ResourceTypeId, ResourceSurrogateId, SearchParamId)` and **non-unique**
(`TokenQuantityCompositeSearchParam.sql:18-26`); there is no identity column and no unique key. The only
constructible join key is exactly the granularity that *loses* the same-row constraint.

**The real hazard is dispatch order, not structure.** The parser builds one
`SearchParameterExpression(compositeParam, Or(And(comp0, comp1)))` with `ComponentIndex` on the leaves
(`SearchParameterExpressionParser.cs:100-160`). If tier 2's generic `And`→`Intersect` rule ever sees that
`And`, it emits `Intersect(ParamSource(TokenSearchParam), ParamSource(QuantitySearchParam))` — wrong
tables, and semantically *"some row has the code, some row has the value"*, which is the precise FHIR bug
composites exist to prevent. **The composite `And` must be consumed by tier 1 before tier 2 descends into
it.** That is an invariant this document must state.

**Rules must name roles, not columns.** This is the one substantive addition, and it fixes a live bug
class. fhir-server's composite path reads the **base** table's column metadata and applies it to a
**different column in a different table**: `StringOverflowRewriter.cs:34,41` chooses `Text2` vs
`TextOverflow2` using `VLatest.StringSearchParam.Text.Metadata.MaxLength`, and `TokenQueryGenerator.cs:51`
picks the `Code1` overflow branch from `VLatest.TokenSearchParam.Code.Metadata.MaxLength`. That is correct
only while the numbers coincide, and they already don't:

| Base column | Composite column | Divergence |
|---|---|---|
| `StringSearchParam.Text` — `Latin1_General_100_CI_AI_SC` | `TokenStringCompositeSearchParam.Text2` — `Latin1_General_CI_AI` | different collation version; no supplementary-char support |
| `QuantitySearchParam.LowValue/HighValue` — **NOT NULL** | `TokenQuantityCompositeSearchParam.LowValue2/HighValue2` — **NULL** | `QuantityQueryGenerator.cs:30-33` branches on a `notNullableValueColumn` that isn't |
| — | `TokenNumberNumberCompositeSearchParam.HasRange` | **unsuffixed, no base counterpart** — the suffix convention does not cover its own tables |

So the leaf context resolves a **role plus ordinal** against the table actually being emitted into:
`ctx.Column(ColumnRole.TokenCode)` binds `Code` against `TokenSearchParam` and `Code1` against
`TokenQuantityCompositeSearchParam` — carrying *that column's own* `MaxLength` and collation. A rule
cannot name a column, only a role. Same trick as `ctx.Parameter()`: not discipline, no API. Note this is
the `_indexedTextMaxLength`-versus-`const 256` example one level worse — not two constants agreeing by
someone remembering, but **one constant standing in for a different column entirely**.

**This is not a speculative risk.** An earlier draft called composite drift "unchecked" and listed it as a
candidate for a future audit. `worktree-sql-datalayer-architecture` has already run that audit and it came
back loaded —
roughly sixteen composite commits, including `96cf279a` (token code overflow threshold **128→256**, write
path), `9d504845` (composite string stored in original case at the correct width), `07f6b412` and
`bb43f052` (read-path convergence for token and string), `426c84b6` (ReferenceToken misordered
components), and `2e19ba51` (*"Fix composite DateTime `sa`/`eb` silently applying no filter"*).
**The composite path — not `StringSearchParam` — is where Ignixa's storage conventions actually drifted**,
and it is a stronger lead example than the `TextOverflow` bug: four-plus independent drifts in one code
path, every one of them the semantics ←→ addressing pair.

**A fourth silent defect, found in passing.** `SearchParameterQueryGenerator.cs:180-198` dispatches five
composite types and has **no arm for `TokenNumberNumber`**; it falls through to
`_ => Enumerable.Empty<long>().AsQueryable()` (`:197`) — no error, no log, a wrong empty result. It is
also structurally stuck: every arm passes `componentExpressions[0], componentExpressions[1]`, and
`TokenNumberNumber` has three components. Same category as the `TextOverflow` bug, and it earns the same
named-regression-test treatment.

**Sizing.** Tier 1 is ~550–650 lines, not 280: seven base rules (~280, unchanged in size — they name roles
instead of column constants), six composite rules (~120; empirically, fhir-server's is 22 lines including
the licence header), ~150–250 of role→column catalog data, and ~20 for the dispatch invariant. Composites
are ~40% of tier 1, and most of that 40% is *catalog facts the DDL already states* rather than lowering
logic. Note the direction of the surprise: composite rules are **smaller** than base rules, because they
delegate.

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
- **Plan reuse is structural** — `name=Smith` and `name=Trudeau` emit byte-identical SQL differing only in
  parameter values, so SQL Server can reuse the plan. **But reuse is not unconditionally the goal, and an
  earlier draft cited its own evidence backwards.**

  `QueryPlanReuseChecker` does not exist to protect plan reuse. It exists to **detect skew and refuse
  it**: it reads `dbo.GetResourceSearchParamStatsProperties`, which runs
  `DBCC SHOW_STATISTICS … WITH HISTOGRAM` and returns a skew ratio of `MAX(eq_rows) / MIN(eq_rows)`; any
  search parameter whose skew exceeds a threshold (default 30) is marked skewed, `CanReuseQueryPlan`
  returns `false`, and the query recompiles rather than reuse a parameter-sniffed plan. fhir-server also
  emits `OPTION (OPTIMIZE FOR UNKNOWN)` for `identifier` + `_include`
  (`SqlQueryGenerator.cs:429-435`), and lazily creates **filtered statistics per
  `(ResourceTypeId, SearchParamId)`** at query time (`Sprocs/CreateResourceSearchParamStats.sql`, driven
  from `SqlServerSearchService.cs:508`) so the optimizer gets per-parameter density instead of one blended
  histogram.

  That is a production-hardened answer to a problem this document had not acknowledged: **full
  parameterization buys exactly one cached plan per shape, and for skewed FHIR data one plan is sometimes
  exactly wrong** (`status=final` at 50M rows and `status=entered-in-error` at 12 share a SQL text).
  Determinism guarantees it rather than mitigating it.

  The invariant stands — injection defense is not negotiable. But the fix is an application of this
  document's own rule, not a concession against it. **"Parameterization" braids two concerns:** *value
  transport* (how a user value reaches SQL Server without becoming syntax) and *plan-cache identity*
  (which values share a plan). Un-braid them and the escape hatch falls out for free — value transport
  stays absolute, plan-cache identity becomes a decision.

  **Resolution.** Skew is a fact about a symbol, so it belongs with the other symbol facts: `SymbolTable`
  gains `bool IsSkewed(SearchParameterInfo)`, populated during `Resolve` from the same histogram query
  fhir-server already runs. `Resolve` is this design's *only* I/O seam, so this costs no new stage and no
  new concept — skew becomes a symbol property exactly as `SearchParamId` already is, and `Compile` stays
  a pure synchronous function of (IR, `SymbolTable`, `SqlCatalog`), unit-testable by hand-building a
  `SymbolTable` with a skewed parameter and asserting the emitted keying. No database.

  The actuator is fhir-server's own: emit its parameter-value hash comment at `Emit` behind
  `PlanCacheKeying { PerValue, PerShape }`, defaulting to `PerValue` — which *is* fhir-server's shipped
  default (`ReuseQueryPlans = false`), so this lands on their behaviour rather than silently reversing it.
  A base64 SHA-256 of parameter values is **not a user value**: it is injection-inert by construction, so
  invariant test #1 still passes unmodified. Roughly 40 lines. Keep `OPTION (OPTIMIZE FOR UNKNOWN)` as a
  third gear — and note it is *better* under this design than under fhir-server's, because with a literal
  `SearchParamId` the "average density" it falls back to is the per-parameter filtered histogram's
  average, not a table-wide one.

  **Do not port a hand-curated shape→hint table.** That is a statistics-driven plan-family mechanism with
  a human in the refresh loop: it adds the same concern this document cut the cost model for, *plus*
  staleness, *plus* an ops burden. fhir-server's version reads the database's own histograms and refreshes
  hourly. If this machinery must exist, use the one that does not rot.

  Two things make the residual tractable. The design literalizes `SearchParamId`, so each parameter
  already gets its own SQL text and its own plan — collapsing exposure from *cross*-parameter (unbounded:
  a `status` plan applied to an `identifier` point lookup) to *within*-parameter. And that is a **strict
  improvement on today**: Ignixa's EF path passes `SearchParamId` as a sniffable parameter
  (`@__searchParamId_Value_N`) under a `!x.HasValue ||` OR-branch, so one plan is currently shared across
  every search parameter of a given type. The compiler is not introducing this problem — it is inheriting
  it, and this should be the first place it improves on the status quo deliberately rather than by
  accident.

`PlanShapeHash` derives from the existing `Expression.AddValueInsensitiveHashCode`, implemented on every
AST node and consumed by nothing in Ignixa. It is a fine identifier for golden tests and for
shape-stability assertions.

**It is not, however, a viable key for a skew/hint policy, and an earlier draft implied otherwise.** It is
value-insensitive *by construction* — which is the wrong axis for a policy whose entire job is telling
values apart. `CompartmentSearchExpression.AddValueInsensitiveHashCode` (`:60-64`) hashes `CompartmentType`
and deliberately **not** `CompartmentId`, so `Patient/mega-patient` and `Patient/normal-patient` produce
the *identical* hash. That is the headline skew case, and the key cannot see it. (Its
`ValueInsensitiveEquals` at `:66-68` *does* compare `CompartmentId`; the hash is deliberately coarser.)
The provenance explains why: it was added for Cosmos cross-partition parallelism, not for plan reuse.
`PlanShapeHash` can express "this *shape* is always skew-prone" — a dictionary lookup at `Emit`,
negligible — and nothing per-value. Claim no more for it than that.

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
- **The live risk is `Lower` becoming the new `SqlQueryGenerator`.** Per-tier context types (see *The tier
  boundary is a type, not a convention*) make the tier-1/tier-2 boundary an invariant rather than a
  convention, which is the mitigation the earlier draft lacked. But they bound *braiding*, not *size*:
  nothing stops tier 3 growing to 2,000 lines, and tier 3 is exactly where chains, includes, and
  `:iterate` are inherently cross-cutting. That is where both prior designs got hard, and where the
  spike's probe will produce the most useful evidence. The honest position is that this design has a
  structural answer for the tiers it understands and a hope for the one it does not.

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

### That argument is wrong, even though the conclusion currently holds

"Determinism means a compiled query either works or does not" is a claim about **one query in isolation**.
Pagination is state carried across two requests that may hit different code. Determinism of each request
says nothing about the pair: the flag is a property of a *deployment*, and a paginating client straddles
deployments — in both directions, since rollback is advertised as a cheap DI swap. Golden tests cannot
establish this before deploy, because there is no single query to test; the bug would live in the seam
between two.

What actually saves the flip today is not determinism but an accident: **Ignixa's continuation token
carries no plan state.** `Core/Ignixa.Search/Models/ContinuationToken.cs:22-33` encodes exactly
`Base64(JSON{Offset, Count})` — no shape, no generator version, no phase, no aliases. Structurally it is
issued by the *serializer*, not the data layer (`StreamingBundleSerializer.cs:226-234` computes
`nextOffset = currentOffset + pageSize`; `SearchResourcesHandler.cs:145` passes `ContinuationToken: null`
with the comment "Serializer will generate this"), and the data layer only consumes it as
`Skip(offset).Take(pageSize)`. fhir-server's `sentinelSortValue` phase-in-the-token hazard is therefore
inert for Ignixa — it is a step-10 problem, not a step-9 one.

**So state it as an invariant rather than a consequence:**

> **`PageSpec` must be plan-independent.** If it ever encodes a surrogate id, a sort phase, or any other
> artefact of the chosen plan, the flag flip is no longer atomic and tokens must be versioned first.

This is not hypothetical: `PageSpec Page;  // TOP + continuation` invites keyset pagination, deep `OFFSET`
is exactly what fhir-server abandoned, and step 8 could silently break the property the cutover silently
depends on. Choosing keyset is defensible — but it converts this section from "right" to "wrong" and must
be a conscious trade, not a side effect. Note the asymmetry that makes it worse: a compiled path *can* be
taught to read a legacy offset token, but a legacy path **cannot** turn a surrogate id back into an offset
without a count query. Keyset breaks precisely the rollback direction this document calls cheap.

### The token format has no owner either — and it is already broken

Verified, and independent of this design: **a foreign token silently truncates the result set.**
`ContinuationToken.TryDecode` (`:42-71`) deserializes into a private `PaginationState { int Offset; int
Count; }`. `System.Text.Json` ignores unknown members and defaults missing ones, so any unrecognized JSON
*object* deserializes to a non-null instance with `Offset = 0, Count = 0` and **returns `true`**. The
`count = 10` fallback at `:45` is overwritten at `:64` before the caller sees it.

Downstream (`Search/SqlEntityFrameworkSearchService.cs:638-650`): `offset = 0`, `pageSize = tokenCount + 1
= 1`, and it logs at **Debug** (`:645`) — the `LogWarning("Invalid continuation token")` at `:649` sits on
the `else` branch and never fires. The client receives HTTP 200, a one-entry bundle, and **no `next`
link**: pagination terminates and the client believes it holds the complete result set. Silent truncation,
unlogged.

This fires *today*, with no compiler involved. `IncludesResourceHandler.cs:151-229` uses the same
base64-JSON envelope with different field names (`IncludesOffset` / `PageSize`), so the two token types
mis-decode into each other. Tellingly, the includes token *does* range-validate (`:205-213`, rejecting
`PageSize < 1`) — exactly the guard whose absence in the base token produces the one-row truncation. And
`Core/Ignixa.Search/Resources.resx:269` defines `InvalidContinuationToken`, which is **dead code with zero
call sites**: Ignixa ported the string from fhir-server and dropped the throw. fhir-server fails closed
with a 400 in both parse layers; Ignixa never returns 400 for a bad token, and its one E2E test
(`IncludesOperationTests.cs:319-342`) accepts *either* 200 or 400, asserting nothing.

Two tokens, one envelope, no discriminator, nothing owning the format. **This is the storage-convention
post-mortem a third time**, in a third place, and it should be fixed on its own merits — before the
compiler exists, so it is not later misattributed to the cutover. There is no TTL, expiry, or timestamp
anywhere (`PaginationState` is two ints), so tokens live in a `next` URL indefinitely and "drain then
flip" is not available.

## Testing

Beyond the golden corpus and differential suite, three invariant tests each make a bug class
unrepresentable:

1. **No user value appears in SQL text.** For every query in the corpus, assert each user-supplied value
   appears in `Parameters` and is absent from `Sql`.
2. **Value-insensitive shape stability.** `date=2013&name=Smith` and `date=2014&name=Trudeau` must emit
   the same **`PlanShapeHash`**, differing only in parameter values — asserted on the *plan*, not on the
   SQL text.

   **The obvious version of this test is a trap, and an earlier draft fell into it.** Asserting *identical
   `Sql`* would encode "one plan per shape, always" as an invariant — and that is precisely the property
   fhir-server disables by default. With `ReuseQueryPlans = false`
   (`Registration/FhirSqlServerConfiguration.cs:16`) it appends a parameter-value hash to the query text
   (`SqlQueryGenerator.AddParametersHash`), deliberately making those two queries emit *different* SQL so
   each gets a plan compiled for its own values. A test demanding byte-identical SQL would fail against
   Microsoft's shipped default and would permanently outlaw the escape hatch this design needs for skew
   (see *The SQL AST invariant*). Assert the shape is stable; leave the emitter free to decide whether two
   stable shapes should share a cache entry.
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
5. Plan IR + `Lower` **tier 1** (leaf rules) for **thirteen** parameter types — the seven base types
   (string, token, date, number, quantity, uri, reference) plus the six composites (`TokenToken`,
   `TokenQuantity`, `TokenString`, `TokenDateTime`, `TokenNumberNumber`, `ReferenceToken`) — and the
   catalog's role→column binding they require (see *Composites*), plus **tier 2** (structural lowering). Encodes the storage
   conventions that `worktree-sql-datalayer-architecture` is consolidating — **blocked on** the
   `TextOverflow` convention fix and the drift audit (see Risks).
   `Explain()` lands here; golden tests assert on plans from this point on.
6. Chain and reverse chain.
7. Include, revinclude, iterate.
8. Compartment (the `CompartmentSearchProblem` case), sort, continuation.
9. Wire into `Ignixa.DataLayer.SqlEntityFramework` behind `UseCompiledSearch`; differential suite.
10. `microsoft/fhir-server`: adopt `Ignixa.Search`, wire the compiler, retire `SqlQueryGenerator`.

### Step 0 — the proving increment

The sequence above proves the easy thing first. Steps 1–5 stack three external dependencies — a
69-file/14,293-line front-end PR, a cross-repo IR port, and a data migration plus an unscoped drift audit
— in front of the first compiled query. **None of them are needed to test the riskiest claim in this
document.**

That claim is not "a compiler can emit a string predicate." The spike already demonstrated that in 24
lines, and tiers 1–2 are the part both prior efforts found easy — the spike's own tail
(`ReversedChainSqlParser` at 262 lines against `StringSqlParser`'s 24) says so. The claim nothing has
tested is: **owning a plan IR actually lets us emit the shape that performs.** That is the
`CompartmentSearchProblem.txt` case, where hand-written CTE-per-reference-parameter SQL completes and the
EF-generated equivalent times out.

That case needs neither #332 nor the string migration. It is a *reference-param compartment search*: no
string overflow, so the `TextOverflow` convention is irrelevant to it; and no parser work, because the
semantic IR for one query can be hand-constructed in a page of code or adapted from the existing
`Expression` tree.

**Step 0:** minimal `SqlAst` + emitter + the reference-param leaf rule + `ChainJoin`, compiled from a
hand-built semantic IR, differentially tested against the EF path on the `CompartmentSearchProblem` case.
It exercises `Resolve`, `Lower`, `Emit`, and the CTE graph end to end on the one query that motivated the
work.

**Run it as a factorial, not an A/B — otherwise it cannot answer the question it exists to answer.**
"Compiler output vs EF output" confounds at least three variables: CTE shape, literal-vs-parameter
`SearchParamId`, and plan-cache keying. If the compiled arm comes back faster, we would not know which one
paid. Four arms, same database, same query, each run cold and warm:

| Arm | What it is | Isolates |
|---|---|---|
| 1 | EF as-is | baseline |
| 2 | EF + `EF.Constant` on `SearchParamId`/`ResourceTypeId`, nothing else | the literal |
| 3 | Hand-written legacy CTE SQL, `SearchParamId` parameterized | the literal, under the good shape |
| 4 | The legacy SQL verbatim | the known-good |

Arm 2 − arm 1 isolates the literal. Arm 4 − arm 3 isolates it again under the good shape. **Arm 4 − arm 2
is the residual that only shape explains — and that number, not the raw EF-vs-compiler delta, is the
actual justification for this compiler.**

The uncomfortable possibility this is designed to expose: the legacy query carries `SearchParamId = 7` as
a **literal** with only `ReferenceResourceId = @p0` parameterized
(`CompartmentSearchProblem.txt:15-17`), which is exactly what lets each CTE seek using the
`(ResourceTypeId, SearchParamId)`-filtered statistic. EF parameterizes it, so every branch of a ~15-way
compartment OR is costed off a blended table histogram. That is a *cardinality-estimation* failure caused
by predicate shape, and it would persist across every plan the optimizer compiles — a far better
explanation of a hard timeout than one unlucky sniff. **If that is right, most of the win comes from
literalizing `SearchParamId`, not from owning a plan IR — and those are separable.** Arm 2 is roughly a
two-line change.

If arm 2 closes most of the gap, the honest conclusion is that the compartment case should stop being this
document's headline. The storage-convention owner and the testability argument stand on their own — the
document says so itself — but the motivating bug would have been an `EF.Constant` away, and it should say
that too. If arm 2 does not close it, the shape argument stops being an assumption and becomes a measured
number, and step 0 proceeds with the strongest evidence available. Either outcome is worth more than any
further paragraph of this document.

This also decouples the step-5 blocker. The `TextOverflow` fix gates only the long-string branch of one
leaf rule, behind a flag that is off by default; it is sequenced here because it is a **live bug owed to
users** (see *The storage-convention divergence*), not because the compiler is waiting on it.

## Risks and prerequisites

**Steps 1–9 are Ignixa-only; step 10 is most of the work and all of the cross-repo risk.** The "both
servers" decision is in practice *designed for both, delivered to Ignixa first, adopted by fhir-server
last*. Named explicitly so step 10 is not estimated as one bullet.

**Price step 10 at zero. It is a free option, not a deliverable.** It requires, at minimum: porting
fhir-server's SMART compartment semantics into `Ignixa.Search` (a prerequisite with no owner — see below);
a major-version binary break on a package published as stable; and winning an organizational contest
against a live in-repo spike that retires the same code by an incompatible route. Any one of those can
stall indefinitely for reasons this document cannot influence.

The planning consequence: **the design must pay for itself on steps 1–9 alone.** It does. The
storage-convention owner, the deterministic CTE shape for the compartment case, and the testability of
`Lower` are all Ignixa-local wins that need no second consumer. If step 10 never happens, what is lost is
the cross-backend *parity* benefit — which this document already classes as a bonus rather than the case
(see *Why the semantic IR earns its keep*). Note also that the `TextOverflow` reindex is **not** a cost
of step 10: it is owed to the `README`'s zero-data-migration promise regardless, and would be worth
landing if this document were rejected entirely. Nothing in steps 1–9 should be shaped around making step
10 cheaper at the cost of making step 9 harder.

**`Ignixa.Search` must absorb fhir-server's semantics before step 10.** It lacks
`SmartCompartmentSearchExpression` and `SmartCompartmentSearchRewriter` (a shipped SMART-on-FHIR feature)
and has no home for the Cosmos compartment rewriter. This prerequisite has no owner today.

**Ignixa's `StringSearchParam` write convention is a data-compatibility defect and must be fixed with a
reindex.** `StringSearchParameterRowGenerator` writes the value's *remainder* to `TextOverflow`;
fhir-server writes the *full value*. Ignixa's requirement is compatibility with fhir-server data, so
fhir-server's convention is authoritative. Correcting the writer without reindexing would break
long-string search against existing rows, so this is a migration, not a code change. It is a
**prerequisite for step 5** (the string lowering rule encodes one convention and must encode the correct
one), and it belongs to `worktree-sql-datalayer-architecture`.

**The drift audit is no longer speculative — it has run, and the composite path is where the bodies are.**
An earlier draft called this "unchecked" and named composites an "obvious candidate". That branch has
since landed ~16 composite commits fixing exactly this category (see *Composites*), so the open question
is not *whether* other types drifted but *what remains*. Two items are known-open and both gate step 5:

- **The overflow threshold has two sources that disagree.** `VLatest.Generated.cs:587,610,626` models
  `Code`/`Code1` as `VarCharColumn(..., 128, ...)` while the DDL says `varchar(256)`
  (`TokenQuantityCompositeSearchParam.sql:7` and its siblings). Ignixa's `96cf279a` ("128 to 256")
  implies 256 is live and the generated model lags — but that is inference. **This is the constant the
  string and token lowering rules encode; settle which source is authoritative before writing them.**
- **`ReferenceToken` ordinal ownership is unresolved and actively changing.** Component ordinals are
  inferred from *values* rather than the definition
  (`SearchParameterExpressionParser.cs:138-157` synthesizes a `SearchParameterInfo` when the definition's
  component type disagrees, to work around DocumentReference `relationship`), while
  `f2d104c9` on the sibling branch moves to extraction "by type instead of `ComponentIndex` heuristics"
  and this branch's own HEAD (`81ccbbd6`) deletes `IsReferenceExpression`/`IsTokenExpression` as
  "superseded by upstream effective-type ordering". **A compiler must decide whether ordinals come from
  the definition or from value inference. Only one can own it.**

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
  composite structure preservation. An earlier draft called this "not colliding, but **converging**". That
  understates it in both directions, and the correction matters.

  **It is ahead of this document, not beside it.** It has already run the composite drift audit an earlier
  draft called "unchecked", and landed ~16 composite fixes (see *Composites*). More
  importantly, commit `289cd2a9` adds **`CompositeComponentExpression` plus
  `IExpressionVisitor.VisitCompositeComponent`**, and `a438048e` wraps composite components in it *at
  parse time*. That is a **semantic-IR grouping node asserting "these predicates share a row"** — the
  exact thing this document's IR sketch lacks, and the thing that neutralizes the tier-2 dispatch hazard
  described in *Composites*. Neither exists on `feature/fhir-to-sql-compiler`.

  Two consequences. First, this design should **adopt that node rather than invent a parallel one**; step
  2's IR port must be reconciled with it, not merged past it. Second, **it is already making the breaking
  `IExpressionVisitor` change this document budgets for.** The risk below treats adding
  `SearchParameterPredicateExpression` to the visitor as *the* binary break needing a major bump; there
  are now at least two such additions in flight from two branches, and they should land as one
  coordinated major version rather than two. That is a collision in a single file, and it needs a
  conversation before step 2 — not step 10.
- **`brendankowitz-simplify-sql-data-layer`** (fhir-server) — holds the north-star spec and the Plan 1
  implementation ported in step 2. Its remaining plans 2–7 are superseded by this document.
- **`personal/rojo/new-sql-parser`** (fhir-server) — an independent parallel probe. Deliberately left
  running; see Prior art and Risks.
