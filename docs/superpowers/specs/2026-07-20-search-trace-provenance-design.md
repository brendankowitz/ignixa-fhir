# Search trace provenance — design

**Date:** 2026-07-20
**Status:** Approved (revision 3 — signed off in principal review)
**Scope:** `Ignixa.Search` (Expressions/Parsers) and `Ignixa.Search.Sql`

## Motivation

We want a developer-facing search playground: paste a FHIR search, see how it parses, how it
compiles, and what SQL it becomes — and trace any one piece back and forth across those stages.
**This spec covers only the library infrastructure that makes such a UI possible.** No UI is built
here.

Today each stage is individually inspectable — the parser produces a typed IR, `QueryPlan.Explain()`
renders a plan, `SqlBuilder.Run` returns SQL plus bound parameters — but nothing connects them. There
is no way to say *"this text produced this predicate, which produced this CTE, which produced this
SQL."* Closing that gap is the whole point.

## Scope

**In scope**

- Source spans on the parse-side syntax and IR nodes.
- A projected, serializable syntax tree so structural provenance is actually reachable.
- A link from IR predicates to the plan nodes they produce.
- A link from plan sections to ranges of emitted SQL text.
- A `SearchTrace` document tying it together, with per-parameter outcomes including failures and
  silently-ignored parameters.

**Explicitly out of scope (deferred)**

- A declarative **capability matrix** (which comparators/modifiers/features each backend supports).
  The existing `NotSupportedException` messages are specific and actionable; surfacing them through
  the trace is sufficient for now. Parameter discovery already works via
  `ISearchParameterDefinitionManager` + `SearchParameterInfo`.
- Any UI.
- Per-*predicate* SQL text ranges (section-level only in v1).

## Guiding principle: one path, not two

The traced path and the production path must be **the same path**. A parallel "tracing mode" is
exactly how `DateTimeEqualityRewriter` silently rotted — it ran against a tree shape the parser had
stopped producing, and nothing noticed because the optimization was invisible when absent.

Consequences, applied throughout:

- Spans are **always populated**, not gated behind a flag.
- `Lower.Run` returns provenance in its normal result rather than via a `RunTraced` twin.
- The trace assembler **wraps the real functions**; it never reimplements them.

### The inversion risk

There is no production orchestration today — tests hand-assemble `Resolve → Lower → SqlBuilder`. The
trace assembler will therefore be the **first** orchestrator, which inverts the usual risk: when the
compiler is later wired into production, if that wiring writes its *own* orchestration, the assembler
becomes the divergent parallel path.

**Requirement:** name the single orchestration entry point now —
`SearchCompiler.CompileAsync(...)` in `Ignixa.Search.Sql`, returning plan + provenance + emitted SQL.
The trace assembler consumes it, and production wiring **must** consume the same function rather than
re-sequencing the stages.

### The same risk on the parse side

The parse side already *has* an orchestrator with non-trivial semantics: `SearchOptionsBuilder.Build`
owns the per-parameter loop, the `_type` two-pass, category routing, and — critically — **the lenient
catch that produces `Ignored`**. An `Ignored` outcome is observable *only* inside that catch.

If the trace assembler re-implements the parameter loop to capture outcomes, it re-implements
`SearchOptionsBuilder` — a second path for the stage with the subtlest semantics in the pipeline.

**Requirement:** trace assembly runs **through** `SearchOptionsBuilder`, via an overload that accepts an
outcome collector the builder populates as it loops. The assembler never owns the parameter loop.

## 1. Trace shape — per-parameter scoping

A FHIR search is many parameters ANDed together: `SearchOptionsBuilder` calls `Parse` once per
parameter and combines the results into one tree. A flat trace therefore cannot answer "which
parameter did this span come from" — and FHIR permits **repeated keys** (`name=John&name=Jane`), so
matching by key/value text fails precisely where it matters most.

The trace is therefore **scoped per parameter**:

```csharp
public sealed record SearchTrace(
    string ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,          // null if compilation never ran
    EmittedSqlTrace? Sql);

// Plan side: the explained plan plus each CTE's link back to the parameter that produced it.
public sealed record QueryPlanTrace(
    string Explain,                                    // QueryPlan.Explain() output
    IReadOnlyList<CteProvenance> Ctes);                // ParameterOrdinal null where exempt (see §4)
public sealed record CteProvenance(int CteIndex, int? ParameterOrdinal, SourceSpan? Span);

// SQL side: the emitted text plus its section ranges (see §5).
public sealed record EmittedSqlTrace(
    string Sql,
    IReadOnlyList<SqlTextRange> Ranges);

public sealed record ParameterTrace(
    int Ordinal,                   // position in the original parameter list — the identity a span needs
    string Key,
    string Value,
    SyntaxNode? Syntax,            // projected; see §3
    Expression? Ir,
    ParameterOutcome Outcome);

public abstract record ParameterOutcome
{
    public sealed record Compiled : ParameterOutcome;
    public sealed record Ignored(string Reason, SourceSpan? Span) : ParameterOutcome;
    public sealed record Failed(TraceStage Stage, string Message, SourceSpan? Span) : ParameterOutcome;
}
```

`Ordinal` is what supplies parameter identity to every span beneath it — spans stay Key/Value-relative
and become unambiguous through their enclosing `ParameterTrace`.

**`Ignored` is not a nicety.** Production parsing is deliberately **fail-soft per parameter**:
`SearchExpressionBinder.BindAtomic` validates via `SearchValueExpressionBuilderHelper` so an
unsupported modifier/comparator throws `InvalidSearchOperationException` inside `SearchOptionsBuilder`'s
per-parameter catch, and the parameter is **silently dropped** (FHIR lenient handling). For a debugger,
*"your parameter was ignored, and here is why"* is among the most valuable things the trace can say — and
a single top-level failure field could not express one dropped parameter alongside three compiled ones.

## 2. The span model

### Shape and coordinate system

```csharp
public readonly record struct SourceSpan(SourceOrigin Origin, int Start, int Length);
public enum SourceOrigin { Key, Value }
```

Offsets are relative to the key or value string the scanner was handed; the enclosing
`ParameterTrace.Ordinal` disambiguates which parameter instance.

**Absolute query offsets are deliberately not modelled** — not a limitation we accepted, but the only
well-defined choice:

- The production API path calls `QueryParameterParser.Parse(IQueryCollection)` (see
  `CompartmentEndpoints.cs:245`). By then ASP.NET has already split the query, and
  `QueryParameterParser` percent-decodes via `Uri.UnescapeDataString`. There is no raw string to offset
  into.
- Decoding shifts offsets anyway: `name=John%20Doe` versus `John Doe`.

Modelling absolute offsets would yield an API that works in a playground and is
impossible-or-fabricated in production — the exact divergence this design exists to prevent. A UI that
wants whole-query highlighting has the raw string *it* sent plus each parameter's key/value text, and
can locate them client-side where the raw text actually exists.

### Where spans attach

- **Every `Syntax` node** (`Parsers/Syntax/*`) carries a span. The scanners already track offsets —
  `SearchKeySyntaxParser.Cursor` maintains `_offset`, and `SearchValueSyntaxParser` threads
  `(start, length)` through `ParseAtomic`/`Slice` — so no scanner re-architecture is required.
- **The typed IR carries spans on our two types only**: `SearchParameterPredicateExpression` and
  `CompositeComponentExpression`. Both are `sealed class`es with hand-written `ValueInsensitiveEquals`,
  `AddValueInsensitiveHashCode`, and `ToString`, so excluding provenance from identity and rendering is
  one explicit line per type.
- **Shared old-shape nodes are untouched**: `BinaryExpression`, `StringExpression`, `MultiaryExpression`,
  `ChainedExpression`, `IncludeExpression`, `NotExpression`, `MissingSearchParameterExpression`.

### Atomic spans include the comparator prefix

**Convention: an `AtomicValueSyntax` span covers the whole token, including any comparator prefix — not
just `RawText`.** `ParseAtomic` strips the prefix (`gt2000` → `RawText` `2000`), so this is a deliberate
choice rather than the incidental one.

It is what keeps §3's "spans are the join key" true. `SearchExpressionBinder.NormalizeCompositeComparator`
fabricates a new `AtomicValueSyntax` by re-concatenating prefix and `RawText`. Under a RawText-only
convention that fabricated node's text is not what its copied span extracts, so testing invariant 1 fails
and the span must be widened during binding — at which point the IR leaf's span no longer matches the
projected `SyntaxNode`'s span (projection happens at scan time, normalization during binding), and the
join key breaks for exactly this shape.

Including the prefix makes the span **invariant across normalization**: it can be copied verbatim,
invariant 1 holds for both the original and the fabricated node, and the correlation survives.

### Record equality — corrected

An earlier revision of this spec claimed a non-positional `init` property would avoid synthesized record
equality. **That is false.** A record's generated `Equals`/`GetHashCode` compares *all* instance fields,
including the backing fields of non-positional properties; positional-versus-not affects only the
constructor and deconstructor.

This matters concretely: `SearchValueSyntaxParserTests` compares syntax records **by value** —
`ShouldBe(new MissingValueSyntax(expected))`, `ShouldBe(new AtomicValueSyntax("alpha,beta", SearchComparator.Eq))`,
`ShouldBe(new OfTypeValueSyntax(...))`. A real span meeting a default-span expected value turns those red.

**Decision:** hand-write `Equals`/`GetHashCode` on the span-carrying `Syntax` records to exclude `Span`,
mirroring the approach already used for the two IR classes. Testing invariant 2 (§8) is extended to cover
syntax records so the exclusion cannot silently regress.

**Implementation caution:** `AlternativesValueSyntax` and `CompositeValueSyntax` hold `ImmutableArray<T>`,
whose synthesized record equality is the struct's own (not element-wise). Hand-written `Equals` must
reproduce the existing semantics exactly — exclude `Span` without inadvertently "improving" collection
comparison, or the existing parser tests change meaning silently.

## 3. Surfacing the syntax tree — projection, not exposure

Structural provenance (chains, includes, comma-alternatives, composites, `:missing`) is what lets us
leave shared nodes untouched. But today that information is unreachable:

- every `Syntax` type is `internal`;
- both syntax trees are **discarded inside `Parse`** — `ExpressionParser.Parse` returns only
  `Expression`, and the value syntax dies inside `SearchParameterExpressionParser.Parse`;
- `IExpressionParser.Parse(string[], string, string) → Expression` has no channel to return it.

Two options were considered. Making the `Syntax` types public was rejected: it converts an internal
scanner detail into a public API-stability commitment for an alpha package. Re-scanning inside the trace
assembler was also rejected — it produces a *second* tree requiring fragile correlation, which is the
two-path failure mode this design opens by forbidding.

**Decision: project.** The scanners keep their internal types; the parser exposes a projected, public,
purpose-built tree:

```csharp
public sealed record SyntaxNode(
    string Kind,                            // "ParameterKey", "ForwardChain", "Alternatives", "Composite", …
    SourceSpan Span,
    IReadOnlyList<SyntaxNode> Children);

public sealed record ParseResult(
    Expression Expression,
    SyntaxNode KeySyntax,
    SyntaxNode? ValueSyntax);               // null for shapes with no value tree — see below
```

`IExpressionParser` gains `ParseResult ParseWithSyntax(string[] resourceTypes, string key, string value)`.

**Projection is an opt-in tail, not always-on.** `Parse` is *not* implemented as
`ParseWithSyntax(...).Expression` — that would allocate a parallel `SyntaxNode` tree on every production
parse, which is precisely the per-call hot-path allocation used to justify making SQL text ranges opt-in
(§4). Instead both entry points share one scan→bind core, and projection runs as a pure additive **tail**
that only `ParseWithSyntax` executes. Projection is a pure function of the syntax tree, not a pipeline
fork, so "one parse implementation" is preserved without paying for it in production.

**`ValueSyntax` is nullable because the grammar demands it.** `_not-referenced` parses its structure from
the *value* string as a key-syntax (`ExpressionParser.Parse:62-67`) and has no value tree;
`_include`/`_revinclude` likewise produce an `IncludeKeySyntax` only. Separately, `SearchOptionsBuilder`
routes includes and sort outside `Parse` entirely — those `ParameterTrace.Syntax` entries are **null in
v1**, which is accepted and stated rather than discovered.

**Interface widening.** The value syntax currently dies inside
`SearchParameterExpressionParser.Parse(searchParameter, modifier, value) → Expression`, which sits behind
the **public** `ISearchParameterExpressionParser`. `ParseWithSyntax` cannot exist without widening that
contract; it is listed with the other signature changes in §6.

**Correlation rule: spans are the join key, resolved by containment.** A syntax node and the IR node built
from it carry the same `SourceSpan` (guaranteed by the prefix-inclusive convention in §2), so a UI
correlates structure to leaves by **span containment, with ties broken by depth**. The tie rule is
required, not decorative: `ParseCompositeItem` with no `$` wraps a single atomic in a
`CompositeValueSyntax` covering the identical range, so two syntax nodes legitimately share one span.
Containment-plus-depth resolves it, and a UI highlighting either node highlights the same text anyway.

This projection also fixes a claim the previous revision got wrong: `SearchTrace` is *not* trivially
JSON-serializable, because it carries `Expression` (a polymorphic class hierarchy), `ISearchValue`, and
`SymbolTable` (tuple-valued dictionaries). The projected DTOs are the serialization boundary; a future
API projects `Expression` into a similar public shape rather than serializing the IR directly.

## 4. IR → plan → SQL

### The missing middle: IR → plan

`Lower` already holds the exact predicate when it creates a `ParamSource`, so capturing provenance is
nearly free — and without it the chain breaks in the middle. Provenance rides **alongside** the plan,
never inside it: `QueryPlan`, `CteDefinition`, and `Predicate` are `record`s where an added field lands
in generated equality.

```csharp
public static LoweredPlan Run(...);   // LoweredPlan(QueryPlan Plan, PlanProvenance Provenance)

public sealed record PlanProvenance(IReadOnlyList<CteOrigin> Origins);
public sealed record CteOrigin(int CteIndex, Expression SourceNode);   // the IR node, NOT a bare span
```

A single always-on path is chosen over a `RunTraced` twin, per the guiding principle. This ripples
through the `Lower` tests and `EndToEndCompilationTests`; `Ignixa.Search.Sql` is alpha with **no
production call sites** for `Lower.Run`/`SqlBuilder.Run`, so this is the cheapest the change will ever be.

**Entries store the IR node reference, not a bare span** — and this is load-bearing. `Lower` runs on the
single merged AND tree that `SearchOptionsBuilder` produces, so the plan is per-*search*, one layer below
the per-parameter scoping of §1. A bare span at this layer is ambiguous by exactly the argument that
motivated per-parameter scoping: `name=John&name=Jane` yields two predicates whose value spans are both
`(Value, 0, 4)`, and the assembler could not tell which `ParameterTrace` a CTE belongs to.

**Resolution rule.** The assembler maps `CteOrigin.SourceNode` to its owning `ParameterTrace` by
**reference identity**, testing the node against each `ParameterTrace.Ir` subtree.

**Cloned nodes: record the original, do not match heuristically.** `Lower.cs:132` (`:not` handling)
rebuilds the predicate to strip the modifier, so the clone has reference identity in no parameter's
subtree. The fix is to remove the problem rather than paper over it: `LowerSearchParameter` holds the
**original** `predicate` at the moment it constructs the clone, so provenance is recorded against that
original node — via a `StructuralContext.Lower` overload taking an explicit provenance node. Reference
identity then holds for `:not` with no matching heuristics at all.

A heuristic fallback must *not* be specified as span + `ValueInsensitiveEquals`. That rule is provably
dead for the only case it was written for:

```csharp
// the clone strips the modifier …
new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value)

// … but ValueInsensitiveEquals compares it
p.Parameter.Equals(Parameter) && p.Comparator == Comparator && p.Modifier == Modifier
```

The clone's defining feature is `Modifier = null` while the original carries `:not`, so the comparison is
**always false** — every `:not` CTE would resolve to no parameter.

**Residual defensive fallback**, for hypothetical future rewrites only: match on span **plus
`Parameter.Equals`** (deliberately excluding comparator and modifier). Within one parameter's subtree,
span plus parameter identity is unambiguous; a residual tie means two literally identical parameters,
where attribution is arbitrary and harmless.

**Invariant that keeps this true:** `ParameterTrace.Ir` must be the *same tree instance* handed to
`SearchCompiler.CompileAsync`. Any rewriter introduced between them must be span-preserving and
re-evaluated against this resolution rule. `Lower.cs:132` is currently the only reference-identity
breaker in the compile path — `SearchOptionsBuilder` applies no rewriters, `DateTimeEqualityRewriter`
lives on the legacy DataLayer path, `SymbolCollectingVisitor`'s output is discarded, and
`LegacyExpressionLowerer` is not in the compile path.

This rule is specified here precisely so implementers do not improvise it — the obvious improvisation
(global span matching) is the ambiguity this section exists to eliminate.

### Provenance is partial by construction

Not every CTE originates from user-written text, and the spec must not claim otherwise:

| CTE origin | Provenance |
|---|---|
| leaf / composite `ParamSource` | span of the originating IR predicate |
| `:missing` `ParamSource` (`LowerParameterPresence`) | none — built from `MissingSearchParameterExpression`, a **shared** node carrying no span |
| compartment `ParamSource` | none — derived from URL path segments, not key/value text |
| `ChainJoin`, `Except`, `Union`, `Intersect`, `ResourceSource` | none — structural, synthesized by `Lower` |

`PlanProvenance` entries are therefore explicitly optional per CTE, and the completeness test (§7) is
scoped to leaf/composite-derived `ParamSource`s.

### Plan → SQL granularity

Section-level in v1: one text range per **CTE / outer `WHERE` / `ORDER BY` / seek predicate / include
stage**. That already answers *"which SQL did my `name` predicate become?"* Per-predicate ranges would
require predicate ids threaded through `Lower`; deferred, and the section map is a
superset-compatible foundation for them.

The emission mechanism is specified in §5.

### Options, not a boolean

```csharp
public static EmittedSql Run(QueryPlan plan, EmitOptions? options = null);
```

A `bool trace` parameter would violate the project's own "boolean parameters are a code smell" rule, and
`EmitOptions` gives per-predicate ranges a home later. `EmittedSql` gains
`IReadOnlyList<SqlTextRange>? TextRanges`, populated only when requested. Existing callers are
source-compatible; note this is a **binary-breaking** change to `EmittedSql`, which is acceptable for an
alpha package. No test compares whole `EmittedSql` values — `EmitTests` asserts on `.Sql` and on
individual `EmittedSqlParameter`s.

## 5. SQL text ranges — emission mechanism

**Requirement:** ranges must be produced *by* the same assembly that produces the SQL, so `trace`-on and
`trace`-off traverse identical code. The concern here is **drift, not runtime cost** — ranges are opt-in,
and even always-on the cost would be trivial. The failure mode is a second copy of the assembly
arithmetic (the `";WITH "` prefix, the `",\n"` and `"\nUNION ALL\n"` separators, which blocks are appended
in which terminal shape) living apart from the real concatenation: someone edits a separator in `Run`,
the mirror arithmetic doesn't move, and the trace confidently highlights the wrong SQL. A
wrong-but-confident debugger is strictly worse than none.

### Rejected alternatives

- **Post-hoc arithmetic** re-deriving the joins: the shadow copy described above.
- **Locating each block by `IndexOf` in the final SQL:** superficially attractive (no arithmetic) and
  **incorrect**. Duplicate blocks are real, not theoretical — `LowerNot` emits a `ResourceSource` CTE, and
  two structurally identical CTEs (same resource type, no predicate) render byte-identically, so
  `IndexOf` finds the first one twice.
- **Sentinel markers stripped afterwards:** rejected outright. Stripping shifts every downstream offset,
  requiring a fixup pass — shadow arithmetic relocated. A missed strip ships corrupt SQL, which is a
  production defect rather than merely a wrong trace. And either `trace`-off pays the sentinel/strip cost
  or it branches to sentinel-free assembly, reintroducing the two paths this design forbids. It would
  also poison the ScriptDom grammar tests' input.

### Decision: a section-scoped writer

The structural fact that makes this contained: every section needing a range — each CTE block, outer
`WHERE`, seek predicate, `ORDER BY`, include stages — **already exists as a complete string before final
concatenation**. `EmitCte`, `EmitParamSource`, `EmitIncludeStage`, `EmitChainJoin`, `EmitPredicate`, and
`EmitSeekPredicate` produce section *content* and never see the final buffer. **They do not change.** Only
the final assembly in `Run` — where content strings become the output string — is replaced.

```csharp
internal sealed class SqlTextWriter   // wraps a StringBuilder
{
    public void Append(string text);
    public void AppendJoin(string separator, IEnumerable<string> values);   // records each element's range
    public SectionScope Section(string label);                             // IDisposable; scopes nest
    public IReadOnlyList<SqlTextRange>? Ranges { get; }                    // null unless requested
}
```

Two points that matter:

- **Sections must be scope-based, not flat labels.** Sections *nest*: in the includes shape the seek
  predicate and outer `WHERE` live inside the `cteMatchPage` block. Flat labelling forces a single
  granularity; scopes yield both ranges for free and are the superset-compatible base for per-predicate
  ranges later.
- **No offset tracking is needed** — the current offset *is* `StringBuilder.Length`. The only conditional
  is whether `EndSection` records into the list; non-trace cost is a null check.

The genuinely fiddly work is not `string.Join` (a trivial `AppendJoin` that records element ranges) but
the three terminal shapes, which today embed sections inside single interpolated strings — e.g. the plain
path's `$"SELECT {top}m.T1, m.Sid1{sortColumns} FROM cte{n} m{sortJoins}{resourceJoin}\nWHERE {…}{orderBy}"`.
Recording `WHERE`/seek/`ORDER BY` ranges requires decomposing those interpolations into sequential
`Append` calls. Mechanical, but it touches essentially every assembly line of all three shapes.

### Parameter-order invariant

`@pN` ordinals are assigned as content strings are **built** (`EmitParam` appends to a shared list), and
today build order matches final-assembly order in all three shapes. The refactor must therefore **not
reorder any `Emit*` call** — only replace the concatenation of their results. The existing `EmitTests`
goldens guard this directly: they are exact-match on both SQL text and parameter ordinals, so any
accidental reordering fails loudly.

### Blast radius

- One new internal type (~60–90 lines) in `Ignixa.Search.Sql/Builders`.
- `SqlBuilder.Run`'s final assembly rewritten to sequential appends across all three terminal shapes
  (~130 lines, mechanical, no logic changes).
- All other `Emit*` helpers untouched.

## 6. Span propagation and copy sites

Spans must survive every site that reconstructs a span-carrying node. These are enumerated so the
implementation plan can cover them explicitly:

- **`Lower.cs:132`** — `:not` handling rebuilds `new SearchParameterPredicateExpression(...)` to strip the
  modifier. Dropping the span here loses provenance for every `:not` predicate.
- **`ExpressionRewriter.cs:111`** — rebuilds `CompositeComponentExpression`; any rewriter pass would
  otherwise drop spans.
- **`SearchExpressionBinder.NormalizeCompositeComparator`** — fabricates a new `AtomicValueSyntax` by
  re-concatenating comparator and text; the span must be recomputed rather than defaulted.
- **`SearchPredicateExpressionBuilder.Build`** — currently takes no syntax/span input; gains one
  (an internal signature change).

Signature changes required beyond the copy sites:

- **`ISearchParameterExpressionParser`** (public) — widened so the value syntax can survive
  `SearchParameterExpressionParser.Parse` rather than dying inside it (§3).
- **`IExpressionParser`** (public) — gains `ParseWithSyntax`.
- **`Resolve`** — return widened to carry unresolved symbols (§7).
- **`Lower.Run`** — returns `LoweredPlan` (§4).
- **`SqlBuilder.Run`** — gains `EmitOptions`; `EmittedSql` gains `TextRanges` (§4, binary-breaking, alpha).
- **`SearchOptionsBuilder.Build`** — overload accepting an outcome collector (guiding principle).

## 7. Error handling

Tracing must not change production semantics. `Lower.Run` and `SqlBuilder.Run` keep throwing exactly as
they do today; the trace assembler calls those same functions and records outcomes at **its own**
boundary. Enrichment never changes the exception type callers see — spans are attached via
`Exception.Data` or applied only inside the assembler.

**Leaf/composite failures** get spans at the dispatch choke points — `LeafLoweringDispatcher` and
`CompositeLoweringDispatcher` already hold the predicate when they invoke a rule. One catch-and-enrich
in each covers every leaf and composite gap.

**Failures outside those choke points** carry stage and message but no span: sort type/cap rejections,
the chain-depth guard, include cycles, `:missing` table gaps, and wildcard-compartment rejections. This
is accepted.

**Unresolved search parameters get special handling**, because it is the most likely playground error of
all. `Resolve` **silently omits** parameters the resolver cannot find (`Resolve.cs:76-81`), so `Lower`
later throws a bare `KeyNotFoundException` from `SymbolTable.SearchParamId` with no useful context.

This needs a mechanism, not just an intention: `Resolve` currently discards the misses and `SymbolTable`
exposes only throwing lookups, so **`Resolve`'s return is widened**:

```csharp
public sealed record ResolvedSymbols(SymbolTable Symbols, IReadOnlyList<SearchParameterInfo> Unresolved);
```

Same alpha-signature-change justification as `LoweredPlan`. The trace assembler reads `Unresolved`
directly and reports `Failed(TraceStage.Resolve, "search parameter is not registered: …", span)` against
the owning parameter, rather than waiting for the downstream throw.

## 8. Testing

1. **Spans are truthful.** Round-trippable and objective: `source.Substring(span.Start, span.Length)`
   equals the expected text. A span that does not extract what it claims is a failing test, not a
   judgment call.
2. **Provenance is invisible to identity and rendering.** `ToString()` output unchanged;
   `ValueInsensitiveEquals`/`AddValueInsensitiveHashCode` ignore spans; **and syntax-record `Equals`
   ignores `Span`**. This protects `SearchParserOldVsNewParityTests` (which compares `ToString()`) and
   `SearchValueSyntaxParserTests` (which compares syntax records by value).
3. **The chain is unbroken, where it is claimed to exist.** For a corpus covering leaf, composite,
   chain, include, sort, **`:not`**, **`:missing`**, and a rewriter round-trip: every
   `SearchParameterPredicateExpression` and `CompositeComponentExpression` has a span, every
   *leaf/composite-derived* `ParamSource` has provenance to an IR node, and every CTE has a SQL text
   range. `:not` and the rewriter case exist specifically to catch the copy sites in §6.

   The assertion is scoped to those two types deliberately. **`:text` and `:of-type` produce no
   span-carrying IR node at all** — `BindValue`'s text path builds
   `Expression.StartsWith(FieldName.TokenText, …)` and `BindOfType` routes through
   `SearchValueExpressionBuilderHelper`, so both yield *shared old-shape leaves from birth*, bypassing
   `SearchPredicateExpressionBuilder`. Syntax-side spans still cover them (the projection carries the
   span) and neither lowers in the SQL compiler today. They are IR-side exemptions, alongside the CTE
   exemptions tabled in §4.
4. **Outcomes land as data.** An unsupported construct (a system-qualified token) yields `Failed` with
   the correct stage, real message, and the offending predicate's span. A parameter dropped by lenient
   handling yields `Ignored` with its reason — not silence.
5. **SQL ranges are truthful.** The same objective style as invariant 1, one layer down:
   `sql.Substring(range.Start, range.Length)` parses as — and equals — the section it claims to be.
6. **Tracing does not alter output.** Emitting with and without `EmitOptions` produces **byte-identical
   SQL and identical parameter order**, and the untraced call yields `TextRanges == null`. Under the
   single-path design this is close to tautological, which is exactly why it is worth asserting: it
   converts an invariant-by-habit into an invariant-by-test.

Existing `EmitTests` goldens are retained unchanged and serve as the guard for the parameter-order
invariant (§5) — they match exactly on both SQL text and `@pN` ordinals. Existing ScriptDom SQL-grammar
tests are unaffected.

## 9. Risks and accepted consequences

- **Provenance drift.** A new lowering rule could forget provenance. Mitigated by test 3 asserting
  completeness rather than spot-checking.
- **Span leakage into equality.** Would silently break parser parity. Mitigated by test 2 and by
  hand-written equality on every span-carrying type.
- **`Lower.Run` signature churn.** Contained: alpha, unwired, covered by tests that fail loudly.
- **Old-shape backends get no leaf provenance.** `LegacyExpressionLowerer` drops spans by construction,
  so the planned CosmosDB backend cannot receive leaf provenance without further work. Accepted — the
  playground traces the compiler path.
- **`SearchTrace` is not directly serializable.** Its projected DTOs are the serialization boundary;
  `Expression`/`ISearchValue`/`SymbolTable` require projection. Stated explicitly so no API is designed
  against the earlier, incorrect claim.
- **Plan caching would pin the parsed IR.** `CteOrigin` holds `Expression` references, so if plan caching
  is ever introduced, a cached plan retains the whole parsed IR — including user values on
  `ISearchValue`. This is a change of magnitude, not of kind: `QueryPlan` already pins user values via
  `SqlParameterRef.Value` in every `Predicate`, so any plan cache must already strip or normalize
  parameter values. Noted so that requirement is not rediscovered later. Nothing here affects
  per-request use: `LoweredPlan` is per-request, the trace already roots every `ParameterTrace.Ir`
  subtree, and `Expression` never references the plan (no cycles).
