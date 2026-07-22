# Compiler Feature-Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close 4 feature gaps in the `Ignixa.Search.Sql` FHIR-search-to-SQL compiler (Token/Number/Quantity/Reference/Uri sort, a keyset continuation-token format, system-level cross-resource-type search, and `$everything`) so a later sub-project can build a SqlServer-native search adapter without inheriting known regressions.

**Architecture:** Every task stays within `Ignixa.Search.Sql`/`Ignixa.Search.Sql.Generators` — zero `Ignixa.DataLayer.SqlServer`/`Ignixa.DataLayer.SqlEntityFramework` changes. Each task extends the existing Build→Resolve→Lower→Emit pipeline: new/changed AST nodes in `Ast/`, new lowering rules/dispatch in `Lowering/`, new SQL rendering in `Builders/SqlBuilder.cs`. No execution against a real database in this plan (that's a later sub-project's differential harness) — every test is a lowering-rule test, an `Emit` golden-SQL test, or an end-to-end `Resolve→Lower→Emit` combined-proof test using `Explain()`/`PlanExplainer`-based assertions, matching this project's established style.

**Tech Stack:** C#/.NET (net9.0 + net10.0 dual-target), xunit + Shouldly, no new NuGet dependencies anywhere in this plan.

## Global Constraints

- Design doc: `docs/superpowers/specs/2026-07-22-fhir-to-sql-compiler-feature-parity-design.md` (5 review rounds, verdict "safe to plan from" — this plan implements it verbatim; if anything here conflicts with that doc, the doc governs and the conflict should be flagged, not silently resolved).
- **Zero changes outside `Ignixa.Search.Sql`/`Ignixa.Search.Sql.Generators`.** No `Ignixa.DataLayer.SqlServer`, no `Ignixa.DataLayer.SqlEntityFramework`.
- **No execution against a real database anywhere in this plan.** Every test is a unit test (lowering-rule test, `Emit` golden-SQL test, or `Resolve→Lower→Emit` combined-proof test).
- **Deterministic `Emit` output**: same plan → byte-identical SQL text. Every new `Emit` code path must be a pure function of its inputs — no non-determinism.
- **Literal vs. parameterized catalog IDs**: preserve existing behavior everywhere except where the design doc explicitly trades it away (system-level search's type-less `ParamSource`/`ResourceSource` — ordinary typed search's literal `ResourceTypeId` rendering in `ParamSource` is UNCHANGED by this plan).
- **"Fail at Lower time" principle**: an unsupported combination throws `NotSupportedException` during `Lower`, not silently produces wrong SQL.
- **Sort-key cap stays at 3** — no code change to the cap itself, only to what a key can represent.
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA comment blocks (`// Arrange` / `// Act` / `// Assert`), `Shouldly` assertions (`.ShouldBe(...)`, `Should.Throw<T>()`).
- Every task ends with a commit. Run the full `Ignixa.Search.Sql.Tests` suite (both `net9.0` and `net10.0`) before each commit, not just the new test file.

---

## File Structure

- `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs` — `SortKeyKind` gains `Aggregated`; `SortKey` gains table/column fields (Task 1).
- `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` — `BuildSortKey` new dispatch arm (Task 1); null-type guard rework, `ResourceTypeId` threading (Task 4); `_type` multi-value handling (Task 5).
- `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` — `EmitSortJoins`/`EmitMissingPrimaryFilter`/`SortValueExpr` new branch (Task 2); `EmitParamSource`/`EmitResourceSource` null-`ResourceTypeId` handling (Task 4); new `EmitTableExistsPredicate`/`EmitVisibleSinceFilter` (Tasks 7-8).
- `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs` — `ParamSource`/`ResourceSource.ResourceTypeId` become `short?` (Task 4); new `TableExistsPredicate`, `VisibleSinceFilter` node kinds (Tasks 7-8).
- `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` — null-safe `ResourceTypeId` resolution (Task 4); new `Except` method (Task 7).
- `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs` (exact filename confirmed at plan time, Task 5) — `TypeEquals` multi-value support.
- New file: `src/Core/Ignixa.Search.Sql/Ast/KeysetContinuationToken.cs` (Task 3).
- New orchestration for `$everything` (exact filename decided at Task 9, e.g. `src/Core/Ignixa.Search.Sql/Lowering/EverythingLowering.cs`).

---

### Task 1: Sort-key AST — `Aggregated` kind and `SortKey` shape

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs:376-384` (`BuildSortKey`)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerSortKeyTests.cs` (new file — or add to an existing `LowerTests.cs`-style file if the implementer finds sort-key-building tests already live somewhere else; check first)

**Interfaces:**
- Consumes: `SqlCatalog.Default.Table(string)` → `TableDescriptor`; `TableDescriptor.Column(string)` → `ColumnDescriptor` (both existing, `src/Core/Ignixa.Search.Sql/Catalog/`).
- Produces: `SortKeyKind.Aggregated`; `SortKey` with new `Table: TableDescriptor?` and `Column: ColumnDescriptor?` fields (both null for `String`/`Date`/`LastUpdated`, both non-null for `Aggregated`) — Task 2 and Task 3 both consume this exact shape.

- [ ] **Step 1: Write the failing tests for the new dispatch arm**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/LowerSortKeyTests.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LowerSortKeyTests
{
    private static SymbolTable SymbolsResolving(SearchParameterInfo parameter, short searchParamId)
        => new(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>());

    [Theory]
    [InlineData(SearchParamType.Token, "TokenSearchParam", "Code")]
    [InlineData(SearchParamType.Number, "NumberSearchParam", "LowValue")]
    [InlineData(SearchParamType.Quantity, "QuantitySearchParam", "LowValue")]
    [InlineData(SearchParamType.Reference, "ReferenceSearchParam", "ReferenceResourceId")]
    [InlineData(SearchParamType.Uri, "UriSearchParam", "Uri")]
    public void GivenASortByAnAggregatedType_WhenLowered_ThenTheKeyCarriesTheCorrectTableAndColumn(
        SearchParamType paramType, string expectedTable, string expectedColumn)
    {
        // Arrange
        var parameter = new SearchParameterInfo("status", "status", paramType, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var sortExpression = new SortExpression(parameter, SortOrder.Ascending);
        var symbols = SymbolsResolving(parameter, 77);

        // Act
        var key = Lower.BuildSortKeyForTest(sortExpression, symbols);

        // Assert
        key.Kind.ShouldBe(SortKeyKind.Aggregated);
        key.SearchParamId.ShouldBe((short)77);
        key.Table.ShouldNotBeNull();
        key.Table!.TableName.ShouldBe(expectedTable);
        key.Column.ShouldNotBeNull();
        key.Column!.Name.ShouldBe(expectedColumn);
    }

    [Fact]
    public void GivenASortByAnUnsupportedCompositeType_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- Composite has no sort meaning (no single scalar column); confirms the switch's
        // default arm still throws for genuinely unsortable types, not silently falling into Aggregated.
        var parameter = new SearchParameterInfo("component-code-value", "component-code-value", SearchParamType.Composite, new Uri("http://hl7.org/fhir/SearchParameter/Observation-component-code-value"));
        var sortExpression = new SortExpression(parameter, SortOrder.Ascending);
        var symbols = SymbolsResolving(parameter, 88);

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.BuildSortKeyForTest(sortExpression, symbols))
            .Message.ShouldContain("Composite");
    }
}
```

Note: `Lower.BuildSortKey` is `private static` today. Add a small `internal` test-seam overload (matching this codebase's established pattern of narrow `internal` seams for otherwise-private logic, e.g. `SqlServerSearchIndexReferenceDataCache`'s `TestSearchParamRowInsertedHookAsync`) — add `[InternalsVisibleTo("Ignixa.Search.Sql.Tests")]` if not already present (check `Ignixa.Search.Sql.csproj`/`AssemblyInfo.cs` first; it likely already exists given other `internal`-seam tests in this project) and change `BuildSortKey`'s access modifier from `private` to `internal`, renaming nothing else. Do NOT add a new `BuildSortKeyForTest` wrapper method — just make the real method `internal` and call it directly as `Lower.BuildSortKey(...)` from the test (the snippet above uses a placeholder name; correct it to the real method name once you confirm the internal-visibility change compiles).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~LowerSortKeyTests"`
Expected: FAIL — `SortKeyKind` has no `Aggregated` member (compile error) until Step 3 lands.

- [ ] **Step 3: Add `SortKeyKind.Aggregated` and `SortKey`'s new fields**

Modify `src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The sort-key kinds the compiler can emit joins and value-expressions for. String and Date read from
/// their search-parameter tables via an IsMin/IsMax-flagged row (no aggregation needed); LastUpdated
/// needs no join at all because ResourceSurrogateId already encodes it; Aggregated covers every other
/// leaf type (Token/Number/Quantity/Reference/Uri) via a MIN/MAX-aggregating derived-table join, since
/// none of those tables carry IsMin/IsMax columns.
/// </summary>
#pragma warning disable CA1720 // Identifier contains type name -- 'String' mirrors the FHIR sort-parameter type it represents.
public enum SortKeyKind
{
    String,
    Date,
    LastUpdated,
    Aggregated,
}
#pragma warning restore CA1720

/// <summary>
/// One _sort key. SearchParamId is null only for <see cref="SortKeyKind.LastUpdated"/>. Table and Column
/// are non-null only for <see cref="SortKeyKind.Aggregated"/> -- String/Date resolve their table/column
/// inline in Emit (StringSearchParam.Text / DateTimeSearchParam.StartDateTime, both fixed), and
/// LastUpdated has no column at all (its sort value is the surrogate id itself).
/// </summary>
public sealed record SortKey(
    short? SearchParamId,
    SortKeyKind Kind,
    SortOrder Direction,
    TableDescriptor? Table = null,
    ColumnDescriptor? Column = null);
```

The rest of the file (`SortPhase`, `SortSpec`, `PageSpec`) is unchanged.

- [ ] **Step 4: Extend `BuildSortKey`'s dispatch**

Modify `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs:376-384` (add `using Ignixa.Search.Sql.Catalog;` to the file's usings if not already present):

```csharp
    /// <summary>Builds one <see cref="SortKey"/>, mapping the parameter to a String/Date/LastUpdated/Aggregated kind and resolving its id (none for _lastUpdated).</summary>
    internal static SortKey BuildSortKey(SortExpression sortExpression, SymbolTable symbols)
    {
        if (sortExpression.Parameter.Code == "_lastUpdated")
        {
            return new SortKey(null, SortKeyKind.LastUpdated, sortExpression.SortOrder);
        }

        var searchParamId = symbols.SearchParamId(sortExpression.Parameter);

        if (sortExpression.Parameter.Type == SearchParamType.String)
        {
            return new SortKey(searchParamId, SortKeyKind.String, sortExpression.SortOrder);
        }

        if (sortExpression.Parameter.Type == SearchParamType.Date)
        {
            return new SortKey(searchParamId, SortKeyKind.Date, sortExpression.SortOrder);
        }

        var (tableName, columnName) = sortExpression.Parameter.Type switch
        {
            SearchParamType.Token => ("TokenSearchParam", "Code"),
            SearchParamType.Number => ("NumberSearchParam", "LowValue"),
            SearchParamType.Quantity => ("QuantitySearchParam", "LowValue"),
            SearchParamType.Reference => ("ReferenceSearchParam", "ReferenceResourceId"),
            SearchParamType.Uri => ("UriSearchParam", "Uri"),
            _ => throw new NotSupportedException(
                $"Sorting by a '{sortExpression.Parameter.Type}' search parameter ('{sortExpression.Parameter.Code}') " +
                "is not supported -- String, Date, _lastUpdated, Token, Number, Quantity, Reference, and Uri " +
                "sort keys are handled; Composite has no single scalar column to sort by."),
        };

        var table = SqlCatalog.Default.Table(tableName);
        var column = table.Column(columnName);
        return new SortKey(searchParamId, SortKeyKind.Aggregated, sortExpression.SortOrder, table, column);
    }
```

Only `BuildSortKey`'s access modifier and body change; `BuildSortSpec` (lines 337-370, the 3-key cap) is untouched.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~LowerSortKeyTests"`
Expected: PASS (both `net9.0` and `net10.0` — run `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj` with no filter once to confirm no regression in the full suite).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/SortSpec.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerSortKeyTests.cs
git commit -m "feat(search-sql): add SortKeyKind.Aggregated covering Token/Number/Quantity/Reference/Uri sort"
```

---

### Task 2: Sort-key `Emit` — aggregated join rendering

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` (`EmitSortJoins`, `EmitMissingPrimaryFilter`, `SortValueExpr`, all currently around lines 338-422 — re-read the current file first, task 1's changes don't touch this file so line numbers should still be close, but confirm before editing)
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitSortAggregatedTests.cs` (new file, or add to the existing sort `EmitTests.cs` region if the implementer finds that's the establishocated convention — check first)

**Interfaces:**
- Consumes: Task 1's `SortKey.Table`/`SortKey.Column` (non-null iff `Kind == Aggregated`).
- Produces: working `Emit`-rendered SQL for `Aggregated` sort keys — sub-project 3's adapter (out of scope here) will execute this SQL directly.

- [ ] **Step 1: Write the failing golden-SQL test**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/EmitSortAggregatedTests.cs
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitSortAggregatedTests
{
    [Fact]
    public void GivenASingleAscendingTokenSortKeyInTheValuedPhase_WhenEmitted_ThenLeftJoinsAnAggregatingDerivedTable()
    {
        // Arrange -- Observation?_sort=status, first page (no boundary).
        var predicateTable = SqlCatalog.Default.Table("TokenSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "Code"), new SqlParameterRef("final"));
        var sortTable = SqlCatalog.Default.Table("TokenSearchParam");
        var sortColumn = sortTable.Column("Code");
        var sort = new SortSpec(
            [new SortKey(77, SortKeyKind.Aggregated, SortOrder.Ascending, sortTable, sortColumn)],
            SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE ResourceTypeId = 103 AND SearchParamId = 202 AND Code = @p0\n" +
            ")\n" +
            "SELECT TOP (10) m.T1, m.Sid1, sk0.AggValue AS SortValue0 FROM cte0 m\n" +
            "LEFT JOIN (\n" +
            "    SELECT ResourceTypeId, ResourceSurrogateId, MIN(Code) AS AggValue\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE SearchParamId = 77\n" +
            "    GROUP BY ResourceTypeId, ResourceSurrogateId\n" +
            ") sk0 ON sk0.ResourceTypeId = m.T1 AND sk0.ResourceSurrogateId = m.Sid1\n" +
            "ORDER BY ISNULL(sk0.AggValue, N''), m.T1 ASC, m.Sid1 ASC");
    }

    [Fact]
    public void GivenADescendingNumberSortKey_WhenEmitted_ThenTheDerivedTableAggregatesWithMax()
    {
        // Arrange
        var predicateTable = SqlCatalog.Default.Table("NumberSearchParam");
        var predicate = new Predicate.Equal(new SqlColumnRef(predicateTable.TableName, "LowValue"), new SqlParameterRef(5m));
        var sortTable = SqlCatalog.Default.Table("NumberSearchParam");
        var sortColumn = sortTable.Column("LowValue");
        var sort = new SortSpec(
            [new SortKey(88, SortKeyKind.Aggregated, SortOrder.Descending, sortTable, sortColumn)],
            SortPhase.Valued);
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(predicateTable, 103, 202, predicate)],
            new CteRef(0),
            Top: 10,
            Sort: sort);

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("MAX(LowValue) AS AggValue");
        emitted.Sql.ShouldContain("ORDER BY ISNULL(sk0.AggValue, '0'), m.T1 ASC, m.Sid1 ASC");
    }
}
```

Note: the exact sentinel value for a numeric `ISNULL` (used above as `'0'`) and for `Aggregated`'s string-vs-numeric-vs-date sentinel choice generally is a real open detail — resolve it by reading `SortValueExpr`'s existing sentinel logic (String→`N''`, Date→`'0001-01-01T00:00:00.0000000'`) and extending the same switch using `SortKey.Column.SqlType` to pick a per-SQL-type sentinel (`nvarchar`/`varchar`→`N''`, numeric types→`'0'` or `0` unquoted per what `ISNULL` needs for that column's real type, `uniqueidentifier`/reference id columns→whatever `ReferenceResourceId`'s actual SQL type turns out to be — check `ColumnDescriptor.SqlType` for `ReferenceSearchParam.ReferenceResourceId` and `UriSearchParam.Uri` specifically before finalizing all 5 sentinels, since Reference's column may not be a simple string). Write the actual sentinel-selection code in Step 3 based on what you find, and update this test's exact expected string to match — do not guess this detail without reading the DDL first (`src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/TokenSearchParam.sql`, `NumberSearchParam.sql`, `QuantitySearchParam.sql`, `ReferenceSearchParam.sql`, `UriSearchParam.sql`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmitSortAggregatedTests"`
Expected: FAIL — no `Aggregated` case in `EmitSortJoins`/`SortValueExpr` yet (either a thrown exception from an unhandled switch case, or a compile error if the existing code is a ternary rather than a switch — check which).

- [ ] **Step 3: Add the `Aggregated` branch to `EmitSortJoins`, `EmitMissingPrimaryFilter`, `SortValueExpr`**

Modify `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`. Re-read the current `EmitSortJoins`/`EmitMissingPrimaryFilter`/`SortValueExpr`/`EmitOrderBy` methods in full before editing (they may have shifted slightly from the line numbers cited in the design doc). Convert each method's `String`-vs-`Date` ternary into a 3-way dispatch, adding the `Aggregated` case as a genuinely new code path (not an extension of the ternary) per the design doc's exact derived-table shape:

```csharp
    private static string EmitSortJoins(SortSpec? sort)
    {
        if (sort is null) return string.Empty;

        var joins = new List<string>();
        for (var i = 0; i < sort.Keys.Count; i++)
        {
            if (i == 0 && sort.Phase == SortPhase.MissingPrimary) { continue; }

            var key = sort.Keys[i];
            if (key.Kind == SortKeyKind.LastUpdated) { continue; }

            if (key.Kind == SortKeyKind.Aggregated)
            {
                var aggFunc = key.Direction == SortOrder.Ascending ? "MIN" : "MAX";
                joins.Add(
                    $"\nLEFT JOIN (\n" +
                    $"    SELECT ResourceTypeId, ResourceSurrogateId, {aggFunc}({key.Column!.Name}) AS AggValue\n" +
                    $"    FROM {key.Table!.SchemaName}.{key.Table.TableName}\n" +
                    $"    WHERE SearchParamId = {key.SearchParamId}\n" +
                    $"    GROUP BY ResourceTypeId, ResourceSurrogateId\n" +
                    $") sk{i} ON sk{i}.ResourceTypeId = m.T1 AND sk{i}.ResourceSurrogateId = m.Sid1");
                continue;
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
```

`EmitMissingPrimaryFilter` needs the equivalent `NOT EXISTS` shape for `Aggregated` (an aggregated key's primary-sort-key `MissingPrimary` phase means "no row exists for this SearchParamId at all," identical in spirit to String/Date's `NOT EXISTS`, just against the aggregated table without a `GROUP BY`):

```csharp
    private static string EmitMissingPrimaryFilter(SortSpec sort)
    {
        var key = sort.Keys[0];
        if (key.Kind == SortKeyKind.LastUpdated || key.SearchParamId is null) { throw new InvalidOperationException("..."); }

        if (key.Kind == SortKeyKind.Aggregated)
        {
            return $"NOT EXISTS (SELECT 1 FROM {key.Table!.SchemaName}.{key.Table.TableName} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
        }

        var table = key.Kind == SortKeyKind.String ? "StringSearchParam" : "DateTimeSearchParam";
        return $"NOT EXISTS (SELECT 1 FROM dbo.{table} s WHERE s.ResourceTypeId = m.T1 AND s.ResourceSurrogateId = m.Sid1 AND s.SearchParamId = {key.SearchParamId})";
    }
```

`SortValueExpr` gains the `Aggregated` branch, resolving its sentinel from `ColumnDescriptor.SqlType` (finalize the exact sentinel-selection logic per Step 1's note, after reading the 5 tables' real column types):

```csharp
    private static string SortValueExpr(SortSpec sort, int index)
    {
        var key = sort.Keys[index];
        if (key.Kind == SortKeyKind.LastUpdated) { return "m.Sid1"; }

        var isGuaranteedNonNull = index == 0 && sort.Phase == SortPhase.Valued;

        if (key.Kind == SortKeyKind.Aggregated)
        {
            var raw = $"sk{index}.AggValue";
            if (isGuaranteedNonNull) { return raw; }
            var aggSentinel = SentinelFor(key.Column!.SqlType); // implement per the real SqlType values found in Step 1
            return $"ISNULL({raw}, {aggSentinel})";
        }

        var column = key.Kind == SortKeyKind.String ? "Text" : "StartDateTime";
        var raw2 = $"sk{index}.{column}";
        if (isGuaranteedNonNull) { return raw2; }
        var sentinel = key.Kind == SortKeyKind.String ? "N''" : "'0001-01-01T00:00:00.0000000'";
        return $"ISNULL({raw2}, {sentinel})";
    }
```

Add a small private `SentinelFor(string sqlType)` helper mapping each real `SqlType` value found in Step 1's DDL read to its correct `ISNULL` sentinel (e.g. `nvarchar`-family → `N''`, numeric-family → `0`, etc.) — write this only after reading the actual DDL, not from assumption.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmitSortAggregatedTests"`
Expected: PASS. Then run the full suite with no filter to confirm zero regression in the existing String/Date sort golden tests (their exact SQL text must stay byte-identical since their code path is untouched).

- [ ] **Step 5: Write and pass a combined end-to-end proof test**

Add one test to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` proving a real `Resolve→Lower→Emit` compilation for `Observation?_sort=status` (or similar) produces the expected `Aggregated` join, matching this file's existing style for other features' combined-proof tests (read a couple of existing tests in that file first for the exact `Resolve`/symbol-table setup boilerplate to reuse).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Ast/EmitSortAggregatedTests.cs test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "feat(search-sql): emit MIN/MAX-aggregating derived-table joins for Aggregated sort keys"
```

---

### Task 3: Keyset continuation token

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Ast/KeysetContinuationToken.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/KeysetContinuationTokenTests.cs`

**Interfaces:**
- Consumes: Task 1's finalized `SortKey.Column` (for `ColumnDescriptor.SqlType`-based boundary-value typing, per the design doc).
- Produces: `KeysetContinuationToken.Encode(...)` / `KeysetContinuationToken.TryDecode(...)` — sub-project 3's adapter (out of scope here) calls these to bridge `SearchOptions.ContinuationToken` (a string) to/from a `PageSpec`.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/KeysetContinuationTokenTests.cs
using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class KeysetContinuationTokenTests
{
    [Fact]
    public void GivenASingleBoundaryValue_WhenEncodedThenDecoded_ThenRoundTripsExactly()
    {
        // Arrange
        var token = KeysetContinuationToken.Encode(["Adams"], resourceTypeId: 103, surrogateId: 5000L);

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeTrue();
        boundaryValues.ShouldBe(["Adams"]);
        resourceTypeId.ShouldBe(103);
        surrogateId.ShouldBe(5000L);
    }

    [Fact]
    public void GivenMultipleBoundaryValues_WhenEncodedThenDecoded_ThenRoundTripsExactly()
    {
        // Arrange
        var token = KeysetContinuationToken.Encode(["Zorro", "2000-01-01T00:00:00.0000000"], resourceTypeId: 103, surrogateId: 9000L);

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeTrue();
        boundaryValues.ShouldBe(["Zorro", "2000-01-01T00:00:00.0000000"]);
        resourceTypeId.ShouldBe(103);
        surrogateId.ShouldBe(9000L);
    }

    [Fact]
    public void GivenAZeroBoundaryValueToken_WhenEncodedThenDecoded_ThenRoundTripsAnEmptyList()
    {
        // Arrange -- MissingPrimary-phase first page has no boundary values at all.
        var token = KeysetContinuationToken.Encode([], resourceTypeId: 103, surrogateId: 7000L);

        // Act
        var decoded = KeysetContinuationToken.TryDecode(token, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeTrue();
        boundaryValues.ShouldBeEmpty();
        resourceTypeId.ShouldBe(103);
        surrogateId.ShouldBe(7000L);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!!")]
    [InlineData("dGhpcyBpcyBub3QgSlNPTg==")] // valid base64, invalid JSON payload
    public void GivenAMalformedToken_WhenDecoded_ThenReturnsFalseWithoutThrowing(string malformed)
    {
        // Act
        var decoded = KeysetContinuationToken.TryDecode(malformed, out var boundaryValues, out var resourceTypeId, out var surrogateId);

        // Assert
        decoded.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~KeysetContinuationTokenTests"`
Expected: FAIL — `KeysetContinuationToken` doesn't exist yet.

- [ ] **Step 3: Implement `KeysetContinuationToken`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/KeysetContinuationToken.cs
using System.Text;
using System.Text.Json;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Encodes/decodes a keyset-pagination continuation token for this compiler's <see cref="PageSpec"/>
/// shape. Not compatible with, and not intended to bridge to, Ignixa.Search.Models.ContinuationToken
/// (an offset+count token for the legacy EF-based read path) -- keyset and offset pagination are
/// different models, not different formats of the same thing. A token minted before a cutover to the
/// keyset-based path simply goes stale; the client restarts from page 1, which is acceptable.
/// </summary>
public static class KeysetContinuationToken
{
    public static string Encode(IReadOnlyList<string> boundaryValues, int resourceTypeId, long surrogateId)
    {
        var state = new TokenState
        {
            BoundaryValues = [.. boundaryValues],
            BoundaryResourceTypeId = resourceTypeId,
            BoundarySurrogateId = surrogateId,
        };
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryDecode(string token, out IReadOnlyList<string> boundaryValues, out int resourceTypeId, out long surrogateId)
    {
        boundaryValues = [];
        resourceTypeId = 0;
        surrogateId = 0;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(token);
            var json = Encoding.UTF8.GetString(bytes);
            var state = JsonSerializer.Deserialize<TokenState>(json);
            if (state is null || state.BoundaryValues is null)
            {
                return false;
            }

            boundaryValues = state.BoundaryValues;
            resourceTypeId = state.BoundaryResourceTypeId;
            surrogateId = state.BoundarySurrogateId;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    private sealed class TokenState
    {
        public string[]? BoundaryValues { get; set; }
        public int BoundaryResourceTypeId { get; set; }
        public long BoundarySurrogateId { get; set; }
    }
}
```

The `catch` clause matches specific exception types (`FormatException` for bad base64, `JsonException` for bad JSON, `DecoderFallbackException` for bad UTF-8) rather than a blanket `catch` — this project's CLAUDE.md standard is "no silent failures" via empty/blanket catches; a `TryXxx` method returning `false` on a recognized decode-failure class is the established exception, not a violation, since it's the documented contract of a `TryDecode` method (mirrors `int.TryParse`'s own shape) — but enumerate the specific exception types rather than `catch (Exception)` to avoid also swallowing a genuine programmer-error exception from elsewhere in the method body.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~KeysetContinuationTokenTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/KeysetContinuationToken.cs test/Ignixa.Search.Sql.Tests/Ast/KeysetContinuationTokenTests.cs
git commit -m "feat(search-sql): add KeysetContinuationToken for PageSpec's keyset boundary"
```

---

### Task 4: System-level search — nullable `ResourceTypeId` core mechanism

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs` (`ParamSource`, `ResourceSource`)
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` (`RequireResourceType`, `LowerNode`, `LowerAnd`, the null-`targetResourceType` guards at ~53-57/62-68, `Run`'s top-level signature)
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` (`Lower`, `LowerComposite`, `LowerResourceSource`, `LowerNot`'s internal call — every `_leafContext.ResourceTypeId(resourceType)` call site)
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` (`EmitParamSource`, `EmitResourceSource`)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/SystemLevelSearchTests.cs` (new file)

**Interfaces:**
- Consumes: nothing new from earlier tasks (independent of Tasks 1-3).
- Produces: `Lower.Run(..., resourceType: null, ...)` no longer throws for ordinary leaf/composite/`And`/`Or` predicates and the base `ResourceSource` case — Task 5 and Task 6 both build on this.

- [ ] **Step 1: Re-read the current code before editing**

Before writing any test or code, re-read in full: `CteDefinition.cs`'s `ParamSource`/`ResourceSource` records, `Lower.cs`'s `RequireResourceType`, `LowerNode`, `LowerAnd`, the null-type guards near lines 53-57 and 62-68, and `Run`'s signature; `StructuralContext.cs`'s `Lower`, `LowerComposite`, `LowerResourceSource`, `LowerNot`, `LowerParameterPresence`; `SqlBuilder.cs`'s `EmitParamSource` and `EmitResourceSource`. Confirm every call site of `_leafContext.ResourceTypeId(resourceType)` (grep for it) so Step 3 covers all of them, not just the ones cited in the design doc.

- [ ] **Step 2: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/SystemLevelSearchTests.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class SystemLevelSearchTests
{
    [Fact]
    public void GivenAnOrdinaryPredicateWithNoResourceType_WhenLowered_ThenParamSourceHasANullResourceTypeId()
    {
        // Arrange -- GET /?status=final (a Token predicate, no resource type constraint at all).
        var parameter = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "final", text: null));
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());
        var resolved = new ResolvedSymbols(symbols, []);

        // Act
        var lowered = Lower.Run(predicate, resolved.Symbols, resourceType: null, [], [], includeLimit: 0, sort: null, SortPhase.Valued, page: null);

        // Assert
        lowered.Plan.Ctes.Count.ShouldBe(1);
        var cte = lowered.Plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        cte.ResourceTypeId.ShouldBeNull();
        cte.SearchParamId.ShouldBe((short)202);
    }

    [Fact]
    public void GivenABareRequestWithNoPredicatesAtAll_WhenLowered_ThenResourceSourceHasANullResourceTypeId()
    {
        // Arrange -- GET /?_lastUpdated=gt2020-01-01 (a resource-column-only query, no ParamSource CTE at all).
        var lastUpdatedParam = new SearchParameterInfo("_lastUpdated", "_lastUpdated", SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
        var predicate = new SearchParameterPredicateExpression(lastUpdatedParam, SearchComparator.Gt, modifier: null, new DateTimeSearchValue(DateTime.Parse("2020-01-01")));
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act
        var lowered = Lower.Run(predicate, symbols, resourceType: null, [], [], includeLimit: 0, sort: null, SortPhase.Valued, page: null);

        // Assert -- exact CTE shape (ResourceSource with a resource-column OuterPredicate, or however
        // _lastUpdated actually lowers -- confirm against the real ResourceColumnLoweringRule behavior
        // before finalizing this assertion; it may be an OuterPredicate on a bare ResourceSource rather
        // than a predicate directly on the ParamSource-shaped node).
        lowered.Plan.Ctes.ShouldContain(c => c is CteDefinition.ResourceSource rs && rs.ResourceTypeId == null);
    }

    [Fact]
    public void GivenAChainedExpressionWithNoResourceType_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- chain still requires a known target type; confirm the composition-limit guard fires.
        // (Construct a minimal ChainedExpression per this test file's existing chain-test conventions --
        // check ChainLoweringRuleTests.cs or similar for the exact construction shape before writing this.)

        // Act & Assert
        // Should.Throw<NotSupportedException>(() => Lower.Run(chainExpression, symbols, resourceType: null, ...));
    }

    [Fact]
    public void GivenAnIncludeWithNoResourceType_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- mirrors the existing wildcard-compartment-plus-include guard; confirm system-level
        // search hits an equivalent guard, not a silent wrong result.
    }

    [Fact]
    public void GivenAWildcardCompartmentSearch_WhenLoweredWithNoOrdinaryPredicates_ThenStillWorksUnaffected()
    {
        // Arrange -- regression guard: wildcard compartment search's own null-resourceType handling
        // (a DIFFERENT null-type case from system-level search) must still behave exactly as before this
        // task's changes. Reuse this file's or LowerTests.cs's existing wildcard-compartment test setup.
    }
}
```

The last three tests are sketched, not complete — Step 5 finalizes them once the guard-rework in Step 3 clarifies exactly how system-level search's null-type case is distinguished from wildcard-compartment's (a boolean flag threaded through `Lower.Run`? A distinct enum? Decide in Step 3, then come back and complete these tests).

- [ ] **Step 3: Make `ResourceTypeId` nullable and thread null-safety through**

Modify `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`:

```csharp
public sealed record ParamSource(TableDescriptor Table, short? ResourceTypeId, short SearchParamId, Predicate? Predicate = null) : CteDefinition;
public sealed record ResourceSource(short? ResourceTypeId, Predicate? Predicate = null) : CteDefinition;
```

(Every other `CteDefinition` variant is unchanged.)

Modify `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`:
- `RequireResourceType` no longer throws for system-level search's null case — it needs to distinguish "null because system-level search" (proceed) from "null because wildcard compartment, and this dispatch site doesn't support that combination" (throw, unchanged behavior). The simplest mechanism: `Lower.Run`'s top-level entry already receives `resourceType: string?` — thread through an explicit new parameter (e.g. `bool allowNullResourceType = false`, defaulting to preserving today's exact throw-on-null behavior everywhere it's currently called) OR distinguish by which caller path reached the guard (wildcard compartment never calls `RequireResourceType`-guarded dispatch at all today per the design doc's own finding — confirm this is still true after your Step 1 re-read; if genuinely true, then ANY caller reaching `RequireResourceType` with a null type is, by construction, system-level search, and the guard can simply stop throwing unconditionally, with wildcard compartment's own separate, already-existing guards at `Lower.cs:53-57`/`62-68` continuing to police the specific combinations (typed leaf, sort) it doesn't support). Verify this claim precisely before choosing the mechanism — it determines whether this is a one-line guard removal or a real new parameter.
- `LowerAnd`, `LowerSearchParameter`, and every other site currently declared `resourceType: string` (non-nullable) after the top-level `RequireResourceType` call becomes `resourceType: string?` and passes it through unchanged to `StructuralContext`.

Modify `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` — every call site of `_leafContext.ResourceTypeId(resourceType)` (found in Step 1's grep) becomes:

```csharp
short? resourceTypeId = resourceType is null ? null : _leafContext.ResourceTypeId(resourceType);
```

`LowerNot`'s internal `LowerResourceSource(resourceType)` call: per the design doc, `:not` needs an explicit decision (nullable treatment vs. explicit guard) — resolve this now by reading `LowerNot`'s full current body (already read in Step 1) and either (a) threading the same nullable treatment through if it composes cleanly, or (b) adding an explicit `if (resourceType is null) throw new NotSupportedException(":not is not supported in system-level search in this phase.")` guard at the top of `LowerNot` — pick whichever the real code shape makes cleaner and document the choice in the commit message.

Modify `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`:

```csharp
    private static string EmitParamSource(CteDefinition.ParamSource p, List<object> parameters)
    {
        var typeFilter = p.ResourceTypeId is { } typeId ? $"ResourceTypeId = {typeId} AND " : string.Empty;
        // ... existing WHERE-clause assembly, with typeFilter prepended instead of an unconditional "ResourceTypeId = {p.ResourceTypeId} AND "
    }

    private static string EmitResourceSource(CteDefinition.ResourceSource rs, List<object> parameters)
    {
        var typeFilter = rs.ResourceTypeId is { } typeId ? $"ResourceTypeId = {EmitParam(new SqlParameterRef(typeId), parameters)} AND " : string.Empty;
        // ... existing WHERE-clause assembly, same pattern
    }
```

Re-read the ACTUAL current bodies of both methods before editing (they were quoted in this plan's research phase but re-confirm against the live file, since Task 1/2 didn't touch this file but time has passed) and apply the null-conditional prefix pattern to whatever the real surrounding WHERE-clause-assembly code looks like — do not guess the surrounding string concatenation shape from this sketch alone.

- [ ] **Step 4: Run tests, expect the first two to pass and the sketched ones to still need finishing**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SystemLevelSearchTests"`

- [ ] **Step 5: Finish the sketched composition-limit and regression tests**

Complete the three sketched tests from Step 2 (chain, include, wildcard-compartment-regression) using the real construction patterns from this test project's existing chain/include/compartment test files (`ChainLoweringRuleTests.cs`, `LowerTests.cs`'s wildcard-compartment tests — read them for exact setup boilerplate). Run the full suite once more to confirm all pass and nothing regresses:

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, full suite, both target frameworks.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Lowering/SystemLevelSearchTests.cs
git commit -m "feat(search-sql): support system-level search via nullable ParamSource/ResourceSource.ResourceTypeId"
```

---

### Task 5: `_type` multi-value support

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs` (confirm exact filename/path at task start — cited as containing `TypeEquals`)
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` (`ExtractResourceColumnPredicates`, `Lower.cs:202-231`)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs` (add to existing file if present, else create)

**Interfaces:**
- Consumes: Task 4's nullable-`ResourceTypeId` infrastructure is NOT required for this task — this closes a gap in ordinary (typed and type-less) `_type` handling, independent of Task 4's mechanism, though it's most visibly useful for system-level search's `_type=Patient,Observation` case.
- Produces: `TypeEquals`/`ExtractResourceColumnPredicates` correctly handle a comma-separated `_type` value list; Task 6's end-to-end tests exercise this.

- [ ] **Step 1: Confirm the gap is real before writing a fix**

Before any code, write and run one throwaway (not committed) confirmation test proving `_type=Patient,Observation` currently throws `NotSupportedException` via `RejectResourceColumnCode` (per the design doc's citation) rather than silently producing wrong results — this confirms the "loud failure, not silent wrong result" framing and that this is genuinely a gap to close, not a misunderstanding. Delete this throwaway test before Step 2's real, permanent test.

- [ ] **Step 2: Write the failing test**

```csharp
// Add to the appropriate existing test file for resource-column lowering, or create
// test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs

[Fact]
public void GivenACommaSeparatedTypeList_WhenLowered_ThenComposesAsAnOrOfEqualsExtractedIntoOuterPredicate()
{
    // Arrange -- GET /?_type=Patient,Observation&status=final (a real multi-value _type combined with
    // an ordinary predicate, matching the design doc's cited example query).
    // Construct per this file's/Lower.cs's existing test conventions for _type handling -- read an
    // existing single-value _type test first for the exact expression-tree shape to build the
    // multi-value version of (likely a SearchParameterExpression wrapping an Or of two TypeEquals-shaped
    // predicates, or however this codebase's binder actually represents "_type=A,B" -- check
    // SearchExpressionBinder.cs for the real binder shape before assuming).

    // Act

    // Assert -- confirms the Or-of-Equals lands in QueryPlan.OuterPredicate (not thrown as
    // NotSupportedException), and that the resulting Emit'd SQL contains
    // "(ResourceTypeId = @p0 OR ResourceTypeId = @p1)" or equivalent.
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~CommaSeparatedTypeList"`
Expected: FAIL with `NotSupportedException` (confirms Step 1's throwaway finding, now as a permanent regression-proof test).

- [ ] **Step 4: Extend `TypeEquals` and `ExtractResourceColumnPredicates`**

Re-read `ResourceColumnLoweringRule.cs`'s `TypeEquals` method and `Lower.cs`'s `ExtractResourceColumnPredicates` (`:202-231`) in full before editing. Per the design doc: `Predicate.In` does not exist in `EmitPredicate`'s arms — the fix is an `Or` of `Predicate.Equal`s, recognized by `ExtractResourceColumnPredicates`. Extend `TypeEquals` to accept a multi-value binder input (confirm the real binder shape from `SearchExpressionBinder.cs` first) and produce `new Predicate.Or([Equal(...), Equal(...), ...])` instead of throwing on non-single-value input; extend `ExtractResourceColumnPredicates` to recognize an `Or` of same-column `Equal` predicates as a resource-column predicate eligible for extraction into `OuterPredicate`, alongside its existing top-level-`And` handling.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~ResourceColumnLoweringRuleTests"` then the full suite with no filter.
Expected: PASS, zero regression (single-value `_type` behavior must stay byte-identical).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/ResourceColumnLoweringRule.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/Lowering/ResourceColumnLoweringRuleTests.cs
git commit -m "feat(search-sql): support comma-separated _type value lists"
```

---

### Task 6: System-level search — end-to-end combined-proof tests

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (add to existing file)

**Interfaces:**
- Consumes: Tasks 4 and 5's completed mechanisms.
- Produces: nothing new (test-only task) — closes out Section 4 of the design doc.

- [ ] **Step 1: Write end-to-end tests**

Add tests to `EndToEndCompilationTests.cs` (read a few existing tests in this file first for exact `Resolve`/symbol-table/`Explain()`-assertion boilerplate) covering, at minimum:
- `GET /?status=final` — ordinary type-less predicate compiles and produces the expected `Explain()`-verified plan shape (one `ParamSource` with null `ResourceTypeId`).
- `GET /?_lastUpdated=gt2020-01-01` — bare resource-column-only system search.
- `GET /?_type=Patient,Observation&status=final` — Task 5's multi-value `_type` composed with an ordinary predicate.
- `GET /?status=final&_include=Observation:subject` (or similar) — confirms the chain/include composition-limit guard from Task 4 fires correctly, `Should.Throw<NotSupportedException>()`.
- A wildcard-compartment regression test proving that flow is entirely unaffected by this task's changes.

Follow this file's established combined-proof pattern (exact `Explain()` string pinning, not loose substring `ShouldContain` checks — this project's own retrospective explicitly calls out substring-only assertions as its most recurring bug class).

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, full suite, both `net9.0` and `net10.0`.

- [ ] **Step 3: Commit**

```bash
git add test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "test(search-sql): combined-proof tests for system-level search end to end"
```

---

### Task 7: `$everything` — `TableExistsPredicate` and `Except`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs` (new `TableExistsPredicate`)
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` (new `Except` method)
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` (new `EmitTableExistsPredicate`, `EmitCte` dispatch arm)
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTableExistsPredicateTests.cs`, `test/Ignixa.Search.Sql.Tests/Lowering/StructuralContextExceptTests.cs`

**Interfaces:**
- Consumes: nothing from Tasks 1-6 (independent).
- Produces: `CteDefinition.TableExistsPredicate`, `StructuralContext.Except(CteRef, CteRef)` — Task 9 composes both.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/EmitTableExistsPredicateTests.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitTableExistsPredicateTests
{
    [Fact]
    public void GivenATableExistsPredicateWithNoPredicate_WhenEmitted_ThenSelectsWithNoWhereClause()
    {
        // Arrange -- "does this resource have any date-typed search-index row at all"
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var plan = new QueryPlan([new CteDefinition.TableExistsPredicate(table)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.DateTimeSearchParam\n" +
            ")\n" +
            "SELECT TOP (1000) m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        // Adjust the TOP/default-count and exact trailing SELECT shape to match this codebase's real
        // no-Top-specified default (check an existing simple ParamSource-only EmitTests case for the
        // real default rendering before finalizing this assertion).
    }

    [Fact]
    public void GivenATableExistsPredicateWithADateRangePredicate_WhenEmitted_ThenFiltersByIt()
    {
        // Arrange -- "does this resource have a date-typed row matching this range"
        var table = SqlCatalog.Default.Table("DateTimeSearchParam");
        var predicate = new Predicate.GreaterThanOrEqual(new SqlColumnRef(table.TableName, "StartDateTime"), new SqlParameterRef("2020-01-01T00:00:00.0000000"));
        var plan = new QueryPlan([new CteDefinition.TableExistsPredicate(table, predicate)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("WHERE StartDateTime >= @p0");
    }
}
```

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/StructuralContextExceptTests.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Search.Sql.Lowering;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class StructuralContextExceptTests
{
    [Fact]
    public void GivenTwoCteRefs_WhenExcepted_ThenAddsAnExceptCteAndReturnsItsRef()
    {
        // Arrange
        var context = new StructuralContext(/* real constructor args -- check StructuralContext.cs's
                                                 actual constructor before writing this, likely takes
                                                 a LeafContext or similar */);
        var left = context.LowerResourceSource("Patient"); // or whatever the real seam is to get a CteRef cheaply for this test
        var right = context.LowerResourceSource("Patient");

        // Act
        var result = context.Except(left, right);

        // Assert
        var plan = context.BuildPlan(result); // or however this test file's siblings materialize a QueryPlan from a StructuralContext -- check existing StructuralContext tests for the pattern
        var exceptCte = plan.Ctes[plan.Ctes.Count - 1].ShouldBeOfType<CteDefinition.Except>();
        exceptCte.Left.ShouldBe(left);
        exceptCte.Right.ShouldBe(right);
    }
}
```

Both tests are sketched with explicit notes to check real constructor/helper shapes — `StructuralContext`'s real public surface wasn't fully captured in this plan's research pass; read the file directly before finalizing either test's Arrange section.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~TableExistsPredicate|FullyQualifiedName~StructuralContextExcept"`
Expected: FAIL (compile errors — neither type/method exists yet).

- [ ] **Step 3: Add `TableExistsPredicate`, `Except`, and their `Emit` support**

`src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs` — add:

```csharp
/// <summary>
/// A raw table row-existence check, scoped only by ResourceSurrogateId (via the outer join, not a WHERE
/// clause of its own) plus an optional additional Predicate. Unlike ParamSource, carries no SearchParamId
/// or ResourceTypeId -- for checks that are genuinely table-wide, e.g. $everything's "does this resource
/// have ANY date-typed search-index row" (Predicate: null) or "...matching this date range" (Predicate: set).
/// </summary>
public sealed record TableExistsPredicate(TableDescriptor Table, Predicate? Predicate = null) : CteDefinition;
```

(`CteDefinition.Except` already exists per this plan's research — confirm at Step 1's re-read, do not re-add it.)

`src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` — add, mirroring `Union`'s existing one-line shape exactly:

```csharp
    public CteRef Except(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Except(left, right));
        return new CteRef(_ctes.Count - 1);
    }
```

`src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` — add `EmitTableExistsPredicate`, mirroring `EmitCompartmentSource`'s shape minus the `WHERE SearchParamId =`/type-filter clauses:

```csharp
    private static string EmitTableExistsPredicate(CteDefinition.TableExistsPredicate tep, List<object> parameters)
    {
        var whereClause = tep.Predicate is not null
            ? $"\n    WHERE {EmitPredicate(tep.Predicate, parameters)}"
            : string.Empty;
        return
            $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            $"    FROM {tep.Table.SchemaName}.{tep.Table.TableName}{whereClause}";
    }
```

Add the dispatch arm to `EmitCte`'s switch: `CteDefinition.TableExistsPredicate tep => EmitTableExistsPredicate(tep, parameters),`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~TableExistsPredicate|FullyQualifiedName~StructuralContextExcept"`
Expected: PASS. Then the full suite with no filter.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTableExistsPredicateTests.cs test/Ignixa.Search.Sql.Tests/Lowering/StructuralContextExceptTests.cs
git commit -m "feat(search-sql): add TableExistsPredicate node kind and StructuralContext.Except"
```

---

### Task 8: `$everything` — `VisibleSinceFilter`

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs` (new `VisibleSinceFilter`)
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs` (new `EmitVisibleSinceFilter`)
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitVisibleSinceFilterTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks (independent).
- Produces: `CteDefinition.VisibleSinceFilter` — Task 9 composes it via the existing `Intersect`.

- [ ] **Step 1: Write the failing test**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/EmitVisibleSinceFilterTests.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitVisibleSinceFilterTests
{
    [Fact]
    public void GivenAVisibleSinceFilter_WhenEmitted_ThenJoinsTransactionsOnVisibleDate()
    {
        // Arrange
        var since = new SqlParameterRef("2020-01-01T00:00:00.0000000");
        var plan = new QueryPlan([new CteDefinition.VisibleSinceFilter(since)], new CteRef(0));

        // Act
        var emitted = SqlBuilder.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.Resource r\n" +
            "    INNER JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue\n" +
            "    WHERE t.VisibleDate >= @p0\n" +
            ")\n" +
            "SELECT TOP (1000) m.T1, m.Sid1 FROM cte0 m\n" +
            "ORDER BY m.T1 ASC, m.Sid1 ASC");
        // Adjust the default-TOP/trailing-SELECT shape to match this codebase's real convention, same
        // note as Task 7's EmitTableExistsPredicateTests -- check an existing simple single-CTE test.
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmitVisibleSinceFilterTests"`
Expected: FAIL (compile error, type doesn't exist).

- [ ] **Step 3: Add `VisibleSinceFilter` and its `Emit` support**

`src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`:

```csharp
/// <summary>
/// $everything's _since filter -- resources visible in a transaction on or after Since. Scoped to
/// whichever branch it's Intersect-composed with (design: the compartment branch only, never the
/// Patient-itself or referenced-type-expansion branches -- see the $everything orchestration task).
/// VisibleDate (not CreateDate) is Transactions' incremental-visibility column, NULL until a
/// transaction becomes visible -- distinct from CreateDate, which SqlServerFhirRepository's existing
/// LastModified derivation uses for a different purpose.
/// </summary>
public sealed record VisibleSinceFilter(SqlParameterRef Since) : CteDefinition;
```

`src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`:

```csharp
    private static string EmitVisibleSinceFilter(CteDefinition.VisibleSinceFilter vsf, List<object> parameters)
        => "    SELECT DISTINCT r.ResourceTypeId AS T1, r.ResourceSurrogateId AS Sid1\n" +
           "    FROM dbo.Resource r\n" +
           "    INNER JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue\n" +
           $"    WHERE t.VisibleDate >= {EmitParam(vsf.Since, parameters)}";
```

Add the dispatch arm: `CteDefinition.VisibleSinceFilter vsf => EmitVisibleSinceFilter(vsf, parameters),`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmitVisibleSinceFilterTests"`
Expected: PASS. Then the full suite with no filter.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs test/Ignixa.Search.Sql.Tests/Ast/EmitVisibleSinceFilterTests.cs
git commit -m "feat(search-sql): add VisibleSinceFilter node kind for \$everything's _since"
```

---

### Task 9: `$everything` — orchestration

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/EverythingLowering.cs` (or a name/location the implementer confirms fits this project's convention better after re-reading `StructuralContext.cs`'s and `Lower.cs`'s organization — e.g. this might belong as new methods directly on `Lower`/`StructuralContext` rather than a new file, matching how `LowerCompartment` lives on `StructuralContext` itself rather than a separate file. Decide based on the real code's existing organization, not a default assumption.)
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs` (add to existing file)

**Interfaces:**
- Consumes: Task 7's `TableExistsPredicate`/`Except`, Task 8's `VisibleSinceFilter`, the existing `CompartmentSource`/`LowerCompartment`/`Union`/`Intersect`/`ResourceSource` mechanisms.
- Produces: a working `$everything` compilation entry point — sub-project 3's adapter (out of scope here) calls it.

- [ ] **Step 1: Re-read the legacy oracle and existing compartment mechanism once more**

Before writing any code, re-read `PatientEverythingQueryGenerator.cs` in full (already quoted extensively during this plan's research, but confirm nothing drifted) and `StructuralContext.cs`'s `LowerCompartment`/`Union`/`Intersect` methods, to finalize the exact orchestration entry-point signature (what inputs does `$everything` need: patient ID, optional date range, optional `_since`, optional `IncludeReferencedResources` flag — mirror `PatientEverythingQueryGenerator`'s own real method signature for the parameter list).

- [ ] **Step 2: Write the failing end-to-end test**

Add to `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, following this file's established `Explain()`-pinned combined-proof style:

```csharp
[Fact]
public void GivenAPatientEverythingRequestWithNoOptionalFilters_WhenCompiled_ThenUnionsPatientItselfAndCompartment()
{
    // Arrange -- the minimal case: no date range, no _since, no referenced-type expansion.
    // Construct via the real orchestration entry point decided in Step 1.

    // Act

    // Assert -- Explain()-pinned: exactly 2 top-level union members (Patient-itself ResourceSource,
    // compartment CompartmentSource-derived Union), no VisibleSinceFilter/TableExistsPredicate/Except
    // present since neither optional filter was requested.
}

[Fact]
public void GivenAPatientEverythingRequestWithADateRangeAndSince_WhenCompiled_ThenComposesTheConditionalDatePredicateAndScopesSinceToTheCompartmentBranchOnly()
{
    // Arrange -- exercises items 3+4 together: the Union(matching-date, no-date-at-all) via Except,
    // and Since applied via Intersect ONLY to the compartment branch, never the Patient-itself branch.

    // Act

    // Assert -- Explain()-pinned. Critically assert the Patient-itself branch's CTE subtree contains NO
    // VisibleSinceFilter/Intersect at all (proving Since correctly does NOT apply there), while the
    // compartment branch's subtree does.
}

[Fact]
public void GivenAPatientEverythingRequestWithReferencedResourceExpansion_WhenCompiled_ThenSeedsFromTheFilteredCompartmentSet()
{
    // Arrange -- exercises item 5, seeded from the AFTER-date/since-filtering compartment set per
    // legacy's own sequencing (PatientEverythingQueryGenerator.cs Step 5 runs after Steps 3-4).

    // Act

    // Assert -- Explain()-pinned, confirms the referenced-type-expansion CTE's upstream dependency is
    // the filtered compartment CteRef, not the raw pre-filter one.
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~PatientEverything"`
Expected: FAIL (orchestration entry point doesn't exist yet).

- [ ] **Step 4: Implement the orchestration**

Compose, per the design doc's finalized Section 5, in this order:
1. Patient-itself: `context.LowerResourceSource("Patient")` (or the real helper name) with an `_id`-equality `Predicate` for the target patient.
2. Compartment: the existing `LowerCompartment` call, unchanged.
3. If a date range was supplied: build two `TableExistsPredicate`s against `DateTimeSearchParam` — one with the date-range `Predicate` (matching-date branch), one with `Predicate: null` (no-date-at-all branch) — and compose `context.Union([context.Intersect(compartmentRef, matchingDateRef), context.Except(compartmentRef, noDateRef)])`, replacing the plain compartment ref used downstream with this composed one. (Confirm `Intersect`'s real signature during Step 1's re-read — it should mirror `Except`'s shape from Task 7.)
4. If `_since` was supplied: `context.Intersect(compartmentRefFromStep3, context.VisibleSinceFilterRef(since))` — applied to the (possibly date-filtered) compartment result specifically, never to the Patient-itself branch.
5. If referenced-type expansion was requested: a new `Union`-composed CTE seeded from step 4's (or step 3's, if no `_since`) result, structurally mirroring how `_revinclude` stages are seeded — read the existing `_revinclude` lowering code once more for the exact composition shape to mirror.
6. Final: `context.Union([patientItselfRef, compartmentResultAfterAllFilters, referencedExpansionRefIfPresent])`.

Write the real method now, following whatever file/location Step 1 determined.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~PatientEverything"`
Expected: PASS. Then the full suite with no filter, both target frameworks — this is the last task in the plan, so also do a final read-through confirming no stray `TODO`/commented-out code was left anywhere across all 9 tasks' diffs.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): \$everything orchestration -- Patient-itself, compartment, conditional date, since-scoped, referenced-type expansion"
```

---

## Post-Plan

After all 9 tasks: dispatch the final whole-branch review (most capable model available, per this initiative's standing practice) covering the full diff from this plan's base commit to its tip. Update `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`'s table to record this sub-project's completion. This sub-project's own completion does not by itself unblock sub-project 3 (the SqlServer-native search adapter) — sub-project 2 (the small `Ignixa.DataLayer.SqlServer` prerequisites: `ct` rename, `SearchCompartmentHandler` fix, composition-root move, repository cleanup) is still pending, sequenced separately per the original 3-sub-project split.
