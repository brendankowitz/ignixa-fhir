# Comparator Semantics Canonicalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Canonicalize `gt`/`ge`/`lt`/`le` comparator semantics for Number/Quantity/DateTime range
comparisons across three divergent implementations, and give `sa`/`eb` (currently aliased to
`gt`/`lt` and, on the InMemory backend for DateTime, indistinguishable from them) their own
`BinaryOperator` values so the operator alone is always self-describing.

**Architecture:** Two new `BinaryOperator` enum values (`StartsAfter`, `EndsBefore`) flow from the
shared Core parser through every backend. SQL's `ComparisonPredicates` becomes the single
canonical implementation for Number/Quantity (both single-param and composite paths delegate to
it) and gains new arms for DateTime too (behavior-preserving there — DateTime's SQL side was
already correct via `FieldName` dispatch, this makes it explicit). The InMemory backend's
`ComparisonValueVisitor` gets matching bound-selection logic for Number/Quantity/DateTime.

**Tech Stack:** C# / .NET 10, EF Core, xUnit + Shouldly, existing `Ignixa.Search`/
`Ignixa.DataLayer.SqlEntityFramework` architecture — no new dependencies.

## Global Constraints

- Build: `dotnet build All.sln` must be 0 warnings, 0 errors after every task.
- Test: `dotnet test` on touched projects must be green after every task (pre-existing unrelated
  failures — 5 documented failures in `Ignixa.DataLayer.LegacySqlEF.Tests` from the EF Core
  InMemory provider's `EF.Constant()`/`Collate` translation gap — are expected and not a blocker).
- No `#region` blocks. 4-space indentation. File-scoped namespaces.
- `BinaryOperator` new members must be **appended** (`StartsAfter = 6, EndsBefore = 7`), never
  inserted — verified safe (no ordinal dependency anywhere in this codebase), but still append-only
  by convention.
- This is a **live search-behavior change** for Number/Quantity `gt`/`ge`/`lt`/`le` on any stored
  value with `LowValue != HighValue` (implicit-precision values) — not a data migration, but worth
  calling out in the final task's commit message and, later, release notes.
- Full design context: `docs/superpowers/specs/2026-07-11-comparator-semantics-design.md`.

---

### Task 1: Add `BinaryOperator.StartsAfter`/`EndsBefore` and `Expression` factory methods

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/BinaryOperator.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Expression.cs:149-167`
- Test: `test/Ignixa.Application.Tests/Search/ExpressionFactoryTests.cs` (new)

**Interfaces:**
- Produces: `BinaryOperator.StartsAfter` (6), `BinaryOperator.EndsBefore` (7);
  `Expression.StartsAfter(FieldName fieldName, int? componentIndex, object value)` and
  `Expression.EndsBefore(FieldName fieldName, int? componentIndex, object value)`, both returning
  `BinaryExpression`, matching the exact shape of the existing `Expression.GreaterThan`/
  `Expression.LessThan` factories at the same location.

- [ ] **Step 1: Write the failing test**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;

namespace Ignixa.Application.Tests.Search;

public class ExpressionFactoryTests
{
    [Fact]
    public void GivenFieldAndValue_WhenStartsAfter_ThenBuildsStartsAfterBinaryExpression()
    {
        var result = Expression.StartsAfter(FieldName.Number, componentIndex: null, 5.4m);

        result.BinaryOperator.ShouldBe(BinaryOperator.StartsAfter);
        result.FieldName.ShouldBe(FieldName.Number);
        result.Value.ShouldBe(5.4m);
    }

    [Fact]
    public void GivenFieldAndValue_WhenEndsBefore_ThenBuildsEndsBeforeBinaryExpression()
    {
        var result = Expression.EndsBefore(FieldName.Number, componentIndex: null, 5.4m);

        result.BinaryOperator.ShouldBe(BinaryOperator.EndsBefore);
        result.FieldName.ShouldBe(FieldName.Number);
        result.Value.ShouldBe(5.4m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~ExpressionFactoryTests"`
Expected: FAIL with a compile error — `Expression.StartsAfter`/`Expression.EndsBefore` and
`BinaryOperator.StartsAfter`/`BinaryOperator.EndsBefore` don't exist yet.

- [ ] **Step 3: Add the two new enum members**

In `src/Core/Ignixa.Search/Expressions/BinaryOperator.cs`, change:

```csharp
public enum BinaryOperator
{
    Equal = 0,
    GreaterThan = 1,
    GreaterThanOrEqual = 2,
    LessThan = 3,
    LessThanOrEqual = 4,
    NotEqual = 5
}
```

to:

```csharp
public enum BinaryOperator
{
    Equal = 0,
    GreaterThan = 1,
    GreaterThanOrEqual = 2,
    LessThan = 3,
    LessThanOrEqual = 4,
    NotEqual = 5,
    StartsAfter = 6,
    EndsBefore = 7
}
```

- [ ] **Step 4: Add the two new factory methods**

In `src/Core/Ignixa.Search/Expressions/Expression.cs`, immediately after the existing
`LessThanOrEqual` factory (ends at line 167), add:

```csharp
    public static BinaryExpression StartsAfter(FieldName fieldName, int? componentIndex, object value)
    {
        return new BinaryExpression(BinaryOperator.StartsAfter, fieldName, componentIndex, value);
    }

    public static BinaryExpression EndsBefore(FieldName fieldName, int? componentIndex, object value)
    {
        return new BinaryExpression(BinaryOperator.EndsBefore, fieldName, componentIndex, value);
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~ExpressionFactoryTests"`
Expected: PASS, 2/2.

- [ ] **Step 6: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s) — confirms no other switch over `BinaryOperator` broke from the
new enum members (switch *expressions* with a `_`/wildcard default don't force exhaustiveness, so
this won't surface as a compile error even in unfixed files — that's expected and handled in later
tasks).

- [ ] **Step 7: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/BinaryOperator.cs src/Core/Ignixa.Search/Expressions/Expression.cs test/Ignixa.Application.Tests/Search/ExpressionFactoryTests.cs
git commit -m "feat(search): add StartsAfter/EndsBefore BinaryOperator values and Expression factories"
```

---

### Task 2: Fix `ComparisonPredicates`' Number/Quantity direction and add `StartsAfter`/`EndsBefore`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs`
  (`ApplyNumberRangeComparison`, `ApplyQuantityRangeComparison`)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs`
  (extend)

**Interfaces:**
- Consumes: `BinaryOperator.StartsAfter`/`EndsBefore` from Task 1.
- Produces: `ComparisonPredicates.ApplyNumberRangeComparison(IQueryable<NumberSearchParamEntity>,
  BinaryOperator, decimal)` and `ApplyQuantityRangeComparison(IQueryable<QuantitySearchParamEntity>,
  BinaryOperator, decimal)` now implement the canonical binding: `GreaterThan`/`GreaterThanOrEqual`
  read `HighValue` (was `LowValue`); `LessThan`/`LessThanOrEqual` read `LowValue` (was `HighValue`);
  `StartsAfter => LowValue > value`; `EndsBefore => HighValue < value`. `Equal`/`NotEqual` unchanged
  (still dead code for Number/Quantity — see Task 4/5 note — kept correct as documentation).

- [ ] **Step 1: Write the failing test**

Add to `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs` (inside
the existing `ComparisonPredicatesTests` class, alongside the existing `GivenInvalidOperator_...`
facts — add the `using` for `Ignixa.DataLayer.SqlEntityFramework.Entities` is already present):

```csharp
    public static IEnumerable<object[]> NumberRangeComparisonCases()
    {
        // Stored range: [Low=10, High=20] - a genuinely fuzzy range, not a point, so overlap vs.
        // containment vs. strict-separation are all distinguishable.
        yield return new object[] { BinaryOperator.GreaterThan, 15m, true };       // High(20) > 15
        yield return new object[] { BinaryOperator.GreaterThan, 25m, false };      // High(20) > 25 is false
        yield return new object[] { BinaryOperator.GreaterThanOrEqual, 20m, true }; // High(20) >= 20
        yield return new object[] { BinaryOperator.GreaterThanOrEqual, 21m, false };
        yield return new object[] { BinaryOperator.LessThan, 15m, true };          // Low(10) < 15
        yield return new object[] { BinaryOperator.LessThan, 5m, false };          // Low(10) < 5 is false
        yield return new object[] { BinaryOperator.LessThanOrEqual, 10m, true };   // Low(10) <= 10
        yield return new object[] { BinaryOperator.LessThanOrEqual, 5m, false };
        yield return new object[] { BinaryOperator.StartsAfter, 5m, true };        // Low(10) > 5
        yield return new object[] { BinaryOperator.StartsAfter, 15m, false };      // Low(10) > 15 is false - distinguishes Sa from Gt (Gt(15) matches, Sa(15) must not)
        yield return new object[] { BinaryOperator.EndsBefore, 25m, true };        // High(20) < 25
        yield return new object[] { BinaryOperator.EndsBefore, 15m, false };       // High(20) < 15 is false - distinguishes Eb from Lt (Lt(15) matches, Eb(15) must not)
    }

    [Theory]
    [MemberData(nameof(NumberRangeComparisonCases))]
    public void GivenStoredRange_WhenApplyNumberRangeComparison_ThenMatchesCanonicalSemantics(
        BinaryOperator op, decimal searchValue, bool expectMatch)
    {
        var stored = new[] { new NumberSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, LowValue = 10m, HighValue = 20m } }.AsQueryable();

        var results = ComparisonPredicates.ApplyNumberRangeComparison(stored, op, searchValue).ToList();

        results.Count.ShouldBe(expectMatch ? 1 : 0);
    }

    [Theory]
    [MemberData(nameof(NumberRangeComparisonCases))]
    public void GivenStoredRange_WhenApplyQuantityRangeComparison_ThenMatchesCanonicalSemantics(
        BinaryOperator op, decimal searchValue, bool expectMatch)
    {
        var stored = new[] { new QuantitySearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, LowValue = 10m, HighValue = 20m } }.AsQueryable();

        var results = ComparisonPredicates.ApplyQuantityRangeComparison(stored, op, searchValue).ToList();

        results.Count.ShouldBe(expectMatch ? 1 : 0);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~ComparisonPredicatesTests"`
Expected: FAIL to compile (`BinaryOperator.StartsAfter`/`EndsBefore` arms don't exist in
`ApplyNumberRangeComparison`/`ApplyQuantityRangeComparison` yet, so those switch expressions throw
`NotSupportedException` at runtime for those two operators — and several `GreaterThan`/`LessThan`/
etc. cases will fail on wrong direction, e.g. `GreaterThan` at `searchValue=25` currently returns a
match because it reads `LowValue(10) > 25` is false... trace through: current code is
`LowValue > value` for GreaterThan, so `10 > 25` is false → no match, but test expects `false` too
for that row - check the `GreaterThan, 15m, true` row instead: current code gives `LowValue(10) >
15` = false, but test expects `true`. This row fails, confirming the direction bug is caught).

- [ ] **Step 3: Fix `ApplyNumberRangeComparison` and `ApplyQuantityRangeComparison`**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs`, replace:

```csharp
    public static IQueryable<Entities.NumberSearchParamEntity> ApplyNumberRangeComparison(
        IQueryable<Entities.NumberSearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.LowValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.LowValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.HighValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.HighValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Number comparison"),
    };
```

with:

```csharp
    public static IQueryable<Entities.NumberSearchParamEntity> ApplyNumberRangeComparison(
        IQueryable<Entities.NumberSearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.HighValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.HighValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.LowValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.LowValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        BinaryOperator.StartsAfter => query.Where(sp => sp.LowValue > value),
        BinaryOperator.EndsBefore => query.Where(sp => sp.HighValue < value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Number comparison"),
    };
```

And replace:

```csharp
    public static IQueryable<Entities.QuantitySearchParamEntity> ApplyQuantityRangeComparison(
        IQueryable<Entities.QuantitySearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.LowValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.LowValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.HighValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.HighValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Quantity comparison"),
    };
```

with:

```csharp
    public static IQueryable<Entities.QuantitySearchParamEntity> ApplyQuantityRangeComparison(
        IQueryable<Entities.QuantitySearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.HighValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.HighValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.LowValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.LowValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        BinaryOperator.StartsAfter => query.Where(sp => sp.LowValue > value),
        BinaryOperator.EndsBefore => query.Where(sp => sp.HighValue < value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Quantity comparison"),
    };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~ComparisonPredicatesTests"`
Expected: PASS, all cases (existing invalid-operator tests plus the 24 new theory rows — 12 per
method).

- [ ] **Step 5: Run the full DataLayer test suite to check for regressions**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: same 5 pre-existing failures as documented in Global Constraints, nothing new broken.
`SearchParameterQueryGeneratorQuantityAndTests`'s `eq`/`ap` cases will still pass because they
delegate to `ApplyQuantityRangeComparison` for both bounds, and this fix doesn't change what
`Equal` returns (only `GreaterThan`/`GreaterThanOrEqual`/`LessThan`/`LessThanOrEqual` direction
changed, and those tests use `eq`/`ap`/`ne`, not bare `gt`/`ge`/`lt`/`le`).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs
git commit -m "fix(sql): canonicalize Number/Quantity gt/ge/lt/le direction, add StartsAfter/EndsBefore"
```

---

### Task 3: Add `StartsAfter`/`EndsBefore` to DateTime comparators, unify `GenerateDateTimeQuery`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs`
  (`ApplyDateTimeStartComparison`, `ApplyDateTimeEndComparison`)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`
  (`GenerateDateTimeQuery`, lines 1609-1657)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs`
  (extend)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs`
  (new)

**Interfaces:**
- Consumes: `BinaryOperator.StartsAfter`/`EndsBefore` from Task 1; `BuildSingleConditionDateTimeQuery`
  (existing private static method at `SearchParameterQueryGenerator.cs:995-1005`, signature
  `(IQueryable<DateTimeSearchParamEntity> baseQuery, (FieldName Field, BinaryOperator Op, DateTime
  Value) condition) => IQueryable<long>`).
- Produces: `ApplyDateTimeStartComparison` gains a `StartsAfter => StartDateTime > value` arm (same
  formula its `GreaterThan` arm already has — behavior-preserving); `ApplyDateTimeEndComparison`
  gains an `EndsBefore => EndDateTime < value` arm (same formula its `LessThan` arm already has).
  `GenerateDateTimeQuery` stops maintaining its own separate `(FieldName, BinaryOperator)` switch
  and delegates to `BuildSingleConditionDateTimeQuery` instead — this is important, not cosmetic:
  without it, once Task 5 makes the parser emit `StartsAfter`/`EndsBefore` for DateTime `sa`/`eb`,
  any *standalone* (non-multiary) `sa`/`eb` DateTime search would hit `GenerateDateTimeQuery`'s old
  switch's `_ => throw NotSupportedException(...)` default and regress from working to throwing.

- [ ] **Step 1: Write the failing tests for `ComparisonPredicates`**

Add to `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs`:

```csharp
    [Fact]
    public void GivenStoredRange_WhenApplyDateTimeStartComparisonStartsAfter_ThenMatchesStrictSeparation()
    {
        var stored = new[]
        {
            new DateTimeSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, StartDateTime = new DateTime(2020, 1, 10), EndDateTime = new DateTime(2020, 1, 20) }
        }.AsQueryable();

        ComparisonPredicates.ApplyDateTimeStartComparison(stored, BinaryOperator.StartsAfter, new DateTime(2020, 1, 5)).Count().ShouldBe(1);
        ComparisonPredicates.ApplyDateTimeStartComparison(stored, BinaryOperator.StartsAfter, new DateTime(2020, 1, 15)).Count().ShouldBe(0);
    }

    [Fact]
    public void GivenStoredRange_WhenApplyDateTimeEndComparisonEndsBefore_ThenMatchesStrictSeparation()
    {
        var stored = new[]
        {
            new DateTimeSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, StartDateTime = new DateTime(2020, 1, 10), EndDateTime = new DateTime(2020, 1, 20) }
        }.AsQueryable();

        ComparisonPredicates.ApplyDateTimeEndComparison(stored, BinaryOperator.EndsBefore, new DateTime(2020, 1, 25)).Count().ShouldBe(1);
        ComparisonPredicates.ApplyDateTimeEndComparison(stored, BinaryOperator.EndsBefore, new DateTime(2020, 1, 15)).Count().ShouldBe(0);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~ComparisonPredicatesTests"`
Expected: FAIL — both new facts throw `NotSupportedException` (no `StartsAfter`/`EndsBefore` arm
yet in either DateTime method).

- [ ] **Step 3: Add the new arms to `ComparisonPredicates`' DateTime methods**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs`, in
`ApplyDateTimeStartComparison`, change:

```csharp
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.StartDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for DateTime start comparison"),
```

to:

```csharp
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.StartDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.StartsAfter => query.Where(sp => sp.StartDateTime > value).Select(sp => sp.ResourceSurrogateId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for DateTime start comparison"),
```

In `ApplyDateTimeEndComparison`, change:

```csharp
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.EndDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for DateTime end comparison"),
```

to:

```csharp
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.EndDateTime <= value).Select(sp => sp.ResourceSurrogateId),
        BinaryOperator.EndsBefore => query.Where(sp => sp.EndDateTime < value).Select(sp => sp.ResourceSurrogateId),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for DateTime end comparison"),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~ComparisonPredicatesTests"`
Expected: PASS.

- [ ] **Step 5: Write the failing test for `GenerateDateTimeQuery`'s unification**

Create `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// Covers GenerateDateTimeQuery - the standalone (non-multiary) DateTime BinaryExpression path,
/// which historically maintained its own (FieldName, BinaryOperator) switch independent of
/// ComparisonPredicates. Confirms the unification onto BuildSingleConditionDateTimeQuery preserves
/// existing behavior and correctly wires the new StartsAfter/EndsBefore operators.
/// </summary>
public class SearchParameterQueryGeneratorDateTimeTests : TestBase
{
    private const short ObservationTypeId = 3;
    private const short DateParamId = 8;
    private const string DateParamUrl = "http://hl7.org/fhir/SearchParameter/Observation-date";

    private readonly SearchParameterQueryGenerator _generator;
    private readonly SearchParameterExpressionParser _parser;
    private readonly SearchParameterInfo _dateParam;

    public SearchParameterQueryGeneratorDateTimeTests()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());

        _generator = new SearchParameterQueryGenerator(
            Context,
            Cache,
            LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(),
            compositeGenerator);

        _parser = new SearchParameterExpressionParser(
            Substitute.For<IReferenceSearchValueParser>(),
            Substitute.For<IFhirSchemaProvider>());

        Context.SearchParams.Add(new SearchParamEntity
        {
            SearchParamId = DateParamId,
            Uri = DateParamUrl,
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow
        });
        Context.SaveChanges();

        _dateParam = new SearchParameterInfo("date", "date", SearchParamType.Date, new Uri(DateParamUrl));
    }

    private async Task<long> CreateObservationWithDateAsync(string resourceId, DateTime start, DateTime end)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        Context.DateTimeSearchParams.Add(new DateTimeSearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = DateParamId,
            StartDateTime = start,
            EndDateTime = end,
            IsLongerThanADay = false,
            IsMin = false,
            IsMax = false
        });
        Context.SaveChanges();

        return resource.ResourceSurrogateId;
    }

    private async Task<List<long>> RunSearchAsync(string queryValue)
    {
        var expression = (SearchParameterExpression)_parser.Parse(_dateParam, modifier: null, queryValue);
        var query = await _generator.GenerateQueryAsync(ObservationTypeId, expression, CancellationToken.None);
        return await query.ToListAsync();
    }

    [Fact]
    public async Task GivenBareGtDateSearch_WhenGeneratingQuery_ThenUnificationPreservesExistingBehavior()
    {
        var matching = await CreateObservationWithDateAsync("obs-late", new DateTime(2020, 6, 1), new DateTime(2020, 6, 1));
        await CreateObservationWithDateAsync("obs-early", new DateTime(2019, 1, 1), new DateTime(2019, 1, 1));

        var results = await RunSearchAsync("gt2020-01-01");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenBareSaDateSearch_WhenGeneratingQuery_ThenAppliesStrictAfterSemantics()
    {
        var matching = await CreateObservationWithDateAsync("obs-clearly-after", new DateTime(2020, 6, 1), new DateTime(2020, 6, 1));
        await CreateObservationWithDateAsync("obs-overlapping-year", new DateTime(2020, 1, 1), new DateTime(2020, 12, 31));

        var results = await RunSearchAsync("sa2020-01-01");

        // "obs-overlapping-year" is a whole-year-precision value straddling the search boundary -
        // sa (strictly after, ignoring precision widening) must exclude it, unlike gt's overlap test.
        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenBareEbDateSearch_WhenGeneratingQuery_ThenAppliesStrictBeforeSemantics()
    {
        var matching = await CreateObservationWithDateAsync("obs-clearly-before", new DateTime(2018, 1, 1), new DateTime(2018, 1, 1));
        await CreateObservationWithDateAsync("obs-overlapping-year", new DateTime(2019, 1, 1), new DateTime(2019, 12, 31));

        var results = await RunSearchAsync("eb2019-06-15");

        results.ShouldBe(new[] { matching });
    }
}
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorDateTimeTests"`
Expected: `GivenBareGtDateSearch_...` PASSES already (unrelated to this change). `GivenBareSaDateSearch_...`
and `GivenBareEbDateSearch_...` FAIL — at this point in the plan, the parser (Task 5) hasn't
changed yet, so `sa`/`eb` are still aliased to `Gt`/`Lt` via `FieldName`, which happens to already
produce the correct strict-separation result through the *current* `GenerateDateTimeQuery` switch
(since `(FieldName.DateTimeStart, GreaterThan)` is handled there today) — so these two may actually
PASS already before any further code change. If so, that's fine and expected: it confirms today's
`sa`/`eb` behavior is already correct (per the design doc), and this task's job is to make sure it
*stays* correct once Task 5 changes the operator these use. Proceed to Step 7 regardless.

- [ ] **Step 7: Unify `GenerateDateTimeQuery` onto `BuildSingleConditionDateTimeQuery`**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`,
replace the entire body of `GenerateDateTimeQuery` (currently lines 1609-1657):

```csharp
    private IQueryable<long> GenerateDateTimeQuery(short? resourceTypeId, short? searchParamId, BinaryExpression binaryExpr)
    {
        // Handle both DateTime and DateTimeOffset
        DateTime value = binaryExpr.Value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => Convert.ToDateTime(binaryExpr.Value)
        };

        _logger.LogDebug(
            "GenerateDateTimeQuery: FieldName={FieldName}, Operator={Operator}, Value={Value}, ResourceTypeId={ResourceTypeId}, SearchParamId={SearchParamId}",
            binaryExpr.FieldName,
            binaryExpr.BinaryOperator,
            value.ToString("o"),
            resourceTypeId,
            searchParamId);

        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.DateTimeSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Apply comparison based on FieldName (Start vs End) and operator
        // The expression parser creates expressions targeting specific fields:
        // - DateTimeStart comparisons filter on sp.StartDateTime
        // - DateTimeEnd comparisons filter on sp.EndDateTime
        query = (binaryExpr.FieldName, binaryExpr.BinaryOperator) switch
        {
            (FieldName.DateTimeStart, BinaryOperator.GreaterThanOrEqual) => query.Where(sp => sp.StartDateTime >= value),
            (FieldName.DateTimeStart, BinaryOperator.GreaterThan) => query.Where(sp => sp.StartDateTime > value),
            (FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual) => query.Where(sp => sp.StartDateTime <= value),
            (FieldName.DateTimeStart, BinaryOperator.LessThan) => query.Where(sp => sp.StartDateTime < value),
            (FieldName.DateTimeStart, BinaryOperator.Equal) => query.Where(sp => sp.StartDateTime == value),
            (FieldName.DateTimeStart, BinaryOperator.NotEqual) => query.Where(sp => sp.StartDateTime != value),

            (FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual) => query.Where(sp => sp.EndDateTime >= value),
            (FieldName.DateTimeEnd, BinaryOperator.GreaterThan) => query.Where(sp => sp.EndDateTime > value),
            (FieldName.DateTimeEnd, BinaryOperator.LessThanOrEqual) => query.Where(sp => sp.EndDateTime <= value),
            (FieldName.DateTimeEnd, BinaryOperator.LessThan) => query.Where(sp => sp.EndDateTime < value),
            (FieldName.DateTimeEnd, BinaryOperator.Equal) => query.Where(sp => sp.EndDateTime == value),
            (FieldName.DateTimeEnd, BinaryOperator.NotEqual) => query.Where(sp => sp.EndDateTime != value),

            _ => throw new NotSupportedException($"DateTime search with FieldName {binaryExpr.FieldName} and BinaryOperator {binaryExpr.BinaryOperator} is not supported")
        };

        return query.Select(sp => sp.ResourceSurrogateId);
    }
```

with:

```csharp
    private IQueryable<long> GenerateDateTimeQuery(short? resourceTypeId, short? searchParamId, BinaryExpression binaryExpr)
    {
        // Handle both DateTime and DateTimeOffset
        DateTime value = binaryExpr.Value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => Convert.ToDateTime(binaryExpr.Value)
        };

        _logger.LogDebug(
            "GenerateDateTimeQuery: FieldName={FieldName}, Operator={Operator}, Value={Value}, ResourceTypeId={ResourceTypeId}, SearchParamId={SearchParamId}",
            binaryExpr.FieldName,
            binaryExpr.BinaryOperator,
            value.ToString("o"),
            resourceTypeId,
            searchParamId);

        // When resourceTypeId is null (system-wide search), don't filter by resource type
        // Filter by SearchParamId to only match values indexed for this specific parameter
        var query = _context.DateTimeSearchParams
            .Where(sp => (!resourceTypeId.HasValue || sp.ResourceTypeId == resourceTypeId.Value)
                && (!searchParamId.HasValue || sp.SearchParamId == searchParamId.Value));

        // Delegates to the same ComparisonPredicates-backed dispatch the multiary DateTime path
        // uses, instead of maintaining a second, independent (FieldName, BinaryOperator) switch.
        return BuildSingleConditionDateTimeQuery(query, (binaryExpr.FieldName, binaryExpr.BinaryOperator, value));
    }
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorDateTimeTests"`
Expected: PASS, 3/3.

- [ ] **Step 9: Run the full DataLayer test suite to check for regressions**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: same 5 pre-existing failures, nothing new broken. In particular check
`SearchParameterQueryGeneratorResourceLevelTests` (the `_lastUpdated`/`_ttl` theory) still passes -
those don't go through `GenerateDateTimeQuery`/`ComparisonPredicates.ApplyDateTime*` at all (they
use `ApplySurrogateIdComparison`/`ApplyTtlComparison`), so should be unaffected, but confirm.

- [ ] **Step 10: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs
git commit -m "fix(sql): add StartsAfter/EndsBefore to DateTime comparators, unify GenerateDateTimeQuery"
```

---

### Task 4: Stop aliasing `sa`/`eb` to `gt`/`lt` for Number/Quantity in the parser

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs:349-377`
  (`GenerateNumberExpression`)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorQuantityAndTests.cs`
  (extend — reuses the existing end-to-end parser+generator test pattern from Bug 1)

**Interfaces:**
- Consumes: `BinaryOperator.StartsAfter`/`EndsBefore` (Task 1), `ComparisonPredicates`' new arms
  (Task 2) - by the time this task ships, the operators these produce are already correctly handled
  downstream.
- Produces: `GenerateNumberExpression` now emits `BinaryOperator.StartsAfter` for
  `SearchComparator.Sa` and `BinaryOperator.EndsBefore` for `SearchComparator.Eb`, instead of
  aliasing into the `Gt`/`Lt` cases.

- [ ] **Step 1: Write the failing test**

`5.4`'s precision-widened range is `[5.35, 5.45)`, and `gt`/`sa` ignore the *search* value's own
precision (compare against the raw `5.4`) — so `gt` and `sa` are only distinguishable when the
*stored* value itself is a fuzzy range (`Low != High`) straddling `5.4`. The existing
`CreateObservationWithQuantityAsync` helper always sets `Low == High` (a point value), which can't
produce that distinction — add a second helper that sets `Low`/`High` independently, then use it.

Add to `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorQuantityAndTests.cs`
(same class, alongside the existing `CreateObservationWithQuantityAsync`/`RunSearchAsync` helpers
from Bug 1's fix):

```csharp
    private async Task<long> CreateObservationWithQuantityRangeAsync(string resourceId, decimal low, decimal high, string? system, string? code)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        int? systemId = system is null ? null : await Cache.GetOrCreateSystemIdAsync(system);
        int? codeId = code is null ? null : await Cache.GetOrCreateQuantityCodeIdAsync(code);

        Context.QuantitySearchParams.Add(new QuantitySearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = ValueQuantityParamId,
            SystemId = systemId,
            QuantityCodeId = codeId,
            SingleValue = null,
            LowValue = low,
            HighValue = high
        });
        Context.SaveChanges();

        return resource.ResourceSurrogateId;
    }

    [Fact]
    public async Task GivenUnitQualifiedSaQuantitySearch_WhenGeneratingQuery_ThenExcludesStraddlingRange()
    {
        // Stored range [5.0, 6.0] straddles the search boundary of 5.4: gt (overlap-above) would
        // match it (HighValue 6.0 > 5.4), but sa (strictly after, no overlap) must not
        // (LowValue 5.0 > 5.4 is false) - exactly the distinction lost by aliasing sa to gt.
        await CreateObservationWithQuantityRangeAsync("obs-straddling", 5.0m, 6.0m, Ucum, "mg");
        var clearlyAfter = await CreateObservationWithQuantityRangeAsync("obs-clearly-after", 10.0m, 10.0m, Ucum, "mg");

        var results = await RunSearchAsync($"sa5.4|{Ucum}|mg");

        results.ShouldBe(new[] { clearlyAfter });
    }

    [Fact]
    public async Task GivenUnitQualifiedEbQuantitySearch_WhenGeneratingQuery_ThenExcludesStraddlingRange()
    {
        // Stored range [5.0, 6.0] straddles the search boundary of 5.4: lt (overlap-below) would
        // match it (LowValue 5.0 < 5.4), but eb (strictly before, no overlap) must not
        // (HighValue 6.0 < 5.4 is false).
        await CreateObservationWithQuantityRangeAsync("obs-straddling", 5.0m, 6.0m, Ucum, "mg");
        var clearlyBefore = await CreateObservationWithQuantityRangeAsync("obs-clearly-before", 1.0m, 1.0m, Ucum, "mg");

        var results = await RunSearchAsync($"eb5.4|{Ucum}|mg");

        results.ShouldBe(new[] { clearlyBefore });
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorQuantityAndTests"`
Expected: both new facts FAIL. Before this task's fix, the parser still aliases `sa`→`Gt` and
`eb`→`Lt` (Tasks 1-2 only added the new operators — they didn't change what the parser emits yet).
So `sa5.4` reaches `ComparisonPredicates.ApplyQuantityRangeComparison` with
`BinaryOperator.GreaterThan`, whose (Task-2-fixed) formula is `HighValue > value`:
`obs-straddling`'s `HighValue(6.0) > 5.4` is true, so it incorrectly matches too — `results`
contains both `obs-straddling` and `clearlyAfter` instead of just `clearlyAfter`, failing
`ShouldBe`. Symmetric failure for the `eb`/`Lt` case.

- [ ] **Step 3: Stop aliasing `Sa`/`Eb` in `GenerateNumberExpression`**

In `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs`, replace:

```csharp
            case SearchComparator.Ge:
                return Expression.GreaterThanOrEqual(fieldName, _componentIndex, number);
            case SearchComparator.Gt:
            case SearchComparator.Sa:
                return Expression.GreaterThan(fieldName, _componentIndex, number);
            case SearchComparator.Le:
                return Expression.LessThanOrEqual(fieldName, _componentIndex, number);
            case SearchComparator.Lt:
            case SearchComparator.Eb:
                return Expression.LessThan(fieldName, _componentIndex, number);
```

with:

```csharp
            case SearchComparator.Ge:
                return Expression.GreaterThanOrEqual(fieldName, _componentIndex, number);
            case SearchComparator.Gt:
                return Expression.GreaterThan(fieldName, _componentIndex, number);
            case SearchComparator.Sa:
                return Expression.StartsAfter(fieldName, _componentIndex, number);
            case SearchComparator.Le:
                return Expression.LessThanOrEqual(fieldName, _componentIndex, number);
            case SearchComparator.Lt:
                return Expression.LessThan(fieldName, _componentIndex, number);
            case SearchComparator.Eb:
                return Expression.EndsBefore(fieldName, _componentIndex, number);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorQuantityAndTests"`
Expected: PASS, all cases (5 from Bug 1's original fix + 2 new).

- [ ] **Step 5: Run the full DataLayer test suite to check for regressions**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: same 5 pre-existing failures, nothing new broken.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorQuantityAndTests.cs
git commit -m "fix(search): stop aliasing sa/eb to gt/lt for Number/Quantity in the parser"
```

---

### Task 5: Stop aliasing `sa`/`eb` to `gt`/`lt` for DateTime in the parser

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs:83-88`
  (DateTime `Visit`)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs`
  (already covers this end-to-end from Task 3 — this task is what makes those `sa`/`eb` tests
  actually exercise the new operator rather than the old aliased one)

**Interfaces:**
- Consumes: `Expression.StartsAfter`/`EndsBefore` (Task 1), `ComparisonPredicates`' DateTime
  `StartsAfter`/`EndsBefore` arms and `GenerateDateTimeQuery`'s unification (Task 3).
- Produces: DateTime `sa` now emits `BinaryExpression(BinaryOperator.StartsAfter,
  FieldName.DateTimeStart, ..., dateTime.End)` (was `GreaterThan`); `eb` now emits
  `BinaryExpression(BinaryOperator.EndsBefore, FieldName.DateTimeEnd, ..., dateTime.Start)` (was
  `LessThan`). `FieldName` is unchanged in both cases.

- [ ] **Step 1: Confirm the existing Task 3 tests currently pass for the wrong reason**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorDateTimeTests"`
Expected: PASS (all 3, per Task 3's Step 8) - `sa`/`eb` currently reach `GenerateDateTimeQuery` as
aliased `GreaterThan`/`LessThan` with the correct `FieldName`, and since Task 3 already added
correct handling there, the *result* is already right. This step is a checkpoint, not new
coverage - the real point of this task is making the operator itself honest, not fixing an
observable bug (there isn't one left to fix for DateTime specifically, thanks to Task 3).

- [ ] **Step 2: Stop aliasing `Sa`/`Eb` in the DateTime `Visit` method**

In `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs`, replace:

```csharp
            case SearchComparator.Sa:
                _outputExpression = Expression.GreaterThan(FieldName.DateTimeStart, _componentIndex, dateTime.End);
                break;
            case SearchComparator.Eb:
                _outputExpression = Expression.LessThan(FieldName.DateTimeEnd, _componentIndex, dateTime.Start);
                break;
```

with:

```csharp
            case SearchComparator.Sa:
                _outputExpression = Expression.StartsAfter(FieldName.DateTimeStart, _componentIndex, dateTime.End);
                break;
            case SearchComparator.Eb:
                _outputExpression = Expression.EndsBefore(FieldName.DateTimeEnd, _componentIndex, dateTime.Start);
                break;
```

- [ ] **Step 3: Run test to verify it still passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorDateTimeTests"`
Expected: PASS, 3/3 - now passing because the operator is genuinely `StartsAfter`/`EndsBefore` and
correctly handled by Task 3's new arms, not because it coincidentally aliased to something else
that happened to work.

- [ ] **Step 4: Add a test proving `sa` and `gt` (and `eb`/`lt`) are now genuinely distinguishable**

This is the regression test that would have caught the original "indistinguishable on the InMemory
backend" finding if it existed earlier - add to
`test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs`:

```csharp
    [Fact]
    public async Task GivenOverlappingStoredRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults()
    {
        // A stored range that overlaps the search boundary: gt (overlap-above) must match it,
        // sa (strictly after, no overlap) must not - this is exactly the distinction that was
        // lost when both aliased to the same BinaryOperator.GreaterThan.
        var straddling = await CreateObservationWithDateAsync("obs-straddling", new DateTime(2019, 6, 1), new DateTime(2020, 6, 1));

        var gtResults = await RunSearchAsync("gt2020-01-01");
        var saResults = await RunSearchAsync("sa2020-01-01");

        gtResults.ShouldBe(new[] { straddling });
        saResults.ShouldBeEmpty();
    }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorDateTimeTests"`
Expected: PASS, 4/4.

- [ ] **Step 6: Run the full DataLayer test suite to check for regressions**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: same 5 pre-existing failures, nothing new broken.

- [ ] **Step 7: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs
git commit -m "fix(search): stop aliasing sa/eb to gt/lt for DateTime in the parser"
```

---

### Task 6: Unify the composite SQL path onto `ComparisonPredicates`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs` (new
  overloads)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs:587-696`
  (`ApplyQuantityFilterAsync`)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs`
  (extend)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs`
  (extend)

**Interfaces:**
- Consumes: `ComparisonPredicates.ApplyQuantityRangeComparison` (Task 2's fixed single-param
  overload), overloaded here for the composite entity shape.
- Produces: a new `ComparisonPredicates.ApplyQuantityRangeComparison(IQueryable<
  TokenQuantityCompositeSearchParamEntity>, BinaryOperator, decimal)` overload with the same
  canonical formulas as the single-param version. `CompositeSearchParameterQueryGenerator.
  ApplyQuantityFilterAsync` delegates to it instead of its own inline switch - this also fixes,
  as a side effect, the already-documented deferred issue (plan's Post-Plan finding #3) where the
  composite path's `_ => query` default silently applied no filter for an unsupported operator:
  the new overload throws `NotSupportedException` like every other `ComparisonPredicates` method.

- [ ] **Step 1: Write the failing test for the new composite overload**

Add to `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs`:

```csharp
    [Theory]
    [MemberData(nameof(NumberRangeComparisonCases))]
    public void GivenStoredCompositeRange_WhenApplyQuantityRangeComparison_ThenMatchesCanonicalSemantics(
        BinaryOperator op, decimal searchValue, bool expectMatch)
    {
        var stored = new[]
        {
            new TokenQuantityCompositeSearchParamEntity { ResourceTypeId = 1, ResourceSurrogateId = 1, SearchParamId = 1, Code1 = "code", LowValue = 10m, HighValue = 20m }
        }.AsQueryable();

        var results = ComparisonPredicates.ApplyQuantityRangeComparison(stored, op, searchValue).ToList();

        results.Count.ShouldBe(expectMatch ? 1 : 0);
    }

    [Fact]
    public void GivenInvalidOperator_WhenApplyQuantityRangeComparisonOnComposite_ThenThrowsNotSupported()
    {
        var query = Enumerable.Empty<TokenQuantityCompositeSearchParamEntity>().AsQueryable();

        Should.Throw<NotSupportedException>(() =>
            ComparisonPredicates.ApplyQuantityRangeComparison(query, InvalidOperator, 1m));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~ComparisonPredicatesTests"`
Expected: FAIL to compile - no `ApplyQuantityRangeComparison` overload accepts
`IQueryable<TokenQuantityCompositeSearchParamEntity>` yet.

- [ ] **Step 3: Add the composite-shaped overload**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs`, immediately
after the existing `ApplyQuantityRangeComparison` method (the one operating on
`Entities.QuantitySearchParamEntity`), add:

```csharp
    public static IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> ApplyQuantityRangeComparison(
        IQueryable<Entities.TokenQuantityCompositeSearchParamEntity> query, BinaryOperator op, decimal value) => op switch
    {
        BinaryOperator.Equal => query.Where(sp => sp.LowValue <= value && sp.HighValue >= value),
        BinaryOperator.GreaterThan => query.Where(sp => sp.HighValue > value),
        BinaryOperator.GreaterThanOrEqual => query.Where(sp => sp.HighValue >= value),
        BinaryOperator.LessThan => query.Where(sp => sp.LowValue < value),
        BinaryOperator.LessThanOrEqual => query.Where(sp => sp.LowValue <= value),
        BinaryOperator.NotEqual => query.Where(sp => sp.HighValue < value || sp.LowValue > value),
        BinaryOperator.StartsAfter => query.Where(sp => sp.LowValue > value),
        BinaryOperator.EndsBefore => query.Where(sp => sp.HighValue < value),
        _ => throw new NotSupportedException($"Binary operator {op} is not supported for Quantity comparison"),
    };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~ComparisonPredicatesTests"`
Expected: PASS.

- [ ] **Step 5: Write the failing test for the composite generator's delegation**

`CompositeSearchParameterQueryGeneratorTests.cs`'s existing `GivenTokenQuantityComposite_
WhenValueInRange_ThenReturnsResource` test (lines 63-98) establishes the pattern: a
`TokenQuantityCompositeSearchParamEntity` row, a token `StringExpression` for `component0`, and
either a bare `BinaryExpression` or a `MultiaryExpression`-wrapped pair for `component1`, passed to
`_generator.GenerateTokenQuantityQueryAsync(resourceTypeId, searchParamId, component0, component1,
CancellationToken.None)`. Add, following that exact pattern:

```csharp
    [Fact]
    public async Task GivenOverlappingStoredCompositeRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults()
    {
        // Arrange
        var resource = CreateResource(resourceTypeId: 3, resourceId: "obs-straddling");
        const short searchParamId = 102;

        Context.TokenQuantityCompositeSearchParams.Add(new TokenQuantityCompositeSearchParamEntity
        {
            ResourceTypeId = 3,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = searchParamId,
            Code1 = "8462-4",
            SystemId1 = null,
            LowValue = 10m,
            HighValue = 20m,
        });
        await Context.SaveChangesAsync();

        var component0 = new StringExpression(StringOperator.Equals, FieldName.TokenCode, null, "8462-4", false);
        var gtComponent1 = new BinaryExpression(BinaryOperator.GreaterThan, FieldName.Quantity, null, 15m);
        var saComponent1 = new BinaryExpression(BinaryOperator.StartsAfter, FieldName.Quantity, null, 15m);

        // Act
        var gtQuery = await _generator.GenerateTokenQuantityQueryAsync(resourceTypeId: 3, searchParamId, component0, gtComponent1, CancellationToken.None);
        var gtResults = await gtQuery.ToListAsync();

        var saQuery = await _generator.GenerateTokenQuantityQueryAsync(resourceTypeId: 3, searchParamId, component0, saComponent1, CancellationToken.None);
        var saResults = await saQuery.ToListAsync();

        // Assert: High(20) > 15 is true (gt matches); Low(10) > 15 is false (sa must not match) -
        // exactly the distinction composite's pre-fix wrong-direction gt/lt couldn't make.
        gtResults.ShouldHaveSingleItem();
        saResults.ShouldBeEmpty();
    }
```

- [ ] **Step 6: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: FAIL. Composite's current `gt` arm reads `LowValue > value` (the pre-fix wrong
direction), so `gtQuery` against `Low=10, High=20` with `value=15` gives `10 > 15` = false —
`gtResults` is empty, contradicting `gtResults.ShouldHaveSingleItem()`. `saComponent1` uses
`BinaryOperator.StartsAfter`, which composite's inline switch doesn't recognize at all yet — its
`_ => query` default applies no filter, so `saResults` contains the resource unfiltered, contradicting
`saResults.ShouldBeEmpty()`.

- [ ] **Step 7: Replace `ApplyQuantityFilterAsync`'s inline switch with delegation**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`,
replace the value-filter section of `ApplyQuantityFilterAsync` (currently, per the method's tail):

```csharp
        // Apply value filter based on what was extracted:
        // - Two BinaryExpressions (eq/ap): Range query - stored range must overlap search range
        // - One BinaryExpression: Single comparator (ge, le, gt, lt)
        if (quantityBinaryExpressions.Count == 2)
        {
            // Range query (equality/approximate): both GreaterThanOrEqual and LessThanOrEqual present
            var lowerBound = quantityBinaryExpressions.FirstOrDefault(e => e.Op == BinaryOperator.GreaterThanOrEqual).Value;
            var upperBound = quantityBinaryExpressions.FirstOrDefault(e => e.Op == BinaryOperator.LessThanOrEqual).Value;

            // Range overlap: stored range must overlap with search range
            // For exact match with stored value X: lowerBound <= X <= upperBound
            query = query.Where(q => q.LowValue <= upperBound && q.HighValue >= lowerBound);
        }
        else if (quantityBinaryExpressions.Count == 1)
        {
            // Single comparator
            var (op, value) = quantityBinaryExpressions[0];
            query = op switch
            {
                // ge: stored value must be >= search value (check HighValue for range overlap)
                BinaryOperator.GreaterThanOrEqual => query.Where(q => q.HighValue >= value),
                // le: stored value must be <= search value (check LowValue for range overlap)
                BinaryOperator.LessThanOrEqual => query.Where(q => q.LowValue <= value),
                // gt: stored value must be > search value
                BinaryOperator.GreaterThan => query.Where(q => q.LowValue > value),
                // lt: stored value must be < search value
                BinaryOperator.LessThan => query.Where(q => q.HighValue < value),
                // ne: stored value must not equal search value
                BinaryOperator.NotEqual => query.Where(q => q.HighValue < value || q.LowValue > value),
                _ => query
            };
        }
```

with:

```csharp
        // Apply value filter based on what was extracted:
        // - Two BinaryExpressions (eq/ap): Range query - both bounds applied sequentially so EF ANDs them
        // - One BinaryExpression: single comparator, delegated to ComparisonPredicates directly
        if (quantityBinaryExpressions.Count == 2)
        {
            var lowerBound = quantityBinaryExpressions.FirstOrDefault(e => e.Op == BinaryOperator.GreaterThanOrEqual).Value;
            var upperBound = quantityBinaryExpressions.FirstOrDefault(e => e.Op == BinaryOperator.LessThanOrEqual).Value;

            query = ComparisonPredicates.ApplyQuantityRangeComparison(query, BinaryOperator.GreaterThanOrEqual, lowerBound);
            query = ComparisonPredicates.ApplyQuantityRangeComparison(query, BinaryOperator.LessThanOrEqual, upperBound);
        }
        else if (quantityBinaryExpressions.Count == 1)
        {
            var (op, value) = quantityBinaryExpressions[0];
            query = ComparisonPredicates.ApplyQuantityRangeComparison(query, op, value);
        }
```

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: PASS, including all pre-existing composite tests in this file (the `eq`/`ap` two-bound
case's *result* is unchanged: `ApplyQuantityRangeComparison(Ge, lowerBound)` then `(Le,
upperBound)` chained gives `HighValue >= lowerBound AND LowValue <= upperBound` - identical to the
prior inline formula, just computed via the shared method).

- [ ] **Step 9: Run the full DataLayer test suite to check for regressions**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: same 5 pre-existing failures, nothing new broken.

- [ ] **Step 10: Update the plan doc's carried-forward finding about composite's silent-filter bug**

The Post-Plan section of `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md`
(finding #3, already partially updated once this session) documents `CompositeSearchParameterQueryGenerator`'s
`_ => query` silent-no-filter default as a known, deferred issue. This task fixes it as a side
effect of the unification (the new default is `throw NotSupportedException`, matching every other
`ComparisonPredicates` method). Update that finding to mark this closed: open the file, find the
paragraph about finding #3's "still open" composite generator issue, and add a strikethrough +
"**Fixed**" note in the same style as the file's other resolved findings (see how finding #4 and
the `TokenCodeStorage` fix were marked resolved earlier in this same file, for the exact
formatting convention to match).

- [ ] **Step 11: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/ComparisonPredicates.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/ComparisonPredicatesTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md
git commit -m "fix(sql): unify composite quantity comparator onto ComparisonPredicates"
```

---

### Task 7: Fix `ComparisonValueVisitor`'s Number/Quantity/DateTime bound selection (InMemory backend)

**Files:**
- Modify: `src/Core/Ignixa.Search/InMemory/ComparisonValueVisitor.cs`
- Test: `test/Ignixa.Application.Tests/Search/SearchQueryInterpreterComparisonTests.cs` (new)

**Interfaces:**
- Consumes: `BinaryOperator.StartsAfter`/`EndsBefore` (Task 1); tested via `SearchQueryInterpreter`'s
  public surface (`VisitBinary`), since `ComparisonValueVisitor` itself is `internal`.
- Produces: `Visit(NumberSearchValue)`/`Visit(QuantitySearchValue)` select `.Low` for `{LessThan,
  LessThanOrEqual, StartsAfter}` and `.High` for `{GreaterThan, GreaterThanOrEqual, EndsBefore}`
  (was always `.High`). `Visit(DateTimeSearchValue)` selects `.Start` for the same three operators
  and `.End` for the other three (was always `.Start`). `AddComparison` gains `StartsAfter`/
  `EndsBefore` arms routed to the same comparison logic as `GreaterThan`/`LessThan` respectively.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Application.Tests/Search/SearchQueryInterpreterComparisonTests.cs`. This tests
`ComparisonValueVisitor` (internal) through `SearchQueryInterpreter`'s public `VisitBinary`, which
is the only way the InMemory backend is exercised elsewhere in this codebase:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Covers ComparisonValueVisitor's Number/Quantity/DateTime bound selection through
/// SearchQueryInterpreter's public surface (ComparisonValueVisitor itself is internal). Locks down
/// that gt/ge/lt/le/sa/eb pick the correct stored-side bound - previously Number/Quantity always
/// read .High and DateTime always read .Start regardless of operator.
/// </summary>
public class SearchQueryInterpreterComparisonTests
{
    private static readonly SearchParameterInfo NumberParam = new("value-number", "value-number", SearchParamType.Number);

    private static bool Evaluate(BinaryExpression expression, ISearchValue storedValue)
    {
        var interpreter = new SearchQueryInterpreter();
        var context = new SearchQueryInterpreter.Context { ParameterName = "value-number" };
        var predicate = interpreter.VisitBinary(expression, context);

        var resourceKey = new ResourceKey("Observation", "obs-1");
        var index = new SearchIndexEntry[] { new(NumberParam, storedValue) };
        var input = new (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)[]
        {
            (resourceKey, index)
        };

        return predicate(input).Any();
    }

    [Theory]
    [InlineData(BinaryOperator.GreaterThan, 15, true)]      // High(20) > 15
    [InlineData(BinaryOperator.GreaterThan, 25, false)]     // High(20) > 25 is false
    [InlineData(BinaryOperator.GreaterThanOrEqual, 20, true)]
    [InlineData(BinaryOperator.LessThan, 15, true)]         // Low(10) < 15
    [InlineData(BinaryOperator.LessThan, 5, false)]         // Low(10) < 5 is false
    [InlineData(BinaryOperator.LessThanOrEqual, 10, true)]
    [InlineData(BinaryOperator.StartsAfter, 5, true)]       // Low(10) > 5
    [InlineData(BinaryOperator.StartsAfter, 15, false)]     // distinguishes Sa from Gt
    [InlineData(BinaryOperator.EndsBefore, 25, true)]       // High(20) < 25
    [InlineData(BinaryOperator.EndsBefore, 15, false)]      // distinguishes Eb from Lt
    public void GivenStoredNumberRange_WhenComparing_ThenMatchesCanonicalSemantics(BinaryOperator op, int searchValue, bool expectMatch)
    {
        var stored = new NumberSearchValue(10m, 20m);
        var expression = new BinaryExpression(op, FieldName.Number, componentIndex: null, (decimal)searchValue);

        Evaluate(expression, stored).ShouldBe(expectMatch);
    }

    [Fact]
    public void GivenOverlappingStoredDateRange_WhenComparingGtAndSa_ThenTheyProduceDifferentResults()
    {
        var start = new PartialDateTime(new DateTimeOffset(2019, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var end = new PartialDateTime(new DateTimeOffset(2020, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var stored = new DateTimeSearchValue(start, end);
        var boundary = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var gt = new BinaryExpression(BinaryOperator.GreaterThan, FieldName.DateTimeEnd, componentIndex: null, boundary);
        var sa = new BinaryExpression(BinaryOperator.StartsAfter, FieldName.DateTimeStart, componentIndex: null, boundary);

        Evaluate(gt, stored).ShouldBeTrue();
        Evaluate(sa, stored).ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchQueryInterpreterComparisonTests"`
Expected: FAIL - `StartsAfter`/`EndsBefore` cases throw `ArgumentOutOfRangeException` from
`AddComparison`'s current default arm; `GreaterThan`/`LessThan`/etc. cases fail because `Visit
(NumberSearchValue)` always uses `.High`, giving wrong results for the `LessThan`/`LessThanOrEqual`
rows.

- [ ] **Step 3: Fix `ComparisonValueVisitor`**

In `src/Core/Ignixa.Search/InMemory/ComparisonValueVisitor.cs`, replace:

```csharp
    public void Visit(DateTimeSearchValue dateTime)
    {
        EnsureArg.IsNotNull(dateTime, nameof(dateTime));
        AddComparison(_expressionBinaryOperator, dateTime.Start);
    }

    public void Visit(NumberSearchValue number)
    {
        EnsureArg.IsNotNull(number, nameof(number));
        AddComparison(_expressionBinaryOperator, number.High);
    }

    public void Visit(QuantitySearchValue quantity)
    {
        EnsureArg.IsNotNull(quantity, nameof(quantity));
        AddComparison(_expressionBinaryOperator, quantity.High);
    }
```

with:

```csharp
    public void Visit(DateTimeSearchValue dateTime)
    {
        EnsureArg.IsNotNull(dateTime, nameof(dateTime));
        AddComparison(_expressionBinaryOperator, UsesLowBound(_expressionBinaryOperator) ? dateTime.Start : dateTime.End);
    }

    public void Visit(NumberSearchValue number)
    {
        EnsureArg.IsNotNull(number, nameof(number));
        AddComparison(_expressionBinaryOperator, UsesLowBound(_expressionBinaryOperator) ? number.Low : number.High);
    }

    public void Visit(QuantitySearchValue quantity)
    {
        EnsureArg.IsNotNull(quantity, nameof(quantity));
        AddComparison(_expressionBinaryOperator, UsesLowBound(_expressionBinaryOperator) ? quantity.Low : quantity.High);
    }

    /// <summary>
    /// Lt/Le/Sa read the stored range's low/start bound; Gt/Ge/Eb read the high/end bound. Eq/Ne
    /// are unreachable here for Number/Quantity/DateTime (they always arrive pre-expanded into a
    /// Ge/Le or Lt/Gt pair upstream in SearchValueExpressionBuilderHelper) - defaulting them to the
    /// high bound matches this visitor's pre-existing behavior for that unreachable case.
    /// </summary>
    private static bool UsesLowBound(BinaryOperator op) => op is BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or BinaryOperator.StartsAfter;
```

Then update `AddComparison`'s switch, replacing:

```csharp
            case BinaryOperator.GreaterThanOrEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) >= 0));
                break;
            case BinaryOperator.LessThanOrEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) <= 0));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binaryOperator));
```

with:

```csharp
            case BinaryOperator.GreaterThanOrEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) >= 0));
                break;
            case BinaryOperator.LessThanOrEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) <= 0));
                break;
            case BinaryOperator.StartsAfter:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) > 0));
                break;
            case BinaryOperator.EndsBefore:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) < 0));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binaryOperator));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchQueryInterpreterComparisonTests"`
Expected: PASS, all cases.

- [ ] **Step 5: Run the full Application test suite to check for regressions**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj`
Expected: no new failures. Pay particular attention to any existing test that exercises
String/Token/Reference/Uri comparisons through `ComparisonValueVisitor` (via `AddComparison`'s
unchanged `Equal`/`GreaterThan`/`LessThan`/`NotEqual`/`GreaterThanOrEqual`/`LessThanOrEqual` arms) -
those arms and their call sites (`Visit(StringSearchValue)`, `Visit(TokenSearchValue)`, etc.) were
not touched, so should be unaffected, but confirm.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search/InMemory/ComparisonValueVisitor.cs test/Ignixa.Application.Tests/Search/SearchQueryInterpreterComparisonTests.cs
git commit -m "fix(search): fix InMemory backend's Number/Quantity/DateTime bound selection"
```

---

### Task 8: End-to-end regression coverage and final verification

**Files:**
- Modify: `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md` (close out the
  comparator-semantics prerequisite)

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: no new production code - this task is verification and documentation closure only.

- [ ] **Step 1: Confirm Bug 1's `ne` formula is unaffected**

`SearchParameterQueryGeneratorQuantityAndTests.cs` already has
`GivenUnitQualifiedNeQuantitySearch_WhenGeneratingQuery_ThenMatchingValueIsExcluded` (from Bug 1's
original fix) - this is the regression guard for `GenerateQuantityAndQueryAsync`'s `ne` handling,
which is a direct inline predicate (`HighValue < firstValue || LowValue > secondValue`), not a
`ComparisonPredicates` delegate call. No new test needed: run it explicitly by itself first to
confirm in isolation before the full-suite run in Step 2.

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~GivenUnitQualifiedNeQuantitySearch_WhenGeneratingQuery_ThenMatchingValueIsExcluded"`
Expected: PASS - full-disjointness doesn't depend on the `gt`/`ge`/`lt`/`le` directional convention
Task 2 changed, so this should be unaffected.

- [ ] **Step 2: Run the full DataLayer test suite**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj`
Expected: same 5 pre-existing failures (documented in Global Constraints), everything else green.

- [ ] **Step 3: Run the full Application test suite**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj`
Expected: all green.

- [ ] **Step 4: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Close out the prerequisite in the plan's Post-Plan section**

Open `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md`, find the "Findings
from Phase 2 prerequisite investigation" section's finding #1 (the comparator-semantics divergence
matrix). Add a note directly after it (matching this file's established style for marking findings
resolved, e.g. the `TokenCodeStorage` fix's annotation earlier in the same document):

```markdown
   **Resolved**: canonicalized on the ms-fhir-server binding across all three implementations, and
   gave `sa`/`eb` real `BinaryOperator` values (previously aliased to `gt`/`lt`, and on the
   InMemory backend for DateTime, indistinguishable from them). See
   `docs/superpowers/specs/2026-07-11-comparator-semantics-design.md` for the full design and
   `docs/superpowers/plans/2026-07-11-comparator-semantics-canonicalization.md` for the
   implementation. This was a live search-behavior change for `gt`/`ge`/`lt`/`le` on any stored
   value with `Low != High` (implicit-precision values) - no data migration needed, call out in
   release notes.
```

- [ ] **Step 6: Commit**

```bash
git add docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md
git commit -m "docs(sql): close out comparator-semantics prerequisite for Phase 2"
```

- [ ] **Step 7: Push**

```bash
git push
```

Prerequisite #1 for Phase 2 (composite semantic leaf) is now resolved. Phase 2 design can proceed.
