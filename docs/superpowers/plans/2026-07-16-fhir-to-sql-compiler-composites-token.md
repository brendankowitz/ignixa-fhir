# Composite Lowering: TokenToken + TokenNumberNumber Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lower the two structurally simplest composite search parameter types (TokenToken, TokenNumberNumber) to `ParamSource` CTEs, and wire the general `SearchParameterExpression`/`CompositeComponentExpression` dispatch mechanism that every future composite type will reuse.

**Architecture:** The real binder (`SearchExpressionBinder.BindComposite`) produces `SearchParameterExpression(compositeParam, And(CompositeComponentExpression, CompositeComponentExpression, ...))` (or `Or(And(...), And(...))` for comma-separated composite values) — confirmed by direct read of `SearchExpressionBinder.cs:186-236` and `docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md:90`. Two new architectural pieces are required, both real gaps today (not composite-specific hacks):

1. **`Resolve`/`SymbolCollectingVisitor` never collects a `SearchParameterExpression`'s own `.Parameter`** — only `SearchParameterPredicateExpression`/`CompositeComponentExpression` are collected today. A composite's own `SearchParamId` (the id stored in every row of its composite table) is only reachable through this wrapper, so this must be fixed for composites to resolve at all.
2. **`Lower.LowerNode` has no case for `SearchParameterExpression`** — it only handles bare `SearchParameterPredicateExpression`/`MultiaryExpression{And|Or}`. This is a general gap (affects every real-binder-driven query, not just composites) that prior increments never hit because their tests hand-build the typed leaf directly, bypassing the binder. This plan closes it as a side effect of correctly threading composite parameter identity through to `Lower`.

Once `SearchParameterExpression` is unwrapped, composite detection happens at exactly one point: a `MultiaryExpression{And}` whose children are *all* `CompositeComponentExpression` is a composite's component list, not an ordinary multi-parameter AND — it must be consumed by a composite-specific handler *before* the generic `And` → `Intersect` case ever sees its children (consuming leaves from different tables independently would be both wrong-table and semantically wrong: "some row has the code, some row has the value" instead of one row having both). Each composite type gets its own small `*LoweringRule` (mirroring `TokenLoweringRule`/`NumberLoweringRule`'s existing per-type-rule style, not a generic `ColumnRole` abstraction — with only two types in scope, a generic role-mapping layer would be premature; the DDL divergences documented for the other four composite types (`TokenString`'s different collation, `TokenQuantity`'s nullable Low/High) can inform that abstraction later if a third+ type needs it).

**Tech Stack:** C#/.NET 9, xUnit + Shouldly, existing `Ignixa.Search.Sql`/`Ignixa.Search.Sql.Tests` projects (no new projects).

## Global Constraints

- Composite value comparison only — `System`-qualified token components throw `NotSupportedException`, matching `TokenLoweringRule`'s existing precedent exactly (same underlying gap: `ISymbolResolver` has no `SystemId` resolution yet).
- `:ap` on the Number components of `TokenNumberNumber` throws, same as `NumberLoweringRule` (via reused `NumericRangeComparison`).
- A composite component whose `WrappedExpression` is not a bare `SearchParameterPredicateExpression` (i.e., a component with its own comma-separated alternatives) throws `NotSupportedException` rather than being silently mishandled — this is a real, if rare, shape per `CompositeComponentExpression`'s own doc comment ("the wrapped expression is frequently a `MultiaryExpression`").
- Only `TokenTokenCompositeSearchParam` (`Code1`/`Code2`, both `SystemId1`/`SystemId2 INT NULL`) and `TokenNumberNumberCompositeSearchParam` (`Code1`, `LowValue2`/`HighValue2`, `LowValue3`/`HighValue3`) are in scope. `TokenString`, `TokenQuantity`, `TokenDateTime`, `ReferenceToken` remain unimplemented — `CompositeLoweringDispatcher` throws `NotSupportedException` for their value-type signatures.
- `SqlCatalog.Default.Table("TokenTokenCompositeSearchParam")` / `.Table("TokenNumberNumberCompositeSearchParam")` already exist (generated from `97.sql` — both table names end with `SearchParam`, already covered by the generator's existing filter and by `SqlCatalogTests.cs`'s existing facts). No `SqlCatalog`/generator changes in this plan.
- No `Predicate`/`Emit`/`PlanExplainer` changes — a composite's `Predicate` tree is built entirely from existing `Predicate.Equal`/`And`/`LessThan`/etc. cases; `Emit`'s `EmitParamSource` and `PlanExplainer` are already generic over arbitrary `Predicate` shapes.
- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing (the `Ignixa.SqlOnFhir.Tests` submodule failures are pre-existing and out of scope, per every prior increment in this branch).

---

### Task 1: `SymbolCollectingVisitor.VisitSearchParameter`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Modify: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`

**Interfaces:**
- Consumes: `SearchParameterExpression` (`Ignixa.Search.Expressions`, properties `Parameter`/`Expression`), `ExpressionRewriter<object?>.VisitSearchParameter` (base recursion, already walks `.Expression`).
- Produces: `SymbolCollectingVisitor.Parameters` now also contains a composite's own `SearchParameterInfo`, not just its components'. `SymbolCollectingVisitor` is `internal` with no `InternalsVisibleTo` granting the test project access (confirmed: no `InternalsVisibleTo` exists anywhere in `Ignixa.Search.Sql`/its csproj) — this must be tested through `Resolve.RunAsync`'s public surface, not by instantiating the visitor directly.

- [ ] **Step 1: Read the existing composite-resolution test**

`test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs` already has `GivenACompositeTree_WhenResolved_ThenBothComponentsAreResolved` (around line 31), building the exact tree shape this task needs: `SearchParameterExpression(compositeParam, MultiaryExpression(And, [CompositeComponentExpression(codeParam, 0, ...), CompositeComponentExpression(quantityParam, 1, ...)]))`. It currently only asserts the two *components* resolve — the gap this task closes is that the composite's own `compositeParam` never gets a `SearchParamId` at all. Extend this existing test rather than writing a new one.

- [ ] **Step 2: Extend the test to assert the composite's own id resolves**

In `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`, inside `GivenACompositeTree_WhenResolved_ThenBothComponentsAreResolved`, add one resolver entry and one assertion:

```csharp
// Add alongside the existing two resolver.SearchParamIds[...] lines:
resolver.SearchParamIds["http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"] = 400;
```

```csharp
// Add alongside the existing two symbolTable.SearchParamId(...) assertions:
symbolTable.SearchParamId(compositeParam).ShouldBe((short)400);
```

Rename the test method to reflect the expanded assertion: `GivenACompositeTree_WhenResolved_ThenTheCompositeAndBothComponentsAreResolved`.

- [ ] **Step 3: Run test to verify it fails**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResolveTests" --nologo
```

Expected: FAIL — `symbolTable.SearchParamId(compositeParam)` throws `KeyNotFoundException` (the composite's own parameter was never collected, so `Resolve.RunAsync` never looked it up, so it's absent from the table).

- [ ] **Step 4: Add the override**

```csharp
// src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs
// Add this method to the existing class, alongside VisitSearchParameterPredicate/VisitCompositeComponent:

    public override Expression VisitSearchParameter(SearchParameterExpression expression, object? context)
    {
        Parameters.Add(expression.Parameter);
        return base.VisitSearchParameter(expression, context);
    }
```

Update the class's `<remarks>` doc comment: the note "Scoped to `SearchParameterPredicateExpression` and `CompositeComponentExpression`" is no longer accurate — replace with a line noting `VisitSearchParameter` is now overridden specifically to collect a composite's own identity (its `SearchParamId` is otherwise unreachable, since it lives only on the `SearchParameterExpression` wrapper, never on any leaf beneath it), and that `base.VisitSearchParameter` is called to preserve the existing recursion into `.Expression` (which already reaches every `SearchParameterPredicateExpression`/`CompositeComponentExpression` beneath it via the other two overrides).

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResolveTests" --nologo
```

Expected: 0 warnings, 0 errors, all passing -- including the other three pre-existing `ResolveTests` facts (an atomic query's own parameter is now collected twice -- once via `VisitSearchParameterPredicate`, once via this new `VisitSearchParameter` override -- into the same `HashSet<SearchParameterInfo>`, which is harmless, but confirm none of them assert an exact `Parameters.Count` that this would break; none do as of this plan's writing, since `Parameters` is not `public` and `ResolveTests` only ever asserts through `SymbolTable.SearchParamId`/`ResourceTypeId`).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): collect a composite parameter's own identity in SymbolCollectingVisitor

SearchParameterExpression.Parameter (the composite's own SearchParameterInfo)
was never collected -- only its components were, via the existing
VisitCompositeComponent override. A composite's own SearchParamId (the id
every row in its composite table carries) is only reachable through this
wrapper, so composite lowering cannot resolve without this."
```

---

### Task 2: `TokenTokenLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenTokenLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenTokenLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext`, `TokenSearchValue`, `SearchParameterPredicateExpression`, `SqlCatalog.Default.Table("TokenTokenCompositeSearchParam")`.
- Produces: `TokenTokenLoweringRule.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<SearchParameterPredicateExpression> components, LeafContext context): CteDefinition.ParamSource`. `components[0]` is position 0 (→ `Code1`), `components[1]` is position 1 (→ `Code2`) — the caller (Task 4's `CompositeLoweringDispatcher`) is responsible for ordering by `Position` before calling.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/TokenTokenLoweringRuleTests.cs
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

public class TokenTokenLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://hl7.org/fhir/SearchParameter/Observation-{code}"));

    private static SearchParameterPredicateExpression TokenComponent(string code, string? system, string? tokenCode)
        => new(ComponentParameter(code), SearchComparator.Eq, modifier: null, new TokenSearchValue(system, tokenCode, text: null));

    [Fact]
    public void GivenTwoCodeOnlyTokenComponents_WhenLowered_ThenComparesBothCodeColumnsOnTheCompositeTable()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[]
        {
            TokenComponent("code", system: null, tokenCode: "8480-6"),
            TokenComponent("value-concept", system: null, tokenCode: "high"),
        };

        // Act
        var cte = TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301));

        // Assert
        cte.SearchParamId.ShouldBe((short)301);
        cte.Table.TableName.ShouldBe("TokenTokenCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var left = and.Left.ShouldBeOfType<Predicate.Equal>();
        left.Column.Column.ShouldBe("Code1");
        left.Value.Value.ShouldBe("8480-6");
        var right = and.Right.ShouldBeOfType<Predicate.Equal>();
        right.Column.Column.ShouldBe("Code2");
        right.Value.Value.ShouldBe("high");
    }

    [Fact]
    public void GivenASystemQualifiedFirstComponent_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheSystem()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[]
        {
            TokenComponent("code", system: "http://loinc.org", tokenCode: "8480-6"),
            TokenComponent("value-concept", system: null, tokenCode: "high"),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301)));
    }

    [Fact]
    public void GivenATextOnlySecondComponent_WhenLowered_ThenThrowsRatherThanProducingAnUnconstrainedMatch()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[]
        {
            TokenComponent("code", system: null, tokenCode: "8480-6"),
            TokenComponent("value-concept", system: null, tokenCode: null),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 301)));
    }
}
```

Verify `TokenSearchValue`'s constructor parameter order/names against the existing `TokenLoweringRuleTests.cs` (if present) or `TokenSearchValue.cs` directly before running — correct the test construction if it disagrees.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenTokenLoweringRuleTests" --nologo
```

Expected: FAIL with "TokenTokenLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `TokenTokenLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenTokenLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenToken composite to a single ParamSource over TokenTokenCompositeSearchParam --
/// components[0] compares Code1, components[1] compares Code2. Code-only case only, same as
/// TokenLoweringRule: System-qualified components (including System = string.Empty) and
/// text-only components (no Code) both throw rather than silently producing a wrong-scope or
/// always-false predicate.
/// </summary>
public static class TokenTokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenTokenCompositeSearchParam");
        var predicate = new Predicate.And(
            TokenColumnEquals(table, "Code1", (TokenSearchValue)components[0].Value, context),
            TokenColumnEquals(table, "Code2", (TokenSearchValue)components[1].Value, context));

        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate TokenColumnEquals(TableDescriptor table, string codeColumn, TokenSearchValue value, LeafContext context)
    {
        if (value.System is not null)
        {
            throw new NotSupportedException(
                "System-qualified token components are not supported yet -- same SystemId resolution gap as " +
                "TokenLoweringRule (ISymbolResolver has no SystemId lookup). This includes System = string.Empty " +
                "(\"|code\" syntax, meaning system must be absent), which this rule cannot express either.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "This rule only supports code-bearing token components -- text-only components (Code is null/empty) " +
                "are not supported yet.");
        }

        var column = new SqlColumnRef(table.TableName, codeColumn);
        return new Predicate.Equal(column, context.Parameter(value.Code));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenTokenLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add TokenTokenLoweringRule

Mirrors TokenLoweringRule's code-only, throw-on-System/throw-on-text-only-code
semantics, applied independently to Code1/Code2 on the composite table."
```

---

### Task 3: `TokenNumberNumberLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenNumberNumberLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenNumberNumberLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext`, `NumericRangeComparison.Build` (existing, `internal static`, same project), `TokenSearchValue`, `NumberSearchValue`, `SqlCatalog.Default.Table("TokenNumberNumberCompositeSearchParam")`.
- Produces: `TokenNumberNumberLoweringRule.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<SearchParameterPredicateExpression> components, LeafContext context): CteDefinition.ParamSource`. `components[0]` is the Token slot (→ `Code1`), `components[1]` is the first Number slot (→ `LowValue2`/`HighValue2`), `components[2]` is the second Number slot (→ `LowValue3`/`HighValue3`).

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/TokenNumberNumberLoweringRuleTests.cs
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

public class TokenNumberNumberLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("component-code-value-number-number", "component-code-value-number-number", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-component-code-value-number-number"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenACodeAndTwoUnqualifiedNumberComponents_WhenLowered_ThenComparesCode1AndBothLowHighPairs()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act
        var cte = TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302));

        // Assert
        cte.SearchParamId.ShouldBe((short)302);
        cte.Table.TableName.ShouldBe("TokenNumberNumberCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = inner.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        tokenPredicate.Value.Value.ShouldBe("8480-6");
        var number1Predicate = inner.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        number1Predicate.Column.Column.ShouldBe("LowValue2");
        var number2Predicate = outer.Right.ShouldBeOfType<Predicate.LessThanOrEqual>();
        number2Predicate.Column.Column.ShouldBe("HighValue3");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new SearchParameterPredicateExpression[]
        {
            new(ComponentParameter("code"), SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(ComponentParameter("low"), SearchComparator.Ge, modifier: null, new NumberSearchValue(5m)),
            new(ComponentParameter("high"), SearchComparator.Le, modifier: null, new NumberSearchValue(10m)),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenNumberNumberLoweringRule.Lower(composite, components, ContextResolving(composite, 302)));
    }
}
```

Verify `NumberSearchValue`'s real constructor (single-decimal-argument form, as used by `NumberLoweringRuleTests.cs`) before running — correct the test construction if it disagrees.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenNumberNumberLoweringRuleTests" --nologo
```

Expected: FAIL with "TokenNumberNumberLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `TokenNumberNumberLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenNumberNumberLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenNumberNumber composite to a single ParamSource over
/// TokenNumberNumberCompositeSearchParam -- components[0] is the token slot (Code1, code-only,
/// same throw rules as TokenLoweringRule), components[1]/[2] are the two number slots (LowValue2/
/// HighValue2, LowValue3/HighValue3), reusing NumericRangeComparison unchanged -- same range
/// semantics as NumberLoweringRule, just against composite-table column names.
/// </summary>
public static class TokenNumberNumberLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenNumberNumberCompositeSearchParam");

        var tokenPredicate = TokenColumnEquals(table, (TokenSearchValue)components[0].Value, context);
        var number1Predicate = NumberRangePredicate(table, "LowValue2", "HighValue2", components[1], context);
        var number2Predicate = NumberRangePredicate(table, "LowValue3", "HighValue3", components[2], context);

        var predicate = new Predicate.And(new Predicate.And(tokenPredicate, number1Predicate), number2Predicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate TokenColumnEquals(TableDescriptor table, TokenSearchValue value, LeafContext context)
    {
        if (value.System is not null)
        {
            throw new NotSupportedException(
                "System-qualified token components are not supported yet -- same SystemId resolution gap as " +
                "TokenLoweringRule (ISymbolResolver has no SystemId lookup). This includes System = string.Empty " +
                "(\"|code\" syntax, meaning system must be absent), which this rule cannot express either.");
        }

        if (string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "This rule only supports code-bearing token components -- text-only components (Code is null/empty) " +
                "are not supported yet.");
        }

        var column = new SqlColumnRef(table.TableName, "Code1");
        return new Predicate.Equal(column, context.Parameter(value.Code));
    }

    private static Predicate NumberRangePredicate(
        TableDescriptor table, string lowColumnName, string highColumnName, SearchParameterPredicateExpression component, LeafContext context)
    {
        var value = (NumberSearchValue)component.Value;
        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("NumberSearchValue has neither Low nor High set.");
        var lowColumn = new SqlColumnRef(table.TableName, lowColumnName);
        var highColumn = new SqlColumnRef(table.TableName, highColumnName);
        return NumericRangeComparison.Build(context, lowColumn, highColumn, component.Comparator, comparisonValue);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenNumberNumberLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add TokenNumberNumberLoweringRule

Reuses NumericRangeComparison from the Number/Quantity increment unchanged --
identical range semantics, applied twice against the composite table's
LowValue2/HighValue2 and LowValue3/HighValue3 column pairs."
```

---

### Task 4: `CompositeLoweringDispatcher`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/CompositeLoweringDispatcherTests.cs`

**Interfaces:**
- Consumes: `CompositeComponentExpression` (`Ignixa.Search.Expressions`, properties `ComponentSearchParameter`/`Position`/`WrappedExpression`), `TokenTokenLoweringRule.Lower`/`TokenNumberNumberLoweringRule.Lower` (Tasks 2-3).
- Produces: `CompositeLoweringDispatcher.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components, LeafContext context): CteDefinition.ParamSource`. This is the method Task 5's `StructuralContext.LowerComposite` calls.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/CompositeLoweringDispatcherTests.cs
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

public class CompositeLoweringDispatcherTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter(string code)
        => new(code, code, SearchParamType.Composite, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    private static CompositeComponentExpression TokenComponentAt(int position, string paramCode, string tokenCode)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: tokenCode, text: null)));
    }

    private static CompositeComponentExpression NumberComponentAt(int position, string paramCode, decimal value)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new NumberSearchValue(value)));
    }

    [Fact]
    public void GivenTwoTokenComponents_WhenDispatched_ThenRoutesToTokenTokenLoweringRule()
    {
        // Arrange
        var composite = CompositeParameter("code-value-concept");
        var components = new[]
        {
            TokenComponentAt(0, "code", "8480-6"),
            TokenComponentAt(1, "value-concept", "high"),
        };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 301));

        // Assert
        cte.Table.TableName.ShouldBe("TokenTokenCompositeSearchParam");
    }

    [Fact]
    public void GivenAOutOfOrderTokenThenTwoNumberComponents_WhenDispatched_ThenOrdersByPositionBeforeRoutingToTokenNumberNumber()
    {
        // Arrange -- constructed out of Position order to prove the dispatcher sorts, not trusts input order
        var composite = CompositeParameter("component-code-value-number-number");
        var components = new[]
        {
            NumberComponentAt(2, "high", 10m),
            TokenComponentAt(0, "code", "8480-6"),
            NumberComponentAt(1, "low", 5m),
        };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 302));

        // Assert
        cte.Table.TableName.ShouldBe("TokenNumberNumberCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var inner = outer.Left.ShouldBeOfType<Predicate.And>();
        inner.Left.ShouldBeOfType<Predicate.Equal>().Value.Value.ShouldBe("8480-6");
    }

    [Fact]
    public void GivenAnUnsupportedComponentTypeCombination_WhenDispatched_ThenThrows()
    {
        // Arrange -- three token components has no composite table
        var composite = CompositeParameter("unsupported");
        var components = new[]
        {
            TokenComponentAt(0, "a", "1"),
            TokenComponentAt(1, "b", "2"),
            TokenComponentAt(2, "c", "3"),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 303)));
    }

    [Fact]
    public void GivenAComponentWrappingAMultiaryExpressionInsteadOfAPredicate_WhenDispatched_ThenThrowsRatherThanCrashing()
    {
        // Arrange -- a component with its own comma-separated alternatives; CompositeComponentExpression's own
        // doc comment notes WrappedExpression is "frequently a MultiaryExpression" in that case. A single-element
        // Or is enough to prove the shape mismatch -- MultiaryExpression's constructor rejects an empty list.
        var composite = CompositeParameter("code-value-concept");
        var codeParam = ComponentParameter("code");
        var alternativePredicate = new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "1", text: null));
        var components = new[]
        {
            new CompositeComponentExpression(codeParam, 0, new MultiaryExpression(MultiaryOperator.Or, [alternativePredicate])),
            TokenComponentAt(1, "value-concept", "high"),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 301)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~CompositeLoweringDispatcherTests" --nologo
```

Expected: FAIL with "CompositeLoweringDispatcher does not exist" (compile error).

- [ ] **Step 3: Implement `CompositeLoweringDispatcher`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Dispatches a composite's ordered components to their tier-1 composite lowering rule, by the
/// runtime type of each component's wrapped ISearchValue. Orders by Position first -- callers
/// (real binder output, and this plan's own tests) are not required to hand components in order.
/// Only TokenToken and TokenNumberNumber are wired; every other composite table (TokenString,
/// TokenQuantity, TokenDateTime, ReferenceToken) throws NotSupportedException, matching
/// LeafLoweringDispatcher's precedent of a loud, explicit gap over a silent wrong answer.
/// </summary>
public static class CompositeLoweringDispatcher
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<CompositeComponentExpression> components,
        LeafContext context)
    {
        var ordered = components.OrderBy(c => c.Position).ToList();
        var predicates = new SearchParameterPredicateExpression[ordered.Count];
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].WrappedExpression is not SearchParameterPredicateExpression predicate)
            {
                throw new NotSupportedException(
                    $"Composite component at position {ordered[i].Position} on '{compositeParameter.Code}' wraps a " +
                    $"{ordered[i].WrappedExpression.GetType().Name}, not a SearchParameterPredicateExpression -- only " +
                    "single-valued components are supported (a component with its own comma-separated alternatives is not).");
            }

            predicates[i] = predicate;
        }

        return predicates.Select(p => p.Value).ToArray() switch
        {
        [TokenSearchValue, TokenSearchValue] => TokenTokenLoweringRule.Lower(compositeParameter, predicates, context),
        [TokenSearchValue, NumberSearchValue, NumberSearchValue] => TokenNumberNumberLoweringRule.Lower(compositeParameter, predicates, context),
            var values => throw new NotSupportedException(
                $"No composite lowering rule for component value types [{string.Join(", ", values.Select(v => v.GetType().Name))}] " +
                $"on composite parameter '{compositeParameter.Code}'."),
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~CompositeLoweringDispatcherTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add CompositeLoweringDispatcher for TokenToken/TokenNumberNumber

Orders components by Position, validates each wraps a bare
SearchParameterPredicateExpression, then routes by the ordered tuple of
component value types. Every other composite shape throws -- explicit gap,
not a silent wrong answer."
```

---

### Task 5: Wire `SearchParameterExpression` into `Lower`, prove end to end

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `CompositeLoweringDispatcher.Lower` (Task 4), `SearchParameterExpression` (`Ignixa.Search.Expressions`).
- Produces: `StructuralContext.LowerComposite(SearchParameterInfo, IReadOnlyList<CompositeComponentExpression>): CteRef`. `Lower.LowerNode` now handles `SearchParameterExpression` for both composite and non-composite bodies.

- [ ] **Step 1: Add `StructuralContext.LowerComposite`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs
// Add alongside the existing Lower(SearchParameterPredicateExpression) method:

    public CteRef LowerComposite(SearchParameterInfo compositeParameter, IReadOnlyList<CompositeComponentExpression> components)
    {
        var cte = CompositeLoweringDispatcher.Lower(compositeParameter, components, _leafContext);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }
```

Add `using Ignixa.Search.Models;` to the file's usings if not already present (for `SearchParameterInfo`).

- [ ] **Step 2: Write the failing E2E tests**

Add two new test methods to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, in the same style as the existing three (same `FakeSymbolResolver`, same `Resolve.RunAsync` → `Lower.Run` → `Emit.Run` pipeline). Add these `using`s if not already present: `using Ignixa.Search.Sql.Lowering;` (for none extra needed beyond what's already imported by this file).

```csharp
    [Fact]
    public async Task GivenAnObservationTokenTokenCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?code-value-concept=8480-6$high
        var compositeParam = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(valueParam, 1,
                    new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "high", text: null))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenTokenCompositeSearchParam[301]  Code1 = @p0 AND Code2 = @p1");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Sql.ShouldNotContain("high");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"8480-6"), ("@p1", (object)"high")]);
    }

    [Fact]
    public async Task GivenAnObservationTokenNumberNumberCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?component-code-value-number-number=8480-6$ge5$le10
        var compositeParam = new SearchParameterInfo(
            "component-code-value-number-number", "component-code-value-number-number", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-component-code-value-number-number"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/Observation-code"));
        var lowParam = new SearchParameterInfo("low", "low", SearchParamType.Number, new Uri("http://example.org/fhir/SearchParameter/Observation-low"));
        var highParam = new SearchParameterInfo("high", "high", SearchParamType.Number, new Uri("http://example.org/fhir/SearchParameter/Observation-high"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(lowParam, 1,
                    new SearchParameterPredicateExpression(lowParam, SearchComparator.Ge, modifier: null, new NumberSearchValue(5m))),
                new CompositeComponentExpression(highParam, 2,
                    new SearchParameterPredicateExpression(highParam, SearchComparator.Le, modifier: null, new NumberSearchValue(10m))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 302;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenNumberNumberCompositeSearchParam[302]  Code1 = @p0 AND LowValue2 >= @p1 AND HighValue3 <= @p2");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"8480-6"), ("@p1", 5m), ("@p2", 10m)]);
    }

    [Fact]
    public async Task GivenACommaSeparatedCompositeAlternatives_WhenCompiled_ThenUnionsOneParamSourcePerAlternative()
    {
        // Arrange -- Observation?code-value-concept=A$1,B$2 (two comma-separated composite values -- SearchParameterExpression(composite, Or(And(...), And(...))))
        var compositeParam = new SearchParameterInfo(
            "code-value-concept", "code-value-concept", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-code-value-concept"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-concept", "value-concept", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-concept"));

        CompositeComponentExpression[] Alternative(string code, string value) =>
        [
            new(codeParam, 0, new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: code, text: null))),
            new(valueParam, 1, new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: value, text: null))),
        ];

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.Or,
            [
                new MultiaryExpression(MultiaryOperator.And, Alternative("A", "1")),
                new MultiaryExpression(MultiaryOperator.And, Alternative("B", "2")),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 301;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);

        // Assert -- two ParamSource CTEs (one per alternative), unioned at the root
        plan.Explain().ShouldBe(
            "cte0 = TokenTokenCompositeSearchParam[301]  Code1 = @p0 AND Code2 = @p1\n" +
            "cte1 = TokenTokenCompositeSearchParam[301]  Code1 = @p2 AND Code2 = @p3\n" +
            "root = Union(cte0, cte1)");
    }
```

The exact `Explain()` string literals above are confirmed against `PlanExplainer.cs`'s source directly: `Predicate.And` renders as `"{Left} AND {Right}"` (uppercase, no comma), `GreaterThanOrEqual`/`LessThanOrEqual` render as `">="`/`"<="`. If a run still disagrees, trust the actual output over this plan and correct the assertion — but this should not happen; the format was read from source, not inferred.

- [ ] **Step 3: Run to confirm the new tests fail correctly**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

Expected: FAIL — `NotSupportedException: Lower does not support SearchParameterExpression yet` (from `Lower.LowerNode`'s current default arm). The two pre-existing E2E tests must still pass.

- [ ] **Step 4: Add the `SearchParameterExpression` case to `Lower.LowerNode`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/Lower.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage: turns a bound Expression tree of ANDed/ORed
/// SearchParameterPredicateExpression leaves and SearchParameterExpression-wrapped composites into a
/// QueryPlan. Chain, include, sort, and :not are not handled -- see this plan's global constraints
/// for the full list and why.
/// </summary>
public static class Lower
{
    public static QueryPlan Run(Expression expression, SymbolTable symbols, int? top = null)
    {
        var context = new StructuralContext(symbols);
        var match = LowerNode(expression, context);
        return new QueryPlan(context.Ctes, match, top);
    }

    private static CteRef LowerNode(Expression expression, StructuralContext context) => expression switch
    {
        SearchParameterPredicateExpression leaf => context.Lower(leaf),
        SearchParameterExpression sp => LowerSearchParameter(sp, context),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context)).ToList()),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

    private static CteRef LowerSearchParameter(SearchParameterExpression sp, StructuralContext context)
    {
        if (TryGetCompositeComponents(sp.Expression, out var components))
        {
            return context.LowerComposite(sp.Parameter, components!);
        }

        if (sp.Expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or
            && or.Expressions.Count > 0
            && or.Expressions.All(e => TryGetCompositeComponents(e, out _)))
        {
            var refs = or.Expressions
                .Select(e =>
                {
                    TryGetCompositeComponents(e, out var alt);
                    return context.LowerComposite(sp.Parameter, alt!);
                })
                .ToList();
            return context.Union(refs);
        }

        return LowerNode(sp.Expression, context);
    }

    private static bool TryGetCompositeComponents(Expression expression, out IReadOnlyList<CompositeComponentExpression>? components)
    {
        if (expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and
            && and.Expressions.Count > 0
            && and.Expressions.All(e => e is CompositeComponentExpression))
        {
            components = and.Expressions.Cast<CompositeComponentExpression>().ToList();
            return true;
        }

        components = null;
        return false;
    }

    private static CteRef LowerAnd(MultiaryExpression and, StructuralContext context)
    {
        var refs = and.Expressions.Select(e => LowerNode(e, context)).ToList();
        var result = refs[0];
        for (var i = 1; i < refs.Count; i++)
        {
            result = context.Intersect(result, refs[i]);
        }
        return result;
    }
}
```

- [ ] **Step 5: Run all Lowering + E2E tests**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass -- including every prior increment's tests (String/Token/Reference/Uri/Number/Quantity/DateTime lowering rules, `SqlCatalogTests`, `EmitTests`, `PlanExplainerTests`), confirming the new `SearchParameterExpression` case and composite dispatch introduced no regression.

- [ ] **Step 6: Full solution build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors. The only failures should be the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (uninitialized submodule) -- confirm no new failures.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): wire SearchParameterExpression into Lower, prove composites end to end

StructuralContext.LowerComposite + Lower.LowerNode's new SearchParameterExpression
case close the general SearchParameterExpression-unwrap gap (every real-binder
query is wrapped in this node; prior increments never hit it because their tests
hand-build the typed leaf directly) as a side effect of correctly threading a
composite's own SearchParamId through to its ParamSource. Handles both a single
composite value (bare And of CompositeComponentExpression) and comma-separated
alternatives (Or of such Ands, unioned)."
```

---

## Self-Review

**Spec coverage:** TokenToken (Task 2) and TokenNumberNumber (Task 3) both covered, both dispatched via Task 4, both wired end-to-end via Task 5. The composite-identity gap (Task 1) and the general `SearchParameterExpression`-unwrap gap (Task 5) are both closed, since composites cannot resolve or lower without them. The four other composite types remain out of scope, explicitly throwing via `CompositeLoweringDispatcher`'s default arm -- no silent partial support.

**Placeholder scan:** No TBD/TODO; every step has complete code. Task 5's E2E golden strings were confirmed against `PlanExplainer.cs`'s actual source (`Predicate.And` renders `"{Left} AND {Right}"`, uppercase), not guessed from analogy to other predicate kinds.

**Type consistency:** `CompositeLoweringDispatcher.Lower`'s signature (`SearchParameterInfo, IReadOnlyList<CompositeComponentExpression>, LeafContext`) matches what `StructuralContext.LowerComposite` (Task 5) calls it with. `TokenTokenLoweringRule.Lower`/`TokenNumberNumberLoweringRule.Lower`'s signature (`SearchParameterInfo, IReadOnlyList<SearchParameterPredicateExpression>, LeafContext`) matches what `CompositeLoweringDispatcher` (Task 4) calls them with -- components pre-ordered by `Position` and pre-validated as bare predicates by the dispatcher, so the two rules never need to touch `.Position`/`.WrappedExpression` themselves, only array index.
