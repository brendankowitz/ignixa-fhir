# Search SQL Phase 1 Terminology Resolution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve token systems and quantity codes before lowering, then compile every qualified leaf and composite search without dropping constraints.

**Architecture:** `Resolve` collects and resolves terminology strings into nullable ID maps on `SymbolTable`; a present key with a null value means the lookup was valid but cannot match indexed rows. Table-local predicate helpers translate those outcomes into `IsNull`, equality, or an explicit `False` predicate while preserving `Lower` and `SqlBuilder` purity.

**Tech Stack:** C# / .NET 9+, EF Core, xUnit, Shouldly, Ignixa.Search.Sql AST/lowering.

**Design:** `docs/superpowers/specs/2026-07-21-search-sql-readme-gaps-design.md`

---

### Task 1: Add null and false predicate shapes

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Predicate.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs:577-587`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs:194-205`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

- [ ] **Step 1: Write failing exact-output tests**

Add plans containing `new Predicate.IsNull(column)` and `new Predicate.False()` to `EmitTests`; require `SystemId IS NULL` and `1 = 0`, with no bound parameter. Add matching explainer tests requiring `SystemId IS NULL` and `false`.

- [ ] **Step 2: Run the focused tests and verify the new types do not compile**

Run:

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~EmitTests|FullyQualifiedName~PlanExplainerTests"
```

Expected: FAIL because `Predicate.IsNull` and `Predicate.False` do not exist.

- [ ] **Step 3: Add and render the predicates**

```csharp
public sealed record IsNull(SqlColumnRef Column) : Predicate;

public sealed record False : Predicate;
```

Add these switch arms in both renderers:

```csharp
Predicate.IsNull isNull => $"{isNull.Column.Column} IS NULL",
Predicate.False => "1 = 0",
```

- [ ] **Step 4: Run the focused tests**

Expected: PASS with zero parameters for both new predicate shapes.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Ast\Predicate.cs src\Core\Ignixa.Search.Sql\Builders\SqlBuilder.cs src\Core\Ignixa.Search.Sql\Ast\PlanExplainer.cs test\Ignixa.Search.Sql.Tests\Ast
git commit -m "Add null and false SQL predicates"
```

### Task 2: Add read-only terminology lookup APIs

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/ISymbolResolver.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/SearchIndexReferenceDataCache.cs:167-289`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSymbolResolver.cs`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/SearchIndexReferenceDataCacheTests.cs`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/SqlEntityFrameworkSymbolResolverTests.cs`

- [ ] **Step 1: Add failing cache tests**

Using the existing context/cache fixture, seed one `SystemEntity` and one `QuantityCodeEntity`, then assert:

```csharp
(await cache.GetSystemIdAsync("http://loinc.org")).ShouldBe(systemId);
(await cache.GetSystemIdAsync("http://unknown.example")).ShouldBeNull();
(await cache.GetQuantityCodeIdAsync("mg")).ShouldBe(quantityCodeId);
(await cache.GetQuantityCodeIdAsync("unknown")).ShouldBeNull();
```

Assert the unknown lookups do not add entities to either DbSet.

- [ ] **Step 2: Run the cache tests**

Run:

```powershell
dotnet test test\Ignixa.DataLayer.SqlEntityFramework.Tests\Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchIndexReferenceDataCacheTests"
```

Expected: FAIL because the read-only methods do not exist.

- [ ] **Step 3: Implement read-only methods**

Add `GetSystemIdAsync(string?)` and `GetQuantityCodeIdAsync(string?)` beside their `GetOrCreate` counterparts. Reuse `_systemCache`, `_quantityCodeCache`, and `_dbLock`; cache positive database results and return null on a miss. Do not put a negative sentinel in these shared caches: `GetOrCreateSystemIdAsync` and `GetOrCreateQuantityCodeIdAsync` treat every cached integer as a real ID, so caching `-1` would corrupt the write path. Do not call `SaveChangesAsync` or either `GetOrCreate` method. The per-compilation `SymbolTable` records known misses.

- [ ] **Step 4: Extend the resolver contract and adapter**

```csharp
Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken);
Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken);
```

The adapter methods call `cancellationToken.ThrowIfCancellationRequested()` and delegate to the new cache methods.

- [ ] **Step 5: Update every `ISymbolResolver` test double**

Update the eight implementations found by `rg ": ISymbolResolver" --glob "*.cs"` in:

```text
test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
test/Ignixa.Search.Sql.Tests/Tracing/SearchTraceFixtures.cs
test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs
test/Ignixa.Search.Sql.Tests/Symbols/ResolvedSymbolsTests.cs
test/Ignixa.Application.Tests/Search/Parsing/SearchTraceRealParserTests.cs
test/Ignixa.Application.Tests/Search/Parsing/SearchTraceImplicitParameterTests.cs
```

Use dictionary-backed methods where the fake is configurable:

```csharp
public Dictionary<string, int> SystemIds { get; } = [];
public Dictionary<string, int> QuantityCodeIds { get; } = [];

public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
    => Task.FromResult(SystemIds.TryGetValue(system, out var id) ? (int?)id : null);

public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
    => Task.FromResult(QuantityCodeIds.TryGetValue(code, out var id) ? (int?)id : null);
```

Null/always-resolving fakes return null or a fixed ID consistently with their existing behavior.

- [ ] **Step 6: Run cache, resolver, and compile tests**

Run:

```powershell
dotnet test test\Ignixa.DataLayer.SqlEntityFramework.Tests\Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchIndexReferenceDataCacheTests"
dotnet test test\Ignixa.DataLayer.SqlEntityFramework.IntegrationTests\Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj --filter "FullyQualifiedName~SqlEntityFrameworkSymbolResolverTests"
dotnet build All.sln
```

Expected: PASS; all resolver implementations compile.

- [ ] **Step 7: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Symbols\ISymbolResolver.cs src\DataLayer\Ignixa.DataLayer.SqlEntityFramework test
git commit -m "Resolve terminology IDs without creating rows"
```

### Task 3: Collect terminology and preserve known misses

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolTable.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/SymbolTableTests.cs`

- [ ] **Step 1: Write three-state and collection tests**

Add tests proving:

```csharp
symbols.SystemId("http://loinc.org").ShouldBe(7);
symbols.SystemId("http://known-but-missing.example").ShouldBeNull();
Should.Throw<KeyNotFoundException>(() => symbols.SystemId("http://never-collected.example"));
```

Mirror this for quantity codes. In `ResolveTests`, build the existing token-quantity composite, configure IDs, and assert both systems plus `mg` resolve. Add call counters to the fake and use duplicate values to assert one resolver call per distinct string.

- [ ] **Step 2: Run the symbol tests**

Expected: FAIL because terminology maps and collector sets do not exist.

- [ ] **Step 3: Extend collection and symbol storage**

Add ordinal `HashSet<string>` collections to `SymbolCollectingVisitor`. In `VisitSearchParameterPredicate`, collect non-empty `TokenSearchValue.System`, and non-empty `QuantitySearchValue.System`/`Code`.

Extend `SymbolTable` without changing the meaning of the existing third positional constructor argument:

```csharp
public SymbolTable(
    IReadOnlyDictionary<string, short> searchParamIds,
    IReadOnlyDictionary<string, short> resourceTypeIds,
    IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? compartmentMembership = null,
    IReadOnlyDictionary<string, int?>? systemIds = null,
    IReadOnlyDictionary<string, int?>? quantityCodeIds = null)
```

`SystemId` and `QuantityCodeId` return the nullable stored value when the key exists and throw `KeyNotFoundException` when it does not. Add delegating nullable methods to `LeafContext`.

- [ ] **Step 4: Resolve every distinct value once**

In `Resolve.RunAsync`, build `Dictionary<string, int?>` maps from the collector sets, storing null resolver results as entries. Pass both maps after `compartmentMembership` when constructing `SymbolTable`.

- [ ] **Step 5: Run symbol and existing trace tests**

Run:

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SymbolTableTests|FullyQualifiedName~ResolveTests|FullyQualifiedName~SearchTrace"
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Symbols src\Core\Ignixa.Search.Sql\Lowering\LeafContext.cs test\Ignixa.Search.Sql.Tests\Symbols
git commit -m "Collect terminology symbols before lowering"
```

### Task 4: Lower qualified token predicates

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/TokenColumnEquality.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/TokenLoweringRule.cs`
- Modify: all token-bearing rules under `src/Core/Ignixa.Search.Sql/Lowering/Composite/`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenLoweringRuleTests.cs`
- Test: token composite test files under `test/Ignixa.Search.Sql.Tests/Lowering/`

- [ ] **Step 1: Replace rejection tests with a behavior matrix**

Pin predicate trees for bare `code`, `|code`, `system|`, `system|code`, and an unknown system. Add at least one qualified-token case to each of the six composite rule test classes.

- [ ] **Step 2: Run token tests and verify the current throws**

Run:

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~TokenLoweringRuleTests|FullyQualifiedName~TokenTokenLoweringRuleTests|FullyQualifiedName~TokenStringLoweringRuleTests|FullyQualifiedName~TokenNumberNumberLoweringRuleTests|FullyQualifiedName~TokenDateTimeLoweringRuleTests|FullyQualifiedName~ReferenceTokenLoweringRuleTests"
```

- [ ] **Step 3: Generalize the shared token helper**

Change `TokenColumnEquality.Build` to accept both system and code column names. Its core shape is:

```csharp
int? systemId = value.System is { Length: > 0 } system ? context.SystemId(system) : null;
if (value.System is { Length: > 0 } && systemId is null)
{
    return new Predicate.False();
}

Predicate? systemPredicate = value.System switch
{
    null => null,
    "" => new Predicate.IsNull(new SqlColumnRef(table.TableName, systemColumn)),
    _ => new Predicate.Equal(new SqlColumnRef(table.TableName, systemColumn), context.Parameter(systemId!.Value)),
};

Predicate? codePredicate = string.IsNullOrEmpty(value.Code)
    ? null
    : new Predicate.Equal(new SqlColumnRef(table.TableName, codeColumn), context.Parameter(value.Code));

return (systemPredicate, codePredicate) switch
{
    ({ } systemOnly, null) => systemOnly,
    (null, { } codeOnly) => codeOnly,
    ({ } systemPart, { } codePart) => new Predicate.And(systemPart, codePart),
    _ => throw new NotSupportedException("Token search requires a system or code; display text is not a code."),
};
```

Use `SystemId`/`Code` for the leaf, `SystemId1`/`Code1` or `SystemId2`/`Code2` for composites. Update every call site; do not leave a token slot on the old code-only signature.

- [ ] **Step 4: Run the token matrix**

Expected: PASS with exact AST assertions and no dropped system constraint.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering test\Ignixa.Search.Sql.Tests\Lowering
git commit -m "Lower system-qualified token searches"
```

### Task 5: Lower quantity identity in leaves and composites

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/QuantityColumnPredicate.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Leaf/QuantityLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Composite/TokenQuantityLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/QuantityLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenQuantityLoweringRuleTests.cs`

- [ ] **Step 1: Write leaf and composite identity tests**

Cover value-only, system-only, code-only (`value||code`), both, unknown system, and unknown code. Assert unknown values produce `Predicate.False`, not an exception.

- [ ] **Step 2: Run tests and verify the current rejection**

- [ ] **Step 3: Add one shared quantity predicate builder**

The helper takes table/column names, the comparator, value, and `LeafContext`. Build the numeric predicate with `NumericRangeComparison.Build`; resolve each non-empty identity value. Return `False` immediately on a known miss, otherwise conjoin:

```csharp
Predicate result = numericPredicate;
if (systemId is { } resolvedSystem)
{
    result = new Predicate.And(result,
        new Predicate.Equal(new SqlColumnRef(table.TableName, systemColumn), context.Parameter(resolvedSystem)));
}

if (quantityCodeId is { } resolvedCode)
{
    result = new Predicate.And(result,
        new Predicate.Equal(new SqlColumnRef(table.TableName, codeColumn), context.Parameter(resolvedCode)));
}
```

Use `SystemId`/`QuantityCodeId` for the leaf and `SystemId2`/`QuantityCodeId2` for token-quantity.

- [ ] **Step 4: Run quantity and full composite tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\Lowering test\Ignixa.Search.Sql.Tests\Lowering
git commit -m "Lower quantity system and code constraints"
```

### Task 6: Prove the full pipeline and update documentation

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`
- Modify: `src/Core/Ignixa.Search.Sql/README.md`

- [ ] **Step 1: Add end-to-end Resolve-Lower-Emit tests**

Compile one `system|code`, one `|code`, one quantity with both IDs, one qualified token composite, and one unknown-system query. Pin complete SQL and parameter order; the unknown query must contain `1 = 0`.

- [ ] **Step 2: Run the compiler and affected DataLayer suites**

```powershell
dotnet test test\Ignixa.Search.Sql.Tests\Ignixa.Search.Sql.Tests.csproj
dotnet test test\Ignixa.DataLayer.SqlEntityFramework.Tests\Ignixa.DataLayer.LegacySqlEF.Tests.csproj
dotnet test test\Ignixa.Application.Tests\Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchTrace"
```

- [ ] **Step 3: Update the README**

Move system-qualified token and quantity system/code matching into the support matrix and remove their two gap bullets. Document that unknown lookup values compile to an empty match.

- [ ] **Step 4: Build the solution**

```powershell
dotnet build All.sln
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

Expected: zero warnings/errors and all non-E2E tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src\Core\Ignixa.Search.Sql\README.md test\Ignixa.Search.Sql.Tests\EndToEndCompilationTests.cs
git commit -m "Document qualified token and quantity support"
```
