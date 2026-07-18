# Include, Reverse Include, and `:iterate` (Phase 7) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `_include`, `_revinclude` (both directions, specific reference param or wildcard), and `:iterate`/`:recurse` (single-hop-per-expression, topologically ordered) in `Ignixa.Search.Sql`, including SQL-level truncation (`TOP(@Limit+1)` + `IsPartial`) and the `cteMatchPage` result-shape change that include-bearing queries require.

**Architecture:** `IncludeExpression` (already ported from fhir-server, already computing `Requires`/`Produces`) never reaches the ordinary `Expression` tree, so `Resolve` is widened to accept `_include`/`_revinclude` lists directly. `Lower` runs Kahn's algorithm over the `:iterate` subset (non-iterate stages keep query-parameter order) and reifies every dependency as data — a new `IncludeStage` record whose `SeedStages` field is a list of plain integer indices into `QueryPlan.Includes`, exactly analogous to how `CteRef` indices work for `QueryPlan.Ctes`. This is deliberately *not* a `CteDefinition` — includes stay outside the `QueryPlan.Ctes` graph per the original design doc's "includes are not predicates" decision. `Emit` gains a per-stage renderer (`EmitIncludeStage`, mirroring `EmitChainJoin`'s literal-only-ids precedent) and, only when `plan.Includes` is non-empty, restructures its top-level `SELECT` to materialize a `cteMatchPage` CTE that every include stage seeds from, plus one `incN`/`incNlim` CTE pair per stage, unioned into a `(T1, Sid1, IsMatch, IsPartial)` result. Plain (no-include) queries keep today's shape byte-identical — this is the single hardest invariant this plan enforces, and every task touching `Emit` is written to prove it by construction, not by inspection.

**Tech Stack:** C# / .NET 9+, xUnit + Shouldly, `Ignixa.Search.Sql` (Core-tier, no EF/ASP.NET references).

**Full design:** `docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-include-design.md` — read this first for the *why* behind every task below; this plan only covers the *what* and *how*, task by task. Section references (§N) below refer to that document. It went through two rounds of adversarial review on the Fable model (a draft-design research pass against real fhir-server source, then a fresh-pass review of the written spec that caught and fixed a real load-bearing SQL-projection bug — an earlier draft of §2's Forward SQL had the SELECT list swapped with Reverse's) before this plan was written from it.

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework) are pre-existing and out of scope, per every prior increment on this branch.
- **`IncludeDirection` is a distinct enum from `ChainDirection`** (design §1.2) — do not reuse `ChainDirection`. The polarity is inverted from a naive "same as chain" reading: forward `_include`'s known/seed set is the *referencing* side (already in the result set), which is the same join shape `ChainJoin.Reverse` emits; `_revinclude`'s known/seed set is the *referenced* side, which is `ChainJoin.Forward`'s shape. Every task touching `Emit`'s Forward/Reverse SQL must be checked against this inverted mapping, not against `ChainJoin`'s naming by feel — this is the single easiest place this phase could ship a silently-swapped emission (same bug class the Phase 6 final review hunted for in reverse chain's field mapping).
- `IncludeStage` (§2), exact field order:
  ```csharp
  public sealed record IncludeStage(
      IncludeDirection Direction,
      short? ReferenceSearchParamId,
      IReadOnlyList<short>? SeedTypeIds,
      IReadOnlyList<short>? OutputTypeIds,
      IReadOnlyList<int> SeedStages,
      bool SeedFromMatch,
      bool Iterate,
      int Limit);
  ```
  `ReferenceSearchParamId = null` means wildcard (no `SearchParamId` filter emitted). `SeedTypeIds = null` means unconstrained on the seed side (only ever happens on the defensive/unreachable path — see Task 3). `OutputTypeIds = null` means wildcard on the produced side — this is the real, reachable case for `_revinclude`'s `*:*` wildcard-source form (design §1.2), and is the one place a `null` list carries FHIR meaning ("matches any type"), not merely "nothing to filter." `SeedStages` holds indices into `QueryPlan.Includes` (not `QueryPlan.Ctes` — a completely separate index space) that this stage's `EXISTS` also seeds from, in addition to `cteMatchPage` when `SeedFromMatch` is true.
- `QueryPlan` gains a fifth field: `IReadOnlyList<IncludeStage>? Includes = null` (nullable, trailing, defaulted — `= []` is not legal C# here, matching why `Top`/`OuterPredicate` are already `?`-typed with `null` defaults). Every consumer treats `plan.Includes is not { Count: > 0 }` as "no includes." This is purely additive — every existing positional `QueryPlan` construction site across the whole test suite is unaffected.
- **Type filters on `IncludeStage` render as literal `OR`-chains, never bound `@pN` parameters** — matching `ChainJoin.OutputResourceTypeIds`'s existing precedent (chain design doc §3's note on why building a real `Predicate.Equal`/`Or` here would wrongly force a bound parameter through `EmitPredicate`) and `PlanExplainer`'s parameter-ordinal invariant. `ReferenceSearchParamId` and `Limit` also render as literals. None of `IncludeStage`'s fields ever touch `EmittedSqlParameter`/`parameters` — `EmitIncludeStage` takes no `parameters` argument, unlike every other `Emit*` helper.
- **`Lower.Run`'s widened signature** (design §5), the ONE signature every call site converts to (no overload — this project's established precedent for a required-parameter-set change is a mechanical full-suite sweep, not preserving a second signature; see the chain plan's Task 1 `targetResourceType` precedent):
  ```csharp
  public static QueryPlan Run(
      Expression? expression,
      SymbolTable symbols,
      string targetResourceType,
      IReadOnlyList<IncludeExpression> includes,
      IReadOnlyList<IncludeExpression> revIncludes,
      int includeLimit,
      int? top = null)
  ```
  `expression` is now nullable — an include-only search (e.g. `Patient?_include=Patient:organization`, no other filter) has no ordinary match expression; `Lower` already has the `LowerResourceSource` fallback used today for resource-column-only queries, and this phase's null-expression path reuses that exact fallback. `includeLimit` is read only when `includes`/`revIncludes` are non-empty — callers with no includes may pass `0`, it is otherwise inert.
- **`Resolve.RunAsync`'s widened signature**, inserting the new parameters immediately after `expression` (the object being widened), before the unchanged `resolver`/`targetResourceType`/`cancellationToken` tail:
  ```csharp
  public static async Task<SymbolTable> RunAsync(
      Expression? expression,
      IReadOnlyList<IncludeExpression> includes,
      IReadOnlyList<IncludeExpression> revIncludes,
      ISymbolResolver resolver,
      string targetResourceType,
      CancellationToken cancellationToken)
  ```
  `expression` is nullable here too, for the same include-only-search reason. `ArgumentNullException.ThrowIfNull(expression)` is removed; `includes`/`revIncludes` get `ArgumentNullException.ThrowIfNull` instead (they are never legitimately null — an include-free search passes an empty list, matching how `SearchOptions.Include`/`RevInclude` are typed today).
- **Direct consequence:** every existing call to `Lower.Run(...)` and `Resolve.RunAsync(...)` in `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`, `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`, and `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (68 call sites total: 34 to `Lower.Run`, 34 to `Resolve.RunAsync`, across those three files) will not compile until updated to pass `includes: [], revIncludes: [], includeLimit: 0` (or real values, for this phase's own new tests). Tasks 1 and 3 each own their half of this sweep (Task 1: `Resolve.RunAsync`'s 34 call sites; Task 3: `Lower.Run`'s 34 call sites) — do not attempt to preserve compilation by re-adding defaults or an overload.
- **Every `Emit`/`PlanExplainer` change in this plan must leave every currently-passing golden string in `EmitTests.cs`/`PlanExplainerTests.cs`/`EndToEndCompilationTests.cs` byte-identical when `plan.Includes` is null or empty** — verified by construction (the no-includes code path is preserved verbatim inside an early return, not rewritten), not merely by re-running the suite and hoping. Task 2's steps say exactly where this early return goes.
- Kahn's algorithm (design §4) needs a **deterministic tie-break** among simultaneously-ready nodes: lowest original-list index wins. Without this, `Explain()` golden strings would be nondeterministic across otherwise-identical inputs, breaking this project's golden-string testing discipline.
- A genuine cycle between two or more *distinct* `:iterate` expressions throws `NotSupportedException` (design §4.3) — not a silent wrong answer, not an infinite compile loop. A single self-referential iterate (e.g. `Observation:has-member:iterate` pointing at `Observation`) is not a cycle for this purpose and compiles to exactly one hop.
- An iterate stage whose `Requires` intersects neither any predecessor's `Produces` nor the match page's own type (`SeedStages = []` AND `SeedFromMatch = false`) is the degenerate case (design §2) — `Lower` drops it entirely (it can never produce any rows) rather than emit an unrenderable zero-branch `EXISTS`, matching this project's "fail at Lower time, not let SQL Server discover it" principle already established for `ChainJoin`'s empty-`OutputResourceTypeIds` case.
- The literal string `"*"` appearing in an `IncludeExpression.Produces`/`Requires` collection (design §1.2: only reachable via `Produces` for `_revinclude`'s `*:*` wildcard-source form, `IncludeExpression.SourceResourceType == "*"`) is a sentinel meaning "matches any resource type," not a real resource-type name — it must never be passed to `ISymbolResolver.GetResourceTypeIdAsync`, and a stage whose `OutputTypeIds` is `null` for this reason must satisfy every downstream `Requires` in the Kahn edge computation (`x.Produces ∩ y.Requires ≠ ∅`), not compute zero edges for it.
- `_sort`/continuation-token interaction with includes, instance-level SMART/compartment filtering (the `OutputScopeFilter` seat, design §6), and true multi-level `:iterate` recursion beyond topological ordering of separately-specified `:iterate` parameters are explicitly out of scope for this plan (design §7) — nothing in this plan should throw a DIFFERENT exception for these than whatever the existing code already produces for unhandled shapes.

---

### Task 1: Widen `Resolve`/`SymbolCollectingVisitor` to resolve `IncludeExpression` lists directly

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: nothing new from earlier tasks (this is the foundational task, mirroring how chain's `SymbolCollectingVisitor.VisitChained` (chain plan Task 6) landed before `ChainJoin`'s AST).
- Produces: `Resolve.RunAsync(Expression? expression, IReadOnlyList<IncludeExpression> includes, IReadOnlyList<IncludeExpression> revIncludes, ISymbolResolver resolver, string targetResourceType, CancellationToken cancellationToken)`. `SymbolCollectingVisitor.CollectInclude(IncludeExpression include)` — a public method (not a `VisitInclude` override; `IncludeExpression` never appears on the `Expression` tree `AcceptVisitor` walks, per design §1.1, so there is nothing for a visitor override to dispatch from). Later tasks call `Resolve.RunAsync` with real `includes`/`revIncludes` lists; Task 4's end-to-end tests are the first callers to pass non-empty ones.

- [ ] **Step 1: Add `SymbolCollectingVisitor.CollectInclude`**

In `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`, add (after the existing `VisitChained` override):

```csharp
    /// <summary>
    /// Collects the symbols an <see cref="IncludeExpression"/> references -- its own
    /// <c>ReferenceSearchParameter</c> (when not a wildcard), and every resource type appearing in
    /// <c>SourceResourceType</c>, <c>TargetResourceType</c>, <c>ReferenceSearchParameter.TargetResourceTypes</c>,
    /// and <c>ReferencedTypes</c>. This over-collects relative to what <c>Requires</c>/<c>Produces</c>
    /// actually uses for any one <see cref="IncludeExpression"/> instance (their exact source field
    /// depends on which of <c>TargetResourceType</c>/<c>ReferenceSearchParameter.TargetResourceTypes</c>/
    /// <c>WildCard</c> is populated) -- resolving a superset is safe, matching <see cref="VisitChained"/>'s
    /// existing precedent of collecting both <c>ResourceTypes</c> and <c>TargetResourceTypes</c> rather than
    /// re-deriving which one a given chain direction actually needs. Not a visitor override:
    /// <see cref="IncludeExpression"/> lives on <c>SearchOptions.Include</c>/<c>RevInclude</c>, never on the
    /// <see cref="Expression"/> tree this visitor walks, so <c>Resolve</c> calls this directly per include.
    /// The literal sentinel string "*" (a <c>_revinclude</c> wildcard-source's <c>SourceResourceType</c>,
    /// design doc §1.2) is skipped, never added as a resource type to resolve.
    /// </summary>
    public void CollectInclude(IncludeExpression include)
    {
        if (include.ReferenceSearchParameter is not null)
        {
            Parameters.Add(include.ReferenceSearchParameter);
            foreach (var targetType in include.ReferenceSearchParameter.TargetResourceTypes)
            {
                AddResourceType(targetType);
            }
        }

        AddResourceType(include.SourceResourceType);
        AddResourceType(include.TargetResourceType);
        foreach (var referencedType in include.ReferencedTypes ?? [])
        {
            AddResourceType(referencedType);
        }
    }

    private void AddResourceType(string? resourceType)
    {
        if (resourceType is { Length: > 0 } and not "*")
        {
            ResourceTypes.Add(resourceType);
        }
    }
```

Add `using Ignixa.Search.Expressions;` if not already present (it is — `IncludeExpression` lives in the same namespace as `ChainedExpression`, already imported).

Update the class's XML doc `<remarks>` (currently ending with "...Compartment target-type resolution remains Phase 8's job. See Resolve's remarks for the full argument.") to add, before that final sentence: `As of Phase 7, <see cref="CollectInclude"/> collects an IncludeExpression's own symbols the same way -- not via a visitor override (IncludeExpression is never part of this Expression tree), but as a direct method Resolve calls once per include/revinclude entry.`

- [ ] **Step 2: Widen `Resolve.RunAsync`**

In `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`, replace the method body:

```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression expression,
        ISymbolResolver resolver,
        string targetResourceType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(targetResourceType);

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
        }

        var resourceTypes = new HashSet<string>(collector.ResourceTypes);
        resourceTypes.Add(targetResourceType);

        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            if (id.HasValue)
            {
                resourceTypeIds[resourceType] = id.Value;
            }
        }

        return new SymbolTable(searchParamIds, resourceTypeIds);
    }
```

with:

```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        ISymbolResolver resolver,
        string targetResourceType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(includes);
        ArgumentNullException.ThrowIfNull(revIncludes);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(targetResourceType);

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
        resourceTypes.Add(targetResourceType);

        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            if (id.HasValue)
            {
                resourceTypeIds[resourceType] = id.Value;
            }
        }

        return new SymbolTable(searchParamIds, resourceTypeIds);
    }
```

Update the `<remarks>` paragraph that begins "Resource-type resolution is out of scope..." to add, after the existing sentence ending "...a `ChainedExpression`'s `ReferenceSearchParameter` and both its `ResourceTypes`/`TargetResourceTypes` arrays.": `As of Phase 7, resolution also extends to every <see cref="IncludeExpression"/> passed via the includes/revIncludes parameters -- see SymbolCollectingVisitor.CollectInclude's remarks for the exact fields collected.`

- [ ] **Step 3: Sweep `ResolveTests.cs`'s 7 call sites**

Every `Resolve.RunAsync(someExpression, resolver, "SomeType", CancellationToken.None)` call in `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs` becomes `Resolve.RunAsync(someExpression, includes: [], revIncludes: [], resolver, "SomeType", CancellationToken.None)`. Example (the first call site, `GivenATreeWithOnePredicate_WhenResolved_ThenSymbolTableHasItsSearchParamId`):

```csharp
        // Act
        var symbolTable = await Resolve.RunAsync(predicate, includes: [], revIncludes: [], resolver, "Patient", CancellationToken.None);
```

Apply this exact transformation (insert `includes: [], revIncludes: [],` immediately after the expression argument) to all 7 call sites in this file. None of these tests need real includes — Task 4 adds the first tests that do.

- [ ] **Step 4: Sweep `EndToEndCompilationTests.cs`'s `Resolve.RunAsync` call sites**

`test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` has 27 calls to `Resolve.RunAsync`. Apply the identical transformation from Step 3 to every one. This file also calls `Lower.Run` in the same test bodies — do not touch those calls yet (Task 3 owns that sweep); only insert `includes: [], revIncludes: [],` into the `Resolve.RunAsync` calls in this step.

- [ ] **Step 5: Add a `CollectInclude` unit test**

In `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`, add:

```csharp
    [Fact]
    public async Task GivenAForwardIncludeExpression_WhenResolved_ThenSymbolTableHasItsReferenceParamAndBothResourceTypes()
    {
        // Arrange -- Patient?_include=Patient:organization
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(
            resourceTypes: ["Patient"],
            referenceSearchParameter: orgParam,
            sourceResourceType: "Patient",
            targetResourceType: "Organization",
            referencedTypes: null,
            wildCard: false,
            reversed: false,
            iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(
            expression: null, includes: [include], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(orgParam).ShouldBe((short)55);
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Organization").ShouldBe((short)105);
    }

    [Fact]
    public async Task GivenARevincludeWildcardSourceExpression_WhenResolved_ThenTheStarSentinelIsNeverPassedToTheResolver()
    {
        // Arrange -- Patient?_revinclude=*:* -- SourceResourceType is the literal sentinel "*"
        // (design doc §1.2); CollectInclude must skip it, not call GetResourceTypeIdAsync("*").
        var include = new IncludeExpression(
            resourceTypes: ["*"],
            referenceSearchParameter: null,
            sourceResourceType: "*",
            targetResourceType: "Patient",
            referencedTypes: ["Observation", "Condition"],
            wildCard: true,
            reversed: true,
            iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.ResourceTypeIds["Condition"] = 106;

        // Act -- must not throw even though the resolver has no row for "*"
        var symbolTable = await Resolve.RunAsync(
            expression: null, includes: [], revIncludes: [include], resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)104);
        symbolTable.ResourceTypeId("Condition").ShouldBe((short)106);
        Should.Throw<KeyNotFoundException>(() => symbolTable.ResourceTypeId("*"));
    }
```

Add `using Ignixa.Search.Expressions;` to the top of `ResolveTests.cs` if not already present (it is, for `SearchParameterPredicateExpression` etc.).

- [ ] **Step 5: Run the test suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, including the 2 new tests and all previously-passing `ResolveTests`/`EndToEndCompilationTests` cases (with their mechanically-updated call sites).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): widen Resolve to collect IncludeExpression symbols"
```

---

### Task 2: `IncludeDirection` + `IncludeStage` AST, `Emit` (per-stage rendering + `cteMatchPage` top-level restructuring), `PlanExplainer` — AST-only, no lowering rule yet

This task has no dependency on Task 1 and could be executed in parallel by a different implementer, but is sequenced second here for narrative clarity. It mirrors the chain plan's Task 7 ("`ChainJoin` CteDefinition + `Emit` + `PlanExplainer` — AST-only, no lowering rule yet"), with one addition chain never needed: because `IncludeStage` requires `Emit.Run`'s top-level `SELECT` to restructure (not just another `cte{i}` block), and this codebase's established testing convention is to exercise `Emit`'s private helpers only through the public `Emit.Run(plan)` entry point (never via direct calls to private methods like `EmitParamSource`/`EmitChainJoin`), the per-stage renderer and the top-level restructuring must land together — testing one without the other is not possible without breaking that convention.

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Ast/IncludeDirection.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/IncludeStage.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `IncludeDirection { Forward, Reverse }`. `IncludeStage` (exact shape in Global Constraints). `QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null, Predicate? OuterPredicate = null, IReadOnlyList<IncludeStage>? Includes = null)`. `Emit.Run` renders a `(T1, Sid1, IsMatch, IsPartial)`-shaped result whenever `plan.Includes is { Count: > 0 }`, unchanged `(T1, Sid1)` otherwise. Task 3 constructs `IncludeStage` instances from real `IncludeExpression`s and hands them to this same `QueryPlan.Includes`/`Emit.Run`/`PlanExplainer.Print` machinery unmodified.

- [ ] **Step 1: `IncludeDirection`**

Create `src/Core/Ignixa.Search.Sql/Ast/IncludeDirection.cs`:

```csharp
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of an IncludeStage's dbo.ReferenceSearchParam row is the "known"/seed side (already in
/// the result set, correlated against cteMatchPage or a predecessor stage) versus the "produced" side
/// (translated via dbo.Resource, or selected directly). A DISTINCT enum from ChainDirection -- the
/// polarity is inverted: forward `_include`'s known side is the referencing resource (already
/// matched), which is the SAME join shape ChainJoin.Reverse emits; `_revinclude`'s known side is the
/// referenced resource, which is ChainJoin.Forward's shape. See
/// docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-include-design.md §1.2.
/// Forward: known/seed side is rsp (the referencing resource, already a surrogate id); produced side
/// is r (the referenced resource, translated via dbo.Resource).
/// Reverse: known/seed side is r (the referenced resource, translated via dbo.Resource); produced side
/// is rsp (the referencing resource, selected directly).
/// </summary>
public enum IncludeDirection
{
    Forward,
    Reverse,
}
```

- [ ] **Step 2: `IncludeStage`**

Create `src/Core/Ignixa.Search.Sql/Ast/IncludeStage.cs`:

```csharp
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One `_include`/`_revinclude`/`:iterate` stage. Deliberately NOT a CteDefinition -- includes stay
/// outside QueryPlan.Ctes per the original design doc's "includes are not predicates" decision; a
/// stage is rendered as its own incN/incNlim CTE pair by Emit, indexed by its position in
/// QueryPlan.Includes (a separate index space from CteRef/QueryPlan.Ctes).
/// SeedStages holds indices into QueryPlan.Includes (never QueryPlan.Ctes) of every EARLIER stage
/// whose Produces intersects this stage's Requires -- populated by Lower's Kahn sort, the
/// load-bearing mechanism that lets Emit be a dumb renderer with no emitter-mutable registry to
/// maintain (contrast fhir-server's own _includeLimitCtesByResourceType). SeedFromMatch is true when
/// this stage ALSO seeds from cteMatchPage directly (every non-iterate stage; an iterate stage only
/// when its Requires intersects the match's own resource type). A stage with SeedStages = [] AND
/// SeedFromMatch = false is unreachable and never constructed -- see Lower's degenerate-case handling
/// (design doc §2).
/// See docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-include-design.md §2.
/// </summary>
public sealed record IncludeStage(
    IncludeDirection Direction,
    short? ReferenceSearchParamId,
    IReadOnlyList<short>? SeedTypeIds,
    IReadOnlyList<short>? OutputTypeIds,
    IReadOnlyList<int> SeedStages,
    bool SeedFromMatch,
    bool Iterate,
    int Limit);
```

- [ ] **Step 3: `QueryPlan.Includes`**

In `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`, change:

```csharp
public sealed record QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null, Predicate? OuterPredicate = null)
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
    IReadOnlyList<IncludeStage>? Includes = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
```

Update the class's XML doc comment: replace the final sentence `"IncludeStage/SortSpec/full PageSpec (tier-3 result-shape stages) are not included yet -- nothing in scope here produces or consumes them."` with `"Includes (Phase 7) is the first tier-3 result-shape field -- non-null and non-empty only for queries with _include/_revinclude/:iterate; Emit materializes a cteMatchPage CTE and a (T1, Sid1, IsMatch, IsPartial) result shape only in that case, leaving every plan with no Includes byte-identical to before this field existed. SortSpec/full PageSpec remain out of scope."`

- [ ] **Step 4: `Emit.EmitIncludeStage` and its helpers**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, add these private static methods (placed after `EmitResourceSource`, before `EmitPredicate`):

```csharp
    private static string EmitIncludeStage(IncludeStage stage)
    {
        var (selectColumns, seedTypeColumn, outputTypeColumn, seedCorrelationAlias) = stage.Direction switch
        {
            IncludeDirection.Forward => ("r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1", "rsp.ResourceTypeId", "r.ResourceTypeId", "rsp"),
            IncludeDirection.Reverse => ("rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1", "r.ResourceTypeId", "rsp.ResourceTypeId", "r"),
            _ => throw new NotSupportedException($"Unknown IncludeDirection '{stage.Direction}'."),
        };

        var whereClauses = new List<string>();
        if (stage.ReferenceSearchParamId is { } paramId)
        {
            whereClauses.Add($"rsp.SearchParamId = {paramId}");
        }

        if (stage.SeedTypeIds is { Count: > 0 } seedTypeIds)
        {
            whereClauses.Add(EmitTypeInFilter(seedTypeColumn, seedTypeIds));
        }

        if (stage.OutputTypeIds is { Count: > 0 } outputTypeIds)
        {
            whereClauses.Add(EmitTypeInFilter(outputTypeColumn, outputTypeIds));
        }

        whereClauses.Add("rsp.BaseUri IS NULL");
        whereClauses.Add(EmitSeedExists(stage, seedCorrelationAlias));

        return $"    SELECT DISTINCT TOP ({stage.Limit + 1}) {selectColumns}\n" +
               $"    FROM dbo.ReferenceSearchParam rsp\n" +
               $"    INNER JOIN dbo.Resource r\n" +
               $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
               $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
               $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
               $"    WHERE {string.Join("\n      AND ", whereClauses)}";
    }

    private static string EmitTypeInFilter(string column, IReadOnlyList<short> typeIds)
    {
        var filter = string.Join(" OR ", typeIds.Select(id => $"{column} = {id}"));
        return typeIds.Count > 1 ? $"({filter})" : filter;
    }

    private static string EmitSeedExists(IncludeStage stage, string correlationAlias)
    {
        var branches = new List<string>();
        if (stage.SeedFromMatch)
        {
            branches.Add($"SELECT 1 FROM cteMatchPage m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        foreach (var seedStageIndex in stage.SeedStages)
        {
            branches.Add($"SELECT 1 FROM inc{seedStageIndex}lim m WHERE m.T1 = {correlationAlias}.ResourceTypeId AND m.Sid1 = {correlationAlias}.ResourceSurrogateId");
        }

        return $"EXISTS (\n        {string.Join("\n        UNION ALL\n        ", branches)}\n    )";
    }
```

`EmitTypeInFilter` deliberately reuses the same hand-rolled `OR`-chain pattern `EmitChainJoin`'s `outputFilter` already uses (not `Predicate.Or`, for the same reason documented on that method: every id here must render as a literal, and routing through `Predicate.Equal` would force a bound `@pN`).

- [ ] **Step 5: `Emit.Run`'s top-level restructuring**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, replace `Run`:

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

with:

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
    }
```

The `plan.Includes is not { Count: > 0 } includes` early-return branch is the ORIGINAL method body, character-for-character. This is what makes the "zero diff for no-includes plans" requirement true by construction: every existing `EmitTests`/`EndToEndCompilationTests` golden string exercises this exact, unmodified code path.

- [ ] **Step 6: `PlanExplainer` rendering**

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, change `Print`:

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

to:

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

        if (plan.Includes is { Count: > 0 } includes)
        {
            for (var i = 0; i < includes.Count; i++)
            {
                lines.Add($"inc{i} = {PrintIncludeStage(includes[i])}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string PrintIncludeStage(IncludeStage stage)
    {
        var refParam = stage.ReferenceSearchParamId is { } id ? $"{id}" : "*";
        var seedTypes = stage.SeedTypeIds is null ? "*" : $"[{string.Join(",", stage.SeedTypeIds)}]";
        var outputTypes = stage.OutputTypeIds is null ? "*" : $"[{string.Join(",", stage.OutputTypeIds)}]";
        var seedStageLabels = stage.SeedStages.Select(s => $"inc{s}");
        var seeds = stage.SeedFromMatch ? seedStageLabels.Prepend("match") : seedStageLabels;
        var iterate = stage.Iterate ? " iterate" : string.Empty;
        return $"IncludeStage(ref={refParam}, seedTypes={seedTypes}, outputTypes={outputTypes}, seeds=[{string.Join(",", seeds)}], limit={stage.Limit}{iterate}, {stage.Direction})";
    }
```

`PlanExplainer` does not attempt to describe the `cteMatchPage`/`IsMatch`/`IsPartial` result-shape change in text -- `Explain()`'s job is plan-shape summary (mirroring how it already doesn't describe `OuterPredicate`'s join mechanics beyond the trailing `WHERE ...`), not a literal SQL preview; `Emit.Run`'s XML doc (next step) is the place that documents the result-shape contract.

- [ ] **Step 7: Document the result-shape contract on `EmittedSql`**

In `src/Core/Ignixa.Search.Sql/Ast/EmittedSql.cs`, add an XML doc comment above `EmittedSql`:

```csharp
/// <summary>
/// Result shape is (T1, Sid1) for any QueryPlan with no Includes (the overwhelming majority).
/// Whenever plan.Includes is non-empty, the shape is (T1, Sid1, IsMatch, IsPartial) instead --
/// IsMatch distinguishes an ordinary match-page row (1) from an included row (0); IsPartial (only
/// ever 1 on an included row) means that stage's TOP(@Limit) truncated further rows. Callers key off
/// plan.Includes.Count > 0 to know which shape to expect, not by inspecting column count at runtime.
/// </summary>
public sealed record EmittedSql(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters);
```

- [ ] **Step 8: Write the failing tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
    [Fact]
    public void GivenAForwardIncludeStageSeededFromMatch_WhenEmitted_ThenProducesTheCteMatchPageShapeWithTheRAsideProjection()
    {
        // Arrange -- Patient?_include=Patient:organization, matching ChainJoin.Reverse's shape per
        // design doc §1.2: forward include's known side is rsp (already-matched Patient rows).
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward,
            ReferenceSearchParamId: 55,
            SeedTypeIds: [103],
            OutputTypeIds: [105],
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Top: 50,
            Includes: [stage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            "),\n" +
            "cteMatchPage AS (\n" +
            "    SELECT TOP (50) m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "),\n" +
            "inc0 AS (\n" +
            "    SELECT DISTINCT TOP (1001) r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE rsp.SearchParamId = 55\n" +
            "      AND rsp.ResourceTypeId = 103\n" +
            "      AND r.ResourceTypeId = 105\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )\n" +
            "),\n" +
            "inc0lim AS (\n" +
            "    SELECT TOP (1000) T1, Sid1,\n" +
            "           CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS IsPartial\n" +
            "    FROM inc0\n" +
            ")\n" +
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC");
        emitted.Parameters.Count.ShouldBe(1);
    }

    [Fact]
    public void GivenAReverseIncludeStage_WhenEmitted_ThenTheKnownSideIsTranslatedThroughDboResourceAndTheOutputSideIsSelectedDirectlyFromRsp()
    {
        // Arrange -- Patient?_revinclude=Observation:subject, matching ChainJoin.Forward's shape.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Reverse,
            ReferenceSearchParamId: 77,
            SeedTypeIds: [103],
            OutputTypeIds: [104],
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [stage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0\n" +
            "),\n" +
            "cteMatchPage AS (\n" +
            "    SELECT m.T1, m.Sid1\n" +
            "    FROM cte0 m\n" +
            "),\n" +
            "inc0 AS (\n" +
            "    SELECT DISTINCT TOP (1001) rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam rsp\n" +
            "    INNER JOIN dbo.Resource r\n" +
            "        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
            "       AND r.ResourceId = rsp.ReferenceResourceId\n" +
            "       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
            "    WHERE rsp.SearchParamId = 77\n" +
            "      AND r.ResourceTypeId = 103\n" +
            "      AND rsp.ResourceTypeId = 104\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId\n" +
            "    )\n" +
            "),\n" +
            "inc0lim AS (\n" +
            "    SELECT TOP (1000) T1, Sid1,\n" +
            "           CASE WHEN COUNT_BIG(*) OVER() > 1000 THEN 1 ELSE 0 END AS IsPartial\n" +
            "    FROM inc0\n" +
            ")\n" +
            "SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage\n" +
            "UNION ALL\n" +
            "SELECT i.T1, i.Sid1, CAST(0 AS bit), i.IsPartial FROM inc0lim i\n" +
            "WHERE NOT EXISTS (SELECT 1 FROM cteMatchPage m WHERE m.T1 = i.T1 AND m.Sid1 = i.Sid1)\n" +
            "ORDER BY IsMatch DESC");
    }

    [Fact]
    public void GivenAWildcardIncludeStage_WhenEmitted_ThenNoSearchParamIdFilterIsEmitted()
    {
        // Arrange -- Patient?_include=Patient:* -- ReferenceSearchParamId is null.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(
            IncludeDirection.Forward,
            ReferenceSearchParamId: null,
            SeedTypeIds: null,
            OutputTypeIds: null,
            SeedStages: [],
            SeedFromMatch: true,
            Iterate: false,
            Limit: 500);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [stage]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert -- no "rsp.SearchParamId = ", no type filters, straight to BaseUri + EXISTS
        emitted.Sql.ShouldContain(
            "    WHERE rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM cteMatchPage m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
        emitted.Sql.ShouldNotContain("SearchParamId = ", Case.Insensitive);
    }

    [Fact]
    public void GivenAnIterateStageSeededFromAPredecessorInclude_WhenEmitted_ThenTheExistsClauseUnionsBothBranches()
    {
        // Arrange -- inc1 seeds from BOTH cteMatchPage and inc0lim.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage0 = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var stage1 = new IncludeStage(IncludeDirection.Forward, 88, [105], [105], SeedStages: [0], SeedFromMatch: false, Iterate: true, Limit: 1000);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 103, 202, predicate)],
            new CteRef(0),
            Includes: [stage0, stage1]);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain(
            "    WHERE rsp.SearchParamId = 88\n" +
            "      AND rsp.ResourceTypeId = 105\n" +
            "      AND r.ResourceTypeId = 105\n" +
            "      AND rsp.BaseUri IS NULL\n" +
            "      AND EXISTS (\n" +
            "        SELECT 1 FROM inc0lim m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
            "    )");
    }

    [Fact]
    public void GivenAPlanWithNoIncludes_WhenEmitted_ThenTheSqlIsByteIdenticalToThePreIncludeShape()
    {
        // Arrange -- this is the zero-diff regression proof: identical to
        // GivenASingleParamSourcePlan_WhenEmitted_ThenProducesAParameterizedSelect's arrangement, above.
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) T1, Sid1 FROM cte0");
    }
```

Add `using Shouldly;` (already present) — `ShouldNotContain(string, Case.Insensitive)` needs `using Shouldly.Configuration;` only if not already resolved by the existing `using Shouldly;`; if the build reports it unresolved, use `emitted.Sql.ShouldNotContain("SearchParamId = ");` instead (case-sensitive is sufficient here since the codebase always renders `SearchParamId` with this exact casing).

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`:

```csharp
    [Fact]
    public void GivenAPlanWithOneIncludeStage_WhenExplained_ThenAppendsAnIncLineAfterTheCteLines()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(IncludeDirection.Forward, 55, [103], [105], [], SeedFromMatch: true, Iterate: false, Limit: 1000);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Includes: [stage]);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");
    }

    [Fact]
    public void GivenAWildcardIncludeStage_WhenExplained_ThenRendersStarForTheNullFields()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        var stage = new IncludeStage(IncludeDirection.Reverse, null, null, null, [], SeedFromMatch: true, Iterate: true, Limit: 500);
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Includes: [stage]);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "inc0 = IncludeStage(ref=*, seedTypes=*, outputTypes=*, seeds=[match], limit=500 iterate, Reverse)");
    }
```

- [ ] **Step 9: Run the tests, expect the new ones to fail (no implementation yet), then build**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`
Expected: build errors (missing types) until Steps 1-7 are applied in order, then 0 warnings, 0 errors.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all tests including the 6 new `EmitTests` and 2 new `PlanExplainerTests` cases, with zero changes to any prior golden string's expected value.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/IncludeDirection.cs src/Core/Ignixa.Search.Sql/Ast/IncludeStage.cs src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs src/Core/Ignixa.Search.Sql/Ast/Emit.cs src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs src/Core/Ignixa.Search.Sql/Ast/EmittedSql.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs
git commit -m "feat(search-sql): add IncludeStage AST, Emit/PlanExplainer rendering"
```

---

### Task 3: `Lower`'s Kahn-sort stage builder + `Run` signature widening

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `IncludeStage`, `IncludeDirection`, `QueryPlan.Includes` (Task 2). `SymbolCollectingVisitor.CollectInclude`, widened `Resolve.RunAsync` (Task 1) — this task's own new `Lower.Run` tests construct a `SymbolTable` directly (matching every existing `LowerTests.cs` test's pattern), so it does not literally call `Resolve.RunAsync`, but Task 4's end-to-end tests exercise both together.
- Produces: `Lower.Run(Expression? expression, SymbolTable symbols, string targetResourceType, IReadOnlyList<IncludeExpression> includes, IReadOnlyList<IncludeExpression> revIncludes, int includeLimit, int? top = null)`. Task 4's end-to-end tests are the primary consumer of the include-bearing behavior; Task 4 also composes this with the widened `Resolve.RunAsync` from Task 1.

- [ ] **Step 1: Add the stage-builder private helpers to `Lower.cs`**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, add `using Ignixa.Search.Sql.Ast;` if not already present (it is, for `CteRef`/`Predicate` etc. — no change needed there), and add these private static members (placed after `LowerScopedExpression`, at the end of the class):

```csharp
    private readonly record struct ResolvedInclude(
        IncludeExpression Expression,
        IncludeDirection Direction,
        IReadOnlyList<short>? Requires,
        IReadOnlyList<short>? Produces);

    private static IReadOnlyList<IncludeStage>? BuildIncludeStages(
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        SymbolTable symbols,
        string matchResourceType,
        int includeLimit)
    {
        if (includes.Count == 0 && revIncludes.Count == 0)
        {
            return null;
        }

        var resolved = includes.Select(e => ResolveInclude(e, IncludeDirection.Forward, symbols))
            .Concat(revIncludes.Select(e => ResolveInclude(e, IncludeDirection.Reverse, symbols)))
            .ToList();

        var nonIterate = resolved.Where(e => !e.Expression.Iterate).ToList();
        var iterate = resolved.Where(e => e.Expression.Iterate).ToList();
        var ordered = nonIterate.Concat(TopologicalSort(iterate)).ToList();

        var matchTypeId = symbols.ResourceTypeId(matchResourceType);
        var stages = new List<IncludeStage>();
        var stageProduces = new List<IReadOnlyList<short>?>();

        foreach (var entry in ordered)
        {
            var seedStages = new List<int>();
            for (var i = 0; i < stages.Count; i++)
            {
                if (Overlaps(stageProduces[i], entry.Requires))
                {
                    seedStages.Add(i);
                }
            }

            var seedFromMatch = Overlaps([matchTypeId], entry.Requires);

            if (seedStages.Count == 0 && !seedFromMatch)
            {
                // Degenerate case (design doc §2): this stage's EXISTS would have zero branches --
                // unrenderable, and not a real shape any binder-produced Requires/Produces pair
                // should reach in practice. Drop it: it can never produce any rows.
                continue;
            }

            var referenceSearchParamId = entry.Expression.WildCard
                ? (short?)null
                : symbols.SearchParamId(entry.Expression.ReferenceSearchParameter);

            stages.Add(new IncludeStage(
                entry.Direction,
                referenceSearchParamId,
                entry.Requires,
                entry.Produces,
                seedStages,
                seedFromMatch,
                entry.Expression.Iterate,
                includeLimit));
            stageProduces.Add(entry.Produces);
        }

        return stages;
    }

    private static ResolvedInclude ResolveInclude(IncludeExpression expression, IncludeDirection direction, SymbolTable symbols)
        => new(expression, direction, ResolveTypeIds(expression.Requires, symbols), ResolveTypeIds(expression.Produces, symbols));

    private static IReadOnlyList<short>? ResolveTypeIds(IReadOnlyCollection<string> types, SymbolTable symbols)
        => types.Contains("*") ? null : types.Select(symbols.ResourceTypeId).ToList();

    private static bool Overlaps(IReadOnlyList<short>? produces, IReadOnlyList<short>? requires)
        => produces is null || requires is null || produces.Any(requires.Contains);

    private static List<ResolvedInclude> TopologicalSort(List<ResolvedInclude> entries)
    {
        var n = entries.Count;
        var inDegree = new int[n];
        var edges = new List<int>[n];
        for (var i = 0; i < n; i++)
        {
            edges[i] = [];
        }

        for (var x = 0; x < n; x++)
        {
            for (var y = 0; y < n; y++)
            {
                if (x == y)
                {
                    continue; // A self-referential iterate is not a cycle for this purpose (design §4.4).
                }

                if (Overlaps(entries[x].Produces, entries[y].Requires))
                {
                    edges[x].Add(y);
                    inDegree[y]++;
                }
            }
        }

        var ready = new SortedSet<int>(Enumerable.Range(0, n).Where(i => inDegree[i] == 0));
        var result = new List<ResolvedInclude>();
        while (ready.Count > 0)
        {
            var node = ready.Min;
            ready.Remove(node);
            result.Add(entries[node]);
            foreach (var next in edges[node])
            {
                if (--inDegree[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        if (result.Count != n)
        {
            throw new NotSupportedException(
                "Two or more :iterate include expressions form a cycle -- the FHIR spec does not define an " +
                "ordering for this case, and fhir-server rejects it too (PR #1391, " +
                "SearchOperationNotSupportedException). Rewrite the search to remove the mutual dependency.");
        }

        return result;
    }
```

`ready.Min` on a `SortedSet<int>` gives the deterministic lowest-original-index tie-break the design requires (design §4.5) — `SortedSet<int>.Min` is O(log n), and ties never need an explicit comparer since the set is keyed on the same integer used for ordering.

- [ ] **Step 2: Widen `Lower.Run`**

Replace:

```csharp
    public static QueryPlan Run(Expression expression, SymbolTable symbols, string targetResourceType, int? top = null)
    {
        var leafContext = new LeafContext(symbols);
        var (remaining, outerPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
        var context = new StructuralContext(symbols);
        var match = remaining is null
            ? context.LowerResourceSource(targetResourceType)
            : LowerNode(remaining, context, targetResourceType);
        return new QueryPlan(context.Ctes, match, top, outerPredicate);
    }
```

with:

```csharp
    public static QueryPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        int? top = null)
    {
        var context = new StructuralContext(symbols);
        CteRef match;
        Predicate? outerPredicate = null;

        if (expression is null)
        {
            match = context.LowerResourceSource(targetResourceType);
        }
        else
        {
            var leafContext = new LeafContext(symbols);
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
            outerPredicate = extractedPredicate;
            match = remaining is null
                ? context.LowerResourceSource(targetResourceType)
                : LowerNode(remaining, context, targetResourceType);
        }

        var includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        return new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages);
    }
```

Update the class's XML doc comment: append, after "...Include and sort are not handled -- see this plan's global constraints for the full list and why.": ` As of Phase 7, includes/revIncludes ARE handled -- via BuildIncludeStages, a Kahn's-algorithm topological sort over the :iterate subset producing QueryPlan.Includes; sort is still not handled.` (and remove "Include and sort are not handled" language that is now half-stale — replace the whole sentence with: `"ChainedExpression (forward and reverse chain, any nesting depth, dispatched to StructuralContext.LowerChain) into a QueryPlan, and includes/revIncludes (via BuildIncludeStages, Phase 7) into QueryPlan.Includes. Sort is not handled -- see this plan's global constraints for why."`).

- [ ] **Step 3: Sweep `LowerTests.cs`'s 6 call sites**

Every `Lower.Run(someExpression, symbols, targetResourceType: "SomeType", ...)` call in `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs` gains `includes: [], revIncludes: [], includeLimit: 0` immediately after `targetResourceType`. Example (`GivenASingleLeafPredicate_WhenLowered_ThenProducesAOneCteQueryPlan`):

```csharp
        // Act
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0);
```

For the one call site that also passes `top:` (`GivenTwoAndedLeafPredicates_WhenLowered_ThenProducesAnIntersectOverBothCtes`):

```csharp
        // Act
        var plan = Lower.Run(tree, symbols, targetResourceType: "Patient", includes: [], revIncludes: [], includeLimit: 0, top: 10);
```

Apply this transformation to all 6 call sites in this file.

- [ ] **Step 4: Sweep `EndToEndCompilationTests.cs`'s `Lower.Run` call sites**

`test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` has 28 calls to `Lower.Run`. Apply the identical transformation from Step 3 to every one (insert `includes: [], revIncludes: [], includeLimit: 0` after `targetResourceType`, before any trailing `top:`).

- [ ] **Step 5: Write new `Lower.Run` include tests**

Add to `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`:

```csharp
    [Fact]
    public void GivenAnIncludeOnlySearchWithNoOtherExpression_WhenLowered_ThenTheMatchFallsBackToResourceSource()
    {
        // Arrange -- Patient?_include=Patient:organization, no other filter (expression is null).
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ResourceSource>();
        plan.Includes.ShouldNotBeNull();
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].Direction.ShouldBe(IncludeDirection.Forward);
        plan.Includes[0].ReferenceSearchParamId.ShouldBe((short)55);
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);
        plan.Includes[0].OutputTypeIds.ShouldBe([(short)105]);
        plan.Includes[0].SeedFromMatch.ShouldBeTrue();
        plan.Includes[0].SeedStages.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTwoIterateIncludesThatChainProducesToRequires_WhenLowered_ThenTheSecondStageSeedsFromTheFirst()
    {
        // Arrange -- Patient?_include:iterate=Organization:partOf&_include=Patient:organization
        // (:iterate stage requires Organization, which the non-iterate stage produces).
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var partOfParam = new SearchParameterInfo(
            "partof", "partof", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);
        var nonIterate = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var iterate = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [orgParam.Url.ToString()] = 55, [partOfParam.Url.ToString()] = 66 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act -- iterate entry listed FIRST in the includes list, to prove ordering is by the sort, not input order.
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate, nonIterate], revIncludes: [], includeLimit: 1000);

        // Assert -- non-iterate stage always sorts first (design §4.1); inc0 is Organization:organization, inc1 is the iterate.
        plan.Includes!.Count.ShouldBe(2);
        plan.Includes[0].ReferenceSearchParamId.ShouldBe((short)55);
        plan.Includes[0].SeedFromMatch.ShouldBeTrue();
        plan.Includes[1].ReferenceSearchParamId.ShouldBe((short)66);
        plan.Includes[1].SeedStages.ShouldBe([0]);
        plan.Includes[1].SeedFromMatch.ShouldBeFalse();
    }

    [Fact]
    public void GivenTwoIndependentIterateIncludesThatBecomeReadySimultaneously_WhenLowered_ThenTheOriginalListOrderIsPreservedAsTheDeterministicTieBreak()
    {
        // Arrange -- Patient?_include:iterate=Condition:subject&_include:iterate=Encounter:subject.
        // Neither stage's Produces overlaps the other's Requires (both just require Patient, satisfied
        // directly by the match) -- both are simultaneously "ready" in Kahn's first round, with no edge
        // between them. Without the deterministic lowest-original-index tie-break (design §4.5), which
        // one sorts first would be an implementation accident, breaking Explain() golden-string stability.
        var conditionSubjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Condition-subject"), targetResourceTypes: ["Patient"]);
        var encounterSubjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Encounter-subject"), targetResourceTypes: ["Patient"]);
        var conditionIterate = new IncludeExpression(["Condition"], conditionSubjectParam, "Condition", "Patient", null, wildCard: false, reversed: false, iterate: true);
        var encounterIterate = new IncludeExpression(["Encounter"], encounterSubjectParam, "Encounter", "Patient", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [conditionSubjectParam.Url.ToString()] = 21, [encounterSubjectParam.Url.ToString()] = 22 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Condition"] = 110, ["Encounter"] = 111 });

        // Act -- Encounter listed first in the input list.
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [encounterIterate, conditionIterate], revIncludes: [], includeLimit: 1000);

        // Assert -- inc0 is the Encounter stage (ref=22), matching its position in the input list.
        plan.Includes!.Count.ShouldBe(2);
        plan.Includes[0].ReferenceSearchParamId.ShouldBe((short)22);
        plan.Includes[1].ReferenceSearchParamId.ShouldBe((short)21);
    }

    [Fact]
    public void GivenTwoMutuallyDependentIterateIncludes_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- two :iterate expressions whose Produces/Requires form a genuine 2-node cycle.
        var aParam = new SearchParameterInfo(
            "a", "a", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/A-a"), targetResourceTypes: ["B"]);
        var bParam = new SearchParameterInfo(
            "b", "b", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/B-b"), targetResourceTypes: ["A"]);
        var includeA = new IncludeExpression(["A"], aParam, "A", "B", null, wildCard: false, reversed: false, iterate: true);
        var includeB = new IncludeExpression(["B"], bParam, "B", "A", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [aParam.Url.ToString()] = 1, [bParam.Url.ToString()] = 2 },
            new Dictionary<string, short> { ["A"] = 10, ["B"] = 11, ["Patient"] = 103 });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [includeA, includeB], revIncludes: [], includeLimit: 1000))
            .Message.ShouldContain("cycle");
    }

    [Fact]
    public void GivenAnIterateIncludeThatNeitherAPredecessorProducesNorTheMatchRequires_WhenLowered_ThenTheStageIsDroppedEntirely()
    {
        // Arrange -- Patient?_include:iterate=Organization:partOf with NO non-iterate Organization-
        // producing include and Patient not being Organization -- Requires=[Organization] intersects
        // neither any predecessor's Produces (there is none) nor the match's own type (Patient).
        var partOfParam = new SearchParameterInfo(
            "partof", "partof", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);
        var iterate = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [partOfParam.Url.ToString()] = 66 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Organization"] = 105 });

        // Act
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate], revIncludes: [], includeLimit: 1000);

        // Assert -- the degenerate stage was dropped, not emitted with an empty EXISTS.
        plan.Includes.ShouldBeNull();
    }

    [Fact]
    public void GivenARevincludeWildcardSourceInclude_WhenLowered_ThenOutputTypeIdsIsNullNotAResolvedStarEntry()
    {
        // Arrange -- Patient?_revinclude=*:*
        var include = new IncludeExpression(["*"], null, "*", "Patient", ["Observation"], wildCard: true, reversed: true, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 });

        // Act
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [include], includeLimit: 1000);

        // Assert
        plan.Includes!.Count.ShouldBe(1);
        plan.Includes[0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBeNull();
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);
    }
```

Add `using Ignixa.Search.Sql.Ast;` for `IncludeDirection` if not already resolved by the existing `using Ignixa.Search.Sql.Ast;` (it is, already imported at the top of `LowerTests.cs`).

- [ ] **Step 6: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all 7 new tests plus every existing `LowerTests`/`EndToEndCompilationTests` case with its mechanically-updated call site.

- [ ] **Step 7: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): Lower builds IncludeStage list via Kahn's algorithm"
```

---

### Task 4: End-to-end compilation tests (Resolve → Lower → Emit, full pipeline)

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: nothing new — this task is pure proof, composing the widened `Resolve.RunAsync`, `Lower.Run`, and `Emit.Run`/`Explain()` together the way Phase 9's DataLayer wiring eventually will.

- [ ] **Step 1: Read the file's existing test pattern**

Open `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` and find its most recent chain-related test (e.g. a forward-chain end-to-end case) to match its exact structure: construct real `IncludeExpression`/`SearchParameterInfo` inputs, a `FakeSymbolResolver`-style resolver (or reuse whatever resolver helper the file already defines), call `await Resolve.RunAsync(...)`, then `Lower.Run(...)`, then assert on `plan.Explain()` and/or `Emit.Run(plan).Sql`.

- [ ] **Step 2: Write the forward-include end-to-end test**

```csharp
    [Fact]
    public async Task GivenPatientIncludeOrganization_WhenCompiledEndToEnd_ThenTheIncludeStageIsForwardWithTheReferencingSideAsTheSeed()
    {
        // Arrange -- Patient?name=Smith&_include=Patient:organization
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = await Resolve.RunAsync(predicate, includes: [include], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, top: 50);

        // Assert -- structure via Explain(), full SQL text via Emit for the whole shape.
        plan.Explain().ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0 top 50\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("cteMatchPage AS (");
        emitted.Sql.ShouldContain("SELECT DISTINCT TOP (1001) r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("SELECT T1, Sid1, CAST(1 AS bit) AS IsMatch, CAST(0 AS bit) AS IsPartial FROM cteMatchPage");
        emitted.Sql.ShouldEndWith("ORDER BY IsMatch DESC");
    }
```

`FakeSymbolResolver` is this file's own existing private nested class (confirmed at `EndToEndCompilationTests.cs:14`, already implementing `ISymbolResolver` with `SearchParamIds`/`ResourceTypeIds` dictionaries) — every one of the file's other 28 end-to-end tests already uses it the same way.

- [ ] **Step 3: Write the reverse-include end-to-end test**

```csharp
    [Fact]
    public async Task GivenPatientRevincludeObservationSubject_WhenCompiledEndToEnd_ThenTheIncludeStageIsReverseWithTheTranslatedSideAsTheSeed()
    {
        // Arrange -- Patient?name=Smith&_revinclude=Observation:subject
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var subjectParam = new SearchParameterInfo(
            "subject", "subject", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"), targetResourceTypes: ["Patient", "Group"]);
        var predicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var revInclude = new IncludeExpression(["Observation"], subjectParam, "Observation", "Patient", null, wildCard: false, reversed: true, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 77;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbols = await Resolve.RunAsync(predicate, includes: [], revIncludes: [revInclude], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(predicate, symbols, targetResourceType: "Patient", includes: [], revIncludes: [revInclude], includeLimit: 1000);

        // Assert
        plan.Explain().ShouldBe(
            "root = StringSearchParam[103,202]  Text = @p0\n" +
            "inc0 = IncludeStage(ref=77, seedTypes=[103], outputTypes=[104], seeds=[match], limit=1000, Reverse)");

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("SELECT DISTINCT TOP (1001) rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("SELECT 1 FROM cteMatchPage m WHERE m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId");
    }
```

- [ ] **Step 4: Write the include-only (null expression) end-to-end test**

```csharp
    [Fact]
    public async Task GivenAnIncludeOnlySearchWithNoOtherFilter_WhenCompiledEndToEnd_ThenTheMatchIsAPlainResourceSource()
    {
        // Arrange -- Patient?_include=Patient:organization, no other search parameter.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var include = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = await Resolve.RunAsync(expression: null, includes: [include], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000, top: 50);

        // Assert
        plan.Explain().ShouldBe(
            "root = ResourceSource[103] top 50\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)");

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("cteMatchPage AS (\n    SELECT TOP (50) m.T1, m.Sid1\n    FROM cte0 m\n)");
    }
```

- [ ] **Step 5: Write the wildcard forward include end-to-end test**

```csharp
    [Fact]
    public async Task GivenPatientIncludeWildcard_WhenCompiledEndToEnd_ThenNoSearchParamIdFilterButOutputTypesAreTheRealReferencedTypes()
    {
        // Arrange -- Patient?_include=Patient:* -- WildCard=true, ReferenceSearchParameter=null,
        // ReferencedTypes carries the REAL resolved output types (design §1.2: this is NOT the "*"
        // sentinel case -- that only arises for _revinclude's wildcard-SOURCE form, tested separately).
        var include = new IncludeExpression(["Patient"], null, "Patient", null, ["Organization", "Practitioner"], wildCard: true, reversed: false, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;
        resolver.ResourceTypeIds["Practitioner"] = 107;

        // Act
        var symbols = await Resolve.RunAsync(expression: null, includes: [include], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [include], revIncludes: [], includeLimit: 1000);

        // Assert
        plan.Includes![0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBe([(short)105, (short)107]);

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldNotContain("rsp.SearchParamId");
        emitted.Sql.ShouldContain("(r.ResourceTypeId = 105 OR r.ResourceTypeId = 107)");
    }
```

- [ ] **Step 6: Write the `*:*` revinclude wildcard-source end-to-end test**

```csharp
    [Fact]
    public async Task GivenRevincludeWildcardSource_WhenCompiledEndToEnd_ThenOutputTypeIdsIsNullSoNoOutputFilterIsEmitted()
    {
        // Arrange -- Patient?_revinclude=*:* -- Produces=["*"] (the literal sentinel, design §1.2).
        var revInclude = new IncludeExpression(["*"], null, "*", "Patient", ["Observation", "Condition"], wildCard: true, reversed: true, iterate: false);

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.ResourceTypeIds["Condition"] = 106;

        // Act
        var symbols = await Resolve.RunAsync(expression: null, includes: [], revIncludes: [revInclude], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [], revIncludes: [revInclude], includeLimit: 1000);

        // Assert
        plan.Includes![0].ReferenceSearchParamId.ShouldBeNull();
        plan.Includes[0].OutputTypeIds.ShouldBeNull();
        plan.Includes[0].SeedTypeIds.ShouldBe([(short)103]);

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldNotContain("rsp.SearchParamId");
        emitted.Sql.ShouldNotContain("rsp.ResourceTypeId = 104");
        emitted.Sql.ShouldNotContain("rsp.ResourceTypeId = 106");
    }
```

- [ ] **Step 7: Write the two-parameter topological `:iterate` ordering end-to-end test**

```csharp
    [Fact]
    public async Task GivenChainedIterateIncludesSpecifiedOutOfOrder_WhenCompiledEndToEnd_ThenTheKahnSortReordersThemRegardlessOfInputOrder()
    {
        // Arrange -- Patient?_include=Patient:organization&_include:iterate=Organization:partOf,
        // with the iterate expression listed FIRST in the includes list.
        var orgParam = new SearchParameterInfo(
            "organization", "organization", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"), targetResourceTypes: ["Organization"]);
        var partOfParam = new SearchParameterInfo(
            "partof", "partof", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"), targetResourceTypes: ["Organization"]);
        var nonIterate = new IncludeExpression(["Patient"], orgParam, "Patient", "Organization", null, wildCard: false, reversed: false, iterate: false);
        var iterate = new IncludeExpression(["Organization"], partOfParam, "Organization", "Organization", null, wildCard: false, reversed: false, iterate: true);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[partOfParam.Url!.ToString()] = 66;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbols = await Resolve.RunAsync(expression: null, includes: [iterate, nonIterate], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(expression: null, symbols, targetResourceType: "Patient", includes: [iterate, nonIterate], revIncludes: [], includeLimit: 1000);

        // Assert -- non-iterate always sorts first regardless of its position in the input list.
        plan.Explain().ShouldBe(
            "root = ResourceSource[103]\n" +
            "inc0 = IncludeStage(ref=55, seedTypes=[103], outputTypes=[105], seeds=[match], limit=1000, Forward)\n" +
            "inc1 = IncludeStage(ref=66, seedTypes=[105], outputTypes=[105], seeds=[inc0], limit=1000 iterate, Forward)");

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("SELECT 1 FROM inc0lim m WHERE m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
    }
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all 6 new end-to-end tests plus the entire pre-existing suite.

- [ ] **Step 9: Commit**

```bash
git add test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "test(search-sql): prove _include/_revinclude/:iterate compile end to end"
```

---

### Task 5: Combined proof + full regression + final whole-branch review prep

**Files:** none (verification only).

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: a clean `dotnet build All.sln` / `dotnet test All.sln` baseline and a review package for the final whole-branch review.

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full solution test**

Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"`
Expected: all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework), unrelated to this plan and out of scope on every prior increment.

- [ ] **Step 3: Re-read the design doc's §7 "explicitly deferred" list and confirm nothing in this plan silently attempted any of it**

Confirm: no `_sort`/continuation-token interaction code was added; no instance-level SMART/compartment filter (`OutputScopeFilter`) was added; no multi-level `:iterate` recursion beyond one Kahn-sorted hop per expression was added. If any of Tasks 1-4's actual committed code drifted into one of these (compare against the diff, not against memory of the plan), flag it now rather than let the final reviewer discover unscoped work.

- [ ] **Step 4: Update the roadmap doc**

In `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`, mark Phase 7's row Complete, following the exact narrative style the Phase 6 (chain) row entry already used (read that row first, then write Phase 7's in matching voice/detail level — resource-type scope, the `IncludeStage`/Kahn mechanism, the two Fable review rounds, the `*:*`/degenerate-case edge handling, and the deliberate divergences from the live executor, each in one or two sentences).

- [ ] **Step 5: Prepare the final whole-branch review package**

Follow `superpowers:subagent-driven-development`'s final-review step: run `scripts/review-package MERGE_BASE HEAD` (from that skill's directory; `MERGE_BASE` = `git merge-base feature/fhir-to-sql-compiler HEAD` if this plan executed on a dedicated worktree branch off `feature/fhir-to-sql-compiler`) and dispatch the final whole-branch reviewer on the most capable available model, per that skill's Model Selection section — this is an architecture-level review, not a mechanical one, given the SMART/compartment-boundary stakes named in design §6 and the SQL-projection bug the design's own second Fable review round already caught once.

- [ ] **Step 6: Report to the user before merging or pushing**

Summarize what shipped (forward/reverse include, wildcard forms, `:iterate` with topological ordering, truncation, the `cteMatchPage` result-shape change) and what's explicitly still deferred (§7's list), then ask before merging into `feature/fhir-to-sql-compiler` and again before pushing — matching every prior increment's established pattern on this branch (every single push this session went through an explicit confirmation first).
