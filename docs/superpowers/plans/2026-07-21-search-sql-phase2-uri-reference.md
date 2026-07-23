# Search SQL Phase 2 URI and Reference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compile URI hierarchy modifiers and make local/absolute reference searches distinguish `BaseUri` correctly.

**Architecture:** Phase 1's `IsNull` predicate scopes local references. A new `PrefixOfParameter` AST predicate represents reverse URI ancestry without treating stored URI characters as SQL wildcard syntax; one shared reference predicate helper keeps leaf and reference-token composite behavior identical.

**Tech Stack:** C# / .NET 9+, xUnit, Shouldly, Ignixa.Search.Sql AST/lowering.

**Prerequisite:** `2026-07-21-search-sql-phase1-terminology-resolution.md`

---

### Task 1: Add reverse-prefix predicate support

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Predicate.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs:577-606`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs:194-205`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitSqlGrammarTests.cs`

- [ ] **Step 1: Write failing exact-output and grammar tests**

Create:

```csharp
new Predicate.PrefixOfParameter(
    new SqlColumnRef("UriSearchParam", "Uri"),
    new SqlParameterRef("http://example.org/fhir/Patient/123"),
    "Latin1_General_100_BIN2")
```

Require SQL equivalent to:

```sql
LEFT(@p0, LEN(Uri)) COLLATE Latin1_General_100_BIN2 = Uri
```

and explainer text `Uri PREFIX_OF @p0 collate Latin1_General_100_BIN2`.

- [ ] **Step 2: Run AST tests and verify the type is missing**

- [ ] **Step 3: Add the AST record and renderer cases**

```csharp
public sealed record PrefixOfParameter(
    SqlColumnRef Column,
    SqlParameterRef Value,
    string? Collation = null) : Predicate;
```

The emitter binds `Value` through `EmitParam`; it must not inline the URI or build a `LIKE` pattern from the stored column. The explainer consumes one parameter ordinal.

- [ ] **Step 4: Run AST and SQL grammar tests**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmitTests|FullyQualifiedName~PlanExplainerTests|FullyQualifiedName~EmitSqlGrammarTests"
```

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Ast src\Core\Ignixa.Search.Sql\Builders test\Ignixa.Search.Sql.Tests\Ast
git commit -m "Add reverse URI prefix predicate"
```

### Task 2: Lower URI hierarchy modifiers

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/UriLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/UriLoweringRuleTests.cs`

- [ ] **Step 1: Replace throw tests with exact predicate tests**

Test plain equality, `:below`, `:above`, differing case, near-prefix values, and URI values containing `%`, `_`, `[`, and `\`.

- [ ] **Step 2: Run tests and verify `:above`/`:below` throw**

- [ ] **Step 3: Implement the modifier switch**

```csharp
const string BinaryCollation = "Latin1_General_100_BIN2";
Predicate predicateExpr = predicate.Modifier?.SearchModifierCode switch
{
    null => new Predicate.Equal(column, context.Parameter(value.Uri), BinaryCollation),
    SearchModifierCode.Below => new Predicate.Like(
        column, context.Parameter(value.Uri), LikeMatch.StartsWith, BinaryCollation),
    SearchModifierCode.Above => new Predicate.PrefixOfParameter(
        column, context.Parameter(value.Uri), BinaryCollation),
    var modifier => throw new NotSupportedException(
        $"Uri search does not support the ':{modifier}' modifier."),
};
```

- [ ] **Step 4: Run URI tests**

Expected: PASS; `:below` uses escaped LIKE parameters and `:above` binds the full search URI once.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering\Leaf\UriLoweringRule.cs test\Ignixa.Search.Sql.Tests\Lowering\UriLoweringRuleTests.cs
git commit -m "Lower URI hierarchy modifiers"
```

### Task 3: Share BaseUri-correct reference lowering

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/ReferenceColumnEquality.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/ReferenceLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Composite/ReferenceTokenLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ReferenceLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ReferenceTokenLoweringRuleTests.cs`

- [ ] **Step 1: Write the local/external behavior matrix**

Require:

```text
local typed:   BaseUri IS NULL AND TypeId = @p0 AND Id = @p1
local untyped: BaseUri IS NULL AND Id = @p0
external:      BaseUri = @p0 COLLATE BIN2 AND TypeId = @p1 AND Id = @p2
```

Repeat external/local coverage for the reference-token composite's `BaseUri1`, `ReferenceResourceTypeId1`, and `ReferenceResourceId1`.

- [ ] **Step 2: Run tests and verify external references throw**

- [ ] **Step 3: Add a configurable shared helper**

```csharp
internal static Predicate Build(
    TableDescriptor table,
    string baseUriColumn,
    string resourceTypeColumn,
    string resourceIdColumn,
    ReferenceSearchValue value,
    LeafContext context)
```

Build the base predicate as `IsNull` when `BaseUri` is null, otherwise `Equal` against `value.BaseUri.ToString()` with `Latin1_General_100_BIN2`. Conjoin the optional resolved resource type and required resource ID in stable left-to-right parameter order.

- [ ] **Step 4: Route both lowerers through the helper**

Delete both duplicated BaseUri throws. Preserve chain/include SQL's existing hard-coded `rsp.BaseUri IS NULL`; this phase enables external leaf matching, not external graph traversal.

- [ ] **Step 5: Run reference tests**

Expected: PASS with identical semantics across leaf and composite rules.

- [ ] **Step 6: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering test\Ignixa.Search.Sql.Tests\Lowering
git commit -m "Match local and external reference identities"
```

### Task 4: Prove end-to-end SQL and document support

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`
- Modify: `src/Core/Ignixa.Search.Sql/README.md`

- [ ] **Step 1: Add end-to-end golden cases**

Compile one `uri:below`, one `uri:above`, one local reference, and two external references sharing type/ID but differing base. Pin full SQL, collation, and parameter order.

- [ ] **Step 2: Run the compiler suite**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj
```

- [ ] **Step 3: Update the README**

Move URI hierarchy and absolute/external reference matching into the support matrix. State that chains/includes remain local-reference traversal and that reference versions are not constrained.

- [ ] **Step 4: Build and run non-E2E tests**

```powershell
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\README.md test\Ignixa.Search.Sql.Tests\EndToEndCompilationTests.cs
git commit -m "Document URI and external reference support"
```
