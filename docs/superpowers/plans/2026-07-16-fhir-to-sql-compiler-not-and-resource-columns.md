# :not (ResourceSource/Except) and Resource-Column Predicates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lower `:not` (via new `ResourceSource`/`Except` CTE kinds) and ordinary resource-column predicates (`_id`, `_type`, exact-instant `_lastUpdated`, via a new outer-WHERE mechanism in `QueryPlan`/`Emit`) to real, correct SQL. This closes out Phase 5's tier-1 leaf/structural coverage except `:ap` and the `ColumnDescriptor.Collation` ambiguity (both already-tracked, separately-deferred items).

**Architecture:** Two genuinely separate mechanisms, confirmed by direct source research, not assumed:

1. **`:not` needs `ResourceSource`+`Except` as CTE-graph nodes.** `NotExpression(Expression)` (`src/Core/Ignixa.Search/Expressions/NotExpression.cs`) is a plain unary wrapper; `SearchExpressionBinder.BindAlternatives` constructs it when it sees a `:not` modifier, wrapping `Expression.Or(itemPredicates)` (or a bare predicate for a single value), then the outer `Bind` wraps the whole thing in `SearchParameterExpression(param, NotExpression(...))` -- the same wrapping convention every other search parameter gets. `:not` on a composite already throws upstream in `BindComposite` (any non-null modifier throws `ModifierNotSupported`), so `NotExpression` never wraps a composite -- not a case `Lower` needs to handle. Semantically, `:not` is a set-subtraction: "every resource of this type MINUS every resource matching the negated condition" -- inherently a CTE-graph operation (`Except(ResourceSource(typeId), negatedMatchCte)`), not expressible as a column-level `WHERE` predicate, so this mechanism is required regardless of how ordinary resource-column predicates are handled.

   **Verified, not assumed: Ignixa's own existing (non-compiler) pipeline already implements `:not` correctly** -- `SearchExpressionQueryBuilder.ApplyNotExpressionAsync` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs:250-264`) does a genuine anti-join against the full resource table (`baseQuery.Where(r => !matchingResourceIds.Contains(r.ResourceSurrogateId))`), so a resource with zero rows in the negated parameter's table is correctly *included*. The FHIR-spec-violation an earlier design doc found (silently dropping zero-row resources) is real, but it's specifically a bug in **fhir-server**, whose behavior this roadmap has already decided not to adopt -- there is no real tension here: this plan implements Ignixa's own already-correct semantics.

2. **Ordinary resource-column predicates (`_id`/`_type`/`_lastUpdated`) use a new outer-`WHERE` mechanism, not `ResourceSource`.** Verified: these three bind through the exact same `SearchExpressionBinder`/`BindAtomic` pipeline as any other search parameter -- no special node type, no special binder path (grepped `SearchExpressionBinder.cs`, zero special-casing). `_id`/`_type` are `SearchParamType.Token` (produce `TokenSearchValue`); `_lastUpdated` is `SearchParamType.Date` (produces `DateTimeSearchValue`) -- identical `ISearchValue` shapes to any other parameter of those types. The only way to route them correctly is a `predicate.Parameter.Code` check against the well-known constants (`_id`, `_type`, `_lastUpdated`) intercepting them *before* the generic `LeafLoweringDispatcher` dispatch, which would otherwise wrongly target `TokenSearchParam`/`DateTimeSearchParam` (`_id` isn't indexed there at all).

   **Decision, made explicitly rather than defaulted to fhir-server's approach:** these become `QueryPlan.OuterPredicate` (a single `Predicate?` applied via an outer join to `dbo.Resource` in the final `SELECT`), not `ResourceSource(typeId, predicate)` CTEs. Reasoning: `Emit` today only knows how to emit CTEs plus a final `SELECT T1, Sid1 FROM cte{Match}` -- the outer-predicate approach needs exactly one new, uniform mechanism (a join clause on the final `SELECT`), whereas folding these into the CTE graph would need zero new `Emit` machinery but relies on SQL Server successfully pushing a highly-selective predicate through multiple `Intersect` layers under `TOP` -- a real, known SQL Server risk (CTEs can be materialized rather than inlined once `TOP` or certain join shapes are involved), not a hypothetical one. Every `CteDefinition` entry (`ParamSource`, `Intersect`, `Union`, and this plan's new `ResourceSource`/`Except`) already emits `(T1, Sid1)` = `(ResourceTypeId, ResourceSurrogateId)` -- confirmed by reading current `Emit.cs` -- so the outer join to `dbo.Resource` needs no CTE-shape changes at all: `INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1`.

   **`_lastUpdated` is narrowed to exact-instant matches only (`value.Start == value.End`) for this increment.** The real, live `_lastUpdated` SQL generator (`SearchParameterQueryGenerator.ProcessResourceLastUpdatedExpressionAsync`, read directly) only ever compares against one already-resolved instant (`ResourceSurrogateId == targetId`, etc.) -- it never handles FHIR's partial-date-precision ranges (`_lastUpdated=2023` meaning "sometime in 2023"), because by the time a value reaches that method it has already been flattened to a single `DateTimeOffset` by an earlier, unrelated pipeline stage. Handling partial precision correctly in this compiler's typed IR (which keeps the full `DateTimeSearchValue.Start`/`.End` range, unflattened, all the way to `Lower`) would need a *point-column-vs-search-range* comparator formula with no live reference implementation to verify it against -- a real correctness risk, deliberately deferred. Partial-precision `_lastUpdated` throws `NotSupportedException`.

   The timestamp-encoding formula itself (`ResourceSurrogateId = MillisecondTruncate(dateTimeOffset.UtcTicks) << 3`, confirmed against `Ignixa.Domain.Abstractions.IdHelper.ToId()`) is duplicated into `Ignixa.Search.Sql` rather than referenced -- `IdHelper` lives in `Ignixa.Domain`, an Application-tier project, and Core (where `Ignixa.Search.Sql` lives) cannot depend on Application per this repo's strict layer rule. The formula is 2 lines of pure `DateTimeOffset`/bit-shift math with zero external dependencies, matching the same "small, stable domain logic, transcribed once" precedent already used for `NumericRangeComparison`/`DateTimeRangeComparison`/`TokenColumnEquality`.

**Tech Stack:** C#/.NET 9, xUnit + Shouldly, existing `Ignixa.Search.Sql`/`Ignixa.Search.Sql.Tests`/`Ignixa.Search.Sql.Generators` projects.

## Global Constraints

- `NotExpression` never wraps a composite (binder-enforced upstream) -- no composite-`:not` interaction to handle.
- `_id`/`_type` throw for a `System`-qualified value or empty `Code`, matching every other token-shaped rule's precedent in this compiler.
- `_lastUpdated` throws `NotSupportedException` for a non-instant (`value.Start != value.End`) search value, and for `:ap` (needs `DateTimeOffset.UtcNow` at lowering time, same reasoning as every other `:ap` gap in this compiler).
- Resource-column predicates (`_id`/`_type`/`_lastUpdated`) are only extracted from the **top level of a conjunction** (a bare leaf, or a top-level `MultiaryExpression{And}`'s direct children) -- nested inside an `Or`, inside a `:not`, or inside a composite is out of scope for this plan. **Correction, found during Task 7's review (this premise was originally wrong):** the generic dispatcher does NOT automatically throw for a fallen-through `_id`/`_type` leaf on its own -- `_id`/`_type` carry an ordinary `TokenSearchValue`, so without an explicit guard the dispatcher would silently route them into `TokenSearchParam` (a real table, just the wrong one), either matching zero rows (silently dropping the `:not` filter once negated by `Except`) or throwing an unrelated `KeyNotFoundException` if unresolved -- neither is the clean, intentional failure this bullet originally claimed. `Lower.LowerNode` initially carried an explicit guard for this (`SearchParameterPredicateExpression { Parameter.Code: "_id" or "_type" or "_lastUpdated" } => throw ...`), but a follow-up adversarial re-review found that guard was positional, not structural: 3 of the 4 real dispatch sites into `LeafLoweringDispatcher`/`CompositeLoweringDispatcher` bypass `LowerNode` entirely. The guard now lives at the actual choke points, `StructuralContext.Lower` and `StructuralContext.LowerComposite`, which every dispatch path funnels through regardless of how it got there; `LowerNode`'s duplicate copy was removed. A comma-separated `_id:not=1,2` alternative is the real-world case that originally surfaced this gap; still out of scope to *support*, but now confirmed to fail loudly, not silently, structurally rather than incidentally.
- `ResourceSource` has no `Predicate` parameter in this plan -- ordinary resource-column filtering goes through the separate outer-`WHERE` mechanism, not through `ResourceSource`'s own predicate (unlike the original design doc's sketch, which combined both into one node -- this plan's `ResourceSource` is used only as `:not`'s base set and the "resource-column-only query" fallback, always unfiltered).
- `Lower.Run`/`Resolve.RunAsync` both gain a new, **optional, trailing** parameter (`string? targetResourceType = null`) -- every existing call site in this codebase's tests is a positional call with 2-3 args, so this is purely additive; no existing test file needs updating.
- No `SqlCatalog`/generator changes beyond adding `"Resource"` to the existing table-name filter (one line) -- `dbo.Resource`'s column list is picked up automatically by the existing DDL parser.
- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing (the `Ignixa.SqlOnFhir.Tests` submodule failures are pre-existing and out of scope, per every prior increment on this branch).

---

### Task 1: `SqlCatalog` generator coverage for `dbo.Resource`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogTests.cs` (add one fact)

**Interfaces:**
- Consumes: `DdlTableParser.ParseTables` (existing, unchanged).
- Produces: `SqlCatalog.Default.Table("Resource")` resolves, with `ResourceTypeId`/`ResourceId`/`ResourceSurrogateId`/`IsHistory`/`IsDeleted` columns present (the real DDL, `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql:592-606`, has all of these plus `Version`/`RequestMethod`/`RawResource`/etc. -- this task only needs to confirm the generator picks the table up at all, not enumerate every column).

- [ ] **Step 1: Extend the generator's table-name filter**

In `src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs`, change:

```csharp
name => name.EndsWith("SearchParam", StringComparison.Ordinal) || name == "ResourceType");
```

to:

```csharp
name => name.EndsWith("SearchParam", StringComparison.Ordinal) || name == "ResourceType" || name == "Resource");
```

- [ ] **Step 2: Write the failing test**

Add to `test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogTests.cs` (match the file's existing fact style exactly -- read a couple of its existing facts first to confirm the pattern before writing this one):

```csharp
[Fact]
public void GivenTheResourceTable_WhenLookedUp_ThenHasResourceTypeIdAndResourceIdAndResourceSurrogateIdColumns()
{
    var table = SqlCatalog.Default.Table("Resource");

    table.TableName.ShouldBe("Resource");
    table.Column("ResourceTypeId").ShouldNotBeNull();
    table.Column("ResourceId").ShouldNotBeNull();
    table.Column("ResourceSurrogateId").ShouldNotBeNull();
    table.Column("IsHistory").ShouldNotBeNull();
    table.Column("IsDeleted").ShouldNotBeNull();
}
```

- [ ] **Step 3: Run to confirm it fails, then passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~SqlCatalogTests" --nologo
```

Expected first run: FAIL (`KeyNotFoundException`, "Resource" not in catalog). After Step 1's filter change, a full rebuild regenerates `SqlCatalog.g.cs` automatically (it's a source generator, not a checked-in file) -- re-run the same command; expected: 0 warnings, 0 errors, all facts pass including the new one.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add dbo.Resource to the SqlCatalog generator's table filter

Needed by both ResourceSource (Task 2) and the resource-column outer-WHERE
mechanism (Task 6) -- the DDL parser already handles this table's shape,
this is purely a one-line filter extension."
```

---

### Task 2: `CteDefinition.ResourceSource`/`Except` + `PlanExplainer` rendering

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `CteDefinition.ResourceSource(short ResourceTypeId)`, `CteDefinition.Except(CteRef Left, CteRef Right)`. `PlanExplainer` renders `ResourceSource[N]` and `Except(cteA, cteB)`.

- [ ] **Step 1: Add the two new `CteDefinition` cases**

Replace the entire contents of `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`:

```csharp
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph. ParamSource (a single search-param table filtered by
/// SearchParamId + Predicate), Intersect (AND), Union (OR), ResourceSource (all current, non-deleted
/// resources of a type -- :not's base set), Except (set subtraction -- :not's own operation).
/// ResourceSource has no Predicate: ordinary resource-column filtering (_id/_type/_lastUpdated) is a
/// separate mechanism, QueryPlan.OuterPredicate -- see that type's remarks. ChainJoin is NOT included
/// -- nothing in this plan's scope (chain) constructs it; add when that lowering rule is written.
/// </summary>
public abstract record CteDefinition
{
    public sealed record ParamSource(TableDescriptor Table, short SearchParamId, Predicate Predicate) : CteDefinition;

    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;

    public sealed record ResourceSource(short ResourceTypeId) : CteDefinition;

    public sealed record Except(CteRef Left, CteRef Right) : CteDefinition;
}
```

- [ ] **Step 2: Write the failing `PlanExplainer` tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs` (read its existing facts first to match the file's construction style -- it likely hand-builds a `QueryPlan` directly rather than going through `Lower`):

```csharp
[Fact]
public void GivenAResourceSourceCte_WhenExplained_ThenRendersResourceTypeId()
{
    var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

    plan.Explain().ShouldBe("root = ResourceSource[103]");
}

[Fact]
public void GivenAnExceptCte_WhenExplained_ThenRendersBothOperands()
{
    var plan = new QueryPlan(
    [
        new CteDefinition.ResourceSource(103),
        new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
        new CteDefinition.Except(new CteRef(0), new CteRef(1)),
    ],
    new CteRef(2));

    plan.Explain().ShouldBe(
        "cte0 = ResourceSource[103]\n" +
        "cte1 = StringSearchParam[202]  Text = @p0\n" +
        "root = Except(cte0, cte1)");
}
```

Verify `SqlColumnRef`/`SqlParameterRef`'s constructors against `src/Core/Ignixa.Search.Sql/Ast/SqlColumnRef.cs`/`SqlParameterRef.cs` before running -- both are used identically elsewhere in this test file already (check an existing fact's construction).

- [ ] **Step 3: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~PlanExplainerTests" --nologo
```

Expected: FAIL with `NotSupportedException: No Explain() rendering for ResourceSource` (or `Except`).

- [ ] **Step 4: Add the rendering cases**

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, add two arms to `PrintCte`'s switch (before the `_ =>` default):

```csharp
        CteDefinition.ResourceSource rs => $"ResourceSource[{rs.ResourceTypeId}]{PrintTop(top)}",
        CteDefinition.Except ex => $"Except(cte{ex.Left.Index}, cte{ex.Right.Index}){PrintTop(top)}",
```

- [ ] **Step 5: Run to confirm they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~PlanExplainerTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass (including every pre-existing fact in this file, unmodified).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add ResourceSource/Except CteDefinition cases + Explain() rendering

ResourceSource has no Predicate parameter -- ordinary resource-column
filtering is a separate mechanism (QueryPlan.OuterPredicate, task 6), not
folded into this node. These two cases are :not's own plumbing (base set
+ set subtraction), needed regardless of how resource-column predicates
are handled."
```

---

### Task 3: `Emit` support for `ResourceSource`/`Except`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

**Interfaces:**
- Consumes: `CteDefinition.ResourceSource`/`Except` (Task 2).
- Produces: `Emit.Run` no longer throws for these two CTE kinds.

- [ ] **Step 1: Write the failing tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs` (match its existing construction/assertion style -- read a couple of existing facts first):

```csharp
[Fact]
public void GivenAResourceSourceCte_WhenEmitted_ThenSelectsFromDboResourceFilteredByType()
{
    var plan = new QueryPlan([new CteDefinition.ResourceSource(103)], new CteRef(0));

    var emitted = Emit.Run(plan);

    emitted.Sql.ShouldContain("FROM dbo.Resource");
    emitted.Sql.ShouldContain("IsHistory = 0");
    emitted.Sql.ShouldContain("IsDeleted = 0");
    emitted.Parameters.ShouldContain(p => p.Value.Equals((short)103));
}

[Fact]
public void GivenAnExceptCte_WhenEmitted_ThenUsesNotExistsAntiJoin()
{
    var plan = new QueryPlan(
    [
        new CteDefinition.ResourceSource(103),
        new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith"))),
        new CteDefinition.Except(new CteRef(0), new CteRef(1)),
    ],
    new CteRef(2));

    var emitted = Emit.Run(plan);

    emitted.Sql.ShouldContain("NOT EXISTS");
    emitted.Sql.ShouldNotContain("Smith");
}
```

- [ ] **Step 2: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests" --nologo
```

Expected: FAIL with `NotSupportedException: No Emit for ResourceSource` (or `Except`).

- [ ] **Step 3: Implement**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, add two arms to `EmitCte`'s switch (before the `_ =>` default) and one new private method:

```csharp
        CteDefinition.ResourceSource rs => EmitResourceSource(rs, parameters),
        CteDefinition.Except ex =>
            $"    SELECT cte{ex.Left.Index}.T1, cte{ex.Left.Index}.Sid1\n" +
            $"    FROM cte{ex.Left.Index}\n" +
            $"    WHERE NOT EXISTS (\n" +
            $"        SELECT 1 FROM cte{ex.Right.Index}\n" +
            $"        WHERE cte{ex.Right.Index}.T1 = cte{ex.Left.Index}.T1 AND cte{ex.Right.Index}.Sid1 = cte{ex.Left.Index}.Sid1)",
```

```csharp
    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters)
        => $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.Resource\n" +
           $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)} AND IsHistory = 0 AND IsDeleted = 0";
```

Verify `SqlParameterRef`'s constructor accepts a single `object value` argument (matches `LeafContext.Parameter`'s existing `new(value)` usage) before writing `new SqlParameterRef(rs.ResourceTypeId)` -- `short` boxes to `object` without issue.

- [ ] **Step 4: Run to confirm they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): emit SQL for ResourceSource/Except CTEs

ResourceSource selects from dbo.Resource filtered to IsHistory=0 AND
IsDeleted=0 -- confirmed index-backed by IX_Resource_ResourceTypeId_ResourceSurrgateId
(97.sql), not a scan. Except uses a correlated NOT EXISTS anti-join,
matching the pattern SQL Server's optimizer reliably turns into an
anti-semi-join, same shape the existing (non-compiler) pipeline's
ApplyNotExpressionAsync already relies on."
```

---

### Task 4: `Resolve.RunAsync` gains an optional `targetResourceType`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Resolve.RunAsync(Expression, ISymbolResolver, CancellationToken, string? targetResourceType = null): Task<SymbolTable>`. When `targetResourceType` is provided, the returned `SymbolTable.ResourceTypeId(targetResourceType)` resolves, in addition to whatever the tree-walk already collects.

- [ ] **Step 1: Write the failing test**

Add to `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs` (uses the file's existing `FakeSymbolResolver`):

```csharp
[Fact]
public async Task GivenATargetResourceType_WhenResolved_ThenSymbolTableHasItsResourceTypeIdEvenWithNoReferenceInTheTree()
{
    // Arrange -- a plain String predicate, nothing in the tree itself mentions "Patient"
    var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
    var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
    var resolver = new FakeSymbolResolver();
    resolver.SearchParamIds[parameter.Url!.ToString()] = 202;
    resolver.ResourceTypeIds["Patient"] = 103;

    // Act
    var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None, targetResourceType: "Patient");

    // Assert
    symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
}
```

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResolveTests" --nologo
```

Expected: FAIL -- compile error (`Resolve.RunAsync` has no 4th parameter yet). This also proves the new parameter doesn't break the 3-existing-facts' 3-arg positional calls once added (they'll still compile, since the new parameter is optional and trailing).

- [ ] **Step 3: Add the parameter**

Replace `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`'s `RunAsync` method signature and body:

```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression expression,
        ISymbolResolver resolver,
        CancellationToken cancellationToken,
        string? targetResourceType = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolver);

        var collector = new SymbolCollectingVisitor();
        expression.AcceptVisitor(collector, context: null);

        var searchParamIds = new Dictionary<string, short>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);
            if (id.HasValue)
            {
                searchParamIds[parameter.Url.ToString()] = id.Value;
            }

            // A null result (unresolvable parameter) is not an error here -- Lower/Emit will throw
            // if something downstream actually needs it. Resolve's job is to look up what it can,
            // not to validate the tree is fully resolvable.
        }

        var resourceTypes = new HashSet<string>(collector.ResourceTypes);
        if (targetResourceType is not null)
        {
            resourceTypes.Add(targetResourceType);
        }

        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            if (id.HasValue)
            {
                resourceTypeIds[resourceType] = id.Value;
            }

            // Same non-error stance as the search-param loop above -- an unresolvable resource
            // type is simply absent from the table until something downstream needs it.
        }

        return new SymbolTable(searchParamIds, resourceTypeIds);
    }
```

Update the class's `<remarks>` doc comment: the existing note about resource-type resolution being "out of scope beyond one narrow exception" (`ReferenceSearchValue.ResourceType`) now has a second, explicit exception -- the caller-supplied `targetResourceType`, needed by `Lower`'s `ResourceSource`/outer-WHERE mechanisms (this plan) since the query's own target resource type does not otherwise appear anywhere in the `Expression` tree.

- [ ] **Step 4: Run to confirm all `ResolveTests` pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResolveTests" --nologo
```

Expected: 0 warnings, 0 errors, all facts pass -- including the pre-existing ones, unmodified, proving the new optional parameter is purely additive.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add optional targetResourceType to Resolve.RunAsync

The query's own target resource type (e.g. \"Patient\" for a Patient?...
search) does not appear anywhere in the bound Expression tree, so
Resolve's tree-walk alone can never discover it -- callers that need
ResourceSource (task 5's :not, task 6's resource-column predicates) must
supply it explicitly. Purely additive: every existing call site is an
unaffected 2-3-arg positional call."
```

---

### Task 5: `:not` -- `StructuralContext`/`Lower` wiring, proven end to end

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `CteDefinition.ResourceSource`/`Except` (Task 2), `Resolve.RunAsync`'s `targetResourceType` (Task 4).
- Produces: `StructuralContext(SymbolTable, string? targetResourceType = null)`, `StructuralContext.LowerResourceSource(): CteRef`, `StructuralContext.LowerNot(CteRef innerMatch): CteRef`. `Lower.LowerSearchParameter` handles `NotExpression`.

- [ ] **Step 1: Add `targetResourceType` threading and the two new methods to `StructuralContext`**

Replace the entire contents of `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The tier-2 (structural) context: builds the CTE graph by dispatching leaves to tier-1 rules and
/// combining their results. Owns the plan's Ctes list -- LeafContext (tier 1) never sees it.
/// </summary>
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;
    private readonly string? _targetResourceType;

    public StructuralContext(SymbolTable symbols, string? targetResourceType = null)
    {
        _leafContext = new LeafContext(symbols);
        _targetResourceType = targetResourceType;
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public CteRef Lower(SearchParameterPredicateExpression predicate)
    {
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components)
    {
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef Intersect(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Intersect(left, right));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef Union(IReadOnlyList<CteRef> parts)
    {
        _ctes.Add(new CteDefinition.Union(parts));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerResourceSource()
    {
        var resourceTypeId = ResolveTargetResourceTypeId();
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch)
    {
        var baseRef = LowerResourceSource();
        _ctes.Add(new CteDefinition.Except(baseRef, innerMatch));
        return new CteRef(_ctes.Count - 1);
    }

    private short ResolveTargetResourceTypeId()
        => _targetResourceType is not null
            ? _leafContext.ResourceTypeId(_targetResourceType)
            : throw new NotSupportedException(
                "This query needs a target resource type (:not, or a resource-column-only match) but " +
                "Lower.Run was not given one -- pass targetResourceType.");
}
```

- [ ] **Step 2: Write the failing E2E tests**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (uses the file's existing `FakeSymbolResolver`; add `using Ignixa.Search.Expressions;`'s `NotExpression` if not already covered by the file's existing `Ignixa.Search.Expressions` using):

```csharp
    [Fact]
    public async Task GivenAPatientNameNotQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Patient?name:not=Smith
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new SearchParameterExpression(
            nameParam,
            new NotExpression(new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"))));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None, targetResourceType: "Patient");
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[103]\n" +
            "cte1 = StringSearchParam[202]  Text LIKE @p0 (StartsWith) collate CI_AI\n" +
            "root = Except(cte0, cte1)");
        emitted.Sql.ShouldContain("NOT EXISTS");
        emitted.Sql.ShouldNotContain("Smith");
    }

    [Fact]
    public async Task GivenAPatientActiveAndNameNotQuery_WhenCompiled_ThenIntersectsTheExceptResult()
    {
        // Arrange -- Patient?active=true&name:not=Smith
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
            new SearchParameterExpression(
                nameParam,
                new NotExpression(new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith")))),
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None, targetResourceType: "Patient");
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- one CTE for `active`, three for the :not (ResourceSource, StringSearchParam, Except), then an outer Intersect
        plan.Explain().ShouldBe(
            "cte0 = TokenSearchParam[44]  Code = @p0\n" +
            "cte1 = ResourceSource[103]\n" +
            "cte2 = StringSearchParam[202]  Text LIKE @p1 (StartsWith) collate CI_AI\n" +
            "cte3 = Except(cte1, cte2)\n" +
            "root = Intersect(cte0, cte3)");
        emitted.Sql.ShouldNotContain("Smith");
        emitted.Sql.ShouldNotContain("true");
    }
```

Verify the golden `Explain()` strings' exact predicate rendering (`Text LIKE @p0 (StartsWith) collate CI_AI` for a default/no-modifier `StringSearchValue`, per `PlanExplainer.cs`'s `Predicate.Like` case: `"{Column} LIKE @pN ({Match}){collation}"`) against `PlanExplainer.cs`'s real source and `StringLoweringRule`'s default-case collation constant before running -- if it disagrees with what a first run actually prints, trust the real output and correct the assertion.

- [ ] **Step 3: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

Expected: FAIL -- either a compile error (`Lower.Run`/`StructuralContext` don't have the new members yet) or, once Step 1 is in place alone, `NotSupportedException: Lower does not support NotExpression yet`. All pre-existing E2E tests must still pass.

- [ ] **Step 4: Add the `NotExpression` case to `Lower.LowerSearchParameter`**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, add the `targetResourceType` parameter to `Run` and one new branch at the top of `LowerSearchParameter`:

```csharp
    public static QueryPlan Run(Expression expression, SymbolTable symbols, int? top = null, string? targetResourceType = null)
    {
        var context = new StructuralContext(symbols, targetResourceType);
        var match = LowerNode(expression, context);
        return new QueryPlan(context.Ctes, match, top);
    }
```

```csharp
    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context)
    {
        if (sp.Expression is NotExpression not)
        {
            return context.LowerNot(LowerNode(not.Expression, context));
        }

        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!);
        }
        // ... rest of the method unchanged from here ...
```

(Task 6 will change `Run`'s body again and add a new `OuterPredicate` argument to the `QueryPlan` construction -- this step's version is an intermediate state, correct and fully tested on its own.)

- [ ] **Step 5: Run to confirm they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass -- including every pre-existing E2E test (base leaves, composites, comma-separated alternatives).

- [ ] **Step 6: Run the full `Ignixa.Search.Sql.Tests` fixture**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```

Expected: 0 warnings, 0 errors, zero regressions.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): lower :not via ResourceSource+Except, prove end to end

NotExpression's inner expression is lowered exactly like any other tree
(bare predicate or Or-of-alternatives, both already handled by the
existing LowerNode dispatch), then wrapped in Except(ResourceSource,
innerMatch) -- :not composes with other predicates via the existing
Intersect/Union machinery unchanged, since it produces an ordinary CteRef
like everything else."
```

---

### Task 6: `QueryPlan.OuterPredicate` + outer-`WHERE` `Emit`/`PlanExplainer` support

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`, `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`, `test/Ignixa.Search.Sql.Tests/Ast/QueryPlanTests.cs` (if it exists -- check first)

**Interfaces:**
- Consumes: nothing new (reuses existing `Predicate` cases).
- Produces: `QueryPlan(IReadOnlyList<CteDefinition>, CteRef, int? Top = null, Predicate? OuterPredicate = null)`. `Emit.Run` joins to `dbo.Resource` and applies the outer `WHERE` when `OuterPredicate` is non-null. `PlanExplainer` appends ` WHERE {predicate}` to the root line.

- [ ] **Step 1: Add `OuterPredicate` to `QueryPlan`**

Replace `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`:

```csharp
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output -- Lower produces this, Emit consumes it. Every entry in Ctes,
/// including Intersect/Union/ResourceSource/Except nodes, becomes its own named CTE when emitted --
/// that is what makes this a graph rather than a tree of inline joins, and lets Match point at any
/// depth of nesting. OuterPredicate is the one exception to "everything is a CTE": ordinary
/// resource-column predicates (_id/_type/_lastUpdated) are applied as a WHERE clause on an outer join
/// to dbo.Resource, not folded into the CTE graph -- see task 6's plan section for why (avoids relying
/// on SQL Server pushing a predicate through multiple CTE layers under TOP, a real, not hypothetical,
/// risk). IncludeStage/SortSpec/full PageSpec (tier-3 result-shape stages) are not included yet --
/// nothing in scope here produces or consumes them.
/// </summary>
public sealed record QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null, Predicate? OuterPredicate = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
```

- [ ] **Step 2: Write the failing tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
[Fact]
public void GivenAnOuterPredicate_WhenEmitted_ThenJoinsToDboResourceAndAppliesTheWhereClause()
{
    var plan = new QueryPlan(
        [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
        new CteRef(0),
        OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")));

    var emitted = Emit.Run(plan);

    emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
    emitted.Sql.ShouldContain("ResourceId =");
    emitted.Sql.ShouldNotContain("123");
    emitted.Parameters.ShouldContain(p => p.Value.Equals("123"));
}

[Fact]
public void GivenNoOuterPredicate_WhenEmitted_ThenNoJoinToDboResourceAppears()
{
    var plan = new QueryPlan(
        [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
        new CteRef(0));

    var emitted = Emit.Run(plan);

    emitted.Sql.ShouldNotContain("dbo.Resource");
}
```

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`:

```csharp
[Fact]
public void GivenAnOuterPredicate_WhenExplained_ThenAppendsWhereToTheRootLine()
{
    var plan = new QueryPlan(
        [new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Smith")))],
        new CteRef(0),
        OuterPredicate: new Predicate.Equal(new SqlColumnRef("Resource", "ResourceId"), new SqlParameterRef("123")));

    plan.Explain().ShouldBe("root = StringSearchParam[202]  Text = @p0 WHERE ResourceId = @p1");
}
```

- [ ] **Step 3: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests|FullyQualifiedName~PlanExplainerTests" --nologo
```

Expected: FAIL -- compile error (`QueryPlan`'s constructor doesn't have `OuterPredicate` until Step 1... which is already done above; if Step 1 already landed, expected is instead: the new tests fail because `Emit`/`PlanExplainer` don't apply `OuterPredicate` yet -- `emitted.Sql.ShouldContain("INNER JOIN dbo.Resource")` fails, `plan.Explain()` doesn't include the `WHERE` suffix).

- [ ] **Step 4: Implement `Emit`'s outer-`WHERE` join**

Replace `Emit.Run`'s body in `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`:

```csharp
    public static EmittedSql Run(QueryPlan plan)
    {
        var parameters = new List<EmittedSqlParameter>();
        var cteBlocks = new List<string>();

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"cte{i} AS (\n{EmitCte(plan.Ctes[i], parameters)}\n)");
        }

        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;
        var withClause = $";WITH {string.Join(",\n", cteBlocks)}\n";
        var sql = plan.OuterPredicate is null
            ? withClause + $"SELECT {top}T1, Sid1 FROM cte{plan.Match.Index}"
            : withClause +
              $"SELECT {top}m.T1, m.Sid1 FROM cte{plan.Match.Index} m\n" +
              $"INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1\n" +
              $"WHERE {EmitPredicate(plan.OuterPredicate, parameters)}";

        return new EmittedSql(sql, parameters);
    }
```

- [ ] **Step 5: Implement `PlanExplainer`'s outer-`WHERE` suffix**

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, replace `Print`:

```csharp
    public static string Print(QueryPlan plan)
    {
        var lines = new List<string>();
        var parameterOrdinal = 0;

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            var isRoot = i == plan.Match.Index;
            var label = isRoot ? "root" : $"cte{i}";
            var top = isRoot ? plan.Top : null;
            var line = $"{label} = {PrintCte(plan.Ctes[i], top, ref parameterOrdinal)}";
            if (isRoot && plan.OuterPredicate is not null)
            {
                line += $" WHERE {PrintPredicate(plan.OuterPredicate, ref parameterOrdinal)}";
            }

            lines.Add(line);
        }

        return string.Join('\n', lines);
    }
```

- [ ] **Step 6: Run to confirm all three files' tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests|FullyQualifiedName~PlanExplainerTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass -- including every pre-existing fact in both files, unmodified.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add QueryPlan.OuterPredicate + outer-WHERE join to dbo.Resource

Every CteDefinition already emits (T1, Sid1) = (ResourceTypeId,
ResourceSurrogateId), so the join needs no CTE-shape changes -- just an
INNER JOIN on those two columns plus a WHERE against dbo.Resource's own
columns (ResourceId/ResourceTypeId), applied once on the outer SELECT."
```

---

### Task 7: `ResourceColumnLoweringRule` for `_id`/`_type` + `Lower.Run`'s extraction pass

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `SqlCatalog.Default.Table("Resource")` (Task 1), `QueryPlan.OuterPredicate`/`Emit`'s join (Task 6), `Lower.Run`'s `targetResourceType` (Task 5).
- Produces: `ResourceColumnLoweringRule.TryLower(SearchParameterPredicateExpression, LeafContext): Predicate?` -- returns `null` for any parameter whose `Code` isn't `_id`/`_type`/`_lastUpdated` (this task wires `_id`/`_type`; Task 8 adds the `_lastUpdated` arm to the same switch). `Lower.Run` extracts resource-column predicates from the top level of a conjunction into `QueryPlan.OuterPredicate` before the normal `LowerNode` recursion runs.

- [ ] **Step 1: Write the failing rule tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class ResourceColumnLoweringRuleTests
{
    private static LeafContext ContextResolving(string resourceType, short resourceTypeId)
        => new(new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { [resourceType] = resourceTypeId }));

    private static SearchParameterInfo IdParameter()
        => new("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));

    private static SearchParameterInfo TypeParameter()
        => new("_type", "_type", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));

    [Fact]
    public void GivenAnOrdinaryTokenParameter_WhenTried_ThenReturnsNull()
    {
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)).ShouldBeNull();
    }

    [Fact]
    public void GivenAnIdParameter_WhenTried_ThenComparesResourceId()
    {
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null));

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var equal = result.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceId");
        equal.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenASystemQualifiedIdParameter_WhenTried_ThenThrows()
    {
        var predicate = new SearchParameterPredicateExpression(IdParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org", code: "123", text: null));

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }

    [Fact]
    public void GivenATypeParameter_WhenTried_ThenComparesResourceTypeIdViaTheResolver()
    {
        var predicate = new SearchParameterPredicateExpression(TypeParameter(), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "Patient", text: null));

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var equal = result.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("ResourceTypeId");
        equal.Value.Value.ShouldBe((short)103);
    }
}
```

Verify `TokenSearchValue`'s constructor parameter order (`system`, `code`, `text`) against `TokenLoweringRuleTests.cs` before running.

- [ ] **Step 2: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResourceColumnLoweringRuleTests" --nologo
```

Expected: FAIL with "ResourceColumnLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `ResourceColumnLoweringRule` (the `_id`/`_type` arms)**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers _id/_type/_lastUpdated -- ordinary resource-column search parameters that bind through the
/// same SearchExpressionBinder/BindAtomic pipeline as any other parameter (no special node type), but
/// target dbo.Resource's own columns via QueryPlan.OuterPredicate, not a ParamSource table. Returns
/// null for any other parameter code -- the caller (Lower.Run's extraction pass) treats null as "not a
/// resource-column predicate, dispatch it normally." _lastUpdated's arm is added in a later increment
/// task; this file starts with _id/_type only.
/// </summary>
public static class ResourceColumnLoweringRule
{
    public static Predicate? TryLower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Parameter.Code switch
    {
        "_id" => IdEquals(predicate, context),
        "_type" => TypeEquals(predicate, context),
        _ => null,
    };

    private static Predicate IdEquals(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        var value = (TokenSearchValue)predicate.Value;
        if (value.System is not null)
        {
            throw new NotSupportedException("_id does not support a System qualifier.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException("_id requires a non-empty value.");
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceId"), context.Parameter(value.Code));
    }

    private static Predicate TypeEquals(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        var value = (TokenSearchValue)predicate.Value;
        if (value.System is not null)
        {
            throw new NotSupportedException("_type does not support a System qualifier.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException("_type requires a non-empty resource type name.");
        }

        var table = SqlCatalog.Default.Table("Resource");
        return new Predicate.Equal(new SqlColumnRef(table.TableName, "ResourceTypeId"), context.Parameter(context.ResourceTypeId(value.Code)));
    }
}
```

- [ ] **Step 4: Run to confirm the rule tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResourceColumnLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Add `Lower.Run`'s extraction pass**

Replace `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`'s `Run` method and add two new private methods:

```csharp
    public static QueryPlan Run(Expression expression, SymbolTable symbols, int? top = null, string? targetResourceType = null)
    {
        var leafContext = new LeafContext(symbols);
        var (remaining, outerPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
        var context = new StructuralContext(symbols, targetResourceType);
        var match = remaining is null
            ? context.LowerResourceSource()
            : LowerNode(remaining, context);
        return new QueryPlan(context.Ctes, match, top, outerPredicate);
    }
```

```csharp
    private static (Expression? Remaining, Predicate? OuterPredicate) ExtractResourceColumnPredicates(Expression expression, LeafContext leafContext)
    {
        if (expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and)
        {
            var kept = new List<Expression>();
            Predicate? outer = null;
            foreach (var child in and.Expressions)
            {
                var resourcePredicate = TryExtractResourceColumnPredicate(child, leafContext);
                outer = resourcePredicate is null
                    ? outer
                    : outer is null ? resourcePredicate : new Predicate.And(outer, resourcePredicate);
                if (resourcePredicate is null)
                {
                    kept.Add(child);
                }
            }

            Expression? remaining = kept.Count switch
            {
                0 => null,
                1 => kept[0],
                _ => new MultiaryExpression(MultiaryOperator.And, kept),
            };
            return (remaining, outer);
        }

        var single = TryExtractResourceColumnPredicate(expression, leafContext);
        return single is null ? (expression, null) : (null, single);
    }

    private static Predicate? TryExtractResourceColumnPredicate(Expression expression, LeafContext leafContext)
        => expression is SearchParameterExpression { Expression: SearchParameterPredicateExpression predicate }
            ? ResourceColumnLoweringRule.TryLower(predicate, leafContext)
            : null;
```

Note the deliberate scope limit (already stated in this plan's Global Constraints): only a bare `SearchParameterExpression(param, SearchParameterPredicateExpression)` shape is recognized -- an `Or`-of-alternatives, a `:not`-wrapped, or a composite-shaped resource-column predicate is not extracted here and falls through to the normal `LowerNode` dispatch, which will throw for `_id`/`_type` (there is no `ResourceSearchParam` table for the generic dispatcher to route to).

- [ ] **Step 6: Write the failing E2E tests**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:

```csharp
    [Fact]
    public async Task GivenAPatientIdOnlyQuery_WhenCompiled_ThenUsesResourceSourceAsTheBaseSetWithAnOuterIdFilter()
    {
        // Arrange -- Patient?_id=123 (no other search parameters)
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var tree = new SearchParameterExpression(
            idParam,
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null)));

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None, targetResourceType: "Patient");
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- ResourceSource's own ResourceTypeId consumes @p0 (it's a real bound parameter in
        // Emit, and PlanExplainer's ordinal counter now accounts for it too), so the outer predicate is @p1
        plan.Explain().ShouldBe("root = ResourceSource[103] WHERE ResourceId = @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldNotContain("123");
    }

    [Fact]
    public async Task GivenAPatientIdAndActiveQuery_WhenCompiled_ThenLowersActiveNormallyAndAppliesIdAsAnOuterFilter()
    {
        // Arrange -- Patient?_id=123&active=true
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null))),
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None, targetResourceType: "Patient");
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- only `active` becomes a CTE; `_id` becomes the outer WHERE
        plan.Explain().ShouldBe("root = TokenSearchParam[44]  Code = @p0 WHERE ResourceId = @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldNotContain("123");
        emitted.Sql.ShouldNotContain("true");
    }
```

- [ ] **Step 7: Run to confirm they fail, then implement/pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

First run (before Step 5's `Lower.cs` changes land): compile error or `NotSupportedException`. After Step 5: expected 0 warnings, 0 errors, all tests pass -- including every pre-existing E2E test.

- [ ] **Step 8: Run the full `Ignixa.Search.Sql.Tests` fixture**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```

Expected: 0 warnings, 0 errors, zero regressions.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(search-sql): lower _id/_type via ResourceColumnLoweringRule + Lower.Run's extraction pass

_id/_type are intercepted at the top level of a conjunction before the
normal LowerNode dispatch (which would otherwise wrongly route them to
TokenSearchParam -- there is no such table for _id/_type). A resource-
column-only query (no other search parameters) falls back to
ResourceSource as the base set, with the outer WHERE still applying."
```

---

### Task 8: `_lastUpdated` (exact-instant only)

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ResourceColumnLoweringRule.TryLower` now also handles `_lastUpdated`.

- [ ] **Step 1: Write the failing tests**

Add to `test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs`:

```csharp
    private static SearchParameterInfo LastUpdatedParameter()
        => new("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));

    [Fact]
    public void GivenAnExactInstantLastUpdatedParameter_WhenTried_ThenComparesResourceSurrogateId()
    {
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var value = new DateTimeSearchValue(instant);
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Ge, modifier: null, value);

        var result = ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103));

        var ge = result.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("ResourceSurrogateId");
        // 2023-06-15T12:30:00.000Z truncated-to-millisecond ticks, left-shifted 3 bits
        var expectedTicks = new DateTime(2023, 6, 15, 12, 30, 0, DateTimeKind.Utc).Ticks;
        ge.Value.Value.ShouldBe(expectedTicks << 3);
    }

    [Fact]
    public void GivenAPartialPrecisionLastUpdatedParameter_WhenTried_ThenThrows()
    {
        // Arrange -- "_lastUpdated=2023" (year-only precision), which PartialDateTime widens to a
        // non-degenerate [Start, End) range rather than a single instant.
        var value = new DateTimeSearchValue(
            new PartialDateTime(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimePrecision.Year));
        var predicate = new SearchParameterPredicateExpression(LastUpdatedParameter(), SearchComparator.Eq, modifier: null, value);

        Should.Throw<NotSupportedException>(() => ResourceColumnLoweringRule.TryLower(predicate, ContextResolving("Patient", 103)));
    }
```

Verify `PartialDateTime`'s real constructor and `DateTimePrecision` enum member names, and `DateTimeSearchValue`'s constructor overload accepting a `PartialDateTime`, against `src/Core/Ignixa.Search/Indexing/SearchValues/DateTimeSearchValue.cs`/`PartialDateTime.cs` before running -- correct the test construction if it disagrees. If no such overload exists, construct a value whose `.Start`/`.End` genuinely differ by whatever means the type actually offers (e.g. an explicit two-`DateTimeOffset`-argument constructor), and adjust the test accordingly; the point of the test is "a value where `Start != End`", not the specific construction API.

- [ ] **Step 2: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResourceColumnLoweringRuleTests" --nologo
```

Expected: FAIL -- `_lastUpdated` falls through `TryLower`'s switch to the `null` default, so the first test's `result.ShouldBeOfType<...>()` fails on a null reference; the second test's `Should.Throw` fails because nothing throws (it returns `null` instead).

- [ ] **Step 3: Implement**

In `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs`, add `"_lastUpdated" => LastUpdatedCompare(predicate, context),` to the `TryLower` switch (before `_ => null`), and add:

```csharp
    private static Predicate LastUpdatedCompare(SearchParameterPredicateExpression predicate, LeafContext context)
    {
        var value = (DateTimeSearchValue)predicate.Value;
        if (value.Start != value.End)
        {
            throw new NotSupportedException(
                "_lastUpdated only supports an exact instant (Start == End) for now -- partial-precision " +
                "ranges need a point-column-vs-search-range comparator formula that has no live reference " +
                "implementation to verify against (the real pipeline's ProcessResourceLastUpdatedExpressionAsync " +
                "only ever compares against one already-resolved instant); deliberately deferred, not an oversight.");
        }

        var targetId = ToSurrogateId(value.Start);
        var table = SqlCatalog.Default.Table("Resource");
        var column = new SqlColumnRef(table.TableName, "ResourceSurrogateId");
        var targetParam = context.Parameter(targetId);

        return predicate.Comparator switch
        {
            SearchComparator.Eq => new Predicate.Equal(column, targetParam),
            SearchComparator.Ne => new Predicate.Or(new Predicate.LessThan(column, targetParam), new Predicate.GreaterThan(column, targetParam)),
            SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(column, targetParam),
            SearchComparator.Ge => new Predicate.GreaterThanOrEqual(column, targetParam),
            SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(column, targetParam),
            SearchComparator.Le => new Predicate.LessThanOrEqual(column, targetParam),
            SearchComparator.Ap => throw new NotSupportedException(
                "_lastUpdated's :ap comparator requires DateTimeOffset.UtcNow at lowering time, which conflicts " +
                "with Lower's purity invariant -- not implemented."),
            _ => throw new NotSupportedException($"Unknown SearchComparator '{predicate.Comparator}'."),
        };
    }

    /// <summary>
    /// Duplicated from Ignixa.Domain.Abstractions.IdHelper.ToId() -- that type lives in an
    /// Application-tier project (Ignixa.Domain), which this Core-tier compiler project cannot
    /// reference per the repo's strict layer rule. The formula is pure DateTimeOffset/bit-shift math
    /// with zero external dependencies, matching the same "small, stable domain logic, transcribed
    /// once" precedent already used for NumericRangeComparison/DateTimeRangeComparison/
    /// TokenColumnEquality. Millisecond-truncated UTC ticks, left-shifted 3 bits (the low 3 bits are
    /// reserved for a per-millisecond uniquifier the database allocates at write time -- irrelevant to
    /// a search-time comparison, which only needs the timestamp bits).
    /// </summary>
    private static long ToSurrogateId(DateTimeOffset dateTimeOffset)
    {
        var utc = dateTimeOffset.UtcDateTime;
        var truncatedTicks = utc.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond;
        return truncatedTicks << 3;
    }
```

Add `using Ignixa.Specification.ValueSets.Normative;` to the file's usings for `SearchComparator` if not already present (check the existing using list first).

- [ ] **Step 4: Run to confirm they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResourceColumnLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Add one E2E test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:

```csharp
    [Fact]
    public async Task GivenAPatientLastUpdatedExactInstantQuery_WhenCompiled_ThenAppliesItAsAnOuterFilter()
    {
        // Arrange -- Patient?_lastUpdated=2023-06-15T12:30:00.000Z
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var instant = new DateTimeOffset(2023, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var tree = new SearchParameterExpression(
            lastUpdatedParam,
            new SearchParameterPredicateExpression(lastUpdatedParam, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(instant)));

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None, targetResourceType: "Patient");
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- ResourceSource's own ResourceTypeId consumes @p0, so the outer predicate is @p1
        plan.Explain().ShouldBe("root = ResourceSource[103] WHERE ResourceSurrogateId >= @p1");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        var expectedTicks = new DateTime(2023, 6, 15, 12, 30, 0, DateTimeKind.Utc).Ticks;
        emitted.Parameters.ShouldContain(p => p.Value.Equals(expectedTicks << 3));
    }
```

- [ ] **Step 6: Run to confirm it passes, then the full `Ignixa.Search.Sql.Tests` fixture**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass, zero regressions.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): lower _lastUpdated (exact-instant only)

Transcribes Ignixa.Domain.Abstractions.IdHelper.ToId()'s formula (Core
cannot reference that Application-tier project) -- millisecond-truncated
UTC ticks left-shifted 3 bits. Comparator mapping matches the real, live
ProcessResourceLastUpdatedExpressionAsync exactly (a point-column
comparison, not the range-overlap logic DateTimeRangeComparison uses for
stored [Start,End] pairs). Partial-precision _lastUpdated throws --
deliberately deferred, no live oracle exists to verify a range-aware
formula against."
```

---

### Task 9: Combined proof + full regression

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-8.
- Produces: no new production code -- this task is proof, not implementation.

- [ ] **Step 1: Write one combined E2E test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:

```csharp
    [Fact]
    public async Task GivenAPatientIdAndNameNotQuery_WhenCompiled_ThenCombinesTheOuterFilterAndTheExceptResult()
    {
        // Arrange -- Patient?_id=123&name:not=Smith
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "123", text: null))),
            new SearchParameterExpression(
                nameParam,
                new NotExpression(new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith")))),
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None, targetResourceType: "Patient");
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- the :not's ResourceSource+Except becomes the match CTE; _id becomes the outer WHERE.
        // ResourceSource's own ResourceTypeId consumes @p0, so Text is @p1 and the outer ResourceId is @p2.
        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[103]\n" +
            "cte1 = StringSearchParam[202]  Text LIKE @p1 (StartsWith) collate CI_AI\n" +
            "root = Except(cte0, cte1) WHERE ResourceId = @p2");
        emitted.Sql.ShouldContain("NOT EXISTS");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource");
        emitted.Sql.ShouldNotContain("Smith");
        emitted.Sql.ShouldNotContain("123");
    }
```

The exact `Explain()` golden string above depends on Task 5's `Text LIKE @p0 (StartsWith) collate CI_AI` rendering being confirmed correct there first -- if Task 5's own test needed correcting against real output, mirror that correction here too.

- [ ] **Step 2: Run to confirm it passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 3: Full solution build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors. The only failures should be the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures per target framework -- confirm no new failures.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(search-sql): prove :not and resource-column predicates compose together

Patient?_id=123&name:not=Smith exercises both new mechanisms in one
query -- the :not's ResourceSource+Except becomes the match CTE, _id
applies as the outer WHERE against dbo.Resource, confirming they're
genuinely independent, composable pieces."
```

---

## Self-Review

**Spec coverage:** `:not` (Tasks 2, 3, 5) and resource-column predicates `_id`/`_type`/`_lastUpdated` (Tasks 6, 7, 8) both covered, both proven end-to-end individually and combined (Task 9). Both real architectural decisions from this plan's design phase are reflected precisely: `ResourceSource` carries no `Predicate` (outer-WHERE is the separate mechanism for ordinary filtering, per the user's explicit choice after the SQL-shape/TOP-pushdown discussion); `_lastUpdated` is exact-instant-only (per the user's explicit choice to avoid an unverified point-vs-range formula).

**Placeholder scan:** No TBD/TODO; every step has complete code, verified against directly-read current source for every file touched (`CteDefinition.cs`, `QueryPlan.cs`, `Emit.cs`, `PlanExplainer.cs`, `Lower.cs`, `StructuralContext.cs`, `LeafContext.cs`, `Resolve.cs`, `SymbolTable.cs`, `NotExpression.cs`, the real `dbo.Resource` DDL, the real `_lastUpdated` SQL generator, the real `IdHelper.ToId()` formula). The one place this plan explicitly flags residual uncertainty (Task 5/9's golden `Explain()` strings for the default-modifier String collation) is a deliberate, stated "verify against real output, don't guess" instruction, not a placeholder.

**Type consistency:** `Lower.Run`'s new signature (`Expression, SymbolTable, int? top = null, string? targetResourceType = null`) and `Resolve.RunAsync`'s new signature (`Expression, ISymbolResolver, CancellationToken, string? targetResourceType = null`) are both additive/optional and used identically across every task that calls them (Tasks 5, 7, 8, 9). `ResourceColumnLoweringRule.TryLower`'s signature (`SearchParameterPredicateExpression, LeafContext`) is set once in Task 7 and only gains a new switch arm in Task 8 -- no signature drift. `StructuralContext.LowerNot`/`LowerResourceSource`'s shapes match exactly how `Lower.cs`'s `LowerSearchParameter`/`Run` call them.
