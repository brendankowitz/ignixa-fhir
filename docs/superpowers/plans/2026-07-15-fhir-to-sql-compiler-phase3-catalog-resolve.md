# Phase 3 — `Ignixa.Search.Sql` Skeleton, `SqlCatalog`, `SymbolTable`, `Resolve` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the new `Ignixa.Search.Sql` project and build its first, I/O-only stage: `Resolve`, which turns Phase 2's typed predicate tree into an immutable `SymbolTable` (real `SearchParamId`/`ResourceTypeId` values), backed by a `SqlCatalog` describing the real search-index schema.

**Architecture:** Per `docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md`'s three-stage pipeline (`Resolve → Lower → Emit`), this phase builds only `Resolve` and its two inputs. `Lower`/`Plan`/`Ast`/`Emit` — and the `Compile()` entry point that ties everything together — are explicitly out of scope; they don't exist until Phases 4-5. `Ignixa.Search.Sql` has zero EF/ASP.NET references (a hard constraint), so `Resolve`'s actual database I/O happens through `ISymbolResolver`, an interface this project defines and `Ignixa.DataLayer.SqlEntityFramework` implements — dependency inversion, not a direct reference. The DataLayer's existing `SearchIndexReferenceDataCache` has the right *behavior* (preload once, cache, sync lookup) but is too EF-coupled to reuse directly; its new adapter wraps it rather than replacing it.

**Tech Stack:** `net9.0;net10.0` (matches `Ignixa.Search.csproj`, confirmed still accurate), `Nullable=enable` (deliberately unlike `Ignixa.Search`'s `disable` — a new project shouldn't inherit that debt, per the original design doc). xUnit + Shouldly for tests, matching repo convention.

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- `Ignixa.Search.Sql.csproj` must have **no** `Microsoft.EntityFrameworkCore*`, no `Microsoft.AspNetCore.*` package or project references — verify this after every task that touches it.
- `SqlCatalog`'s data must be transcribed from the real DDL (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql`), read directly before writing any catalog entry — do not trust paraphrased column lists from prior research without re-confirming against the actual script.
- This phase populates `SqlCatalog` with only the tables Phase 3's own tests need to prove `Resolve` works (`StringSearchParam`, `TokenSearchParam`, `ReferenceSearchParam`, plus the `ResourceType`/`SearchParam` lookup tables) — do not attempt to populate all 13 leaf types' catalog data now. That's Phase 5's job, sized in the original design doc at ~150-250 lines of catalog role-mapping alone; populating it before `Lower` exists to consume it would be premature and untested.
- `ColumnDescriptor`/`TableDescriptor` should describe only what this phase needs (name, SQL type, max length, collation, nullability) — do not add the role-based composite column-addressing machinery (`ColumnRole`, `ctx.Column(ColumnRole.TokenCode)`) the design doc describes for composites. That's Phase 5 machinery with no consumer yet; adding it now means guessing its shape without the tier-1 leaf rules that would actually exercise it.
- Follow repo convention: file-scoped namespaces, AAA test structure, `GivenContext_WhenAction_ThenResult` naming, no `#region`, one type per file.

---

### Task 1: `Ignixa.Search.Sql` project skeleton

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`
- Create: `src/Core/Ignixa.Search.Sql/Symbols/` (empty directory, populated in Task 3-4)
- Create: `src/Core/Ignixa.Search.Sql/Catalog/` (empty directory, populated in Task 2)
- Create: `test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
- Modify: `All.sln`

**Interfaces:**
- Consumes: nothing.
- Produces: an empty, correctly-configured project every later task adds real code to.

- [ ] **Step 1: Read `Ignixa.Search.csproj` for the real convention to mirror**

```bash
cat src/Core/Ignixa.Search/Ignixa.Search.csproj
```

Confirm `TargetFrameworks` is `net9.0;net10.0`, note the `IsPackable`/`PackageStability`/package-metadata block shape exactly (property names, values) — this phase's new csproj copies that structure with two deliberate differences: `Nullable` flips to `enable`, and there are no EF/AspNetCore package or project references.

- [ ] **Step 2: Create the project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <IsPackable>true</IsPackable>
  </PropertyGroup>

  <!-- Copy the remaining PackageId/Authors/Description/PackageStability/etc. properties from
       Ignixa.Search.csproj's PropertyGroup verbatim, adjusting PackageId and Description to name
       this package. Do not add any PackageReference to Microsoft.EntityFrameworkCore* or
       Microsoft.AspNetCore.* -- this project has none, by design (see design doc). -->

</Project>
```

Fill in the exact PackageId/Authors/Description/version-related properties by copying `Ignixa.Search.csproj`'s pattern from Step 1 — do not invent new metadata conventions.

- [ ] **Step 3: Create empty `Symbols/` and `Catalog/` directories**

No files in them yet — an empty directory won't be tracked by git; add a `.gitkeep` only if `dotnet build` fails without at least one file present (it usually doesn't matter, since Task 2/3 add real files immediately after). Skip the `.gitkeep` unless you hit an actual problem.

- [ ] **Step 4: Create the test project**

```bash
dotnet new xunit -n Ignixa.Search.Sql.Tests -o test/Ignixa.Search.Sql.Tests
```

Add a `ProjectReference` to `Ignixa.Search.Sql.csproj`. Remove the template's default `UnitTest1.cs`. Match package reference versions to `Directory.Packages.props` (central package management — no explicit `Version` attributes), matching the pattern every other test project in this repo already uses.

- [ ] **Step 5: Register both projects in `All.sln`**

```bash
dotnet sln All.sln add src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
dotnet sln All.sln add test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj
```

- [ ] **Step 6: Build and verify no EF/ASP.NET reference exists**

```bash
dotnet build All.sln --nologo
grep -i "EntityFrameworkCore\|AspNetCore" src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```

**Expected:** 0 warnings, 0 errors. The grep returns nothing.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): scaffold the Ignixa.Search.Sql project

Empty skeleton -- no compiler logic yet. net9.0;net10.0, Nullable=enable
(deliberately unlike Ignixa.Search's disable), no EF/ASP.NET references.
Symbols/ and Catalog/ populated in tasks 2-4 of this plan."
```

---

### Task 2: `SqlCatalog`, `TableDescriptor`, `ColumnDescriptor`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Catalog/ColumnDescriptor.cs`
- Create: `src/Core/Ignixa.Search.Sql/Catalog/TableDescriptor.cs`
- Create: `src/Core/Ignixa.Search.Sql/Catalog/SqlCatalog.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `SqlCatalog.Default` (or equivalent), a lookup from table name → `TableDescriptor`, used by Task 4's `Resolve` (indirectly, via `ISymbolResolver`'s DataLayer-side implementation in Task 5) and by every later phase's `Lower` tier-1 rules.

- [ ] **Step 1: Read the real DDL before transcribing anything**

```bash
grep -n "CREATE TABLE dbo.StringSearchParam\|CREATE TABLE dbo.TokenSearchParam\|CREATE TABLE dbo.ReferenceSearchParam\|CREATE TABLE dbo.ResourceType\|CREATE TABLE dbo.SearchParam" -A 15 src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql
```

Record the exact column names, SQL types, max lengths, collations, and nullability for `StringSearchParam`, `TokenSearchParam`, `ReferenceSearchParam`, `ResourceType`, and `SearchParam`. Do not trust any prior summary of these — read the actual `CREATE TABLE` statements now. If a column's collation isn't explicit in the DDL (inherits database default), record that explicitly rather than guessing a specific collation name.

- [ ] **Step 2: Write `ColumnDescriptor`**

```csharp
namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes one column's schema-derived facts -- name, SQL type, length, collation, nullability --
/// as the DDL states them. Does not describe storage convention (see
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md, "Lower owns storage convention").
/// </summary>
public sealed record ColumnDescriptor(
    string Name,
    string SqlType,
    int? MaxLength,
    string? Collation,
    bool IsNullable);
```

- [ ] **Step 3: Write `TableDescriptor`**

```csharp
namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes one search-index table's schema, as the real DDL in
/// src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql states it.
/// </summary>
public sealed record TableDescriptor(
    string SchemaName,
    string TableName,
    IReadOnlyList<ColumnDescriptor> Columns)
{
    public ColumnDescriptor Column(string name)
        => Columns.FirstOrDefault(c => c.Name == name)
           ?? throw new KeyNotFoundException($"Table {SchemaName}.{TableName} has no column named '{name}'.");
}
```

- [ ] **Step 4: Write `SqlCatalog`, populated with the tables read in Step 1**

```csharp
namespace Ignixa.Search.Sql.Catalog;

/// <summary>
/// Describes the tables and columns this compiler emits SQL against, as the real DDL states them.
/// Deliberately does not describe storage convention (e.g. which column an overflowing string
/// lands in) -- that is Lower's job, encoded as a rule, not a catalog fact. See
/// docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md.
/// </summary>
public sealed class SqlCatalog
{
    private readonly IReadOnlyDictionary<string, TableDescriptor> _tables;

    private SqlCatalog(IReadOnlyDictionary<string, TableDescriptor> tables)
    {
        _tables = tables;
    }

    public TableDescriptor Table(string name)
        => _tables.TryGetValue(name, out var table)
           ? table
           : throw new KeyNotFoundException($"SqlCatalog has no table named '{name}'.");

    /// <summary>
    /// The catalog for this phase's known tables. Populated from Task 2 Step 1's real DDL read --
    /// intentionally covers only StringSearchParam/TokenSearchParam/ReferenceSearchParam plus the
    /// ResourceType/SearchParam lookup tables; the remaining 10 leaf types are Phase 5's job.
    /// </summary>
    public static SqlCatalog Default { get; } = Build();

    private static SqlCatalog Build()
    {
        // Fill in each TableDescriptor using the EXACT column facts recorded in Task 2 Step 1 --
        // do not approximate. Example shape for one table (StringSearchParam), to be corrected
        // against the real DDL read, not used as-is if it disagrees with what Step 1 found:
        var stringSearchParam = new TableDescriptor("dbo", "StringSearchParam",
        [
            new ColumnDescriptor("ResourceTypeId", "smallint", null, null, false),
            new ColumnDescriptor("ResourceSurrogateId", "bigint", null, null, false),
            new ColumnDescriptor("SearchParamId", "smallint", null, null, false),
            new ColumnDescriptor("Text", "nvarchar", 256, "Latin1_General_100_CI_AI_SC", false),
            new ColumnDescriptor("TextOverflow", "nvarchar", null, "Latin1_General_100_CI_AI_SC", true),
            new ColumnDescriptor("IsMin", "bit", null, null, false),
            new ColumnDescriptor("IsMax", "bit", null, null, false),
        ]);

        // ... TokenSearchParam, ReferenceSearchParam, ResourceType, SearchParam similarly, each
        // verified against the real DDL, not invented ...

        var tables = new Dictionary<string, TableDescriptor>
        {
            [stringSearchParam.TableName] = stringSearchParam,
            // ... the rest ...
        };

        return new SqlCatalog(tables);
    }
}
```

The `Build()` method's actual content must match Step 1's real DDL findings exactly — the sketch above is a shape template, not verified data (deliberately, since this plan was written before Step 1 ran). If any column's real max length, type, or collation differs from a guess anywhere in this plan, the real DDL wins.

- [ ] **Step 5: Write tests asserting the catalog matches the real schema**

```csharp
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Catalog;

public class SqlCatalogTests
{
    [Fact]
    public void GivenStringSearchParam_WhenLookedUp_ThenTextColumnMatchesRealDdl()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act
        var table = catalog.Table("StringSearchParam");
        var text = table.Column("Text");

        // Assert
        text.SqlType.ShouldBe("nvarchar");
        text.MaxLength.ShouldBe(256);
        text.IsNullable.ShouldBeFalse();
    }

    [Fact]
    public void GivenAnUnknownTable_WhenLookedUp_ThenThrows()
    {
        // Arrange
        var catalog = SqlCatalog.Default;

        // Act & Assert
        Should.Throw<KeyNotFoundException>(() => catalog.Table("NotARealTable"));
    }
}
```

Add one assertion per table populated in Step 4, checking at least one column's real facts (max length, collation, nullability) — enough to catch a transcription error, not exhaustive column-by-column coverage.

- [ ] **Step 6: Build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~SqlCatalogTests" --nologo
```

**Expected:** 0 warnings, 0 errors, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add SqlCatalog, TableDescriptor, ColumnDescriptor

Schema facts (name, type, length, collation, nullability) transcribed
directly from Resources/97.sql's real DDL -- not storage convention, which
stays Lower's job per the design doc. Covers StringSearchParam/
TokenSearchParam/ReferenceSearchParam/ResourceType/SearchParam only; the
remaining leaf types are populated in phase 5 when Lower's tier-1 rules
exist to consume them."
```

---

### Task 3: `ISymbolResolver` and `SymbolTable`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Symbols/ISymbolResolver.cs`
- Create: `src/Core/Ignixa.Search.Sql/Symbols/SymbolTable.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/SymbolTableTests.cs`

**Interfaces:**
- Consumes: `SearchParameterInfo` (`Ignixa.Search.Models`, already a project reference via... actually check: does `Ignixa.Search.Sql` need a `ProjectReference` to `Ignixa.Search` for this type? Yes — add it in Step 1 if not already present from Task 1.
- Produces: `ISymbolResolver` (the I/O contract Task 5's DataLayer adapter implements), `SymbolTable` (the immutable resolved snapshot Task 4's `Resolve` builds and Phase 4+'s `Lower`/`Emit` consume).

- [ ] **Step 1: Add the `Ignixa.Search` project reference if not already present**

```bash
grep -n "ProjectReference" src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```

If it doesn't reference `Ignixa.Search.csproj`, add it — `SymbolTable`/`ISymbolResolver` need `SearchParameterInfo`.

- [ ] **Step 2: Write `ISymbolResolver`**

```csharp
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The compiler's only I/O seam. Resolves search-parameter and resource-type identity to the
/// integer surrogate keys the search-index schema actually stores. Implemented by the data layer
/// (e.g. Ignixa.DataLayer.SqlEntityFramework) -- this project has no EF/ASP.NET reference and does
/// no I/O of its own. See docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md, "Resolve".
/// </summary>
public interface ISymbolResolver
{
    /// <summary>
    /// Resolves a search parameter's SearchParamId. Returns null if the parameter has no catalog
    /// row (e.g. an override URL that hasn't been indexed) -- callers decide whether that's an
    /// error or an empty-result case, this method does not throw for "not found."
    /// </summary>
    Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a FHIR resource type name (e.g. "Patient") to its ResourceTypeId.
    /// </summary>
    Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write `SymbolTable`**

```csharp
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// An immutable snapshot of resolved SearchParamId/ResourceTypeId values, built once by Resolve
/// before Lower/Emit run. Lower and Emit are pure, synchronous functions of (IR, SymbolTable,
/// SqlCatalog) -- this type is what makes that true; all I/O happened before it was constructed.
/// </summary>
public sealed class SymbolTable
{
    private readonly IReadOnlyDictionary<string, short> _searchParamIds;
    private readonly IReadOnlyDictionary<string, short> _resourceTypeIds;

    public SymbolTable(
        IReadOnlyDictionary<string, short> searchParamIds,
        IReadOnlyDictionary<string, short> resourceTypeIds)
    {
        _searchParamIds = searchParamIds;
        _resourceTypeIds = resourceTypeIds;
    }

    /// <summary>
    /// Looks up a search parameter's SearchParamId. Throws if Resolve did not resolve this
    /// parameter -- by the time Lower runs, every parameter the IR actually references must
    /// already be in the table; a miss here means Resolve's tree-walk (task 4) missed a node kind,
    /// not a legitimate runtime "not found" case (that's ISymbolResolver's nullable return, handled
    /// during Resolve, before this table is ever handed to Lower).
    /// </summary>
    public short SearchParamId(SearchParameterInfo parameter)
        => _searchParamIds.TryGetValue(parameter.Url.ToString(), out var id)
           ? id
           : throw new KeyNotFoundException($"SymbolTable has no SearchParamId for '{parameter.Url}' -- Resolve should have resolved every parameter Lower will need.");

    public short ResourceTypeId(string resourceType)
        => _resourceTypeIds.TryGetValue(resourceType, out var id)
           ? id
           : throw new KeyNotFoundException($"SymbolTable has no ResourceTypeId for '{resourceType}'.");
}
```

Verify `SearchParameterInfo.Url` is the right stable identity key (check `src/Core/Ignixa.Search/Models/SearchParameterInfo.cs` — the design doc's own worked example keys by `("Patient","name")`, a (resource type, code) pair, which may be more appropriate than URL if the same code means different things per resource type; confirm which is actually unique before committing to the dictionary key shape, correcting this sketch if `Url` alone isn't sufficient).

- [ ] **Step 4: Write tests**

```csharp
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Symbols;

public class SymbolTableTests
{
    [Fact]
    public void GivenAResolvedParameter_WhenLookedUp_ThenReturnsItsSearchParamId()
    {
        // Arrange -- construct SearchParameterInfo using whatever pattern Phase 2's own tests
        // already established (search test/Ignixa.Application.Tests/Search/Expressions/Parsers/
        // for the real constructor call, do not guess)
    }

    [Fact]
    public void GivenAnUnresolvedParameter_WhenLookedUp_ThenThrows()
    {
        // Assert the KeyNotFoundException message is informative, not just that it throws.
    }
}
```

Fill in real `SearchParameterInfo` construction using Phase 2's already-established test pattern (reuse, don't reinvent).

- [ ] **Step 5: Build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~SymbolTableTests" --nologo
```

**Expected:** 0 warnings, 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add ISymbolResolver and SymbolTable

ISymbolResolver is the compiler's only I/O seam -- implemented by the
data layer, not referenced here. SymbolTable is the immutable resolved
snapshot Resolve produces and Lower/Emit consume as a pure function
input. No resolution logic yet -- that's task 4."
```

---

### Task 4: `Resolve`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`

**Interfaces:**
- Consumes: `SearchParameterPredicateExpression`/`CompositeComponentExpression` (Phase 2, `Ignixa.Search.Expressions`), `ISymbolResolver` (Task 3), `IExpressionVisitor<TContext,TOutput>` (Phase 2, already extended with the two new methods).
- Produces: `SymbolTable` (Task 3) — the deliverable this whole phase exists for.

- [ ] **Step 1: Write a symbol-collecting visitor**

`Resolve` needs to find every `SearchParameterPredicateExpression`/`CompositeComponentExpression` node in a tree and collect the (resource type, `SearchParameterInfo`) pairs that need resolving, before making any I/O calls (batch, don't resolve one-by-one interleaved with tree traversal — this is exactly the "symbol lookup (I/O) ←→ tree traversal" un-braiding the design doc's Resolve stage exists for). Use `ExpressionRewriter<TContext>` or a plain `IExpressionVisitor` implementation — read `LegacyExpressionLowerer.cs` (Phase 2) for the established pattern of deriving from `ExpressionRewriter<TContext>` and overriding only the leaf methods, then adapt it for collection instead of rewriting:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// Walks a typed predicate tree collecting every search parameter it references, without doing
/// any I/O -- Resolve batches these into ISymbolResolver calls afterward. Un-braids tree traversal
/// from symbol lookup, per docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md.
/// </summary>
internal sealed class SymbolCollectingVisitor : ExpressionRewriter<object?>
{
    public HashSet<SearchParameterInfo> Parameters { get; } = [];

    public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
    {
        Parameters.Add(expression.Parameter);
        return expression;
    }

    public override Expression VisitCompositeComponent(CompositeComponentExpression expression, object? context)
    {
        Parameters.Add(expression.ComponentSearchParameter);
        return base.VisitCompositeComponent(expression, context);
    }
}
```

Verify `ExpressionRewriter<TContext>`'s real base-method contracts match this `override` shape (same verification `LegacyExpressionLowerer` needed in Phase 2) — correct if it differs. Also verify whether resource-type collection is needed here too (does the design doc's `Resolve` need to resolve `ResourceTypeId` for every resource type a `SearchParameterExpression`/compartment/chain touches, not just search-param codes? Read `SearchParameterExpression.cs` and `ChainedExpression.cs` to determine whether resource-type identity is carried on nodes this visitor should also collect from — if so, extend the visitor; if resource-type resolution isn't needed until Phase 5's `Lower` stage actually builds `ParamSource`/`ResourceSource` nodes, scope it out of this task and say so in the report).

- [ ] **Step 2: Write `Resolve`**

```csharp
namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The compiler's Resolve stage: walks a typed predicate tree once, collects every search
/// parameter it references, and resolves them all via ISymbolResolver -- the compiler's only I/O,
/// done up front, producing an immutable SymbolTable that Lower/Emit consume synchronously.
/// </summary>
public static class Resolve
{
    public static async Task<SymbolTable> RunAsync(
        Expression expression,
        ISymbolResolver resolver,
        CancellationToken cancellationToken)
    {
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
            // A null result (unresolvable parameter) is not an error here -- Lower/Emit will throw
            // if something downstream actually needs it. Resolve's job is to look up what it can,
            // not to validate the tree is fully resolvable.
        }

        // Resource-type resolution: see task 4 step 1's note -- fill in only if step 1 determined
        // this stage needs it; otherwise pass an empty dictionary and document why in the report.
        var resourceTypeIds = new Dictionary<string, short>();

        return new SymbolTable(searchParamIds, resourceTypeIds);
    }
}
```

Adjust the `using`s and namespace references to match what actually compiles (`Expression` is `Ignixa.Search.Expressions.Expression`).

- [ ] **Step 3: Write tests using a fake `ISymbolResolver`**

```csharp
using Ignixa.Search.Sql.Symbols;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Symbols;

public class ResolveTests
{
    [Fact]
    public async Task GivenATreeWithOnePredicate_WhenResolved_ThenSymbolTableHasItsSearchParamId()
    {
        // Arrange: build a SearchParameterPredicateExpression by hand (same construction pattern
        // as Phase 2's SearchParameterPredicateExpressionTests.cs), and a fake ISymbolResolver
        // (an in-memory Dictionary-backed implementation, not a mock framework -- matches this
        // repo's stated testing philosophy of testing real behavior).

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, fakeResolver, CancellationToken.None);

        // Assert
        symbolTable.SearchParamId(parameter).ShouldBe(expectedId);
    }

    [Fact]
    public async Task GivenACompositeTree_WhenResolved_ThenBothComponentsAreResolved()
    {
        // Arrange: a SearchParameterExpression wrapping Or(And(CompositeComponentExpression(...),
        // CompositeComponentExpression(...))), matching Phase 2's own composite test shape.
    }

    [Fact]
    public async Task GivenAParameterTheResolverCannotFind_WhenResolved_ThenItIsSimplyAbsentFromTheTable()
    {
        // Assert: no exception from Resolve itself; SymbolTable.SearchParamId(...) for that
        // specific parameter throws only when actually looked up later, not during Resolve.
    }
}
```

Write a small fake `ISymbolResolver` (a class backed by a `Dictionary`, implementing the two async methods) rather than a mocking framework, matching the AAA-with-real-behavior convention established throughout this project's other test files.

- [ ] **Step 4: Build and test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~ResolveTests" --nologo
```

**Expected:** 0 warnings, 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add Resolve, the compiler's first pipeline stage

Walks the typed predicate tree once (SymbolCollectingVisitor), batches
every referenced search parameter, and resolves them via ISymbolResolver
-- the compiler's only I/O, done up front. Produces the immutable
SymbolTable Lower/Emit will consume as a pure function input in later
phases. Tested against a fake in-memory resolver, not a live database --
task 5 wires a real one."
```

---

### Task 5: A real `ISymbolResolver` in the SQL EF data layer

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSymbolResolver.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj` (add `ProjectReference` to `Ignixa.Search.Sql`, if not already present transitively)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/SqlEntityFrameworkSymbolResolverTests.cs` (the integration test project Step 0 created — reuse it, don't create a new one)

**Interfaces:**
- Consumes: `ISymbolResolver` (Task 3), `SearchIndexReferenceDataCache` (existing, `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/SearchIndexReferenceDataCache.cs`).
- Produces: a working, live-database-backed `ISymbolResolver` implementation — proof `Resolve` (Task 4) actually works end to end, not just against a fake.

**Before writing code:** read `SearchIndexReferenceDataCache.cs` in full. It's `ConcurrentDictionary`-backed with sentinel negative-caching, requires `PreloadXAsync` before sync `TryGetXFromCache` lookups succeed reliably, is `IDisposable` (wraps an `FhirDbContext`), and lives in this project already — do not duplicate its caching logic; wrap it.

- [ ] **Step 1: Read the cache's real public API**

```bash
cat src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/SearchIndexReferenceDataCache.cs
```

Confirm the exact method names/signatures for preloading and looking up `SearchParamId` and `ResourceTypeId` (the design doc's earlier research summarized these as `PreloadXAsync` + `TryGetXFromCache`, but confirm exact names before writing the adapter).

- [ ] **Step 2: Write the adapter**

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Adapts the existing SearchIndexReferenceDataCache (EF-coupled, this project's own) to
/// ISymbolResolver (Ignixa.Search.Sql's I/O contract, which has no EF reference). Does not
/// duplicate the cache's preload/negative-caching logic -- wraps it.
/// </summary>
public sealed class SqlEntityFrameworkSymbolResolver : ISymbolResolver
{
    private readonly SearchIndexReferenceDataCache _cache;

    public SqlEntityFrameworkSymbolResolver(SearchIndexReferenceDataCache cache)
    {
        _cache = cache;
    }

    public async Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
    {
        // Fill in using the real preload+lookup method names confirmed in Step 1 -- this sketch
        // assumes the cache exposes an async getter directly (matching CompartmentSearchQueryGenerator's
        // own usage pattern, `await _cache.GetSearchParamIdAsync(searchParamInfo)`, cited in earlier
        // research this session); if the real API instead requires a separate preload call first,
        // this method's shape needs adjusting to call that preload step, not silently assume it
        // already happened.
        return await _cache.GetSearchParamIdAsync(parameter);
    }

    public async Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
    {
        // Same caveat -- fill in against the real confirmed API.
        throw new NotImplementedException("Fill in against SearchIndexReferenceDataCache's real ResourceTypeId lookup method, confirmed in Step 1.");
    }
}
```

Replace both bodies with real calls once Step 1's exact API is confirmed — do not leave the `NotImplementedException` in the committed version.

- [ ] **Step 3: Write an integration test against a live database**

Reuse `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/` (Step 0's project) and its `TEST_SQL_CONNECTION_STRING` convention. Seed a minimal `SearchParam`/`ResourceType` catalog (reuse `CompartmentDataSeeder`'s catalog-seeding helper from Step 0 if it's still usable standalone, or the real base FHIR search-parameter sync mechanism `IgnixaApiFixture.cs` uses — do not hand-roll a third catalog-seeding approach), then resolve a known parameter through `Resolve.RunAsync` + this real resolver, asserting the returned `SearchParamId` matches what was seeded.

```csharp
[Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
public async Task GivenARealDatabase_WhenResolvingAKnownParameter_ThenReturnsItsRealSearchParamId()
{
    // Arrange: seed one real search parameter, construct SqlEntityFrameworkSymbolResolver against
    // the live connection, build a one-node predicate tree referencing that parameter.

    // Act
    var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);

    // Assert
    symbolTable.SearchParamId(parameter).ShouldBe(theRealSeededId);
}
```

Mark it `[Fact(Skip = "...")]` matching this project's existing convention for manual/live-database tests (established in Step 0's `DatabaseSchemaInitializationTests.cs` and `CompartmentSearchStep0BenchmarkTests.cs`) — this is not a CI test.

- [ ] **Step 4: Run it if a live SQL Server is reachable**

Check for a running `ignixa-test-sql` container first (may already be up from earlier work). If reachable, remove the `Skip` locally, run the test, confirm it passes, then restore the `Skip` before committing. If not reachable, say so honestly in the report rather than claiming verification that didn't happen — this project's history has one prior instance of an unverified E2E claim causing a real problem; don't repeat it.

- [ ] **Step 5: Build and run the non-E2E suite**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

**Expected:** 0 warnings, 0 errors, all green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(datalayer): add SqlEntityFrameworkSymbolResolver

Adapts the existing SearchIndexReferenceDataCache to Ignixa.Search.Sql's
ISymbolResolver contract -- no caching logic duplicated, just an
interface adapter. This is the first real (non-fake) proof that Resolve
works end to end against a live database.

Closes phase 3 of docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md."
```

## Self-Review

- **Spec coverage:** Task 1 covers the project skeleton, Task 2 the catalog, Tasks 3-4 the symbol table and resolve stage, Task 5 proves it against a real database — matching the roadmap's exact Phase 3 scope ("`Ignixa.Search.Sql` project skeleton, `SqlCatalog`, `SymbolTable`, `Resolve`"), no more (no `Lower`/`Plan`/`Ast`/`Emit`, no `Compile()` entry point, no full 13-type catalog) and no less.
- **Placeholder scan:** Task 2's `SqlCatalog.Build()` body, Task 4's resource-type-resolution scope, and Task 5's adapter method bodies are deliberately marked "verify against real source before finalizing" rather than pre-written as fact, matching the established honest-deferral pattern from every prior phase's plan — each names the exact file/command to resolve the unknown, not a vague "figure it out."
- **Type consistency:** `SqlCatalog.Table(string)`, `TableDescriptor.Column(string)`, `ISymbolResolver.GetSearchParamIdAsync`/`GetResourceTypeIdAsync`, `SymbolTable.SearchParamId`/`ResourceTypeId`, and `Resolve.RunAsync(Expression, ISymbolResolver, CancellationToken)` are used identically across Tasks 2-5 — checked for drift, none found.
