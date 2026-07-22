# Search SQL Phase 3 String Overflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make string `:contains` and `:exact` operate on the complete logical value across `Text` and `TextOverflow`.

**Architecture:** Reuse Phase 1's `IsNull`, `And`, and `Or` predicates. Lowering selects a predicate tree from catalog-derived inline width; no schema, row-generator, or composite change is required.

**Tech Stack:** C# / .NET 9+, xUnit, Shouldly, generated `SqlCatalog`.

**Prerequisite:** `2026-07-21-search-sql-phase1-terminology-resolution.md`

---

### Task 1: Pin the storage-width contract

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogTests.cs`

- [ ] **Step 1: Add the catalog invariant test**

```csharp
[Fact]
public void GivenStringSearchParam_WhenRead_ThenInlineTextWidthIs256()
{
    var table = SqlCatalog.Default.Table("StringSearchParam");
    table.Column("Text").MaxLength.ShouldBe(256);
    table.Column("TextOverflow").MaxLength.ShouldBeNull();
}
```

- [ ] **Step 2: Run the test**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SqlCatalogTests"
```

Expected: PASS; stop if generated catalog metadata disagrees because the lowering design depends on it.

- [ ] **Step 3: Commit**

```powershell
git add test\Ignixa.Search.Sql.Tests\Catalog\SqlCatalogTests.cs
git commit -m "Pin string overflow catalog width"
```

### Task 2: Implement exact matching at every width

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/StringLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/StringLoweringRuleTests.cs`

- [ ] **Step 1: Replace the 256-character throw test**

Add exact predicate assertions for lengths 255, 256, and 257. At 255/256 require:

```csharp
new Predicate.And(
    new Predicate.IsNull(new SqlColumnRef("StringSearchParam", "TextOverflow")),
    new Predicate.Equal(
        new SqlColumnRef("StringSearchParam", "Text"),
        context.Parameter(value),
        "Latin1_General_100_CS_AS"))
```

At 257 require equality on `TextOverflow` only.

- [ ] **Step 2: Run the tests and verify the 256 case throws**

- [ ] **Step 3: Implement exact-width selection**

Create explicit `textColumn` and `overflowColumn` refs. Remove the boundary throw. For every exact value with `Length <= inlineWidth`, return `IsNull(TextOverflow) AND Text = value`; for longer values return `TextOverflow = value`.

- [ ] **Step 4: Run string lowering tests**

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering\Leaf\StringLoweringRule.cs test\Ignixa.Search.Sql.Tests\Lowering\StringLoweringRuleTests.cs
git commit -m "Match exact strings across overflow storage"
```

### Task 3: Implement contains matching across both columns

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/StringLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/StringLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitSqlGrammarTests.cs`

- [ ] **Step 1: Replace the inline contains throw test**

For a short value require this exact tree:

```csharp
new Predicate.Or(
    new Predicate.And(
        new Predicate.IsNull(overflowColumn),
        new Predicate.Like(textColumn, context.Parameter(value), LikeMatch.Contains, CaseInsensitiveCollation)),
    new Predicate.Like(overflowColumn, context.Parameter(value), LikeMatch.Contains, CaseInsensitiveCollation))
```

Add lengths 255, 256, and 257 plus `%`, `_`, `[`, `\`, case, and accent values. Length 257 must target only `TextOverflow`.

- [ ] **Step 2: Add exact SQL and grammar tests**

Require fully parenthesized SQL:

```sql
((TextOverflow IS NULL AND Text COLLATE Latin1_General_100_CI_AI LIKE @p0 ESCAPE '\')
 OR TextOverflow COLLATE Latin1_General_100_CI_AI LIKE @p1 ESCAPE '\')
```

Assert both parameters contain the same escaped search value and preserve left-to-right numbering.

- [ ] **Step 3: Run tests and verify the current throw**

- [ ] **Step 4: Implement the dual-column contains branch**

Remove the throw and build the tree above when `Length <= inlineWidth`; retain the existing single overflow-column LIKE for longer values.

- [ ] **Step 5: Run lowering, emitter, and grammar tests**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~StringLoweringRuleTests|FullyQualifiedName~EmitTests|FullyQualifiedName~EmitSqlGrammarTests"
```

- [ ] **Step 6: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering\Leaf\StringLoweringRule.cs test\Ignixa.Search.Sql.Tests
git commit -m "Search complete strings with contains"
```

### Task 4: Prove parser-to-SQL behavior and update documentation

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`
- Modify: `src/Core/Ignixa.Search.Sql/README.md`

- [ ] **Step 1: Add end-to-end golden tests**

Compile `name:exact` at 256 characters and `name:contains` with a short value. Pin full plan, SQL, two-parameter contains binding, and the overflow null guard.

- [ ] **Step 2: Run the full compiler suite**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj
```

- [ ] **Step 3: Update the README**

Remove the string overflow gap bullet and document complete-value matching, collations, and the inline/overflow predicate shape.

- [ ] **Step 4: Build and run non-E2E tests**

```powershell
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\README.md test\Ignixa.Search.Sql.Tests\EndToEndCompilationTests.cs
git commit -m "Document complete string matching"
```
