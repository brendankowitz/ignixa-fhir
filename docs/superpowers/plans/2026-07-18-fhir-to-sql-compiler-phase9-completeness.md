# Compiler Completeness (Phase 9): count/_total and :missing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `_summary=count`/`_total=accurate` (a count-only compiled query shape) and the `:missing` search modifier (across all 7 leaf types and all 6 composite types) to `Ignixa.Search.Sql`.

**Architecture:** `CountOnly` is a new `bool` field on `QueryPlan`, consumed only by `Emit` at the terminal-`SELECT` site — zero new `CteDefinition` node kinds, since the match graph that determines *which* resources match is identical whether the caller wants rows or a count. `:missing` reuses the *existing* `:not`/`Except` machinery verbatim: a new `CteDefinition.ParamSource` shape with no value predicate ("any row exists for this parameter") is built via one new `StructuralContext` method, then optionally wrapped in the already-existing `context.LowerNot(...)` for the `:missing=true` case. One small, purely-additive AST change (`ParamSource.Predicate` becomes nullable) unlocks both the leaf and composite cases through a single mechanism.

**Tech Stack:** C# / .NET 9+, xUnit + Shouldly, `Ignixa.Search.Sql` (Core-tier, no EF/ASP.NET references).

**Full design:** `docs/superpowers/specs/2026-07-18-fhir-to-sql-compiler-phase9-completeness-design.md` — read this first for the *why*. This plan corrects two things the design doc's prose got wrong, found by re-deriving ground truth against the real current files rather than trusting the spec's assertions (as required by this project's own plan-writing discipline):

1. **The spec's §4 assumed `:missing` is a modifier on `SearchParameterPredicateExpression`, mirroring `:not`'s exact shape.** It is not. The real binder (`SearchExpressionBinder.cs:66-71`) produces a dedicated `MissingSearchParameterExpression` (`Parameter`, `bool IsMissing`), a sibling `Expression` subtype dispatched at `Lower.cs`'s top-level `LowerNode` switch — the same tier as `SearchParameterExpression`/`ChainedExpression`/`CompartmentSearchExpression`, not nested inside `LowerSearchParameter`. This is architecturally *simpler* than the spec assumed (no interaction with composite-detection logic to worry about — it's a fully separate case), but it means:
2. **The spec's claim that `ResourceColumnLoweringRule` "already correctly rejects `:missing` for `_id`/`_type`/`_lastUpdated`" is false.** That guard fires on `predicate.Modifier is not null`, and `MissingSearchParameterExpression` has no `.Modifier` property at all — it never reaches that guard. This plan's Task 3 adds a real, new rejection guard (mirroring `StructuralContext.RejectResourceColumnCode`, already used identically by `LowerComposite`).

The spec's core design intent — reuse `:not`/`Except`, one generic mechanism, `Predicate.True`-shaped "no filter" `ParamSource` — is otherwise unchanged. Where the spec said `Predicate.True` (a new AST case), this plan uses **`ParamSource.Predicate` becoming nullable instead**, matching `CteDefinition.ResourceSource`'s own already-established `Predicate? Predicate = null` shape (`CteDefinition.cs:33`) and its already-existing conditional-rendering precedent in `EmitResourceSource` — a smaller, better-precedented change than introducing new AST surface.

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework) are pre-existing and out of scope, per every prior increment on this branch.
- **`ParamSource`'s new shape**: `public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate? Predicate = null) : CteDefinition;` — purely additive (a default value on an already-required positional parameter position does NOT break existing callers passing a concrete `Predicate`; C# has no ABI distinction between `Predicate` and `Predicate?` for a reference type at call sites). **Zero of the 13 existing `ParamSource` construction call sites (7 leaf + 6 composite lowering rules) need any edit.**
- **`Emit`'s `EmitParamSource` must render conditionally**, matching `EmitResourceSource`'s already-existing pattern (`Emit.cs:378-384`, `rs.Predicate is null ? string.Empty : $" AND {EmitPredicate(...)}"`) — when `p.Predicate is null`, the `WHERE` clause is `WHERE ResourceTypeId = {rt} AND SearchParamId = {sp}` only (no trailing `AND {predicate}`, never `AND 1=1`).
- **`QueryPlan.CountOnly`**: `bool CountOnly = false`, purely additive, trailing field.
- **`Emit`'s count-terminal-SELECT shape**: `SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte{Match} m [INNER JOIN dbo.Resource r ON ... WHERE {OuterPredicate}]` — always `COUNT_BIG(DISTINCT m.Sid1)`, never a separate unconditional `COUNT_BIG(*)` branch (unlike real fhir-server's dual-mode `SqlQueryGenerator.cs:236-249`) — Ignixa's `QueryPlan.Match` always points at a real CTE (even an unconditional type search goes through a `ResourceSource` CTE), so there is no "no search-param table expressions" case to special-case the way fhir-server's generator does. `DISTINCT` is required unconditionally: a `Union`-rooted match (compartment search, wildcard compartment search) can legitimately produce duplicate `Sid1` values across branches.
- **`CountOnly` ignores `Top`/`Sort`/`Page`/`Includes` entirely** — none of `plan.Top`, `plan.Sort`, `plan.Page`, `plan.Includes` are consulted when `plan.CountOnly` is true. A count has no row order, no page boundary, no included resources.
- **`Lower.Run` gains `bool countOnly = false`**, a pure pass-through onto `QueryPlan.CountOnly` — no lowering-tier logic (the match graph construction is completely unaffected by whether the caller wants a count or rows).
- **`_total=estimate` throws** `NotSupportedException("_total=estimate is not supported -- real fhir-server does not implement this distinctly from _total=accurate either (TotalType.Estimate exists as an enum value but is never consumed in Microsoft.Health.Fhir.Core or Microsoft.Health.Fhir.SqlServer). Use _total=accurate.")` — exact message, verbatim.
- **`:missing`'s real expression shape**: `MissingSearchParameterExpression` (`src/Core/Ignixa.Search/Expressions/MissingSearchParameterExpression.cs`) — `public MissingSearchParameterExpression(SearchParameterInfo searchParameter, bool isMissing)`, inherits `Parameter` from `SearchParameterExpressionBase`, exposes `bool IsMissing`. Dispatched via `Lower.cs`'s top-level `LowerNode` switch, NOT nested inside `LowerSearchParameter`.
- **`StructuralContext.LowerParameterPresence(SearchParameterInfo parameter, string resourceType)` is the one new machinery method**: builds a `ParamSource` scoped to `(ResourceTypeId, SearchParamId)` with `Predicate: null`, calling `RejectResourceColumnCode(parameter.Code)` first (matching `LowerComposite`'s existing precedent at `StructuralContext.cs:44`) — `_id`/`_type`/`_lastUpdated` combined with `:missing` must throw, not silently compile.
- **Leaf-type table resolution** (`SearchParamType` → table name): `String`→`StringSearchParam`, `Token`→`TokenSearchParam`, `Reference`→`ReferenceSearchParam`, `Uri`→`UriSearchParam`, `Number`→`NumberSearchParam`, `Quantity`→`QuantitySearchParam`, `Date`→`DateTimeSearchParam`. `Composite`/`Special` are not leaf types (handled by Task 4 / rejected).
- **Composite-type table resolution** (STATIC, via `SearchParameterInfo.Component`, not runtime values — `CompositeLoweringDispatcher`'s existing runtime-value-based dispatch is NOT reused, since `:missing` has no component values to inspect): `parameter.Component.Select(c => c.ResolvedSearchParameter.Type).ToArray()` against the 6 real table names (confirmed against `97.sql`): `[Token, Token]`→`TokenTokenCompositeSearchParam`, `[Token, Number, Number]`→`TokenNumberNumberCompositeSearchParam`, `[Token, String]`→`TokenStringCompositeSearchParam`, `[Token, Quantity]`→`TokenQuantityCompositeSearchParam`, `[Token, Date]`→`TokenDateTimeCompositeSearchParam`, `[Reference, Token]` or `[Token, Reference]`→`ReferenceTokenCompositeSearchParam`.
- **`SearchParameterInfo.Component`'s list order is the canonical component order** (`SearchParameterComponentInfo` has no separate `Position` field — unlike the runtime `CompositeComponentExpression.Position` `CompositeLoweringDispatcher` orders by) — populated once at search-parameter-definition load time (`SearchParameterDefinitionBuilder.cs`/`SearchParameterDefinitionManager.cs`), not per-request. Task 4 must write a test proving `ResolvedSearchParameter` is actually populated for a real composite `SearchParameterInfo` before relying on it, not just assume the design doc's/this plan's claim.
- **Supported `:missing` scope this phase**: all 7 leaf types + 6 composite types + the `_id`/`_type`/`_lastUpdated` rejection. Explicitly out of scope, throwing loudly where reached (not silently ignored): `_total=estimate`, Reference `:identifier`/type modifiers, multi-level `:iterate` recursion — none of these are touched by this plan.
- **Every test pins exact `Explain()`/`Emit.Run` SQL shapes, never loose `ShouldContain`/non-null checks** — this project's own seventh-increment retrospective found a test that would have passed identically whether a predicate was correctly compiled or silently dropped; do not repeat that mistake.

---

### Task 1: `ParamSource.Predicate` becomes nullable; `Emit` renders conditionally

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (foundational).
- Produces: `CteDefinition.ParamSource(TableDescriptor, short, short, Predicate? = null)`. Task 3 (`StructuralContext.LowerParameterPresence`) is the primary consumer of the `Predicate: null` case; Tasks 2 (CountOnly) does not depend on this.

- [ ] **Step 1: Widen `ParamSource`'s `Predicate` to nullable**

In `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`, change:

```csharp
public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate Predicate) : CteDefinition;
```

to:

```csharp
public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate? Predicate = null) : CteDefinition;
```

- [ ] **Step 2: Make `EmitParamSource` render conditionally**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, change:

```csharp
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE ResourceTypeId = {p.ResourceTypeId} AND SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";
```

to:

```csharp
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
    {
        var predicateClause = p.Predicate is null ? string.Empty : $" AND {EmitPredicate(p.Predicate, parameters)}";
        return $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
               $"    WHERE ResourceTypeId = {p.ResourceTypeId} AND SearchParamId = {p.SearchParamId}{predicateClause}";
    }
```

This mirrors `EmitResourceSource`'s existing pattern exactly (`Emit.cs:378-384`) — do not invent a different conditional shape.

- [ ] **Step 3: Confirm zero existing call sites break**

Run `grep -rn "new CteDefinition.ParamSource(" src/Core/Ignixa.Search.Sql/` — confirm all 13 existing call sites (7 leaf lowering rules, 6 composite lowering rules) pass a concrete, non-null `Predicate` as the 4th positional argument. None need editing; this step is a verification, not a code change. If you find a call site that does NOT pass a 4th argument at all (relying on some other default), stop and report — that would be a sign this plan's premise about the current file state is stale.

- [ ] **Step 4: Write the new test**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
    [Fact]
    public void GivenAParamSourceWithNoPredicate_WhenEmitted_ThenTheWhereClauseHasNoTrailingAndClause()
    {
        // Arrange -- the shape Task 3's LowerParameterPresence will produce: "any row exists for this parameter."
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202\n" +
            ")\n" +
            "SELECT m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.ShouldBeEmpty();
    }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj` — expect 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` — expect all existing tests (226 as of this plan's writing) plus the 1 new test, all passing on both net9.0/net10.0.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs src/Core/Ignixa.Search.Sql/Ast/Emit.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs
git commit -m "feat(search-sql): make ParamSource.Predicate nullable, Emit renders conditionally"
```

---

### Task 2: `QueryPlan.CountOnly`, `Lower.Run` threading, `Emit`'s count-terminal-SELECT, `_total=estimate` guard

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (independent feature).
- Produces: `QueryPlan.CountOnly`, `Lower.Run(..., bool countOnly = false, ...)`. Task 5's combined-proof tests are the primary consumer of the full `Resolve→Lower→Emit` pipeline with `countOnly: true`.

- [ ] **Step 1: `QueryPlan.CountOnly`**

In `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`, change:

```csharp
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
```

to:

```csharp
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null,
    bool CountOnly = false)
{
    public string Explain() => PlanExplainer.Print(this);
}
```

Update the class's XML doc comment: append, after the existing final sentence ending "...existed.", the new sentence: ` CountOnly (Phase 9) is a third tier-3 result-shape field -- when true, Emit ignores Top/Sort/Page/Includes entirely and renders a single COUNT_BIG(DISTINCT Sid1) terminal SELECT instead of any row-returning shape; a plan with CountOnly false (the default) is byte-identical to before this field existed.`

- [ ] **Step 2: `Emit.Run`'s count branch**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, `Run`'s very first line after building `cteBlocks` currently computes `top` and then branches on `plan.Includes`. Add the `CountOnly` check BEFORE that branch (count-only plans never need the includes machinery, sort joins, or `top` at all):

Change:

```csharp
        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"cte{i} AS (\n{EmitCte(plan.Ctes[i], parameters)}\n)");
        }

        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;

        if (plan.Includes is not { Count: > 0 } includes)
```

to:

```csharp
        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"cte{i} AS (\n{EmitCte(plan.Ctes[i], parameters)}\n)");
        }

        if (plan.CountOnly)
        {
            var countWithClause = $";WITH {string.Join(",\n", cteBlocks)}\n";
            var countSql = plan.OuterPredicate is null
                ? countWithClause + $"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte{plan.Match.Index} m"
                : countWithClause +
                  $"SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte{plan.Match.Index} m\n" +
                  $"INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1\n" +
                  $"WHERE {EmitPredicate(plan.OuterPredicate, parameters)}";

            return new EmittedSql(countSql, parameters);
        }

        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;

        if (plan.Includes is not { Count: > 0 } includes)
```

This is placed before the `plan.Includes` branch deliberately — a count-only plan never reaches (or needs) the `cteMatchPage`/`UNION ALL` includes machinery, sort joins, or `TOP`, regardless of whether `plan.Includes`/`plan.Sort`/`plan.Page` happen to be non-null on the `QueryPlan` (a caller should not populate them for a count request, but this ordering means it would not matter if one did — `CountOnly` wins unconditionally).

- [ ] **Step 3: `Lower.Run`'s `countOnly` parameter**

Read `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`'s current `Run` signature and final `QueryPlan` construction in full before editing (this plan's Global Constraints describe the target shape; confirm the exact current parameter list and trailing `return new QueryPlan(...)` call against the real file, since it may have shifted since Phase 8 part 2). Add a new `bool countOnly = false` parameter (place it as the last parameter before `top`, matching this project's established "new orthogonal caller input goes near the end" convention from `SortPhase`/`PageSpec`'s own addition in Phase 8 part 2), and thread it straight onto the final `QueryPlan` construction's new `CountOnly` argument — no other logic in `Run`'s body should reference `countOnly` at all (the match-graph construction, includes, sort, and resource-column extraction are completely unaffected by it).

- [ ] **Step 4: Sweep every `Lower.Run` call site**

Run `grep -rn "Lower\.Run(" --include=*.cs .` from the repo root (re-run yourself, do not trust a list transcribed here, since call sites may have shifted). Insert `countOnly: false,` immediately after the last positional/named argument currently supplied at every existing call site EXCEPT the new tests Task 5 writes (which will pass `countOnly: true` deliberately). Since `countOnly` has a default value, this sweep is only needed if any call site passes ALL of `Lower.Run`'s parameters positionally past where `countOnly` is inserted (which would shift positional arguments) — if every existing call site uses named arguments for everything after `includeLimit:` (matching this project's established style, confirmed in the Phase 8 part 2 plan's own call-site examples), no sweep is needed at all. Verify this directly rather than assuming either way.

- [ ] **Step 5: `_total=estimate` guard — where it lives**

This plan does NOT build a `_total=estimate` parameter into `Ignixa.Search.Sql` (there is no FHIR-level `_total`/`_summary` concept in the compiler's own IR — `CountOnly` is the compiler's only vocabulary for "count, don't return rows"; interpreting a client's `_total=estimate` request and rejecting it is a caller-side (eventual Phase 10 executor) concern, not something `Lower.Run`/`Emit.Run` themselves parse). Add the guard as a clearly-documented note in `Lower.cs`'s class-level XML doc comment instead, so a Phase 10 implementer finds it before wiring `_total` handling: append the sentence: ` Phase 10's executor must reject _total=estimate at its own boundary with NotSupportedException("_total=estimate is not supported -- real fhir-server does not implement this distinctly from _total=accurate either (TotalType.Estimate exists as an enum value but is never consumed in Microsoft.Health.Fhir.Core or Microsoft.Health.Fhir.SqlServer). Use _total=accurate."); Lower.Run/Emit.Run have no _total vocabulary of their own -- CountOnly is the only "count instead of rows" concept this compiler exposes.`

- [ ] **Step 6: `PlanExplainer` rendering**

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, find `Print`'s existing sequence of `if (plan.X is ...) { lines.Add(...) }` blocks (for `Sort`/`Page`) and add, after them:

```csharp
        if (plan.CountOnly)
        {
            lines.Add("countOnly = true");
        }
```

- [ ] **Step 7: Write the new tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
    [Fact]
    public void GivenACountOnlyPlanWithNoOuterPredicate_WhenEmitted_ThenTheSqlIsACountBigDistinctQuery()
    {
        // Arrange -- Patient?name=Smith&_total=accurate, no resource-column predicate.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), CountOnly: true);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m");
        emitted.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenACountOnlyPlanWithAnOuterPredicate_WhenEmitted_ThenTheSqlJoinsResourceAndFiltersBeforeCounting()
    {
        // Arrange -- Patient?_id=abc&_total=accurate (a resource-column OuterPredicate case).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var outerPredicate = new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("abc"));
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0),
            OuterPredicate: outerPredicate, CountOnly: true);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m\n" +
            "INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1\n" +
            "WHERE ResourceId = @p1");
    }

    [Fact]
    public void GivenACountOnlyPlanWithSortAndTopAndIncludesAllSet_WhenEmitted_ThenTheyAreAllIgnored()
    {
        // Arrange -- proves CountOnly wins unconditionally, regardless of what else is set on the plan
        // (a caller should never populate these for a count request, but Emit must not depend on that).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0),
            Top: 10, Sort: sort, Includes: [includeStage], CountOnly: true);

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- no TOP, no ORDER BY, no sort join, no cteMatchPage, no UNION ALL anywhere.
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT COUNT_BIG(DISTINCT m.Sid1) FROM cte0 m");
    }
```

Add to `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`:

```csharp
    [Fact]
    public void GivenCountOnlyTrue_WhenLowered_ThenQueryPlanCountOnlyIsTrue()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, countOnly: true);

        // Assert
        plan.CountOnly.ShouldBeTrue();
    }

    [Fact]
    public void GivenCountOnlyOmitted_WhenLowered_ThenQueryPlanCountOnlyDefaultsFalse()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.CountOnly.ShouldBeFalse();
    }
```

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`:

```csharp
    [Fact]
    public void GivenACountOnlyPlan_WhenExplained_ThenPrintsCountOnlyLine()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), CountOnly: true);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "countOnly = true");
    }
```

- [ ] **Step 8: Run the tests**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj` — expect 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` — expect all previous tests plus these 6 new ones, all passing.

- [ ] **Step 9: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs src/Core/Ignixa.Search.Sql/Ast/Emit.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs
git commit -m "feat(search-sql): add QueryPlan.CountOnly, Lower/Emit support for _summary=count/_total=accurate"
```

---

### Task 3: `:missing` for leaf types — `MissingSearchParameterExpression` dispatch, `LowerParameterPresence`, resource-column rejection

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`

**Interfaces:**
- Consumes: `ParamSource(..., Predicate? = null)` (Task 1).
- Produces: `StructuralContext.LowerParameterPresence(SearchParameterInfo, string)`. Task 4 (composite `:missing`) extends this method's table-resolution switch; Task 5's combined-proof tests are the primary consumer of the full pipeline.

- [ ] **Step 1: Read `Lower.cs`'s `LowerNode` and `StructuralContext.cs`'s `LowerNot`/`LowerComposite` in full**

Confirm the exact current shape of `LowerNode`'s top-level switch (where `MissingSearchParameterExpression` needs a new arm) and `StructuralContext`'s constructor/private fields (`_leafContext`, `_ctes`) before writing this task's code — this plan's Global Constraints describe the target shape from an earlier read; the real file is the source of truth.

- [ ] **Step 2: Add `StructuralContext.LowerParameterPresence`**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, add a new public method (after `LowerComposite`, before `RejectResourceColumnCode`):

```csharp
    public CteRef LowerParameterPresence(SearchParameterInfo parameter, string resourceType)
    {
        RejectResourceColumnCode(parameter.Code);

        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var searchParamId = _leafContext.SearchParamId(parameter);
        var table = ResolveMissingTable(parameter);

        var cte = new CteDefinition.ParamSource(table, resourceTypeId, searchParamId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    private static TableDescriptor ResolveMissingTable(SearchParameterInfo parameter)
    {
        var tableName = parameter.Type switch
        {
            SearchParamType.String => "StringSearchParam",
            SearchParamType.Token => "TokenSearchParam",
            SearchParamType.Reference => "ReferenceSearchParam",
            SearchParamType.Uri => "UriSearchParam",
            SearchParamType.Number => "NumberSearchParam",
            SearchParamType.Quantity => "QuantitySearchParam",
            SearchParamType.Date => "DateTimeSearchParam",
            _ => throw new NotSupportedException(
                $":missing is not supported for search parameter type '{parameter.Type}' on '{parameter.Code}' -- " +
                "composite types are handled separately (see ResolveMissingCompositeTable); Special is out of scope."),
        };

        return SqlCatalog.Default.Table(tableName);
    }
```

(`ResolveMissingCompositeTable`, referenced in the throw message, is added by Task 4 — this task's `ResolveMissingTable` throws for `SearchParamType.Composite` until Task 4 lands, which is correct and expected: running this task's own tests against a composite parameter should fail loudly, not silently succeed, until Task 4 explicitly wires that case in.)

Add `using Ignixa.Search.Models;` and `using Ignixa.Search.Sql.Catalog;` to `StructuralContext.cs` if not already present — check the file's current usings first.

- [ ] **Step 3: Add the `MissingSearchParameterExpression` case to `Lower.cs`'s `LowerNode`**

Find `LowerNode`'s switch expression (the one containing arms for `SearchParameterPredicateExpression`, `SearchParameterExpression`, `MultiaryExpression`, `ChainedExpression`, `CompartmentSearchExpression`). Add a new arm — place it directly before the `SearchParameterExpression sp => LowerSearchParameter(sp, context, resourceType)` arm (both are `SearchParameterExpressionBase`-derived types dispatched at this same tier, so grouping them together matches the file's existing organization):

```csharp
        MissingSearchParameterExpression missing => LowerMissing(missing, context, resourceType),
```

Add the new private method `LowerMissing` (after `LowerSearchParameter`, before `TryGetCompositeComponents`):

```csharp
    private static CteRef LowerMissing(MissingSearchParameterExpression missing, StructuralContext context, string resourceType)
    {
        var presence = context.LowerParameterPresence(missing.Parameter, resourceType);
        return missing.IsMissing ? context.LowerNot(presence, resourceType) : presence;
    }
```

Add `using Ignixa.Search.Expressions;` if `MissingSearchParameterExpression` isn't already resolvable — check `Lower.cs`'s current usings (it almost certainly already has this, since `SearchParameterExpression`/`ChainedExpression`/`CompartmentSearchExpression` are in the same namespace).

- [ ] **Step 4: Write the new tests**

Add to `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`:

```csharp
    [Fact]
    public void GivenAMissingFalseOnAStringParameter_WhenLowered_ThenThePlanIsAParamSourceWithNoPredicate()
    {
        // Arrange -- Patient?name:missing=false ("name is present").
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Explain().ShouldBe("root = StringSearchParam[103,202]");
    }

    [Fact]
    public void GivenAMissingTrueOnAStringParameter_WhenLowered_ThenThePlanIsAnExceptOfResourceSourceAndParamSource()
    {
        // Arrange -- Patient?name:missing=true ("name is absent") -- reuses :not's Except/ResourceSource shape.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var missing = new MissingSearchParameterExpression(nameParam, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Except>();
        var except = (CteDefinition.Except)plan.Ctes[plan.Match.Index];
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();
        ((CteDefinition.ParamSource)plan.Ctes[except.Right.Index]).Predicate.ShouldBeNull();
    }

    [Theory]
    [InlineData("_id")]
    [InlineData("_type")]
    [InlineData("_lastUpdated")]
    public void GivenMissingOnAResourceColumnParameter_WhenLowered_ThenThrowsNotSupportedException(string code)
    {
        // Arrange -- _id/_type/_lastUpdated:missing=true is nonsensical (these are never absent) and
        // must throw loudly, not silently compile a query against the wrong table.
        var param = new SearchParameterInfo(code, code, SearchParamType.String, new Uri($"http://hl7.org/fhir/SearchParameter/Resource-{code}"));
        var missing = new MissingSearchParameterExpression(param, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null));
    }

    [Fact]
    public void GivenMissingOnAnUnsupportedParameterType_WhenLowered_ThenThrowsNotSupportedExceptionCitingTheType()
    {
        // Arrange -- Special is not a leaf type this compiler handles at all.
        var param = new SearchParameterInfo("composition", "composition", SearchParamType.Special, new Uri("http://hl7.org/fhir/SearchParameter/special-composition"));
        var missing = new MissingSearchParameterExpression(param, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("Special");
    }
```

(The four resource-column-code test cases use `SearchParamType.String` deliberately, not a resource-column-specific type — the point is that `RejectResourceColumnCode` fires on the parameter's `Code` string alone, matching its exact existing implementation, which checks `parameterCode is "_id" or "_type" or "_lastUpdated"` regardless of `Type`.)

- [ ] **Step 5: Run the tests**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj` — expect 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` — expect all previous tests plus these 6 new ones (2 `[Fact]` + 1 `[Theory]` with 3 cases + 1 `[Fact]`), all passing.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs
git commit -m "feat(search-sql): lower :missing for leaf search parameters via LowerParameterPresence"
```

---

### Task 4: `:missing` for composite types — static component-type-sequence table resolution

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`

**Interfaces:**
- Consumes: `StructuralContext.LowerParameterPresence`/`ResolveMissingTable` (Task 3).
- Produces: `ResolveMissingCompositeTable` extends `ResolveMissingTable`'s dispatch. Task 5's combined-proof tests are the primary consumer.

- [ ] **Step 1: Verify `SearchParameterComponentInfo.ResolvedSearchParameter` is actually populated for a real composite parameter — do not skip this**

Before writing any production code, write and run a small standalone check (a throwaway test in `LowerTests.cs`, or a scratch console check — your choice, but it must be a REAL check against REAL composite `SearchParameterInfo` construction, not a read of this plan's own claim) confirming that `SearchParameterComponentInfo.ResolvedSearchParameter` is non-null and has the correct `.Type` for each component of a real composite search parameter as constructed by this codebase's existing test helpers or definition-loading path (`SearchParameterDefinitionBuilder.cs`/`SearchParameterDefinitionManager.cs`, per this plan's Global Constraints — read those two files' `ResolvedSearchParameter = ...` assignment sites to confirm exactly when/how population happens, and to find or build a `SearchParameterInfo` construction path for your test that matches how the rest of `Ignixa.Search.Sql.Tests` already constructs composite `SearchParameterInfo`s for its existing composite lowering rule tests — e.g. `test/Ignixa.Search.Sql.Tests/Lowering/TokenQuantityLoweringRuleTests.cs` almost certainly already builds one; match its pattern, and confirm whether it already sets `ResolvedSearchParameter` on each component or leaves you needing to set it yourself in this task's own tests).

**If `ResolvedSearchParameter` is NOT reliably populated** by whatever construction path this codebase's own tests already use for composite `SearchParameterInfo`s, STOP and report `BLOCKED` rather than inventing a workaround — this would mean the plan's premise about static component-type resolution needs a different mechanism than assumed, and that is a design question for the controlling session, not something to route around silently.

- [ ] **Step 2: Extend `ResolveMissingTable` for `SearchParamType.Composite`**

Assuming Step 1 confirms `ResolvedSearchParameter` is reliably populated, change `StructuralContext.cs`'s `ResolveMissingTable` (from Task 3):

```csharp
    private static TableDescriptor ResolveMissingTable(SearchParameterInfo parameter)
    {
        if (parameter.Type == SearchParamType.Composite)
        {
            return ResolveMissingCompositeTable(parameter);
        }

        var tableName = parameter.Type switch
        {
            SearchParamType.String => "StringSearchParam",
            SearchParamType.Token => "TokenSearchParam",
            SearchParamType.Reference => "ReferenceSearchParam",
            SearchParamType.Uri => "UriSearchParam",
            SearchParamType.Number => "NumberSearchParam",
            SearchParamType.Quantity => "QuantitySearchParam",
            SearchParamType.Date => "DateTimeSearchParam",
            _ => throw new NotSupportedException(
                $":missing is not supported for search parameter type '{parameter.Type}' on '{parameter.Code}'."),
        };

        return SqlCatalog.Default.Table(tableName);
    }

    private static TableDescriptor ResolveMissingCompositeTable(SearchParameterInfo parameter)
    {
        var componentTypes = parameter.Component.Select(c => c.ResolvedSearchParameter.Type).ToArray();

        var tableName = componentTypes switch
        {
            [SearchParamType.Token, SearchParamType.Token] => "TokenTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Number, SearchParamType.Number] => "TokenNumberNumberCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.String] => "TokenStringCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Quantity] => "TokenQuantityCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Date] => "TokenDateTimeCompositeSearchParam",
            [SearchParamType.Reference, SearchParamType.Token] => "ReferenceTokenCompositeSearchParam",
            [SearchParamType.Token, SearchParamType.Reference] => "ReferenceTokenCompositeSearchParam",
            var types => throw new NotSupportedException(
                $":missing is not supported for composite search parameter '{parameter.Code}' with component types " +
                $"[{string.Join(", ", types)}] -- no matching composite table."),
        };

        return SqlCatalog.Default.Table(tableName);
    }
```

Remove the placeholder comment in Task 3's throw message referencing `ResolveMissingCompositeTable` as "added by Task 4" — it now exists.

- [ ] **Step 3: Write the new tests**

Add to `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs` — match whatever composite `SearchParameterInfo` construction pattern Step 1 found already in use elsewhere in this test file (e.g. in a `TokenQuantityLoweringRuleTests.cs`-adjacent helper), so `ResolvedSearchParameter` is populated the same way:

```csharp
    [Fact]
    public void GivenMissingFalseOnATokenQuantityCompositeParameter_WhenLowered_ThenThePlanIsAParamSourceAgainstTheCompositeTable()
    {
        // Arrange -- Observation?component-code-value-quantity:missing=false.
        var tokenComponent = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));
        var quantityComponent = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/clinical-value-quantity"));
        var composite = new SearchParameterInfo(
            "component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"),
            components: new[]
            {
                new SearchParameterComponentInfo(tokenComponent.Url, "code") { ResolvedSearchParameter = tokenComponent },
                new SearchParameterComponentInfo(quantityComponent.Url, "value.as(Quantity)") { ResolvedSearchParameter = quantityComponent },
            });
        var missing = new MissingSearchParameterExpression(composite, isMissing: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [composite.Url.ToString()] = 909 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        // Act
        var plan = Lower.Run(
            missing, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Explain().ShouldBe("root = TokenQuantityCompositeSearchParam[104,909]");
    }

    [Fact]
    public void GivenMissingOnACompositeWithNoMatchingTable_WhenLowered_ThenThrowsNotSupportedExceptionCitingTheComponentTypes()
    {
        // Arrange -- a synthetic, unsupported composite shape (Number+Number, no such table exists).
        var numberComponent1 = new SearchParameterInfo("a", "a", SearchParamType.Number, new Uri("http://example.org/a"));
        var numberComponent2 = new SearchParameterInfo("b", "b", SearchParamType.Number, new Uri("http://example.org/b"));
        var composite = new SearchParameterInfo(
            "unsupported-composite", "unsupported-composite", SearchParamType.Composite,
            new Uri("http://example.org/unsupported-composite"),
            components: new[]
            {
                new SearchParameterComponentInfo(numberComponent1.Url, "a") { ResolvedSearchParameter = numberComponent1 },
                new SearchParameterComponentInfo(numberComponent2.Url, "b") { ResolvedSearchParameter = numberComponent2 },
            });
        var missing = new MissingSearchParameterExpression(composite, isMissing: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                missing, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("Number");
    }
```

**If Step 1's construction-pattern verification found a different constructor shape than shown above** (e.g. `SearchParameterInfo`'s real constructor takes `components` differently, or `SearchParameterComponentInfo`'s constructor signature differs from what this plan assumed reading it in isolation), adjust these two tests to match the REAL shape — the behavior these tests assert (a composite `:missing=false` compiles to a bare `ParamSource` against the correct composite table; an unsupported component-type combination throws citing the types) is what matters, not byte-for-byte transcription of construction syntax that may have drifted.

- [ ] **Step 4: Run the tests**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj` — expect 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` — expect all previous tests plus these 2 new ones, all passing.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs
git commit -m "feat(search-sql): lower :missing for composite search parameters via static component-type resolution"
```

---

### Task 5: Combined proof — `CountOnly`/`:missing` compose with sort, includes, chain, compartment

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: nothing new — pure proof, matching every prior phase's "combined proof" task pattern. Proves the design doc's specific composability claims (§2 "CountOnly composes for free with sort/includes/chain/compartment"; §4 "chain-nested :missing composes for free, by construction") via real composed scenarios, not just unit-testing each piece in isolation.

- [ ] **Step 1: Read the file's existing test pattern**

Open `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, confirm its `FakeSymbolResolver`/`FakeCompartmentDefinitionManager`/`FakeSearchParameterDefinitionManager` private nested classes (reuse them, do not redeclare), and find its most recent test for structural reference.

- [ ] **Step 2: `CountOnly` + compartment search**

```csharp
    [Fact]
    public async Task GivenACompartmentSearchWithCountOnly_WhenCompiledEndToEnd_ThenTheCountQueryReusesTheCompartmentUnionRoot()
    {
        // Arrange -- GET /Patient/123/Observation?_total=accurate -- proves CountOnly composes with the
        // Union-rooted compartment match graph, including the DISTINCT that matters for a Union root.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Observation",
            CancellationToken.None, compartmentManager, searchParamManager);
        var plan = Lower.Run(
            compartment, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null, countOnly: true);

        // Assert
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("SELECT COUNT_BIG(DISTINCT m.Sid1)");
        emitted.Sql.ShouldNotContain("TOP (");
        emitted.Sql.ShouldNotContain("ORDER BY");
    }
```

- [ ] **Step 3: `:missing` nested inside a chain's target expression**

```csharp
    [Fact]
    public async Task GivenAChainWithMissingInsideTheTargetExpression_WhenCompiledEndToEnd_ThenTheMissingBranchIsReachableAtChainNestingDepth()
    {
        // Arrange -- Patient?organization.name:missing=true -- the referenced Organization has no name.
        var orgRefParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var missingName = new MissingSearchParameterExpression(nameParam, isMissing: true);
        var chain = new ChainedExpression(["Patient"], orgRefParam, "Patient", ["Organization"], missingName, reversed: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgRefParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = await Resolve.RunAsync(
            chain, includes: [], revIncludes: [], sort: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(
            chain, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        // Assert -- structural, not SQL-text, assertions (matching Task 3's own :missing=true test style):
        // the chain's InnerMatch CteRef must point at the Except/ResourceSource/ParamSource shape
        // LowerMissing produces standalone, proving it is reachable at chain-nesting depth with zero
        // new chain-specific wiring. The plan's match root itself is the ChainJoin.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.ChainJoin>();
        var chainJoin = (CteDefinition.ChainJoin)plan.Ctes[plan.Match.Index];
        plan.Ctes[chainJoin.InnerMatch.Index].ShouldBeOfType<CteDefinition.Except>();
        var except = (CteDefinition.Except)plan.Ctes[chainJoin.InnerMatch.Index];
        plan.Ctes[except.Left.Index].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Ctes[except.Right.Index].ShouldBeOfType<CteDefinition.ParamSource>();
        ((CteDefinition.ParamSource)plan.Ctes[except.Right.Index]).Predicate.ShouldBeNull();

        // Also confirm the whole plan still emits without error -- a real, if not exhaustively
        // asserted, proof that ChainJoin's Emit code and the Except/ParamSource-no-predicate shape
        // compose into valid SQL text end to end.
        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldNotBeNullOrWhiteSpace();
    }
```

**`CteDefinition.ChainJoin`'s exact field name for the inner match CteRef** (`InnerMatch`, used above) is asserted from this plan's own earlier read of `Emit.cs`'s `EmitChainJoin` method (`cj.InnerMatch.Index`, `Emit.cs:192,201`) — confirmed against the real file during this plan's ground-truth pass, not guessed. If `ChainedExpression`'s real constructor signature differs from what's shown above (this plan's earlier tasks did not touch chain code, so `ChainedExpression`'s exact constructor parameter list was not independently re-verified during this plan's ground-truth pass), adjust the Arrange section to match the real constructor — the structural assertions above are what must hold regardless of exact constructor shape.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` — expect all previous tests plus these 2 new ones, all passing on both net9.0/net10.0.

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "test(search-sql): prove CountOnly and :missing compose with compartment search and chain nesting"
```

---

### Task 6: Final regression + roadmap update + review prep

**Files:** none (verification only), plus a roadmap doc update.

**Interfaces:**
- Consumes: everything from Tasks 1-5.
- Produces: a clean `dotnet build All.sln` / `dotnet test All.sln` baseline and a review package for the final whole-branch review.

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln` — expect 0 warnings, 0 errors.

- [ ] **Step 2: Full solution test**

Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` — expect all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework).

- [ ] **Step 3: Confirm no scope creep**

Grep the diff since this plan's base commit for `TotalType`, `SummaryType`, `Ignixa.DataLayer`, `Ignixa.Api`, `Ignixa.Application` — confirm zero matches outside doc/comment text explicitly quoting the `_total=estimate` guidance message. This plan touches only `Ignixa.Search.Sql` and its own test project.

- [ ] **Step 4: Update the roadmap doc**

In `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`, add a new paragraph after the tenth increment's (Phase 8 part 2 sort) write-up, following that paragraph's narrative style/detail level: summarize what shipped (`_summary=count`/`_total=accurate` via `QueryPlan.CountOnly`, zero new `CteDefinition` nodes; `:missing` across all 7 leaf and 6 composite types, reusing the existing `:not`/`Except` machinery via `ParamSource.Predicate` becoming nullable rather than new AST surface; the ground-truth correction found during this plan's writing — `:missing`'s real expression shape is `MissingSearchParameterExpression`, not a modifier, dispatched at `LowerNode`'s top tier). Mark Phase 9 (compiler completeness) **Complete**, and note Phase 10 (DataLayer wiring, formerly numbered Phase 9) is next.

- [ ] **Step 5: Prepare the final whole-branch review package**

Follow `superpowers:subagent-driven-development`'s final-review step: run `scripts/review-package MERGE_BASE HEAD` and dispatch the final whole-branch reviewer on the most capable available model, per that skill's Model Selection section. Explicitly ask the reviewer to independently re-verify: (a) that `:missing=true`'s `Except`/`ResourceSource` shape is genuinely reused, not reimplemented in parallel; (b) that `CountOnly` really does ignore `Top`/`Sort`/`Page`/`Includes` in every code path, not just the ones this plan's own tests happened to construct; (c) the composite table-resolution switch's correctness against the real 6-table set, independently re-derived from `97.sql`, not trusted from this plan's own transcription.

- [ ] **Step 6: Report to the user before merging or pushing**

Summarize what shipped, confirm Phase 9 (compiler completeness) is done, and that Phase 10 (DataLayer wiring) is next in the roadmap. Ask before merging into `feature/fhir-to-sql-compiler` and again before pushing — matching every prior increment's established pattern on this branch.
