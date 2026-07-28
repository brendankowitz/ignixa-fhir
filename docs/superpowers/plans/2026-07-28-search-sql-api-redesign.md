# Search.Sql Compiler API Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three hand-chained static stages (`Resolve.RunAsync` → `Lower.Run` → `SqlBuilder.Run`) and the misfiled `Tracing.SearchCompiler` with a two-phase public facade — `ISearchSqlCompiler.CreatePlanAsync(...)` returning a `SearchPlan`, and `SearchPlan.Compile()` returning a `CompiledSearch`.

**Architecture:** One internal `CompilationContext` record is built once from the caller's inputs and consumed by both Resolve and Lower, so the two stages can no longer observe divergent inputs (the defect class that shipped four times). The split between `CreatePlanAsync` (async — Resolve is the only I/O) and `Compile()` (sync — Lower and Emit are pure) puts the I/O boundary in the type system and gives callers a seam to inspect or rewrite the `QueryPlan` before SQL is emitted. Diagnostics become opt-in via `SearchDiagnosticsLevel`; failures are surfaced twice — thrown by `CreatePlan*`/`Compile`, returned as data by `TryCreatePlan*`/`TryCompile`.

**Tech Stack:** .NET 9, C# 13, xUnit + Shouldly + NSubstitute, `dotnet build All.sln` / `dotnet test All.sln`.

**Source of truth:** `docs/superpowers/specs/2026-07-28-search-sql-api-design.md`. Read it before starting.

---

## Ground rules for every task

- **Warnings are errors.** `dotnet build All.sln` must end 0 warnings, 0 errors before any commit.
- **Golden SQL and corpus tests must not move.** This refactor emits byte-identical SQL for every existing case. A changed golden file means the refactor changed behaviour and is wrong — stop and investigate rather than re-baselining.
- **One type per file**, file-scoped namespaces, 4-space indent, nullable enabled, no `#region`, no inline comments unless the code genuinely needs an invariant explained.
- Async methods take `CancellationToken cancellationToken` (never `ct`).
- Tests are AAA and named `GivenContext_WhenAction_ThenResult`.
- **Never `git commit` without asking the user first.** Each task ends with a commit step; run it only after the user approves.

## Known deviation from the spec, decided here

The spec declares `public enum CompilationStage { Build, Resolve, Lower, Emit }`. An enum with the same shape already exists as `Ignixa.Search.Parsing.TraceStage { Parse, Resolve, Lower, Emit }`, and `ParameterOutcome.Failed` takes a `TraceStage`.

We still introduce `CompilationStage`, because the SQL package should own the vocabulary in its own public failure contract rather than re-export a Parsing-package enum. The cost is one 4-line mapping inside `CompilationDiagnosticsBuilder` (`Build → Parse`, the other three 1:1) at the single point where a `ParameterOutcome.Failed` is stamped. That mapping lives in Task 6.

## File structure

### New production files — `src/Core/Ignixa.Search.Sql/`

| File | Responsibility |
|---|---|
| `SearchDiagnosticsLevel.cs` | public enum: `None`, `Parameters`, `Full` |
| `SearchPlanOptions.cs` | public record: everything the caller controls that is not the query itself |
| `Compilation/CompilationContext.cs` | internal record: the single set of inputs both stages read |
| `Compilation/CompilationContextMapping.cs` | internal: `Mapped` / `NotApplicable` — the classification the completeness test enforces |
| `Compilation/SymbolResolution.cs` | internal record: resolver + the two optional definition managers |
| `Compilation/CompilationDiagnosticsBuilder.cs` | internal: the diagnostics/attribution helpers lifted out of `Tracing.SearchCompiler` |
| `CompilationStage.cs` | public enum: `Build`, `Resolve`, `Lower`, `Emit` |
| `SearchCompilationFailure.cs` | public record: stage, message, attribution, diagnostics |
| `SearchCompilationException.cs` | public exception wrapping a failure |
| `SearchCompilationDiagnostics.cs` | public record: parameters, implicit, plan trace, SQL text ranges |
| `SearchPlan.cs` | public record: `Query`, `Diagnostics`, `DiagnosticsLevel`, `Compile()`, `TryCompile()` |
| `CompiledSearch.cs` | public record: `Sql`, `Parameters`, `Plan`, `Diagnostics` |
| `SearchPlanResult.cs` | public record: `Plan?` / `Failure?` |
| `SearchCompilationResult.cs` | public record: `Compiled?` / `Failure?` |
| `ISearchSqlCompiler.cs` | public interface: the four entry points |
| `SearchSqlCompiler.cs` | public sealed class: the only orchestrator |

### Moved production files

| From | To |
|---|---|
| `Tracing/QueryPlanTrace.cs` | `QueryPlanTrace.cs` (namespace `Ignixa.Search.Sql`) |
| `Tracing/CteProvenance.cs` | `CteProvenance.cs` (namespace `Ignixa.Search.Sql`) |
| `Tracing/ImplicitParameter.cs` | `ImplicitParameter.cs` (namespace `Ignixa.Search.Sql`) |
| `Lowering/LowerOptions.cs` | `test/Ignixa.Search.Sql.Tests/TestSupport/LowerOptions.cs` |

### Deleted production files

`Tracing/SearchCompiler.cs`, `Tracing/SearchTrace.cs`, `Tracing/TraceFailure.cs`, `Tracing/EmittedSqlTrace.cs`. The `Tracing` folder and namespace disappear.

### New test files — `test/Ignixa.Search.Sql.Tests/TestSupport/`

| File | Responsibility |
|---|---|
| `LowerOptions.cs` | moved verbatim; harness input only |
| `CompilationContextFactory.cs` | builds a `CompilationContext` for a test; the idiom for new tests |
| `LowerHarness.cs` | reproduces today's `Lower.Run` argument list exactly |
| `ResolveHarness.cs` | reproduces today's `Resolve.RunAsync` argument list exactly |
| `FakeSymbolResolver.cs` | promoted out of `Tracing/SearchTraceFixtures.cs` (Task 9) |
| `FakeSearchOptionsBuilder.cs` | promoted out of `Tracing/SearchTraceFixtures.cs` (Task 9) |
| `PlanFixtures.cs` | `QueryPlan`s for tests that need a plan but not the plumbing (Task 9) |
| `CompilerFixtures.cs` | pre-wired `SearchSqlCompiler` instances for the facade tests (Task 10) |

---

## Phase 1 — internal foundations, no public API change

Phase 1 changes nothing a consumer can see. `Tracing.SearchCompiler` keeps working throughout; it is rewired onto the new context in Task 5 and deleted in Phase 4.

### Task 1: `SearchDiagnosticsLevel` and `SearchPlanOptions`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/SearchDiagnosticsLevel.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchPlanOptions.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/SearchPlanOptionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Search.Sql.Tests/Compilation/SearchPlanOptionsTests.cs`:

```csharp
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchPlanOptionsTests
{
    [Fact]
    public void GivenADefaultSearchPlanOptions_WhenReadingIt_ThenItIsTheLeanNonTracingShape()
    {
        var options = new SearchPlanOptions();

        options.CountOnly.ShouldBeFalse();
        options.IncludeLimit.ShouldBe(0);
        options.SortPhase.ShouldBe(SortPhase.Valued);
        options.CountPhaseScoped.ShouldBeFalse();
        options.IncludesOnly.ShouldBeFalse();
        options.Top.ShouldBeNull();
        options.Page.ShouldBeNull();
        options.OffsetPage.ShouldBeNull();
        options.SurrogateRange.ShouldBeNull();
        options.SearchParameterHash.ShouldBeNull();
        options.OperationExpression.ShouldBeNull();
        options.DiagnosticsLevel.ShouldBe(SearchDiagnosticsLevel.None);
    }

    [Fact]
    public void GivenSearchPlanOptions_WhenCopyingWithAChangedProperty_ThenTheOriginalIsUnchanged()
    {
        var original = new SearchPlanOptions { Top = 10 };

        var copy = original with { Top = 20 };

        original.Top.ShouldBe(10);
        copy.Top.ShouldBe(20);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchPlanOptionsTests"`

Expected: build failure — `The type or namespace name 'SearchPlanOptions' could not be found`.

- [ ] **Step 3: Write `SearchDiagnosticsLevel`**

Create `src/Core/Ignixa.Search.Sql/SearchDiagnosticsLevel.cs`:

```csharp
namespace Ignixa.Search.Sql;

/// <summary>
/// How much a compile records about its own work. The default is <see cref="None"/>: diagnostics cost
/// allocations on every compile, and a production search path wants none of them.
/// </summary>
public enum SearchDiagnosticsLevel
{
    /// <summary>Nothing is captured. No outcome list is passed to the builder and the plan explainer never runs.</summary>
    None = 0,

    /// <summary>Per-parameter outcomes, implicit parameters, and failure attribution.</summary>
    Parameters,

    /// <summary>Everything in <see cref="Parameters"/>, plus plan explain rows and SQL text ranges.</summary>
    Full,
}
```

- [ ] **Step 4: Write `SearchPlanOptions`**

Create `src/Core/Ignixa.Search.Sql/SearchPlanOptions.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql;

/// <summary>
/// Everything a caller controls about a compile that is not the query itself. Every property is optional;
/// the default instance is a plain, untraced, uncapped search.
/// </summary>
public sealed record SearchPlanOptions
{
    /// <summary>Emit a row count instead of the rows themselves.</summary>
    public bool CountOnly { get; init; }

    /// <summary>The per-stage cap on included resources; 0 means no cap.</summary>
    public int IncludeLimit { get; init; }

    /// <summary>Which phase of a two-phase sort this compile emits.</summary>
    public SortPhase SortPhase { get; init; } = SortPhase.Valued;

    /// <summary>
    /// Scopes a <see cref="CountOnly"/> count to the current sort phase's own join output. Requires
    /// <see cref="CountOnly"/> and at least one sort key.
    /// </summary>
    public bool CountPhaseScoped { get; init; }

    /// <summary>Return include-stage rows only, omitting the match page. The <c>$includes</c> second page.</summary>
    public bool IncludesOnly { get; init; }

    /// <summary>
    /// A SQL <c>TOP</c> cap; null means no cap. May be combined with <see cref="Page"/> to cap a keyset
    /// page. Mutually exclusive with <see cref="OffsetPage"/>, which carries its own row count.
    /// </summary>
    public int? Top { get; init; }

    /// <summary>
    /// A keyset continuation boundary. Mutually exclusive with <see cref="OffsetPage"/>. The compiler has
    /// always supported this; before this API existed no orchestrated entry point could ask for it.
    /// </summary>
    public PageSpec? Page { get; init; }

    /// <summary>An OFFSET/FETCH page. Mutually exclusive with <see cref="Page"/> and <see cref="Top"/>.</summary>
    public OffsetSpec? OffsetPage { get; init; }

    /// <summary>
    /// An inclusive surrogate-id bound. When set it wins over <c>SearchOptions.StartSurrogateId</c>/
    /// <c>EndSurrogateId</c>. Named to match <c>QueryPlan.SurrogateRange</c>, but typed as raw longs so a
    /// caller never has to construct the AST's <c>SurrogateIdRange</c> node.
    /// </summary>
    public (long Start, long End)? SurrogateRange { get; init; }

    /// <summary>
    /// The expected search-parameter hash for reindex gating; null when unused. Typed as a string so a
    /// caller never has to construct an AST node to express it.
    /// </summary>
    public string? SearchParameterHash { get; init; }

    /// <summary>
    /// A FHIR operation root such as <c>PatientEverythingExpression</c>, which no query string can produce.
    /// When set it replaces the expression the options builder derived.
    /// </summary>
    public Expression? OperationExpression { get; init; }

    /// <summary>How much this compile records about its own work.</summary>
    public SearchDiagnosticsLevel DiagnosticsLevel { get; init; } = SearchDiagnosticsLevel.None;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchPlanOptionsTests"`

Expected: PASS, 2 tests.

- [ ] **Step 6: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql/SearchDiagnosticsLevel.cs src/Core/Ignixa.Search.Sql/SearchPlanOptions.cs test/Ignixa.Search.Sql.Tests/Compilation/SearchPlanOptionsTests.cs
git commit -m "feat(search-sql): add SearchPlanOptions and SearchDiagnosticsLevel"
```

---

### Task 2: `CompilationContext`, its mapping table, and the completeness test

This is the task that closes the defect class. `CompilationContextMapping` names every `SearchOptions` property as either mapped into the context or explicitly not applicable with a reason, and a reflection test fails the build when a new property is neither.

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Compilation/SymbolResolution.cs`
- Create: `src/Core/Ignixa.Search.Sql/Compilation/CompilationContext.cs`
- Create: `src/Core/Ignixa.Search.Sql/Compilation/CompilationContextMapping.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/CompilationContextMappingTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/CompilationContextTests.cs`

- [ ] **Step 1: Write the failing completeness test**

Create `test/Ignixa.Search.Sql.Tests/Compilation/CompilationContextMappingTests.cs`:

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Compilation;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class CompilationContextMappingTests
{
    [Fact]
    public void GivenEverySearchOptionsProperty_WhenCreatingCompilationContext_ThenEachIsMappedOrExplicitlyExcluded()
    {
        var classified = CompilationContextMapping.Mapped
            .Concat(CompilationContextMapping.NotApplicable.Keys)
            .ToHashSet(StringComparer.Ordinal);

        typeof(SearchOptions).GetProperties()
            .Select(p => p.Name)
            .Where(name => !classified.Contains(name))
            .ShouldBeEmpty(
                "every SearchOptions property must be mapped into CompilationContext or listed in " +
                "CompilationContextMapping.NotApplicable with a stated reason");
    }

    [Fact]
    public void GivenTheMappingTable_WhenReadingIt_ThenNoPropertyIsBothMappedAndNotApplicable()
    {
        CompilationContextMapping.Mapped
            .Where(CompilationContextMapping.NotApplicable.ContainsKey)
            .ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheMappingTable_WhenReadingIt_ThenEveryClassifiedNameIsARealSearchOptionsProperty()
    {
        var real = typeof(SearchOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        CompilationContextMapping.Mapped
            .Concat(CompilationContextMapping.NotApplicable.Keys)
            .Where(name => !real.Contains(name))
            .ShouldBeEmpty("a stale entry hides a real gap");
    }

    [Fact]
    public void GivenTheNotApplicableTable_WhenReadingIt_ThenEveryReasonIsStated()
    {
        CompilationContextMapping.NotApplicable
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~CompilationContextMappingTests"`

Expected: build failure — `CompilationContextMapping` does not exist.

- [ ] **Step 3: Write `SymbolResolution`**

Create `src/Core/Ignixa.Search.Sql/Compilation/SymbolResolution.cs`:

```csharp
using Ignixa.Search.Definition;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The three collaborators Resolve needs, grouped so the compiler holds them once from construction
/// rather than threading three optional arguments through every call.
/// </summary>
/// <remarks>
/// Both definition managers are optional because most searches need neither. A compartment search or
/// <c>$everything</c> needs <see cref="CompartmentDefinitionManager"/>; a <c>_not-referenced</c> path
/// filter needs <see cref="SearchParameterDefinitionManager"/>. Each throws naming itself when a query
/// requires it and it was not supplied.
/// </remarks>
internal sealed record SymbolResolution(
    ISymbolResolver Resolver,
    ICompartmentDefinitionManager? CompartmentDefinitionManager = null,
    ISearchParameterDefinitionManager? SearchParameterDefinitionManager = null);
```

- [ ] **Step 4: Write `CompilationContext`**

Create `src/Core/Ignixa.Search.Sql/Compilation/CompilationContext.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The single set of inputs Resolve and Lower both read. Built once per compile so the two stages cannot
/// observe different values — the shape of every forwarding defect this compiler has shipped.
/// </summary>
internal sealed record CompilationContext
{
    public required Expression? Expression { get; init; }

    /// <summary>Null means a system-level or wildcard-compartment search. Normalized exactly once, in <see cref="Create"/>.</summary>
    public required string? TargetResourceType { get; init; }

    public required IReadOnlyList<IncludeExpression> Includes { get; init; }

    public required IReadOnlyList<IncludeExpression> RevIncludes { get; init; }

    public required IReadOnlyList<SortExpression> Sort { get; init; }

    public required IReadOnlyList<AccessConstraint> AccessConstraints { get; init; }

    public required IReadOnlyList<string> ResourceTypes { get; init; }

    public required DateTimeOffset ApproximationReferenceTime { get; init; }

    public required ResourceVisibility? Visibility { get; init; }

    public required SurrogateIdRange? SurrogateRange { get; init; }

    public required SearchPlanOptions Options { get; init; }

    public bool SystemLevelSearch => TargetResourceType is null;

    /// <summary>
    /// Maps a built <see cref="SearchOptions"/> and the caller's <see cref="SearchPlanOptions"/> onto the
    /// one context both stages read. This is the only place that mapping happens;
    /// <see cref="CompilationContextMapping"/> is its enforced contract.
    /// </summary>
    public static CompilationContext Create(
        SearchOptions searchOptions,
        string? targetResourceType,
        SearchPlanOptions options,
        DateTimeOffset approximationReferenceTime)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);
        ArgumentNullException.ThrowIfNull(options);

        return new CompilationContext
        {
            Expression = options.OperationExpression ?? searchOptions.Expression,
            TargetResourceType = string.IsNullOrEmpty(targetResourceType) ? null : targetResourceType,
            Includes = searchOptions.Include,
            RevIncludes = searchOptions.RevInclude,
            Sort = searchOptions.Sort,
            AccessConstraints = searchOptions.AccessConstraints ?? [],
            ResourceTypes = searchOptions.ResourceTypes ?? [],
            ApproximationReferenceTime = approximationReferenceTime,
            Visibility = ToVisibility(searchOptions.ResourceVersionTypes),
            SurrogateRange = ToSurrogateRange(options.SurrogateRange, searchOptions),
            Options = options,
        };
    }

    /// <summary>
    /// Maps <see cref="SearchOptions.ResourceVersionTypes"/> onto <see cref="ResourceVisibility"/>.
    /// <see cref="ResourceVersionTypes.Latest"/> alone returns null, which
    /// <see cref="QueryPlan.EffectiveVisibility"/> already treats as <see cref="ResourceVisibility.Current"/>.
    /// </summary>
    private static ResourceVisibility? ToVisibility(ResourceVersionTypes types) => types switch
    {
        ResourceVersionTypes.None => throw new NotSupportedException(
            "SearchOptions.ResourceVersionTypes.None is not a valid search input; a search must select at least Latest."),
        ResourceVersionTypes.Latest => null,
        _ => new ResourceVisibility(
            IncludeHistory: types.HasFlag(ResourceVersionTypes.History),
            IncludeDeleted: types.HasFlag(ResourceVersionTypes.SoftDeleted)),
    };

    /// <summary>
    /// The surrogate-id bound this compile applies: the explicit <see cref="SearchPlanOptions.SurrogateRange"/>
    /// when supplied, otherwise the <see cref="SearchOptions"/> pair. A half-open pair is a caller error,
    /// not a partial intent to honour.
    /// </summary>
    private static SurrogateIdRange? ToSurrogateRange((long Start, long End)? explicitRange, SearchOptions searchOptions)
    {
        if (explicitRange is { } range)
        {
            return new SurrogateIdRange(new SqlParameterRef(range.Start), new SqlParameterRef(range.End));
        }

        return (searchOptions.StartSurrogateId, searchOptions.EndSurrogateId) switch
        {
            (null, null) => null,
            ({ } start, { } end) => new SurrogateIdRange(new SqlParameterRef(start), new SqlParameterRef(end)),
            _ => throw new NotSupportedException(
                "SearchOptions.StartSurrogateId and EndSurrogateId must both be set or both be null."),
        };
    }
}
```

- [ ] **Step 5: Write `CompilationContextMapping`**

Create `src/Core/Ignixa.Search.Sql/Compilation/CompilationContextMapping.cs`:

```csharp
using System.Collections.Frozen;

namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The enforced contract for <see cref="CompilationContext.Create"/>: every property of
/// <c>SearchOptions</c> is either mapped into a compilation input or explicitly not applicable with a
/// stated reason.
/// </summary>
/// <remarks>
/// Four properties have, one at a time, been added to <c>SearchOptions</c>, accepted by the compiler, and
/// never forwarded — each a control that looked live and silently did nothing. A test over these two
/// collections fails the build when a fifth is added and classified as neither.
/// </remarks>
internal static class CompilationContextMapping
{
    /// <summary>The properties <see cref="CompilationContext.Create"/> reads.</summary>
    public static FrozenSet<string> Mapped { get; } = new[]
    {
        nameof(Search.Models.SearchOptions.Expression),
        nameof(Search.Models.SearchOptions.Sort),
        nameof(Search.Models.SearchOptions.Include),
        nameof(Search.Models.SearchOptions.RevInclude),
        nameof(Search.Models.SearchOptions.ResourceTypes),
        nameof(Search.Models.SearchOptions.StartSurrogateId),
        nameof(Search.Models.SearchOptions.EndSurrogateId),
        nameof(Search.Models.SearchOptions.ResourceVersionTypes),
        nameof(Search.Models.SearchOptions.AccessConstraints),
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The properties that deliberately do not become compilation inputs, and why.</summary>
    public static FrozenDictionary<string, string> NotApplicable { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(Search.Models.SearchOptions.MaxItemCount)] =
            "Callers transform it before a search runs — SearchResourcesHandler requests MaxItemCount + 1 to detect 'has more' — so forwarding it as Top would silently fight that transformation. Row capping is SearchPlanOptions.Top, Page, or OffsetPage.",
        [nameof(Search.Models.SearchOptions.ContinuationToken)] =
            "Decoding it into a keyset or OFFSET page is adapter logic in a different layer. The decoded result arrives as SearchPlanOptions.Page or OffsetPage.",
        [nameof(Search.Models.SearchOptions.Elements)] =
            "A serialization-time projection of the returned resource body, applied after the rows are read.",
        [nameof(Search.Models.SearchOptions.Total)] =
            "Bundle metadata. The compiler's only count concept is SearchPlanOptions.CountOnly, which the caller sets directly.",
        [nameof(Search.Models.SearchOptions.Summary)] =
            "A serialization-time projection, like Elements.",
        [nameof(Search.Models.SearchOptions.UnsupportedParams)] =
            "Builder output describing what it could not honour; it shapes the OperationOutcome, not the SQL.",
        [nameof(Search.Models.SearchOptions.BundleIssues)] =
            "Builder output, like UnsupportedParams.",
        [nameof(Search.Models.SearchOptions.ResourceType)] =
            "Superseded by the targetResourceType argument, which is normalized once in CompilationContext.Create so every stage observes the same value.",
        [nameof(Search.Models.SearchOptions.IncludesMaxItemCount)] =
            "The $includes operation's page size, applied by the caller. The compiler's per-stage cap is SearchPlanOptions.IncludeLimit.",
        [nameof(Search.Models.SearchOptions.IncludesContinuationToken)] =
            "Decoded by the adapter layer, like ContinuationToken.",
    }.ToFrozenDictionary(StringComparer.Ordinal);
}
```

- [ ] **Step 6: Run the completeness test to verify it passes**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~CompilationContextMappingTests"`

Expected: PASS, 4 tests. If the first test fails naming a property, `SearchOptions` gained one since this plan was written — classify it rather than deleting the assertion.

- [ ] **Step 7: Write the mapping-behaviour tests**

Create `test/Ignixa.Search.Sql.Tests/Compilation/CompilationContextTests.cs`:

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class CompilationContextTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenAnEmptyResourceType_WhenCreatingTheContext_ThenItIsNormalizedToNullAndTheSearchIsSystemLevel()
    {
        var context = CompilationContext.Create(new SearchOptions(), string.Empty, new SearchPlanOptions(), ReferenceTime);

        context.TargetResourceType.ShouldBeNull();
        context.SystemLevelSearch.ShouldBeTrue();
    }

    [Fact]
    public void GivenResourceVersionTypesNone_WhenCreatingTheContext_ThenItThrows()
    {
        var searchOptions = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.None };

        Should.Throw<NotSupportedException>(
            () => CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime));
    }

    [Fact]
    public void GivenResourceVersionTypesLatest_WhenCreatingTheContext_ThenVisibilityIsNull()
    {
        var searchOptions = new SearchOptions { ResourceVersionTypes = ResourceVersionTypes.Latest };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldBeNull();
    }

    [Fact]
    public void GivenResourceVersionTypesHistory_WhenCreatingTheContext_ThenVisibilityIncludesHistory()
    {
        var searchOptions = new SearchOptions
        {
            ResourceVersionTypes = ResourceVersionTypes.Latest | ResourceVersionTypes.History,
        };

        var context = CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime);

        context.Visibility.ShouldNotBeNull();
        context.Visibility!.IncludeHistory.ShouldBeTrue();
        context.Visibility.IncludeDeleted.ShouldBeFalse();
    }

    [Fact]
    public void GivenOnlyAStartSurrogateId_WhenCreatingTheContext_ThenItThrows()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1 };

        Should.Throw<NotSupportedException>(
            () => CompilationContext.Create(searchOptions, "Patient", new SearchPlanOptions(), ReferenceTime));
    }

    [Fact]
    public void GivenBothAnExplicitRangeAndSearchOptionsBounds_WhenCreatingTheContext_ThenTheExplicitRangeWins()
    {
        var searchOptions = new SearchOptions { StartSurrogateId = 1, EndSurrogateId = 2 };
        var options = new SearchPlanOptions { SurrogateRange = (10, 20) };

        var context = CompilationContext.Create(searchOptions, "Patient", options, ReferenceTime);

        context.SurrogateRange.ShouldNotBeNull();
        context.SurrogateRange!.Start.Value.ShouldBe(10L);
        context.SurrogateRange.End.Value.ShouldBe(20L);
    }

    [Fact]
    public void GivenAnOperationExpression_WhenCreatingTheContext_ThenItReplacesTheSearchExpressionWithoutMutatingSearchOptions()
    {
        var searchExpression = Expression.Missing(default!, true);
        var searchOptions = new SearchOptions { Expression = searchExpression };
        var operationExpression = Expression.Missing(default!, false);
        var options = new SearchPlanOptions { OperationExpression = operationExpression };

        var context = CompilationContext.Create(searchOptions, "Patient", options, ReferenceTime);

        context.Expression.ShouldBeSameAs(operationExpression);
        searchOptions.Expression.ShouldBeSameAs(searchExpression);
    }
}
```

> **Note for the implementer:** the last test needs two distinguishable `Expression` instances. `Expression.Missing(...)` is a placeholder — use whatever cheap factory the existing tests in `test/Ignixa.Search.Sql.Tests/Lowering/` already use to build a throwaway expression, and keep the two instances distinct so `ShouldBeSameAs` is meaningful. If `SurrogateIdRange.Start` is not an `SqlParameterRef` with a `Value` property, adjust the two `ShouldBe` assertions to match its real shape.

- [ ] **Step 8: Run the behaviour tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~CompilationContextTests"`

Expected: PASS, 7 tests.

- [ ] **Step 9: Full build and test**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 10: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql/Compilation test/Ignixa.Search.Sql.Tests/Compilation
git commit -m "feat(search-sql): add CompilationContext and enforce SearchOptions mapping completeness"
```

---

### Task 3: Collapse `Resolve.RunAsync` onto the context, behind a test harness

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs:35-51`
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/CompilationContextFactory.cs`
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/ResolveHarness.cs`
- Modify: every `Resolve.RunAsync(` call site in `test/` (23 of them) and in `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs`

- [ ] **Step 1: Write the harness and the context factory**

Create `test/Ignixa.Search.Sql.Tests/TestSupport/CompilationContextFactory.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Compilation;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>Builds a <see cref="CompilationContext"/> for a test without going through the facade.</summary>
internal static class CompilationContextFactory
{
    public static readonly DateTimeOffset DefaultReferenceTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static CompilationContext For(
        Expression? expression,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression>? includes = null,
        IReadOnlyList<IncludeExpression>? revIncludes = null,
        IReadOnlyList<SortExpression>? sort = null,
        IReadOnlyList<AccessConstraint>? accessConstraints = null,
        IReadOnlyList<string>? resourceTypes = null,
        DateTimeOffset? approximationReferenceTime = null,
        ResourceVisibility? visibility = null,
        SurrogateIdRange? surrogateRange = null,
        SearchPlanOptions? options = null)
        => new()
        {
            Expression = expression,
            TargetResourceType = string.IsNullOrEmpty(targetResourceType) ? null : targetResourceType,
            Includes = includes ?? [],
            RevIncludes = revIncludes ?? [],
            Sort = sort ?? [],
            AccessConstraints = accessConstraints ?? [],
            ResourceTypes = resourceTypes ?? [],
            ApproximationReferenceTime = approximationReferenceTime ?? DefaultReferenceTime,
            Visibility = visibility,
            SurrogateRange = surrogateRange,
            Options = options ?? new SearchPlanOptions(),
        };
}
```

Create `test/Ignixa.Search.Sql.Tests/TestSupport/ResolveHarness.cs`:

```csharp
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Reproduces the argument list <see cref="Resolve.RunAsync"/> had before it was collapsed onto
/// <see cref="CompilationContext"/>, so the existing corpus of Resolve tests migrates by renaming the
/// call and nothing else. New tests should build a context with <see cref="CompilationContextFactory"/>
/// and call <see cref="Resolve.RunAsync"/> directly.
/// </summary>
internal static class ResolveHarness
{
    public static Task<ResolvedSymbols> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        IReadOnlyList<SortExpression> sort,
        ISymbolResolver resolver,
        string? targetResourceType,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null,
        IReadOnlyList<string>? additionalResourceTypes = null,
        IReadOnlyList<AccessConstraint>? accessConstraints = null)
    {
        var context = CompilationContextFactory.For(
            expression,
            targetResourceType,
            includes,
            revIncludes,
            sort,
            accessConstraints,
            additionalResourceTypes);

        var deps = new SymbolResolution(resolver, compartmentDefinitionManager, searchParameterDefinitionManager);

        return Resolve.RunAsync(context, deps, cancellationToken);
    }
}
```

- [ ] **Step 2: Migrate the call sites**

In `test/`, find/replace `Resolve.RunAsync(` → `ResolveHarness.RunAsync(` and add `using Ignixa.Search.Sql.Tests.TestSupport;` to each touched file.

Run to find them: `git grep -n "Resolve\.RunAsync(" -- test`
Expected before: 23 hits. Expected after: 0.

- [ ] **Step 3: Collapse the signature**

In `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`, replace the parameter list at lines 35–51 with:

```csharp
    internal static async Task<ResolvedSymbols> RunAsync(
        CompilationContext context,
        SymbolResolution deps,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(deps);

        var expression = context.Expression;
        var includes = context.Includes;
        var revIncludes = context.RevIncludes;
        var sort = context.Sort;
        var resolver = deps.Resolver;
        var targetResourceType = context.TargetResourceType;
        var compartmentDefinitionManager = deps.CompartmentDefinitionManager;
        var searchParameterDefinitionManager = deps.SearchParameterDefinitionManager;
        var additionalResourceTypes = context.ResourceTypes;
        var accessConstraints = context.AccessConstraints;
```

The rest of the method body is unchanged — the locals keep every downstream reference working. Add `using Ignixa.Search.Sql.Compilation;` to the file.

> Leaving the locals in is deliberate for this task: it keeps the diff to the signature, so a reviewer can see that no logic moved. Inlining them is optional cleanup for a later commit, not part of this one.

Keep `public static class Resolve` public for now — Task 16 seals it, after every consumer has moved.

- [ ] **Step 4: Fix the one production call site**

In `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs`, both `Resolve.RunAsync(...)` calls (lines ~78 and ~214) must build a context. Task 5 rewires this class properly; for now, make it compile by constructing the context inline at each call:

```csharp
        var context = CompilationContext.Create(
            options,
            resourceType,
            new SearchPlanOptions
            {
                CountOnly = countOnly,
                IncludeLimit = includeLimit,
                SortPhase = sortPhase,
                CountPhaseScoped = countPhaseScoped,
                OffsetPage = offsetPage,
                SurrogateRange = surrogateIdRange,
            },
            approximationReferenceTime);

        var resolved = await Resolve.RunAsync(
            context,
            new SymbolResolution(resolver, compartmentDefinitionManager, searchParameterDefinitionManager),
            cancellationToken);
```

For `CompileWithTimeProviderAsync` the `SearchPlanOptions` is `new SearchPlanOptions { OperationExpression = operationExpression }` and the `options.Expression = operationExpression` mutation at line 75 is **deleted** — `CompilationContext.Create` now applies the override without touching the caller's object.

- [ ] **Step 5: Build and run the full SQL test suite**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: all tests pass, same count as before this task.

- [ ] **Step 6: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql test/Ignixa.Search.Sql.Tests
git commit -m "refactor(search-sql): collapse Resolve.RunAsync onto CompilationContext"
```

---

### Task 4: Collapse `Lower.Run` onto the context, behind a test harness

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs:30-57`
- Move: `src/Core/Ignixa.Search.Sql/Lowering/LowerOptions.cs` → `test/Ignixa.Search.Sql.Tests/TestSupport/LowerOptions.cs`
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/LowerHarness.cs`
- Modify: every `Lower.Run(` call site in `test/` (91 of them) and in `Tracing/SearchCompiler.cs`

- [ ] **Step 1: Move `LowerOptions` into the test project**

```bash
git mv src/Core/Ignixa.Search.Sql/Lowering/LowerOptions.cs test/Ignixa.Search.Sql.Tests/TestSupport/LowerOptions.cs
```

Change its namespace to `Ignixa.Search.Sql.Tests.TestSupport` and its accessibility to `internal sealed record LowerOptions`. Replace the `<summary>` with:

```csharp
/// <summary>
/// Test-support only. The optional inputs Lower.Run took before it was collapsed onto CompilationContext,
/// preserved so the existing corpus of lowering tests migrates by renaming the call and nothing else. No
/// production code references this type; new tests should build a CompilationContext directly.
/// </summary>
```

Keep every property exactly as it is, including the XML docs — the harness maps them one for one and a reviewer needs to see they did not change.

- [ ] **Step 2: Write the harness**

Create `test/Ignixa.Search.Sql.Tests/TestSupport/LowerHarness.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>
/// Reproduces the argument list <see cref="Lower.Run"/> had before it was collapsed onto
/// <c>CompilationContext</c>. Every argument, including the <see cref="LowerOptions"/> initialiser, is
/// unchanged, so migrating a test is a one-token rename.
/// </summary>
internal static class LowerHarness
{
    public static LoweredPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        IReadOnlyList<SortExpression> sort,
        SortPhase sortPhase,
        PageSpec? page,
        LowerOptions? options = null)
    {
        options ??= new LowerOptions();

        var context = CompilationContextFactory.For(
            expression,
            targetResourceType,
            includes,
            revIncludes,
            sort,
            options.AccessConstraints,
            options.ResourceTypes,
            options.ApproximationReferenceTime,
            options.Visibility,
            options.SurrogateRange,
            new SearchPlanOptions
            {
                CountOnly = options.CountOnly,
                IncludeLimit = includeLimit,
                SortPhase = sortPhase,
                CountPhaseScoped = options.CountPhaseScoped,
                IncludesOnly = options.IncludesOnly,
                Top = options.Top,
                Page = page,
                OffsetPage = options.OffsetPage,
                SearchParameterHash = options.SearchParameterHash?.Value as string,
            });

        return Lower.Run(context with { TargetResourceType = ResolveTargetType(targetResourceType, options) }, symbols);
    }

    /// <summary>
    /// <c>LowerOptions.SystemLevelSearch</c> was an explicit flag; on the context it is derived as
    /// <c>TargetResourceType is null</c>. A test that set the flag with a non-null target type was asking
    /// for cross-type leaf lowering under a named type, which the context expresses as a null target.
    /// </summary>
    private static string? ResolveTargetType(string? targetResourceType, LowerOptions options)
        => options.SystemLevelSearch ? null : (string.IsNullOrEmpty(targetResourceType) ? null : targetResourceType);
}
```

> **Implementer: verify this before moving on.** `SystemLevelSearch` and `TargetResourceType` were independent in `LowerOptions`; on the context, `SystemLevelSearch` is derived. Run `git grep -n "SystemLevelSearch = true" -- test` and read every hit. If any test sets `SystemLevelSearch = true` **and** a non-null `targetResourceType`, this collapse changes its meaning and the derivation is wrong — stop and add an explicit `SystemLevelSearch` property to `CompilationContext` instead of deriving it. Report what you found either way.
>
> Likewise `SearchParameterHash`: `LowerOptions` typed it `SqlParameterRef?` and `SearchPlanOptions` types it `string?`. The `?.Value as string` unwrap above assumes every test passes a string-valued ref. Run `git grep -n "SearchParameterHash" -- test`, confirm that holds, and if it does not, keep the harness passing the ref straight through by adding an internal `SqlParameterRef? SearchParameterHashRef` to `CompilationContext` that `SearchSqlCompiler` fills from the string.

- [ ] **Step 3: Migrate the call sites**

In `test/`, find/replace `Lower.Run(` → `LowerHarness.Run(` and add `using Ignixa.Search.Sql.Tests.TestSupport;` to each touched file. Do **not** touch any other token on those lines.

Run to find them: `git grep -n "Lower\.Run(" -- test`
Expected before: 91 hits. Expected after: 0.

- [ ] **Step 4: Collapse the signature**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, replace lines 30–57 with:

```csharp
    internal static LoweredPlan Run(CompilationContext context, SymbolTable symbols)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options;
        var expression = context.Expression;
        var targetResourceType = context.TargetResourceType;
        var includes = context.Includes;
        var revIncludes = context.RevIncludes;
        var includeLimit = options.IncludeLimit;
        var sort = context.Sort;
        var sortPhase = options.SortPhase;
        var page = options.Page;

        if (options.OffsetPage is not null && (page is not null || options.Top is not null))
        {
            throw new NotSupportedException(
                "OffsetPage cannot combine with keyset paging or Top: OFFSET/FETCH and TOP are mutually exclusive in T-SQL (error 10741).");
        }

        if (options.CountPhaseScoped && (!options.CountOnly || sort.Count == 0))
        {
            throw new NotSupportedException(
                "CountPhaseScoped requires CountOnly with at least one sort key: there is no sort phase to scope the count to otherwise.");
        }

        var accessConstraintApplier = new AccessConstraintApplier(context.AccessConstraints);
        var lowerContext = new StructuralContext(symbols, context.ApproximationReferenceTime, accessConstraintApplier);
```

Then, through the rest of the method body:
- rename the existing `context` local (the `StructuralContext`) to `lowerContext` — `git grep -n "context\." src/Core/Ignixa.Search.Sql/Lowering/Lower.cs` lists every use;
- replace `options.ResourceTypes` with `context.ResourceTypes`, `options.AccessConstraints` with `context.AccessConstraints`, `options.Visibility` with `context.Visibility`, `options.SurrogateRange` with `context.SurrogateRange`, and `options.SystemLevelSearch` with `context.SystemLevelSearch`;
- `options.ApproximationReferenceTime` becomes `context.ApproximationReferenceTime`;
- `options.SearchParameterHash` becomes `context.Options.SearchParameterHash is { } hash ? new SqlParameterRef(hash) : null` at the single point where the 14-argument `QueryPlan` is constructed (line ~193);
- `options.CountOnly`, `options.Top`, `options.IncludesOnly`, `options.CountPhaseScoped`, and `options.OffsetPage` keep working unchanged — `SearchPlanOptions` has all five under the same names.

Add `using Ignixa.Search.Sql.Compilation;` to the file. Update the `<summary>` to reference `CompilationContext` rather than `LowerOptions`.

- [ ] **Step 5: Fix the production call sites**

In `Tracing/SearchCompiler.cs`, both `Lower.Run(...)` calls become `Lower.Run(context, resolved.Symbols)` — the context built in Task 3 Step 4 already carries every input the `LowerOptions` initialiser used to supply. Delete both `new LowerOptions { ... }` initialisers and the `using Ignixa.Search.Sql.Lowering;` if nothing else in the file needs it.

- [ ] **Step 6: Build and run everything**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: all tests pass, same count as before this task. **Any golden SQL or corpus assertion that fails means this refactor changed behaviour — do not re-baseline; find the mapping that drifted.**

- [ ] **Step 7: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql test/Ignixa.Search.Sql.Tests
git commit -m "refactor(search-sql): collapse Lower.Run onto CompilationContext"
```

---

### Task 5: Lift the diagnostics helpers out of `SearchCompiler`

`SearchCompiler` is deleted in Phase 4, but roughly 250 lines of it are the attribution and provenance logic the new facade still needs. Move that logic first, so the deletion later is a deletion and not a rewrite.

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Compilation/CompilationDiagnosticsBuilder.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs` — delete the moved members, call the new class

- [ ] **Step 1: Create the builder with the members moved verbatim**

Create `src/Core/Ignixa.Search.Sql/Compilation/CompilationDiagnosticsBuilder.cs` containing these members, moved **unchanged** from `Tracing/SearchCompiler.cs`:

| Member | Source lines |
|---|---|
| `MarkUnresolved` | 407–438 |
| `MarkKnownMisses` | 449–476 |
| `PredicateOf` | 479–485 |
| `FindFalse` | 491–497 |
| `BuildPlanTrace` | 500–526 |
| `ContributingOrdinals` | 540–564 |
| `RecordFailure` | 573–595 |
| `ParametersOf` | 603–623 |
| `ExtractSpan` | 626–631 |
| `Flatten` | 634–656 |
| `DetectImplicit` | 381–404 |
| `ResolveFailure` | 358–367 |

Declare the class as:

```csharp
namespace Ignixa.Search.Sql.Compilation;

/// <summary>
/// The attribution and provenance logic behind <see cref="SearchCompilationDiagnostics"/>: which parameter
/// owns which CTE, which parameter a lowering failure belongs to, and which control values took effect
/// without the caller sending them.
/// </summary>
internal static class CompilationDiagnosticsBuilder
```

Two changes are required while moving:
- every member becomes `public` (the class itself is `internal`, so this exposes nothing);
- `RecordFailure`, `ResolveFailure`, and the `ParameterOutcome.Failed` construction inside `MarkUnresolved` take a `CompilationStage` and map it to a `TraceStage` when stamping the outcome. Add this private helper (`CompilationStage` arrives in Task 6 — if you are doing Task 5 first, keep `TraceStage` throughout and add the mapping in Task 6):

```csharp
    private static TraceStage ToTraceStage(CompilationStage stage) => stage switch
    {
        CompilationStage.Build => TraceStage.Parse,
        CompilationStage.Resolve => TraceStage.Resolve,
        CompilationStage.Lower => TraceStage.Lower,
        CompilationStage.Emit => TraceStage.Emit,
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };
```

`RecordFailure` and `ResolveFailure` return `SearchCompilationFailure` instead of `TraceFailure` once Task 6 lands. Until then they keep returning `TraceFailure`, and Task 6 flips them.

- [ ] **Step 2: Delete the moved members from `SearchCompiler` and call through**

Delete every member listed above from `Tracing/SearchCompiler.cs` and replace each call with `CompilationDiagnosticsBuilder.X(...)`. `SearchCompiler` should shrink to its two orchestration bodies plus the three entry-point signatures.

- [ ] **Step 3: Build and run everything**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: all tests pass, unchanged count.

- [ ] **Step 4: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql
git commit -m "refactor(search-sql): extract CompilationDiagnosticsBuilder from SearchCompiler"
```

---

## Phase 2 — the public vocabulary

Phase 2 adds types. Nothing consumes them yet; Phase 3 wires them together.

### Task 6: `CompilationStage`, `SearchCompilationFailure`, `SearchCompilationException`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/CompilationStage.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchCompilationFailure.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchCompilationException.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Compilation/CompilationDiagnosticsBuilder.cs` — return `SearchCompilationFailure`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/SearchCompilationFailureTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Search.Sql.Tests/Compilation/SearchCompilationFailureTests.cs`:

```csharp
using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchCompilationFailureTests
{
    [Fact]
    public void GivenAFailure_WhenWrappingItInAnException_ThenTheExceptionCarriesItAndRepeatsItsMessage()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Lower,
            "Chained search requires a single target resource type.",
            ParameterCode: "subject",
            Span: null,
            Exception: null);

        var exception = new SearchCompilationException(failure);

        exception.Failure.ShouldBeSameAs(failure);
        exception.Message.ShouldBe(failure.Message);
    }

    [Fact]
    public void GivenAFailure_WhenNoDiagnosticsWereCaptured_ThenAttributionIsStillPresent()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Lower, "boom", ParameterCode: "name", Span: null, Exception: null);

        failure.Diagnostics.ShouldBeNull();
        failure.ParameterCode.ShouldBe("name");
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchCompilationFailureTests"`

Expected: build failure — `CompilationStage` does not exist.

- [ ] **Step 3: Write the three types**

Create `src/Core/Ignixa.Search.Sql/CompilationStage.cs`:

```csharp
namespace Ignixa.Search.Sql;

/// <summary>Which stage of the compiler produced a failure.</summary>
public enum CompilationStage
{
    /// <summary>The options builder turning query parameters into a <c>SearchOptions</c>.</summary>
    Build,

    /// <summary>Resolving search parameters, compartments, and access constraints to storage symbols.</summary>
    Resolve,

    /// <summary>Turning the bound expression tree into a <see cref="Ast.QueryPlan"/>.</summary>
    Lower,

    /// <summary>Emitting SQL text and bound parameters from the plan.</summary>
    Emit,
}
```

Create `src/Core/Ignixa.Search.Sql/SearchCompilationFailure.cs`:

```csharp
using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql;

/// <summary>
/// A compilation failure as data. <see cref="ParameterCode"/> and <see cref="Span"/> are populated even at
/// <see cref="SearchDiagnosticsLevel.None"/> — the lowering dispatchers attach them to the exception, so
/// attribution costs nothing.
/// </summary>
public sealed record SearchCompilationFailure(
    CompilationStage Stage,
    string Message,
    string? ParameterCode,
    SourceSpan? Span,
    Exception? Exception)
{
    /// <summary>Whatever diagnostics had been gathered when the failure occurred; null at <see cref="SearchDiagnosticsLevel.None"/>.</summary>
    public SearchCompilationDiagnostics? Diagnostics { get; init; }
}
```

Create `src/Core/Ignixa.Search.Sql/SearchCompilationException.cs`:

```csharp
namespace Ignixa.Search.Sql;

/// <summary>
/// Thrown by the non-<c>Try</c> entry points. The same information the <c>Try</c> entry points return as
/// a <see cref="SearchCompilationFailure"/>.
/// </summary>
public sealed class SearchCompilationException(SearchCompilationFailure failure)
    : Exception(failure?.Message, failure?.Exception)
{
    /// <summary>The failure this exception reports.</summary>
    public SearchCompilationFailure Failure { get; } = failure ?? throw new ArgumentNullException(nameof(failure));
}
```

> `SourceSpan` lives in `Ignixa.Search.Expressions` — confirm the namespace with `git grep -n "record struct SourceSpan\|record SourceSpan"` and adjust the `using` if it differs.

- [ ] **Step 4: Flip `CompilationDiagnosticsBuilder` onto the new types**

In `Compilation/CompilationDiagnosticsBuilder.cs`, change `RecordFailure` and `ResolveFailure` to take and return `SearchCompilationFailure`, adding the `ToTraceStage` helper from Task 5 Step 1:

```csharp
    public static SearchCompilationFailure RecordFailure(IList<ParameterTrace> outcomes, CompilationStage stage, Exception ex)
    {
        var span = ex.Data[LeafLoweringDispatcher.SpanDataKey] as SourceSpan?;
        var parameter = ex.Data[LeafLoweringDispatcher.ParameterDataKey] as SearchParameterInfo;
        var failure = new SearchCompilationFailure(stage, ex.Message, parameter?.Code, span, ex);

        if (parameter is null)
        {
            return failure;
        }

        for (var i = 0; i < outcomes.Count; i++)
        {
            var trace = outcomes[i];
            if (trace.Ir is null || !Flatten(trace.Ir).SelectMany(ParametersOf).Any(p => p.Parameter.Equals(parameter)))
            {
                continue;
            }

            outcomes[i] = trace with { Outcome = new ParameterOutcome.Failed(ToTraceStage(stage), ex.Message, span) };
        }

        return failure;
    }

    public static SearchCompilationFailure? ResolveFailure(IReadOnlyList<SearchParameterInfo> unresolved)
    {
        if (unresolved.Count == 0)
        {
            return null;
        }

        var codes = string.Join(", ", unresolved.Select(p => $"'{p.Code}'"));
        return new SearchCompilationFailure(
            CompilationStage.Resolve,
            $"Search parameters could not be resolved: {codes}.",
            unresolved.Count == 1 ? unresolved[0].Code : null,
            Span: null,
            Exception: null);
    }
```

`MarkUnresolved` keeps constructing `ParameterOutcome.Failed(TraceStage.Resolve, ...)` unchanged.

- [ ] **Step 5: Adapt `SearchCompiler` to the changed return types**

`Tracing.SearchCompiler` still builds a `SearchTrace` whose `Failure` is a `TraceFailure`. Convert at that single point:

```csharp
        Failure = failure is null
            ? null
            : new TraceFailure(ToTraceStage(failure.Stage), failure.Message, failure.Span),
```

with a local copy of `ToTraceStage`. This adapter is deleted along with the class in Task 15.

- [ ] **Step 6: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchCompilationFailureTests"`
Expected: PASS, 2 tests.

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql test/Ignixa.Search.Sql.Tests
git commit -m "feat(search-sql): add the public compilation failure vocabulary"
```

---

### Task 7: `SearchCompilationDiagnostics`, and move the surviving trace types to the root namespace

**Files:**
- Move: `src/Core/Ignixa.Search.Sql/Tracing/QueryPlanTrace.cs` → `src/Core/Ignixa.Search.Sql/QueryPlanTrace.cs`
- Move: `src/Core/Ignixa.Search.Sql/Tracing/CteProvenance.cs` → `src/Core/Ignixa.Search.Sql/CteProvenance.cs`
- Move: `src/Core/Ignixa.Search.Sql/Tracing/ImplicitParameter.cs` → `src/Core/Ignixa.Search.Sql/ImplicitParameter.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchCompilationDiagnostics.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/SearchCompilationDiagnosticsTests.cs`

- [ ] **Step 1: Move the three types**

```bash
git mv src/Core/Ignixa.Search.Sql/Tracing/QueryPlanTrace.cs src/Core/Ignixa.Search.Sql/QueryPlanTrace.cs
git mv src/Core/Ignixa.Search.Sql/Tracing/CteProvenance.cs src/Core/Ignixa.Search.Sql/CteProvenance.cs
git mv src/Core/Ignixa.Search.Sql/Tracing/ImplicitParameter.cs src/Core/Ignixa.Search.Sql/ImplicitParameter.cs
```

Change each file's namespace from `Ignixa.Search.Sql.Tracing` to `Ignixa.Search.Sql`. Every consumer that had `using Ignixa.Search.Sql.Tracing;` and now fails to compile needs `using Ignixa.Search.Sql;` instead — the compiler lists them.

- [ ] **Step 2: Write the failing test**

Create `test/Ignixa.Search.Sql.Tests/Compilation/SearchCompilationDiagnosticsTests.cs`:

```csharp
using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchCompilationDiagnosticsTests
{
    [Fact]
    public void GivenDefaultDiagnostics_WhenReadingThem_ThenTheCollectionsAreEmptyRatherThanNull()
    {
        var diagnostics = new SearchCompilationDiagnostics();

        diagnostics.Parameters.ShouldBeEmpty();
        diagnostics.Implicit.ShouldBeEmpty();
        diagnostics.SqlTextRanges.ShouldBeEmpty();
        diagnostics.Plan.ShouldBeNull();
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchCompilationDiagnosticsTests"`

Expected: build failure — `SearchCompilationDiagnostics` does not exist.

- [ ] **Step 4: Write `SearchCompilationDiagnostics`**

Create `src/Core/Ignixa.Search.Sql/SearchCompilationDiagnostics.cs`:

```csharp
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// What a compile recorded about its own work. Present only when
/// <see cref="SearchPlanOptions.DiagnosticsLevel"/> is above <see cref="SearchDiagnosticsLevel.None"/>.
/// </summary>
/// <remarks>
/// <c>CreatePlanFromOptionsAsync</c> never runs the options builder, so on that path
/// <see cref="Parameters"/> and <see cref="Implicit"/> are always empty regardless of level: there is no
/// query string to attribute outcomes to, and no way to tell an explicit <c>_count</c> from a server
/// default. <see cref="Plan"/> and <see cref="SqlTextRanges"/> are populated normally.
/// </remarks>
public sealed record SearchCompilationDiagnostics
{
    /// <summary>Per-parameter outcomes from the options builder, stamped by Resolve and Lower.</summary>
    public IReadOnlyList<ParameterTrace> Parameters { get; init; } = [];

    /// <summary>Control values that took effect without the caller sending them.</summary>
    public IReadOnlyList<ImplicitParameter> Implicit { get; init; } = [];

    /// <summary>The plan's explain rows and per-CTE provenance. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public QueryPlanTrace? Plan { get; init; }

    /// <summary>Which span of the emitted SQL each plan element produced. Populated at <see cref="SearchDiagnosticsLevel.Full"/>.</summary>
    public IReadOnlyList<SqlTextRange> SqlTextRanges { get; init; } = [];
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchCompilationDiagnosticsTests"`
Expected: PASS, 1 test.

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql test/Ignixa.Search.Sql.Tests
git commit -m "feat(search-sql): add SearchCompilationDiagnostics and move trace types to the root namespace"
```

---

### Task 8: `CompiledSearch`, `SearchPlanResult`, `SearchCompilationResult`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/CompiledSearch.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchPlanResult.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchCompilationResult.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/ResultTypeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Search.Sql.Tests/Compilation/ResultTypeTests.cs`:

```csharp
using Ignixa.Search.Sql;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class ResultTypeTests
{
    [Fact]
    public void GivenASearchCompilationResultCarryingAFailure_WhenCheckingSucceeded_ThenItIsFalse()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Emit, "boom", ParameterCode: null, Span: null, Exception: null);

        var result = new SearchCompilationResult(Compiled: null, failure);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBeSameAs(failure);
    }

    [Fact]
    public void GivenASearchPlanResultCarryingAFailure_WhenCheckingSucceeded_ThenItIsFalse()
    {
        var failure = new SearchCompilationFailure(
            CompilationStage.Resolve, "boom", ParameterCode: null, Span: null, Exception: null);

        var result = new SearchPlanResult(Plan: null, failure);

        result.Succeeded.ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~ResultTypeTests"`

Expected: build failure — `SearchCompilationResult` does not exist.

- [ ] **Step 3: Write the three types**

Create `src/Core/Ignixa.Search.Sql/CompiledSearch.cs`:

```csharp
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// A compiled search: the SQL text, its bound parameters, and the plan it came from. Read
/// <see cref="Plan"/> to pick a result-row reader — <c>Plan.Includes</c> and <c>Plan.Projection</c>
/// determine the column shape.
/// </summary>
public sealed record CompiledSearch(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    QueryPlan Plan)
{
    /// <summary>Plan-phase and emit-phase diagnostics merged; null at <see cref="SearchDiagnosticsLevel.None"/>.</summary>
    public SearchCompilationDiagnostics? Diagnostics { get; init; }
}
```

Create `src/Core/Ignixa.Search.Sql/SearchPlanResult.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>The outcome of a <c>TryCreatePlan</c> call: exactly one of <see cref="Plan"/> or <see cref="Failure"/> is non-null.</summary>
public sealed record SearchPlanResult(SearchPlan? Plan, SearchCompilationFailure? Failure)
{
    /// <summary>True when a plan was produced.</summary>
    [MemberNotNullWhen(true, nameof(Plan))]
    public bool Succeeded => Plan is not null;
}
```

Create `src/Core/Ignixa.Search.Sql/SearchCompilationResult.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Sql;

/// <summary>The outcome of a <c>TryCompile</c> call: exactly one of <see cref="Compiled"/> or <see cref="Failure"/> is non-null.</summary>
public sealed record SearchCompilationResult(CompiledSearch? Compiled, SearchCompilationFailure? Failure)
{
    /// <summary>True when SQL was emitted.</summary>
    [MemberNotNullWhen(true, nameof(Compiled))]
    public bool Succeeded => Compiled is not null;
}
```

> `SearchPlanResult` references `SearchPlan`, which arrives in Task 9. Do Task 9 first if you prefer a compiling checkpoint per task; otherwise add the empty `SearchPlan` shell from Task 9 Step 3 now and fill it in there.

- [ ] **Step 4: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~ResultTypeTests"`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql test/Ignixa.Search.Sql.Tests
git commit -m "feat(search-sql): add CompiledSearch and the Try result types"
```

---

## Phase 3 — the facade

### Task 9: `SearchPlan` with `Compile()` and `TryCompile()`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/SearchPlan.cs`
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/FakeSymbolResolver.cs` (promoted, see Step 0)
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/FakeSearchOptionsBuilder.cs` (promoted, see Step 0)
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/PlanFixtures.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/SearchPlanTests.cs`

- [ ] **Step 0: Promote the two fakes into `TestSupport`**

`FakeSymbolResolver` exists as a private nested class in four separate files (`EndToEndCompilationTests.cs:17`, `Tracing/CompileFromOptionsTests.cs:353`, `Symbols/ResolveTests.cs:597`, `Tracing/SearchTraceFixtures.cs:696`) and `FakeSearchOptionsBuilder` in one (`Tracing/SearchTraceFixtures.cs:613`). Move the `SearchTraceFixtures` copies into `test/Ignixa.Search.Sql.Tests/TestSupport/` as `internal sealed class`es in the `Ignixa.Search.Sql.Tests.TestSupport` namespace, keeping their bodies byte-for-byte.

Leave the other three nested copies alone — deduplicating them is unrelated cleanup and would bloat this diff.

`FakeSymbolResolver` exposes `SearchParamIds` and `ResourceTypeIds` dictionaries; `FakeSearchOptionsBuilder`'s constructor is `(SearchOptions options, IReadOnlyList<ParameterTrace> outcomes)`. The fixtures below depend on both shapes.

- [ ] **Step 1: Write `PlanFixtures`**

Create `test/Ignixa.Search.Sql.Tests/TestSupport/PlanFixtures.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>Query plans for tests that need a real plan but do not care how it was produced.</summary>
internal static class PlanFixtures
{
    public static readonly SearchParameterInfo NameParameter = new(
        "name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

    /// <summary>A plain <c>Patient?name:exact=Smith</c>, compiled through the facade.</summary>
    public static async Task<QueryPlan> SimplePatientSearchAsync()
        => (await CompilerFixtures.ForPatient()
            .CreatePlanAsync("Patient", [new QueryParameter("name:exact", "Smith")])).Query;

    /// <summary>
    /// A plan that cannot be emitted: <c>IncludesOnly</c> with no include stages, which
    /// <c>SqlBuilder.RejectUnsupportedCombinations</c> refuses because it can only ever return nothing.
    /// </summary>
    public static async Task<QueryPlan> IncoherentPlanAsync()
        => await SimplePatientSearchAsync() with { IncludesOnly = true };

    /// <summary>An expression no query string can produce, standing in for a FHIR operation root.</summary>
    public static Expression EverythingExpression()
        => new SearchParameterExpression(
            NameParameter,
            new SearchParameterPredicateExpression(
                NameParameter, SearchComparator.Eq, modifier: null, new StringSearchValue("operation-root")));
}
```

> If `QueryPlan.IncludesOnly` is not a settable positional member, pick whichever `RejectUnsupportedCombinations` guard is cheapest to trip with a `with` expression — `OffsetPage` set alongside `Top` also works and both are positional members of the 14-argument record.

- [ ] **Step 2: Write the failing tests**

Create `test/Ignixa.Search.Sql.Tests/Compilation/SearchPlanTests.cs`:

```csharp
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchPlanTests
{
    [Fact]
    public async Task GivenAPlan_WhenCompilingIt_ThenTheSqlAndTheOriginatingPlanAreBothReturned()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.SimplePatientSearchAsync() };

        var compiled = plan.Compile();

        compiled.Sql.ShouldNotBeNullOrWhiteSpace();
        compiled.Plan.ShouldBeSameAs(plan.Query);
    }

    [Fact]
    public async Task GivenAPlan_WhenRewritingTheQueryWithAWithExpression_ThenTheOriginalPlanIsUnchanged()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.SimplePatientSearchAsync() };

        var rewritten = plan with { Query = plan.Query with { Top = 5 } };

        rewritten.Query.Top.ShouldBe(5);
        plan.Query.Top.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAPlanThatCannotEmit_WhenCompilingIt_ThenItThrowsASearchCompilationExceptionAtTheEmitStage()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.IncoherentPlanAsync() };

        var exception = Should.Throw<SearchCompilationException>(() => plan.Compile());

        exception.Failure.Stage.ShouldBe(CompilationStage.Emit);
    }

    [Fact]
    public async Task GivenAPlanThatCannotEmit_WhenTryCompilingIt_ThenItReturnsAFailureRatherThanThrowing()
    {
        var plan = new SearchPlan { Query = await PlanFixtures.IncoherentPlanAsync() };

        var result = plan.TryCompile();

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Emit);
    }

    [Fact]
    public async Task GivenAPlanAtDiagnosticsLevelNone_WhenCompilingIt_ThenNoDiagnosticsAreAttached()
    {
        var plan = new SearchPlan
        {
            Query = await PlanFixtures.SimplePatientSearchAsync(),
            DiagnosticsLevel = SearchDiagnosticsLevel.None,
        };

        plan.Compile().Diagnostics.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAPlanAtDiagnosticsLevelFull_WhenCompilingIt_ThenSqlTextRangesAreAttached()
    {
        var plan = new SearchPlan
        {
            Query = await PlanFixtures.SimplePatientSearchAsync(),
            DiagnosticsLevel = SearchDiagnosticsLevel.Full,
        };

        plan.Compile().Diagnostics!.SqlTextRanges.ShouldNotBeEmpty();
    }
}
```

`PlanFixtures` depends on `CompilerFixtures`, which Task 10 Step 1 defines. Write `CompilerFixtures` now (its code is in Task 10) rather than stubbing it.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchPlanTests"`

Expected: build failure — `SearchPlan` does not exist.

- [ ] **Step 4: Write `SearchPlan`**

Create `src/Core/Ignixa.Search.Sql/SearchPlan.cs`:

```csharp
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql;

/// <summary>
/// A lowered search, ready to emit. Inspect it with <c>Query.Explain()</c>, rewrite it with
/// <c>plan with { Query = rewritten }</c>, then call <see cref="Compile"/>.
/// </summary>
/// <remarks>
/// Constructing a plan never throws; validation happens in <see cref="Compile"/> so a rewritten plan is
/// checked on the same terms as the original.
/// </remarks>
public sealed record SearchPlan
{
    /// <summary>The lowered plan.</summary>
    public required QueryPlan Query { get; init; }

    /// <summary>Build, Resolve, and Lower diagnostics. Null when <see cref="DiagnosticsLevel"/> is <see cref="SearchDiagnosticsLevel.None"/>.</summary>
    public SearchCompilationDiagnostics? Diagnostics { get; init; }

    /// <summary>Carried from <see cref="SearchPlanOptions.DiagnosticsLevel"/> so <see cref="Compile"/> emits at the same detail.</summary>
    public SearchDiagnosticsLevel DiagnosticsLevel { get; init; }

    /// <summary>Emits SQL, throwing <see cref="SearchCompilationException"/> when the plan cannot be emitted.</summary>
    public CompiledSearch Compile()
    {
        var result = TryCompile();
        return result.Succeeded ? result.Compiled : throw new SearchCompilationException(result.Failure!);
    }

    /// <summary>Emits SQL, returning the failure as data when the plan cannot be emitted.</summary>
    public SearchCompilationResult TryCompile()
    {
        var includeTextRanges = DiagnosticsLevel == SearchDiagnosticsLevel.Full;

        EmittedSql emitted;
        try
        {
            emitted = SqlBuilder.Run(Query, new EmitOptions(includeTextRanges));
        }
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            var failure = new SearchCompilationFailure(
                CompilationStage.Emit, ex.Message, ParameterCode: null, Span: null, ex)
            {
                Diagnostics = Diagnostics,
            };

            return new SearchCompilationResult(Compiled: null, failure);
        }

        var compiled = new CompiledSearch(emitted.Sql, emitted.Parameters, Query)
        {
            Diagnostics = DiagnosticsLevel == SearchDiagnosticsLevel.None
                ? null
                : (Diagnostics ?? new SearchCompilationDiagnostics()) with
                {
                    SqlTextRanges = emitted.TextRanges ?? [],
                },
        };

        return new SearchCompilationResult(compiled, Failure: null);
    }
}
```

> `ArgumentException` from the trace records' construction guards is deliberately **not** caught: those detect defects in this compiler, and filing one as a property of the user's query would send the next reader to the wrong file.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchPlanTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql/SearchPlan.cs test/Ignixa.Search.Sql.Tests
git commit -m "feat(search-sql): add SearchPlan with Compile and TryCompile"
```

---

### Task 10: `ISearchSqlCompiler` and `SearchSqlCompiler`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/ISearchSqlCompiler.cs`
- Create: `src/Core/Ignixa.Search.Sql/SearchSqlCompiler.cs`
- Create: `test/Ignixa.Search.Sql.Tests/TestSupport/CompilerFixtures.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/SearchSqlCompilerTests.cs`

- [ ] **Step 1: Write `CompilerFixtures`**

Create `test/Ignixa.Search.Sql.Tests/TestSupport/CompilerFixtures.cs`. It uses the two fakes promoted in Task 9 Step 0.

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Ignixa.Search.Sql.Tests.TestSupport;

/// <summary>Pre-wired <see cref="SearchSqlCompiler"/> instances for the facade tests.</summary>
internal static class CompilerFixtures
{
    /// <summary>A resolver that knows <c>Patient</c> and the <c>name</c> search parameter.</summary>
    public static FakeSymbolResolver PatientResolver()
    {
        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[PlanFixtures.NameParameter.Url!.ToString()] = 202;
        resolver.ResourceTypeIds["Patient"] = 103;
        return resolver;
    }

    /// <summary>A compiler that compiles <c>Patient?name:exact=Smith</c> successfully.</summary>
    public static SearchSqlCompiler ForPatient()
        => new(PatientResolver(), PatientOptionsBuilder());

    /// <summary>A compiler whose resolver finds nothing, so every parameter comes back unresolved.</summary>
    public static SearchSqlCompiler WithUnresolvableParameters()
        => new(new FakeSymbolResolver(), PatientOptionsBuilder());

    /// <summary>A compiler whose options builder throws a <see cref="FhirException"/> from the build stage.</summary>
    public static SearchSqlCompiler WithThrowingOptionsBuilder()
    {
        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder
            .Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<object?>(), Arg.Any<IList<ParameterTrace>>())
            .Throws(new FhirException("Unparseable search value."));

        return new SearchSqlCompiler(PatientResolver(), builder);
    }

    private static FakeSearchOptionsBuilder PatientOptionsBuilder()
    {
        var predicate = new SearchParameterPredicateExpression(
            PlanFixtures.NameParameter, SearchComparator.Eq, new SearchModifier(SearchModifierCode.Exact), new StringSearchValue("Smith"));
        var expression = new SearchParameterExpression(PlanFixtures.NameParameter, predicate);

        return new FakeSearchOptionsBuilder(
            new SearchOptions { ResourceType = "Patient", Expression = expression },
            [new ParameterTrace(0, "name:exact", null, "Smith", null, expression, new ParameterOutcome.Compiled(), null)]);
    }
}
```

> Match `ISearchOptionsBuilder.Build`'s real parameter list when writing the NSubstitute setup — read the interface rather than trusting the `Arg.Any<object?>()` placeholder for `schemaProvider`. Confirm `FhirException`'s namespace and that it has a single-string constructor.

- [ ] **Step 2: Write the failing tests**

Create `test/Ignixa.Search.Sql.Tests/Compilation/SearchSqlCompilerTests.cs`:

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql;
using Ignixa.Search.Sql.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Compilation;

public class SearchSqlCompilerTests
{
    [Fact]
    public async Task GivenAQueryString_WhenCreatingAPlanAndCompilingIt_ThenSqlIsEmitted()
    {
        var compiler = CompilerFixtures.ForPatient();

        var plan = await compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);
        var compiled = plan.Compile();

        compiled.Sql.ShouldNotBeNullOrWhiteSpace();
        compiled.Plan.ShouldBeSameAs(plan.Query);
    }

    [Fact]
    public async Task GivenAnUnresolvableSearchParameter_WhenCreatingAPlan_ThenItThrowsAtTheResolveStage()
    {
        var compiler = CompilerFixtures.WithUnresolvableParameters();

        var exception = await Should.ThrowAsync<SearchCompilationException>(
            () => compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]));

        exception.Failure.Stage.ShouldBe(CompilationStage.Resolve);
    }

    [Fact]
    public async Task GivenAnUnresolvableSearchParameter_WhenTryingToCreateAPlan_ThenItReturnsAFailure()
    {
        var compiler = CompilerFixtures.WithUnresolvableParameters();

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Resolve);
    }

    [Fact]
    public async Task GivenSearchOptionsCarryingAnOperationExpression_WhenCreatingAPlan_ThenTheCallersOptionsAreNotMutated()
    {
        var compiler = CompilerFixtures.ForPatient();
        var searchOptions = new SearchOptions();
        var operationExpression = PlanFixtures.EverythingExpression();

        await compiler.CreatePlanFromOptionsAsync(
            searchOptions, "Patient", new SearchPlanOptions { OperationExpression = operationExpression });

        searchOptions.Expression.ShouldBeNull();
    }

    [Fact]
    public async Task GivenDiagnosticsLevelNone_WhenCreatingAPlan_ThenNoDiagnosticsAreAttached()
    {
        var compiler = CompilerFixtures.ForPatient();

        var plan = await compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        plan.Diagnostics.ShouldBeNull();
    }

    [Fact]
    public async Task GivenDiagnosticsLevelParameters_WhenCreatingAPlan_ThenPerParameterOutcomesAreAttached()
    {
        var compiler = CompilerFixtures.ForPatient();
        var options = new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Parameters };

        var plan = await compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")], options);

        plan.Diagnostics!.Parameters.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenNoOptionsBuilder_WhenCreatingAPlanFromAQueryString_ThenItThrowsNamingTheMissingDependency()
    {
        var compiler = new SearchSqlCompiler(CompilerFixtures.PatientResolver());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]));

        exception.Message.ShouldContain(nameof(ISearchOptionsBuilder));
    }

    [Fact]
    public async Task GivenAFhirExceptionFromTheOptionsBuilder_WhenCreatingAPlan_ThenItPropagatesUnwrapped()
    {
        var compiler = CompilerFixtures.WithThrowingOptionsBuilder();

        await Should.ThrowAsync<FhirException>(
            () => compiler.CreatePlanAsync("Patient", [new QueryParameter("name", "smith")]));
    }

    [Fact]
    public async Task GivenAFhirExceptionFromTheOptionsBuilder_WhenTryingToCreateAPlan_ThenItIsCapturedAtTheBuildStage()
    {
        var compiler = CompilerFixtures.WithThrowingOptionsBuilder();

        var result = await compiler.TryCreatePlanAsync("Patient", [new QueryParameter("name", "smith")]);

        result.Succeeded.ShouldBeFalse();
        result.Failure!.Stage.ShouldBe(CompilationStage.Build);
        result.Failure.Exception.ShouldBeOfType<FhirException>();
    }
}
```

> **Implementer:** `SearchSqlCompiler`'s constructor signature is fixed in Step 5 below — `CompilerFixtures` above assumes `(ISymbolResolver, ISearchOptionsBuilder)` positionally. If Step 5's primary constructor takes optional trailing parameters, the fixtures still compile unchanged.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchSqlCompilerTests"`

Expected: build failure — `SearchSqlCompiler` does not exist.

- [ ] **Step 4: Write the interface**

Create `src/Core/Ignixa.Search.Sql/ISearchSqlCompiler.cs`:

```csharp
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql;

/// <summary>
/// Compiles a FHIR search into a <see cref="SearchPlan"/>. Call <see cref="SearchPlan.Compile"/> on the
/// result to emit SQL. The split is deliberate: creating a plan reads storage symbols and is therefore
/// asynchronous, while emitting from a plan is pure — and the seam between them is where a caller can
/// inspect or rewrite the plan.
/// </summary>
public interface ISearchSqlCompiler
{
    /// <summary>Builds, resolves, and lowers a query string. Throws <see cref="SearchCompilationException"/> on failure.</summary>
    Task<SearchPlan> CreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves and lowers an already-built <see cref="SearchOptions"/>, skipping the build stage. Throws
    /// <see cref="SearchCompilationException"/> on failure.
    /// </summary>
    Task<SearchPlan> CreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>As <see cref="CreatePlanAsync"/>, returning the failure as data instead of throwing.</summary>
    Task<SearchPlanResult> TryCreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>As <see cref="CreatePlanFromOptionsAsync"/>, returning the failure as data instead of throwing.</summary>
    Task<SearchPlanResult> TryCreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Write the implementation**

Create `src/Core/Ignixa.Search.Sql/SearchSqlCompiler.cs`:

```csharp
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Compilation;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql;

/// <summary>
/// The compiler's only orchestrator. <paramref name="optionsBuilder"/> is required only by the
/// query-string entry points; the definition managers are required only by compartment searches,
/// <c>$everything</c>, and <c>_not-referenced</c> path filters. Each throws
/// <see cref="InvalidOperationException"/> naming itself when a query needs it and it was not supplied.
/// </summary>
public sealed class SearchSqlCompiler(
    ISymbolResolver resolver,
    ISearchOptionsBuilder? optionsBuilder = null,
    ICompartmentDefinitionManager? compartmentDefinitionManager = null,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager = null,
    TimeProvider? timeProvider = null) : ISearchSqlCompiler
{
    private readonly SymbolResolution _deps = new(
        resolver ?? throw new ArgumentNullException(nameof(resolver)),
        compartmentDefinitionManager,
        searchParameterDefinitionManager);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<SearchPlan> CreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await TryCreatePlanCoreAsync(resourceType, parameters, options, rethrowBuildFailures: true, cancellationToken);
        return result.Succeeded ? result.Plan : throw new SearchCompilationException(result.Failure!);
    }

    public async Task<SearchPlanResult> TryCreatePlanAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
        => await TryCreatePlanCoreAsync(resourceType, parameters, options, rethrowBuildFailures: false, cancellationToken);

    public async Task<SearchPlan> CreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = await TryCreatePlanFromOptionsAsync(searchOptions, resourceType, options, cancellationToken);
        return result.Succeeded ? result.Plan : throw new SearchCompilationException(result.Failure!);
    }

    public async Task<SearchPlanResult> TryCreatePlanFromOptionsAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchOptions);

        options ??= new SearchPlanOptions();

        return await RunAsync(searchOptions, resourceType, options, outcomes: [], implicitParameters: [], cancellationToken);
    }

    private async Task<SearchPlanResult> TryCreatePlanCoreAsync(
        string? resourceType,
        IReadOnlyList<QueryParameter> parameters,
        SearchPlanOptions? options,
        bool rethrowBuildFailures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        options ??= new SearchPlanOptions();

        if (optionsBuilder is null)
        {
            throw new InvalidOperationException(
                $"Compiling a query string requires an {nameof(ISearchOptionsBuilder)}; none was supplied to {nameof(SearchSqlCompiler)}.");
        }

        var outcomes = new List<ParameterTrace>();

        SearchOptions searchOptions;
        try
        {
            searchOptions = optionsBuilder.Build(resourceType, parameters, schemaProvider: null, outcomes);
        }
        catch (FhirException ex) when (!rethrowBuildFailures)
        {
            return new SearchPlanResult(
                Plan: null,
                new SearchCompilationFailure(CompilationStage.Build, ex.Message, ParameterCode: null, Span: null, ex));
        }

        var implicitParameters = options.DiagnosticsLevel == SearchDiagnosticsLevel.None
            ? []
            : CompilationDiagnosticsBuilder.DetectImplicit(parameters, searchOptions);

        return await RunAsync(searchOptions, resourceType, options, outcomes, implicitParameters, cancellationToken);
    }

    private async Task<SearchPlanResult> RunAsync(
        SearchOptions searchOptions,
        string? resourceType,
        SearchPlanOptions options,
        List<ParameterTrace> outcomes,
        IReadOnlyList<ImplicitParameter> implicitParameters,
        CancellationToken cancellationToken)
    {
        var context = CompilationContext.Create(searchOptions, resourceType, options, _timeProvider.GetUtcNow());
        var traced = options.DiagnosticsLevel != SearchDiagnosticsLevel.None;

        var resolved = await Resolve.RunAsync(context, _deps, cancellationToken);

        if (resolved.Unresolved.Count > 0)
        {
            if (traced)
            {
                CompilationDiagnosticsBuilder.MarkUnresolved(outcomes, resolved.Unresolved);
            }

            var resolveFailure = CompilationDiagnosticsBuilder.ResolveFailure(resolved.Unresolved)!;
            return new SearchPlanResult(
                Plan: null,
                resolveFailure with { Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace: null) });
        }

        LoweredPlan lowered;
        try
        {
            lowered = Lower.Run(context, resolved.Symbols);
        }
        catch (Exception ex) when (ex is NotSupportedException or KeyNotFoundException)
        {
            var failure = CompilationDiagnosticsBuilder.RecordFailure(outcomes, CompilationStage.Lower, ex);
            return new SearchPlanResult(
                Plan: null,
                failure with { Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace: null) });
        }

        QueryPlanTrace? planTrace = null;
        if (options.DiagnosticsLevel == SearchDiagnosticsLevel.Full)
        {
            planTrace = CompilationDiagnosticsBuilder.BuildPlanTrace(lowered, outcomes);
        }

        if (traced)
        {
            CompilationDiagnosticsBuilder.MarkKnownMisses(outcomes, lowered);
        }

        var plan = new SearchPlan
        {
            Query = lowered.Plan,
            DiagnosticsLevel = options.DiagnosticsLevel,
            Diagnostics = Diagnostics(traced, outcomes, implicitParameters, planTrace),
        };

        return new SearchPlanResult(plan, Failure: null);
    }

    private static SearchCompilationDiagnostics? Diagnostics(
        bool traced,
        IReadOnlyList<ParameterTrace> outcomes,
        IReadOnlyList<ImplicitParameter> implicitParameters,
        QueryPlanTrace? planTrace)
        => traced
            ? new SearchCompilationDiagnostics
            {
                Parameters = outcomes,
                Implicit = implicitParameters,
                Plan = planTrace,
            }
            : null;
}
```

> Two things the implementer must confirm against the real signatures: `ISearchOptionsBuilder.Build`'s parameter list (`SearchCompiler.cs:67` shows `Build(resourceType, parameters, schemaProvider: null, outcomes)` — note `resourceType` there is non-null, so check whether it accepts null and adjust), and `FhirException`'s namespace for the `using`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~SearchSqlCompilerTests"`
Expected: PASS, 9 tests.

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql test/Ignixa.Search.Sql.Tests
git commit -m "feat(search-sql): add ISearchSqlCompiler and SearchSqlCompiler"
```

---

## Phase 4 — migrate consumers, delete the old orchestrator

### Task 11: Migrate `CorpusCompiler` onto the facade

**Files:**
- Modify: `test/Ignixa.Search.Sql.Tests/Corpus/CorpusCompiler.cs`

- [ ] **Step 1: Rewrite the stage wiring as a facade call**

`CorpusCompiler` currently orchestrates the three stages by hand. Replace that with:

```csharp
        var compiler = new SearchSqlCompiler(
            resolver,
            optionsBuilder,
            compartmentDefinitionManager,
            searchParameterDefinitionManager,
            timeProvider);

        var result = await compiler.TryCreatePlanAsync(
            resourceType,
            parameters,
            new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Full },
            cancellationToken);

        if (!result.Succeeded)
        {
            return /* the corpus file's existing failure shape, fed from result.Failure */;
        }

        var compiled = result.Plan.Compile();
```

Keep every field the corpus records exactly as it is — the corpus baselines are the regression net and must not move. Map `result.Failure.Stage` and `.Message` onto whatever the corpus file writes for a failure today.

- [ ] **Step 2: Run the corpus tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~Corpus"`
Expected: PASS with **zero** baseline files changed. Confirm with `git status` — if any corpus baseline is modified, the migration changed behaviour. Do not re-baseline; find the difference.

- [ ] **Step 3: Commit** (ask the user first)

```bash
git add test/Ignixa.Search.Sql.Tests/Corpus
git commit -m "test(search-sql): migrate CorpusCompiler onto the facade"
```

---

### Task 12: Migrate the tracing tests onto the facade

**Files:**
- Modify: every file under `test/Ignixa.Search.Sql.Tests/Tracing/`

- [ ] **Step 1: List what has to move**

Run: `git grep -ln "SearchCompiler\.\|SearchTrace" -- test/Ignixa.Search.Sql.Tests`

- [ ] **Step 2: Rewrite each file against the facade**

Mechanical substitutions:

| Was | Becomes |
|---|---|
| `await SearchCompiler.CompileAsync(rt, ps, ob, r)` | `await new SearchSqlCompiler(r, ob).TryCreatePlanAsync(rt, ps, FullDiagnostics)` |
| `await SearchCompiler.CompileWithTimeProviderAsync(..., tp, ...)` | same, with `timeProvider: tp` on the constructor |
| `await SearchCompiler.CompileFromOptionsAsync(so, rt, r, ...)` | `await new SearchSqlCompiler(r, ...).TryCreatePlanFromOptionsAsync(so, rt, FullDiagnostics)` |
| `trace.CompiledPlan` | `result.Plan!.Query` |
| `trace.Parameters` | `result.Plan!.Diagnostics!.Parameters` |
| `trace.Implicit` | `result.Plan!.Diagnostics!.Implicit` |
| `trace.Plan` | `result.Plan!.Diagnostics!.Plan` |
| `trace.Sql!.Sql` / `.Parameters` | `result.Plan!.Compile().Sql` / `.Parameters` |
| `trace.Sql!.Ranges` | `result.Plan!.Compile().Diagnostics!.SqlTextRanges` |
| `trace.Failure!.Stage == TraceStage.Lower` | `result.Failure!.Stage == CompilationStage.Lower` |
| `trace.ResourceType` | dropped — the test passed it in |

where `FullDiagnostics` is `new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Full }`.

Rename `SearchTraceFixtures` to `CompilationFixtures` and have it return `SearchPlanResult` rather than `SearchTrace`.

Two tests need a genuine rewrite rather than a substitution, because the contract changed:
- any test asserting that an unresolved parameter yields a trace with a **null plan** now asserts `result.Succeeded.ShouldBeFalse()` and `result.Failure!.Stage.ShouldBe(CompilationStage.Resolve)`;
- any test asserting `SearchTrace.ResourceType` is deleted.

Move the migrated files from `test/Ignixa.Search.Sql.Tests/Tracing/` to `test/Ignixa.Search.Sql.Tests/Compilation/` and delete the empty folder.

- [ ] **Step 3: Run them**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: all pass.

- [ ] **Step 4: Commit** (ask the user first)

```bash
git add test/Ignixa.Search.Sql.Tests
git commit -m "test(search-sql): migrate the tracing tests onto the facade"
```

---

### Task 13: Migrate `Ignixa.Application.Tests` — public API only

This is the proof that the facade is sufficient. `Ignixa.Application.Tests` has no `InternalsVisibleTo` into the SQL package, so if these tests compile against the facade alone, the facade is complete.

**Files:**
- Modify: `test/Ignixa.Application.Tests/Search/Parsing/SearchTrace*Tests.cs`

- [ ] **Step 1: Find them**

Run: `git grep -ln "SearchCompiler\|SearchTrace" -- test/Ignixa.Application.Tests`

- [ ] **Step 2: Rewrite using the same substitution table as Task 12**

If any of these tests needs a type that is now `internal`, that is a **finding, not an obstacle**: it means the facade is missing something. Stop and report which type, rather than adding an `InternalsVisibleTo`.

- [ ] **Step 3: Run them**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Search"`
Expected: all pass.

- [ ] **Step 4: Commit** (ask the user first)

```bash
git add test/Ignixa.Application.Tests
git commit -m "test(application): migrate search compilation tests onto the public facade"
```

---

### Task 14: Migrate the end-to-end integration test

`CompiledSearchEndToEndTests` hand-chains all three stages and is the closest thing this repo has to a production consumer. Migrating it is the strongest signal the facade works.

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs:74-76`

- [ ] **Step 1: Replace the hand-chained stages**

Replace the three-stage block at lines 74–76 with:

```csharp
        var compiler = new SearchSqlCompiler(resolver, optionsBuilder);
        var plan = await compiler.CreatePlanFromOptionsAsync(searchOptions, resourceType, cancellationToken: cancellationToken);
        var compiled = plan.Compile();
```

and use `compiled.Sql`, `compiled.Parameters`, and `compiled.Plan` where the test previously used `emitted.Sql`, `emitted.Parameters`, and `lowered.Plan`.

- [ ] **Step 2: Run it**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj --filter "FullyQualifiedName~CompiledSearchEndToEnd"`
Expected: PASS. (These need SQL Server; if the suite is skipped in your environment, say so explicitly rather than reporting a pass.)

- [ ] **Step 3: Commit** (ask the user first)

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests
git commit -m "test(sql-ef): migrate the end-to-end compiled search test onto the facade"
```

---

### Task 15: Delete `Tracing`

**Files:**
- Delete: `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs`
- Delete: `src/Core/Ignixa.Search.Sql/Tracing/SearchTrace.cs`
- Delete: `src/Core/Ignixa.Search.Sql/Tracing/TraceFailure.cs`
- Delete: `src/Core/Ignixa.Search.Sql/Tracing/EmittedSqlTrace.cs`

- [ ] **Step 1: Confirm nothing references them**

Run: `git grep -n "SearchCompiler\|SearchTrace\|TraceFailure\|EmittedSqlTrace\|Ignixa\.Search\.Sql\.Tracing"`
Expected: zero hits outside the four files being deleted. Any other hit must be migrated first.

- [ ] **Step 2: Delete**

```bash
git rm src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs src/Core/Ignixa.Search.Sql/Tracing/SearchTrace.cs src/Core/Ignixa.Search.Sql/Tracing/TraceFailure.cs src/Core/Ignixa.Search.Sql/Tracing/EmittedSqlTrace.cs
```

The `Tracing` folder should now be empty.

- [ ] **Step 3: Build and run everything**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet test All.sln`
Expected: all pass.

- [ ] **Step 4: Commit** (ask the user first)

```bash
git add -A src/Core/Ignixa.Search.Sql
git commit -m "refactor(search-sql): delete the Tracing orchestrator, superseded by the facade"
```

---

## Phase 5 — seal and document

### Task 16: Make the stages internal

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/EmitOptions.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/EmittedSql.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LoweredPlan.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/ResolvedSymbols.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolTable.cs`

- [ ] **Step 1: Confirm `InternalsVisibleTo` is in place**

Run: `git grep -n "InternalsVisibleTo" src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`
Expected: an entry for `Ignixa.Search.Sql.Tests`. It already exists; if it does not, add it before proceeding.

- [ ] **Step 2: Change eight declarations from `public` to `internal`**

`public static class Resolve` → `internal static class Resolve`
`public static class Lower` → `internal static class Lower`
`public static class SqlBuilder` → `internal static class SqlBuilder`
`public sealed record EmitOptions` → `internal sealed record EmitOptions`
`public sealed record EmittedSql` → `internal sealed record EmittedSql`
`public sealed record LoweredPlan` → `internal sealed record LoweredPlan`
`public sealed record ResolvedSymbols` → `internal sealed record ResolvedSymbols`
`public sealed class SymbolTable` / `public sealed record SymbolTable` → `internal`

**`EmittedSqlParameter` stays public** — it is reached through `CompiledSearch.Parameters`. It lives in the same file as `EmittedSql`; change only the latter.

- [ ] **Step 3: Build**

Run: `dotnet build All.sln`

Expected: 0 warnings, 0 errors. If `Ignixa.Application.Tests` or the integration tests fail to compile, a public API gap exists — report which type is needed and do **not** widen `InternalsVisibleTo` to paper over it.

Expect `CA1812`-style "internal class is never instantiated" or accessibility-inconsistency warnings on any public member whose signature still names a now-internal type. Every such member is a leak the seal just exposed; fix the signature rather than reverting the seal.

- [ ] **Step 4: Run everything**

Run: `dotnet test All.sln`
Expected: all pass.

- [ ] **Step 5: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql
git commit -m "refactor(search-sql): seal the compiler stages behind the facade"
```

---

### Task 17: Rewrite the package README

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/README.md`

- [ ] **Step 1: Fix the alpha warning**

It currently names `Resolve / Lower / SqlBuilder` as the public API. Replace that sentence with one naming `ISearchSqlCompiler` and `SearchPlan`, and stating that the stage classes are internal.

- [ ] **Step 2: Replace the quick start**

```markdown
## Quick start

```csharp
var compiler = new SearchSqlCompiler(resolver, optionsBuilder);

// Phase 1 — build, resolve, lower. Asynchronous: resolving symbols is the only I/O.
SearchPlan plan = await compiler.CreatePlanAsync("Patient", parameters, cancellationToken: cancellationToken);

// Optional: inspect or rewrite the plan before any SQL exists.
Console.WriteLine(plan.Query.Explain());
plan = plan with { Query = plan.Query with { Top = 50 } };

// Phase 2 — emit. Synchronous: lowering and emission are pure.
CompiledSearch compiled = plan.Compile();
```

When a query-shape problem should be data rather than an exception, use the `Try` pair:

```csharp
var result = await compiler.TryCreatePlanAsync("Patient", parameters, cancellationToken: cancellationToken);
if (!result.Succeeded)
{
    logger.LogWarning("Search failed at {Stage}: {Message}", result.Failure!.Stage, result.Failure.Message);
    return;
}
```

Diagnostics are opt-in and off by default:

```csharp
var options = new SearchPlanOptions { DiagnosticsLevel = SearchDiagnosticsLevel.Full };
var plan = await compiler.CreatePlanAsync("Patient", parameters, options, cancellationToken);
foreach (var parameter in plan.Diagnostics!.Parameters)
{
    Console.WriteLine($"{parameter.Name}: {parameter.Outcome}");
}
```
```

- [ ] **Step 3: Reframe the stage diagram**

The three-stage diagram stays, retitled from a description of the API to **"How it works inside"**, with a sentence noting the stages are internal and that the boundary a consumer sees is `CreatePlanAsync` / `Compile`.

- [ ] **Step 4: Verify the samples compile**

Paste each snippet into a scratch test, build, delete the scratch test. A README sample that does not compile is worse than none.

- [ ] **Step 5: Commit** (ask the user first)

```bash
git add src/Core/Ignixa.Search.Sql/README.md
git commit -m "docs(search-sql): document the two-phase compiler API"
```

---

## Final verification

- [ ] `dotnet build All.sln` → 0 warnings, 0 errors
- [ ] `dotnet test All.sln` → all pass
- [ ] `git status` shows **no modified corpus or golden SQL baseline files** across the whole branch — verify with `git diff --stat main...HEAD -- test/Ignixa.Search.Sql.Tests/Corpus` and the golden SQL directory. A moved baseline means behaviour changed and the refactor is wrong.
- [ ] `git grep -n "Ignixa\.Search\.Sql\.Tracing"` → zero hits
- [ ] `git grep -n "LowerOptions" -- src` → zero hits (it lives in the test project now)
- [ ] `Ignixa.Application.Tests` compiles with no `InternalsVisibleTo` into `Ignixa.Search.Sql`

### Deliberately not in this plan

**No DI registration task.** The spec says the layer that supplies `ISymbolResolver` registers `ISearchSqlCompiler` alongside it. That layer is `Ignixa.DataLayer.SqlEntityFramework` (`Search/SqlEntityFrameworkSymbolResolver.cs`), but `git grep -n "SqlEntityFrameworkSymbolResolver" -- src` shows it is only ever declared — nothing registers it in a container today, so there is no registration site to add a second line to. `Ignixa.Search.Sql` deliberately takes no DI-container reference, so `SearchSqlCompiler` is a plain class with a public constructor; whichever composition root first registers the resolver registers the compiler beside it. Adding a registration now would mean inventing the consumer.

**No `IsUnsatisfiable` fast path.** `CompilationDiagnosticsBuilder.MarkKnownMisses` flags a CTE that lowered to `Predicate.False`. Promoting that to a whole-plan "skip the round trip" is out of scope: a false predicate in one CTE does not make the plan unsatisfiable, and getting the promotion rule right needs its own design pass.

