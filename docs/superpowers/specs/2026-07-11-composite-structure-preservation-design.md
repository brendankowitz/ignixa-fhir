# Design: Composite Search Parameter Structure Preservation (Phase 2)

Date: 2026-07-11
Status: Approved (Fable-reviewed, conditional amendments incorporated)
Predecessor: `docs/superpowers/specs/2026-07-11-comparator-semantics-design.md` (Phase 0/1 prerequisite, merged)
Reference: `docs/features/sql-datalayer-architecture/investigations/staged-query-compiler.md` ("Phase 2" scope, audit finding 3)

## Problem

FHIR composite search parameters (e.g. `code-value-quantity`, Token+Quantity; `relationship`, Reference+Token) have
each component's `SearchParameterInfo` fully resolved at parse time
(`SearchParameterExpressionParser.cs`, composite branch, ~lines 100-169), but that structure is discarded — the
parser only tags the resulting field-level leaf expressions with a bare `int? ComponentIndex`
(`IFieldExpression.ComponentIndex`). Downstream, the SQL data layer has to reconstruct what it just threw away:

- `SearchParameterQueryGenerator.ExtractComponentExpressions` (~205-295) heuristically regroups leaf expressions by
  walking `ComponentIndex` and building `HashSet<int?>`s over the expression tree.
- `CompositeSearchParameterQueryGenerator.DetermineCompositeType` (~46-114) infers the composite's physical type by
  reading `Component[i].ResolvedSearchParameter?.Type` from the **static definition**.
- `CompositeSearchParameterQueryGenerator.GenerateReferenceTokenQueryAsync` (~307-391) additionally has to
  runtime-*sniff* which built expression is Reference-shaped vs Token-shaped via `IsReferenceExpression`/
  `IsTokenExpression` (~396-432), because DocumentReference's `relationship` composite parameter has component
  *values* that don't reliably match their static component *definitions* — the parser already detects and corrects
  this per-value (see "Effective search parameter" below), but that correction is invisible by the time the SQL
  layer sees only bare leaf expressions.

This is reconstruction of already-known information via heuristics that can be (and in one confirmed case, are)
wrong. This phase fixes it by carrying the parse-time semantic identity all the way to the point where it's needed,
instead of discarding and re-deriving it.

## Goals

- Every composite component's resolved (effective) `SearchParameterInfo` and position survives from parse time to
  SQL query generation, replacing heuristic reconstruction with direct reads.
- Delete `IsReferenceExpression`/`IsTokenExpression` — the ordering problem they exist to work around is solved
  upstream instead.
- Fix a confirmed pre-existing bug this rewrite exposes: composite OR-of-value-groups
  (`code-value-quantity=a$1,b$2`) currently ANDs components across groups instead of unioning per-group results.
- Zero behavior change to non-composite search, and zero signature change to
  `CompositeSearchParameterQueryGenerator`'s public methods (protects the existing, extensively-tested
  `CompositeSearchParameterQueryGeneratorTests.cs` suite from Phase 0/1).

## Non-goals

- Removing leaf-level `ComponentIndex` from `BinaryExpression`/`StringExpression`/etc. `DateTimeEqualityRewriter`'s
  pattern matcher and expression equality/hashing still depend on it. That's a separate, later cleanup.
- Fixing `CompositeSearchParameterQueryGenerator.ApplyDateTimeFilter`'s last-writer-wins overwrite bug (composite
  DateTime `eq` searches can drop the lower bound). Confirmed pre-existing, out of scope because decision 3 below
  keeps that class untouched. Tracked as a follow-up (see "Out of scope" at the end).
- Testcontainers / EF InMemory infrastructure changes. Already confirmed unnecessary for this phase (no
  `EF.Constant()`/`Collate` usage in any touched file).

## Design

### 1. New type: `CompositeComponentExpression`

`src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs`:

```csharp
public sealed class CompositeComponentExpression : Expression
{
    public CompositeComponentExpression(SearchParameterInfo componentSearchParameter, int position, Expression wrappedExpression)
    {
        ComponentSearchParameter = componentSearchParameter ?? throw new ArgumentNullException(nameof(componentSearchParameter));
        Position = position;
        WrappedExpression = wrappedExpression ?? throw new ArgumentNullException(nameof(wrappedExpression));
    }

    public SearchParameterInfo ComponentSearchParameter { get; }
    public int Position { get; }
    public Expression WrappedExpression { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        => visitor.VisitCompositeComponent(this, context);

    public override string ToString()
        => $"(Component[{Position}] {ComponentSearchParameter.Code} {WrappedExpression})";

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(CompositeComponentExpression));
        hashCode.Add(Position);
        WrappedExpression.AddValueInsensitiveHashCode(ref hashCode);
    }

    public override bool ValueInsensitiveEquals(Expression other)
        => other is CompositeComponentExpression cce &&
           cce.Position == Position &&
           WrappedExpression.ValueInsensitiveEquals(cce.WrappedExpression);
}
```

Design points settled during review:

- **Does not implement `IFieldExpression`.** `WrappedExpression` is frequently a `MultiaryExpression` (e.g. a token
  component is `And(system, code)`) which has no single `FieldName` — a delegating property would have to throw,
  violating "properties don't throw." Nothing needs it: the only two `is IFieldExpression` call sites today
  (`ExtractComponentExpressions` lines 214, 261) are deleted by this phase. `ComponentSearchParameter`/`Position` are
  the real source of truth.
- **`ComponentSearchParameter` stores the *effective* `SearchParameterInfo`** — the possibly-synthetic,
  value-inferred one built at `SearchParameterExpressionParser.cs:141-156` (`effectiveSearchParameter`), never the
  raw `Component[i].ResolvedSearchParameter`. This is the load-bearing detail: for DocumentReference's `relationship`
  parameter, the value at a given position can be Reference-shaped or Token-shaped independent of what the static
  component definition claims, and the parser already resolves that ambiguity per-value. If the wrapper stored the
  raw definition instead, the SQL layer would see the same swapped-order ambiguity it has today, just moved.

### 2. Visitor interface: `VisitCompositeComponent`

New method on `IExpressionVisitor<TContext, TOutput>` (`src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs`):

```csharp
TOutput VisitCompositeComponent(CompositeComponentExpression expression, TContext context);
```

Four root implementers require an addition (confirmed by direct read of each file — there are four, not three):

| Implementer | Behavior | Why |
|---|---|---|
| `DefaultExpressionVisitor<TContext,TOutput>` | `public virtual TOutput VisitCompositeComponent(...) => default;` | Matches every other default in this class — scan-only, no rebuild semantics needed. |
| `SearchQueryInterpreter` (InMemory backend) | `return expression.WrappedExpression.AcceptVisitor(this, context);` | Zero `ComponentIndex`/component-identity awareness today; evaluating the wrapped predicate as-is is behavior-preserving. |
| `ExpressionRewriter<TContext>` | **Rebuild, not strip** — see below | Naive unwrap-and-return silently discards the wrapper on any rewrite, breaking downstream extraction. Confirmed live bug path via `DateTimeEqualityRewriter`. |
| `SearchExpressionQueryBuilder` (SQL backend) | `throw new NotSupportedException("CompositeComponentExpression must be unwrapped by SearchParameterQueryGenerator before reaching the generic visitor dispatch.")` | See "Why throw" below. |

**`ExpressionRewriter<TContext>` implementation** (`src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs`), matching
the file's existing rebuild-if-changed idiom (identical shape to `VisitChained`/`VisitNotExpression`):

```csharp
public virtual Expression VisitCompositeComponent(CompositeComponentExpression expression, TContext context)
{
    Expression visitedExpression = expression.WrappedExpression.AcceptVisitor(this, context);
    if (ReferenceEquals(visitedExpression, expression.WrappedExpression)) return expression;

    return new CompositeComponentExpression(expression.ComponentSearchParameter, expression.Position, visitedExpression);
}
```

Why this matters concretely: `DateTimeEqualityRewriter` (`ExpressionRewriterWithInitialContext<object>`, used in
`SearchOptionsBuilder`) explicitly opts into composite parameters with a Date component
(`VisitSearchParameter`, line 22-23) and rewrites the date component's inner `And(ge DateTimeStart x, le DateTimeEnd
y)` into a 3-expression range-scan pattern. That inner `And` sits one level *below* the `CompositeComponentExpression`
wrapper (only the whole component is wrapped, not each leaf inside it), so the outer composite `And(CCE0, CCE1)` is
visited first (`VisitMultiary` → recurses into each child via `AcceptVisitor` → dispatches to `VisitCompositeComponent`
on the Date component → recurses into `WrappedExpression` → hits `DateTimeEqualityRewriter.VisitMultiary` correctly on
the inner And → pattern matches and rewrites). With the rebuild-if-changed shape above, the outer wrapper is correctly
reconstructed around the rewritten inner expression, so `ExtractComponentExpressions`' consumer still sees a
`CompositeComponentExpression` at that position afterward. With a naive strip-and-return, the wrapper is gone,
`ExtractComponentExpressions`' consumer no longer recognizes that position as a component at all, and every composite
Token+DateTime `eq` search silently returns nothing.

**Why `SearchExpressionQueryBuilder` throws instead of passing through:** confirmed by reading
`SearchExpressionQueryBuilder.VisitSearchParameter` (line 121) — it unconditionally delegates *all* search-parameter
handling, composite or not, to `SearchParameterQueryGenerator.GenerateQueryAsync`, which internally walks the
composite's structure directly (never via `AcceptVisitor` on individual components). There is no legitimate path by
which a `CompositeComponentExpression` reaches the generic visitor dispatch on this class. A silent passthrough here
would mask exactly one thing — a future wiring bug that bypasses composite handling — and its failure mode would be
evaluating correlated multi-field components as independent uncorrelated predicates against separate index rows:
plausible-looking wrong results in a healthcare search, not a crash. Per this repo's own "fail fast for programmer
errors, no silent failures" standard, throw.

### 3. Parser: wrap at construction

`SearchParameterExpressionParser.cs`, composite branch (~line 158-162), changes from:

```csharp
compositeExpressions[componentIndex] = Build(
    effectiveSearchParameter,
    null,
    componentIndex,
    componentValue);
```

to:

```csharp
Expression componentExpression = Build(
    effectiveSearchParameter,
    null,
    componentIndex,
    componentValue);

compositeExpressions[componentIndex] = new CompositeComponentExpression(
    effectiveSearchParameter,
    componentIndex,
    componentExpression);
```

`Build(...)`'s internal call to `helper.Build(..., componentIndex, ...)` is unchanged — leaf-level `ComponentIndex`
stays populated exactly as today (non-goal: removing it). The overall shape produced by the composite branch is
unchanged structurally, just with each component now wrapped:

- Single value group: `SearchParameterExpression(Composite, And(CCE(pos:0), CCE(pos:1)))`
- Multiple OR'd value groups (`a$1,b$2`): `SearchParameterExpression(Composite, Or(And(CCE0_g1, CCE1_g1), And(CCE0_g2, CCE1_g2)))`

### 4. `SearchParameterQueryGenerator`: consume wrappers, unwrap before calling the composite generator

`ProcessCompositeExpressionAsync` and `ExtractComponentExpressions` (~146-295) are rewritten. `DetermineCompositeType`
is **not called differently** — it still reads static `Component[i].ResolvedSearchParameter.Type` to pick which
physical SQL table/route to use (that's a static, definition-level routing decision, orthogonal to the
per-value effective-type correction the wrapper carries).

**Extraction — replaces `ExtractComponentExpressions`'s `ComponentIndex`-HashSet heuristic with direct type
matching, and fixes the OR-of-groups bug as part of the rewrite:**

```csharp
private static List<List<CompositeComponentExpression>> ExtractComponentGroups(Expression expr) =>
    expr is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } orExpr
        ? orExpr.Expressions.Select(ExtractSingleGroup).ToList()
        : [ExtractSingleGroup(expr)];

private static List<CompositeComponentExpression> ExtractSingleGroup(Expression expr) => expr switch
{
    CompositeComponentExpression cce => [cce],
    MultiaryExpression { MultiaryOperation: MultiaryOperator.And } andExpr =>
        andExpr.Expressions.OfType<CompositeComponentExpression>().OrderBy(c => c.Position).ToList(),
    _ => []
};
```

**`ProcessCompositeExpressionAsync`** processes each OR-group independently and unions (OR-combines) the per-group
resource-ID queries — this is the fix for the confirmed pre-existing bug (today, components from different OR groups
get merged by index and ANDed, producing wrong/empty results for `code-value-quantity=a$1,b$2`):

```csharp
private async Task<IQueryable<long>> ProcessCompositeExpressionAsync(
    short? resourceTypeId, short searchParamId, SearchParameterInfo searchParameter, Expression expr, CancellationToken ct)
{
    var compositeType = _compositeQueryGenerator.DetermineCompositeType(searchParameter);

    if (compositeType == CompositeType.Unknown)
    {
        _logger.LogWarning("Unknown composite type for parameter {Code}, falling back to non-composite search", searchParameter.Code);
        return await ProcessExpressionAsync(resourceTypeId, searchParamId, UnwrapCompositeComponents(expr), ct);
    }

    var groups = ExtractComponentGroups(expr);

    if (groups.Count == 0 || groups.Any(g => g.Count < 2))
    {
        _logger.LogWarning("Composite parameter {Code} requires at least 2 components in every OR group", searchParameter.Code);
        return Enumerable.Empty<long>().AsQueryable();
    }

    var groupQueries = new List<IQueryable<long>>(groups.Count);
    foreach (var group in groups)
        groupQueries.Add(await GenerateGroupQueryAsync(resourceTypeId, searchParamId, compositeType, group, ct));

    return groupQueries.Count == 1 ? groupQueries[0] : CombineWithOr(groupQueries);
}

private async Task<IQueryable<long>> GenerateGroupQueryAsync(
    short? resourceTypeId, short searchParamId, CompositeType compositeType, List<CompositeComponentExpression> group, CancellationToken ct)
{
    return compositeType switch
    {
        CompositeType.TokenToken => await _compositeQueryGenerator.GenerateTokenTokenQueryAsync(
            resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

        CompositeType.TokenQuantity => await _compositeQueryGenerator.GenerateTokenQuantityQueryAsync(
            resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

        CompositeType.TokenString => await _compositeQueryGenerator.GenerateTokenStringQueryAsync(
            resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

        CompositeType.TokenDateTime => await _compositeQueryGenerator.GenerateTokenDateTimeQueryAsync(
            resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

        CompositeType.ReferenceToken => await GenerateReferenceTokenGroupQueryAsync(resourceTypeId, searchParamId, group, ct),

        _ => Enumerable.Empty<long>().AsQueryable()
    };
}

private async Task<IQueryable<long>> GenerateReferenceTokenGroupQueryAsync(
    short? resourceTypeId, short searchParamId, List<CompositeComponentExpression> group, CancellationToken ct)
{
    // Order determined from the parser's effective (value-inferred) type, not fixed position —
    // replaces IsReferenceExpression/IsTokenExpression sniffing on the built expression shape.
    //
    // FirstOrDefault, not SingleOrDefault: both components can legitimately infer the same effective
    // type (e.g. relationship=DocumentReference/a$DocumentReference/b infers Reference twice;
    // relationship=sys|a$sys2|b infers Token twice; a bare-code value with no inference signal falls
    // back to a possibly-wrong static definition type). SingleOrDefault throws InvalidOperationException
    // on these reachable inputs, turning a degraded-but-200 search into a 500. FirstOrDefault degrades
    // to the warning branch below instead, same as today's "unable to determine component types" fallback
    // in IsReferenceExpression/IsTokenExpression — this is a deliberate behavior change from today's
    // "assume position order and return plausible-garbage filters" to "warn and return empty results",
    // consistent with this design's stance elsewhere (Q1) that plausible-wrong results are worse than
    // an empty/degraded result in a healthcare search. Covered by a dedicated ambiguous-order test.
    var referenceComponent = group.FirstOrDefault(c => c.ComponentSearchParameter.Type == SearchParamType.Reference);
    var tokenComponent = group.FirstOrDefault(c => c.ComponentSearchParameter.Type == SearchParamType.Token);

    if (referenceComponent == null || tokenComponent == null)
    {
        _logger.LogWarning("Reference|Token composite SearchParamId={SearchParamId} did not resolve one Reference and one Token component", searchParamId);
        return Enumerable.Empty<long>().AsQueryable();
    }

    return await _compositeQueryGenerator.GenerateReferenceTokenQueryAsync(
        resourceTypeId, searchParamId, referenceComponent.WrappedExpression, tokenComponent.WrappedExpression, ct);
}
```

`CombineWithOr` does **not** already exist on `SearchParameterQueryGenerator` — verified by grep; it exists only as
a `private static` on `SearchExpressionQueryBuilder` (`SearchExpressionQueryBuilder.cs:354-368`). Add an equivalent
`private static IQueryable<long> CombineWithOr(List<IQueryable<long>> queries)` to `SearchParameterQueryGenerator`,
copying `SearchExpressionQueryBuilder`'s implementation verbatim (`Concat`-then-`Distinct`, not chained `Union` —
the existing comment there explains why: chained `Union` nests deeply and can stack-overflow EF Core's expression
tree funcletizer past ~100 queries; `Concat`+`Distinct` gives the same OR semantics with a flat tree):

```csharp
private static IQueryable<long> CombineWithOr(List<IQueryable<long>> queries)
{
    if (queries.Count == 0)
        throw new ArgumentException("Cannot combine zero queries", nameof(queries));

    var result = queries.Aggregate((current, next) => current.Concat(next));
    return result.Distinct();
}
```

**Unknown-type fallback must unwrap, not just pass the raw tree through** — otherwise `ProcessExpressionAsync`
receives `CompositeComponentExpression` nodes it doesn't understand and throws instead of degrading gracefully
(behavior change we didn't sign up for):

```csharp
private static Expression UnwrapCompositeComponents(Expression expr) => expr switch
{
    CompositeComponentExpression cce => UnwrapCompositeComponents(cce.WrappedExpression),
    MultiaryExpression m => new MultiaryExpression(m.MultiaryOperation, m.Expressions.Select(UnwrapCompositeComponents).ToList()),
    NotExpression n => new NotExpression(UnwrapCompositeComponents(n.Expression)),
    _ => expr
};
```

### 5. `CompositeSearchParameterQueryGenerator`: unchanged

Per decision 3 (blast-radius containment): `GenerateTokenTokenQueryAsync`, `GenerateTokenQuantityQueryAsync`,
`GenerateTokenStringQueryAsync`, `GenerateTokenDateTimeQueryAsync`, `GenerateReferenceTokenQueryAsync` keep their
exact current signatures (`Expression component0, Expression component1`). `IsReferenceExpression`/
`IsTokenExpression` (~396-432) are **deleted** — their job is now done upstream by
`GenerateReferenceTokenGroupQueryAsync`'s type-based ordering. `DetermineCompositeType` is unchanged.
`CompositeSearchParameterQueryGeneratorTests.cs` requires no changes.

## Testing strategy

- **`CompositeComponentExpression`**: unit tests for `AcceptVisitor` dispatch, `ToString`, `ValueInsensitiveEquals`/
  `AddValueInsensitiveHashCode` (mirroring existing `BinaryExpression`/`StringExpression` test patterns).
- **`ExpressionRewriter<TContext>.VisitCompositeComponent`**: a concrete test rewriter (or `DateTimeEqualityRewriter`
  directly) verifying a composite Token+DateTime `eq` search's date component still gets the 3-expression range-scan
  rewrite, AND that the `CompositeComponentExpression` wrapper survives the rewrite (regression test for the bug
  this design amendment exists to prevent).
- **`SearchExpressionQueryBuilder.VisitCompositeComponent`**: single test asserting `NotSupportedException`.
- **`SearchParameterExpressionParser`**: composite parsing produces `CompositeComponentExpression`-wrapped
  components with `ComponentSearchParameter` set to the *effective* (inferred) type, including the DocumentReference
  `relationship` swapped-value case.
- **`SearchParameterQueryGenerator`**: 
  - One test per composite type (all 6) confirming `ExtractComponentGroups`/`GenerateGroupQueryAsync` route
    correctly and produce the same SQL predicate as the equivalent pre-Phase-2 hand-built expression (regression
    coverage against `CompositeSearchParameterQueryGeneratorTests.cs`'s existing fixtures, adapted to go through the
    parser+generator path end-to-end rather than constructing bare expressions).
  - **New**: OR-of-groups test — `code-value-quantity=a$1,b$2` returns the union of matches for each group, not the
    (currently wrong) intersection/AND-across-groups.
  - **New**: Reference|Token swapped-order test (DocumentReference `relationship`) confirming
    `GenerateReferenceTokenGroupQueryAsync` resolves the correct component regardless of position, replacing the
    `IsReferenceExpression`/`IsTokenExpression`-covering test that exercised the old sniffing path.
  - **New**: Reference|Token ambiguous-order test — both components infer the same effective type (e.g. both
    values contain `/`, or both contain `|`) — confirming the warning branch is hit and empty results are returned,
    not an `InvalidOperationException`. This intentionally changes behavior from today's "assume position order,
    return plausible-garbage filters" fallback; call it out in the PR description alongside the OR-of-groups fix.
  - Unknown-composite-type fallback test confirming it still degrades gracefully (doesn't throw) once components are
    unwrapped.

## Out of scope — tracked follow-ups

1. **`CompositeSearchParameterQueryGenerator.ApplyDateTimeFilter` last-writer-wins bug** (~lines 682-741): composite
   DateTime `eq` searches can silently drop the lower bound because the 3-expression range-scan pattern's `ge`/`le`
   pair overwrites a single start/end slot instead of accumulating both bounds. Confirmed real, needs a test and a
   fix, but this class is intentionally untouched by Phase 2 (decision 3). File as a follow-up item in this repo's
   plan-tracking doc once Phase 2 lands.
2. Leaf-level `ComponentIndex` removal (superseded by `Position` on the wrapper) — deferred; still load-bearing for
   `DateTimeEqualityRewriter.MatchPattern` and expression equality/hashing today.
3. **`DateTimeEqualityRewriter.MatchPattern` doesn't match `eq`'s current output shape** (discovered during Phase 2
   Task 3, Fable-verified): commit `23c18854` (2025-12-09, six weeks before Phase 2 started) changed
   `SearchValueExpressionBuilderHelper`'s `SearchComparator.Eq` case from containment semantics
   (`And(GE(DateTimeStart, x), LE(DateTimeEnd, y))`) to overlap-check semantics
   (`And(LE(DateTimeStart, y), GE(DateTimeEnd, x))`) — a genuinely different relationship between the two bounds, not
   just a field/operator swap. `MatchPattern` still only recognizes the old containment pairing, so
   `DateTimeEqualityRewriter`'s 3-expression range-scan-friendly rewrite has been dead code for every `eq` Date
   search (composite or plain) since that commit — unrelated to Phase 2, pre-existing, six weeks older than this
   phase's Task 1. **Not fully dead**: the `ap` (approximate) comparator still emits the old containment shape, so
   the rewriter still fires for `ap` searches today — don't delete it as unreachable.
   **No safe local fix exists**: under containment, `Start >= x AND End <= y` plus the `Start <= End` invariant
   trivially implies `Start <= y`, which is why the rewriter's third clause is a provably-redundant, index-friendly
   tightening. Under overlap (`Start <= y AND End >= x`), no analogous bound on `Start` is derivable the same way —
   a stored range `[1900, 2100]` legitimately overlaps a one-day search window, so there's no finite lower bound to
   add. The only known correct approach under overlap semantics is a range-width split (bucket short vs. long
   stored ranges and derive different bounds per bucket, per Microsoft FHIR Server's `IsLongerThanADay` pattern) —
   a schema/query-generator design decision requiring its own investigation, not a one-line `MatchPattern` fix.
   Interacts with follow-up #1 above: fixing `ApplyDateTimeFilter`'s overwrite bug without also fixing this one (or
   vice versa) changes observable behavior, since the 3-expression pattern `ApplyDateTimeFilter` consumes currently
   never arrives for `eq`. Both should be investigated together.
4. **`SearchParameterExpressionParser.InferSearchParamTypeFromValue` misclassifies quantity-with-unit
   values as Token** (discovered during Phase 2's final whole-branch review; pre-existing, predates
   this phase, but Phase 2 makes the effective type it produces load-bearing everywhere downstream).
   The heuristic (`src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:369-406`)
   infers `Token` for any value containing `|`, with no further discrimination. FHIR quantity values
   legitimately carry units in `value|system|code` form (e.g. `code-value-quantity=8480-6$gt150|http://unitsofmeasure.org|mm[Hg]`)
   — that component also contains `|`, so it gets misclassified as Token even though its static
   component definition is Quantity. The parser then builds a Token-shaped expression from a quantity
   value; downstream, the composite quantity filter finds no quantity comparison to apply and silently
   matches on the token component alone, ignoring the value entirely — a real correctness gap, not
   theoretical. Likely fix direction: only apply value-shape inference to composites whose static
   component definitions include a Reference component (the only confirmed case needing it, per
   DocumentReference's `relationship` parameter), or explicitly exclude values that parse successfully
   as a quantity/number before inferring Token from the presence of `|`. Needs its own investigation and
   fix, tracked here rather than fixed as part of Phase 2.

## Risks

| Risk | Mitigation |
|---|---|
| Missing one of the 4 `IExpressionVisitor` implementers | Compile error — interface addition is non-optional for all implementers. |
| `ExpressionRewriter` strip-instead-of-rebuild silently breaking composite Date `eq` searches | Explicit regression test (see Testing strategy); this was caught in design review, not left implicit. |
| OR-of-groups fix changing behavior for existing composite OR searches in production | This is a bug fix, not new behavior — today's AND-across-groups is wrong per FHIR composite semantics (each comma-separated group is an independent match candidate, OR'd together). Call out explicitly in the PR description. |
| `CompositeType.Unknown` fallback throwing instead of degrading | `UnwrapCompositeComponents` restores exact pre-Phase-2 tree shape before falling back; test coverage confirms no throw. |
