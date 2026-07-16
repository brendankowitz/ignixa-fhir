# Number/Quantity/DateTime Leaf Lowering Rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the compiler's `Predicate` plan-IR with comparison operators (`<`, `<=`, `>`, `>=`, `Or`) and add `NumberLoweringRule`/`QuantityLoweringRule`/`DateTimeLoweringRule` — the three remaining base leaf types with real (not composite) FHIR range-comparator semantics — joining `String`/`Token`/`Reference`/`Uri` as working tier-1 rules.

**Architecture:** `Predicate.Equal`/`.Like`/`.And` (existing) cannot express range comparisons or disjunction — `Number`/`Quantity`'s `:eq`/`:ne` comparators compile to a compound `AND`/`OR` of two column comparisons against the same row (per the real, verified SQL the legacy system already emits), and `DateTime`'s comparators compile to range-overlap checks against `StartDateTime`/`EndDateTime` with per-comparator column pairing that differs from Number/Quantity's Low/High pairing. All three types share the `:ap` (approximately) exclusion: FHIR's `:ap` widens the search bound using `DateTimeOffset.UtcNow` (DateTime) or a fixed 10% tolerance (Number/Quantity) at the moment a query is built — for DateTime specifically this is wall-clock-dependent, which conflicts with `Lower`'s "pure function of IR/SymbolTable/SqlCatalog" invariant. Rather than silently break that invariant or silently produce a non-widened (wrong) comparison, `:ap` throws `NotSupportedException` for all three types, with the real fix (an explicit `now` parameter on `Lower.Run`) named as a documented follow-up, not implemented here.

**Tech Stack:** `net9.0;net10.0`, matching `Ignixa.Search.Sql`'s existing convention. xUnit + Shouldly.

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- `Ignixa.Search.Sql.csproj` must keep no `Microsoft.EntityFrameworkCore*`/`Microsoft.AspNetCore.*` reference.
- **The SQL formulas below are transcribed from real, already-verified source** (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`'s `GenerateNumberQuery`/`GenerateQuantityQueryAsync`/`GenerateDateTimeQuery`, and `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs`'s field-level semantic layer). Re-read the real files before implementing each rule and correct any formula below that disagrees with what you find — this document's tables are a verified starting point, not a substitute for checking.
- **`:ap` (`SearchComparator.Ap`) throws `NotSupportedException` for all three types.** Do not attempt to implement approximate widening — it requires an input (wall-clock time for DateTime, a tolerance policy for Number/Quantity) that `Lower.Run`'s current signature (`Expression, SymbolTable, int? top`) doesn't carry, and adding one is a deliberate, separate design decision (an explicit `now: DateTimeOffset?` parameter defaulting to real time, overridable for tests) — not something to bolt on silently inside a leaf rule.
- **`Quantity`'s `System`/`Code` fields are out of scope.** `QuantitySearchValue.System`/`.Code` being non-null/non-empty throws `NotSupportedException` — matching `TokenLoweringRule`'s precedent for the identical class of gap (system-URI resolution needs a new `ISymbolResolver` method that doesn't exist yet). Only the value comparison (`SingleValue`/`LowValue`/`HighValue`) is implemented.
- Every new `Predicate` case is an immutable record, matching the existing family in `Predicate.cs`.
- Follow repo convention: file-scoped namespaces (usings above the namespace line), AAA test structure, `GivenContext_WhenAction_ThenResult` naming, no `#region`, one cohesive concept per file.

---

### Task 1: Extend `Predicate` with comparison operators and `Or`; extend `Emit` and `PlanExplainer`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Predicate.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs` (extend)
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs` (extend)

**Interfaces:**
- Consumes: nothing new.
- Produces: `Predicate.LessThan`, `.LessThanOrEqual`, `.GreaterThan`, `.GreaterThanOrEqual`, `.Or`, consumed by tasks 2-4's leaf rules.

- [ ] **Step 1: Add the four comparison cases and `Or` to `Predicate`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/Predicate.cs -- add these cases to the existing abstract record
public sealed record LessThan(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

public sealed record LessThanOrEqual(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

public sealed record GreaterThan(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

public sealed record GreaterThanOrEqual(SqlColumnRef Column, SqlParameterRef Value) : Predicate;

public sealed record Or(Predicate Left, Predicate Right) : Predicate;
```

No `Collation` parameter on the comparison cases — collation is a string-comparison concept, not applicable to numeric/temporal comparisons.

- [ ] **Step 2: Write failing `Emit` tests for the new cases**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs -- add to the existing class
[Fact]
public void GivenACompoundAndOfTwoComparisons_WhenEmitted_ThenProducesBothConditionsJoinedByAnd()
{
    // Arrange
    var table = SqlCatalog.Default.Table("NumberSearchParam");
    var predicate = new Predicate.And(
        new Predicate.LessThanOrEqual(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)),
        new Predicate.GreaterThanOrEqual(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)));
    var plan = new QueryPlan([new CteDefinition.ParamSource(table, 99, predicate)], new CteRef(0));

    // Act
    var emitted = Emit.Run(plan);

    // Assert
    emitted.Sql.ShouldContain("LowValue <= @p0 AND HighValue >= @p1");
    emitted.Parameters.Select(p => p.Value).ShouldBe([5m, 5m]);
}

[Fact]
public void GivenAnOrOfTwoComparisons_WhenEmitted_ThenProducesBothConditionsJoinedByOrInParens()
{
    // Arrange
    var table = SqlCatalog.Default.Table("NumberSearchParam");
    var predicate = new Predicate.Or(
        new Predicate.LessThan(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)),
        new Predicate.GreaterThan(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)));
    var plan = new QueryPlan([new CteDefinition.ParamSource(table, 99, predicate)], new CteRef(0));

    // Act
    var emitted = Emit.Run(plan);

    // Assert
    emitted.Sql.ShouldContain("(HighValue < @p0 OR LowValue > @p1)");
}
```

- [ ] **Step 3: Run to confirm failure, implement `Emit`'s new cases**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/Emit.cs -- add arms to EmitPredicate's switch expression
Predicate.LessThan lt => $"{lt.Column.Column} < {EmitParam(lt.Value, parameters)}",
Predicate.LessThanOrEqual le => $"{le.Column.Column} <= {EmitParam(le.Value, parameters)}",
Predicate.GreaterThan gt => $"{gt.Column.Column} > {EmitParam(gt.Value, parameters)}",
Predicate.GreaterThanOrEqual ge => $"{ge.Column.Column} >= {EmitParam(ge.Value, parameters)}",
Predicate.Or or => $"({EmitPredicate(or.Left, parameters)} OR {EmitPredicate(or.Right, parameters)})",
```

Note: the existing `Predicate.And` arm already wraps as `{left} AND {right}` without parens (verify current shape before assuming) — for a top-level `And`, that's correct standalone SQL; keep `Or` parenthesized as shown so it composes safely if it's ever nested inside a future `And` (defensive, not exercised by this plan's own tests, but cheap and correct).

- [ ] **Step 4: Run to confirm `Emit` tests pass; write and implement matching `PlanExplainer` cases**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests" --nologo
```

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs -- add to the existing class
[Fact]
public void GivenACompoundAndOfTwoComparisons_WhenExplained_ThenPrintsBothConditions()
{
    // Arrange
    var table = SqlCatalog.Default.Table("NumberSearchParam");
    var predicate = new Predicate.And(
        new Predicate.LessThanOrEqual(new SqlColumnRef(table.TableName, "LowValue"), new SqlParameterRef(5m)),
        new Predicate.GreaterThanOrEqual(new SqlColumnRef(table.TableName, "HighValue"), new SqlParameterRef(5m)));
    var plan = new QueryPlan([new CteDefinition.ParamSource(table, 99, predicate)], new CteRef(0));

    // Act
    var explained = plan.Explain();

    // Assert
    explained.ShouldBe("root = NumberSearchParam[99]  LowValue <= @p0 AND HighValue >= @p1");
}
```

```csharp
// src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs -- add arms to PrintPredicate's switch expression
Predicate.LessThan lt => $"{lt.Column.Column} < @p{parameterOrdinal++}",
Predicate.LessThanOrEqual le => $"{le.Column.Column} <= @p{parameterOrdinal++}",
Predicate.GreaterThan gt => $"{gt.Column.Column} > @p{parameterOrdinal++}",
Predicate.GreaterThanOrEqual ge => $"{ge.Column.Column} >= @p{parameterOrdinal++}",
Predicate.Or or => $"{PrintPredicate(or.Left, ref parameterOrdinal)} OR {PrintPredicate(or.Right, ref parameterOrdinal)}",
```

- [ ] **Step 5: Run to confirm all tests pass, build**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests|FullyQualifiedName~PlanExplainerTests" --nologo
dotnet build All.sln --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): extend Predicate/Emit/PlanExplainer with comparison operators and Or

LessThan/LessThanOrEqual/GreaterThan/GreaterThanOrEqual/Or -- Equal/Like/And
alone can't express Number/Quantity's compound :eq (AND of two range
bounds) and :ne (OR of two range bounds), or DateTime's range-overlap
comparators. No leaf rule consumes these yet -- tasks 2-4 do."
```

---

### Task 2: `NumberLoweringRule` (+ shared `NumericRangeComparison` helper)

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/NumericRangeComparison.cs`
- Create: `src/Core/Ignixa.Search.Sql/Lowering/NumberLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/NumberLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext` (existing), `NumberSearchValue` (`Ignixa.Search.Indexing.SearchValues`), `SqlCatalog.Default.Table("NumberSearchParam")` (already generator-populated).
- Produces: `NumberLoweringRule.Lower(...): CteDefinition.ParamSource`, `NumericRangeComparison.Build(...): Predicate` (also consumed by task 3's `QuantityLoweringRule`).

- [ ] **Step 1: Verify `NumberSearchValue`'s real shape before writing code**

```bash
grep -n "class NumberSearchValue" -A 20 src/Core/Ignixa.Search/Indexing/SearchValues/NumberSearchValue.cs
```

Confirm `.Low`/`.High` are `decimal?` and confirm how a "point value" search (e.g. `value=5.4`) is represented — per earlier research, `NumberSearchValue(decimal number)` sets `Low = High = number`, so a leaf rule can always read `.Low`/`.High` uniformly without a separate "is this a point or a range" branch. Verify this is still accurate.

- [ ] **Step 2: Write the shared `NumericRangeComparison` helper**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/NumericRangeComparison.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by Number and Quantity leaf lowering (both store
/// LowValue/HighValue with identical range semantics). Transcribed from
/// SearchParameterQueryGenerator.cs's GenerateNumberQuery/GenerateQuantityQueryAsync -- the real,
/// already-shipped SQL these comparators emit today. Ap throws: it requires a tolerance/widening input
/// this pure function doesn't have.
/// </summary>
internal static class NumericRangeComparison
{
    public static Predicate Build(SqlColumnRef lowColumn, SqlColumnRef highColumn, SearchComparator comparator, SqlParameterRef value) => comparator switch
    {
        SearchComparator.Eq => new Predicate.And(new Predicate.LessThanOrEqual(lowColumn, value), new Predicate.GreaterThanOrEqual(highColumn, value)),
        SearchComparator.Ne => new Predicate.Or(new Predicate.LessThan(highColumn, value), new Predicate.GreaterThan(lowColumn, value)),
        SearchComparator.Ge => new Predicate.GreaterThanOrEqual(lowColumn, value),
        SearchComparator.Gt or SearchComparator.Sa => new Predicate.GreaterThan(lowColumn, value),
        SearchComparator.Le => new Predicate.LessThanOrEqual(highColumn, value),
        SearchComparator.Lt or SearchComparator.Eb => new Predicate.LessThan(highColumn, value),
        SearchComparator.Ap => throw new NotSupportedException(
            "The :ap (approximately) comparator requires a tolerance/widening input this pure lowering " +
            "function doesn't have -- not implemented. Would need Lower.Run to accept an explicit widening policy."),
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };
}
```

- [ ] **Step 3: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/NumberLoweringRuleTests.cs
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

public class NumberLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo Parameter()
        => new("probability", "probability", SearchParamType.Number, new Uri("http://hl7.org/fhir/SearchParameter/RiskAssessment-probability"));

    [Fact]
    public void GivenEqComparator_WhenLowered_ThenBuildsCompoundAndOfLowAndHighBounds()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201));

        // Assert
        cte.SearchParamId.ShouldBe((short)201);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("LowValue");
        le.Value.Value.ShouldBe(5.4m);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("HighValue");
        ge.Value.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenNeComparator_WhenLowered_ThenBuildsOrOfLowAndHighBounds()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201));

        // Assert
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.LessThan>().Column.Column.ShouldBe("HighValue");
        or.Right.ShouldBeOfType<Predicate.GreaterThan>().Column.Column.ShouldBe("LowValue");
    }

    [Fact]
    public void GivenGeComparator_WhenLowered_ThenComparesLowValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201));

        // Assert
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("LowValue");
    }

    [Fact]
    public void GivenLtComparator_WhenLowered_ThenComparesHighValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Lt, modifier: null, new NumberSearchValue(5.4m));

        // Act
        var cte = NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201));

        // Assert
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("HighValue");
    }

    [Fact]
    public void GivenApComparator_WhenLowered_ThenThrows()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, new NumberSearchValue(5.4m));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            NumberLoweringRule.Lower(predicate, (NumberSearchValue)predicate.Value, ContextResolving(parameter, 201)));
    }
}
```

Also add tests for `Gt`, `Le`, `Sa`, `Eb` following the exact same pattern as `Ge`/`Lt` above, asserting: `Gt` → `GreaterThan` on `LowValue`; `Le` → `LessThanOrEqual` on `HighValue`; `Sa` → same shape as `Gt` (`GreaterThan` on `LowValue`); `Eb` → same shape as `Lt` (`LessThan` on `HighValue`).

- [ ] **Step 4: Implement `NumberLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/NumberLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Number search value to a ParamSource over NumberSearchParam. All comparators except :ap
/// (see NumericRangeComparison) are supported, matching the real SQL SearchParameterQueryGenerator
/// already emits for this table.
/// </summary>
public static class NumberLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, NumberSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("NumberSearchParam");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue");

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("NumberSearchValue has neither Low nor High set.");
        var predicateExpr = NumericRangeComparison.Build(lowColumn, highColumn, predicate.Comparator, context.Parameter(comparisonValue));

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
```

Verify `value.Low ?? value.High` is the right way to get "the search value" for a point search — per Task 2 Step 1's confirmation that `NumberSearchValue(decimal number)` sets both `Low` and `High` to `number`, `value.Low` alone should always be non-null for any value reaching this rule; adjust if verification in Step 1 found otherwise.

- [ ] **Step 5: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~NumberLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add NumberLoweringRule and the shared NumericRangeComparison helper

All comparators except :ap, matching the exact SQL SearchParameterQueryGenerator
already emits for NumberSearchParam. NumericRangeComparison is shared
with QuantityLoweringRule (task 3) -- identical LowValue/HighValue
range semantics on both tables."
```

---

### Task 3: `QuantityLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/QuantityLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/QuantityLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext`, `NumericRangeComparison.Build` (task 2), `QuantitySearchValue`, `SqlCatalog.Default.Table("QuantitySearchParam")`.
- Produces: `QuantityLoweringRule.Lower(...): CteDefinition.ParamSource`.

- [ ] **Step 1: Verify `QuantitySearchValue`'s real shape**

```bash
grep -n "class QuantitySearchValue" -A 25 src/Core/Ignixa.Search/Indexing/SearchValues/QuantitySearchValue.cs
```

Confirm `.System`/`.Code` are `string?` and `.Low`/`.High` are `decimal?`.

- [ ] **Step 2: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/QuantityLoweringRuleTests.cs
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

public class QuantityLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo Parameter()
        => new("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"));

    [Fact]
    public void GivenAnUnqualifiedQuantityValue_WhenLowered_ThenComparesLowAndHighValueOnly()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m));

        // Act
        var cte = QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("LowValue");
        and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("HighValue");
    }

    [Fact]
    public void GivenASystemQualifiedQuantity_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheSystem()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 5.4m));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202)));
    }

    [Fact]
    public void GivenACodeQualifiedQuantity_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheCode()
    {
        // Arrange
        var parameter = Parameter();
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: "mg", 5.4m));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            QuantityLoweringRule.Lower(predicate, (QuantitySearchValue)predicate.Value, ContextResolving(parameter, 202)));
    }
}
```

Verify `QuantitySearchValue`'s real constructor parameter names/order against Step 1's findings and correct the test construction if it disagrees.

- [ ] **Step 3: Implement `QuantityLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/QuantityLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Quantity search value to a ParamSource over QuantitySearchParam -- value comparison only.
/// System/Code matching needs SystemId/QuantityCodeId resolution, a genuinely separate resolver
/// mechanism (SearchIndexReferenceDataCache.GetOrCreateSystemIdAsync/GetOrCreateQuantityCodeIdAsync in
/// the DataLayer today, no ISymbolResolver equivalent yet) -- not implemented; throws rather than
/// silently ignoring a system/code constraint the user actually specified.
/// </summary>
public static class QuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, QuantitySearchValue value, LeafContext context)
    {
        if (!string.IsNullOrEmpty(value.System) || !string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "Quantity search with System or Code is not supported yet -- this rule only implements the value comparison. " +
                "SystemId/QuantityCodeId resolution needs a new ISymbolResolver method, not built yet.");
        }

        var table = SqlCatalog.Default.Table("QuantitySearchParam");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue");

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("QuantitySearchValue has neither Low nor High set.");
        var predicateExpr = NumericRangeComparison.Build(lowColumn, highColumn, predicate.Comparator, context.Parameter(comparisonValue));

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
```

- [ ] **Step 4: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~QuantityLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add QuantityLoweringRule (value comparison only)

Reuses NumericRangeComparison from task 2 -- identical range semantics
to Number. System/Code throw NotSupportedException rather than silently
matching without those constraints; needs new resolver surface not
built yet."
```

---

### Task 4: `DateTimeLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/DateTimeLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/DateTimeLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext`, `DateTimeSearchValue`, `SqlCatalog.Default.Table("DateTimeSearchParam")`.
- Produces: `DateTimeLoweringRule.Lower(...): CteDefinition.ParamSource`.

**Comparator table** (transcribed from `SearchValueExpressionBuilderHelper.cs`'s field-level semantic layer — range-overlap between the stored row's `[Start, End]` and the search value's own `[Start, End]`, which already encodes precision, e.g. a year-only search becomes Jan 1 00:00:00 to Dec 31 23:59:59.9999999 — a `DateTimeLoweringRule` never handles partial precision itself, it only ever sees two concrete `DateTimeOffset`s):

| Comparator | Predicate |
|---|---|
| `Eq` | `And(StartDateTime <= search.End, EndDateTime >= search.Start)` |
| `Ne` | `Or(StartDateTime < search.Start, EndDateTime > search.End)` |
| `Lt` | `LessThan(StartDateTime, search.Start)` |
| `Gt` | `GreaterThan(EndDateTime, search.End)` |
| `Le` | `LessThanOrEqual(StartDateTime, search.End)` |
| `Ge` | `GreaterThanOrEqual(EndDateTime, search.Start)` |
| `Sa` | `GreaterThan(StartDateTime, search.End)` |
| `Eb` | `LessThan(EndDateTime, search.Start)` |
| `Ap` | throws |

- [ ] **Step 1: Verify this table against real source before implementing**

```bash
grep -n "SearchComparator.Eq\|SearchComparator.Ne\|SearchComparator.Lt\|SearchComparator.Gt\|SearchComparator.Le\|SearchComparator.Ge\|SearchComparator.Sa\|SearchComparator.Eb" -B2 -A2 src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs
```

Read the actual DateTime comparator branch (not just the Number/Quantity one) and confirm the table above matches exactly — correct any row that disagrees. Also confirm `DateTimeSearchValue.Start`/`.End` are the right field names to read for both the search value (in this rule) and note that the stored *column* names are `StartDateTime`/`EndDateTime` (different names, same concept — don't conflate them when writing the rule).

- [ ] **Step 2: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/DateTimeLoweringRuleTests.cs
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

public class DateTimeLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo Parameter()
        => new("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));

    [Fact]
    public void GivenEqComparator_WhenLowered_ThenBuildsCompoundAndOfStartAndEndConditions()
    {
        // Arrange
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203));

        // Assert
        cte.SearchParamId.ShouldBe((short)203);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var le = and.Left.ShouldBeOfType<Predicate.LessThanOrEqual>();
        le.Column.Column.ShouldBe("StartDateTime");
        le.Value.Value.ShouldBe(value.End);
        var ge = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(value.Start);
    }

    [Fact]
    public void GivenNeComparator_WhenLowered_ThenBuildsOrOfStartAndEndConditions()
    {
        // Arrange
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ne, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203));

        // Assert
        var or = cte.Predicate.ShouldBeOfType<Predicate.Or>();
        or.Left.ShouldBeOfType<Predicate.LessThan>().Column.Column.ShouldBe("StartDateTime");
        or.Right.ShouldBeOfType<Predicate.GreaterThan>().Column.Column.ShouldBe("EndDateTime");
    }

    [Fact]
    public void GivenGeComparator_WhenLowered_ThenComparesEndDateTimeAgainstSearchStart()
    {
        // Arrange
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203));

        // Assert
        var ge = cte.Predicate.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        ge.Column.Column.ShouldBe("EndDateTime");
        ge.Value.Value.ShouldBe(value.Start);
    }

    [Fact]
    public void GivenLtComparator_WhenLowered_ThenComparesStartDateTimeAgainstSearchStart()
    {
        // Arrange
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Lt, modifier: null, value);

        // Act
        var cte = DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203));

        // Assert
        var lt = cte.Predicate.ShouldBeOfType<Predicate.LessThan>();
        lt.Column.Column.ShouldBe("StartDateTime");
        lt.Value.Value.ShouldBe(value.Start);
    }

    [Fact]
    public void GivenApComparator_WhenLowered_ThenThrows()
    {
        // Arrange
        var parameter = Parameter();
        var value = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Ap, modifier: null, value);

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            DateTimeLoweringRule.Lower(predicate, (DateTimeSearchValue)predicate.Value, ContextResolving(parameter, 203)));
    }
}
```

Also add tests for `Gt`, `Le`, `Sa`, `Eb` following the exact same pattern, asserting the shapes named in the comparator table above (`Gt` → `GreaterThan` on `EndDateTime` vs `search.End`; `Le` → `LessThanOrEqual` on `StartDateTime` vs `search.End`; `Sa` → `GreaterThan` on `StartDateTime` vs `search.End`; `Eb` → `LessThan` on `EndDateTime` vs `search.Start`).

- [ ] **Step 3: Implement `DateTimeLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/DateTimeLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a DateTime search value to a ParamSource over DateTimeSearchParam -- range-overlap semantics
/// between the stored row's [StartDateTime, EndDateTime] and the search value's own [Start, End]
/// (which already encodes precision -- a year-only search is a full-year range by the time it reaches
/// here). Transcribed from SearchValueExpressionBuilderHelper.cs's field-level semantic layer. :ap
/// throws -- it requires DateTimeOffset.UtcNow at lowering time, which this pure function doesn't have.
/// </summary>
public static class DateTimeLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, DateTimeSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime");

        Predicate predicateExpr = predicate.Comparator switch
        {
            SearchComparator.Eq => new Predicate.And(
                new Predicate.LessThanOrEqual(startColumn, context.Parameter(value.End)),
                new Predicate.GreaterThanOrEqual(endColumn, context.Parameter(value.Start))),
            SearchComparator.Ne => new Predicate.Or(
                new Predicate.LessThan(startColumn, context.Parameter(value.Start)),
                new Predicate.GreaterThan(endColumn, context.Parameter(value.End))),
            SearchComparator.Lt => new Predicate.LessThan(startColumn, context.Parameter(value.Start)),
            SearchComparator.Gt => new Predicate.GreaterThan(endColumn, context.Parameter(value.End)),
            SearchComparator.Le => new Predicate.LessThanOrEqual(startColumn, context.Parameter(value.End)),
            SearchComparator.Ge => new Predicate.GreaterThanOrEqual(endColumn, context.Parameter(value.Start)),
            SearchComparator.Sa => new Predicate.GreaterThan(startColumn, context.Parameter(value.End)),
            SearchComparator.Eb => new Predicate.LessThan(endColumn, context.Parameter(value.Start)),
            SearchComparator.Ap => throw new NotSupportedException(
                "The :ap (approximately) comparator requires DateTimeOffset.UtcNow at lowering time, which " +
                "conflicts with Lower's purity invariant -- not implemented. Would need Lower.Run to accept an explicit 'now' parameter."),
            _ => throw new NotSupportedException($"Unknown SearchComparator '{predicate.Comparator}'."),
        };

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
```

- [ ] **Step 4: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~DateTimeLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add DateTimeLoweringRule

Range-overlap semantics between the stored row's Start/EndDateTime and
the search value's own Start/End (precision already resolved by the
time DateTimeSearchValue is constructed). Per-comparator column pairing
differs from Number/Quantity's Low/High -- transcribed from
SearchValueExpressionBuilderHelper.cs's real semantic layer, not shared
via NumericRangeComparison. :ap throws for the same purity reason as
Number/Quantity."
```

---

### Task 5: Wire into `LeafLoweringDispatcher`, end-to-end proof

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs`
- Test: extend `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: `NumberLoweringRule.Lower`, `QuantityLoweringRule.Lower`, `DateTimeLoweringRule.Lower` (tasks 2-4).
- Produces: all three types now dispatch correctly through `Lower.Run`, proven end-to-end.

- [ ] **Step 1: Wire the dispatcher**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs -- add three switch arms
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

Update the `NotSupportedException` message's excluded-type list (should now say only "composites are out of scope," dropping Date/Number/Quantity).

- [ ] **Step 2: Add an end-to-end test**

```csharp
// test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs -- add this [Fact] to the existing class
[Fact]
public async Task GivenAnObservationDateRangeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
{
    // Arrange -- Observation?date=ge2023-01-01&value-quantity=gt5.4
    var dateParam = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Observation-date"));
    var quantityParam = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-value-quantity"));
    var dateValue = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
    var tree = new MultiaryExpression(MultiaryOperator.And,
    [
        new SearchParameterPredicateExpression(dateParam, SearchComparator.Ge, modifier: null, dateValue),
        new SearchParameterPredicateExpression(quantityParam, SearchComparator.Gt, modifier: null, new QuantitySearchValue(system: null!, code: null!, 5.4m)),
    ]);
    var resolver = new FakeSymbolResolver();
    resolver.SearchParamIds[dateParam.Url!.ToString()] = 203;
    resolver.SearchParamIds[quantityParam.Url!.ToString()] = 204;

    // Act
    var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
    var plan = Lower.Run(tree, symbolTable);
    var emitted = Emit.Run(plan);

    // Assert
    plan.Explain().ShouldBe(
        "cte0 = DateTimeSearchParam[203]  EndDateTime >= @p0\n" +
        "cte1 = QuantitySearchParam[204]  LowValue > @p1\n" +
        "root = Intersect(cte0, cte1)");
    emitted.Sql.ShouldNotContain("2023");
    emitted.Parameters.ShouldContain(p => p.Value.Equals(dateValue.Start));
    emitted.Parameters.ShouldContain(p => p.Value.Equals(5.4m));
}
```

Reuse the existing `FakeSymbolResolver` already defined in this test class — do not redefine it. If the hand-derived `Explain()` string doesn't match your actual output byte-for-byte, treat it as normal TDD: trace through `PlanExplainer`'s actual (already-working) logic and correct the assertion to match real, correct behavior.

- [ ] **Step 3: Run to confirm it passes, build the full solution**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors, all green (aside from the known pre-existing `sql-on-fhir-tests` submodule gap).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(search-sql): wire Number/Quantity/DateTime into the dispatcher, prove end to end

Observation?date=ge...&value-quantity=gt5.4 compiles through
Resolve -> Lower -> Emit correctly, joining String/Token/Reference/Uri.
Closes this increment: 6 of 7 base leaf types now have working Lower
rules (Uri already shipped; only the composite types remain unimplemented,
plus :ap and Quantity system/code, both explicitly deferred with reasons)."
```

## Self-Review

- **Spec coverage:** Task 1 extends the plan-IR with exactly what tasks 2-4 need (comparison operators + Or), no more. Tasks 2-4 cover Number/Quantity/DateTime with every comparator except the explicitly-deferred `:ap`, each transcribed from real, already-shipped SQL/semantic logic rather than invented. Task 5 wires and proves it end to end.
- **Placeholder scan:** every SQL formula is marked "verify against real source before implementing" with the exact grep/file to check, matching this repo's established honest-deferral pattern. The Gt/Le/Sa/Eb test cases for Number and DateTime are described by exact expected shape (a table/pattern) rather than fully spelled out in duplicate, to keep the plan bounded — the shapes are unambiguous, not vague.
- **Type consistency:** `NumericRangeComparison.Build(SqlColumnRef, SqlColumnRef, SearchComparator, SqlParameterRef): Predicate`, `NumberLoweringRule.Lower`, `QuantityLoweringRule.Lower`, `DateTimeLoweringRule.Lower` (all `(SearchParameterPredicateExpression, TValue, LeafContext): CteDefinition.ParamSource`) are used identically across tasks 2-5 — checked for drift, none found.
- **Scope discipline:** `:ap` and `Quantity` system/code are the only two things silently-workable-but-deliberately-thrown; both have a one-line "why" and a named follow-up (an explicit `now` parameter on `Lower.Run`; a new `ISymbolResolver` method) rather than a vague "not supported."
