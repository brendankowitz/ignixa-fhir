# Sort and Keyset Pagination (Phase 8, part 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compile `_sort` (single- and multi-key, capped at 3 keys) into keyset/seek-style pagination in `Ignixa.Search.Sql`, composing cleanly with `_include`/`_revinclude` (Phase 7) and `CompartmentSearchExpression` (Phase 8 part 1) with zero changes to either mechanism.

**Architecture:** No new `CteDefinition`. Sort is two new trailing `QueryPlan` fields — `SortSpec` (which keys, in what order, which two-phase segment) and `PageSpec` (the keyset boundary, decoded from a continuation token by the caller) — synthesized into SQL by `Emit` at the page-selection site only, keeping the match graph (`Ctes`/`Match`) permanently free of ordering concerns. The sort source is `dbo.StringSearchParam`/`dbo.DateTimeSearchParam`'s `IsMin`/`IsMax` flag columns (now correctly populated at write time — a prerequisite fix merged earlier this session, `docs/superpowers/plans/2026-07-18-search-indexer-min-max-flags.md`), joined directly with `IsMin = 1`/`IsMax = 1`, no query-time aggregation. `SortPhase` (which two-phase segment: `Valued` or `MissingPrimary`) is a **caller input**, not something `Lower` computes — the phase-transition state machine lives in the eventual Phase 9 executor, exactly as it does in fhir-server.

**Tech Stack:** C# / .NET 9+, xUnit + Shouldly, `Ignixa.Search.Sql` (Core-tier, no EF/ASP.NET references).

**Full design:** `docs/superpowers/specs/2026-07-18-fhir-to-sql-compiler-sort-design.md` — read this first for the *why* behind every task below; this plan only covers the *what* and *how*, task by task. Section references (§N) below refer to that document. It went through two rounds of Fable adversarial research (fhir-server's real sort/continuation mechanism and bug cluster; a focused hand-traced validation of the multi-key sentinel-substitution design after the user pushed back on an initial recommendation to defer multi-key entirely), then one more revision after the user asked to fix a newly-discovered Ignixa bug (`IsMin`/`IsMax` never populated) as a prerequisite rather than deferring it.

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework) are pre-existing and out of scope, per every prior increment on this branch.
- **`SortKey`/`SortSpec`/`PageSpec`**, exact shapes (design §3):
  ```csharp
  public enum SortKeyKind { String, Date, LastUpdated }

  public sealed record SortKey(short? SearchParamId, SortKeyKind Kind, SortOrder Direction);
  // SearchParamId is null only for Kind == LastUpdated (a resource-column key, no join needed).

  public enum SortPhase { Valued, MissingPrimary }

  public sealed record SortSpec(IReadOnlyList<SortKey> Keys, SortPhase Phase);
  // Keys.Count is 1-3; Phase applies to Keys[0] only.

  public sealed record PageSpec(
      IReadOnlyList<SqlParameterRef> Boundary,     // one value per ACTIVE key for this phase: Keys.Count in Valued, Keys.Count-1 in MissingPrimary (Keys[0] excluded)
      SqlParameterRef BoundaryResourceTypeId,
      SqlParameterRef BoundarySurrogateId);
  ```
  `SortOrder` is `Ignixa.Search.Expressions.SortOrder` (`Ascending`/`Descending`) — already exists, do not define a new enum for direction.
- **`QueryPlan` gains two trailing optional fields** (purely additive, matching every prior phase's precedent): `SortSpec? Sort = null, PageSpec? Page = null`.
- **Sort source is `IsMin`/`IsMax`, not query-time aggregation** — `WHERE ... AND sk{i}.IsMin = 1` (ascending) / `AND sk{i}.IsMax = 1` (descending), a plain filtered join against `dbo.StringSearchParam`/`dbo.DateTimeSearchParam`, riding the `(SearchParamId, Text)`/`(SearchParamId, StartDateTime)` indexes directly. This is safe now — the write-path prerequisite fix (`ElementSearchIndexer.MarkMinMaxValues`) is merged and pushed.
- **The primary key (`Keys[0]`) drives the two-phase segmentation**: in `SortPhase.Valued`, its join is `INNER JOIN` (presence gates the match) and its value expression never needs `ISNULL` (an `INNER JOIN` guarantees non-null). In `SortPhase.MissingPrimary`, `Keys[0]` is excluded from the join list entirely and replaced by a `NOT EXISTS` clause against its table/`SearchParamId` — see Task 3. Every OTHER key (secondary keys in either phase) is always `LEFT JOIN` with `ISNULL(col, sentinel)` wherever its value is read.
- **Sentinels** (design §3.3): String → `N''`. Date → `'0001-01-01T00:00:00.0000000'` (matches `DATETIME2(7)`'s real minimum, confirmed against `97.sql:293`: `StartDateTime DATETIME2(7) NOT NULL`). `_lastUpdated` needs no sentinel — it never has a `NULL`/missing case (every resource has a surrogate id).
- **The `ORDER BY` expression and the seek-predicate expression for a given key must be produced by the exact same helper method call, not independently re-derived text** (the F1 invariant, design §3.3) — a single `SortValueExpr(SortKey key, int index, SortPhase phase)`-shaped helper is called from both the `ORDER BY` builder and the seek-predicate builder; there must be no second place in `Emit.cs` that hand-writes an `ISNULL(...)` string for a sort key.
- **Every `TOP` this compiler emits must be paired with an `ORDER BY` in the same `SELECT`** (design §1.4/§4, closing a real, live gap in `cteMatchPage`'s current construction) — this applies unconditionally, not just when `Sort` is present: the existing no-sort `TOP` sites (`cteMatchPage`'s `SELECT TOP(n) m.T1, m.Sid1 ...` and the plain outer `SELECT TOP(n) T1, Sid1 FROM cte{Match}`) currently have NO `ORDER BY` at all — this plan closes that gap universally (defaulting to `ORDER BY T1, Sid1` when `plan.Sort` is null), not only for sorted queries.
- **`(T1, Sid1)` (or the sort-scoped equivalent, `ResourceTypeId`/`ResourceSurrogateId`) is always the final tie-break**, present in both `ORDER BY` and the seek predicate unconditionally — never optional, never conditionally omitted for the "single resource type" case (a defensible simplification the design doc's own illustrative SQL used, but this plan always includes the full `(T1, Sid1)` composite for consistency and because it costs nothing: `T1 = T1` is trivially true and harmless for a single-type match, and unconditional inclusion is what correctly supports a wildcard-compartment sorted search, which spans multiple types).
- **`Resolve.RunAsync`'s `targetResourceType` becomes `string?`** (aligning it with `Lower.Run`'s existing Phase 8 part 1 nullability — a carried-forward inconsistency this phase closes, design §5) — this is a pure type-widening change requiring **zero call-site edits** (every existing call site already passes a non-null string literal, valid for a `string?` parameter), exactly like `Lower.Run`'s own Phase 8 part 1 precedent. Do not attempt a call-site sweep for this change.
- **Multi-key sort is capped at 3 keys**, a policy guard not an architectural limit (design §3.3) — `Lower` throws a named `NotSupportedException` beyond 3 keys, citing the cap.
- **Supported `SortKeyKind`s this phase: `String`, `Date`, `LastUpdated`** — `Token`/`Number`/`Quantity`/`Reference`/`Uri` sort throws a named `NotSupportedException`, deferred.
- **A wildcard compartment search (`targetResourceType == null`) combined with `_sort` throws `NotSupportedException`** (design §5) — a `SortSpec` needs a single `ResourceTypeId` scope for its joins, matching the Phase 8 part 1 precedent already established for typed leaves and `_include`/`_revinclude` under a null scope.
- `_sort`/continuation-token interaction with the `$includes` operation's own separate mechanism, instance-level SMART/compartment filtering, the `IncludeStage.Direction`/`Reversed` dual-source-of-truth risk (Phase 7's final review), and the compartment nested-`And` `ExtractResourceColumnPredicates` gap (Phase 8 part 1's final review) are all explicitly out of scope for this plan (design §7) — nothing in this plan should throw a DIFFERENT exception for these than whatever the existing code already produces for unhandled shapes, and nothing in this plan should touch `Ignixa.Api`/`Ignixa.Application`/`Ignixa.DataLayer.SqlEntityFramework`.

---

### Task 1: Widen `Resolve`/`SymbolCollectingVisitor` for `SortExpression`, align `targetResourceType` nullability

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`

**Interfaces:**
- Consumes: nothing new from earlier tasks (foundational, matching the "resolve first" sequencing every prior phase in this roadmap used).
- Produces: `Resolve.RunAsync(Expression? expression, IReadOnlyList<IncludeExpression> includes, IReadOnlyList<IncludeExpression> revIncludes, IReadOnlyList<SortExpression> sort, ISymbolResolver resolver, string? targetResourceType, CancellationToken cancellationToken, ICompartmentDefinitionManager? compartmentDefinitionManager = null, ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)`. `SymbolCollectingVisitor.CollectSort(SortExpression)`. Task 6 (`Lower`) is the primary consumer of the resolved `SearchParamId`s this produces.

`SortExpression` (`src/Core/Ignixa.Search/Expressions/SortExpression.cs`) lives on `SearchOptions.Sort`, never inside `options.Expression` — no `VisitSortParameter` override would ever fire, the same situation Phase 7's `IncludeExpression` was in (not Phase 6's `ChainedExpression`, which genuinely is part of the walked tree). This is a direct-call method, mirroring `CollectInclude`'s shape, not a visitor override.

- [ ] **Step 1: Add `SymbolCollectingVisitor.CollectSort`**

In `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`, add (after `CollectInclude`, before the private `AddResourceType` helper):

```csharp
    /// <summary>
    /// Collects a SortExpression's own SearchParameterInfo for the existing SearchParamId resolution
    /// loop -- _lastUpdated needs no SearchParamId at all (it lowers to a direct ResourceSurrogateId
    /// ordering, matching the compiler's existing precedent that treats _lastUpdated as a derived
    /// function of the surrogate id, per the sixth increment's ResourceColumnLoweringRule), so it is
    /// deliberately skipped here rather than added and later failing SymbolTable.SearchParamId. Not a
    /// visitor override: SortExpression lives on SearchOptions.Sort, never on the Expression tree this
    /// visitor walks, so Resolve calls this directly per sort key.
    /// </summary>
    public void CollectSort(SortExpression sort)
    {
        if (sort.Parameter.Code != "_lastUpdated")
        {
            Parameters.Add(sort.Parameter);
        }
    }
```

Add `using Ignixa.Search.Expressions;` if not already present (it is, for `ChainedExpression`/`IncludeExpression`/`CompartmentSearchExpression`, all in the same namespace as `SortExpression`).

Update the class's `<remarks>` block: append, after the existing sentence ending "...see Resolve's remarks for the full argument.": ` As of Phase 8 part 2, CollectSort collects a SortExpression's own SearchParameterInfo the same way CollectInclude does -- a direct method, not a visitor override, since SortExpression is also never part of this Expression tree.`

- [ ] **Step 2: Widen `Resolve.RunAsync`**

In `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`, change the signature:

```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        ISymbolResolver resolver,
        string targetResourceType,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
```

to:

```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        IReadOnlyList<SortExpression> sort,
        ISymbolResolver resolver,
        string? targetResourceType,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
```

`sort` is inserted after `revIncludes` (grouped with the other "collection of things to collect symbols from" parameters, before the single-purpose `resolver`/`targetResourceType`/`cancellationToken` tail) — matching the existing `includes`/`revIncludes` placement convention. `targetResourceType`'s type changes from `string` to `string?`; remove `ArgumentNullException.ThrowIfNull(targetResourceType);` (a null value is now legitimate — the wildcard-compartment-search case) and replace the body's use of `targetResourceType` accordingly:

```csharp
        ArgumentNullException.ThrowIfNull(includes);
        ArgumentNullException.ThrowIfNull(revIncludes);
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(resolver);

        var collector = new SymbolCollectingVisitor();
        if (expression is not null)
        {
            expression.AcceptVisitor(collector, context: null);
        }

        foreach (var include in includes)
        {
            collector.CollectInclude(include);
        }

        foreach (var revInclude in revIncludes)
        {
            collector.CollectInclude(revInclude);
        }

        foreach (var sortExpression in sort)
        {
            collector.CollectSort(sortExpression);
        }

        var compartmentMembership = ResolveCompartmentMembership(collector, compartmentDefinitionManager, searchParameterDefinitionManager);

        var searchParamIds = new Dictionary<string, short>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);
            if (id.HasValue)
            {
                searchParamIds[parameter.Url.ToString()] = id.Value;
            }
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
        }

        return new SymbolTable(searchParamIds, resourceTypeIds, compartmentMembership);
```

(`ResolveCompartmentMembership`, the private helper below this method, is unchanged — do not modify it.)

Update the class's `<remarks>` block: append, after the existing sentence ending "...see that method's remarks for the exact shape.": ` As of Phase 8 part 2, resolution also extends to every SortExpression passed via the sort parameter -- see SymbolCollectingVisitor.CollectSort's remarks. targetResourceType is now nullable, matching Lower.Run's own Phase 8 part 1 widening for the wildcard-compartment-search case -- when null, only resource types collected from the tree/includes/sort/compartments are resolved, with no forced addition.`

- [ ] **Step 3: Sweep call sites — confirm zero edits needed for `targetResourceType`, add `sort: []` to every existing call**

`targetResourceType`'s `string` → `string?` change needs no call-site edits (every existing literal remains valid). Adding the new REQUIRED `sort` parameter does need a sweep, since it has no default. Run `grep -rn "Resolve\.RunAsync(" --include=*.cs .` from the repo root to find every call site (as of this plan's writing: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`, `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/SqlEntityFrameworkSymbolResolverTests.cs`, `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs` — re-run this grep yourself rather than trusting this list, since call sites may have shifted since this plan was written). Insert `sort: []` immediately after the `revIncludes:` argument at every call site — matching the exact mechanical pattern every prior phase's `includes: [], revIncludes: []` insertion already used. Example transformation:

```csharp
// Before:
var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
// After:
var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], sort: [], resolver, "Patient", CancellationToken.None);
```

- [ ] **Step 4: Add a `CollectSort` unit test**

Add to `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`:

```csharp
    [Fact]
    public async Task GivenASortExpression_WhenResolved_ThenSymbolTableHasItsSearchParamId()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var sortExpression = new SortExpression(nameParam, SortOrder.Ascending);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [sortExpression], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(nameParam).ShouldBe((short)202);
    }

    [Fact]
    public async Task GivenALastUpdatedSortExpression_WhenResolved_ThenNoSearchParamIdIsRequested()
    {
        // Arrange -- _lastUpdated needs no SearchParamId lookup at all.
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var sortExpression = new SortExpression(lastUpdatedParam, SortOrder.Descending);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act -- must not throw even though the resolver has no SearchParamId row for _lastUpdated.
        var symbolTable = await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: [sortExpression], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        Should.Throw<KeyNotFoundException>(() => symbolTable.SearchParamId(lastUpdatedParam));
    }
```

Add `using Ignixa.Search.Expressions;` to `ResolveTests.cs`'s usings if not already present (it is).

- [ ] **Step 5: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all new tests, plus every existing test in this project (with its mechanically-updated `sort: []` call site).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/SqlEntityFrameworkSymbolResolverTests.cs test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs
git commit -m "feat(search-sql): resolve SortExpression, align targetResourceType nullability"
```

---

### Task 2: `SortKey`/`SortSpec`/`PageSpec` AST + `Emit`'s core rendering (joins, `ORDER BY`, seek predicate) + `PlanExplainer` — wired into `Emit.Run`'s plain (no-includes) path only

This is the highest-risk task in the plan — the actual keyset/seek SQL generation. It deliberately covers ONLY `Emit.Run`'s existing early-return (no-includes) branch; the `cteMatchPage`/includes-path restructuring is Task 3, which reuses every helper this task writes without modification. AST-only in the sense that no `Lower`-side translation exists yet (Task 4) — every test here hand-constructs `SortSpec`/`PageSpec` directly, matching the exact pattern every prior phase's "AST + Emit" task used.

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `SortKeyKind`, `SortKey(short? SearchParamId, SortKeyKind Kind, SortOrder Direction)`, `SortPhase`, `SortSpec(IReadOnlyList<SortKey> Keys, SortPhase Phase)`, `PageSpec(IReadOnlyList<SqlParameterRef> Boundary, SqlParameterRef BoundaryResourceTypeId, SqlParameterRef BoundarySurrogateId)`. `QueryPlan.Sort`/`QueryPlan.Page`. Task 3 reuses `Emit`'s private `EmitOrderBy`/`EmitSeekPredicate`/`SortValueExpr`/`EmitSortJoins` helpers verbatim for the includes path. Task 4 (`Lower`) is the primary consumer that constructs real `SortSpec`/`PageSpec` instances from bound queries.

- [ ] **Step 1: `SortKey`/`SortSpec`/`PageSpec` records**

Create `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs`:

```csharp
using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which FHIR sort-key kinds this compiler can emit joins/value-expressions for. String and Date are
/// the only search-parameter-table kinds fhir-server's own SQL sort path supports; LastUpdated is a
/// resource-column kind needing no join at all (ResourceSurrogateId already encodes it, per the
/// compiler's existing ResourceColumnLoweringRule precedent).
/// </summary>
public enum SortKeyKind
{
    String,
    Date,
    LastUpdated,
}

/// <summary>
/// One _sort key. SearchParamId is null only for Kind == LastUpdated. Reuses Ignixa.Search.Expressions.SortOrder
/// (Ascending/Descending) directly rather than a new enum -- no polarity-inversion risk exists here the
/// way ChainDirection/IncludeDirection's own distinct-enum precedent was protecting against.
/// </summary>
public sealed record SortKey(short? SearchParamId, SortKeyKind Kind, SortOrder Direction);

/// <summary>
/// Which two-phase missing-value segment this plan computes -- Valued (Keys[0]'s join is INNER,
/// gating on presence) or MissingPrimary (Keys[0] is excluded from the join list entirely, replaced by
/// a NOT EXISTS clause). Only Keys[0] (the primary key) has a phase; every other key is always a
/// LEFT JOIN tie-breaker in either phase. The phase is a CALLER input -- Lower does not compute it by
/// inspecting the query, matching fhir-server's own executor-driven phase-transition model. See
/// docs/superpowers/specs/2026-07-18-fhir-to-sql-compiler-sort-design.md §1.2/§3.2.
/// </summary>
public enum SortPhase
{
    Valued,
    MissingPrimary,
}

/// <summary>
/// A compiled _sort, capped at 3 keys (Global Constraints) this phase. Keys[0] is the primary key,
/// whose presence/absence Phase segments; Keys[1..] are always ordinary LEFT-JOIN tie-breakers.
/// </summary>
public sealed record SortSpec(IReadOnlyList<SortKey> Keys, SortPhase Phase);

/// <summary>
/// The keyset boundary decoded from a continuation token by the caller -- null PageSpec means "first
/// page." Boundary carries one value per ACTIVE key for the current SortSpec.Phase: Keys.Count values
/// in SortPhase.Valued, Keys.Count-1 in SortPhase.MissingPrimary (Keys[0] excluded, since the primary
/// key has no value in that phase by construction). Values are POST-sentinel-substitution (§3.3) --
/// the caller is responsible for applying the same ISNULL/sentinel logic to a decoded token value that
/// Emit applies to a live column, so the two compare correctly. All three fields render as bound
/// SqlParameterRefs, never inlined literals -- they are client-controlled input.
/// </summary>
public sealed record PageSpec(
    IReadOnlyList<SqlParameterRef> Boundary,
    SqlParameterRef BoundaryResourceTypeId,
    SqlParameterRef BoundarySurrogateId);
```

- [ ] **Step 2: `QueryPlan.Sort`/`QueryPlan.Page`**

In `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`, change:

```csharp
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null)
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
    PageSpec? Page = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
```

Update the class's XML doc comment: replace the final sentence `"SortSpec/full PageSpec remain out of scope."` with `"Sort/Page (Phase 8 part 2) are the second tier-3 result-shape fields -- Sort decorates ordering only (never membership), synthesized entirely inside Emit's page-selection sites; Page is the keyset boundary a caller decodes from a continuation token. Both are purely additive -- a plan with neither is byte-identical to before these fields existed."`

- [ ] **Step 3: `Emit`'s sort-rendering helpers, wired into the no-includes branch**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, `Run`'s existing no-includes branch reads:

```csharp
        if (plan.Includes is not { Count: > 0 } includes)
        {
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

Replace it with:

```csharp
        if (plan.Includes is not { Count: > 0 } includes)
        {
            var withClause = $";WITH {string.Join(",\n", cteBlocks)}\n";
            var sortJoins = EmitSortJoins(plan.Sort);
            var sortColumns = EmitSortSelectColumns(plan.Sort);
            var orderBy = $"\nORDER BY {EmitOrderBy(plan.Sort)}";

            var whereClauses = new List<string>();
            if (plan.OuterPredicate is not null)
            {
                whereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
            }

            if (plan.Sort is { Phase: SortPhase.MissingPrimary })
            {
                whereClauses.Add(EmitMissingPrimaryFilter(plan.Sort));
            }

            if (plan.Page is { } page)
            {
                whereClauses.Add(EmitSeekPredicate(plan.Sort, page, parameters));
            }

            string sql;
            if (whereClauses.Count == 0)
            {
                sql = withClause + $"SELECT {top}m.T1, m.Sid1{sortColumns} FROM cte{plan.Match.Index} m{sortJoins}{orderBy}";
            }
            else
            {
                var resourceJoin = plan.OuterPredicate is null
                    ? string.Empty
                    : "\nINNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
                sql = withClause +
                      $"SELECT {top}m.T1, m.Sid1{sortColumns} FROM cte{plan.Match.Index} m{sortJoins}{resourceJoin}\n" +
                      $"WHERE {string.Join(" AND ", whereClauses)}{orderBy}";
            }

            return new EmittedSql(sql, parameters);
        }
```

This is a deliberate, minimal rewrite of the no-includes branch: when `plan.Sort` and `plan.Page` are BOTH null, `EmitSortJoins`/`EmitSortSelectColumns` both return `string.Empty` and `EmitOrderBy` returns the plain `"m.T1 ASC, m.Sid1 ASC"` default — meaning the rendered SQL is `SELECT {top}m.T1, m.Sid1 FROM cte{Match} m\nORDER BY m.T1 ASC, m.Sid1 ASC` (or with `OuterPredicate`'s `INNER JOIN`/`WHERE`) — **NOT byte-identical to the pre-Phase-8-part-2 shape**, because the plain `T1, Sid1` column aliasing changes from `T1, Sid1` (no `m.` prefix, no `ORDER BY`) to `m.T1, m.Sid1` with a trailing `ORDER BY`. This is an intentional, one-time, universal change (Global Constraints: "every `TOP` this compiler emits must be paired with an `ORDER BY`... applies unconditionally") — every existing golden string in `EmitTests.cs`/`EndToEndCompilationTests.cs` that asserts the OLD no-`ORDER BY` shape needs updating in this task, not preserved. This is the one deliberate exception to this project's usual "zero diff for the common case" discipline, and it is called out explicitly here so it isn't mistaken for an accidental regression.

Add the new private helper methods (after `OutputTypeColumn`, before `EmitCompartmentSource`):

```csharp
    private static string EmitSortJoins(SortSpec? sort)
    {
        if (sort is null)
        {
            return string.Empty;
        }

        var joins = new List<string>();
        for (var i = 0; i < sort.Keys.Count; i++)
        {
            if (i == 0 && sort.Phase == SortPhase.MissingPrimary)
            {
                continue; // primary key excluded from the join list in this phase -- see EmitMissingPrimaryFilter.
            }

            var key = sort.Keys[i];
            if (key.Kind == SortKeyKind.LastUpdated)
            {
                continue; // resource-column key, no join needed.
            }

            var table = key.Kind == SortKeyKind.String ? "StringSearchParam" : "DateTimeSearchParam";
            var flag = key.Direction == SortOrder.Ascending ? "IsMin" : "IsMax";
            var joinType = i == 0 ? "INNER" : "LEFT";
            joins.Add(
                $"\n{joinType} JOIN dbo.{table} sk{i}\n" +
                $"    ON sk{i}.ResourceTypeId = m.T1 AND sk{i}.ResourceSurrogateId = m.Sid1\n" +
                $"   AND sk{i}.SearchParamId = {key.SearchParamId} AND sk{i}.{flag} = 1");
        }

        return string.Concat(joins);
    }

    private static string EmitMissingPrimaryFilter(SortSpec sort)
    {
        var key = sort.Keys[0];
        var table = key.Kind == SortKeyKind.String ? "StringSearchParam" : "DateTimeSearchParam";
        return $"NOT EXISTS (SELECT 1 FROM dbo.{table} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
    }

    private static IReadOnlyList<int> ActiveKeyIndices(SortSpec? sort)
        => sort is null
            ? []
            : sort.Phase == SortPhase.Valued
                ? Enumerable.Range(0, sort.Keys.Count).ToList()
                : Enumerable.Range(1, sort.Keys.Count - 1).ToList();

    /// <summary>
    /// The F1 invariant (design §3.3): this is the ONLY place a sort key's value expression is
    /// rendered. EmitOrderBy and EmitSeekPredicate both call this -- neither hand-writes an
    /// ISNULL(...)/sentinel string independently, so the ORDER BY and seek-predicate expressions for
    /// a given key can never drift out of sync.
    /// </summary>
    private static string SortValueExpr(SortSpec sort, int index)
    {
        var key = sort.Keys[index];
        if (key.Kind == SortKeyKind.LastUpdated)
        {
            return "m.Sid1";
        }

        var column = key.Kind == SortKeyKind.String ? "Text" : "StartDateTime";
        var raw = $"sk{index}.{column}";

        var isGuaranteedNonNull = index == 0 && sort.Phase == SortPhase.Valued;
        if (isGuaranteedNonNull)
        {
            return raw;
        }

        var sentinel = key.Kind == SortKeyKind.String ? "N''" : "'0001-01-01T00:00:00.0000000'";
        return $"ISNULL({raw}, {sentinel})";
    }

    private static string EmitOrderBy(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select(i =>
            $"{SortValueExpr(sort!, i)} {(sort!.Keys[i].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return string.Join(", ", terms.Append("m.T1 ASC").Append("m.Sid1 ASC"));
    }

    private static string EmitSortSelectColumns(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        return activeIndices.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", activeIndices.Select((idx, ordinal) => $"{SortValueExpr(sort!, idx)} AS SortValue{ordinal}"));
    }

    private static string EmitSeekPredicate(SortSpec? sort, PageSpec page, List<EmittedSqlParameter> parameters)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var boundaryParams = page.Boundary.Select(b => EmitParam(b, parameters)).ToList();
        var typeParam = EmitParam(page.BoundaryResourceTypeId, parameters);
        var sidParam = EmitParam(page.BoundarySurrogateId, parameters);

        var branches = new List<string>();
        for (var level = 0; level < activeIndices.Count; level++)
        {
            var terms = new List<string>();
            for (var j = 0; j < level; j++)
            {
                terms.Add($"{SortValueExpr(sort!, activeIndices[j])} = {boundaryParams[j]}");
            }

            var key = sort!.Keys[activeIndices[level]];
            var op = key.Direction == SortOrder.Ascending ? ">" : "<";
            terms.Add($"{SortValueExpr(sort, activeIndices[level])} {op} {boundaryParams[level]}");
            branches.Add(terms.Count > 1 ? $"({string.Join(" AND ", terms)})" : terms[0]);
        }

        var allEqual = activeIndices.Select((idx, j) => $"{SortValueExpr(sort!, idx)} = {boundaryParams[j]}").ToList();
        var allEqualPrefix = allEqual.Count > 0 ? string.Join(" AND ", allEqual) + " AND " : string.Empty;
        branches.Add($"({allEqualPrefix}m.T1 = {typeParam} AND m.Sid1 > {sidParam})");
        branches.Add($"({allEqualPrefix}m.T1 > {typeParam})");

        return branches.Count == 1 ? branches[0] : string.Join("\n       OR ", branches);
    }
```

`EmitMissingPrimaryFilter` is a separate WHERE-clause fragment (the `NOT EXISTS` check), not a join — it is called directly from `Run`'s `whereClauses` assembly above, not from `EmitSortJoins`. `EmitSortJoins` only ever emits `JOIN` clauses; the primary key's exclusion from the join list in `SortPhase.MissingPrimary` is the only phase-specific behavior it has, handled by the `i == 0 && sort.Phase == SortPhase.MissingPrimary` skip already shown above.

- [ ] **Step 4: `PlanExplainer` rendering**

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, change `Print` to add, after the existing `if (plan.Includes is { Count: > 0 } includes) { ... }` block, before `return string.Join('\n', lines);`:

```csharp
        if (plan.Sort is { } sort)
        {
            lines.Add($"sort = {PrintSortSpec(sort)}");
        }

        if (plan.Page is { } page)
        {
            lines.Add($"page = {PrintPageSpec(page, ref parameterOrdinal)}");
        }
```

Add the two new private methods (after `PrintIncludeStage`):

```csharp
    private static string PrintSortSpec(SortSpec sort)
    {
        var keys = sort.Keys.Select(k =>
            $"{k.Kind}:{(k.SearchParamId is { } id ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")} {(k.Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return $"SortSpec([{string.Join(", ", keys)}], {sort.Phase})";
    }

    private static string PrintPageSpec(PageSpec page, ref int parameterOrdinal)
    {
        var boundary = page.Boundary.Select(_ => $"@p{parameterOrdinal++}");
        var typeParam = $"@p{parameterOrdinal++}";
        var sidParam = $"@p{parameterOrdinal++}";
        return $"PageSpec(boundary=[{string.Join(",", boundary)}], type={typeParam}, sid={sidParam})";
    }
```

Add `using Ignixa.Search.Expressions;` to `PlanExplainer.cs` if not already present (needed for `SortOrder`).

- [ ] **Step 5: Update every existing `EmitTests.cs`/`EndToEndCompilationTests.cs`/`PlanExplainerTests.cs` golden string for the universal `ORDER BY` addition**

Per Step 3's explicit note, EVERY existing golden `Sql` string in `EmitTests.cs` and `EndToEndCompilationTests.cs` asserting the plain no-includes shape (`SELECT [TOP (n) ]T1, Sid1 FROM cte{N}` with no `m.` alias, no `ORDER BY`) must be updated to the new shape (`SELECT [TOP (n) ]m.T1, m.Sid1 FROM cte{N} m\nORDER BY m.T1 ASC, m.Sid1 ASC`, or with the `INNER JOIN dbo.Resource r ... WHERE ...` shape when `OuterPredicate` is present, same `ORDER BY` suffix). Grep for `SELECT {top}T1, Sid1` and `SELECT TOP (` in both test files to find every affected assertion — there is no way to enumerate them precisely here since this plan does not have live access to re-run the test suite, but every prior increment's zero-diff proof pattern inverts here on purpose: this task's own regression proof (Step 7) is running the full suite and updating every string that changed, not preserving any of them. Do NOT skip any `ShouldBe`/`ShouldContain` assertion that references the old shape — a plan that leaves even one stale golden string will fail CI, not silently pass.

`EmitTests.cs`'s `GivenAPlanWithNoIncludes_WhenEmitted_ThenTheSqlIsByteIdenticalToThePreIncludeShape` (added by the Phase 7 include plan specifically to pin the old no-`ORDER BY` shape) needs its own name and assertion updated in this task — rename it to `GivenAPlanWithNoIncludesAndNoSort_WhenEmitted_ThenTheSqlHasTheDefaultTypeAndSurrogateIdOrdering` and update its expected string to:

```csharp
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
```

- [ ] **Step 6: Write the new sort-specific tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
    [Fact]
    public void GivenASingleAscendingStringSortKeyInTheValuedPhase_WhenEmitted_ThenJoinsOnIsMinAndOrdersByTheJoinedColumn()
    {
        // Arrange -- Patient?_sort=name, first page (no boundary).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1, sk0.Text AS SortValue0 FROM cte0 m\n" +
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenASortWithAPageBoundary_WhenEmitted_ThenTheSeekPredicateAppearsInTheWhereClause()
    {
        // Arrange -- Patient?_sort=name, second page.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort, Page: page);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "WHERE sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND m.T1 = @p2 AND m.Sid1 > @p3)\n" +
            "       OR (sk0.Text = @p1 AND m.T1 > @p2)\n" +
            "ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
        emitted.Parameters.Count.ShouldBe(4);
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "Adams"));
        emitted.Parameters[2].ShouldBe(new EmittedSqlParameter("@p2", (short)103));
        emitted.Parameters[3].ShouldBe(new EmittedSqlParameter("@p3", 5000L));
    }

    [Fact]
    public void GivenTheMissingPrimaryPhase_WhenEmitted_ThenTheJoinIsReplacedByNotExistsAndTheOrderByOmitsTheMissingKey()
    {
        // Arrange -- Patient?_sort=name, second (missing-name) phase, no secondary keys.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.MissingPrimary);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10, Sort: sort);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain(
            "SELECT TOP (10) m.T1, m.Sid1 FROM cte0 m\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenAMultiKeySortWithMixedDirectionsAndASecondaryKeyTie_WhenEmitted_ThenTheOrderByAndSeekPredicateUseTheIdenticalIsNullExpression()
    {
        // Arrange -- Patient?_sort=name,-birthdate, valued phase, second key uses the F1 invariant
        // (ISNULL identical in ORDER BY and seek) since it's a LEFT-JOIN tie-breaker, not the primary.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec(
            [
                new SortKey(202, SortKeyKind.String, SortOrder.Ascending),
                new SortKey(303, SortKeyKind.Date, SortOrder.Descending),
            ],
            SortPhase.Valued);
        var page = new PageSpec(
            [new SqlParameterRef("Zorro"), new SqlParameterRef("2000-01-01T00:00:00.0000000")],
            new SqlParameterRef((short)103),
            new SqlParameterRef(9000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- same ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') text in both places.
        emitted.Sql.ShouldContain(
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "LEFT JOIN dbo.DateTimeSearchParam sk1\n" +
            "    ON sk1.ResourceTypeId = m.T1 AND sk1.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk1.SearchParamId = 303 AND sk1.IsMax = 1");
        emitted.Sql.ShouldContain(
            "WHERE sk0.Text > @p1\n" +
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') < @p2)\n" +
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p2 AND m.T1 = @p3 AND m.Sid1 > @p4)\n" +
            "       OR (sk0.Text = @p1 AND ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') = @p2 AND m.T1 > @p3)\n" +
            "ORDER BY sk0.Text ASC, ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenALastUpdatedSortKey_WhenEmitted_ThenNoJoinIsEmittedAndTheOrderByUsesTheSurrogateIdDirectly()
    {
        // Arrange -- Patient?_sort=-_lastUpdated.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(null, SortKeyKind.LastUpdated, SortOrder.Descending)], SortPhase.Valued);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("JOIN dbo.");
        emitted.Sql.ShouldContain("SELECT m.T1, m.Sid1, m.Sid1 AS SortValue0 FROM cte0 m\n");
        emitted.Sql.ShouldContain("ORDER BY m.Sid1 DESC, m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenNoSortButAPageBoundary_WhenEmitted_ThenTheSeekPredicateIsTheBareTypeAndSurrogateIdTupleOnly()
    {
        // Arrange -- an ordinary, unsorted paginated search (design §2's "no sort" keyset case).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var page = new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Page: page);

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- branches.Count == 2 here (no key levels, just the two final type/sid tie-break
        // branches), so EmitSeekPredicate's multi-branch join applies: "\n       OR ", not a single
        // space -- matching every other multi-branch case in this same method, not a special case.
        emitted.Sql.ShouldContain(
            "WHERE (m.T1 = @p1 AND m.Sid1 > @p2)\n" +
            "       OR (m.T1 > @p1)\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
    }
```

Add `using Ignixa.Search.Expressions;` to `EmitTests.cs` if not already present (needed for `SortOrder`).

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`:

```csharp
    [Fact]
    public void GivenAPlanWithSortAndAPageBoundary_WhenExplained_ThenPrintsBothAsTrailingLines()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var page = new PageSpec([new SqlParameterRef("Adams")], new SqlParameterRef((short)103), new SqlParameterRef(5000L));
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Sort: sort, Page: page);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "sort = SortSpec([String:202 ASC], Valued)\n" +
            "page = PageSpec(boundary=[@p1], type=@p2, sid=@p3)");
    }
```

Add `using Ignixa.Search.Expressions;` to `PlanExplainerTests.cs` if not already present.

- [ ] **Step 7: Run the tests, fix every stale golden string, confirm the new ones pass**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`
Expected: build errors until Steps 1-4 are applied in order, then 0 warnings, 0 errors.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: FAILures at first, from every existing golden string that assumed the old no-`ORDER BY` shape (Step 5's territory) — go through each failure, confirm it's exactly the `m.` alias + `ORDER BY` addition described in Step 3/5 (not some other, unrelated regression), and update the expected string. Do not update any golden string to a shape other than what Step 3's code actually produces — if a failure looks like anything other than the described `ORDER BY`/alias addition, stop and re-check Step 3's code against this brief before editing a test to match. Once every existing test is updated, expect PASS across the board including all 8 new tests.

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs src/Core/Ignixa.Search.Sql/Ast/Emit.cs src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): add SortSpec/PageSpec, Emit keyset-seek rendering for the plain (no-includes) path"
```

---

### Task 3: `Emit.Run`'s `cteMatchPage`/includes path — sort decoration + the widened result-shape contract

Reuses every helper Task 2 wrote (`EmitSortJoins`, `EmitSortSelectColumns`, `EmitOrderBy`, `EmitSeekPredicate`, `EmitMissingPrimaryFilter`, `ActiveKeyIndices`) verbatim — this task adds ONE new helper (`EmitOuterOrderByForIncludes`) and restructures `Emit.Run`'s `cteMatchPage`/`UNION ALL` branch to call them, following the exact "F1 invariant: one method renders this" discipline Task 2 established.

**A design simplification this task makes, not present in the design doc's own text — stated here so it isn't mistaken for an oversight**: the design doc's §4 describes a `CASE WHEN IsMatch = 1 THEN SortValue ELSE NULL END` wrapper for the outer `ORDER BY`, mirroring fhir-server's own generator. This plan omits it: because `IsMatch DESC` is already the FIRST (dominant) `ORDER BY` term, it alone fully partitions match rows before include rows; within the include partition every `SortValueN` column is uniformly `NULL` (a fixed tie contributing nothing to relative order), so a plain `ORDER BY IsMatch DESC, SortValue0 {dir}, ..., T1 ASC, Sid1 ASC` produces the identical result to the `CASE WHEN` form, with less code. fhir-server needs the `CASE WHEN` because its generator's sort state threads through differently; this compiler's `IsMatch`-first ordering makes it unnecessary here.

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

**Interfaces:**
- Consumes: `EmitSortJoins`, `EmitSortSelectColumns`, `EmitOrderBy`, `EmitSeekPredicate`, `EmitMissingPrimaryFilter`, `ActiveKeyIndices`, `SortSpec`, `PageSpec`, `SortPhase` (Task 2).
- Produces: the widened `(T1, Sid1, IsMatch, IsPartial, SortValue0, ..., SortValueN-1)` result shape whenever both `plan.Includes` and `plan.Sort` are non-null. Task 5's end-to-end tests are the primary consumer proving this composes correctly with real `IncludeStage`s.

- [ ] **Step 1: Add `EmitOuterOrderByForIncludes`**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, add (after `EmitOrderBy`, before `EmitSortSelectColumns`):

```csharp
    private static string EmitOuterOrderByForIncludes(SortSpec? sort)
    {
        var activeIndices = ActiveKeyIndices(sort);
        var terms = activeIndices.Select((idx, ordinal) =>
            $"SortValue{ordinal} {(sort!.Keys[idx].Direction == SortOrder.Ascending ? "ASC" : "DESC")}");
        return string.Join(", ", new[] { "IsMatch DESC" }.Concat(terms).Append("T1 ASC").Append("Sid1 ASC"));
    }
```

This is a DIFFERENT method from `EmitOrderBy` (Task 2) — `EmitOrderBy` references the inner join aliases (`sk0.Text`), valid only inside `cteMatchPage`'s own `SELECT`; `EmitOuterOrderByForIncludes` references the PROJECTED column names (`SortValue0`, `SortValue1`, ...) that only exist once `cteMatchPage`'s columns have been selected out into the final `UNION ALL`. Do not attempt to unify these into one method — they operate in genuinely different scopes.

- [ ] **Step 2: Restructure `Emit.Run`'s `cteMatchPage`/includes branch**

Change:

```csharp
        var matchJoin = plan.OuterPredicate is null
            ? string.Empty
            : $"\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1\n    WHERE {EmitPredicate(plan.OuterPredicate, parameters)}";
        cteBlocks.Add(
            $"cteMatchPage AS (\n" +
            $"    SELECT {top}m.T1, m.Sid1\n" +
            $"    FROM cte{plan.Match.Index} m{matchJoin}\n" +
            $")");

        for (var i = 0; i < includes.Count; i++)
        {
            var stage = includes[i];
            cteBlocks.Add($"inc{i} AS (\n{EmitIncludeStage(stage)}\n)");
            cteBlocks.Add(
                $"inc{i}lim AS (\n" +
                $"    SELECT TOP ({stage.Limit}) T1, Sid1,\n" +
                $"           CASE WHEN COUNT_BIG(*) OVER() > {stage.Limit} THEN 1 ELSE 0 END AS IsPartial\n" +
                $"    FROM inc{i}\n" +
                $")");
        }

        var unionBlocks = new List<string>
        {
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage",
        };
        for (var i = 0; i < includes.Count; i++)
        {
            unionBlocks.Add(
                $"SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc{i}lim i\n" +
                $"WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        }

        var includeSql = $";WITH {string.Join(",\n", cteBlocks)}\n" +
                          $"{string.Join("\nUNION ALL\n", unionBlocks)}\n" +
                          $"ORDER BY IsMatch DESC";

        return new EmittedSql(includeSql, parameters);
```

to:

```csharp
        var sortJoins = EmitSortJoins(plan.Sort);
        var sortColumns = EmitSortSelectColumns(plan.Sort);
        var activeSortKeyCount = ActiveKeyIndices(plan.Sort).Count;
        var cteOrderBy = $"\n    ORDER BY {EmitOrderBy(plan.Sort)}";

        var matchWhereClauses = new List<string>();
        if (plan.OuterPredicate is not null)
        {
            matchWhereClauses.Add(EmitPredicate(plan.OuterPredicate, parameters));
        }

        if (plan.Sort is { Phase: SortPhase.MissingPrimary } missingPhaseSort)
        {
            matchWhereClauses.Add(EmitMissingPrimaryFilter(missingPhaseSort));
        }

        if (plan.Page is { } page)
        {
            matchWhereClauses.Add(EmitSeekPredicate(plan.Sort, page, parameters));
        }

        var matchResourceJoin = plan.OuterPredicate is null
            ? string.Empty
            : "\n    INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1";
        var matchWhere = matchWhereClauses.Count > 0
            ? $"\n    WHERE {string.Join(" AND ", matchWhereClauses)}"
            : string.Empty;

        cteBlocks.Add(
            $"cteMatchPage AS (\n" +
            $"    SELECT {top}m.T1, m.Sid1{sortColumns}\n" +
            $"    FROM cte{plan.Match.Index} m{sortJoins}{matchResourceJoin}{matchWhere}{cteOrderBy}\n" +
            $")");

        for (var i = 0; i < includes.Count; i++)
        {
            var stage = includes[i];
            cteBlocks.Add($"inc{i} AS (\n{EmitIncludeStage(stage)}\n)");
            cteBlocks.Add(
                $"inc{i}lim AS (\n" +
                $"    SELECT TOP ({stage.Limit}) T1, Sid1,\n" +
                $"           CASE WHEN COUNT_BIG(*) OVER() > {stage.Limit} THEN 1 ELSE 0 END AS IsPartial\n" +
                $"    FROM inc{i}\n" +
                $")");
        }

        var nullSortColumns = string.Concat(Enumerable.Repeat(", NULL", activeSortKeyCount));
        var matchSortColumnRefs = string.Concat(Enumerable.Range(0, activeSortKeyCount).Select(o => $", SortValue{o}"));

        var unionBlocks = new List<string>
        {
            $"SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial{matchSortColumnRefs} FROM cteMatchPage",
        };
        for (var i = 0; i < includes.Count; i++)
        {
            unionBlocks.Add(
                $"SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial{nullSortColumns} FROM inc{i}lim i\n" +
                $"WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)");
        }

        var includeSql = $";WITH {string.Join(",\n", cteBlocks)}\n" +
                          $"{string.Join("\nUNION ALL\n", unionBlocks)}\n" +
                          $"ORDER BY {EmitOuterOrderByForIncludes(plan.Sort)}";

        return new EmittedSql(includeSql, parameters);
```

**This is a second universal, deliberate SQL-shape change, exactly like Task 2's — not scoped to sorted queries only.** Even with `plan.Sort` entirely null: `cteMatchPage` gains its own internal `ORDER BY m.T1 ASC, m.Sid1 ASC` (closing the same "every `TOP` needs an `ORDER BY`" gap Task 2 closed for the plain path — `cteMatchPage`'s `TOP` had none before this task), and the outer final `SELECT`'s `ORDER BY` widens from bare `IsMatch DESC` to `IsMatch DESC, T1 ASC, Sid1 ASC` (a real, deliberate tie-break addition, matching the Global Constraints' "always include the composite tie-break" rule). **Every existing include-path golden string in `EmitTests.cs`/`EndToEndCompilationTests.cs` (from the Phase 7 increment) needs updating for both of these additions** — this is the include-path counterpart to Task 2's Step 5, and uses the identical discipline: run the suite, confirm every failure is exactly this described shape change and nothing else, update the expected string, do not silently paper over an unexpected failure.

- [ ] **Step 3: Write the new sort+includes tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
    [Fact]
    public void GivenAnIncludeBearingPlanWithASortKey_WhenEmitted_ThenCteMatchPageCarriesTheSortJoinAndTheOuterUnionProjectsSortValueColumns()
    {
        // Arrange -- Patient?_sort=name&_include=Patient:organization.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var sort = new SortSpec([new SortKey(202, SortKeyKind.String, SortOrder.Ascending)], SortPhase.Valued);
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Sort: sort,
            Includes: [includeStage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT TOP (50) m.T1, m.Sid1, sk0.Text AS SortValue0\n" +
            "    FROM cte0 m\n" +
            "INNER JOIN dbo.StringSearchParam sk0\n" +
            "    ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "   AND sk0.SearchParamId = 202 AND sk0.IsMin = 1\n" +
            "    ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial, NULL FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC, SortValue0 ASC, T1 ASC, Sid1 ASC");
    }

    [Fact]
    public void GivenAnIncludeBearingPlanWithNoSort_WhenEmitted_ThenCteMatchPageStillGetsAnOrderByAndTheOuterOrderByGetsATieBreak()
    {
        // Arrange -- Patient?_include=Patient:organization, no _sort -- proves the universal
        // "every TOP needs an ORDER BY" invariant applies even when Sort is entirely absent.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var includeStage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [includeStage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "cteMatchPage AS (\n" +
            "    SELECT m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "    ORDER BY m.T1 ASC, m.Sid1 ASC\n" +
            ")");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, T1 ASC, Sid1 ASC");
    }
```

Add `using Ignixa.Search.Expressions;` to `EmitTests.cs` if not already present (it should already be from Task 2).

- [ ] **Step 4: Run the tests, fix every stale include-path golden string, confirm the new ones pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: FAILures at first, from every existing include-path golden string that assumed no `cteMatchPage` `ORDER BY` and a bare `ORDER BY IsMatch DESC` outer clause — update each, following Step 2's description of exactly what changed and why, exactly as Task 2's Step 7 handled the plain-path equivalent. Once every existing include-path test is updated, expect PASS across the board including the 2 new tests.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/Emit.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): thread sort decoration + result-shape widening through the cteMatchPage/includes path"
```

---

### Task 4: `Lower` translates `SortExpression` + `SortPhase` + `PageSpec` into `QueryPlan.Sort`/`Page`

Unlike Phase 7's `BuildIncludeStages`, this is NOT a topological-sort-shaped task — `SortPhase` is a caller input (design §1.2/§4: "the executor drives the phase-1→2 transition, as in fhir-server"), and `PageSpec` is passed straight through unmodified (its boundary values were already decoded from a continuation token and wrapped in `SqlParameterRef`s by the caller — `Lower` has no boundary-VALUE interpretation to do, only `SortExpression`-to-`SortKey` translation, cap/kind validation, and threading `PageSpec` onto the `QueryPlan`).

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`

**Interfaces:**
- Consumes: `SortKey`/`SortSpec`/`SortPhase`/`PageSpec` (Task 2). `SymbolTable.SearchParamId` (already exists).
- Produces: `Lower.Run(Expression? expression, SymbolTable symbols, string? targetResourceType, IReadOnlyList<IncludeExpression> includes, IReadOnlyList<IncludeExpression> revIncludes, int includeLimit, IReadOnlyList<SortExpression> sort, SortPhase sortPhase, PageSpec? page, int? top = null)`. Task 5's end-to-end tests are the primary consumer of the full pipeline.

- [ ] **Step 1: Widen `Lower.Run` and add `BuildSortSpec`/`BuildSortKey`**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, add `using Ignixa.Search.Models;` (for `SearchParamType`) if not already present.

Change `Run`'s signature and final `QueryPlan` construction:

```csharp
    public static QueryPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        int? top = null)
```

to:

```csharp
    public static QueryPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        IReadOnlyList<SortExpression> sort,
        SortPhase sortPhase,
        PageSpec? page,
        int? top = null)
```

Insert the new wildcard-compartment-search-with-sort guard immediately after the existing wildcard-compartment-search-with-includes guard, and pass the new `sortSpec`/`page` fields into the final `QueryPlan` construction:

```csharp
        if (targetResourceType is null && sort.Count > 0)
        {
            throw new NotSupportedException(
                "_sort combined with a wildcard compartment search (no single target resource type) is not " +
                "supported -- a SortSpec needs a single ResourceTypeId scope for its joins, the same reasoning " +
                "already established for typed leaves and _include/_revinclude under a null scope.");
        }

        IReadOnlyList<IncludeStage>? includeStages;
        if (targetResourceType is null)
        {
            if (includes.Count > 0 || revIncludes.Count > 0)
            {
                throw new NotSupportedException(
                    "_include/_revinclude combined with a wildcard compartment search (no single target resource " +
                    "type) is not supported -- BuildIncludeStages needs a concrete match resource type to compute " +
                    "SeedFromMatch.");
            }

            includeStages = null;
        }
        else
        {
            includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        }

        var sortSpec = BuildSortSpec(sort, sortPhase, symbols);

        return new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages, sortSpec, page);
```

(The `if (targetResourceType is null && sort.Count > 0)` guard is a NEW block, inserted before the existing `IReadOnlyList<IncludeStage>? includeStages; if (targetResourceType is null) { ... }` block, which itself is UNCHANGED except for the trailing `return` line gaining `sortSpec, page` as two new trailing arguments.)

Add `BuildSortSpec`/`BuildSortKey` (after `BuildIncludeStages`'s closing brace, before `ResolveInclude`):

```csharp
    private static SortSpec? BuildSortSpec(IReadOnlyList<SortExpression> sort, SortPhase phase, SymbolTable symbols)
    {
        if (sort.Count == 0)
        {
            return null;
        }

        if (sort.Count > 3)
        {
            throw new NotSupportedException(
                $"_sort supports at most 3 keys this phase (got {sort.Count}) -- a cap on per-request join cost " +
                "and plan-shape risk, not an architectural limit. Rewrite the search to use 3 or fewer sort keys.");
        }

        var keys = sort.Select(s => BuildSortKey(s, symbols)).ToList();
        return new SortSpec(keys, phase);
    }

    private static SortKey BuildSortKey(SortExpression sortExpression, SymbolTable symbols)
    {
        if (sortExpression.Parameter.Code == "_lastUpdated")
        {
            return new SortKey(null, SortKeyKind.LastUpdated, sortExpression.SortOrder);
        }

        var kind = sortExpression.Parameter.Type switch
        {
            SearchParamType.String => SortKeyKind.String,
            SearchParamType.Date => SortKeyKind.Date,
            _ => throw new NotSupportedException(
                $"Sorting by a '{sortExpression.Parameter.Type}' search parameter ('{sortExpression.Parameter.Code}') " +
                "is not supported this phase -- only String, Date, and _lastUpdated sort keys are handled. " +
                "Token/Number/Quantity/Reference/Uri sort is deferred."),
        };

        var searchParamId = symbols.SearchParamId(sortExpression.Parameter);
        return new SortKey(searchParamId, kind, sortExpression.SortOrder);
    }
```

Update the class's XML doc comment: append, after the existing sentence ending "...nested inside an And alongside ordinary predicates.": ` As of Phase 8 part 2, SortExpression/SortPhase/PageSpec are also handled, via BuildSortSpec -- SortPhase is a caller input (the executor drives the two-phase transition, matching fhir-server's own model), not something Lower computes by inspecting the query.`

- [ ] **Step 2: Sweep every `Lower.Run` call site**

Run `grep -rn "Lower\.Run(" --include=*.cs .` from the repo root (as of this plan's writing: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`, `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs` — re-run this yourself, do not trust this list, since call sites may have shifted since this plan was written) and insert `sort: [], sortPhase: SortPhase.Valued, page: null,` immediately after the `includeLimit:` argument at every call site. Example transformation:

```csharp
// Before:
var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
// After:
var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, sort: [], sortPhase: SortPhase.Valued, page: null);
```

`sortPhase: SortPhase.Valued` is inert/unused whenever `sort` is empty — passing it is mechanical, not a meaningful choice for these call sites.

- [ ] **Step 3: Write the new tests**

Add to `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`:

```csharp
    [Fact]
    public void GivenASingleStringSortKey_WhenLowered_ThenPlanSortHasTheResolvedSearchParamId()
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
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Sort.ShouldNotBeNull();
        plan.Sort!.Keys.Count.ShouldBe(1);
        plan.Sort.Keys[0].SearchParamId.ShouldBe((short)202);
        plan.Sort.Keys[0].Kind.ShouldBe(SortKeyKind.String);
        plan.Sort.Keys[0].Direction.ShouldBe(SortOrder.Ascending);
        plan.Sort.Phase.ShouldBe(SortPhase.Valued);
    }

    [Fact]
    public void GivenALastUpdatedSortKey_WhenLowered_ThenNoSearchParamIdIsRequested()
    {
        // Arrange -- symbols has no SearchParamId entry at all; must not throw.
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(lastUpdatedParam, SortOrder.Descending)], sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Sort!.Keys[0].SearchParamId.ShouldBeNull();
        plan.Sort.Keys[0].Kind.ShouldBe(SortKeyKind.LastUpdated);
    }

    [Fact]
    public void GivenFourSortKeys_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange
        var p1 = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var p2 = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));
        var p3 = new SearchParameterInfo("gender", "gender", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-gender"));
        var lastUpdated = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [p1.Url.ToString()] = 1, [p2.Url.ToString()] = 2, [p3.Url.ToString()] = 3 },
            new Dictionary<string, short> { ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
                sort: [
                    new SortExpression(p1, SortOrder.Ascending),
                    new SortExpression(p2, SortOrder.Descending),
                    new SortExpression(p3, SortOrder.Ascending),
                    new SortExpression(lastUpdated, SortOrder.Descending),
                ],
                sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("at most 3 keys");
    }

    [Fact]
    public void GivenATokenSortKey_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- Token/Number/Quantity/Reference/Uri sort is deferred, not silently mishandled.
        var statusParam = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [statusParam.Url.ToString()] = 1 },
            new Dictionary<string, short> { ["Observation"] = 104 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                expression: null, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(statusParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("Token");
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithASortKey_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/*?_sort=name -- no single resource type to scope the sort join against.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(
                compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0,
                sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null))
            .Message.ShouldContain("wildcard compartment search");
    }
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all new tests, and every existing `LowerTests`/`EndToEndCompilationTests`/`CompiledSearchEndToEndTests` case with its mechanically-updated call site.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs
git commit -m "feat(search-sql): Lower translates SortExpression/SortPhase/PageSpec into QueryPlan.Sort/Page"
```

---

### Task 5: End-to-end compilation tests (`Resolve` → `Lower` → `Emit`, sort+includes+compartment composability)

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: nothing new — pure proof, composing the full pipeline the way Phase 9's DataLayer wiring eventually will, and directly proving the design doc §4 bug-avoidance claims (not just "it compiles").

- [ ] **Step 1: Read the file's existing test pattern**

Open `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, confirm its `FakeSymbolResolver`/`FakeCompartmentDefinitionManager`/`FakeSearchParameterDefinitionManager` private nested classes (added by the Phase 8 part 1 plan — reuse them, do not create new ones), and find its most recent Phase-8-part-1 test for structural reference.

- [ ] **Step 2: Write the sorted-search end-to-end test**

```csharp
    [Fact]
    public async Task GivenAPatientSearchSortedByName_WhenCompiledEndToEnd_ThenTheMatchGainsAnIsMinJoinAndAnOrderBy()
    {
        // Arrange -- Patient?name=Smith&_sort=name, first page.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbols = await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(
            predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null, top: 10);

        // Assert
        plan.Explain().ShouldContain("sort = SortSpec([String:202 ASC], Valued)");
        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }
```

- [ ] **Step 3: Write the sort+includes composability test**

```csharp
    [Fact]
    public async Task GivenAPatientSearchSortedByNameWithAnInclude_WhenCompiledEndToEnd_ThenIncludeStageMachineryIsUnchangedAndTheOuterUnionCarriesTheSortValue()
    {
        // Arrange -- Patient?_sort=name&_include=Patient:organization, proving §4's "IncludeStage
        // needs zero changes" composability claim through the real pipeline, not just Emit in isolation.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = await Resolve.RunAsync(
            expression: null, includes: [include], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null, top: 50);

        // Assert -- IncludeStage's own fields are exactly what Phase 7 already produces; no new field.
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].SeedFromMatch.ShouldBeTrue();
        plan.Includes[0].SeedStages.ShouldBeEmpty();

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("cteMatchPage AS (");
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain(
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial, SortValue0 FROM cteMatchPage");
        emitted.Sql.ShouldContain("SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial, NULL FROM inc0lim i");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC, SortValue0 ASC, T1 ASC, Sid1 ASC");
    }
```

- [ ] **Step 4: Write the multi-key mixed-direction end-to-end test**

```csharp
    [Fact]
    public async Task GivenAPatientSearchSortedByNameAscendingThenBirthdateDescending_WhenCompiledEndToEnd_ThenBothKeysAppearWithTheCorrectJoinTypesAndDirections()
    {
        // Arrange -- Patient?_sort=name,-birthdate.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var birthDateParam = new SearchParameterInfo("birthdate", "birthdate", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Patient-birthdate"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[birthDateParam.Url!.ToString()] = 303;
        resolver.ResourceTypeIds["Patient"] = 103;

        var sortExpressions = new List<SortExpression>
        {
            new(nameParam, SortOrder.Ascending),
            new(birthDateParam, SortOrder.Descending),
        };

        // Act
        var symbols = await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: sortExpressions, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: sortExpressions, sortPhase: SortPhase.Valued, page: null);

        // Assert
        plan.Sort!.Keys.Count.ShouldBe(2);
        plan.Sort.Keys[0].Kind.ShouldBe(SortKeyKind.String);
        plan.Sort.Keys[1].Kind.ShouldBe(SortKeyKind.Date);
        plan.Sort.Keys[1].Direction.ShouldBe(SortOrder.Descending);

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain("LEFT JOIN dbo.DateTimeSearchParam sk1");
        emitted.Sql.ShouldContain("sk1.IsMax = 1");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, ISNULL(sk1.StartDateTime, '0001-01-01T00:00:00.0000000') DESC, m.T1 ASC, m.Sid1 ASC");
    }
```

- [ ] **Step 5: Write the compartment+sort composability test**

```csharp
    [Fact]
    public async Task GivenACompartmentSearchSortedByName_WhenCompiledEndToEnd_ThenTheSortDecorationComposesWithTheCompartmentUnionRoot()
    {
        // Arrange -- GET /Patient/123/Observation?_sort=name -- proves the #5672-class fhir-server bug
        // (SMART compartment + _sort by a parameter returning empty results) does not apply here: a
        // compartment match root is just another Union CteRef, sort-agnostic, composed for free.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Observation-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Observation", CancellationToken.None, compartmentManager, searchParamManager);
        var plan = Lower.Run(
            compartment, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.Valued, page: null);

        // Assert -- the match is the compartment's own Union; sort still decorates cleanly on top.
        plan.Ctes[plan.Match.Index].ShouldBeOfType<CteDefinition.Union>();
        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("sk0.IsMin = 1");
        emitted.Sql.ShouldContain("ORDER BY sk0.Text ASC, m.T1 ASC, m.Sid1 ASC");
    }
```

- [ ] **Step 6: Write the missing-primary-phase second-page end-to-end test**

```csharp
    [Fact]
    public async Task GivenTheMissingPrimaryPhaseWithAPageBoundary_WhenCompiledEndToEnd_ThenTheSeekPredicateIsSidOnly()
    {
        // Arrange -- Patient?_sort=name, second (missing-name) phase, resuming after a prior page.
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbols = await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [], sort: [new SortExpression(nameParam, SortOrder.Ascending)],
            resolver, targetResourceType: "Patient", CancellationToken.None);
        var page = new PageSpec([], new SqlParameterRef((short)103), new SqlParameterRef(7000L));
        var plan = Lower.Run(
            expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0,
            sort: [new SortExpression(nameParam, SortOrder.Ascending)], sortPhase: SortPhase.MissingPrimary, page: page, top: 10);

        // Assert
        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldNotContain("INNER JOIN dbo.StringSearchParam sk0");
        emitted.Sql.ShouldContain("WHERE NOT EXISTS (SELECT 1 FROM dbo.StringSearchParam s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = 202)");
        emitted.Sql.ShouldContain("(m.T1 = @p0 AND m.Sid1 > @p1)");
        emitted.Sql.ShouldContain("(m.T1 > @p0)");
    }
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all 5 new end-to-end tests plus the entire pre-existing suite.

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "test(search-sql): prove sort composes with includes and compartment search end to end"
```

---

### Task 6: Combined proof + full regression + final whole-branch review prep

**Files:** none (verification only), plus a roadmap doc update.

**Interfaces:**
- Consumes: everything from Tasks 1-5.
- Produces: a clean `dotnet build All.sln` / `dotnet test All.sln` baseline and a review package for the final whole-branch review.

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full solution test**

Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"`
Expected: all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework).

- [ ] **Step 3: Re-read the design doc's §7 "explicitly deferred" list and confirm nothing in this plan silently attempted any of it**

Confirm: no `$includes`-operation continuation-mechanism code was added; the live executor's fallback-ordering inconsistency was not touched; no instance-level SMART/compartment filter was added; `IncludeStage.Direction`'s dual-source-of-truth risk and the compartment nested-`And` gap were not touched. Grep the diff for `HttpContext`, `IncludesContinuationToken`, `OutputScopeFilter` to confirm zero scope creep.

- [ ] **Step 4: Update the roadmap doc**

In `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`, add a new paragraph after the ninth increment's (Phase 8 part 1 compartment) write-up, following that paragraph's exact narrative style/detail level: summarize what shipped (keyset/seek pagination replacing OFFSET, the `IsMin`/`IsMax`-based single-key shape, the sentinel-substitution multi-key design capped at 3 keys, the `cteMatchPage` sort composability with includes, the universal "every `TOP` needs an `ORDER BY`" invariant closing a real live gap). Mark Phase 8 (both parts) **Complete**, and explicitly note **Checkpoint 1.5 is now reached** — the roadmap's own gate: do not proceed into Phase 9 (DataLayer wiring) without an explicit go/no-go review of Phases 1-8, which this task does not itself perform (that review is the next, separate step after this plan's own final whole-branch review, per the roadmap's own instruction).

- [ ] **Step 5: Prepare the final whole-branch review package**

Follow `superpowers:subagent-driven-development`'s final-review step: run `scripts/review-package MERGE_BASE HEAD` (from that skill's directory; `MERGE_BASE` = `git merge-base feature/fhir-to-sql-compiler HEAD` if this plan executed on a dedicated worktree branch off `feature/fhir-to-sql-compiler`) and dispatch the final whole-branch reviewer on the most capable available model, per that skill's Model Selection section. This is the highest-stakes final review of the whole roadmap so far (Checkpoint 1.5 depends on it) — the dispatch should explicitly ask the reviewer to hunt for exactly the bug classes design §1.3/§4 named (unordered `TOP`, unstated ordering assumptions, phase/includes state leaking across request boundaries) and to independently re-verify the F1 invariant (identical `ORDER BY`/seek expressions) by reading `Emit.cs` directly, not just trusting that the tests pass.

- [ ] **Step 6: Report to the user before merging or pushing**

Summarize what shipped, what's explicitly still deferred (§7's list), and that Checkpoint 1.5 is reached — ask explicitly whether the user wants to proceed to the go/no-go review of Phases 1-8 next, or pause here. Ask before merging into `feature/fhir-to-sql-compiler` and again before pushing — matching every prior increment's established pattern on this branch.
