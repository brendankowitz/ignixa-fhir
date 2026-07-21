# FHIR-to-SQL Compiler: Phase 4 (AST + Emit) + Phase 5 (TextOverflow fix, Lower tier-1/tier-2, narrowed) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the compiler's CTE-graph plan IR (`QueryPlan`/`CteDefinition`/`Explain()`) and its `Emit` stage, fix the `TextOverflow` write-convention bug that blocks correct string lowering, and implement `Lower`'s structural (`And`/`Or`) and three leaf-type (`String`/`Token`/`Reference`) rules -- proving `Resolve -> Lower -> Emit` end to end against real bound queries.

**Architecture:** Per `docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md`'s three-stage pipeline (`Resolve -> Lower -> Emit`, Phase 3 built `Resolve`), this plan builds the CTE-graph IR and `Emit` first against hand-built plans (no `Lower` dependency), then builds `Lower`'s tier-1/tier-2 rules on top. The tier boundary is enforced as a *type*, not convention: `LeafContext` exposes only symbol lookups and value parameterization (no `CteRef`, no sibling access); `StructuralContext` owns the plan's CTE list and dispatches leaves to tier-1 rules. `Ignixa.Search.Sql` keeps zero EF/AspNetCore references throughout -- `Emit` returns a plain `EmittedSql`/`EmittedSqlParameter` pair, not `Microsoft.Data.SqlClient.SqlParameter`.

**Tech Stack:** `net9.0;net10.0`, `Nullable=enable` (matches `Ignixa.Search.Sql`'s existing convention). xUnit + Shouldly for tests.

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- `Ignixa.Search.Sql.csproj` must keep **no** `Microsoft.EntityFrameworkCore*`/`Microsoft.AspNetCore.*` reference -- verify after every task that touches it (all of tasks 2-10).
- **Scope is deliberately narrower than the full roadmap phases 4-5**, mirroring Phase 3's own "only what has a consumer now" discipline. Explicitly OUT of scope for this plan, each with a one-line reason:
  - `Date`/`Number`/`Quantity`/`Uri` leaf types and all 6 composite leaf types (`TokenToken`, `TokenQuantity`, `TokenString`, `TokenDateTime`, `TokenNumberNumber`, `ReferenceToken`) -- only `String`/`Token`/`Reference` (already in Phase 3's `SqlCatalog`) get lowering rules.
  - `Not` -> `Except` (and its `ResourceSource` seed synthesis) -- requires `ResourceTypeId` resolution for the *searched* resource type, which `Resolve` does not do (Phase 3 deliberately left `resourceTypeIds` empty). Tier 2 in this plan covers only `And` -> `Intersect`, `Or` -> `Union`.
  - `ResourceSource`, `Except`, `ChainJoin` `CteDefinition` cases -- no rule in this plan's scope constructs them. Add when `Not`/chain lowering is written (a future phase).
  - `IncludeStage`/`SortSpec`/full `PageSpec` (tier-3 result-shape stages) -- `QueryPlan` carries only an optional row cap (`Top`).
  - Composite fixes to `TokenStringCompositeRowGenerator.cs` and `RefTokenCompositeRowGenerator.cs` (same `TextOverflow`-style remainder-write defect as task 1's target, confirmed present in both) -- deferred to whichever future phase implements `TokenString`/`ReferenceToken` composite lowering, since nothing consumes the corrected convention for those tables yet.
  - A reindex/backfill mechanism for already-written `TextOverflow` rows -- **does not exist anywhere in this codebase** (`BackgroundJobType.Reindex` is an unused enum stub, "for future use"). Task 1 fixes the write/read code only. **Do not deploy task 1's fix against any database with pre-existing >256-char string search values until a backfill mechanism is built and run** -- that mechanism is a separate, substantial follow-up, not part of this plan.
  - `Emit`'s SQL text-building does not go through a separate `SqlAst` node-tree layer as the design doc's prose describes (`QueryPlan -> SqlAst -> text`). This plan collapses that into `Emit`'s own string-building, reusing the Plan-IR's `Predicate`/`SqlParameterRef` types to satisfy the doc's real invariant ("no unparameterized user value ever appears in SQL text") without a third type hierarchy that has no other behavior yet. Revisit if/when `Emit` needs to compose more elaborate SQL (tier-3 stages).
- Every `Predicate`/`CteDefinition`/`QueryPlan` type is an immutable record (or nested sealed record family) -- no mutable plan-IR state after construction.
- Follow repo convention: file-scoped namespaces (usings above the namespace line), AAA test structure, `GivenContext_WhenAction_ThenResult` naming, no `#region`, one cohesive concept per file (a closed record hierarchy that always changes together, e.g. `CteDefinition`'s cases, lives in one file -- see `docs/superpowers/plans` precedent of splitting by responsibility, not mechanically by type).
- Real DDL/collation facts (`Latin1_General_100_CS_AS`/`Latin1_General_100_CI_AI`, `256`-char `Text` width, etc.) are cited from `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql` and Phase 3's `SqlCatalog` -- do not re-derive or approximate.

---

### Task 1: `TextOverflow` write-convention fix (`StringSearchParam` only)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs` (four `Text`/`TextOverflow` read-reconstruction call sites, currently around lines 1560-1644 -- confirm exact lines before editing, they may have shifted)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/StringSearchParameterRowGeneratorTests.cs` (create if it doesn't already exist -- check first)
- Test: wherever this repo's existing string-search read-path tests live (grep for `:exact`/`:contains` string modifier tests under `test/Ignixa.DataLayer.SqlEntityFramework*`) -- extend with a >256-char-value case; do not create a parallel test file if one already covers this area.

**Interfaces:**
- Consumes: `Ignixa.Search.Sql.Catalog.SqlCatalog.Default` (Phase 3, already referenced via the `ProjectReference` Phase 3 Task 5 added to `Ignixa.DataLayer.SqlEntityFramework.csproj`).
- Produces: correct `Text`/`TextOverflow` write and read behavior every later task in this plan's `StringLoweringRule` (task 6) assumes.

- [ ] **Step 1: Confirm the current defect and the real DDL width**

```bash
grep -n "StringColumnMaxLength\|TextOverflow" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs
grep -n "Text \|TextOverflow" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql
```

Confirm current code still does `record.SetString(3, textValue.Substring(0, StringColumnMaxLength))` / `record.SetString(4, textValue.Substring(StringColumnMaxLength))` (writes the *remainder* to `TextOverflow`) and that `dbo.StringSearchParam.Text` is `NVARCHAR(256)`. If either has changed since this plan was written, adjust the steps below to match what's actually there.

- [ ] **Step 2: Write the failing write-path test**

```csharp
// test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/StringSearchParameterRowGeneratorTests.cs
using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.RowGenerators;

public class StringSearchParameterRowGeneratorTests
{
    [Fact]
    public void GivenAStringLongerThan256Chars_WhenGeneratingRow_ThenTextOverflowHoldsTheWholeValue()
    {
        // Arrange
        var longValue = new string('A', 300);
        var generator = new StringSearchParameterRowGenerator();
        var resource = TestResourceBuilder.WithStringSearchValue(longValue); // use this project's existing resource/search-value test builder -- grep for the pattern other RowGenerator tests use before inventing a new one

        // Act
        var record = generator.GenerateSqlDataRecords(
            [resource], TestResourceBuilder.ResourceTypeIdMap, TestResourceBuilder.SearchParamIdMap).Single();

        // Assert
        record.GetString(3).Length.ShouldBe(256);                 // Text: redundant 256-char prefix
        record.GetString(3).ShouldBe(longValue[..256]);
        record.GetString(4).ShouldBe(longValue);                  // TextOverflow: the WHOLE value, not the remainder
    }

    [Fact]
    public void GivenAStringUnder256Chars_WhenGeneratingRow_ThenTextOverflowIsNull()
    {
        // Arrange
        var shortValue = "Smith";
        var generator = new StringSearchParameterRowGenerator();
        var resource = TestResourceBuilder.WithStringSearchValue(shortValue);

        // Act
        var record = generator.GenerateSqlDataRecords(
            [resource], TestResourceBuilder.ResourceTypeIdMap, TestResourceBuilder.SearchParamIdMap).Single();

        // Assert
        record.GetString(3).ShouldBe(shortValue);
        record.IsDBNull(4).ShouldBeTrue();
    }
}
```

The `TestResourceBuilder` calls above are a placeholder name -- find this test project's real existing helper/fixture for constructing a `ResourceWrapper` + calling `ISearchParameterRowGenerator.GenerateSqlDataRecords` (grep `test/Ignixa.DataLayer.SqlEntityFramework.Tests/RowGenerators/` for an existing `*RowGeneratorTests.cs` to copy the real setup pattern) and use that instead of inventing a new one.

- [ ] **Step 3: Run to confirm it fails**

```bash
dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests --filter "FullyQualifiedName~StringSearchParameterRowGeneratorTests" --nologo
```

Expected: FAIL on the `TextOverflowHoldsTheWholeValue` case (`record.GetString(4)` currently returns the 44-char remainder, not the 300-char whole value).

- [ ] **Step 4: Fix the write path, sourcing the inline width from `SqlCatalog` instead of a local hardcoded constant**

```csharp
// src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/StringSearchParameterRowGenerator.cs
using Ignixa.Search.Sql.Catalog;

// ... inside the class, replacing `private const int StringColumnMaxLength = 256;`:
private static readonly int InlineWidth =
    SqlCatalog.Default.Table("StringSearchParam").Column("Text").MaxLength
    ?? throw new InvalidOperationException("StringSearchParam.Text has no MaxLength in SqlCatalog.");

// ... replacing the write-path branch:
var textValue = stringValue.String;
if (textValue != null)
{
    if (textValue.Length > InlineWidth)
    {
        // Text keeps a redundant prefix so the index can still seek (fhir-server's convention);
        // TextOverflow holds the WHOLE value -- not the remainder -- so LIKE/= against TextOverflow
        // alone is correct for the >InlineWidth case. See this plan's task 1 and
        // docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md's TextOverflow section.
        record.SetString(3, textValue[..InlineWidth]);
        record.SetString(4, textValue);
    }
    else
    {
        record.SetString(3, textValue);
        record.SetDBNull(4);
    }
}
else
{
    record.SetDBNull(3);
    record.SetDBNull(4);
}
```

- [ ] **Step 5: Run to confirm the write-path tests pass**

```bash
dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests --filter "FullyQualifiedName~StringSearchParameterRowGeneratorTests" --nologo
```

Expected: PASS, both tests.

- [ ] **Step 6: Fix the read path -- find and correct all four call sites**

```bash
grep -n "TextOverflow" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs
```

For each match, the reconstruction currently assumes `TextOverflow` holds only the remainder and concatenates:

```csharp
// BEFORE (wrong once task 1's write-path fix lands -- would double the prefix):
sp.TextOverflow != null ? sp.Text + sp.TextOverflow : sp.Text
```

```csharp
// AFTER (TextOverflow now holds the whole value when present):
sp.TextOverflow != null ? sp.TextOverflow : sp.Text
```

And the `LIKE`-based call site (the `StartsWith`/`Contains`/`EndsWith` branch that calls `EF.Functions.Collate(sp.Text + sp.TextOverflow, collation)`):

```csharp
// BEFORE:
.Where(sp => sp.TextOverflow != null && EF.Functions.Like(
    EF.Functions.Collate(sp.Text + sp.TextOverflow, collation), pattern))
```

```csharp
// AFTER:
.Where(sp => sp.TextOverflow != null && EF.Functions.Like(
    EF.Functions.Collate(sp.TextOverflow, collation), pattern))
```

Apply the same `sp.Text + sp.TextOverflow` -> `sp.TextOverflow` transform at each of the four sites found by the grep above. Do not change the collation-selection logic (`Latin1_General_100_CI_AI` vs `Latin1_General_100_CS_AS` for `:exact`) -- only the column expression feeding into it.

- [ ] **Step 7: Write/extend a read-path test for a >256-char value**

Find this repo's existing string-search read-path test file (grep `test/Ignixa.DataLayer.SqlEntityFramework*` for a test exercising `:exact`/`:contains`/`:starts-with` against `StringSearchParam`) and add a case seeding a >256-char value, then searching for a substring/prefix/exact match that only exists past character 256, asserting the resource is found. If no such live-DB test exists yet in a runnable (non-`Skip`) form, add this as a `[Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]` in the same integration test project Phase 3 task 5 used, matching that established convention.

- [ ] **Step 8: Build and run the non-E2E suite**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors, all green (the new read-path test stays `Skip`ped if no live SQL Server is reachable -- say so honestly rather than claiming it ran).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "fix(datalayer): TextOverflow now holds the whole string value, not the remainder

Ignixa's write path stored only the characters past position 256 in
TextOverflow; fhir-server (and this fix) store the whole value there,
keeping Text as a redundant 256-char prefix for index seeking. The read
path's Text+TextOverflow concatenation is corrected to use TextOverflow
alone when present -- the old concatenation would double the prefix
under the new write convention.

Inline width is now sourced from Ignixa.Search.Sql's SqlCatalog (Phase 3)
instead of a locally hardcoded constant, removing one of three
independent copies of the 256/128 threshold this codebase carried.

No reindex/backfill mechanism exists in this codebase for already-written
rows -- this fix must not be deployed against a database with existing
>256-char string search values until one is built. TokenStringComposite
and RefTokenComposite row generators carry the same defect for their own
overflow columns and are NOT fixed here -- deferred to whichever phase
implements those composite leaf lowering rules."
```

---

### Task 2: Plan IR -- `CteRef`, `Predicate`, `SqlColumnRef`, `SqlParameterRef`, `LikeMatch`, `CteDefinition`, `QueryPlan`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Ast/CteRef.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/SqlParameterRef.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/SqlColumnRef.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/LikeMatch.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/Predicate.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/QueryPlanTests.cs`

**Interfaces:**
- Consumes: `Ignixa.Search.Sql.Catalog.TableDescriptor` (Phase 3).
- Produces: every type task 3 (`Explain`), task 4 (`Emit`), and tasks 5-10 (`Lower`) build on. Exact shapes below are load-bearing for every later task -- do not drift field names.

- [ ] **Step 1: Write the leaf IR types**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/CteRef.cs
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// An index into QueryPlan.Ctes -- how one CteDefinition refers to another. Matches Explain()'s
/// cte0/cte1/... numbering by construction.
/// </summary>
public readonly record struct CteRef(int Index);
```

```csharp
// src/Core/Ignixa.Search.Sql/Ast/SqlParameterRef.cs
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// A placeholder for one user-supplied value. Emit turns this into a real parameterized SQL parameter
/// -- SQL text never contains a literal user value (design doc's "no unparameterized user value"
/// AST invariant).
/// </summary>
public sealed record SqlParameterRef(object Value);
```

```csharp
// src/Core/Ignixa.Search.Sql/Ast/SqlColumnRef.cs
namespace Ignixa.Search.Sql.Ast;

public sealed record SqlColumnRef(string Table, string Column);
```

```csharp
// src/Core/Ignixa.Search.Sql/Ast/LikeMatch.cs
namespace Ignixa.Search.Sql.Ast;

public enum LikeMatch { Contains, StartsWith, EndsWith }
```

- [ ] **Step 2: Write `Predicate`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/Predicate.cs
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// A WHERE-clause fragment over one ParamSource CTE's own table -- never spans tables. Composites
/// (out of scope for this plan) would express multiple column comparisons as nested And.
/// </summary>
public abstract record Predicate
{
    public sealed record Equal(SqlColumnRef Column, SqlParameterRef Value, string? Collation = null) : Predicate;

    public sealed record Like(SqlColumnRef Column, SqlParameterRef Value, LikeMatch Match, string? Collation = null) : Predicate;

    public sealed record And(Predicate Left, Predicate Right) : Predicate;
}
```

- [ ] **Step 3: Write `CteDefinition`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph. Scoped to this plan's needs only: ParamSource (a single
/// search-param table filtered by SearchParamId + Predicate), Intersect (AND), Union (OR).
/// ResourceSource/Except/ChainJoin are NOT included -- nothing in this plan's scope (:not, chain)
/// constructs them; add when that lowering rule is written. See design doc's CteDefinition grammar.
/// </summary>
public abstract record CteDefinition
{
    public sealed record ParamSource(TableDescriptor Table, short SearchParamId, Predicate Predicate) : CteDefinition;

    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;
}
```

- [ ] **Step 4: Write `QueryPlan`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output -- Lower produces this, Emit consumes it. Every entry in Ctes,
/// including Intersect/Union nodes, becomes its own named CTE when emitted -- that is what makes this
/// a graph rather than a tree of inline joins, and lets Match point at any depth of nesting.
/// IncludeStage/SortSpec/full PageSpec (tier-3 result-shape stages) are not included yet -- nothing in
/// scope here produces or consumes them.
/// </summary>
public sealed record QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null);
```

- [ ] **Step 5: Write construction tests that assert real structural facts, not just property echo**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/QueryPlanTests.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class QueryPlanTests
{
    [Fact]
    public void GivenAParamSourceAndAnIntersectReferencingIt_WhenConstructed_ThenTheGraphIsWellFormed()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));

        // Act
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            Match: new CteRef(2),
            Top: 10);

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
        var intersect = plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        intersect.Left.ShouldBe(new CteRef(0));
        intersect.Right.ShouldBe(new CteRef(1));
        plan.Match.ShouldBe(new CteRef(2));
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenAUnionOfTwoCteRefs_WhenConstructed_ThenPartsPreserveOrder()
    {
        // Act
        var union = new CteDefinition.Union([new CteRef(0), new CteRef(1)]);

        // Assert
        union.Parts.ShouldBe([new CteRef(0), new CteRef(1)]);
    }
}
```

- [ ] **Step 6: Run to confirm they pass, then build**

```bash
dotnet test All.sln --filter "FullyQualifiedName~QueryPlanTests" --nologo
dotnet build All.sln --nologo
grep -i "EntityFrameworkCore\|AspNetCore" src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```

Expected: 0 warnings, 0 errors, both tests pass, grep returns nothing.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add the CTE-graph plan IR

CteRef, Predicate, SqlColumnRef, SqlParameterRef, LikeMatch, CteDefinition
(ParamSource/Intersect/Union), QueryPlan. Every CteDefinition entry
becomes its own named CTE at emit time, including Intersect/Union nodes
-- this is what lets the graph nest to arbitrary depth rather than only
supporting one level of combination. ResourceSource/Except/ChainJoin and
tier-3 result-shape types are deliberately not included -- no rule in
this plan's scope constructs them yet."
```

---

### Task 3: `Explain()` -- the plan-shape golden-test format

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs` (add an `Explain()` instance method)
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: `QueryPlan`, `CteDefinition`, `Predicate` (task 2).
- Produces: `QueryPlan.Explain(): string`, used directly by task 10's golden test and by any future phase's golden-plan tests.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class PlanExplainerTests
{
    [Fact]
    public void GivenASingleParamSourcePlan_WhenExplained_ThenPrintsTheColumnComparisonAsRoot()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"),
            new SqlParameterRef("Smith"),
            "Latin1_General_100_CS_AS");
        var plan = new QueryPlan(
            [new CteDefinition.ParamSource(table, 202, predicate)],
            Match: new CteRef(0),
            Top: 10);

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = StringSearchParam[202]  Text = @p0 collate CS_AS top 10");
    }

    [Fact]
    public void GivenAnIntersectOfTwoParamSources_WhenExplained_ThenLeavesAreNumberedAndRootReferencesThem()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(
            new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            Match: new CteRef(2));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe(
            "cte0 = StringSearchParam[202]  Text = @p0 collate CS_AS\n" +
            "cte1 = TokenSearchParam[44]  Code = @p1\n" +
            "root = Intersect(cte0, cte1)");
    }
}
```

- [ ] **Step 2: Run to confirm they fail (no `Explain()` method exists yet)**

```bash
dotnet build All.sln --nologo
```

Expected: build error, `QueryPlan` has no `Explain` member.

- [ ] **Step 3: Implement `PlanExplainer` and wire `QueryPlan.Explain()`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs
using System.Text;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Renders a QueryPlan as human-readable text -- the compiler's plan-shape golden-test format.
/// Read-only by design: no parser, no round-trip (design doc's Explain() rationale -- a parseable
/// plan DSL would need a printer AND a parser, and would import SQL's semantics into a FHIR-meaning
/// layer for no benefit).
/// </summary>
public static class PlanExplainer
{
    public static string Print(QueryPlan plan)
    {
        var lines = new List<string>();
        var parameterOrdinal = 0;

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            var isRoot = i == plan.Match.Index;
            var label = isRoot ? "root" : $"cte{i}";
            var top = isRoot ? plan.Top : null;
            lines.Add($"{label} = {PrintCte(plan.Ctes[i], top, ref parameterOrdinal)}");
        }

        return string.Join('\n', lines);
    }

    private static string PrintCte(CteDefinition cte, int? top, ref int parameterOrdinal) => cte switch
    {
        CteDefinition.ParamSource p =>
            $"{p.Table.TableName}[{p.SearchParamId}]  {PrintPredicate(p.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
        CteDefinition.Intersect x =>
            $"Intersect(cte{x.Left.Index}, cte{x.Right.Index}){PrintTop(top)}",
        CteDefinition.Union u =>
            $"Union({string.Join(", ", u.Parts.Select(r => $"cte{r.Index}"))}){PrintTop(top)}",
        _ => throw new NotSupportedException($"No Explain() rendering for {cte.GetType().Name}."),
    };

    private static string PrintPredicate(Predicate predicate, ref int parameterOrdinal) => predicate switch
    {
        Predicate.Equal e => $"{e.Column.Column} = @p{parameterOrdinal++}{PrintCollation(e.Collation)}",
        Predicate.Like l => $"{l.Column.Column} LIKE @p{parameterOrdinal++} ({l.Match}){PrintCollation(l.Collation)}",
        Predicate.And a => $"{PrintPredicate(a.Left, ref parameterOrdinal)} AND {PrintPredicate(a.Right, ref parameterOrdinal)}",
        _ => throw new NotSupportedException($"No Explain() rendering for {predicate.GetType().Name}."),
    };

    private static string PrintCollation(string? collation)
    {
        if (collation is null) return string.Empty;
        if (collation.EndsWith("_CS_AS", StringComparison.Ordinal)) return " collate CS_AS";
        if (collation.EndsWith("_CI_AI", StringComparison.Ordinal)) return " collate CI_AI";
        return $" collate {collation}";
    }

    private static string PrintTop(int? top) => top is null ? string.Empty : $" top {top}";
}
```

```csharp
// src/Core/Ignixa.Search.Sql/Ast/QueryPlan.cs -- add inside the record:
public sealed record QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
```

- [ ] **Step 4: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~PlanExplainerTests" --nologo
```

Expected: 0 warnings, 0 errors, both tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add QueryPlan.Explain(), the plan-shape golden-test format

Read-only, no parser -- a parseable plan DSL would need both a printer
and a parser and would import SQL's semantics into a FHIR-meaning layer
for no benefit. Golden tests assert on this output, not SQL text, so
they survive Emit changes."
```

---

### Task 4: `Emit` -- `QueryPlan` to parameterized T-SQL

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Ast/EmittedSql.cs`
- Create: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`

**Interfaces:**
- Consumes: `QueryPlan`, `CteDefinition`, `Predicate` (task 2).
- Produces: `Emit.Run(QueryPlan): EmittedSql`, `EmittedSql(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters)` -- consumed by task 10's end-to-end test and, eventually, whatever DataLayer executes compiled queries (maps `EmittedSqlParameter` to a real ADO parameter type; `Ignixa.Search.Sql` never references one itself).

- [ ] **Step 1: Write `EmittedSql`/`EmittedSqlParameter`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/EmittedSql.cs
namespace Ignixa.Search.Sql.Ast;

public sealed record EmittedSqlParameter(string Name, object Value);

public sealed record EmittedSql(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters);
```

- [ ] **Step 2: Write the failing golden SQL tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Ast;

public class EmitTests
{
    [Fact]
    public void GivenASingleParamSourcePlan_WhenEmitted_ThenProducesAParameterizedSelect()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 202, predicate)], new CteRef(0), Top: 10);

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            ")\n" +
            "SELECT TOP (10) T1, Sid1 FROM cte0");
        emitted.Parameters.Count.ShouldBe(1);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", "Smith"));
    }

    [Fact]
    public void GivenAnIntersectOfTwoParamSources_WhenEmitted_ThenJoinsThemOnResourceIdentity()
    {
        // Arrange
        var stringTable = SqlCatalog.Default.Table("StringSearchParam");
        var tokenTable = SqlCatalog.Default.Table("TokenSearchParam");
        var stringPredicate = new Predicate.Equal(
            new SqlColumnRef(stringTable.TableName, "Text"), new SqlParameterRef("Smith"), "Latin1_General_100_CS_AS");
        var tokenPredicate = new Predicate.Equal(new SqlColumnRef(tokenTable.TableName, "Code"), new SqlParameterRef("true"));
        var plan = new QueryPlan(
            [
                new CteDefinition.ParamSource(stringTable, 202, stringPredicate),
                new CteDefinition.ParamSource(tokenTable, 44, tokenPredicate),
                new CteDefinition.Intersect(new CteRef(0), new CteRef(1)),
            ],
            new CteRef(2));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.StringSearchParam\n" +
            "    WHERE SearchParamId = 202 AND Text = @p0 COLLATE Latin1_General_100_CS_AS\n" +
            "),\n" +
            "cte1 AS (\n" +
            "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.TokenSearchParam\n" +
            "    WHERE SearchParamId = 44 AND Code = @p1\n" +
            "),\n" +
            "cte2 AS (\n" +
            "    SELECT cte0.T1, cte0.Sid1\n" +
            "    FROM cte0\n" +
            "    INNER JOIN cte1 ON cte0.T1 = cte1.T1 AND cte0.Sid1 = cte1.Sid1\n" +
            ")\n" +
            "SELECT T1, Sid1 FROM cte2");
        emitted.Parameters.Select(p => p.Name).ShouldBe(["@p0", "@p1"]);
    }

    [Fact]
    public void GivenAnyPlanWithAUserValue_WhenEmitted_ThenTheValueNeverAppearsInSqlTextOnlyInParameters()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Like(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("100%"), LikeMatch.Contains, "Latin1_General_100_CI_AI");
        var plan = new QueryPlan([new CteDefinition.ParamSource(table, 202, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldNotContain("100");
        emitted.Parameters.ShouldContain(p => p.Value.Equals("%100\\%%"));
    }
}
```

- [ ] **Step 3: Run to confirm failure**

```bash
dotnet build All.sln --nologo
```

Expected: build error, `Emit` doesn't exist yet.

- [ ] **Step 4: Implement `Emit`**

```csharp
// src/Core/Ignixa.Search.Sql/Ast/Emit.cs
namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Turns a QueryPlan into parameterized T-SQL text -- deterministic (same plan -> byte-identical SQL).
/// Every CteDefinition entry, including Intersect/Union, becomes its own named CTE, so Match can point
/// at any depth of nesting without special-casing the outer SELECT. No user value is ever inlined into
/// SQL text -- every SqlParameterRef becomes a named parameter (see design doc's AST invariant).
/// </summary>
public static class Emit
{
    public static EmittedSql Run(QueryPlan plan)
    {
        var parameters = new List<EmittedSqlParameter>();
        var cteBlocks = new List<string>();

        for (var i = 0; i < plan.Ctes.Count; i++)
        {
            cteBlocks.Add($"cte{i} AS (\n{EmitCte(plan.Ctes[i], parameters)}\n)");
        }

        var top = plan.Top is { } n ? $"TOP ({n}) " : string.Empty;
        var sql = $";WITH {string.Join(",\n", cteBlocks)}\n" +
                  $"SELECT {top}T1, Sid1 FROM cte{plan.Match.Index}";

        return new EmittedSql(sql, parameters);
    }

    private static string EmitCte(CteDefinition cte, List<EmittedSqlParameter> parameters) => cte switch
    {
        CteDefinition.ParamSource p => EmitParamSource(p, parameters),
        CteDefinition.Intersect x =>
            $"    SELECT cte{x.Left.Index}.T1, cte{x.Left.Index}.Sid1\n" +
            $"    FROM cte{x.Left.Index}\n" +
            $"    INNER JOIN cte{x.Right.Index} ON cte{x.Left.Index}.T1 = cte{x.Right.Index}.T1 AND cte{x.Left.Index}.Sid1 = cte{x.Right.Index}.Sid1",
        CteDefinition.Union u =>
            string.Join("\n    UNION\n", u.Parts.Select(r => $"    SELECT T1, Sid1 FROM cte{r.Index}")),
        _ => throw new NotSupportedException($"No Emit for {cte.GetType().Name}."),
    };

    private static string EmitParamSource(CteDefinition.ParamSource p, List<EmittedSqlParameter> parameters)
        => $"    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM {p.Table.SchemaName}.{p.Table.TableName}\n" +
           $"    WHERE SearchParamId = {p.SearchParamId} AND {EmitPredicate(p.Predicate, parameters)}";

    private static string EmitPredicate(Predicate predicate, List<EmittedSqlParameter> parameters) => predicate switch
    {
        Predicate.Equal e => $"{e.Column.Column} = {EmitParam(e.Value, parameters)}{EmitCollation(e.Collation)}",
        Predicate.Like l => $"{l.Column.Column} LIKE {EmitParam(EscapeLike(l), parameters)} ESCAPE '\\'{EmitCollation(l.Collation)}",
        Predicate.And a => $"({EmitPredicate(a.Left, parameters)} AND {EmitPredicate(a.Right, parameters)})",
        _ => throw new NotSupportedException($"No Emit for {predicate.GetType().Name}."),
    };

    private static SqlParameterRef EscapeLike(Predicate.Like like)
    {
        var raw = (string)like.Value.Value;
        var escaped = raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
        var pattern = like.Match switch
        {
            LikeMatch.Contains => $"%{escaped}%",
            LikeMatch.StartsWith => $"{escaped}%",
            LikeMatch.EndsWith => $"%{escaped}",
            _ => throw new NotSupportedException($"No LIKE pattern for {like.Match}."),
        };
        return new SqlParameterRef(pattern);
    }

    private static string EmitParam(SqlParameterRef value, List<EmittedSqlParameter> parameters)
    {
        var name = $"@p{parameters.Count}";
        parameters.Add(new EmittedSqlParameter(name, value.Value));
        return name;
    }

    private static string EmitCollation(string? collation) => collation is null ? string.Empty : $" COLLATE {collation}";
}
```

- [ ] **Step 5: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EmitTests" --nologo
```

Expected: 0 warnings, 0 errors, all three tests pass. If the exact whitespace in the golden SQL strings doesn't match your `Emit` output byte-for-byte, fix the test's expected string to match `Emit`'s actual deterministic output -- this plan's exact spacing is a reasonable canonical choice, not a literal design-doc quote (the doc gives prose/illustrative SQL, not a byte-exact target for the two-CTE case).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add Emit -- QueryPlan to parameterized T-SQL

Deterministic: same plan produces byte-identical SQL. Every CteDefinition
entry becomes its own named CTE, so nesting depth is unbounded without
special-casing the outer SELECT. No SqlAst intermediate layer -- Emit
builds SQL text directly from the plan IR's Predicate/SqlParameterRef
types, which already satisfy the 'no unparameterized user value'
invariant a separate AST layer would otherwise exist to enforce (see
this plan's global constraints for the reasoning)."
```

---

### Task 5: `LeafContext`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LeafContextTests.cs`

**Interfaces:**
- Consumes: `Ignixa.Search.Sql.Symbols.SymbolTable` (Phase 3), `Ignixa.Search.Models.SearchParameterInfo`, `Ignixa.Search.Sql.Ast.SqlParameterRef` (task 2).
- Produces: `LeafContext`, the only thing tasks 6-8's leaf rules are given -- no `CteRef`, no plan access, enforcing the tier-1 boundary as a type per the design doc.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/LeafContextTests.cs
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class LeafContextTests
{
    [Fact]
    public void GivenAResolvedParameter_WhenSearchParamIdRequested_ThenReturnsTheSymbolTablesValue()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short>());
        var context = new LeafContext(symbols);

        // Act & Assert
        context.SearchParamId(parameter).ShouldBe((short)202);
    }

    [Fact]
    public void GivenAValue_WhenParameterized_ThenReturnsASqlParameterRefWrappingIt()
    {
        // Arrange
        var symbols = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());
        var context = new LeafContext(symbols);

        // Act
        var parameterRef = context.Parameter("Smith");

        // Assert
        parameterRef.Value.ShouldBe("Smith");
    }
}
```

- [ ] **Step 2: Run to confirm failure, then implement**

```bash
dotnet build All.sln --nologo
```

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The tier-1 (leaf) context: exposes symbol lookups and value parameterization only -- no CteRef,
/// no Intersect/Union, no sibling access. A leaf rule cannot see or affect the rest of the plan by
/// construction (design doc: "enforce the tier boundary as a type, not convention").
/// </summary>
public sealed class LeafContext
{
    private readonly SymbolTable _symbols;

    public LeafContext(SymbolTable symbols)
    {
        _symbols = symbols;
    }

    public short SearchParamId(SearchParameterInfo parameter) => _symbols.SearchParamId(parameter);

    public short ResourceTypeId(string resourceType) => _symbols.ResourceTypeId(resourceType);

    public SqlParameterRef Parameter(object value) => new(value);
}
```

- [ ] **Step 3: Run to confirm tests pass, then build**

```bash
dotnet test All.sln --filter "FullyQualifiedName~LeafContextTests" --nologo
dotnet build All.sln --nologo
```

Expected: 0 warnings, 0 errors, both tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add LeafContext, the tier-1 lowering boundary

Exposes only SymbolTable lookups and value parameterization -- no
CteRef, no plan access. A leaf rule literally cannot construct
Intersect/Union or see sibling predicates, by type, not convention."
```

---

### Task 6: `StringLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/StringLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/StringLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext` (task 5), `SearchParameterPredicateExpression` (Phase 2), `StringSearchValue` (`Ignixa.Search.Indexing.SearchValues`), `SqlCatalog.Default.Table("StringSearchParam")` (Phase 3, now correct per task 1's fix).
- Produces: `StringLoweringRule.Lower(SearchParameterPredicateExpression, StringSearchValue, LeafContext): CteDefinition.ParamSource`, dispatched to by task 9's `LeafLoweringDispatcher`.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/StringLoweringRuleTests.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class StringLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    [Fact]
    public void GivenAnExactModifier_WhenLowered_ThenComparesTextWithCaseSensitiveCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        cte.SearchParamId.ShouldBe((short)202);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Text");
        equal.Collation.ShouldBe("Latin1_General_100_CS_AS");
        equal.Value.Value.ShouldBe("Smith");
    }

    [Fact]
    public void GivenNoModifier_WhenLowered_ThenComparesTextWithCaseInsensitiveCollation()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Collation.ShouldBe("Latin1_General_100_CI_AI");
    }

    [Fact]
    public void GivenAContainsModifier_WhenLowered_ThenUsesLikeWithContainsMatch()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Contains), new StringSearchValue("mit"));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        var like = cte.Predicate.ShouldBeOfType<Predicate.Like>();
        like.Match.ShouldBe(LikeMatch.Contains);
    }

    [Fact]
    public void GivenAValueLongerThan256Chars_WhenLowered_ThenComparesTextOverflowInstead()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var longValue = new string('A', 300);
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue(longValue));

        // Act
        var cte = StringLoweringRule.Lower(predicate, (StringSearchValue)predicate.Value, ContextResolving(parameter, 202));

        // Assert
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("TextOverflow");
        equal.Value.Value.ShouldBe(longValue);
    }
}
```

- [ ] **Step 2: Run to confirm failure, then implement**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/StringLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a String search value to a ParamSource over StringSearchParam. Values within the inline
/// width match Text directly; values beyond it match TextOverflow directly -- correct now that this
/// plan's task 1 makes TextOverflow hold the whole value, matching fhir-server's convention.
/// fhir-server also adds a redundant Text-prefix-seek check for the overflow case as a performance
/// optimization (its own index can still be used); this rule is correct without that optimization,
/// which is a documented follow-up, not required here.
/// </summary>
public static class StringLoweringRule
{
    private const string CaseInsensitiveCollation = "Latin1_General_100_CI_AI";
    private const string CaseSensitiveCollation = "Latin1_General_100_CS_AS";

    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, StringSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var inlineWidth = table.Column("Text").MaxLength
            ?? throw new InvalidOperationException("StringSearchParam.Text has no MaxLength in SqlCatalog.");

        var column = new SqlColumnRef(table.TableName, value.String.Length > inlineWidth ? "TextOverflow" : "Text");

        var exact = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Exact;
        var contains = predicate.Modifier?.SearchModifierCode == SearchModifierCode.Contains;
        var collation = exact ? CaseSensitiveCollation : CaseInsensitiveCollation;

        Predicate p = (exact, contains) switch
        {
            (true, _) => new Predicate.Equal(column, context.Parameter(value.String), collation),
            (false, true) => new Predicate.Like(column, context.Parameter(value.String), LikeMatch.Contains, collation),
            _ => new Predicate.Like(column, context.Parameter(value.String), LikeMatch.StartsWith, collation),
        };

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), p);
    }
}
```

- [ ] **Step 3: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~StringLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, all four tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add StringLoweringRule, the compiler's first tier-1 leaf rule

Matches the design doc's own worked example (:exact against Text with
CS_AS collation). Values past the inline width match TextOverflow
directly -- correct per task 1's write-convention fix. fhir-server's
Text-prefix-seek optimization for the overflow case is a documented
follow-up, not implemented here."
```

---

### Task 7: `TokenLoweringRule`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/TokenLoweringRule.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/TokenLoweringRuleTests.cs`

**Interfaces:**
- Consumes: `LeafContext` (task 5), `TokenSearchValue`.
- Produces: `TokenLoweringRule.Lower(SearchParameterPredicateExpression, TokenSearchValue, LeafContext): CteDefinition.ParamSource`.

**Scope note:** `TokenSearchParam.SystemId` requires resolving a system URI string to an integer ID -- `ISymbolResolver` has no such method today (only `SearchParamId`/`ResourceTypeId`). This rule handles the code-only case (no `System` specified); a system-qualified token throws `NotSupportedException` rather than silently ignoring the system filter, which would return too many rows silently.

- [ ] **Step 1: Write the failing tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/TokenLoweringRuleTests.cs
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

public class TokenLoweringRuleTests
{
    private static LeafContext ContextResolving(SearchParameterInfo parameter, short searchParamId)
        => new(new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = searchParamId },
            new Dictionary<string, short>()));

    [Fact]
    public void GivenACodeOnlyToken_WhenLowered_ThenComparesCodeColumnOnly()
    {
        // Arrange
        var parameter = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));

        // Act
        var cte = TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 44));

        // Assert
        cte.SearchParamId.ShouldBe((short)44);
        var equal = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        equal.Column.Column.ShouldBe("Code");
        equal.Value.Value.ShouldBe("true");
    }

    [Fact]
    public void GivenASystemQualifiedToken_WhenLowered_ThenThrowsRatherThanSilentlyIgnoringTheSystem()
    {
        // Arrange
        var parameter = new SearchParameterInfo("identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: "http://example.org/mrn", code: "12345", text: null));

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            TokenLoweringRule.Lower(predicate, (TokenSearchValue)predicate.Value, ContextResolving(parameter, 55)));
    }
}
```

- [ ] **Step 2: Run to confirm failure, then implement**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/TokenLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Token search value to a ParamSource over TokenSearchParam. Code-only case only --
/// system-qualified tokens need SystemId resolution, which ISymbolResolver does not support yet, so
/// they throw rather than silently ignoring the system filter (a silent wrong-answer would be worse
/// than a loud failure).
/// </summary>
public static class TokenLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, TokenSearchValue value, LeafContext context)
    {
        if (!string.IsNullOrEmpty(value.System))
        {
            throw new NotSupportedException(
                "System-qualified token search requires SystemId resolution, which ISymbolResolver does not " +
                "support yet -- see docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-phase4-5-ast-emit-lower.md task 7's scope note.");
        }

        var table = SqlCatalog.Default.Table("TokenSearchParam");
        var column = new SqlColumnRef(table.TableName, "Code");
        var predicateExpr = new Predicate.Equal(column, context.Parameter(value.Code));

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), predicateExpr);
    }
}
```

- [ ] **Step 3: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~TokenLoweringRuleTests" --nologo
```

Expected: 0 warnings, 0 errors, both tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add TokenLoweringRule (code-only)

System-qualified tokens throw NotSupportedException rather than
silently dropping the system filter -- SystemId resolution isn't
supported by ISymbolResolver yet. A documented scope limit, not a
silent gap."
```

---

### Task 8: `ReferenceLoweringRule` (extends `Resolve` to collect resource types)

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/ReferenceLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs` (also collect `ReferenceSearchValue.ResourceType` when present)
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs` (resolve the collected resource types into `SymbolTable`'s `resourceTypeIds`)
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/ReferenceLoweringRuleTests.cs`
- Test: extend `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs` with a case proving resource-type resolution now happens for `ReferenceSearchValue` leaves.

**Interfaces:**
- Consumes: `LeafContext.ResourceTypeId(string)` (task 5, wraps `SymbolTable.ResourceTypeId`, already existed since Phase 3 but was never populated), `ReferenceSearchValue`.
- Produces: `ReferenceLoweringRule.Lower(...)`. Also changes `Resolve.RunAsync`'s behavior: `SymbolTable.ResourceTypeId` now succeeds for any resource type referenced by a `ReferenceSearchValue` leaf in the tree (still empty for resource types touched only by chain/compartment -- that generalization stays Phase 6/8's job per the roadmap).

**Scope note:** This is a narrow, targeted extension -- only `ReferenceSearchValue.ResourceType` gets collected and resolved. The roadmap's Phase 6 note about extending `SymbolCollectingVisitor` "non-trivially" for chain's `TargetResourceTypes` is a separate, larger generalization; do not attempt it here.

- [ ] **Step 1: Read the current `SymbolCollectingVisitor` and `Resolve` before modifying**

```bash
cat src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs
cat src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs
```

Confirm the exact current shape matches what Phase 3 shipped (a `HashSet<SearchParameterInfo> Parameters` collected via `VisitSearchParameterPredicate`/`VisitCompositeComponent`, and `Resolve.RunAsync` always returning an empty `resourceTypeIds` dictionary) before editing -- adjust the steps below if it has drifted.

- [ ] **Step 2: Write the failing `Resolve` test**

```csharp
// test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs -- add this test to the existing class
[Fact]
public async Task GivenATreeWithAReferencePredicate_WhenResolved_ThenSymbolTableHasItsResourceTypeId()
{
    // Arrange
    var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
    var predicate = new SearchParameterPredicateExpression(
        parameter, SearchComparator.Eq, modifier: null,
        new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "123"));
    var resolver = new FakeSymbolResolver();
    resolver.SearchParamIds[parameter.Url.ToString()] = 77;
    resolver.ResourceTypeIds["Patient"] = 103;

    // Act
    var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);

    // Assert
    symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
}
```

Verify `ReferenceSearchValue`'s real constructor signature/field names first (`grep -n "class ReferenceSearchValue" -A 20 src/Core/Ignixa.Search/Indexing/SearchValues/ReferenceSearchValue.cs`) and correct this test's construction if it disagrees -- do not guess.

- [ ] **Step 3: Run to confirm it fails**

```bash
dotnet test All.sln --filter "FullyQualifiedName~GivenATreeWithAReferencePredicate" --nologo
```

Expected: FAIL, `SymbolTable.ResourceTypeId("Patient")` throws `KeyNotFoundException`.

- [ ] **Step 4: Extend `SymbolCollectingVisitor` to also collect resource types from `ReferenceSearchValue` leaves**

```csharp
// src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs -- add a resource-type collection
public HashSet<string> ResourceTypes { get; } = [];

public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
{
    Parameters.Add(expression.Parameter);
    if (expression.Value is ReferenceSearchValue { ResourceType: { Length: > 0 } resourceType })
    {
        ResourceTypes.Add(resourceType);
    }
    return expression;
}
```

(Keep the existing `VisitCompositeComponent` override unchanged -- composites are out of scope for this plan.)

- [ ] **Step 5: Extend `Resolve.RunAsync` to resolve the collected resource types**

```csharp
// src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs -- replace the empty resourceTypeIds construction
var resourceTypeIds = new Dictionary<string, short>();
foreach (var resourceType in collector.ResourceTypes)
{
    var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
    if (id.HasValue)
    {
        resourceTypeIds[resourceType] = id.Value;
    }
}
```

- [ ] **Step 6: Run to confirm the `Resolve` test passes**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ResolveTests" --nologo
```

Expected: 0 warnings, 0 errors, all `ResolveTests` pass including the new one.

- [ ] **Step 7: Write the failing `ReferenceLoweringRule` tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/ReferenceLoweringRuleTests.cs
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

public class ReferenceLoweringRuleTests
{
    [Fact]
    public void GivenATypedReference_WhenLowered_ThenComparesResourceTypeAndResourceId()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: "Patient", resourceId: "123"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103 });
        var context = new LeafContext(symbols);

        // Act
        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, context);

        // Assert
        cte.SearchParamId.ShouldBe((short)77);
        var and = cte.Predicate.ShouldBeOfType<Predicate.And>();
        var typeEqual = and.Left.ShouldBeOfType<Predicate.Equal>();
        typeEqual.Column.Column.ShouldBe("ReferenceResourceTypeId");
        typeEqual.Value.Value.ShouldBe((short)103);
        var idEqual = and.Right.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
        idEqual.Value.Value.ShouldBe("123");
    }

    [Fact]
    public void GivenAnUntypedReference_WhenLowered_ThenComparesResourceIdOnly()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, modifier: null,
            new ReferenceSearchValue(ReferenceKind.Internal, baseUri: null!, resourceType: string.Empty, resourceId: "123"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 77 },
            new Dictionary<string, short>());
        var context = new LeafContext(symbols);

        // Act
        var cte = ReferenceLoweringRule.Lower(predicate, (ReferenceSearchValue)predicate.Value, context);

        // Assert
        var idEqual = cte.Predicate.ShouldBeOfType<Predicate.Equal>();
        idEqual.Column.Column.ShouldBe("ReferenceResourceId");
    }
}
```

Verify `ReferenceSearchValue`'s real constructor/field names against the actual source before finalizing this test (same caveat as step 2).

- [ ] **Step 8: Implement `ReferenceLoweringRule`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/ReferenceLoweringRule.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers a Reference search value to a ParamSource over ReferenceSearchParam. Handles the common
/// relative-reference case (resource type + resource id); BaseUri (absolute/external references) and
/// ReferenceResourceVersion are out of scope for this rule -- documented, not silently dropped.
/// </summary>
public static class ReferenceLoweringRule
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, ReferenceSearchValue value, LeafContext context)
    {
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var idPredicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "ReferenceResourceId"), context.Parameter(value.ResourceId));

        Predicate combined = string.IsNullOrEmpty(value.ResourceType)
            ? idPredicate
            : new Predicate.And(
                new Predicate.Equal(
                    new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"),
                    context.Parameter(context.ResourceTypeId(value.ResourceType))),
                idPredicate);

        return new CteDefinition.ParamSource(table, context.SearchParamId(predicate.Parameter), combined);
    }
}
```

- [ ] **Step 9: Run to confirm all new tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~ReferenceLoweringRuleTests|FullyQualifiedName~ResolveTests" --nologo
dotnet build All.sln --nologo
```

Expected: 0 warnings, 0 errors, all pass.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add ReferenceLoweringRule; extend Resolve to collect resource types

SymbolCollectingVisitor now also collects ReferenceSearchValue.ResourceType,
and Resolve resolves those into SymbolTable's ResourceTypeId lookup --
previously always empty. A narrow, targeted extension (only reference
leaves), not the generalized chain-target-type resolution Phase 6 will
need. BaseUri and ReferenceResourceVersion are out of scope for the
lowering rule itself -- documented, not silently ignored."
```

---

### Task 9: `StructuralContext`, `LeafLoweringDispatcher`, `Lower` entry point (`And`/`Or` only)

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs`
- Create: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Create: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`

**Interfaces:**
- Consumes: `LeafContext` (task 5), `StringLoweringRule`/`TokenLoweringRule`/`ReferenceLoweringRule` (tasks 6-8), `SymbolTable` (Phase 3), `Ignixa.Search.Expressions.Expression`/`MultiaryExpression`/`MultiaryOperator` (confirmed shape: `MultiaryExpression(MultiaryOperator MultiaryOperation, IReadOnlyList<Expression> Expressions)`, `MultiaryOperator { And, Or }`).
- Produces: `Lower.Run(Expression, SymbolTable, int? top = null): QueryPlan` -- the entry point task 10's end-to-end test calls directly after `Resolve.RunAsync`.

- [ ] **Step 1: Write the dispatcher**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/LeafLoweringDispatcher.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Dispatches a leaf predicate to its tier-1 lowering rule by the runtime type of its ISearchValue.
/// Date/Number/Quantity/Uri and all composites throw -- out of scope for this plan (see this plan's
/// global constraints).
/// </summary>
public static class LeafLoweringDispatcher
{
    public static CteDefinition.ParamSource Lower(SearchParameterPredicateExpression predicate, LeafContext context) => predicate.Value switch
    {
        StringSearchValue s => StringLoweringRule.Lower(predicate, s, context),
        TokenSearchValue t => TokenLoweringRule.Lower(predicate, t, context),
        ReferenceSearchValue r => ReferenceLoweringRule.Lower(predicate, r, context),
        _ => throw new NotSupportedException(
            $"No lowering rule for {predicate.Value.GetType().Name} -- Date/Number/Quantity/Uri and composites are out of scope for this plan."),
    };
}
```

- [ ] **Step 2: Write `StructuralContext`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The tier-2 (structural) context: builds the CTE graph by dispatching leaves to tier-1 rules and
/// combining their results. Owns the plan's Ctes list -- LeafContext (tier 1) never sees it.
/// </summary>
public sealed class StructuralContext
{
    private readonly List<CteDefinition> _ctes = [];
    private readonly LeafContext _leafContext;

    public StructuralContext(SymbolTable symbols)
    {
        _leafContext = new LeafContext(symbols);
    }

    public IReadOnlyList<CteDefinition> Ctes => _ctes;

    public CteRef Lower(SearchParameterPredicateExpression predicate)
    {
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext);
        _ctes.Add(cte);
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef Intersect(CteRef left, CteRef right)
    {
        _ctes.Add(new CteDefinition.Intersect(left, right));
        return new CteRef(_ctes.Count - 1);
    }

    public CteRef Union(IReadOnlyList<CteRef> parts)
    {
        _ctes.Add(new CteDefinition.Union(parts));
        return new CteRef(_ctes.Count - 1);
    }
}
```

- [ ] **Step 3: Write the failing `Lower` tests**

```csharp
// test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs
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

public class LowerTests
{
    [Fact]
    public void GivenASingleLeafPredicate_WhenLowered_ThenProducesAOneCteQueryPlan()
    {
        // Arrange
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [parameter.Url.ToString()] = 202 },
            new Dictionary<string, short>());

        // Act
        var plan = Lower.Run(predicate, symbols);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Match.ShouldBe(new CteRef(0));
        plan.Ctes[0].ShouldBeOfType<CteDefinition.ParamSource>();
    }

    [Fact]
    public void GivenTwoAndedLeafPredicates_WhenLowered_ThenProducesAnIntersectOverBothCtes()
    {
        // Arrange
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var namePredicate = new SearchParameterPredicateExpression(
            nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var activePredicate = new SearchParameterPredicateExpression(
            activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null));
        var tree = new MultiaryExpression(MultiaryOperator.And, [namePredicate, activePredicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [nameParam.Url.ToString()] = 202, [activeParam.Url.ToString()] = 44 },
            new Dictionary<string, short>());

        // Act
        var plan = Lower.Run(tree, symbols, top: 10);

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Match.ShouldBe(new CteRef(2));
        plan.Top.ShouldBe(10);
    }

    [Fact]
    public void GivenAnUnsupportedExpressionShape_WhenLowered_ThenThrowsRatherThanSilentlyDroppingIt()
    {
        // Arrange -- NotExpression is out of scope (":not" needs ResourceTypeId-based seed synthesis, not built yet)
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var notExpression = Expression.Not(predicate);
        var symbols = new SymbolTable(new Dictionary<string, short> { [parameter.Url.ToString()] = 202 }, new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<NotSupportedException>(() => Lower.Run(notExpression, symbols));
    }
}
```

Verify `Expression.Not(...)`'s real static-factory signature (`grep -n "static Expression Not" src/Core/Ignixa.Search/Expressions/Expression.cs`) before finalizing the third test -- adjust if it takes different arguments.

- [ ] **Step 4: Run to confirm failure, then implement `Lower`**

```csharp
// src/Core/Ignixa.Search.Sql/Lowering/Lower.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// The compiler's Lower stage, narrowed to this plan's scope: turns a bound Expression tree of
/// ANDed/ORed SearchParameterPredicateExpression leaves (String/Token/Reference only) into a
/// QueryPlan. Composites, chain, include, sort, and :not are not handled -- see this plan's global
/// constraints for the full list and why.
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
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context)).ToList()),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };

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

- [ ] **Step 5: Run to confirm tests pass**

```bash
dotnet test All.sln --filter "FullyQualifiedName~LowerTests" --nologo
dotnet build All.sln --nologo
grep -i "EntityFrameworkCore\|AspNetCore" src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```

Expected: 0 warnings, 0 errors, all three tests pass, grep returns nothing.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(search-sql): add StructuralContext, LeafLoweringDispatcher, Lower entry point

Tier 2 covers And->Intersect and Or->Union only -- :not (needs
ResourceTypeId-based seed synthesis), chain, include, sort, and
composites are out of scope and throw NotSupportedException rather than
silently producing a wrong plan. This is the first point where Resolve's
SymbolTable output and the tier-1 leaf rules compose into a real
QueryPlan."
```

---

### Task 10: End-to-end proof -- `Resolve -> Lower -> Emit`

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs` (live-DB, `[Fact(Skip = ...)]`-gated per established Phase 3 convention)

**Interfaces:**
- Consumes: `Resolve.RunAsync` (Phase 3), `Lower.Run` (task 9), `Emit.Run` (task 4), `SqlEntityFrameworkSymbolResolver` (Phase 3 task 5).
- Produces: nothing new -- this is the proof that everything built in this plan composes correctly, in-memory and (if a live SQL Server is reachable) against a real database. Matches the user's stated priority that the CTE-graph IR mechanism be shown working, not just unit-tested piecewise.

- [ ] **Step 1: Write the in-memory pipeline test**

```csharp
// test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests;

public class EndToEndCompilationTests
{
    private sealed class FakeSymbolResolver : ISymbolResolver
    {
        public Dictionary<string, short> SearchParamIds { get; } = [];
        public Dictionary<string, short> ResourceTypeIds { get; } = [];

        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);
    }

    [Fact]
    public async Task GivenAPatientNameExactAndActiveQuery_WhenCompiled_ThenProducesTheExpectedPlanAndSql()
    {
        // Arrange -- Patient?name:exact=Smith&active=true
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var activeParam = new SearchParameterInfo("active", "active", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-active"));
        var tree = new MultiaryExpression(MultiaryOperator.And,
        [
            new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith")),
            new SearchParameterPredicateExpression(activeParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "true", text: null)),
        ]);
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[nameParam.Url!.ToString()] = 202;
        resolver.SearchParamIds[activeParam.Url!.ToString()] = 44;

        // Act
        var symbolTable = await Resolve.RunAsync(tree, resolver, CancellationToken.None);
        var plan = Lower.Run(tree, symbolTable, top: 10);
        var emitted = Emit.Run(plan);

        // Assert -- the plan-shape golden test
        plan.Explain().ShouldBe(
            "cte0 = StringSearchParam[202]  Text = @p0 collate CS_AS\n" +
            "cte1 = TokenSearchParam[44]  Code = @p1\n" +
            "root = Intersect(cte0, cte1) top 10");

        // Assert -- no user value ever appears in SQL text
        emitted.Sql.ShouldNotContain("Smith");
        emitted.Sql.ShouldNotContain("true");
        emitted.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)"Smith"), ("@p1", (object)"true")]);
    }
}
```

- [ ] **Step 2: Run to confirm it passes (everything needed already exists from tasks 1-9)**

```bash
dotnet test All.sln --filter "FullyQualifiedName~EndToEndCompilationTests" --nologo
```

Expected: PASS. If it fails, the failure is in composition (tasks 1-9's pieces individually passed but don't fit together) -- debug the actual mismatch rather than adjusting the assertion to match broken output.

- [ ] **Step 3: Write the live-database proof, gated like Phase 3's**

```csharp
// test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlEntityFramework.IntegrationTests;

/// <summary>
/// Proves Resolve -> Lower -> Emit compiles to SQL that actually returns the right resource when
/// executed against a live SQL Server -- not just that each stage's unit tests pass in isolation.
/// </summary>
public class CompiledSearchEndToEndTests
{
    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING must be set to run this test (see docker-compose.test.yml).");
        }

        return connectionString;
    }

    [Fact(Skip = "Manual integration test -- requires TEST_SQL_CONNECTION_STRING and a live SQL Server, not part of CI")]
    public async Task GivenARealDatabase_WhenCompiledQueryIsExecuted_ThenReturnsTheSeededResource()
    {
        // Arrange -- seed one Patient row's StringSearchParam(name) via the same real seeding
        // mechanism Phase 3 task 5 used (SearchIndexReferenceDataCache + a real resource merge) --
        // find and reuse that project's established resource-seeding helper rather than hand-rolling
        // INSERTs, matching this project's existing integration-test convention.
        var connectionString = GetConnectionString();
        var options = new DbContextOptionsBuilder<FhirDbContext>().UseSqlServer(connectionString).Options;
        await using var context = new FhirDbContext(options);
        var initializer = new DatabaseInitializer(context, NullLogger<DatabaseInitializer>.Instance, "Development");
        await initializer.InitializeAsync();

        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://ignixa.dev/fhir/task10/SearchParameter/patient-name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));

#pragma warning disable CA2000
        var cache = new SearchIndexReferenceDataCache(context, NullLogger<SearchIndexReferenceDataCache>.Instance);
#pragma warning restore CA2000
        await cache.SyncSearchParametersToDatabase([parameter.Url!.ToString()], null!);
        var searchParamId = await context.SearchParams.AsNoTracking()
            .Where(sp => sp.Uri == parameter.Url.ToString()).Select(sp => sp.SearchParamId).SingleAsync();

        // Seed one StringSearchParam row directly -- proving the compiled query reads real rows,
        // not asserting on the row-generation path (that's task 1's job).
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax) VALUES (103, 999001, {searchParamId}, 'Smith', NULL, 0, 0)");

        var resolver = new SqlEntityFrameworkSymbolResolver(cache);

        // Act
        var symbolTable = await Resolve.RunAsync(predicate, resolver, CancellationToken.None);
        var plan = Lower.Run(predicate, symbolTable);
        var emitted = Emit.Run(plan);

        await using var command = new SqlCommand(emitted.Sql, (SqlConnection)context.Database.GetDbConnection());
        await context.Database.OpenConnectionAsync();
        foreach (var p in emitted.Parameters) command.Parameters.AddWithValue(p.Name, p.Value);
        await using var reader = await command.ExecuteReaderAsync();

        // Assert
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt64(1).ShouldBe(999001L);
        (await reader.ReadAsync()).ShouldBeFalse();
    }
}
```

- [ ] **Step 4: Run it if a live SQL Server is reachable**

Check for a running SQL Server test container first. If reachable, remove `Skip` locally, run, confirm it passes, then restore `Skip` before committing -- if not reachable, say so honestly rather than claiming verification that didn't happen (same discipline Phase 3 task 5 already established for this exact situation).

- [ ] **Step 5: Build and run the full non-E2E suite**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

Expected: 0 warnings, 0 errors, all green (aside from any pre-existing unrelated failures like the `sql-on-fhir-tests` submodule gap noted in prior phases -- confirm any failure is pre-existing before treating it as caused by this plan).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test(search-sql): prove Resolve -> Lower -> Emit end to end

In-memory: Patient?name:exact=Smith&active=true compiles to the expected
plan shape (via Explain()) and SQL with no user value inlined into text.
Live-database (Skip-gated, matching Phase 3's established convention):
the same pipeline's emitted SQL executed against a real seeded row.

Closes the narrowed phase 4+5 scope of
docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md:
CTE-graph plan IR, Explain(), Emit, the TextOverflow write-convention
fix, and Lower's structural (And/Or) plus String/Token/Reference tier-1
rules. Date/Number/Quantity/Uri, all six composites, :not, chain,
include, sort, and continuation remain for future phases."
```

## Self-Review

- **Spec coverage:** Task 1 covers the TextOverflow write-convention fix (narrowed to `StringSearchParam`, per this plan's own scope decision). Tasks 2-4 cover Phase 4's full scope (CTE-graph plan IR, `Explain()`, `Emit`) against hand-built plans, with no dependency on `Lower`. Tasks 5-9 cover Phase 5's scope narrowed to `String`/`Token`/`Reference` tier-1 rules and `And`/`Or` tier-2 rules. Task 10 proves the full pipeline composes, in-memory and (if reachable) against a live database. Every deliberate scope cut is named in the Global Constraints section with a reason, matching this roadmap's established pattern (e.g. Phase 3's narrowing to 3 tables) rather than silently doing less than "Phase 4+5" implies.
- **Placeholder scan:** Task 1 steps 2/7, task 6/8's `SearchParameterInfo`/`ReferenceSearchValue` construction, and task 9's `Expression.Not(...)` signature are marked "verify against real source before finalizing" rather than asserted as fact, matching the established honest-deferral pattern from every prior phase's plan in this repo.
- **Type consistency:** `CteRef`, `Predicate`/`Predicate.Equal`/`Predicate.Like`/`Predicate.And`, `CteDefinition`/`CteDefinition.ParamSource`/`.Intersect`/`.Union`, `QueryPlan(Ctes, Match, Top)`, `LeafContext.SearchParamId`/`.ResourceTypeId`/`.Parameter`, `StructuralContext.Lower`/`.Intersect`/`.Union`, `Emit.Run`, `Lower.Run` are used identically across tasks 2-10 -- checked for drift, none found.
- **Dependency ordering:** Tasks 2-4 (plan IR, `Explain`, `Emit`) have zero dependency on `Lower` and can be fully verified before task 5 starts. Tasks 5-8 (leaf rules) depend only on `LeafContext`, not on each other or on `StructuralContext` -- each is independently testable in isolation, matching this skill's task-right-sizing guidance ("a reviewer could meaningfully reject one task while approving its neighbor").
