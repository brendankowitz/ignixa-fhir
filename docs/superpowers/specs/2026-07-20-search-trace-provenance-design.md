# Search trace provenance — design

**Date:** 2026-07-20
**Status:** Proposed
**Scope:** `Ignixa.Search` (Expressions/Parsers) and `Ignixa.Search.Sql`

## Motivation

We want a developer-facing search playground: paste a FHIR search, see how it parses, how it
compiles, and what SQL it becomes — with the ability to trace any one piece back and forth across
those stages. **This spec covers only the library infrastructure that makes such a UI possible.** No
UI is built here.

Today each stage is individually inspectable — the parser produces a typed IR, `QueryPlan.Explain()`
renders a plan, `SqlBuilder.Run` returns SQL plus bound parameters — but nothing connects them. There
is no way to say *"this text produced this predicate, which produced this CTE, which produced this
SQL."* Closing that gap is the whole point of this work.

## Scope

**In scope**

- Source spans on the parse-side syntax and IR nodes.
- A link from IR predicates to the plan nodes they produce.
- A link from plan sections to ranges of emitted SQL text.
- A serializable `SearchTrace` document tying it together, including failures.

**Explicitly out of scope (deferred)**

- A declarative **capability matrix** (which comparators/modifiers/features each backend supports).
  Deferred deliberately: the existing `NotSupportedException` messages are specific and actionable,
  and surfacing them in the trace is sufficient for now. Parameter discovery already works via
  `ISearchParameterDefinitionManager` + `SearchParameterInfo`.
- Any UI.
- Per-*predicate* SQL text ranges (section-level only in v1 — see "Plan → SQL").

## Guiding principle: one path, not two

The traced path and the production path must be **the same path**. A parallel "tracing mode" pipeline
is exactly how `DateTimeEqualityRewriter` silently rotted — it ran against a tree shape that the
parser had stopped producing, and no test noticed because the optimization was invisible when absent.

Consequences, applied throughout:

- Spans are **always populated**, not gated behind a flag.
- `Lower.Run` gains provenance in its normal return value rather than a `RunTraced` twin.
- The trace assembler is a thin **wrapper over the real functions**, never a reimplementation.

## 1. Entry point and trace shape

**Spans are always on.** The scanner already knows its offsets; a `SourceSpan` (two ints) per node is
negligible, and always-on removes the divergence risk above.

**The SQL text-range map is opt-in.** Unlike spans it is a per-call allocation on what will become the
hot path once the compiler is wired in.

```csharp
public static EmittedSql Run(QueryPlan plan, bool trace = false);

public sealed record EmittedSql(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    IReadOnlyList<SqlTextRange>? TextRanges = null);   // populated only when trace: true
```

Existing callers are source-compatible: the parameter defaults to `false` and `TextRanges` stays null.
`EmittedSql` is a record, but the added member is safe here because no test compares whole `EmittedSql`
values — `EmitTests` asserts on `.Sql` and on individual `EmittedSqlParameter`s.

**Two trace levels, matching the assembly boundary** (`Ignixa.Search.Sql` references `Ignixa.Search`,
never the reverse):

- **`Ignixa.Search`** owns the *parse trace*: source text plus a typed IR carrying spans. Usable by
  anything that only parses.
- **`Ignixa.Search.Sql`** owns the *full pipeline trace*: a `SearchTrace` record carrying the source
  query, parsed IR, `SymbolTable`, `QueryPlan`, `EmittedSql` with its text-range map, and a nullable
  `Failure`.

`SearchTrace` is plain records — no cycles, no behavior — so a future API can serialize it to JSON
without a mapping layer.

## 2. The span model (parse side)

### Coordinate system

`IExpressionParser.Parse(string[] resourceTypes, string key, string value)` receives the key and value
as **separate strings**. It never sees the raw `Patient?name=Smith&birthdate=gt2000`, so a span cannot
be an absolute offset into the query.

```csharp
public readonly record struct SourceSpan(SourceOrigin Origin, int Start, int Length);
public enum SourceOrigin { Key, Value }
```

Offsets are relative to the string the scanner was handed.

**Absolute query offsets are deliberately not modelled.** This is not a limitation we accepted; it is
the only well-defined choice available:

- The production API path calls `QueryParameterParser.Parse(IQueryCollection)` (see
  `CompartmentEndpoints.cs:245`). By then ASP.NET has **already split and percent-decoded** the query.
  There is no raw string to offset into.
- Percent-decoding means offsets differ between what the developer typed (`name=John%20Doe`) and the
  decoded value (`John Doe`).

Modelling absolute offsets would therefore produce an API that works in a playground and is
impossible-or-fabricated in production — the exact divergence this design exists to avoid. A UI that
wants whole-query highlighting has the raw string *it* sent plus each parameter's key/value text from
the trace, and can locate them client-side where the raw text actually exists.

### Where spans attach

- **Every `Syntax` node** (`Parsers/Syntax/*`) carries a span. These are ours, and the scanner already
  knows offsets. This yields **full structural provenance** — chains, includes, alternatives,
  composites, `:missing`.
- **The typed IR carries spans on our two types only**: `SearchParameterPredicateExpression` and
  `CompositeComponentExpression`. Both are `sealed class`es with hand-written `ValueInsensitiveEquals`,
  `AddValueInsensitiveHashCode`, and `ToString` — so "provenance never participates in identity or
  rendering" is one explicit line per type, with no fight against compiler-generated record equality.
- **Shared old-shape nodes are untouched**: `BinaryExpression`, `StringExpression`, `MultiaryExpression`,
  `ChainedExpression`, `IncludeExpression`, etc.

This split is what keeps the change free of MS FHIR Server divergence. Leaf-only IR spans would seem to
cost chain/include highlighting — it does not, because **structural provenance comes from the `Syntax`
tree** while leaf provenance comes from the IR. The trace exposes both, so a UI can highlight an entire
`_has:Observation:patient:code` chain *and* a single predicate without modifying one shared node.

The `Syntax` types are positional `record`s, so a positionally-added span would land in generated
equality; it is added as a non-positional `init` property instead.

Syntax errors already carry positions via `SearchSyntaxExceptionFactory`, so they become spans in the
same coordinate system rather than a separate concept.

## 3. Linking IR → plan → SQL

### The missing middle: IR → plan

Spans give query↔IR and the emit map gives plan↔SQL, but nothing connects a predicate to the CTE it
produced. `Lower` already holds the exact predicate when it creates a `ParamSource`, so capturing it is
nearly free — and without it the chain breaks in the middle.

Provenance rides **alongside** the plan, never inside it: `QueryPlan`, `CteDefinition`, and `Predicate`
are `record`s, where an added field lands in compiler-generated equality.

### Signature change

```csharp
// before
public static QueryPlan Run(...)
// after
public static LoweredPlan Run(...)          // LoweredPlan(QueryPlan Plan, PlanProvenance Provenance)
```

`PlanProvenance` is a small list of `(CteIndex, SourceSpan)` pairs. A single always-on path is chosen
over a `RunTraced` twin for the reason stated above.

This ripples through the `Lower` tests and `EndToEndCompilationTests`. `Ignixa.Search.Sql` is **alpha
and has no production call sites** for `Lower.Run`/`SqlBuilder.Run`, so this is the cheapest this change
will ever be.

### Plan → SQL granularity (v1: section-level)

`SqlBuilder` emits section by section, so recording one text range per **CTE / outer `WHERE` /
`ORDER BY` / seek predicate / include stage** is natural and needs no new node ids. That already answers
*"which SQL did my `name` predicate become?"*

Per-predicate ranges would require predicate ids threaded through `Lower`; deferred. The section map is
a superset-compatible foundation for adding them later.

### Resulting chain

```
SourceSpan → IR node → CteIndex → SQL text range
```

Every hop is derived from data the pipeline already has.

## 4. Error handling

Tracing **must not change production semantics.** `Lower.Run` and `SqlBuilder.Run` keep throwing exactly
as they do today. The `SearchTrace` assembler calls those same functions and catches at its own boundary
to record:

```csharp
public sealed record Failure(TraceStage Stage, string Message, SourceSpan? Span);
```

This is a wrapper over one path, not a second path.

**Attaching a span to a failure.** Today's `NotSupportedException`s carry good messages but no node
reference. Rather than a new exception hierarchy or enriching a dozen throw sites, the span is attached
at the **dispatch choke points** — `LeafLoweringDispatcher` and `CompositeLoweringDispatcher` already
hold the predicate when they invoke a rule. One catch-and-enrich in each (a boundary, consistent with
the project's "handle errors at boundaries" rule) gives every leaf and composite gap a span.

`Failure.Span` is nullable and best-effort; structural failures report stage and message only.

## 5. Testing

1. **Spans are truthful.** Round-trippable and objective:
   `source.Substring(span.Start, span.Length)` equals the expected text. A span that does not extract
   what it claims is a failing test, not a judgment call.
2. **Provenance is invisible to identity and rendering.** Assert `ToString()` output is unchanged and
   that `ValueInsensitiveEquals`/`AddValueInsensitiveHashCode` ignore spans. This protects
   `SearchParserOldVsNewParityTests`, which compares `ToString()` — any leak turns that suite red loudly.
3. **The chain is unbroken.** For representative queries (leaf, composite, chain, include, sort): every
   predicate has a span, every `ParamSource` CTE has provenance back to an IR node, and every CTE has a
   SQL text range. This is the test that catches "a new lowering path forgot provenance."
4. **Failures land as data.** An unsupported construct (a system-qualified token) yields a `Failure`
   with the correct stage, the real message, and the offending predicate's span — not an escaped
   exception.

Existing ScriptDom SQL-grammar tests are unaffected.

## Risks

- **Provenance drift.** A new lowering rule could forget to record provenance. Mitigated by test 3,
  which asserts completeness rather than spot-checking.
- **Span leakage into equality.** Would silently break parser parity. Mitigated by test 2, and by
  choosing classes (hand-written equality) over records for the IR types that carry spans.
- **`Lower.Run` signature churn.** Contained: alpha, unwired, and covered by existing tests that will
  fail loudly rather than silently.
