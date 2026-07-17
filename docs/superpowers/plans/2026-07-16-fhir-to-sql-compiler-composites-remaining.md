# Composite Lowering: TokenString, TokenQuantity, TokenDateTime, ReferenceToken Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish all 6 composite search-parameter types by lowering the remaining 4 (`TokenString`, `TokenQuantity`, `TokenDateTime`, `ReferenceToken`) to `ParamSource` CTEs, reusing the range-comparison infrastructure already built for the base leaf types and the `CompositeLoweringDispatcher`/`Lower` wiring the prior composites increment (`2026-07-16-fhir-to-sql-compiler-composites-token.md`) already landed.

**Architecture:** All 4 remaining composite tables have the same two-slot shape the prior increment's `TokenToken`/`TokenNumberNumber` rules already established: a Token slot (`Code1`, `SystemId1`) plus one other-typed slot, except `ReferenceToken` (Reference + Token, in either component order — see below). Real DDL confirmed directly from `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql`:

- `TokenStringCompositeSearchParam`: `SystemId1`/`Code1` (Token) + `Text2 NVARCHAR(256) COLLATE Latin1_General_CI_AI NOT NULL` + `TextOverflow2` (String). Collation is `Latin1_General_CI_AI` — **not** the base `StringSearchParam.Text`'s `Latin1_General_100_CI_AI` (confirmed by reading `StringLoweringRule.cs`'s hardcoded constant), a real, DDL-confirmed divergence.
- `TokenQuantityCompositeSearchParam`: `SystemId1`/`Code1` (Token) + `SystemId2`/`QuantityCodeId2`/`SingleValue2`/`LowValue2`/`HighValue2` (Quantity, all nullable — unlike base `QuantitySearchParam`'s `NOT NULL` `LowValue`/`HighValue`, an already-known divergence).
- `TokenDateTimeCompositeSearchParam`: `SystemId1`/`Code1` (Token) + `StartDateTime2`/`EndDateTime2 DATETIME2(7) NOT NULL` + `IsLongerThanADay2` (DateTime) — same shape as base `DateTimeSearchParam`, just numbered columns.
- `ReferenceTokenCompositeSearchParam`: `BaseUri1`/`ReferenceResourceTypeId1`/`ReferenceResourceId1`/`ReferenceResourceVersion1` (Reference) + `SystemId2`/`Code2`/`CodeOverflow2` (Token).

**Two real simplifications found while designing this plan, not assumed:**

1. **Composite components never carry a `SearchModifier`.** `SearchExpressionBinder.BindComposite` (`:186-236`, already read in the prior increment's research) calls `BindAtomic(effective, modifier: null, index, componentSyntax)` — `modifier: null`, unconditionally, for every component. `StringLoweringRule`'s `:exact`/`:contains` branches (and their overflow-safety throw guards) are therefore dead code for a composite's String component: only the default/no-modifier `StartsWith` case is ever reachable, which `StringLoweringRule`'s own doc comment already establishes is safe against the inline column alone in both the overflowed and non-overflowed case (no throw guard needed). `TokenStringLoweringRule` implements exactly that one case — not a re-derivation of `StringLoweringRule`'s full modifier-branching logic.
2. **`ReferenceToken`'s Reference/Token roles must be resolved by component *value type*, not by array position.** Confirmed both by this roadmap's own already-resolved note (`2026-07-15-fhir-to-sql-compiler-roadmap.md`, "`ReferenceToken` ordinal ownership") and by reading the write path directly: `RefTokenCompositeRowGenerator.cs:74-83` finds its Reference/Token components via `component.OfType<ReferenceSearchValue>()`/`OfType<TokenSearchValue>()`, with the comment "Find Reference and Token components by type, not by position -- This handles cases where component definitions have swapped expressions" (e.g. `DocumentReference.relationship`). `ReferenceTokenLoweringRule` mirrors this: it searches its `components` list by `is ReferenceSearchValue`/`is TokenSearchValue`, never assumes `components[0]` is the Reference slot. `CompositeLoweringDispatcher` gets **two** arms for this type (`[ReferenceSearchValue, TokenSearchValue]` and `[TokenSearchValue, ReferenceSearchValue]`), both routing to the same order-agnostic rule.

**Two shared helpers get extracted first** (Tasks 1-2), closing a Minor finding from the prior increment's final review (`TokenColumnEquals`'s ~25-line guard logic was already duplicated verbatim between `TokenTokenLoweringRule`/`TokenNumberNumberLoweringRule`; a third Token-slot composite type was flagged as the point to stop duplicating it — this plan adds three more):

- `TokenColumnEquality.Build(TableDescriptor, string codeColumn, TokenSearchValue, LeafContext): Predicate` — the code-only, throw-on-System/throw-on-empty-Code logic, extracted unchanged from `TokenTokenLoweringRule`/`TokenNumberNumberLoweringRule`, both refactored to call it. All 4 new composite types have a Token slot and reuse it too.
- `DateTimeRangeComparison.Build(LeafContext, SqlColumnRef startColumn, SqlColumnRef endColumn, SearchComparator, DateTimeSearchValue): Predicate` — extracted unchanged from `DateTimeLoweringRule`, which is refactored to call it, mirroring the exact pattern `NumericRangeComparison` already established for Number/Quantity in an earlier increment. `TokenDateTimeLoweringRule` reuses it against `StartDateTime2`/`EndDateTime2`.

**Tech Stack:** C#/.NET 9, xUnit + Shouldly, existing `Ignixa.Search.Sql`/`Ignixa.Search.Sql.Tests` projects (no new projects, no `SqlCatalog`/generator changes -- all 4 remaining tables are already covered by the existing generator's DDL-wide `*SearchParam` filter).

## Global Constraints

- `System`-qualified token slots throw `NotSupportedException`, matching `TokenLoweringRule`'s existing precedent exactly (same underlying gap: `ISymbolResolver` has no `SystemId` resolution yet) -- now centralized in `TokenColumnEquality`.
- `TokenQuantityLoweringRule`'s Quantity slot: value comparison only, throws for non-empty `System`/`Code`, matching `QuantityLoweringRule`'s existing precedent (same gap: no `SystemId`/`QuantityCodeId` resolver yet).
- `:ap` on the DateTime/Quantity slots throws, same as `DateTimeLoweringRule`/`NumberLoweringRule` (via the reused range-comparison helpers).
- `TokenStringLoweringRule`'s String slot implements only the default (no-modifier) `StartsWith` case -- composite components never carry a modifier (see Architecture), so `:exact`/`:contains` are unreachable, not merely unimplemented.
- `ReferenceTokenLoweringRule`'s Reference slot: absolute/external references (non-null `BaseUri`) throw, matching `ReferenceLoweringRule`'s existing precedent exactly.
- All 4 remaining composite types are the full remainder of the six-type family -- after this plan, `CompositeLoweringDispatcher`'s default arm becomes unreachable for any *documented* composite shape; it still exists as a defensive throw for any future/unknown component-type combination.
- `SqlCatalog.Default.Table("TokenStringCompositeSearchParam")` / `"TokenQuantityCompositeSearchParam"` / `"TokenDateTimeCompositeSearchParam"` / `"ReferenceTokenCompositeSearchParam"` already exist (generated from `97.sql`, already covered by `SqlCatalogTests.cs`'s existing facts, including regression tests for the `Text2` collation and nullable `LowValue2`/`HighValue2` divergences).
- No `Predicate`/`Emit`/`PlanExplainer` changes -- every new rule's `Predicate` tree is built entirely from existing `Predicate.Equal`/`Like`/`And`/`LessThan`/etc. cases.
- **Known, independently-tracked, out-of-scope bug** (do not fix as part of this plan, do not let it block anything here): the composite row *generators* (`TokenStringCompositeRowGenerator.cs`, `TokenQuantityCompositeRowGenerator.cs`, `RefTokenCompositeRowGenerator.cs`, etc. -- the *write* path) hardcode the wrong overflow-split width (128, not the DDL's real 256) for their `CodeOverflow`/text columns. Confirmed again while reading `RefTokenCompositeRowGenerator.cs:131` for this plan (`tokenComponent.Code.Length > 128`). This is a write-path data-correctness bug, already tracked in the roadmap's "Independent items worth their own tickets" list (item 4) -- it does not affect the correctness of the *read*-path lowering rules this plan builds (an `Equal`/`Like` comparison against `Code1`/`Code2` is correct regardless of how that column was populated; a value written incorrectly is a data-quality problem this compiler cannot detect or correct at query time).
- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing (the `Ignixa.SqlOnFhir.Tests` submodule failures are pre-existing and out of scope, per every prior increment on this branch).

---

### Task 1: Extract `TokenColumnEquality`, refactor `TokenTokenLoweringRule`/`TokenNumberNumberLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenColumnEquality.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenTokenLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenNumberNumberLoweringRule.cs`

**Interfaces:**
- Consumes: `LeafContext`, `TokenSearchValue`, `TableDescriptor` (`Ignixa.Search.Sql.Catalog`).
- Produces: `TokenColumnEquality.Build(TableDescriptor table, string codeColumn, TokenSearchValue value, LeafContext context): Predicate`. Every remaining task in this plan calls it for its Token slot.

- [ ] **Step 1: Create the shared helper**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenColumnEquality.cs
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the code-only equality predicate shared by every composite type's Token slot (TokenToken,
/// TokenNumberNumber, TokenString, TokenQuantity, TokenDateTime, ReferenceToken all have one) --
/// identical semantics to TokenLoweringRule: System-qualified components (including System =
/// string.Empty) and text-only components (no Code) both throw rather than silently producing a
/// wrong-scope or always-false predicate.
/// </summary>
internal static class TokenColumnEquality
{
    public static Predicate Build(TableDescriptor table, string codeColumn, TokenSearchValue value, LeafContext context)
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

- [ ] **Step 2: Refactor `TokenTokenLoweringRule` to use it**

Replace the entire contents of `src/Core/Ignixa.Search.Sql/Lowering/TokenTokenLoweringRule.cs` with:

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
/// components[0] compares Code1, components[1] compares Code2, both via TokenColumnEquality.
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
            TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context),
            TokenColumnEquality.Build(table, "Code2", (TokenSearchValue)components[1].Value, context));

        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }
}
```

- [ ] **Step 3: Refactor `TokenNumberNumberLoweringRule` to use it**

In `src/Core/Ignixa.Search.Sql/Lowering/TokenNumberNumberLoweringRule.cs`, delete the private `TokenColumnEquals` method and replace its call site:

```csharp
// Replace:
//     var tokenPredicate = TokenColumnEquals(table, (TokenSearchValue)components[0].Value, context);
// With:
        var tokenPredicate = TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context);
```

Delete the entire `private static Predicate TokenColumnEquals(TableDescriptor table, TokenSearchValue value, LeafContext context) { ... }` method -- its body is now `TokenColumnEquality.Build`.

- [ ] **Step 4: Run the existing tests to confirm the refactor is behavior-preserving**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenTokenLoweringRuleTests|FullyQualifiedName~TokenNumberNumberLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all previously-passing tests still pass unchanged -- this step is a pure refactor, no test file changes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(search-sql): extract TokenColumnEquality, dedupe TokenToken/TokenNumberNumber

Closes a Minor finding from the prior composites increment's final review:
the code-only token-column guard logic was duplicated verbatim between
TokenTokenLoweringRule and TokenNumberNumberLoweringRule. Every remaining
composite type in this plan has a Token slot too, so this now serves six
call sites instead of accumulating a third/fourth/fifth copy."
```

---

### Task 2: Extract `DateTimeRangeComparison`, refactor `DateTimeLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/DateTimeRangeComparison.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/DateTimeLoweringRule.cs`

**Interfaces:**
- Consumes: `LeafContext`, `DateTimeSearchValue`, `SqlColumnRef`, `SearchComparator`.
- Produces: `DateTimeRangeComparison.Build(LeafContext context, SqlColumnRef startColumn, SqlColumnRef endColumn, SearchComparator comparator, DateTimeSearchValue value): Predicate`. Task 5 (`TokenDateTimeLoweringRule`) calls it.

- [ ] **Step 1: Create the shared helper**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/DateTimeRangeComparison.cs
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Builds the comparator-dependent predicate shared by DateTime leaf lowering (base and composite --
/// both store [StartDateTime, EndDateTime] with identical range-overlap semantics against the search
/// value's own [Start, End], which already encodes FHIR partial-date precision by construction).
/// Transcribed once from SearchValueExpressionBuilderHelper.Visit(DateTimeSearchValue), the real,
/// live-executed comparator branch. :ap throws -- it requires DateTimeOffset.UtcNow at lowering time,
/// which this pure function doesn't have.
/// </summary>
internal static class DateTimeRangeComparison
{
    public static Predicate Build(LeafContext context, SqlColumnRef startColumn, SqlColumnRef endColumn, SearchComparator comparator, DateTimeSearchValue value) => comparator switch
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
        _ => throw new NotSupportedException($"Unknown SearchComparator '{comparator}'."),
    };
}
```

- [ ] **Step 2: Refactor `DateTimeLoweringRule` to use it**

Replace the entire contents of `src/Core/Ignixa.Search.Sql/Lowering/DateTimeLoweringRule.cs` with:

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/DateTimeLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a DateTime search value to a ParamSource over DateTimeSearchParam, via DateTimeRangeComparison.
/// </summary>
public static class DateTimeLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, DateTimeSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime");
        var predicateExpr = DateTimeRangeComparison.Build(context, startColumn, endColumn, predicate.Comparator, value);

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
```

- [ ] **Step 3: Run the existing tests to confirm the refactor is behavior-preserving**

```bash
dotnet test All.sln --filter "FullyQualifiedName~DateTimeLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors -- all 9 comparator cases (`Eq,Ne,Lt,Gt,Le,Ge,Sa,Eb,Ap`) still pass unchanged; this step is a pure refactor, no test file changes.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(search-sql): extract DateTimeRangeComparison from DateTimeLoweringRule

Mirrors the NumericRangeComparison extraction pattern from an earlier
increment -- lets TokenDateTimeLoweringRule reuse the same 9-comparator
range-overlap logic against composite-table column names without
duplicating it."
```

---

### Task 3: `TokenStringLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenStringLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenStringLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `TokenColumnEquality.Build` (Task 1), `LeafContext`, `TokenSearchValue`, `StringSearchValue`.
- Produces: `TokenStringLoweringRule.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<SearchParameterPredicateExpression> components, LeafContext context): CteDefinition.ParamSource`. `components[0]` is the token slot (→ `Code1`), `components[1]` is the string slot (→ `Text2`/`TextOverflow2`).

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/TokenStringLoweringRuleTests.cs
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

public class TokenStringLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("code-value-string", "code-value-string", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-string"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndAShortStringComponent_WhenLowered_ThenComparesCode1AndText2WithStartsWith()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var stringParam = ComponentParameter("value-string");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(stringParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated")),
        };

        // Act
        var cte = TokenStringLoweringRule.Lower(composite, components, ContextResolving(composite, 401));

        // Assert
        cte.SearchParamId.ShouldBe((short)401);
        cte.Table.TableName.ShouldBe("TokenStringCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var stringPredicate = and.Right.ShouldBeOfType<Predicate.Like>();
        stringPredicate.Column.Column.ShouldBe("Text2");
        stringPredicate.Match.ShouldBe(LikeMatch.StartsWith);
        stringPredicate.Collation.ShouldBe("Latin1_General_CI_AI");
        stringPredicate.Value.Value.ShouldBe("Elevated");
    }

    [Fact]
    public void GivenAStringComponentLongerThanTheInlineWidth_WhenLowered_ThenComparesTextOverflow2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var stringParam = ComponentParameter("value-string");
        var longValue = new string('x', 300);
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(stringParam, SearchComparator.Eq, modifier: null, new StringSearchValue(longValue)),
        };

        // Act
        var cte = TokenStringLoweringRule.Lower(composite, components, ContextResolving(composite, 401));

        // Assert
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var stringPredicate = and.Right.ShouldBeOfType<Predicate.Like>();
        stringPredicate.Column.Column.ShouldBe("TextOverflow2");
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var stringParam = ComponentParameter("value-string");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(stringParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated")),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenStringLoweringRule.Lower(composite, components, ContextResolving(composite, 401)));
    }
}
```

Verify `StringSearchValue`'s real constructor (single-string-argument form, as used by `StringLoweringRuleTests.cs`/`EndToEndCompilationTests.cs`) before running -- correct the test construction if it disagrees.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenStringLoweringRuleTests" --nologo
```

Expected: FAIL with "TokenStringLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `TokenStringLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenStringLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenString composite to a single ParamSource over TokenStringCompositeSearchParam --
/// components[0] is the token slot (Code1, via TokenColumnEquality), components[1] is the string slot
/// (Text2/TextOverflow2). Composite components never carry a SearchModifier
/// (SearchExpressionBinder.BindComposite always passes modifier: null per component -- confirmed by
/// reading its source), so unlike StringLoweringRule this rule has exactly one case: the default/
/// no-modifier StartsWith semantics, which per StringLoweringRule's own doc comment is safe against
/// the inline column alone in both the overflowed and non-overflowed case -- no throw guard needed.
/// The collation is TokenStringCompositeSearchParam's own (Latin1_General_CI_AI), NOT
/// StringSearchParam.Text's (Latin1_General_100_CI_AI) -- a real, DDL-confirmed divergence.
/// </summary>
public static class TokenStringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_CI_AI";

    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenStringCompositeSearchParam");
        var tokenPredicate = TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context);
        var stringPredicate = StringColumnStartsWith(table, (StringSearchValue)components[1].Value, context);

        var predicate = new Predicate.And(tokenPredicate, stringPredicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate StringColumnStartsWith(TableDescriptor table, StringSearchValue value, LeafContext context)
    {
        var inlineWidth = table.Column("Text2").MaxLength
            ?? throw new InvalidOperationException("TokenStringCompositeSearchParam.Text2 has no MaxLength in SqlCatalog.");

        var usesTextColumn = value.String.Length <= inlineWidth;
        var column = new SqlColumnRef(table.TableName, usesTextColumn ? "Text2" : "TextOverflow2");
        return new Predicate.Like(column, context.Parameter(value.String), LikeMatch.StartsWith, CaseInsensitiveCollation);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenStringLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add TokenStringLoweringRule

Composite components never carry a SearchModifier, so unlike
StringLoweringRule this only implements the default/no-modifier StartsWith
case -- the one case that's safe against Text2 alone in both the
overflowed and non-overflowed case. Uses the composite table's own
Latin1_General_CI_AI collation, distinct from StringSearchParam.Text's."
```

---

### Task 4: `TokenQuantityLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenQuantityLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenQuantityLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `TokenColumnEquality.Build` (Task 1), `NumericRangeComparison.Build` (existing), `LeafContext`, `TokenSearchValue`, `QuantitySearchValue`.
- Produces: `TokenQuantityLoweringRule.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<SearchParameterPredicateExpression> components, LeafContext context): CteDefinition.ParamSource`. `components[0]` is the token slot (→ `Code1`), `components[1]` is the quantity slot (→ `LowValue2`/`HighValue2`).

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/TokenQuantityLoweringRuleTests.cs
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

public class TokenQuantityLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://hl7.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndAnUnqualifiedQuantityComponent_WhenLowered_ThenComparesCode1AndLowHighValue2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m)),
        };

        // Act
        var cte = TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402));

        // Assert
        cte.SearchParamId.ShouldBe((short)402);
        cte.Table.TableName.ShouldBe("TokenQuantityCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var quantityPredicate = and.Right.ShouldBeOfType<Predicate.And>();
        quantityPredicate.Left.ShouldBeOfType<Predicate.GreaterThanOrEqual>().Column.Column.ShouldBe("LowValue2");
        quantityPredicate.Right.ShouldBeOfType<Predicate.LessThanOrEqual>().Column.Column.ShouldBe("HighValue2");
    }

    [Fact]
    public void GivenASystemQualifiedQuantityComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("component-code");
        var quantityParam = ComponentParameter("component-value-quantity");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(quantityParam, SearchComparator.Eq, modifier: null, new QuantitySearchValue("http://unitsofmeasure.org", "mg", 120m)),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenQuantityLoweringRule.Lower(composite, components, ContextResolving(composite, 402)));
    }
}
```

Verify `QuantitySearchValue`'s real constructor parameter names/order (`system`, `code`, `value` -- as used by `QuantityLoweringRuleTests.cs`) against source before running.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenQuantityLoweringRuleTests" --nologo
```

Expected: FAIL with "TokenQuantityLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `TokenQuantityLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenQuantityLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenQuantity composite to a single ParamSource over TokenQuantityCompositeSearchParam --
/// components[0] is the token slot (Code1), components[1] is the quantity slot (LowValue2/HighValue2,
/// value comparison only -- System2/QuantityCodeId2 need SystemId/QuantityCodeId resolution, the same
/// gap QuantityLoweringRule already defers). LowValue2/HighValue2 are nullable in this composite table
/// (unlike the base QuantitySearchParam's NOT NULL columns), which needs no special handling here: SQL
/// NULL comparison semantics already exclude a non-matching row correctly.
/// </summary>
public static class TokenQuantityLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenQuantityCompositeSearchParam");

        var tokenPredicate = TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context);
        var quantityPredicate = QuantityRangePredicate(table, components[1], context);

        var predicate = new Predicate.And(tokenPredicate, quantityPredicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate QuantityRangePredicate(TableDescriptor table, SearchParameterPredicateExpression component, LeafContext context)
    {
        var value = (QuantitySearchValue)component.Value;
        if (!string.IsNullOrEmpty(value.System) || !string.IsNullOrEmpty(value.Code))
        {
            throw new NotSupportedException(
                "Quantity search with System or Code is not supported yet -- this rule only implements the value comparison. " +
                "SystemId/QuantityCodeId resolution needs a new ISymbolResolver method, not built yet.");
        }

        var comparisonValue = value.Low ?? value.High
            ?? throw new NotSupportedException("QuantitySearchValue has neither Low nor High set.");
        var lowColumn = new SqlColumnRef(table.TableName, "LowValue2");
        var highColumn = new SqlColumnRef(table.TableName, "HighValue2");
        return NumericRangeComparison.Build(context, lowColumn, highColumn, component.Comparator, comparisonValue);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenQuantityLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add TokenQuantityLoweringRule (value comparison only)

Reuses NumericRangeComparison unchanged, same as the base QuantityLoweringRule.
System/Code throw NotSupportedException rather than silently matching
without those constraints, matching QuantityLoweringRule's existing gap."
```

---

### Task 5: `TokenDateTimeLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenDateTimeLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenDateTimeLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `TokenColumnEquality.Build` (Task 1), `DateTimeRangeComparison.Build` (Task 2), `LeafContext`, `TokenSearchValue`, `DateTimeSearchValue`.
- Produces: `TokenDateTimeLoweringRule.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<SearchParameterPredicateExpression> components, LeafContext context): CteDefinition.ParamSource`. `components[0]` is the token slot (→ `Code1`), `components[1]` is the datetime slot (→ `StartDateTime2`/`EndDateTime2`).

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/TokenDateTimeLoweringRuleTests.cs
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

public class TokenDateTimeLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    private static SearchParameterInfo CompositeParameter()
        => new("code-value-date", "code-value-date", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-date"));

    private static SearchParameterInfo ComponentParameter(string code)
        => new(code, code, SearchParamType.Token, new Uri($"http://example.org/fhir/SearchParameter/Observation-{code}"));

    [Fact]
    public void GivenATokenComponentAndADateTimeComponent_WhenLowered_ThenComparesCode1AndStartEndDateTime2()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var dateParam = ComponentParameter("value-date");
        var dateValue = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null)),
            new(dateParam, SearchComparator.Ge, modifier: null, dateValue),
        };

        // Act
        var cte = TokenDateTimeLoweringRule.Lower(composite, components, ContextResolving(composite, 403));

        // Assert
        cte.SearchParamId.ShouldBe((short)403);
        cte.Table.TableName.ShouldBe("TokenDateTimeCompositeSearchParam");
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var tokenPredicate = and.Left.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code1");
        var datePredicate = and.Right.ShouldBeOfType<Predicate.GreaterThanOrEqual>();
        datePredicate.Column.Column.ShouldBe("EndDateTime2");
        datePredicate.Value.Value.ShouldBe(dateValue.Start);
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var tokenParam = ComponentParameter("code");
        var dateParam = ComponentParameter("value-date");
        var components = new SearchParameterPredicateExpression[]
        {
            new(tokenParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://loinc.org", code: "8480-6", text: null)),
            new(dateParam, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero))),
        };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenDateTimeLoweringRule.Lower(composite, components, ContextResolving(composite, 403)));
    }
}
```

Verify `DateTimeSearchValue`'s real constructor (single-`DateTimeOffset`-argument form, as used by `DateTimeLoweringRuleTests.cs`/`EndToEndCompilationTests.cs`) against source before running.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenDateTimeLoweringRuleTests" --nologo
```

Expected: FAIL with "TokenDateTimeLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `TokenDateTimeLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenDateTimeLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a TokenDateTime composite to a single ParamSource over TokenDateTimeCompositeSearchParam --
/// components[0] is the token slot (Code1), components[1] is the datetime slot (StartDateTime2/
/// EndDateTime2), reusing DateTimeRangeComparison unchanged -- identical range semantics to
/// DateTimeLoweringRule, just against composite-table column names.
/// </summary>
public static class TokenDateTimeLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("TokenDateTimeCompositeSearchParam");

        var tokenPredicate = TokenColumnEquality.Build(table, "Code1", (TokenSearchValue)components[0].Value, context);

        var dateComponent = components[1];
        var dateValue = (DateTimeSearchValue)dateComponent.Value;
        var startColumn = new SqlColumnRef(table.TableName, "StartDateTime2");
        var endColumn = new SqlColumnRef(table.TableName, "EndDateTime2");
        var datePredicate = DateTimeRangeComparison.Build(context, startColumn, endColumn, dateComponent.Comparator, dateValue);

        var predicate = new Predicate.And(tokenPredicate, datePredicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenDateTimeLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add TokenDateTimeLoweringRule

Reuses DateTimeRangeComparison from task 2 unchanged -- identical range
semantics to DateTimeLoweringRule, applied against StartDateTime2/EndDateTime2."
```

---

### Task 6: `ReferenceTokenLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/ReferenceTokenLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ReferenceTokenLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `TokenColumnEquality.Build` (Task 1), `LeafContext`, `ReferenceSearchValue`, `TokenSearchValue`.
- Produces: `ReferenceTokenLoweringRule.Lower(SearchParameterInfo compositeParameter, IReadOnlyList<SearchParameterPredicateExpression> components, LeafContext context): CteDefinition.ParamSource`. **Order-agnostic**: finds the Reference-typed and Token-typed components by value type, not by index -- the caller (Task 7's dispatcher) may hand `components` in either order.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/ReferenceTokenLoweringRuleTests.cs
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

public class ReferenceTokenLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo compositeParameter, short searchParamId, string resourceType, short resourceTypeId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [compositeParameter.Url!.ToString()] = searchParamId },
            new Dictionary<string, short> { [resourceType] = resourceTypeId }));

    private static SearchParameterInfo CompositeParameter()
        => new("relatesto", "relatesto", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/DocumentReference-relatesto"));

    private static SearchParameterInfo ComponentParameter(string code, SearchParamType type)
        => new(code, code, type, new Uri($"http://example.org/fhir/SearchParameter/DocumentReference-{code}"));

    private static SearchParameterPredicateExpression ReferenceComponent(string code)
        => new(ComponentParameter(code, SearchParamType.Reference), SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456"));

    private static SearchParameterPredicateExpression TokenComponent(string code, string? system = null)
        => new(ComponentParameter(code, SearchParamType.Token), SearchComparator.Eq, modifier: null,
            new TokenSearchValue(system, code: "replaces", text: null));

    [Fact]
    public void GivenAReferenceComponentThenATokenComponent_WhenLowered_ThenComparesReferenceIdAndCode2()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[] { ReferenceComponent("target"), TokenComponent("code") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55));

        // Assert
        cte.SearchParamId.ShouldBe((short)404);
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var referencePredicate = outer.Left.ShouldBeOfType<Predicate.And>();
        referencePredicate.Left.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceTypeId1");
        referencePredicate.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceId1");
        var tokenPredicate = outer.Right.ShouldBeOfType<Predicate.Equal>();
        tokenPredicate.Column.Column.ShouldBe("Code2");
    }

    [Fact]
    public void GivenATokenComponentThenAReferenceComponent_WhenLowered_ThenStillFindsRolesByType()
    {
        // Arrange -- swapped order proves role assignment is type-based, not positional
        // (mirrors RefTokenCompositeRowGenerator's own "find by type, not position" handling of
        // definitions like DocumentReference.relationship that swap component expressions).
        var composite = CompositeParameter();
        var components = new[] { TokenComponent("code"), ReferenceComponent("target") };

        // Act
        var cte = ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55));

        // Assert -- identical shape to the non-swapped case
        var outer = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var referencePredicate = outer.Left.ShouldBeOfType<Predicate.And>();
        referencePredicate.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("ReferenceResourceId1");
        outer.Right.ShouldBeOfType<Predicate.Equal>().Column.Column.ShouldBe("Code2");
    }

    [Fact]
    public void GivenAnAbsoluteReference_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var referenceParam = ComponentParameter("target", SearchParamType.Reference);
        var absoluteReference = new SearchParameterPredicateExpression(
            referenceParam, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.External, baseUri: new Uri("http://example.org/fhir"), resourceType: "DocumentReference", resourceId: "456"));
        var components = new[] { absoluteReference, TokenComponent("code") };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55)));
    }

    [Fact]
    public void GivenASystemQualifiedTokenComponent_WhenLowered_ThenThrows()
    {
        // Arrange
        var composite = CompositeParameter();
        var components = new[] { ReferenceComponent("target"), TokenComponent("code", system: "http://example.org/relationship-type") };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            ReferenceTokenLoweringRule.Lower(composite, components, ContextResolving(composite, 404, "DocumentReference", 55)));
    }
}
```

Verify `ReferenceSearchValue`'s real constructor (`ReferenceKind`, `baseUri`, `resourceType`, `resourceId` -- as used by `ResolveTests.cs`) against source before running, and confirm `ReferenceKind.Internal`/`ReferenceKind.External` are the real enum member names.

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ReferenceTokenLoweringRuleTests" --nologo
```

Expected: FAIL with "ReferenceTokenLoweringRule does not exist" (compile error).

- [ ] **Step 3: Implement `ReferenceTokenLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/ReferenceTokenLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a ReferenceToken composite to a single ParamSource over ReferenceTokenCompositeSearchParam.
/// Finds the Reference and Token components by their runtime ISearchValue type, not by array index --
/// some component definitions swap expressions (e.g. DocumentReference's relationship composite), so
/// the write path (RefTokenCompositeRowGenerator.cs) already resolves roles this way too. Mirrors
/// ReferenceLoweringRule's BaseUri throw and typed/untyped ResourceTypeId/ResourceId logic, and
/// TokenColumnEquality for the token slot.
/// </summary>
public static class ReferenceTokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(
        SearchParameterInfo compositeParameter,
        IReadOnlyList<SearchParameterPredicateExpression> components,
        LeafContext context)
    {
        var referenceComponent = components.FirstOrDefault(c => c.Value is ReferenceSearchValue)
            ?? throw new NotSupportedException($"ReferenceToken composite '{compositeParameter.Code}' has no Reference-typed component.");
        var tokenComponent = components.FirstOrDefault(c => c.Value is TokenSearchValue)
            ?? throw new NotSupportedException($"ReferenceToken composite '{compositeParameter.Code}' has no Token-typed component.");

        var table = SqlCatalog.Default.Table("ReferenceTokenCompositeSearchParam");

        var referencePredicate = ReferenceColumnEquality((ReferenceSearchValue)referenceComponent.Value, table, context);
        var tokenPredicate = TokenColumnEquality.Build(table, "Code2", (TokenSearchValue)tokenComponent.Value, context);

        var predicate = new Predicate.And(referencePredicate, tokenPredicate);
        return new CteDefinition.ParamSource(table, context.SearchParamId(compositeParameter), predicate);
    }

    private static Predicate ReferenceColumnEquality(ReferenceSearchValue value, TableDescriptor table, LeafContext context)
    {
        if (value.BaseUri is not null)
        {
            throw new NotSupportedException(
                $"Absolute/external reference search (BaseUri '{value.BaseUri}') is not supported by ReferenceTokenLoweringRule, " +
                "matching ReferenceLoweringRule's own scope note.");
        }

        var idPredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "ReferenceResourceId1"), context.Parameter(value.ResourceId));

        return string.IsNullOrEmpty(value.ResourceType)
            ? idPredicate
            : new Predicate.And(
                new Predicate.Equal(
                    new SqlColumnRef(table.TableName, "ReferenceResourceTypeId1"),
                    context.Parameter(context.ResourceTypeId(value.ResourceType))),
                idPredicate);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ReferenceTokenLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass -- including the swapped-order test proving type-based (not positional) role assignment.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add ReferenceTokenLoweringRule (type-based, not positional, role assignment)

Finds the Reference/Token components by ISearchValue type rather than
array index, matching RefTokenCompositeRowGenerator's own 'find by type,
not position' handling of definitions that swap component expressions
(e.g. DocumentReference.relationship). Mirrors ReferenceLoweringRule's
BaseUri throw and TokenColumnEquality for the two slots."
```

---

### Task 7: Extend `CompositeLoweringDispatcher`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs`
- Modify: `test/Ignixa.Search.Sql.Tests/Lowering/CompositeLoweringDispatcherTests.cs`

**Interfaces:**
- Consumes: `TokenStringLoweringRule.Lower`/`TokenQuantityLoweringRule.Lower`/`TokenDateTimeLoweringRule.Lower`/`ReferenceTokenLoweringRule.Lower` (Tasks 3-6).
- Produces: no signature change to `CompositeLoweringDispatcher.Lower` -- only new switch arms.

- [ ] **Step 1: Write the new failing tests**

Add these test methods to the existing `test/Ignixa.Search.Sql.Tests/Lowering/CompositeLoweringDispatcherTests.cs` (alongside its existing helpers -- add a `StringComponentAt`/`DateComponentAt`/`ReferenceComponentAt` helper following the file's existing `TokenComponentAt`/`NumberComponentAt` pattern):

```csharp
    private static CompositeComponentExpression StringComponentAt(int position, string paramCode, string text)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue(text)));
    }

    private static CompositeComponentExpression DateComponentAt(int position, string paramCode, DateTimeOffset value)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Ge, modifier: null, new DateTimeSearchValue(value)));
    }

    private static CompositeComponentExpression ReferenceComponentAt(int position, string paramCode)
    {
        var parameter = ComponentParameter(paramCode);
        return new CompositeComponentExpression(
            parameter, position,
            new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null,
                new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456")));
    }

    [Fact]
    public void GivenATokenThenAStringComponent_WhenDispatched_ThenRoutesToTokenString()
    {
        // Arrange
        var composite = CompositeParameter("code-value-string");
        var components = new[] { TokenComponentAt(0, "code", "8480-6"), StringComponentAt(1, "value-string", "Elevated") };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 401));

        // Assert
        cte.Table.TableName.ShouldBe("TokenStringCompositeSearchParam");
    }

    [Fact]
    public void GivenATokenThenADateComponent_WhenDispatched_ThenRoutesToTokenDateTime()
    {
        // Arrange
        var composite = CompositeParameter("code-value-date");
        var components = new[] { TokenComponentAt(0, "code", "8480-6"), DateComponentAt(1, "value-date", new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)) };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 403));

        // Assert
        cte.Table.TableName.ShouldBe("TokenDateTimeCompositeSearchParam");
    }

    [Fact]
    public void GivenAReferenceThenATokenComponent_WhenDispatched_ThenRoutesToReferenceToken()
    {
        // Arrange
        var composite = CompositeParameter("relatesto");
        var components = new[] { ReferenceComponentAt(0, "target"), TokenComponentAt(1, "code", "replaces") };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 404));

        // Assert
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");
    }

    [Fact]
    public void GivenATokenThenAReferenceComponent_WhenDispatched_ThenStillRoutesToReferenceToken()
    {
        // Arrange -- swapped order, proving the dispatcher's second arm for this type also works
        var composite = CompositeParameter("relatesto");
        var components = new[] { TokenComponentAt(0, "code", "replaces"), ReferenceComponentAt(1, "target") };

        // Act
        var cte = CompositeLoweringDispatcher.Lower(composite, components, ContextResolving(composite, 404));

        // Assert
        cte.Table.TableName.ShouldBe("ReferenceTokenCompositeSearchParam");
    }
```

Add `using Ignixa.Search.Indexing.SearchValues;` and `using Ignixa.Specification.ValueSets.Normative;`'s `ReferenceKind` if not already imported by the file (check the existing `using` list first).

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test All.sln --filter "FullyQualifiedName~CompositeLoweringDispatcherTests" --nologo
```

Expected: FAIL -- the 4 new tests throw `NotSupportedException` from the dispatcher's still-unextended default arm (`GivenAnUnsupportedComponentTypeCombination...`-style failure), the 4 pre-existing tests still pass.

- [ ] **Step 3: Extend the dispatcher's switch**

In `src/Core/Ignixa.Search.Sql/Lowering/CompositeLoweringDispatcher.cs`, add these arms to the existing `switch` (before the `var values =>` default arm):

```csharp
        [TokenSearchValue, StringSearchValue] => TokenStringLoweringRule.Lower(compositeParameter, predicates, context),
        [TokenSearchValue, QuantitySearchValue] => TokenQuantityLoweringRule.Lower(compositeParameter, predicates, context),
        [TokenSearchValue, DateTimeSearchValue] => TokenDateTimeLoweringRule.Lower(compositeParameter, predicates, context),
        [ReferenceSearchValue, TokenSearchValue] => ReferenceTokenLoweringRule.Lower(compositeParameter, predicates, context),
        [TokenSearchValue, ReferenceSearchValue] => ReferenceTokenLoweringRule.Lower(compositeParameter, predicates, context),
```

The existing `[TokenSearchValue, TokenSearchValue]` and `[TokenSearchValue, NumberSearchValue, NumberSearchValue]` arms, and the class's XML doc comment (update "Only TokenToken and TokenNumberNumber are wired" to reflect all six types now being wired, `TokenQuantity`/`TokenString`/`TokenDateTime` are entirely covered and `ReferenceToken` is covered in both component orders), stay otherwise unchanged.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~CompositeLoweringDispatcherTests" --nologo
```

Expected: 0 warnings, 0 errors, all 8 tests (4 pre-existing + 4 new) pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): wire TokenString/TokenQuantity/TokenDateTime/ReferenceToken into CompositeLoweringDispatcher

ReferenceToken gets two arms (both component orders) since role
assignment inside ReferenceTokenLoweringRule is type-based, not
positional -- the dispatcher must route either order to the same rule."
```

---

### Task 8: Wire up end-to-end, prove all 6 composite types compile

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: no new production code -- this task is proof, not implementation.

- [ ] **Step 1: Write the new E2E tests**

Add these test methods to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, in the same style as the existing composite E2E tests (real `Resolve.RunAsync` → `Lower.Run` → `Emit.Run` pipeline, real `SearchParameterExpression`/`CompositeComponentExpression` tree shapes):

```csharp
    [Fact]
    public async Task GivenAnObservationTokenStringCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?code-value-string=8480-6$Elevated
        var compositeParam = new SearchParameterInfo(
            "code-value-string", "code-value-string", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-string"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/Observation-code"));
        var valueParam = new SearchParameterInfo("value-string", "value-string", SearchParamType.String, new Uri("http://example.org/fhir/SearchParameter/Observation-value-string"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(valueParam, 1,
                    new SearchParameterPredicateExpression(valueParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Elevated"))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 401;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenStringCompositeSearchParam[401]  Code1 = @p0 AND Text2 LIKE @p1 (StartsWith) collate CI_AI");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Sql.ShouldNotContain("Elevated");
    }

    [Fact]
    public async Task GivenAnObservationTokenQuantityCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?component-code-value-quantity=8480-6$120
        var compositeParam = new SearchParameterInfo(
            "component-code-value-quantity", "component-code-value-quantity", SearchParamType.Composite,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value-quantity"));
        var codeParam = new SearchParameterInfo("component-code", "component-code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code"));
        var quantityParam = new SearchParameterInfo("component-value-quantity", "component-value-quantity", SearchParamType.Quantity, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-value-quantity"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(quantityParam, 1,
                    new SearchParameterPredicateExpression(quantityParam, SearchComparator.Ge, modifier: null, new QuantitySearchValue(system: null!, code: null!, 120m))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 402;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert -- Ge (not Eq) so the raw value is used directly, no precision-widening bounds to compute
        plan.Explain().ShouldBe("root = TokenQuantityCompositeSearchParam[402]  Code1 = @p0 AND LowValue2 >= @p1");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Parameters.ShouldContain(p => p.Value.Equals(120m));
    }

    [Fact]
    public async Task GivenAnObservationTokenDateTimeCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Observation?code-value-date=8480-6$ge2023-01-01
        var compositeParam = new SearchParameterInfo(
            "code-value-date", "code-value-date", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/Observation-code-value-date"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/Observation-code"));
        var dateParam = new SearchParameterInfo("value-date", "value-date", SearchParamType.Date, new Uri("http://example.org/fhir/SearchParameter/Observation-value-date"));
        var dateValue = new DateTimeSearchValue(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(codeParam, 0,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "8480-6", text: null))),
                new CompositeComponentExpression(dateParam, 1,
                    new SearchParameterPredicateExpression(dateParam, SearchComparator.Ge, modifier: null, dateValue)),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 403;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe("root = TokenDateTimeCompositeSearchParam[403]  Code1 = @p0 AND EndDateTime2 >= @p1");
        emitted.Sql.ShouldNotContain("8480-6");
        emitted.Parameters.ShouldContain(p => p.Value.Equals(dateValue.Start));
    }

    [Fact]
    public async Task GivenADocumentReferenceRelatesToCompositeQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- DocumentReference?relatesto=replaces$DocumentReference/456
        var compositeParam = new SearchParameterInfo(
            "relatesto", "relatesto", SearchParamType.Composite,
            new Uri("http://example.org/fhir/SearchParameter/DocumentReference-relatesto"));
        var targetParam = new SearchParameterInfo("target", "target", SearchParamType.Reference, new Uri("http://example.org/fhir/SearchParameter/DocumentReference-target"));
        var codeParam = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://example.org/fhir/SearchParameter/DocumentReference-code"));

        var tree = new SearchParameterExpression(
            compositeParam,
            new MultiaryExpression(MultiaryOperator.And,
            [
                new CompositeComponentExpression(targetParam, 0,
                    new SearchParameterPredicateExpression(targetParam, SearchComparator.Eq, modifier: null,
                        new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "DocumentReference", resourceId: "456"))),
                new CompositeComponentExpression(codeParam, 1,
                    new SearchParameterPredicateExpression(codeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "replaces", text: null))),
            ]));

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[compositeParam.Url!.ToString()] = 404;
        resolver.ResourceTypeIds["DocumentReference"] = 55;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable);
        var emitted = Emit.Run(plan);

        // Assert
        plan.Explain().ShouldBe(
            "root = ReferenceTokenCompositeSearchParam[404]  ReferenceResourceTypeId1 = @p0 AND ReferenceResourceId1 = @p1 AND Code2 = @p2");
        emitted.Sql.ShouldNotContain("456");
        emitted.Sql.ShouldNotContain("replaces");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)(short)55), ("@p1", (object)"456"), ("@p2", (object)"replaces")]);
    }
```

The exact `Explain()` golden strings above follow `PlanExplainer.cs`'s confirmed rendering rules (from the prior composites increment: `Predicate.And` → `"{Left} AND {Right}"` uppercase, `Predicate.Like` → `"{Column} LIKE @pN ({Match}){collation}"`, `>=`/`<=` tokens, `PrintCollation`'s `_CI_AI`-suffix rule rendering `Latin1_General_CI_AI` as `" collate CI_AI"`). If a run disagrees, trust the actual output over this plan and correct the assertion, but this should not happen -- the rendering rules were read from `PlanExplainer.cs`'s source, not inferred.

- [ ] **Step 2: Run all E2E tests to confirm they pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

Expected: 0 warnings, 0 errors -- all tests pass, including the 7 pre-existing E2E tests (3 base-leaf, 1 non-composite-wrapper, TokenToken, TokenNumberNumber, comma-separated-alternatives).

- [ ] **Step 3: Run the full `Ignixa.Search.Sql.Tests` fixture**

```bash
dotnet test All.sln --filter "FullyQualifiedName~Ignixa.Search.Sql.Tests" --nologo
```

Expected: 0 warnings, 0 errors, all tests pass -- confirming zero regressions across every prior increment's tests (all 7 base leaf rules, the 2 already-shipped composite rules, `SqlCatalogTests`, `EmitTests`, `PlanExplainerTests`, `ResolveTests`).

- [ ] **Step 4: Full solution build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors. The only failures should be the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures per target framework (uninitialized submodule) -- confirm no new failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(search-sql): prove all 6 composite types compile end to end

TokenToken, TokenNumberNumber (prior increment), TokenString,
TokenQuantity, TokenDateTime, ReferenceToken (this increment) all now
lower through the real Resolve -> Lower -> Emit pipeline from real
SearchParameterExpression-wrapped trees."
```

---

## Self-Review

**Spec coverage:** All 4 remaining composite types covered (Tasks 3-6), dispatched via Task 7 (including both component orders for `ReferenceToken`), proven end-to-end via Task 8. The two shared-helper extractions (Tasks 1-2) both close a real, previously-identified gap (duplicated token-column logic; unextracted DateTime range logic) rather than adding speculative abstraction -- both are consumed by multiple call sites within this same plan, not just "for future use."

**Placeholder scan:** No TBD/TODO; every step has complete code. The two real simplifications this plan's design relies on (composite components never carry a modifier; `ReferenceToken` role assignment must be type-based) are both cited against directly-read source (`SearchExpressionBinder.BindComposite`, `RefTokenCompositeRowGenerator.cs`), not assumed.

**Type consistency:** All 4 new rules' signatures (`SearchParameterInfo, IReadOnlyList<SearchParameterPredicateExpression>, LeafContext`) match the calling convention `CompositeLoweringDispatcher` (Task 7) already established for `TokenTokenLoweringRule`/`TokenNumberNumberLoweringRule` in the prior increment -- no new calling convention introduced. `TokenColumnEquality.Build`'s signature matches both its Task-1 refactor call sites and its four new call sites (Tasks 3, 4, 5, 6) exactly. `DateTimeRangeComparison.Build`'s signature matches `NumericRangeComparison.Build`'s existing shape (context, two columns, comparator, value) for consistency, and its two call sites (Task 2's refactored `DateTimeLoweringRule`, Task 5's `TokenDateTimeLoweringRule`) both use it identically.
