# Chain and Reverse Chain (Phase 6) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship forward chain, reverse chain (`_has`), nested chains, and multiary chain-target expressions in `Ignixa.Search.Sql`, and fix a pre-existing correctness bug (`ParamSource` never constrains `ResourceTypeId`, so a `SearchParamId` shared across resource types can return wrong-type resources from any ordinary query today).

**Architecture:** One new `CteDefinition` node (`ChainJoin`, a direction-flag model covering both forward and reverse with the `dbo.Resource` natural-id-to-surrogate-id translation join on whichever side is "unknown"). One new optional field on `ResourceSource` (a `Predicate?`, used only in nested/chain-scoped lowering, never at the top level). `ParamSource` gains a required `ResourceTypeId` field. Resource-type scope becomes an explicit parameter threaded through `Lower`'s recursion instead of a single `StructuralContext` field, so a chain's target expression can lower against a different resource type than the outer query. Nested chains and multiary chain-target expressions need no new mechanism — they fall out of `ChainJoin` consuming an ordinary `CteRef` as its `InnerMatch`, composing with the existing `Intersect`/`Union` machinery.

**Tech Stack:** C# / .NET 9+, xUnit + Shouldly, `Ignixa.Search.Sql` (Core-tier, no EF/ASP.NET references).

**Full design:** `docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-chain-design.md` — read this first for the *why* behind every task below; this plan only covers the *what* and *how*, task by task. Section references (§N) below refer to that document.

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework) are pre-existing and out of scope, per every prior increment on this branch.
- `ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate Predicate)` — `ResourceTypeId` is the **second** positional field, between `Table` and `SearchParamId`, matching `ResourceSource(short ResourceTypeId)`'s existing field-naming precedent. Every construction site across every leaf and composite lowering rule changes from `new CteDefinition.ParamSource(table, <searchParamIdExpr>, <predicateExpr>)` to `new CteDefinition.ParamSource(table, resourceTypeId, <searchParamIdExpr>, <predicateExpr>)`.
- `Emit.EmitParamSource` renders `ResourceTypeId` as a **literal**, not a bound parameter — matching `SearchParamId`'s existing literal treatment in the same method, and critically, so this fix does not shift any existing `@pN` parameter ordinal in any already-passing golden string.
- `PlanExplainer`'s `ParamSource` bracket notation becomes `TableName[ResourceTypeId,SearchParamId]` (e.g. `StringSearchParam[103,202]`) — a minimal, consistent extension of the existing `TableName[SearchParamId]` format and `ResourceSource[ResourceTypeId]`'s existing bracket convention. Every existing golden `Explain()` string touching a `ParamSource` CTE needs this bracket updated; nothing else about those strings changes.
- `Lower.Run` and `Resolve.RunAsync` both make `targetResourceType` a **required, non-nullable `string`** parameter (dropping the `string? ... = null` default) — every real FHIR search has exactly one target resource type by construction (the URL path names it, or `_type` names it explicitly, which is already a separate, already-handled mechanism). This is a compile-time-enforced fix, not a runtime check: making the parameter required is strictly better than a runtime null-check, per this project's established "unrepresentable defect class" precedent (see e.g. `ParamSource` itself, which already cannot omit `SearchParamId`). `Lower.Run`'s signature becomes `Run(Expression expression, SymbolTable symbols, string targetResourceType, int? top = null)` (`targetResourceType` moved before the now-only-remaining-optional `top`, since C# requires optional parameters last). `Resolve.RunAsync`'s becomes `RunAsync(Expression expression, ISymbolResolver resolver, string targetResourceType, CancellationToken cancellationToken)` (`targetResourceType` moved before `cancellationToken`, keeping the .NET convention of `CancellationToken` last).
- **Direct consequence:** the existing test `GivenAProperlyWrappedNotExpressionWithNoTargetResourceTypeSupplied_WhenLowered_ThenThrowsBecauseResourceSourceNeedsIt` (`test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`) calls `Lower.Run(tree, symbols)` with no `targetResourceType` specifically to prove a runtime throw — once the parameter is required, that call **will not compile**. Task 1 deletes this test (the behavior it proved, "you cannot lower without a target resource type," is now enforced by the compiler, which is a strictly stronger guarantee than the runtime throw it replaces). Do not attempt to preserve it by re-adding a default.
- `StructuralContext`'s constructor becomes `StructuralContext(SymbolTable symbols, string targetResourceType)` (no default) as part of Task 1; the resource-type-scope-as-a-single-field design is only fully replaced by a threaded parameter in Task 5 (see that task) — Tasks 1-4 use the field as-is, just made mandatory.
- `ChainJoin`'s SQL always includes `SELECT DISTINCT` (§3 — without it, a resource matched via N referencing rows produces N duplicate output rows that `Intersect`/`Except` don't deduplicate).
- `ChainJoin`'s SQL always includes an explicit inner-side type filter (`rsp.ReferenceResourceTypeId = @innerTypeId` for forward, `rsp.ResourceTypeId = @innerTypeId` for reverse) — this is load-bearing correctness (§3), not an optimization: `InnerMatch` is not guaranteed type-pure since a `SearchParamId` can be shared across resource types (the same root cause `ParamSource`'s fix addresses).
- `ChainJoin`'s output-type filter (`OutputResourceTypeIds`, which may be plural) renders as an `Or`-chain of `Predicate.Equal`, not a new IN-list predicate type — reuses existing, already-tested `Predicate`/`Emit` machinery.
- `ChainJoin`'s `SearchParamId` renders as a literal, matching `ParamSource`'s precedent.
- `ChainJoin`'s translation join includes `BaseUri IS NULL`, `IsHistory = 0`, `IsDeleted = 0` — baked in, not optional.
- Nested chain depth is capped at **10 levels** (§8) — a `ChainedExpression` whose `.Expression` is itself a `ChainedExpression`, recursively, more than 10 times throws `NotSupportedException` naming the limit.
- `_include`/`_revinclude`/`:iterate` and SMART/compartment scope enforcement are explicitly out of scope for this plan — nothing in this plan should throw a DIFFERENT exception for these than the existing `Lower.LowerNode`'s generic `"Lower does not support X yet"` fallback already produces for unhandled expression shapes.

---

### Task 1: `ParamSource` gains `ResourceTypeId`; `Lower.Run`/`Resolve.RunAsync` require `targetResourceType`; wire through `String`/`Token` (pilot rules)

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StringLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: nothing new from earlier tasks (this is the foundational task).
- Produces: `CteDefinition.ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate Predicate)`. `Lower.Run(Expression, SymbolTable, string targetResourceType, int? top = null)`. `Resolve.RunAsync(Expression, ISymbolResolver, string targetResourceType, CancellationToken cancellationToken)`. `StructuralContext(SymbolTable, string targetResourceType)`. `LeafLoweringDispatcher.Lower(SearchParameterPredicateExpression, LeafContext, short resourceTypeId)`. Every leaf rule's `Lower` method gains a trailing `short resourceTypeId` parameter — later tasks (2, 3) apply this exact same shape to the remaining 11 rules.

- [ ] **Step 1: Update `CteDefinition.ParamSource`**

In `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`, change:
```csharp
    public sealed record ParamSource(TableDescriptor Table, short SearchParamId, Predicate Predicate) : CteDefinition;
```
to:
```csharp
    public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate Predicate) : CteDefinition;
```

Update the class's XML doc comment: replace the sentence `"ChainJoin is NOT included -- nothing in this plan's scope (chain) constructs it; add when that lowering rule is written."` with `"ParamSource.ResourceTypeId constrains which resource type's rows this CTE can return -- a SearchParamId is assigned per search-parameter-definition URL, not per resource type, so a shared definition (e.g. one search parameter spanning Patient/Practitioner) would otherwise let a ParamSource CTE return rows from the wrong resource type."` (this is now historical -- `ChainJoin` is added in Task 7, this comment update is just removing a now-stale forward-reference).

This will not compile yet -- every construction site of `ParamSource` needs the new argument. That's expected; proceed to the next steps before building.

- [ ] **Step 2: Update `Emit.EmitParamSource` and `PlanExplainer`'s `ParamSource` case**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, change:
```csharp
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";
```
to:
```csharp
    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE ResourceTypeId = {p.ResourceTypeId} AND SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";
```

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, change:
```csharp
        CteDefinition.ParamSource p =>
            $"{p.Table.TableName}[{p.SearchParamId}]  {PrintPredicate(p.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
```
to:
```csharp
        CteDefinition.ParamSource p =>
            $"{p.Table.TableName}[{p.ResourceTypeId},{p.SearchParamId}]  {PrintPredicate(p.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
```

Both `ResourceTypeId` and `SearchParamId` render as literals -- neither consumes a parameter ordinal in `PlanExplainer`, matching `Emit`'s literal (not `@pN`) treatment. No `parameterOrdinal` bookkeeping changes are needed here (contrast with `ResourceSource`'s ordinal-consuming `PrintResourceSource`, which is a real bound parameter in `Emit` -- `ParamSource`'s new field is not).

- [ ] **Step 3: Make `StructuralContext`'s target resource type mandatory and resolve it once per call**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, change the field and constructor:
```csharp
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;
    private readonly string? _targetResourceType;

    public StructuralContext(SymbolTable symbols, string? targetResourceType = null)
    {
        _leafContext = new LeafContext(symbols);
        _targetResourceType = targetResourceType;
    }
```
to:
```csharp
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;
    private readonly string _targetResourceType;

    public StructuralContext(SymbolTable symbols, string targetResourceType)
    {
        _leafContext = new LeafContext(symbols);
        _targetResourceType = targetResourceType;
    }
```

Change `Lower` and `LowerComposite` to resolve the type once and pass it down:
```csharp
    public CteRef Lower(SearchParameterPredicateExpression predicate)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = _leafContext.ResourceTypeId(_targetResourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = _leafContext.ResourceTypeId(_targetResourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }
```

Simplify `ResolveTargetResourceTypeId` -- the null-check-and-throw is no longer reachable now that `_targetResourceType` is non-nullable:
```csharp
    private short ResolveTargetResourceTypeId() => _leafContext.ResourceTypeId(_targetResourceType);
```

(`CompositeLoweringDispatcher.Lower` doesn't have the new `short resourceTypeId` parameter yet -- that's Step 5. This file will not compile until Step 5 lands; that's expected within this task.)

- [ ] **Step 4: Update `LeafLoweringDispatcher` and the two pilot leaf rules**

In `src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs`, change:
```csharp
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Value switch
    {
        StringSearchValue s => StringLoweringRule.Lower(predicate, s, context),
        TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context),
        ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context),
        UriSearchValue u => UriLoweringRule.Lower(predicate, u, context),
        NumberSearchValue n => NumberLoweringRule.Lower(predicate, n, context),
        QuantitySearchValue q => QuantityLoweringRule.Lower(predicate, q, context),
        DateTimeSearchValue d => DateTimeLoweringRule.Lower(predicate, d, context),
        _ => throw new NotSupportedException(
            $"No lowering rule for {predicate.Value.GetType().Name} -- composites are out of scope for this plan."),
    };
```
to:
```csharp
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context, short resourceTypeId) => predicate.Value switch
    {
        StringSearchValue s => StringLoweringRule.Lower(predicate, s, context, resourceTypeId),
        TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context, resourceTypeId),
        ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context, resourceTypeId),
        UriSearchValue u => UriLoweringRule.Lower(predicate, u, context, resourceTypeId),
        NumberSearchValue n => NumberLoweringRule.Lower(predicate, n, context, resourceTypeId),
        QuantitySearchValue q => QuantityLoweringRule.Lower(predicate, q, context, resourceTypeId),
        DateTimeSearchValue d => DateTimeLoweringRule.Lower(predicate, d, context, resourceTypeId),
        _ => throw new NotSupportedException(
            $"No lowering rule for {predicate.Value.GetType().Name} -- composites are out of scope for this plan."),
    };
```

(This file references `NumberLoweringRule`/`QuantityLoweringRule`/`DateTimeLoweringRule`/`UriLoweringRule`/`ReferenceLoweringRule` with the new signature before Task 2 updates them -- this whole file, and the whole project, will not compile until Task 2 lands. That's expected: Task 1 establishes the pattern on 2 rules; Task 2 finishes the remaining 5. If your environment requires a green build before committing Task 1 in isolation, see the note at the end of this task's Step 8.)

In `src/Core/Ignixa.Search.Sql/Lowering/StringLoweringRule.cs`, change the signature and return line:
```csharp
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, StringSearchValue value, LeafContext context)
```
to:
```csharp
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, StringSearchValue value, LeafContext context, short resourceTypeId)
```
and:
```csharp
        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), p);
```
to:
```csharp
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), p);
```

In `src/Core/Ignixa.Search.Sql/Lowering/TokenLoweringRule.cs`, change the signature and return line:
```csharp
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context)
```
to:
```csharp
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context, short resourceTypeId)
```
and:
```csharp
        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
```
to:
```csharp
        return new CteDefinition.ParamSource(table, resourceTypeId, context.SearchParamId(predicate.Parameter), predicateExpr);
```

- [ ] **Step 5: Update `CompositeLoweringDispatcher`'s signature (implementation lands in Task 3)**

In `src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs`, change:
```csharp
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<CompositeComponentExpression> components,
        LeafContext context)
    {
```
to:
```csharp
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<CompositeComponentExpression> components,
        LeafContext context,
        short resourceTypeId)
    {
```

Leave the six `TokenTokenLoweringRule.Lower(compositeParameter, predicates, context)`-style calls inside this method's `switch` unchanged for now -- they will not compile until Task 3 adds `resourceTypeId` to each composite rule and to these call sites. This is expected; the whole solution does not need to build again until Task 3 completes (Tasks 1-3 are one continuous compile-red streak by design, since `ParamSource`'s constructor signature is shared everywhere). Do not attempt to make Task 1 alone produce a green build -- see Step 8's note.

- [ ] **Step 6: Make `Lower.Run` and `Resolve.RunAsync` require `targetResourceType`**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, change:
```csharp
    public static QueryPlan Run(Expression expression, SymbolTable symbols, int? top = null, string? targetResourceType = null)
    {
        var leafContext = new LeafContext(symbols);
        var (remaining, outerPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
        var context = new StructuralContext(symbols, targetResourceType);
```
to:
```csharp
    public static QueryPlan Run(Expression expression, SymbolTable symbols, string targetResourceType, int? top = null)
    {
        var leafContext = new LeafContext(symbols);
        var (remaining, outerPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
        var context = new StructuralContext(symbols, targetResourceType);
```

In `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`, change:
```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression expression,
        ISymbolResolver resolver,
        CancellationToken cancellationToken,
        string? targetResourceType = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(resolver);
```
to:
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
```

Update the rest of the method body: it currently has `if (targetResourceType is not null) { resourceTypes.Add(targetResourceType); }` -- change to an unconditional `resourceTypes.Add(targetResourceType);` since the parameter can no longer be null.

Update the method's XML `<remarks>` block: the paragraph starting `"Resource-type resolution is out of scope for this stage beyond two narrow exceptions..."` now needs a third clause -- `targetResourceType` is no longer one of "two narrow exceptions", it's now the primary, always-present source of resource-type resolution for the outer query; the `ReferenceSearchValue`/`_type` exceptions remain narrow exceptions for *additional* resource types beyond the caller's own. Reword the paragraph to reflect that `targetResourceType` is mandatory, not one of two optional narrow cases.

- [ ] **Step 7: Remove the now-uncompilable "missing targetResourceType" test; fix every other caller**

In `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`, delete the entire `GivenAProperlyWrappedNotExpressionWithNoTargetResourceTypeSupplied_WhenLowered_ThenThrowsBecauseResourceSourceNeedsIt` fact (added in the `:not`/resource-column-predicates increment specifically to prove this runtime throw -- the throw is now a compile error instead, a strictly stronger guarantee, so the test's premise no longer applies).

Every other call to `Lower.Run(...)` or `Resolve.RunAsync(...)` across the whole `Ignixa.Search.Sql.Tests` project now needs a `targetResourceType` argument if it doesn't already have one. Search for every call site:
```bash
grep -rn "Lower\.Run(\|Resolve\.RunAsync(" test/Ignixa.Search.Sql.Tests/ --include="*.cs"
```
For each call currently missing `targetResourceType`, add it as a named argument (`targetResourceType: "Patient"`, or whichever resource type that test's `SearchParameterInfo` URLs imply -- e.g. a test using `http://hl7.org/fhir/SearchParameter/Patient-name` needs `targetResourceType: "Patient"`) to both the `Resolve.RunAsync` and `Lower.Run` calls in that test, and ensure the test's `FakeSymbolResolver`/resolver setup includes `resolver.ResourceTypeIds["Patient"] = <some short>;` (or whatever type) if it doesn't already -- every leaf-rule and composite-rule unit test file constructs its own resolver/symbol table by hand and will need this. Do this for `EndToEndCompilationTests.cs` now (every fact in that file); the remaining per-rule unit test files (`StringLoweringRuleTests.cs`, `TokenLoweringRuleTests.cs`, etc. -- whatever the actual per-rule test files are named, confirm via `ls test/Ignixa.Search.Sql.Tests/Lowering/`) are Task 2/3's job for the rule types they cover, but if any of those files call `Lower.Run`/`Resolve.RunAsync` directly (rather than calling a rule's `Lower` method directly with a hand-built `LeafContext`), fix those calls now too, since this step's grep will have found them.

- [ ] **Step 8: Update golden strings for every `String`/`Token`-only test, run, iterate to green**

Every `Explain()` or `emitted.Sql`-asserting test whose plan includes a `StringSearchParam` or `TokenSearchParam` `ParamSource` CTE needs its golden string's bracket updated from `TableName[SearchParamId]` to `TableName[ResourceTypeId,SearchParamId]` (Step 2's format) -- e.g. `"StringSearchParam[202]  Text = @p0 collate CS_AS"` becomes `"StringSearchParam[103,202]  Text = @p0 collate CS_AS"` where `103` is whatever `ResourceTypeId` that test's resolver assigns to the target resource type (e.g. `resolver.ResourceTypeIds["Patient"] = 103`). Do this now for every fact in `EndToEndCompilationTests.cs` that touches only `String`/`Token` leaf types (leave facts touching `Number`/`Quantity`/`DateTime`/`Uri`/`Reference`/composites red -- Tasks 2/3 fix those). Also update `EmitTests.cs`/`PlanExplainerTests.cs` facts that directly construct a `CteDefinition.ParamSource` by hand (they need the new constructor argument, plus their expected string updated the same way).

Build and run:
```bash
dotnet build All.sln --nologo
```
Expected: 0 errors (Tasks 1-3 together restore a green build; do not expect green after Task 1 alone if `LeafLoweringDispatcher`/`CompositeLoweringDispatcher` reference not-yet-updated rules from Steps 4/5 above -- if your dispatch model requires a green build per task, complete Tasks 1-3 as one combined implementer dispatch instead of three separate ones; the controller executing this plan should decide this based on how the SDD process is being run, and note the decision in the progress ledger).

Once buildable, run:
```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Fix every failure whose only diff is the bracket format or a missing `targetResourceType` argument, per Steps 7-8's rules above, for `String`/`Token`-only tests specifically. `Number`/`Quantity`/`DateTime`/`Uri`/`Reference`/composite-touching tests remain red until Tasks 2/3.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(search-sql): ParamSource gains ResourceTypeId; targetResourceType is now mandatory

Fixes a pre-existing correctness bug: ParamSource never constrained
ResourceTypeId, and a SearchParamId is assigned per search-parameter
URL, not per resource type, so a definition shared across resource
types (e.g. individual-email spanning Patient/Practitioner) could
return wrong-type resources from an ordinary, already-merged query.
Lower.Run/Resolve.RunAsync's targetResourceType becomes required
(compile-time enforced) rather than an optional parameter only :not/
resource-column queries needed. Wires the fix through String/Token as
pilot leaf rules; Tasks 2/3 apply the identical pattern to the
remaining leaf and composite rules."
```

---

### Task 2: Apply `ResourceTypeId` to the remaining 5 leaf rules

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/NumberLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/QuantityLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/DateTimeLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/UriLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/ReferenceLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` and every per-rule unit test file for these 5 types (confirm exact filenames via `ls test/Ignixa.Search.Sql.Tests/Lowering/`)

**Interfaces:**
- Consumes: `LeafLoweringDispatcher.Lower(predicate, context, resourceTypeId)` (Task 1) already calls all 5 of these with the new signature -- this task makes that call site compile.
- Produces: nothing new for later tasks; this task closes out `LeafLoweringDispatcher`'s compile errors from Task 1.

Apply the exact same two-line transformation Task 1 applied to `StringLoweringRule`/`TokenLoweringRule`, to each of these 5 files. Every one of these methods currently has the shape `public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, <TValue> value, LeafContext context)` ending in `return new CteDefinition.ParamSource(table, <expr>, <expr2>);` (or, for `ReferenceLoweringRule`, `return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), combined);`) -- add `, short resourceTypeId` to the signature's parameter list (after `LeafContext context`), and add `resourceTypeId, ` as the new second argument to every `new CteDefinition.ParamSource(table, ...)` call in that file:

- [ ] **Step 1: `NumberLoweringRule.cs`** -- signature line and `return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);` both get the transformation.
- [ ] **Step 2: `QuantityLoweringRule.cs`** -- same transformation.
- [ ] **Step 3: `DateTimeLoweringRule.cs`** -- same transformation.
- [ ] **Step 4: `UriLoweringRule.cs`** -- same transformation.
- [ ] **Step 5: `ReferenceLoweringRule.cs`** -- same transformation (return line is `return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), combined);` -- note the variable is named `combined`, not `predicateExpr`, in this file).
- [ ] **Step 6: Build**

```bash
dotnet build All.sln --nologo
```
Expected: 0 errors now that every `LeafLoweringDispatcher` call site's target method matches.

- [ ] **Step 7: Update golden strings and `targetResourceType` arguments for every test touching `Number`/`Quantity`/`DateTime`/`Uri`/`Reference`**

Same two fixes as Task 1 Step 7/8, scoped to tests touching these 5 leaf types: add `targetResourceType` to `Lower.Run`/`Resolve.RunAsync` calls that are missing it, and update the `TableName[SearchParamId]` → `TableName[ResourceTypeId,SearchParamId]` bracket in every affected golden string.

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Fix every remaining failure whose only diff is one of these two patterns, for these 5 types. Composite-touching tests remain red until Task 3.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(search-sql): apply ResourceTypeId to Number/Quantity/DateTime/Uri/Reference leaf rules"
```

---

### Task 3: Apply `ResourceTypeId` to all 6 composite rules

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenTokenLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenNumberNumberLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenStringLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenQuantityLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenDateTimeLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/ReferenceTokenLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` and composite-related unit test files

**Interfaces:**
- Consumes: `CompositeLoweringDispatcher.Lower(compositeParameter, components, context, resourceTypeId)` (Task 1, Step 5) already has this signature -- this task wires its internal `switch` calls and every composite rule to match.
- Produces: nothing new for later tasks; closes out the last of `ParamSource`'s constructor-signature compile errors.

- [ ] **Step 1: Wire `CompositeLoweringDispatcher`'s six dispatch calls**

In `src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs`, every line inside the final `switch` currently reads `<Rule>.Lower(compositeParameter, predicates, context)` -- add `, resourceTypeId` to all six:
```csharp
        return predicates.Select(p => p.Value).ToArray() switch
        {
            [TokenSearchValue, TokenSearchValue] => TokenTokenLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, NumberSearchValue, NumberSearchValue] => TokenNumberNumberLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, StringSearchValue] => TokenStringLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, QuantitySearchValue] => TokenQuantityLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, DateTimeSearchValue] => TokenDateTimeLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [ReferenceSearchValue, TokenSearchValue] => ReferenceTokenLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            [TokenSearchValue, ReferenceSearchValue] => ReferenceTokenLoweringRule.Lower(compositeParameter, predicates, context, resourceTypeId),
            var values => throw new NotSupportedException(
                $"No composite lowering rule for component value types [{string.Join(", ", values.Select(v => v.GetType().Name))}] " +
                $"on composite parameter '{compositeParameter.Code}'."),
        };
```

- [ ] **Step 2: Apply the signature + return-line transformation to all 6 composite rule files**

Each of these 6 files has a multi-line `public static CteDefinition.ParamSource Lower(SearchParameterInfo compositeParameter, SearchParameterPredicateExpression[] predicates, LeafContext context)`-shaped signature (confirm each file's exact parameter list by reading it -- some may differ slightly, e.g. `ReferenceTokenLoweringRule` resolves Reference/Token roles by runtime type and may have extra local parameters) ending in `return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);`. For each of `TokenTokenLoweringRule.cs`, `TokenNumberNumberLoweringRule.cs`, `TokenStringLoweringRule.cs`, `TokenQuantityLoweringRule.cs`, `TokenDateTimeLoweringRule.cs`, `ReferenceTokenLoweringRule.cs`: add a trailing `short resourceTypeId` parameter to the `Lower` signature, and add `resourceTypeId, ` as the new second argument to that file's `new CteDefinition.ParamSource(table, ...)` call.

- [ ] **Step 3: Build**

```bash
dotnet build All.sln --nologo
```
Expected: 0 warnings, 0 errors. This should be the first fully green build since Task 1 began (Tasks 1-3 together restore the whole solution to green).

- [ ] **Step 4: Update golden strings and `targetResourceType` arguments for every composite-touching test**

Same two fixes as before (bracket format, `targetResourceType` argument), scoped to every test touching any of the 6 composite types.

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: this is the point where the `Ignixa.Search.Sql.Tests` project itself should be fully green again (Task 4 does a final full-solution sweep to catch anything this task and Tasks 1-2 missed).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): apply ResourceTypeId to all 6 composite lowering rules"
```

---

### Task 4: Full-solution regression sweep for the `ParamSource` fix

**Files:**
- Modify: any remaining test files with `ParamSource`-touching golden strings or missing `targetResourceType` arguments that Tasks 1-3 missed (expected to be few or none if Tasks 1-3 were thorough, but this task exists specifically to catch what a mechanical per-task sweep might have missed -- e.g. `LowerTests.cs` facts not scoped to a single leaf type, `ResourceColumnLoweringRuleTests.cs` if it incidentally touches `ParamSource`-shaped fixtures, `Catalog/SqlCatalogTests.cs` if it references `ParamSource`).

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: a fully green `Ignixa.Search.Sql.Tests` suite and full-solution build -- the foundation the rest of this plan's chain tasks build on.

- [ ] **Step 1: Full solution build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```
Expected: 0 warnings, 0 errors. The only failures should be the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework). Fix any remaining `Ignixa.Search.Sql.Tests` failures using the exact same two transformation rules from Tasks 1-3 (bracket format, `targetResourceType` argument) -- do not introduce any new behavior here, this task is pure cleanup of what earlier tasks' mechanical sweeps missed.

- [ ] **Step 2: Commit (only if Step 1 required changes)**

```bash
git add -A
git commit -m "fix(search-sql): sweep remaining ParamSource golden-string/targetResourceType gaps"
```

If Step 1 required no changes (Tasks 1-3 were fully thorough), skip this commit and note in the progress ledger that Task 4 found nothing to fix.

---

### Task 5: Thread resource-type scope through `Lower`'s recursion (refactor, no behavior change)

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-4 (a fully working, `ResourceTypeId`-correct compiler for the single-scope case).
- Produces: `StructuralContext.Lower(SearchParameterPredicateExpression predicate, string resourceType)`, `LowerComposite(..., string resourceType)`, `LowerResourceSource(string resourceType)`, `LowerNot(CteRef innerMatch, string resourceType)` -- every method that previously read `_targetResourceType` now takes it as an explicit parameter. `Lower.LowerNode`/`LowerSearchParameter`/`LowerAnd`/`ExtractResourceColumnPredicates` all gain a `string resourceType` parameter threaded through their recursion. Task 8 (forward chain) is the first consumer that actually passes a *different* value than the top-level scope.

This is a pure refactor -- behavior for every existing (non-chain) query is unchanged, since the top-level scope is still the only scope that exists until Task 8. Verify this with zero golden-string changes: if any existing test's expected output changes in this task, something went wrong.

- [ ] **Step 1: Remove `_targetResourceType` as a field; thread it as a parameter**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, remove the `_targetResourceType` field and the `targetResourceType` constructor parameter entirely:
```csharp
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;

    public StructuralContext(SymbolTable symbols)
    {
        _leafContext = new LeafContext(symbols);
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public CteRef Lower(SearchParameterPredicateExpression predicate, string resourceType)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components, string resourceType)
    {
        foreach (var component in components)
        {
            RejectResourceColumnCode(component.ComponentSearchParameter.Code);
        }

        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }
```
(`RejectResourceColumnCode`, `Intersect`, `Union` are unchanged.)

```csharp
    public CteRef LowerResourceSource(string resourceType)
    {
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef LowerNot(CteRef innerMatch, string resourceType)
    {
        var baseRef = LowerResourceSource(resourceType);
        _ctes.Add(new CteDefinition.Except(baseRef, innerMatch));
        return new CteRef(_ctes.Count - 1);
    }
```

Delete `ResolveTargetResourceTypeId` entirely -- its one caller (`LowerResourceSource`) now resolves inline, and no other caller exists.

- [ ] **Step 2: Thread `resourceType` through `Lower.cs`'s recursion**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, change every recursive method to accept and forward a `string resourceType` parameter:
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

    private static CteRef LowerNode(Expression expression, StructuralContext context, string resourceType) => expression switch
    {
        SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } => throw new NotSupportedException(
            "A :not-modified predicate reached leaf dispatch directly, outside a SearchParameterExpression wrapper -- " +
            "the real binder never produces this shape (LowerSearchParameter handles :not for both the single-value " +
            "and comma-separated cases), so this is unexpected input. Throwing rather than silently lowering it as a " +
            "positive match, which is exactly the bug this guard exists to prevent."),
        SearchParameterPredicateExpression leaf => context.Lower(leaf, resourceType),
        SearchParameterExpression sp => LowerSearchParameter(sp, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList()),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context, string resourceType)
    {
        if (sp.Expression is NotExpression not)
        {
            return context.LowerNot(LowerNode(not.Expression, context, resourceType), resourceType);
        }

        if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
        {
            var positiveMatch = new SearchParameterPredicateExpression(predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value);
            return context.LowerNot(context.Lower(positiveMatch, resourceType), resourceType);
        }

        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!, resourceType);
        }

        if (sp.Expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or
            && or.Expressions.Count > 0
            && or.Expressions.All(e => TryGetCompositeComponents(e, out _)))
        {
            var refs = or.Expressions
                .Select(e =>
                {
                    TryGetCompositeComponents(e, out var alt);
                    return context.LowerComposite(sp.Parameter, alt!, resourceType);
                })
                .ToList();
            return context.Union(refs);
        }

        return LowerNode(sp.Expression, context, resourceType);
    }
```
(`TryGetCompositeComponents` is unchanged -- it doesn't touch `StructuralContext`.)
```csharp
    private static CteRef LowerAnd(MultiaryExpression and, StructuralContext context, string resourceType)
    {
        var refs = and.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList();
        var result = refs[0];
        for (var i = 1; i < refs.Count; i++)
        {
            result = context.Intersect(result, refs[i]);
        }
        return result;
    }
```
(`ExtractResourceColumnPredicates`/`TryExtractResourceColumnPredicate` are unchanged in this task -- they don't reference `_targetResourceType` or `StructuralContext` today. Task 12 changes them.)

- [ ] **Step 3: Full regression -- prove zero behavior change**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 warnings, 0 errors, and **every existing test passes with its golden string completely unchanged from Task 4's state**. If any `Explain()`/SQL-text assertion needed a text change to pass, that is a sign this refactor accidentally changed behavior -- stop and investigate rather than "fixing" the test.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(search-sql): thread resource-type scope through Lower's recursion

Pure refactor, zero behavior change for existing (non-chain) queries --
StructuralContext no longer holds a single _targetResourceType field;
every method that needs it now takes an explicit resourceType parameter,
threaded through Lower.LowerNode's recursion. This is what lets a
chain's target expression (Task 8+) lower against a different resource
type than the outer query, at any nesting depth, without a second
mutable context instance."
```

---

### Task 6: `SymbolCollectingVisitor` gains a `VisitChained` override

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`

**Interfaces:**
- Consumes: `ChainedExpression(string[] ResourceTypes, SearchParameterInfo ReferenceSearchParameter, string[] TargetResourceTypes, bool Reversed, Expression Expression)` (`src/Core/Ignixa.Search/Expressions/ChainedExpression.cs`, already exists, untouched by this plan).
- Produces: `Resolve.RunAsync` now resolves a `ChainedExpression`'s `ReferenceSearchParameter`'s `SearchParamId`, and every resource type named in `ResourceTypes`/`TargetResourceTypes`, into the returned `SymbolTable` -- Task 8+ depend on this being populated.

- [ ] **Step 1: Write the failing test**

Add to `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`:
```csharp
    [Fact]
    public async Task GivenAChainedExpression_WhenResolved_ThenSymbolTableHasTheReferenceParamAndBothResourceTypes()
    {
        // Arrange -- Patient?organization.name=Acme
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(chain, resolver, targetResourceType: "Patient", CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(orgParam).ShouldBe((short)55);
        symbolTable.SearchParamId(nameParam).ShouldBe((short)202);
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Organization").ShouldBe((short)105);
    }
```
(Confirm `ChainedExpression`'s exact constructor argument order and `FakeSymbolResolver`'s exact field names against the top of `ResolveTests.cs` before running -- they were established earlier in this file/session and must match exactly.)

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAChainedExpression_WhenResolved" --nologo
```
Expected: FAIL -- `symbolTable.SearchParamId(orgParam)` throws `KeyNotFoundException`, since `SymbolCollectingVisitor` never visits `ReferenceSearchParameter` today.

- [ ] **Step 3: Implement**

In `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`, add (after `VisitSearchParameter`, before the closing brace):
```csharp
    public override Expression VisitChained(ChainedExpression expression, object? context)
    {
        Parameters.Add(expression.ReferenceSearchParameter);
        foreach (var resourceType in expression.ResourceTypes)
        {
            ResourceTypes.Add(resourceType);
        }

        foreach (var resourceType in expression.TargetResourceTypes)
        {
            ResourceTypes.Add(resourceType);
        }

        return base.VisitChained(expression, context);
    }
```
`base.VisitChained` (`ExpressionRewriter.VisitChained`) recurses into `expression.Expression`, which reaches every `SearchParameterPredicateExpression`/`SearchParameterExpression`/nested `ChainedExpression` beneath via the existing overrides (and this same new override, for nested chains).

Add a `using Ignixa.Search.Expressions;` if not already present (confirm against the file's existing usings first).

- [ ] **Step 4: Run to confirm it passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAChainedExpression_WhenResolved" --nologo
```
Expected: PASS.

- [ ] **Step 5: Run the full `Ignixa.Search.Sql.Tests` suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 failures, 0 regressions.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): SymbolCollectingVisitor resolves ChainedExpression's reference param and both resource types"
```

---

### Task 7: `ChainJoin` CteDefinition + `Emit` + `PlanExplainer` (AST-only, no lowering rule yet)

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: nothing new -- this task adds a new `CteDefinition` case that nothing constructs yet except tests, matching the precedent set by `ResourceSource`/`Except` in the `:not`/resource-column-predicates increment (AST support landed a task before the lowering rule that produces it).
- Produces: `CteDefinition.ChainJoin(CteRef InnerMatch, short ReferenceSearchParamId, short InnerResourceTypeId, IReadOnlyList<short> OutputResourceTypeIds, ChainDirection Direction)`, and a new `ChainDirection` enum (`Forward`, `Reverse`). Task 8 is the first real caller.

- [ ] **Step 1: Add `ChainDirection` and `CteDefinition.ChainJoin`**

Create `src/Core/Ignixa.Search.Sql/Ast/ChainDirection.cs`:
```csharp
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Which side of a ChainJoin's dbo.ReferenceSearchParam row is the "known" (InnerMatch-correlated)
/// side versus the "unknown" (dbo.Resource-translated) side. Forward: InnerMatch is the referenced
/// (target) side, translated via dbo.Resource; output is the referencing (source) side, already a
/// surrogate id. Reverse: InnerMatch is the referencing side, correlated directly; output is the
/// referenced side, translated via dbo.Resource. See docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-chain-design.md §2-3.
/// </summary>
public enum ChainDirection
{
    Forward,
    Reverse,
}
```

In `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`, add after `Except`:
```csharp
    public sealed record ChainJoin(
        CteRef InnerMatch,
        short ReferenceSearchParamId,
        short InnerResourceTypeId,
        IReadOnlyList<short> OutputResourceTypeIds,
        ChainDirection Direction) : CteDefinition;
```

Add `using Ignixa.Search.Sql.Ast;` for `ChainDirection` if `CteDefinition.cs` is not already in that namespace (it is -- `ChainDirection` lives in the same namespace, no new using needed).

Update the class's XML doc comment, replacing the (now Task-1-superseded) `ParamSource.ResourceTypeId` sentence's trailing reference if needed, and adding: `"ChainJoin represents a chain (forward or reverse) as a join through dbo.ReferenceSearchParam and dbo.Resource -- see the chain design doc for the full derivation."`

- [ ] **Step 2: Write failing `Emit` tests for both directions**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:
```csharp
    [Fact]
    public void GivenAForwardChainJoin_WhenEmitted_ThenTranslatesTheOutputSideThroughResource()
    {
        // Arrange -- cte0 is some pre-existing target-side match; ChainJoin wraps it as InnerMatch
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), ResourceTypeId: 105, SearchParamId: 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Acme"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 55, InnerResourceTypeId: 105, OutputResourceTypeIds: [103], ChainDirection.Forward),
            ],
            new CteRef(1));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r");
        emitted.Sql.ShouldContain("ON r.ResourceTypeId = rsp.ReferenceResourceTypeId");
        emitted.Sql.ShouldContain("AND r.ResourceId = rsp.ReferenceResourceId");
        emitted.Sql.ShouldContain("AND r.IsHistory = 0 AND r.IsDeleted = 0");
        emitted.Sql.ShouldContain("INNER JOIN cte0 m");
        emitted.Sql.ShouldContain("ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId");
        emitted.Sql.ShouldContain("WHERE rsp.SearchParamId = 55");
        emitted.Sql.ShouldContain("AND rsp.ReferenceResourceTypeId = 105");
        emitted.Sql.ShouldContain("AND rsp.ResourceTypeId = 103");
        emitted.Sql.ShouldContain("AND rsp.BaseUri IS NULL");
    }

    [Fact]
    public void GivenAReverseChainJoinWithPluralOutputTypes_WhenEmitted_ThenOrsTheOutputTypeFilter()
    {
        // Arrange -- cte0 is the referencing-side match; output can be more than one type
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("TokenSearchParam"), ResourceTypeId: 106, SearchParamId: 88, new Predicate.Equal(new SqlColumnRef("TokenSearchParam", "Code"), new SqlParameterRef("1234-5"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 77, InnerResourceTypeId: 106, OutputResourceTypeIds: [103, 108], ChainDirection.Reverse),
            ],
            new CteRef(1));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldContain("INNER JOIN cte0 m");
        emitted.Sql.ShouldContain("ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId");
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r");
        emitted.Sql.ShouldContain("ON r.ResourceTypeId = rsp.ReferenceResourceTypeId");
        emitted.Sql.ShouldContain("WHERE rsp.SearchParamId = 77");
        emitted.Sql.ShouldContain("AND rsp.ResourceTypeId = 106");
        emitted.Sql.ShouldContain("AND (rsp.ReferenceResourceTypeId = 103 OR rsp.ReferenceResourceTypeId = 108)");
        emitted.Sql.ShouldContain("AND rsp.BaseUri IS NULL");
    }
```
(Confirm `QueryPlan`'s constructor argument order and `EmitTests.cs`'s existing usings/helper patterns before running -- follow this file's own established style for constructing a bare `QueryPlan`/`CteDefinition.ParamSource` by hand, matching how `EmitTests.cs`'s existing `Except`/`ResourceSource` tests are structured.)

- [ ] **Step 3: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ChainJoin" --nologo
```
Expected: FAIL -- `Emit.EmitCte`'s switch has no `ChainJoin` arm, so it hits the `_ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}.")` default.

- [ ] **Step 4: Implement `EmitChainJoin`**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, add a case to `EmitCte`'s switch (after `CteDefinition.Except`):
```csharp
        CteDefinition.ChainJoin cj => EmitChainJoin(cj, parameters),
```

Add the method:
```csharp
    private static string EmitChainJoin(CteDefinition.ChainJoin cj, List<EmittedSqlParameter> parameters)
    {
        var outputFilter = string.Join(
            " OR ",
            cj.OutputResourceTypeIds.Select(id => $"{OutputTypeColumn(cj.Direction)} = {id}"));
        if (cj.OutputResourceTypeIds.Count > 1)
        {
            outputFilter = $"({outputFilter})";
        }

        return cj.Direction switch
        {
            ChainDirection.Forward =>
                $"    SELECT DISTINCT rsp.ResourceTypeId AS T1, rsp.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN dbo.Resource r\n" +
                $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
                $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
                $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
                $"    INNER JOIN cte{cj.InnerMatch.Index} m\n" +
                $"        ON m.T1 = r.ResourceTypeId AND m.Sid1 = r.ResourceSurrogateId\n" +
                $"    WHERE rsp.SearchParamId = {cj.ReferenceSearchParamId}\n" +
                $"      AND rsp.ReferenceResourceTypeId = {cj.InnerResourceTypeId}\n" +
                $"      AND {outputFilter}\n" +
                $"      AND rsp.BaseUri IS NULL",
            ChainDirection.Reverse =>
                $"    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
                $"    FROM dbo.ReferenceSearchParam rsp\n" +
                $"    INNER JOIN cte{cj.InnerMatch.Index} m\n" +
                $"        ON m.T1 = rsp.ResourceTypeId AND m.Sid1 = rsp.ResourceSurrogateId\n" +
                $"    INNER JOIN dbo.Resource r\n" +
                $"        ON r.ResourceTypeId = rsp.ReferenceResourceTypeId\n" +
                $"       AND r.ResourceId = rsp.ReferenceResourceId\n" +
                $"       AND r.IsHistory = 0 AND r.IsDeleted = 0\n" +
                $"    WHERE rsp.SearchParamId = {cj.ReferenceSearchParamId}\n" +
                $"      AND rsp.ResourceTypeId = {cj.InnerResourceTypeId}\n" +
                $"      AND {outputFilter}\n" +
                $"      AND rsp.BaseUri IS NULL",
            _ => throw new NotSupportedException($"Unknown ChainDirection '{cj.Direction}'."),
        };
    }

    private static string OutputTypeColumn(ChainDirection direction) => direction switch
    {
        ChainDirection.Forward => "rsp.ResourceTypeId",
        ChainDirection.Reverse => "rsp.ReferenceResourceTypeId",
        _ => throw new NotSupportedException($"Unknown ChainDirection '{direction}'."),
    };
```

- [ ] **Step 5: Run to confirm they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ChainJoin" --nologo
```
Expected: 0 warnings, 0 errors, both tests pass. If the exact whitespace/line-join in your assertions doesn't match (the `ShouldContain` calls above check substrings, not the whole string, specifically to avoid brittleness on incidental whitespace -- if a specific `ShouldContain` fails, check the actual rendered SQL and adjust only that one assertion's text, not the production code's structure).

- [ ] **Step 6: Write and implement `PlanExplainer` support**

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`:
```csharp
    [Fact]
    public void GivenAForwardChainJoin_WhenExplained_ThenRendersTheJoinShape()
    {
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(SqlCatalog.Default.Table("StringSearchParam"), ResourceTypeId: 105, SearchParamId: 202, new Predicate.Equal(new SqlColumnRef("StringSearchParam", "Text"), new SqlParameterRef("Acme"))),
                new CteDefinition.ChainJoin(new CteRef(0), ReferenceSearchParamId: 55, InnerResourceTypeId: 105, OutputResourceTypeIds: [103], ChainDirection.Forward),
            ],
            new CteRef(1));

        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text = @p0\n" +
            "root = ChainJoin(cte0, ref=55, inner=105, output=[103], Forward)");
    }
```

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, add a case to `PrintCte`'s switch (after `CteDefinition.Except`):
```csharp
        CteDefinition.ChainJoin cj =>
            $"ChainJoin(cte{cj.InnerMatch.Index}, ref={cj.ReferenceSearchParamId}, inner={cj.InnerResourceTypeId}, output=[{string.Join(",", cj.OutputResourceTypeIds)}], {cj.Direction}){PrintTop(top)}",
```
`ChainJoin` consumes no parameter ordinal in `PlanExplainer` -- every value it carries (`ReferenceSearchParamId`, `InnerResourceTypeId`, `OutputResourceTypeIds`) renders as a literal in `Emit` too (Step 4), matching `ParamSource`'s established literal-not-parameter precedent for these kinds of catalog ids.

- [ ] **Step 7: Run to confirm it passes, then the full suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ChainJoin" --nologo
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 warnings, 0 errors, zero regressions.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(search-sql): ChainJoin CteDefinition + Emit + PlanExplainer (AST-only)

Adds the AST-level support for chain's SQL shape -- ChainDirection,
CteDefinition.ChainJoin, Emit rendering for both directions (DISTINCT,
inner-type filter, Or-chained output-type filter, BaseUri IS NULL,
history/deleted baked into the translation join), and PlanExplainer's
literal, non-parameter-consuming rendering. No lowering rule constructs
this yet -- that's Tasks 8-9."
```

---

### Task 8: Forward chain lowering + E2E proof

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `CteDefinition.ChainJoin`/`ChainDirection` (Task 7), `SymbolCollectingVisitor.VisitChained` (Task 6), threaded `resourceType` recursion (Task 5).
- Produces: `StructuralContext.LowerChain(ChainedExpression chain, string outerResourceType)` -- Task 9 (reverse) extends this same method rather than adding a parallel one.

- [ ] **Step 1: Write the failing E2E test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:
```csharp
    [Fact]
    public async Task GivenAForwardChainQuery_WhenCompiled_ThenChainJoinsThroughTheReferenceTranslation()
    {
        // Arrange -- Patient?organization.name=Acme
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(chain, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- the target-side match (Organization.name=Acme) becomes cte0, the ChainJoin is root
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text = @p0\n" +
            "root = ChainJoin(cte0, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldContain("SELECT DISTINCT");
        emitted.Sql.ShouldNotContain("Acme");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("Acme"));
    }
```

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAForwardChainQuery" --nologo
```
Expected: FAIL -- `Lower.LowerNode` has no case for `ChainedExpression`, hits the generic `"Lower does not support ChainedExpression yet"` throw.

- [ ] **Step 3: Implement `StructuralContext.LowerChain`**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, add (after `LowerNot`):
```csharp
    public CteRef LowerChain(ChainedExpression chain, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        if (chain.Reversed)
        {
            throw new NotSupportedException("Reverse chain is not implemented yet -- see this plan's Task 9.");
        }

        var targetResourceType = chain.TargetResourceTypes switch
        {
            [var single] => single,
            _ => throw new NotSupportedException(
                $"Forward chain resolved to {chain.TargetResourceTypes.Length} candidate target types -- the real binder " +
                "always resolves forward chains to exactly one target type before this point (SearchKeyBinder.BindForward " +
                "throws ChainedParameterSpecifyType on genuine ambiguity), so this is unexpected input."),
        };

        var innerMatch = lowerNode(chain.Expression, this, targetResourceType);
        var referenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
        var innerResourceTypeId = _leafContext.ResourceTypeId(targetResourceType);
        var outputResourceTypeIds = chain.ResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

        _ctes.Add(new CteDefinition.ChainJoin(innerMatch, referenceSearchParamId, innerResourceTypeId, outputResourceTypeIds, ChainDirection.Forward));
        return new CteRef(_ctes.Count - 1);
    }
```
(The `Func<Expression, StructuralContext, string, CteRef> lowerNode` parameter exists because `StructuralContext` cannot call `Lower.LowerNode` directly -- `Lower.cs` is the caller of `StructuralContext`, not the reverse, matching this project's existing tier-2/dispatcher separation. `Lower.cs` passes its own `LowerNode` method reference in. This keeps `StructuralContext` from taking a hard dependency on `Lower`, avoiding a circular reference between the two.)

Add `using Ignixa.Search.Expressions;` if not already present (it already is, per the file's existing `SearchParameterInfo` usage).

- [ ] **Step 4: Wire `Lower.LowerNode`'s new case**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, add a case to `LowerNode`'s switch (before the generic `_ =>` fallback):
```csharp
        ChainedExpression chain => context.LowerChain(chain, LowerNode),
```

- [ ] **Step 5: Run to confirm it passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAForwardChainQuery" --nologo
```
Expected: PASS. If the golden `Explain()` string doesn't match exactly, hand-trace `LowerChain`/`EmitChainJoin`/`PrintCte`'s `ChainJoin` case against the real code rather than pasting whatever the actual output was -- confirm any divergence is a legitimate rendering detail (matching this project's established practice from prior increments), not a sign the underlying join logic is wrong.

- [ ] **Step 6: Run the full suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 failures, 0 regressions.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): forward chain lowering (organization.name=Acme)"
```

---

### Task 9: Reverse chain lowering + E2E proof

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `StructuralContext.LowerChain` (Task 8) -- extends the same method's `if (chain.Reversed)` branch rather than adding a parallel method.
- Produces: nothing new for later tasks -- both directions are now handled by one method.

- [ ] **Step 1: Write the failing E2E test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:
```csharp
    [Fact]
    public async Task GivenAReverseChainQuery_WhenCompiled_ThenChainJoinsWithOutputOnTheReferencedSide()
    {
        // Arrange -- Patient?_has:Observation:patient:code=1234-5
        var patientRefParam = new SearchParameterInfo("patient", "patient", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var innerPredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1234-5", text: null));
        var chain = new ChainedExpression(["Observation"], patientRefParam, ["Patient"], reversed: true, new SearchParameterExpression(codeParam, innerPredicate));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[patientRefParam.Url!.ToString()] = 77;
        resolver.SearchParamIds[codeParam.Url!.ToString()] = 88;
        resolver.ResourceTypeIds["Observation"] = 106;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(chain, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- the referencing-side match (Observation.code=1234-5) becomes cte0, the ChainJoin is root
        plan.Explain().ShouldBe(
            "cte0 = TokenSearchParam[106,88]  Code = @p0\n" +
            "root = ChainJoin(cte0, ref=77, inner=106, output=[103], Reverse)");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        emitted.Sql.ShouldNotContain("1234-5");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("1234-5"));
    }
```

- [ ] **Step 2: Run to confirm it fails**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAReverseChainQuery" --nologo
```
Expected: FAIL -- `LowerChain`'s `if (chain.Reversed)` branch throws `NotSupportedException("Reverse chain is not implemented yet...")`.

- [ ] **Step 3: Implement the reverse branch**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, replace `LowerChain`'s body:
```csharp
    public CteRef LowerChain(ChainedExpression chain, Func<Expression, StructuralContext, string, CteRef> lowerNode)
    {
        if (chain.Reversed)
        {
            var referencingResourceType = chain.ResourceTypes switch
            {
                [var single] => single,
                _ => throw new NotSupportedException(
                    $"Reverse chain's referencing side resolved to {chain.ResourceTypes.Length} types -- the real binder " +
                    "always binds a reverse chain's target expression against a single referencing type " +
                    "(SearchKeyBinder.BindReverse's syntax.SourceResourceType), so this is unexpected input."),
            };

            var innerMatch = lowerNode(chain.Expression, this, referencingResourceType);
            var referenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
            var innerResourceTypeId = _leafContext.ResourceTypeId(referencingResourceType);
            var outputResourceTypeIds = chain.TargetResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

            _ctes.Add(new CteDefinition.ChainJoin(innerMatch, referenceSearchParamId, innerResourceTypeId, outputResourceTypeIds, ChainDirection.Reverse));
            return new CteRef(_ctes.Count - 1);
        }

        var targetResourceType = chain.TargetResourceTypes switch
        {
            [var single] => single,
            _ => throw new NotSupportedException(
                $"Forward chain resolved to {chain.TargetResourceTypes.Length} candidate target types -- the real binder " +
                "always resolves forward chains to exactly one target type before this point (SearchKeyBinder.BindForward " +
                "throws ChainedParameterSpecifyType on genuine ambiguity), so this is unexpected input."),
        };

        var forwardInnerMatch = lowerNode(chain.Expression, this, targetResourceType);
        var forwardReferenceSearchParamId = _leafContext.SearchParamId(chain.ReferenceSearchParameter);
        var forwardInnerResourceTypeId = _leafContext.ResourceTypeId(targetResourceType);
        var forwardOutputResourceTypeIds = chain.ResourceTypes.Select(_leafContext.ResourceTypeId).ToList();

        _ctes.Add(new CteDefinition.ChainJoin(forwardInnerMatch, forwardReferenceSearchParamId, forwardInnerResourceTypeId, forwardOutputResourceTypeIds, ChainDirection.Forward));
        return new CteRef(_ctes.Count - 1);
    }
```
(The forward branch's local variables are renamed with a `forward` prefix here only to avoid shadowing warnings against the reverse branch's identically-purposed locals in the same method scope -- both branches remain otherwise identical to Task 8's version.)

- [ ] **Step 4: Run to confirm it passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAReverseChainQuery" --nologo
dotnet test All.sln --filter "FullyQualifiedName~GivenAForwardChainQuery" --nologo
```
Expected: both PASS -- confirms the shared method didn't regress the forward case.

- [ ] **Step 5: Run the full suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 failures, 0 regressions.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): reverse chain lowering (_has:Observation:patient:code=X)"
```

---

### Task 10: Nested chains + the 10-level depth guard + E2E proof

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `LowerChain` (Tasks 8-9) -- nested chains are proven to already work via recursive composition (`lowerNode` calling back into `LowerNode`, which has a `ChainedExpression` case that calls `LowerChain` again). This task adds the depth guard and proves nesting works; it does not change the core join logic.
- Produces: `NotSupportedException` for chains nested more than 10 levels deep.

- [ ] **Step 1: Write the failing depth-guard test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:
```csharp
    [Fact]
    public async Task GivenANestedChainTwoLevelsDeep_WhenCompiled_ThenComposesTwoChainJoins()
    {
        // Arrange -- Patient?organization.partof.name=Acme (Organization.partOf is itself a reference to Organization)
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var partOfParam = new SearchParameterInfo("partof", "partof", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Organization-partof"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        var innerChain = new ChainedExpression(["Organization"], partOfParam, ["Organization"], reversed: false, new SearchParameterExpression(nameParam, innerPredicate));
        var outerChain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, innerChain);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[partOfParam.Url!.ToString()] = 56;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(outerChain, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(outerChain, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- the innermost match (Organization.name=Acme) becomes cte0, the inner ChainJoin
        // (partof) becomes cte1 and is itself InnerMatch for the outer ChainJoin (organization).
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text = @p0\n" +
            "cte1 = ChainJoin(cte0, ref=56, inner=105, output=[105], Forward)\n" +
            "root = ChainJoin(cte1, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("Acme");
    }

    [Fact]
    public void GivenAChainNestedMoreThan10LevelsDeep_WhenCompiled_ThenThrows()
    {
        // Arrange -- build a chain 11 levels deep by wrapping a leaf predicate in ChainedExpression 11 times
        var refParam = new SearchParameterInfo("ref", "ref", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Organization-ref"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var innerPredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"));
        Expression current = new SearchParameterExpression(nameParam, innerPredicate);
        for (var i = 0; i < 11; i++)
        {
            current = new ChainedExpression(["Organization"], refParam, ["Organization"], reversed: false, current);
        }

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[refParam.Url!.ToString()] = 60;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act & Assert -- Resolve doesn't need to run for this test; Lower's depth guard is what's under test
        var symbolTable = new SymbolTable(
            new Dictionary<string, short> { [refParam.Url!.ToString()] = 60, [nameParam.Url!.ToString()] = 202 },
            new Dictionary<string, short> { ["Organization"] = 105 });

        Should.Throw<NotSupportedException>(() => Lower.Run(current, symbolTable, targetResourceType: "Organization"))
            .Message.ShouldContain("10");
    }
```

- [ ] **Step 2: Run to confirm the depth test fails (nesting test should already pass)**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenANestedChainTwoLevelsDeep" --nologo
dotnet test All.sln --filter "FullyQualifiedName~GivenAChainNestedMoreThan10LevelsDeep" --nologo
```
Expected: the two-level nesting test PASSES already (recursive composition falls out for free, per the design). The 11-level depth test FAILS -- no guard exists yet, so it either succeeds (builds an 11-CTE plan) or fails for an unrelated reason, not the intended `NotSupportedException` naming the limit.

- [ ] **Step 3: Implement the depth guard**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, add a private field and a check at the top of `LowerChain`:
```csharp
    private int _chainDepth;

    private const int MaxChainDepth = 10;
```
(Add these near the top of the class, alongside `_ctes`/`_leafContext`.)

At the very start of `LowerChain`'s body (before the `if (chain.Reversed)` check):
```csharp
        _chainDepth++;
        if (_chainDepth > MaxChainDepth)
        {
            throw new NotSupportedException(
                $"Chain nesting exceeds this compiler's 10-level depth guard -- this is a robustness ceiling against " +
                "SQL Server optimizer degradation under deeply nested CTE chains (see the chain design doc §8 for " +
                "the fhir-server precedent this mirrors), not a FHIR-spec limit. If a real query legitimately needs " +
                "more than 10 chain levels, this guard's threshold should be revisited deliberately, not silently raised.");
        }

        try
        {
            // ... existing LowerChain body (both branches) ...
        }
        finally
        {
            _chainDepth--;
        }
```
Wrap the entire existing method body (both the `if (chain.Reversed)` branch and the forward branch below it) in this `try`/`finally` -- the `finally` decrements `_chainDepth` so sibling (non-nested) chains in the same query don't accumulate depth across each other, only genuine nesting (one chain's `.Expression` recursively containing another `ChainedExpression`) does.

- [ ] **Step 4: Run to confirm both pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenANestedChainTwoLevelsDeep" --nologo
dotnet test All.sln --filter "FullyQualifiedName~GivenAChainNestedMoreThan10LevelsDeep" --nologo
```
Expected: both PASS.

- [ ] **Step 5: Run the full suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 failures, 0 regressions.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): prove nested chains compose for free; add a 10-level depth guard"
```

---

### Task 11: Multiary chain-target expressions + E2E proof

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `LowerChain` (Tasks 8-9) -- proven to already work via `lowerNode` (which is `Lower.LowerNode`, already handling `And`/`Or` via `LowerAnd`/`Union`) being the same function used for both the outer query and a chain's target expression.

No production code changes are expected in this task -- per the design (§1, "Nested chains and multiary target expressions need no new machinery"), this is a proof task. If the test below fails, that is a real signal something in Tasks 8-9's implementation is wrong (most likely: `lowerNode`'s threaded `resourceType` parameter not reaching a nested `And`/`Or` correctly) -- investigate and fix in this task rather than assuming a new mechanism is needed.

- [ ] **Step 1: Write the E2E test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:
```csharp
    [Fact]
    public async Task GivenAForwardChainWithAMultiaryTargetExpression_WhenCompiled_ThenIntersectsBothTargetPredicates()
    {
        // Arrange -- Patient?organization.name=Acme&organization.active=true
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Organization-active"));
        var targetExpression = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(nameParam, new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"))),
            new SearchParameterExpression(activeParam, new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null))),
        ]);
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(chain, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- both target predicates intersect into one InnerMatch before the ChainJoin
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[105,202]  Text = @p0\n" +
            "cte1 = TokenSearchParam[105,44]  Code = @p1\n" +
            "cte2 = Intersect(cte0, cte1)\n" +
            "root = ChainJoin(cte2, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("Acme");
        emitted.Sql.ShouldNotContain("true");
    }
```

- [ ] **Step 2: Run**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenAForwardChainWithAMultiaryTargetExpression" --nologo
```
Expected: PASS on the first run, with no production code changes, if Tasks 8-9's implementation is correct. If it fails, hand-trace `LowerChain`'s call to `lowerNode(chain.Expression, this, targetResourceType)` against `Lower.LowerNode`'s `MultiaryExpression { MultiaryOperation: MultiaryOperator.And }` case (`LowerAnd`) to find where the `resourceType` parameter or the `And`/`Or` composition breaks, and fix the actual bug in `StructuralContext.cs`/`Lower.cs` rather than adding new chain-specific multiary-handling code.

- [ ] **Step 3: Run the full suite**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 failures, 0 regressions.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(search-sql): prove multiary chain-target expressions compose via existing Intersect machinery"
```

---

### Task 12: Resource-column predicates inside a chain's target expression (both directions)

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `ResourceColumnLoweringRule.TryLower` (already exists, unchanged), `LowerChain` (Tasks 8-9).
- Produces: `CteDefinition.ResourceSource(short ResourceTypeId, Predicate? Predicate = null)` -- the top level's `OuterPredicate` mechanism is completely unchanged; this is purely additive for nested scopes.

- [ ] **Step 1: Add the optional `Predicate` field to `ResourceSource`**

In `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`, change:
```csharp
    public sealed record ResourceSource(short ResourceTypeId) : CteDefinition;
```
to:
```csharp
    public sealed record ResourceSource(short ResourceTypeId, Predicate? Predicate = null) : CteDefinition;
```
This is purely additive (a new optional trailing parameter with a default) -- every existing `new CteDefinition.ResourceSource(id)` call site (there is exactly one production call site, `StructuralContext.LowerResourceSource`) still compiles unchanged.

Update the class's XML doc comment: the sentence `"ResourceSource has no Predicate: ordinary resource-column filtering (_id/_type/_lastUpdated) is a separate mechanism, QueryPlan.OuterPredicate -- see that type's remarks."` is now only true for the TOP-LEVEL scope -- reword to: `"ResourceSource's Predicate is null at the top level (QueryPlan.OuterPredicate is the mechanism there, unchanged); a nested scope (a chain's target expression, which has no 'outer' WHERE to attach to) uses it directly, Intersected with any ordinary predicates in that scope -- see the chain design doc §5 for the full reasoning."`

- [ ] **Step 2: Update `Emit.EmitResourceSource` and `PlanExplainer`'s `ResourceSource` case**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, change:
```csharp
    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters)
        => $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.Resource\n" +
           $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)} AND IsHistory = 0 AND IsDeleted = 0";
```
to:
```csharp
    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<EmittedSqlParameter> parameters)
    {
        var predicateClause = rs.Predicate is null ? string.Empty : $" AND {EmitPredicate(rs.Predicate, parameters)}";
        return $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
               $"    FROM dbo.Resource\n" +
               $"    WHERE ResourceTypeId = {EmitParam(new SqlParameterRef(rs.ResourceTypeId), parameters)} AND IsHistory = 0 AND IsDeleted = 0{predicateClause}";
    }
```

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, change `PrintResourceSource`:
```csharp
    private static string PrintResourceSource(CteDefinition.ResourceSource rs, int? top, ref int parameterOrdinal)
    {
        // ResourceTypeId is a real bound parameter in Emit (EmitResourceSource), so this must consume
        // an ordinal too -- otherwise Explain()'s @pN numbering silently diverges from the emitted
        // SQL's real parameter numbering for any plan mixing a ResourceSource with another
        // parameterized CTE or an OuterPredicate. The literal ResourceTypeId is still shown inline
        // (not "@pN") because it reads better in a human-facing summary; only the counter is shared.
        parameterOrdinal++;
        return $"ResourceSource[{rs.ResourceTypeId}]{PrintTop(top)}";
    }
```
to:
```csharp
    private static string PrintResourceSource(CteDefinition.ResourceSource rs, int? top, ref int parameterOrdinal)
    {
        // ResourceTypeId is a real bound parameter in Emit (EmitResourceSource), so this must consume
        // an ordinal too -- otherwise Explain()'s @pN numbering silently diverges from the emitted
        // SQL's real parameter numbering for any plan mixing a ResourceSource with another
        // parameterized CTE or an OuterPredicate. The literal ResourceTypeId is still shown inline
        // (not "@pN") because it reads better in a human-facing summary; only the counter is shared.
        // rs.Predicate (nested-scope resource-column filter, e.g. a chain target's _id=X), when
        // present, is a real predicate rendered the same way OuterPredicate is -- it also consumes
        // whatever ordinals PrintPredicate consumes internally.
        parameterOrdinal++;
        var predicateSuffix = rs.Predicate is null ? string.Empty : $" WHERE {PrintPredicate(rs.Predicate, ref parameterOrdinal)}";
        return $"ResourceSource[{rs.ResourceTypeId}]{predicateSuffix}{PrintTop(top)}";
    }
```

- [ ] **Step 3: Write the failing E2E tests (both directions)**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:
```csharp
    [Fact]
    public async Task GivenAForwardChainWithAResourceColumnPredicateOnTheTarget_WhenCompiled_ThenIntersectsAFilteredResourceSource()
    {
        // Arrange -- Patient?organization._id=org-1
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var targetExpression = new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "org-1", text: null)));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(chain, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- the target scope's _id predicate becomes a filtered ResourceSource (not OuterPredicate,
        // which only applies at the true top level), the ChainJoin's InnerMatch is that ResourceSource directly
        // (no Intersect needed since _id was the target expression's only predicate).
        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[105] WHERE ResourceId = @p1\n" +
            "root = ChainJoin(cte0, ref=55, inner=105, output=[103], Forward)");
        emitted.Sql.ShouldNotContain("org-1");
    }

    [Fact]
    public async Task GivenAReverseChainWithAResourceColumnPredicateOnTheReferencingSide_WhenCompiled_ThenIntersectsAFilteredResourceSource()
    {
        // Arrange -- Patient?_has:Observation:patient:_id=obs-1
        var patientRefParam = new SearchParameterInfo("patient", "patient", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"));
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var targetExpression = new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "obs-1", text: null)));
        var chain = new ChainedExpression(["Observation"], patientRefParam, ["Patient"], reversed: true, targetExpression);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[patientRefParam.Url!.ToString()] = 77;
        resolver.ResourceTypeIds["Observation"] = 106;
        resolver.ResourceTypeIds["Patient"] = 103;

        // Act
        var symbolTable = await Resolve.RunAsync(chain, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(chain, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- identical mechanism to the forward case, just on the referencing (inner) side this time
        plan.Explain().ShouldBe(
            "cte0 = ResourceSource[106] WHERE ResourceId = @p1\n" +
            "root = ChainJoin(cte0, ref=77, inner=106, output=[103], Reverse)");
        emitted.Sql.ShouldNotContain("obs-1");
    }
```

- [ ] **Step 4: Run to confirm they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResourceColumnPredicateOnThe" --nologo
```
Expected: FAIL -- `LowerChain` calls `lowerNode(chain.Expression, this, targetResourceType)`, and `chain.Expression` here is a bare `_id`-only `SearchParameterExpression`, which reaches `Lower.LowerNode`'s generic dispatch and hits the choke-point guard in `StructuralContext.Lower`/`LowerComposite` (`RejectResourceColumnCode`), throwing `NotSupportedException` rather than lowering it.

- [ ] **Step 5: Make resource-column extraction scope-aware, by changing what callback `Lower.cs` passes into `LowerChain` -- not by changing `LowerChain` itself**

`StructuralContext.LowerChain` (Tasks 8-9) already takes its `lowerNode` behavior as a `Func<Expression, StructuralContext, string, CteRef>` parameter, supplied by its caller in `Lower.cs`. This step exploits that: instead of changing `LowerChain`'s own code, `Lower.cs` starts passing a DIFFERENT function -- one that runs the extraction pass before delegating to the ordinary `LowerNode` -- as that parameter. `LowerChain` itself needs **no changes** in this step.

First, expose `StructuralContext`'s `LeafContext` (needed so `Lower.cs`'s new function can call `ResourceColumnLoweringRule.TryLower` the same way the top-level extraction already does) and add a method to construct a `ResourceSource` carrying a predicate. In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, add (near `Ctes`):
```csharp
    public LeafContext LeafContext => _leafContext;
```
and add (after `LowerResourceSource`):
```csharp
    public CteRef LowerResourceSourceWithPredicate(string resourceType, Predicate? predicate)
    {
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        _ctes.Add(new CteDefinition.ResourceSource(resourceTypeId, predicate));
        return new CteRef(_ctes.Count - 1);
    }
```

Now, in `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, add a new private function alongside `LowerNode` (do not modify `LowerNode`, `LowerSearchParameter`, `LowerAnd`, or `ExtractResourceColumnPredicates`/`TryExtractResourceColumnPredicate` at all in this step -- they are reused exactly as they exist after Task 5):
```csharp
    private static CteRef LowerScopedExpression(Expression expression, StructuralContext context, string resourceType)
    {
        var (remaining, nestedPredicate) = ExtractResourceColumnPredicates(expression, context.LeafContext);
        if (remaining is null)
        {
            return context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate);
        }

        var ordinaryMatch = LowerNode(remaining, context, resourceType);
        return nestedPredicate is null
            ? ordinaryMatch
            : context.Intersect(context.LowerResourceSourceWithPredicate(resourceType, nestedPredicate), ordinaryMatch);
    }
```
This calls the SAME `ExtractResourceColumnPredicates` the top-level `Lower.Run` already uses (no duplication, no renaming) -- the only difference from the top level is what happens with a hit: the top level folds it into `QueryPlan.OuterPredicate` (via `Lower.Run`'s own existing code, untouched), this nested version folds it into a `ResourceSource`+`Predicate`, `Intersect`ed with any ordinary predicates also present in the same scope.

Change exactly one line in `LowerNode`'s switch -- the `ChainedExpression` arm (added in Task 8) changes from passing `LowerNode` itself to passing this new function:
```csharp
        ChainedExpression chain => context.LowerChain(chain, LowerScopedExpression),
```
(was `context.LowerChain(chain, LowerNode)`). Every other arm of `LowerNode`'s switch is unchanged.

`Lower.Run` itself is **completely unchanged** by this step -- it still calls `ExtractResourceColumnPredicates` once at the very top and folds a hit into `OuterPredicate`, exactly as it has since Task 5. That is the whole point: the top-level mechanism this project chose for performance reasons stays untouched; only chain-nested scopes (reached exclusively via `LowerChain`'s `lowerNode` callback) get the new `ResourceSource`+`Predicate` treatment.

- [ ] **Step 6: Run to confirm the new tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResourceColumnPredicateOnThe" --nologo
```
Expected: PASS for both directions, via the exact same `LowerScopedExpression` function regardless of `ChainDirection` -- `LowerChain` doesn't know or care that its `lowerNode` callback now does extraction, and `LowerScopedExpression` doesn't know or care whether it's running for a forward or reverse chain's target expression. No `if (chain.Reversed)`-style branching exists anywhere in this step's code.

- [ ] **Step 7: Run the full suite**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```
Expected: 0 warnings, 0 errors, zero regressions across every prior task's tests (Tasks 1-11), since `Lower.Run`'s own top-level extraction call and the `OuterPredicate` mechanism are completely untouched by this task -- only the `ChainedExpression` arm's callback changed.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(search-sql): resource-column predicates inside a chain's target expression (both directions)

ResourceSource gains an optional Predicate field, used only in nested
(chain) scope -- the top level's OuterPredicate mechanism is completely
unchanged. One shared extraction+composition function (Lower.
LowerScopedExpression, passed into LowerChain as its lowerNode
callback) handles both forward and reverse chains identically, since
neither needs the second dbo.Resource join fhir-server's own chain
implementation requires for this case -- InnerMatch is already an
ordinary Intersect-composed CTE regardless of which side of the
reference it represents."
```

---

### Task 13: Combined proof + full regression + final whole-branch review prep

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-12.
- Produces: no new production code -- this task is proof, not implementation, matching the prior increment's final task.

- [ ] **Step 1: Write one combined E2E test exercising several mechanisms together**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`:
```csharp
    [Fact]
    public async Task GivenAForwardChainCombinedWithAnOrdinaryPredicateAndResourceColumnOnTheOuterQuery_WhenCompiled_ThenComposesAllThreeMechanisms()
    {
        // Arrange -- Patient?_id=pt-1&active=true&organization.name=Acme
        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var orgParam = new SearchParameterInfo("organization", "organization", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Organization-name"));
        var chain = new ChainedExpression(["Patient"], orgParam, ["Organization"], reversed: false,
            new SearchParameterExpression(nameParam, new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Acme"))));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterExpression(idParam, new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "pt-1", text: null))),
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
            chain,
        ]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;
        resolver.SearchParamIds[orgParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Organization"] = 105;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, targetResourceType: "Patient", CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, targetResourceType: "Patient");
        var emitted = Emit.Run(plan);

        // Assert -- _id is extracted to the outer WHERE (top-level mechanism, unchanged); active and
        // the chain intersect into the match CTE.
        emitted.Sql.ShouldContain("INNER JOIN dbo.Resource r ON r.ResourceTypeId = m.T1 AND r.ResourceSurrogateId = m.Sid1");
        emitted.Sql.ShouldContain("FROM dbo.ReferenceSearchParam rsp");
        plan.OuterPredicate.ShouldNotBeNull();
        emitted.Sql.ShouldNotContain("pt-1");
        emitted.Sql.ShouldNotContain("true");
        emitted.Sql.ShouldNotContain("Acme");
    }
```

- [ ] **Step 2: Run to confirm it passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ThreeMechanisms" --nologo
```
Expected: PASS. If the golden shape doesn't match your expectation, hand-trace `Lower.Run`/`ExtractResourceColumnPredicates`/`LowerAnd`/`LowerChain` against the real code rather than adjusting the test to whatever the actual (possibly wrong) output is.

- [ ] **Step 3: Full solution build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```
Expected: 0 warnings, 0 errors. The only failures should be the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures per target framework -- confirm no new failures anywhere in the solution, not just `Ignixa.Search.Sql.Tests`.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test(search-sql): prove chain composes with ordinary predicates and top-level resource-column predicates in one query

Patient?_id=pt-1&active=true&organization.name=Acme exercises the
top-level OuterPredicate mechanism (unchanged since the :not/resource-
column-predicates increment), an ordinary ParamSource predicate, and a
forward ChainJoin all in one plan -- confirming they're genuinely
independent, composable pieces rather than accidentally-working special
cases."
```

---

## Self-Review

**Spec coverage:** Every §-numbered section of `docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-chain-design.md` maps to a task: §1-2 (binder/schema ground truth) inform Tasks 6-9's code directly, no dedicated task needed (they're not implementation, they're the evidence base). §3 (`ChainJoin` node) → Task 7. §4 (scope threading) → Task 5. §5 (`ParamSource` fix) → Tasks 1-4. §6 (resource-column predicates in chain scope) → Task 12. §7 (`SymbolCollectingVisitor`) → Task 6. §8 (complexity guard) → Task 10. §9 (in-scope/deferred list) → reflected in this plan's Global Constraints (deferred items produce the existing generic `NotSupportedException`, no new code). Appendix A (include sketch) and Appendix B (SMART/compartment analysis) are explicitly forward-looking, not implemented by this plan, and this plan makes no attempt to build toward them, per the design's own explicit scope boundary.

**Placeholder scan:** No TBD/TODO. Task 12's Step 5 is the plan's most structurally complex step (adding `Lower.LowerScopedExpression` as a new `lowerNode`-shaped callback passed into `LowerChain`, reusing `ExtractResourceColumnPredicates` unchanged rather than duplicating or moving it) -- verified it gives complete code for every changed method, not a description of what to do. An earlier draft of this step contained a genuine placeholder (an incomplete `LowerScopedExpression` sketch with a stray comment) and a self-contradictory intermediate design (two incompatible versions of `Lower.Run`); both were caught and replaced with the single coherent design in the final text above.

**Type consistency:** `StructuralContext.LowerChain(ChainedExpression, Func<Expression, StructuralContext, string, CteRef>)`'s signature, introduced in Task 8, is never changed -- Task 12 makes chain-target resource-column predicates work purely by changing which function `Lower.cs` passes as that `Func` argument (`LowerScopedExpression` instead of `LowerNode`), so `LowerChain`'s own code is identical from Task 8 through Task 13. `CteDefinition.ChainJoin`'s 5-field shape (Task 7) is used identically in Tasks 8, 9, 12, 13 -- `InnerResourceTypeId` is always a single resolved `short`, `OutputResourceTypeIds` is always `IReadOnlyList<short>`, matching §3's cardinality invariant from the design doc throughout. `ParamSource`'s 4-field shape (Task 1) is used identically in every leaf/composite rule from Task 1 through Task 3, and never referenced positionally without naming after Task 1 (every construction site in this plan uses the same `(table, resourceTypeId, searchParamIdExpr, predicateExpr)` positional order).
