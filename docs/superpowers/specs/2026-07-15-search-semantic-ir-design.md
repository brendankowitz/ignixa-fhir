# Ignixa.Search Semantic IR — Design

**Date:** 2026-07-15
**Status:** Proposed
**Area:** `src/Core/Ignixa.Search/Expressions/` (new predicate node, new visitor methods), `src/Core/Ignixa.Search/Expressions/Parsers/` (binder wiring), a new `LegacyExpressionLowerer`
**Precedes:** Phase 2 of `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`. Feeds `Ignixa.Search.Sql`'s `Resolve`/`Lower` stages (Phase 3+), which consume this IR directly rather than the old field-level `Expression` tree.

## Executive recommendation

Add one new leaf expression node, `SearchParameterPredicateExpression`, that carries a **typed search value** and a **typed comparator** instead of today's `object Value`. Do not invent a new value-type hierarchy for it — `ISearchValue` (`src/Core/Ignixa.Search/Indexing/SearchValues/`) already **is** that typed value; the parser already builds one on every request and discards it one step later. This design's entire job is to stop discarding it.

Composites get the same treatment for free: `worktree-sql-datalayer-architecture`'s `CompositeComponentExpression` (unmerged, `origin/worktree-sql-datalayer-architecture`, commits `289cd2a9`/`a438048e`) already solved the hard part — per-component identity, including the DocumentReference ordinal-inference problem — by wrapping at the expression-tree level rather than the value level. Adopt it unchanged; only retarget what it wraps, from the old field-level tree to the new predicate node.

The result is a data flow with exactly one new type doing exactly one new job, sitting at a seam (`BoundParameterKey` → value construction) that already exists and is already typed — not a parallel pipeline, not a port of anything from `fhir-server` (which, verified this session, has no richer IR to port — see *Why not model this on fhir-server*), and not a rewrite of the structural nodes (`And`/`Or`/`Chained`/`Include`/`Compartment`/`Sort`) that don't need to change.

```
SearchExpressionBinder (PR #332, already merged)
  BoundParameterKey(SearchParameterInfo, SearchModifier?)
     │
     ├─ TODAY: SearchValueExpressionBuilderHelper (ISearchValueVisitor)
     │           → BinaryExpression(BinaryOperator, FieldName, int? ComponentIndex, object Value)
     │           → StringExpression(StringOperator, FieldName, int? ComponentIndex, string Value, bool IgnoreCase)
     │
     └─ THIS DESIGN: new sibling builder (same ISearchValueVisitor dispatch, same typed inputs)
                 → SearchParameterPredicateExpression(SearchParameterInfo, SearchComparator, SearchModifier?, ISearchValue)
                     │
                     ├─► LegacyExpressionLowerer   (NEW — thin adapter, reuses SearchValueExpressionBuilderHelper)
                     │      → old field-level Expression
                     │          ├─► InMemoryIndex SearchQueryInterpreter   unchanged
                     │          └─► Cosmos (fhir-server)                  unchanged
                     │
                     └─► Ignixa.Search.Sql  (Phase 3+, consumes this IR directly)
```

## Context

### What exists today, verified this session

- **The old field-level AST is exactly what the fhir-to-sql-compiler design doc criticizes.** `BinaryExpression(BinaryOperator, FieldName, int? ComponentIndex, object value)` (`src/Core/Ignixa.Search/Expressions/BinaryExpression.cs:23`) and `StringExpression` carry an untyped `object`/raw `string`. Every consumer downstream re-derives the real type from `FieldName` + `SearchParamType` by convention, not by the type system.
- **A typed value already exists, transiently, on every request — and is thrown away one visit later.** `ISearchValue` (`src/Core/Ignixa.Search/Indexing/SearchValues/`) is a closed hierarchy: `StringSearchValue`, `TokenSearchValue`, `OfTypeTokenSearchValue`, `NumberSearchValue`, `QuantitySearchValue`, `DateTimeSearchValue`, `ReferenceSearchValue`, `UriSearchValue`, `CompositeSearchValue`. `SearchAtomicValueParser` (part of PR #332's new front-end, already merged onto `feature/fhir-to-sql-compiler`) constructs these directly from parsed search-value syntax. `SearchExpressionBinder` then calls `SearchValueExpressionBuilderHelper` — an `ISearchValueVisitor` — which pattern-matches over those same nine types **specifically to discard the typing** and emit `object Value` on a `BinaryExpression`/`StringExpression`.
- **The comparator is already a clean, separate concept, not smashed into the value.** `SearchComparator` lives on PR #332's `AtomicValueSyntax.Comparator` (`SearchExpressionBinder.cs:121,239`). Checked `NumberSearchValue`/`QuantitySearchValue` directly — neither carries a comparator or prefix field. This is the un-braiding the design doc's own principle asks for, and it already exists; nothing needs inventing here.
- **`SearchModifier` is already carried through binding.** `BoundParameterKey(SearchParameterInfo SearchParameter, SearchModifier? Modifier)` (`src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundParameterKey.cs:13`) is real, on this branch, today.

### Why not model this on fhir-server

The roadmap originally said to model this IR "directly against fhir-server's live query-generation code." Verified false this session, in enough detail to be worth recording so it isn't re-investigated: `Microsoft.Health.Fhir.Core`'s `SearchParameterExpression` wraps `SearchParameterInfo` around an inner `Expression` that bottoms out in the **same shape** Ignixa already has — `BinaryExpression(BinaryOperator, FieldName, int? ComponentIndex, object Value)`. A repo-wide grep for `ISearchValue`/`SearchComparator` under fhir-server's `Features/Search/Expressions/` returns zero hits. Traced the `:exact` string case end to end (`StringOverflowRewriter.VisitString` → `StringQueryGenerator.VisitString`) — same rewrite-then-emit-over-one-untyped-type pipeline the design doc criticizes, just 22 passes deep instead of Ignixa's fewer. fhir-server's composite handling is the same `ComponentIndex` int on the same field-level nodes; it has no dedicated composite node at all.

**Consequence:** this IR is original design work for both repos' eventual benefit, not a port. What *is* worth reusing from fhir-server, and is reused below: its proven 15-method `IExpressionVisitor` surface as a sanity check on node-kind count, and its own precedent (`StringExpression`/`VisitString` distinct from generic `VisitBinary`) that giving one FHIR type special-case treatment when it needs it is normal, not a sign the design is wrong.

## The design

### `SearchParameterPredicateExpression` — one new leaf node, not a family of them

```csharp
public sealed class SearchParameterPredicateExpression : Expression
{
    public SearchParameterInfo Parameter { get; }
    public SearchComparator Comparator { get; }
    public SearchModifier? Modifier { get; }
    public ISearchValue Value { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        => visitor.VisitSearchParameterPredicate(this, context);
}
```

**Decision: one generic node with a closed polymorphic `Value`, not one node type per FHIR search-value kind.** Considered and rejected the alternative (`StringPredicateExpression`, `TokenPredicateExpression`, ... — one type per kind, mirroring fhir-server's `StringExpression` treatment for every type, not just string). Rejected because:

- The pair this node un-braids is **predicate identity (parameter, comparator, modifier — common to every kind) ←→ value representation (which FHIR type)**. A dedicated node per kind re-braids that pair back together — every node type would duplicate the identity fields, and the visitor surface would grow by one method per kind (9+) for information the type system already carries via `Value`'s concrete type.
- `ISearchValue` is a **closed** set (9 known implementers, sealed/internal construction). Pattern-matching on it (`switch (predicate.Value) { case StringSearchValue s => ..., ... }`) gets the same build-time exhaustiveness guarantee a dedicated-node visitor method would give, without the extra visitor methods. A missing `case` is a build warning (nullable/exhaustiveness analysis) or a test failure (an explicit exhaustiveness test — see *Testing*), not a smaller win than a missing `VisitTokenPredicate` override.
- fhir-server's own precedent for giving a type special-case treatment (`StringExpression`) is precisely because string carries *behavior* other types don't (`:exact`/`:contains` collation and overflow-column selection) — not because every type needs its own node. That precedent argues for special-casing where the behavior actually diverges (inside `Lower`'s leaf rules, Phase 3+), not for a wider visitor surface at the IR layer where the fields are otherwise identical.

**`CompositeSearchValue` does not participate in this node's `Value`, ever — confirmed, not assumed.** `SearchExpressionBinder.BindComposite` (`:186-233`) decomposes a composite search parameter into N per-component calls to `BindAtomic`, each producing its own atomic `ISearchValue` and its own predicate, before any aggregate value is built — the `index` passed at `:219-221` is exactly the position a `CompositeComponentExpression` wrapper carries. `CompositeSearchValue`'s only construction site in the entire repo is `ElementSearchIndexer.cs:151` — the **write/indexing path**, untouched by either the old or new query parser. It is not legacy (nothing frozen it as a rollback lever), it is simply a different concern that happens to share a value hierarchy. **Rename it `CompositeIndexSearchValue`** and update its doc comment to state plainly that it's constructed only by indexing and consumed by neither search parser — so a future reader doesn't waste time wondering whether the query side is supposed to be building one.

### Composites: adopt `CompositeComponentExpression`, retarget what it wraps

```csharp
// Unchanged from worktree-sql-datalayer-architecture — adopt as-is:
public sealed class CompositeComponentExpression : Expression
{
    public SearchParameterInfo ComponentSearchParameter { get; }  // effective/inferred type (handles DocumentReference)
    public int Position { get; }
    public Expression WrappedExpression { get; }  // THIS DESIGN: now a SearchParameterPredicateExpression, not field-level

    // dispatches via visitor.VisitCompositeComponent(this, context) — already a real IExpressionVisitor method
    // on that branch; adopted here unchanged.
}
```

Tree shape is unchanged from what that branch already builds: `SearchParameterExpression(compositeParam, Or(And(CompositeComponentExpression(...), CompositeComponentExpression(...)), ...))`. The only change this design makes is what `WrappedExpression` holds — a typed predicate instead of an untyped `BinaryExpression`. No new grammar, no new grouping concept; the branch that owns composite-identity correctness keeps owning it.

### Construction: a sibling builder, not a new parsing pipeline

`SearchAtomicValueParser` already builds `ISearchValue`. Today, exactly one thing consumes it: `SearchValueExpressionBuilderHelper` (an `ISearchValueVisitor`), which flattens it. This design adds a second `ISearchValueVisitor` implementation — call it `SearchPredicateExpressionBuilder` — that performs the identical dispatch but constructs `SearchParameterPredicateExpression(parameter, comparator, modifier, value)` instead of flattening. `SearchExpressionBinder`'s `BindAtomic`/`BindComposite` call this new builder instead of the old one as their canonical output.

**This makes `SearchParameterPredicateExpression` the primary parse result going forward**, not an alternate representation built alongside the old tree. One parse, one canonical semantic tree.

### Legacy lowering: reuses existing logic, does not reimplement it

`LegacyExpressionLowerer` converts the new tree back to the old field-level shape, for the two consumers that still need it (`InMemoryIndex.SearchQueryInterpreter`, Cosmos):

```csharp
public sealed class LegacyExpressionLowerer : IExpressionVisitor<object?, Expression>
{
    public Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression node, object? context)
        => new SearchValueExpressionBuilderHelper().Build(node.Value, node.Parameter, node.Modifier);
        // ^ the EXISTING helper, unmodified — it already knows how to turn this exact ISearchValue
        //   into the old field-level tree, because that's the only thing it has ever done.
        //   No ComponentIndex here: a bare predicate is never itself a composite component — see below.

    public Expression VisitCompositeComponent(CompositeComponentExpression node, object? context)
    {
        // node.WrappedExpression is a SearchParameterPredicateExpression; lower it the same way as any
        // other predicate, then apply node.Position the same way the OLD path attached ComponentIndex —
        // i.e. this method is where positional context re-enters the old field-level shape, not the
        // predicate node itself. Exact mechanics (re-stamping ComponentIndex onto the lowered
        // BinaryExpression/StringExpression) are implementation-plan detail, not an architecture decision.
        Expression lowered = node.WrappedExpression.AcceptVisitor(this, context);
        return lowered.WithComponentIndex(node.Position); // illustrative — exact API decided at implementation time
    }

    // every structural node (And/Or/Chained/Include/Compartment/Sort/Not/...) passes through unchanged —
    // this lowerer only has real work to do at the two new leaf kinds.
}
```

No new flattening logic exists anywhere in this design. The only thing that changed is *when* `SearchValueExpressionBuilderHelper` runs — after a round-trip through the typed tree instead of inline during binding — which is exactly the point: the typing survives long enough to be useful (to the new SQL compiler) before it's optionally discarded for consumers that don't want it.

### `Lower`'s eventual leaf-rule dispatch (Phase 3+, shown here to confirm the IR supports it)

```csharp
CteDefinition Lower(SearchParameterPredicateExpression predicate, LeafContext ctx) =>
    predicate.Value switch
    {
        StringSearchValue s => LowerString(predicate.Parameter, predicate.Modifier, s, ctx),
        TokenSearchValue t => LowerToken(predicate.Parameter, predicate.Modifier, t, ctx),
        DateTimeSearchValue d => LowerDate(predicate.Parameter, predicate.Comparator, d, ctx),
        QuantitySearchValue q => LowerQuantity(predicate.Parameter, predicate.Comparator, q, ctx),
        NumberSearchValue n => LowerNumber(predicate.Parameter, predicate.Comparator, n, ctx),
        ReferenceSearchValue r => LowerReference(predicate.Parameter, predicate.Modifier, r, ctx),
        UriSearchValue u => LowerUri(predicate.Parameter, u, ctx),
        OfTypeTokenSearchValue ot => LowerOfTypeToken(predicate.Parameter, ot, ctx),
        _ => throw new NotSupportedException($"No lowering rule for {predicate.Value.GetType()}")
    };
```

This is Phase 3+ scope, not built here — shown only to confirm the IR this design produces is directly consumable by the compiler's leaf rules without another translation step.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Value typing | Reuse existing `ISearchValue`, don't invent a parallel hierarchy | It's already exactly this shape and already built on every request; inventing a second one would be two names for the same fact |
| Node granularity | One generic `SearchParameterPredicateExpression`, closed polymorphic `Value` | Un-braids "predicate identity" from "value kind" cleanly; avoids growing the visitor surface by 9 methods for information the type system already carries |
| Composites | Adopt `CompositeComponentExpression` from `worktree-sql-datalayer-architecture` unchanged; retarget `WrappedExpression` | That branch already solved component-identity correctness (DocumentReference ordinal inference); reinventing it would duplicate solved work and risk a regression |
| `CompositeSearchValue` | Rename to `CompositeIndexSearchValue`, document as indexing-only | It's not legacy (nothing froze it as a rollback lever) — it's a different, still-active concern (write path) that happens to share a hierarchy. "Legacy" would misrepresent it |
| Validation placement | None at the node itself; rely on PR #332's binder-time positioned validation | The binder already rejects invalid comparator/modifier/value combinations before any expression is built; re-validating downstream would duplicate a boundary check |
| Legacy consumers (InMemory, Cosmos) | `LegacyExpressionLowerer`, reusing the existing `SearchValueExpressionBuilderHelper` unmodified | Zero new flattening logic; the exact same code that already builds the old tree from an `ISearchValue` keeps doing that job, just invoked one hop later |
| Primary parse output | `SearchExpressionBinder` builds `SearchParameterPredicateExpression` as its canonical result, not an alternate tree built alongside the old one | One parse, one semantic tree — avoids a permanent double-build cost on every request and matches "one canonical representation" over "two trees kept in sync" |

## Non-goals

- Changing any structural node (`MultiaryExpression`/And-Or, `ChainedExpression`, `IncludeExpression`, `NotExpression`, `CompartmentSearchExpression`, `SortExpression`) — none of them carry untyped values today; none of them need to change for this design.
- Building `Ignixa.Search.Sql`'s `Resolve`/`Lower`/`Emit` stages — this design only produces the IR those stages will consume (Phase 3+).
- Touching the indexing/write path (`ElementSearchIndexer`, row generators) beyond the `CompositeIndexSearchValue` rename — this is a read-path design.
- Porting anything from `fhir-server` — confirmed above there's nothing richer there to port.

## Risks

- **Binary-breaking change to `IExpressionVisitor<TContext,TOutput>`.** Adding `VisitSearchParameterPredicate` and `VisitCompositeComponent` to a `public`, `IsPackable=true` interface breaks any external implementor. Mitigation, per the original fhir-to-sql-compiler design doc: ship both as default interface methods throwing `NotSupportedException` for anyone who hasn't overridden them, plus a major version bump.
- **Two binary-breaking additions to the same interface are in flight from two branches** — this design's `VisitSearchParameterPredicate`/`VisitCompositeComponent`, and `worktree-sql-datalayer-architecture`'s own `VisitCompositeComponent` (already real on that branch). These must land as one coordinated major-version bump, not two — already flagged in the roadmap's coordination checklist; repeating here because it's this design's own risk, not just an FYI.
- **`SearchExpressionBinder` becoming the sole producer of the canonical tree is a behavior change for every existing caller**, even though `LegacyExpressionLowerer` is designed to make the output *equivalent* for InMemory/Cosmos. The differential test in *Testing* below exists specifically to catch a divergence here before it ships, not after.
- **`SearchParameterPredicateExpression`'s `Comparator`/`Modifier` fields can still express combinations `ISearchValue`'s type doesn't logically support** (e.g. `Comparator.GreaterThan` paired with a `StringSearchValue`) — the design relies on binder-time validation preventing this from ever being constructed, not on the type system making it unrepresentable. This is a deliberate, named trade-off (see *Validation placement* above), not an oversight — flagged so it isn't rediscovered as a surprise later.

## Testing

1. **Golden shape tests** (new): for a representative search query string, assert the exact `SearchParameterPredicateExpression`/`CompositeComponentExpression` tree shape produced — following the pattern PR #332 already established (`ExpressionParserCharacterizationTests.cs`), not a new testing convention.
2. **Differential test**: for the same input, assert `LegacyExpressionLowerer`'s output is structurally identical (`ValueInsensitiveEquals`, already implemented on every `Expression` node) to what the *old* direct-construction path (`SearchValueExpressionBuilderHelper` invoked straight from the binder, pre-this-design) produces. This is the test that makes "InMemory/Cosmos never notice anything changed" a verified claim rather than an assertion.
3. **`ISearchValue` exhaustiveness test**: one test that enumerates every concrete `ISearchValue` implementer via reflection and asserts a lowering rule exists for each (Phase 3+, but the test's *shape* — reflect over the closed set, assert coverage — belongs to this design since it's what makes the closed-polymorphic-`Value` choice actually pay off).
4. **Composite integration test**: at least one composite search parameter (recommend `Observation?component-code-value-quantity=...`, already exercised by existing composite tests) asserting the full `SearchParameterExpression(Or(And(CompositeComponentExpression(...))))` shape with typed `Value` on each wrapped predicate.
