# Composite Search Parameter Structure Preservation (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry each composite FHIR search parameter component's resolved (effective) `SearchParameterInfo`
and position from parse time all the way to SQL query generation, replacing heuristic
`ComponentIndex`-based reconstruction and expression-shape sniffing with direct reads — and fix a
confirmed pre-existing bug (composite OR-of-value-groups) exposed while rewriting the extraction logic.

**Architecture:** A new `CompositeComponentExpression : Expression` wraps each composite component at
parse time, carrying its effective `SearchParameterInfo` and position. A new
`IExpressionVisitor.VisitCompositeComponent` method is added (compile-enforced across all 4 root
implementers). The SQL backend unwraps components in `SearchParameterQueryGenerator` — before calling
`CompositeSearchParameterQueryGenerator`, whose public methods and existing test suite are untouched.

**Tech Stack:** C# / .NET 10, EF Core, xUnit + Shouldly + NSubstitute, existing `Ignixa.Search`/
`Ignixa.DataLayer.SqlEntityFramework` architecture — no new dependencies.

## Global Constraints

- Build: `dotnet build All.sln` must be 0 warnings, 0 errors after every task.
- Test: `dotnet test` on touched projects must be green after every task (pre-existing unrelated
  failures — 5 documented failures in `Ignixa.DataLayer.LegacySqlEF.Tests` from the EF Core InMemory
  provider's `EF.Constant()`/`Collate` translation gap — are expected and not a blocker).
- No `#region` blocks. 4-space indentation. File-scoped namespaces.
- `CompositeSearchParameterQueryGenerator`'s public method signatures (`GenerateTokenTokenQueryAsync`,
  `GenerateTokenQuantityQueryAsync`, `GenerateTokenStringQueryAsync`, `GenerateTokenDateTimeQueryAsync`,
  `GenerateReferenceTokenQueryAsync`) and `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs`
  must NOT change. This is a hard boundary, not a style preference — verify with `git diff --stat` on
  both files before the final task's commit and confirm zero changes.
- Leaf-level `ComponentIndex` (on `BinaryExpression`/`StringExpression`/etc.) stays exactly as-is.
  `DateTimeEqualityRewriter.MatchPattern` and expression equality/hashing depend on it. Do not remove
  or repurpose it in this plan.
- This plan deliberately changes two behaviors beyond pure refactoring — call both out explicitly in
  the final task's commit message and PR description:
  1. Composite OR-of-value-groups (e.g. `code-value-quantity=a$1,b$2`) now unions per-group results
     instead of (incorrectly) ANDing components across groups.
  2. Reference|Token composite searches where both components resolve to the same effective type
     (ambiguous order) now return empty results with a warning, instead of assuming position order
     and returning plausible-but-possibly-wrong filtered results.
- Full design context: `docs/superpowers/specs/2026-07-11-composite-structure-preservation-design.md`.
  Read it before starting Task 1 — every task below implements a specific section of it.

---

### Task 1: `CompositeComponentExpression` type and `IExpressionVisitor.VisitCompositeComponent`

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/DefaultExpressionVisitor.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs`
- Modify: `src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs`
- Test: `test/Ignixa.Application.Tests/Search/CompositeComponentExpressionTests.cs` (new)
- Test: `test/Ignixa.Application.Tests/Search/ExpressionRewriterCompositeComponentTests.cs` (new)
- Test: `test/Ignixa.Application.Tests/Search/SearchQueryInterpreterCompositeComponentTests.cs` (new)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchExpressionQueryBuilderCompositeComponentTests.cs` (new)

**Interfaces:**
- Produces: `CompositeComponentExpression(SearchParameterInfo componentSearchParameter, int position,
  Expression wrappedExpression)` — public properties `ComponentSearchParameter`, `Position`,
  `WrappedExpression`. Does NOT implement `IFieldExpression`. `IExpressionVisitor<TContext,TOutput>.VisitCompositeComponent(CompositeComponentExpression expression, TContext context)`.

This is one atomic task: adding an interface method requires every implementer to add it in the same
commit, or the solution doesn't compile. There are exactly 4 root implementers in this codebase —
`DefaultExpressionVisitor<TContext,TOutput>`, `ExpressionRewriter<TContext>`, `SearchQueryInterpreter`,
`SearchExpressionQueryBuilder` (verified by direct read of all four files; `CompartmentSearchRewriter`
and `DateTimeEqualityRewriter` inherit from `ExpressionRewriter<TContext>` and don't implement the
interface directly, so they need no changes in this task).

- [ ] **Step 1: Write the failing test for the type itself**

Create `test/Ignixa.Application.Tests/Search/CompositeComponentExpressionTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

public class CompositeComponentExpressionTests
{
    private static readonly SearchParameterInfo TokenComponentParam =
        new("code", "code", SearchParamType.Token);

    [Fact]
    public void GivenComponent_WhenConstructed_ThenExposesComponentSearchParameterPositionAndWrapped()
    {
        var wrapped = Expression.Equals(FieldName.TokenCode, 0, "8480-6");

        var component = new CompositeComponentExpression(TokenComponentParam, 0, wrapped);

        component.ComponentSearchParameter.ShouldBe(TokenComponentParam);
        component.Position.ShouldBe(0);
        component.WrappedExpression.ShouldBe(wrapped);
    }

    [Fact]
    public void GivenComponent_WhenAcceptVisitor_ThenDispatchesToVisitCompositeComponent()
    {
        var wrapped = Expression.Equals(FieldName.TokenCode, 0, "8480-6");
        var component = new CompositeComponentExpression(TokenComponentParam, 0, wrapped);
        var visitor = new RecordingVisitor();

        var result = component.AcceptVisitor(visitor, context: 0);

        result.ShouldBeSameAs(component);
        visitor.VisitedComponent.ShouldBeSameAs(component);
    }

    [Fact]
    public void GivenComponent_WhenToString_ThenIncludesPositionAndCode()
    {
        var wrapped = Expression.Equals(FieldName.TokenCode, 0, "8480-6");
        var component = new CompositeComponentExpression(TokenComponentParam, 1, wrapped);

        component.ToString().ShouldContain("[1]");
        component.ToString().ShouldContain("code");
    }

    [Fact]
    public void GivenTwoComponentsWithSamePositionAndEquivalentWrapped_WhenValueInsensitiveEquals_ThenTrue()
    {
        var a = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var b = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "b"));

        a.ValueInsensitiveEquals(b).ShouldBeTrue();
    }

    [Fact]
    public void GivenTwoComponentsWithDifferentPosition_WhenValueInsensitiveEquals_ThenFalse()
    {
        var a = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var b = new CompositeComponentExpression(TokenComponentParam, 1, Expression.Equals(FieldName.TokenCode, 0, "a"));

        a.ValueInsensitiveEquals(b).ShouldBeFalse();
    }

    private sealed class RecordingVisitor : IExpressionVisitor<int, Expression>
    {
        public CompositeComponentExpression VisitedComponent { get; private set; }

        public Expression VisitCompositeComponent(CompositeComponentExpression expression, int context)
        {
            VisitedComponent = expression;
            return expression;
        }

        public Expression VisitSearchParameter(SearchParameterExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitBinary(BinaryExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitChained(ChainedExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitMissingField(MissingFieldExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitMissingSearchParameter(MissingSearchParameterExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitNotExpression(NotExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitMultiary(MultiaryExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitString(StringExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitCompartment(CompartmentSearchExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitInclude(IncludeExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitSortParameter(SortExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitIn<T>(InExpression<T> expression, int context) => throw new NotImplementedException();
        public Expression VisitUnion(UnionExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitPatientEverything(PatientEverythingExpression expression, int context) => throw new NotImplementedException();
        public Expression VisitNotReferenced(NotReferencedExpression expression, int context) => throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~CompositeComponentExpressionTests"`
Expected: FAIL to compile — `CompositeComponentExpression` and `IExpressionVisitor.VisitCompositeComponent` don't exist yet.

- [ ] **Step 3: Create `CompositeComponentExpression`**

Create `src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Wraps one component of a composite search parameter expression, carrying the component's
/// effective (value-inferred) <see cref="SearchParameterInfo"/> and position from parse time
/// through to query generation. Does not implement <see cref="IFieldExpression"/> - the wrapped
/// expression is frequently a <see cref="MultiaryExpression"/> with no single field name, and
/// nothing needs to query this type's identity through that interface.
/// </summary>
public sealed class CompositeComponentExpression : Expression
{
    public CompositeComponentExpression(SearchParameterInfo componentSearchParameter, int position, Expression wrappedExpression)
    {
        EnsureArg.IsNotNull(componentSearchParameter, nameof(componentSearchParameter));
        EnsureArg.IsNotNull(wrappedExpression, nameof(wrappedExpression));

        ComponentSearchParameter = componentSearchParameter;
        Position = position;
        WrappedExpression = wrappedExpression;
    }

    /// <summary>
    /// Gets the effective search parameter for this component - the value-inferred type when it
    /// diverges from the static component definition (e.g. DocumentReference's swapped
    /// <c>relationship</c> component definitions), otherwise the static definition itself.
    /// </summary>
    public SearchParameterInfo ComponentSearchParameter { get; }

    /// <summary>
    /// Gets the zero-based position of this component within the composite search parameter.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// Gets the expression built for this component's value.
    /// </summary>
    public Expression WrappedExpression { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        return visitor.VisitCompositeComponent(this, context);
    }

    public override string ToString()
        => $"(Component[{Position}] {ComponentSearchParameter.Code} {WrappedExpression})";

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(CompositeComponentExpression));
        hashCode.Add(Position);
        WrappedExpression.AddValueInsensitiveHashCode(ref hashCode);
    }

    public override bool ValueInsensitiveEquals(Expression other)
        => other is CompositeComponentExpression cce &&
           cce.Position == Position &&
           WrappedExpression.ValueInsensitiveEquals(cce.WrappedExpression);
}
```

- [ ] **Step 4: Add `VisitCompositeComponent` to `IExpressionVisitor`**

In `src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs`, add after `VisitNotReferenced` (the last
member, currently ending the interface at line 118):

```csharp

    /// <summary>
    /// Visits the <see cref="CompositeComponentExpression"/>.
    /// </summary>
    /// <param name="expression">The expression to visit.</param>
    /// <param name="context">The input</param>
    TOutput VisitCompositeComponent(CompositeComponentExpression expression, TContext context);
```

- [ ] **Step 5: Build to confirm all 4 implementers now fail to compile**

Run: `dotnet build All.sln`
Expected: Compile errors in `DefaultExpressionVisitor.cs`, `ExpressionRewriter.cs`,
`SearchQueryInterpreter.cs`, `SearchExpressionQueryBuilder.cs` — each "does not implement interface
member `IExpressionVisitor<...>.VisitCompositeComponent`". This confirms the interface addition is
compile-enforced across every implementer, with no way to silently miss one.

- [ ] **Step 6: Implement `DefaultExpressionVisitor.VisitCompositeComponent`**

In `src/Core/Ignixa.Search/Expressions/DefaultExpressionVisitor.cs`, add after `VisitNotReferenced`
(currently the last method, ending at line 120):

```csharp

    public virtual TOutput VisitCompositeComponent(CompositeComponentExpression expression, TContext context)
    {
        return default;
    }
```

This class is `internal abstract` and currently has no subclasses anywhere in this repository (verified
by grep) - this default matches the pattern every other scan-only member of this class already uses
(`VisitBinary`, `VisitMissingField`, etc. all `=> default;`), kept in sync for any future implementer.

- [ ] **Step 7: Implement `ExpressionRewriter.VisitCompositeComponent` with rebuild-if-changed semantics**

In `src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs`, add after `VisitNotReferenced` (currently
the last interface method, ending at line 99, immediately before the `protected IReadOnlyList<TExpression>
VisitArray` helper):

```csharp

    public virtual Expression VisitCompositeComponent(CompositeComponentExpression expression, TContext context)
    {
        Expression visitedExpression = expression.WrappedExpression.AcceptVisitor(this, context);
        if (ReferenceEquals(visitedExpression, expression.WrappedExpression)) return expression;

        return new CompositeComponentExpression(expression.ComponentSearchParameter, expression.Position, visitedExpression);
    }
```

This must rebuild the wrapper around the rewritten inner expression, not strip it — a naive
`return expression.WrappedExpression.AcceptVisitor(this, context);` would silently discard the
`CompositeComponentExpression` identity on any rewrite that changes the wrapped expression. Task 3
adds the regression test proving why this matters (`DateTimeEqualityRewriter`'s composite Date `eq`
rewrite).

- [ ] **Step 8: Implement `SearchQueryInterpreter.VisitCompositeComponent` (passthrough)**

In `src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs`, add after `VisitNotReferenced`
(currently the last interface method, ending at line 279):

```csharp

    public SearchPredicate VisitCompositeComponent(CompositeComponentExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));
        EnsureArg.IsNotNull<Context>(context, nameof(context));

        return expression.WrappedExpression.AcceptVisitor(this, context);
    }
```

This class has zero `ComponentIndex`/component-identity awareness today (`VisitBinary` matches purely
by `context.ParameterName`), so evaluating the wrapped predicate directly is behavior-preserving.

- [ ] **Step 9: Implement `SearchExpressionQueryBuilder.VisitCompositeComponent` (throw)**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs`, add
after the last explicit interface method (find it by searching for `VisitNotReferenced` in this file,
matching the file's existing explicit-interface-implementation style, e.g.
`async Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitSearchParameter(...)`):

```csharp

    Task<IQueryable<ResourceEntity>> IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>.VisitCompositeComponent(CompositeComponentExpression expression, SqlQueryContext context)
    {
        throw new NotSupportedException(
            "CompositeComponentExpression must be unwrapped by SearchParameterQueryGenerator before reaching the generic visitor dispatch.");
    }
```

Not `async` - it never awaits, and marking it `async` would produce a CS1998 warning ("this async
method lacks 'await' operators"), which this repo treats as a build error. `VisitSearchParameter`
(this class, line 121) unconditionally delegates all search-parameter handling to
`SearchParameterQueryGenerator.GenerateQueryAsync`, which walks composite structure directly rather
than via generic `AcceptVisitor` recursion into components — there is no legitimate path by which a
`CompositeComponentExpression` reaches this method. Throwing surfaces a future wiring bug immediately
instead of silently evaluating a multi-field component as an uncorrelated predicate.

- [ ] **Step 10: Run the Task 1 unit test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~CompositeComponentExpressionTests"`
Expected: PASS, 5/5.

- [ ] **Step 11: Write and run the `ExpressionRewriter` rebuild test**

Create `test/Ignixa.Application.Tests/Search/ExpressionRewriterCompositeComponentTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Proves ExpressionRewriter's default VisitCompositeComponent rebuilds the wrapper around a
/// rewritten inner expression instead of stripping it - a naive unwrap-and-return would silently
/// discard the CompositeComponentExpression identity on any rewrite that changes the wrapped
/// expression. Task 3 adds the realistic end-to-end regression test via DateTimeEqualityRewriter.
/// </summary>
public class ExpressionRewriterCompositeComponentTests
{
    private static readonly SearchParameterInfo TokenComponentParam = new("code", "code", SearchParamType.Token);

    [Fact]
    public void GivenComponentWhoseInnerExpressionChanges_WhenRewritten_ThenWrapperIsRebuiltAroundNewInner()
    {
        var original = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var replacement = Expression.Equals(FieldName.TokenCode, 0, "b");
        var rewriter = new ReplacingRewriter(replacement);

        var result = rewriter.VisitCompositeComponent(original, context: 0);

        var rebuilt = result.ShouldBeOfType<CompositeComponentExpression>();
        rebuilt.Position.ShouldBe(0);
        rebuilt.ComponentSearchParameter.ShouldBe(TokenComponentParam);
        rebuilt.WrappedExpression.ShouldBeSameAs(replacement);
    }

    [Fact]
    public void GivenComponentWhoseInnerExpressionDoesNotChange_WhenRewritten_ThenSameInstanceReturned()
    {
        var original = new CompositeComponentExpression(TokenComponentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var rewriter = new NoOpRewriter();

        var result = rewriter.VisitCompositeComponent(original, context: 0);

        result.ShouldBeSameAs(original);
    }

    private sealed class ReplacingRewriter(Expression replacement) : ExpressionRewriter<int>
    {
        public override Expression VisitBinary(BinaryExpression expression, int context) => replacement;
    }

    private sealed class NoOpRewriter : ExpressionRewriter<int>
    {
    }
}
```

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~ExpressionRewriterCompositeComponentTests"`
Expected: PASS, 2/2.

- [ ] **Step 12: Write and run the `SearchQueryInterpreter` passthrough test**

Create `test/Ignixa.Application.Tests/Search/SearchQueryInterpreterCompositeComponentTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

public class SearchQueryInterpreterCompositeComponentTests
{
    private static readonly SearchParameterInfo TokenComponentParam = new("code", "code", SearchParamType.Token);

    [Fact]
    public void GivenComponent_WhenVisited_ThenPredicateComesFromWrappedExpression()
    {
        var interpreter = new SearchQueryInterpreter();
        var context = interpreter.InitialContext.WithParameterName("code");
        var wrapped = Expression.StringEquals(FieldName.TokenCode, null, "8480-6", false);
        var direct = wrapped.AcceptVisitor(interpreter, context);

        var component = new CompositeComponentExpression(TokenComponentParam, 0, wrapped);
        var viaWrapper = interpreter.VisitCompositeComponent(component, context);

        // Both predicates are functionally equivalent - built from the identical wrapped expression
        // via the identical visitor and context, so they must have the same delegate target method.
        direct.Method.ShouldBe(viaWrapper.Method);
    }
}
```

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchQueryInterpreterCompositeComponentTests"`
Expected: PASS, 1/1.

- [ ] **Step 13: Write and run the `SearchExpressionQueryBuilder` throw test**

Create `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchExpressionQueryBuilderCompositeComponentTests.cs`.
An existing test, `SearchExpressionQueryBuilderVisitorTests.cs` (same directory), already constructs a
full `SearchExpressionQueryBuilder` with all six collaborators — copy its constructor-setup block
exactly (`CompositeSearchParameterQueryGenerator` → `SearchParameterQueryGenerator` →
`ChainedExpressionProcessor` → `CompartmentSearchQueryGenerator` → `PatientEverythingQueryGenerator` →
`SearchExpressionQueryBuilder`, each built from real instances over `Context`/`Cache`, only
`ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` substituted):

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using NSubstitute;
using Shouldly;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

public class SearchExpressionQueryBuilderCompositeComponentTests : TestBase
{
    [Fact]
    public void GivenCompositeComponentExpression_WhenVisitedDirectly_ThenThrowsNotSupported()
    {
        var compositeGenerator = new CompositeSearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<CompositeSearchParameterQueryGenerator>());
        var parameterGenerator = new SearchParameterQueryGenerator(
            Context, Cache, LoggerFactory.CreateLogger<SearchParameterQueryGenerator>(), compositeGenerator);
        var chainedProcessor = new ChainedExpressionProcessor(
            Context, Cache, parameterGenerator, LoggerFactory.CreateLogger<ChainedExpressionProcessor>());
        var compartmentGenerator = new CompartmentSearchQueryGenerator(
            Context,
            Cache,
            Substitute.For<ICompartmentDefinitionManager>(),
            Substitute.For<ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<CompartmentSearchQueryGenerator>());
        var patientEverythingGenerator = new PatientEverythingQueryGenerator(
            Context, compartmentGenerator, LoggerFactory.CreateLogger<PatientEverythingQueryGenerator>());

        var visitor = (IExpressionVisitor<SqlQueryContext, Task<IQueryable<ResourceEntity>>>)new SearchExpressionQueryBuilder(
            Context,
            parameterGenerator,
            chainedProcessor,
            compartmentGenerator,
            patientEverythingGenerator,
            Substitute.For<ISearchParameterDefinitionManager>(),
            LoggerFactory.CreateLogger<SearchExpressionQueryBuilder>());

        var componentParam = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var component = new CompositeComponentExpression(componentParam, 0, Expression.Equals(FieldName.TokenCode, 0, "a"));
        var context = new SqlQueryContext(Context.Resources, ResourceTypeId: 3, CancellationToken.None);

        Should.Throw<NotSupportedException>(() => visitor.VisitCompositeComponent(component, context));
    }
}
```

`SqlQueryContext` is a `readonly record struct SqlQueryContext(IQueryable<ResourceEntity> BaseQuery,
short? ResourceTypeId, CancellationToken CancellationToken)` (confirmed by direct read,
`SearchExpressionQueryBuilder.cs:21-24`) — constructed with named arguments above to avoid relying on
positional order.

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchExpressionQueryBuilderCompositeComponentTests"`
Expected: PASS, 1/1.

- [ ] **Step 14: Full build and full test run to check for regressions**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test All.sln`
Expected: same pre-existing failures as documented in Global Constraints, nothing new broken.

- [ ] **Step 15: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs src/Core/Ignixa.Search/Expressions/DefaultExpressionVisitor.cs src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs test/Ignixa.Application.Tests/Search/CompositeComponentExpressionTests.cs test/Ignixa.Application.Tests/Search/ExpressionRewriterCompositeComponentTests.cs test/Ignixa.Application.Tests/Search/SearchQueryInterpreterCompositeComponentTests.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchExpressionQueryBuilderCompositeComponentTests.cs
git commit -m "feat(search): add CompositeComponentExpression and IExpressionVisitor.VisitCompositeComponent"
```

---

### Task 2: Parser wraps composite components with their effective `SearchParameterInfo`

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:158-162`
- Test: `test/Ignixa.Application.Tests/Search/SearchParameterExpressionParserCompositeTests.cs` (new)

**Interfaces:**
- Consumes: `CompositeComponentExpression` (Task 1).
- Produces: the composite branch of `SearchParameterExpressionParser.Parse` now wraps each
  component's built expression in a `CompositeComponentExpression` carrying `effectiveSearchParameter`
  (the possibly-synthetic, value-inferred `SearchParameterInfo` - never the raw
  `Component[i].ResolvedSearchParameter`) and the zero-based `componentIndex` as `Position`.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Application.Tests/Search/SearchParameterExpressionParserCompositeTests.cs`. This
test constructs a synthetic composite `SearchParameterInfo` directly (Token component + Reference
component, mirroring DocumentReference's `relationship`) rather than depending on the real FHIR
definition, so the swap case is deterministic and doesn't depend on specification data. Before
writing this test, read `SearchParameterExpressionParser`'s public `Parse` method signature and the
`ISearchParameterExpressionParser` interface (both in
`src/Core/Ignixa.Search/Expressions/Parsers/`) to confirm the exact call shape - prior test files in
this codebase call it as `_parser.Parse(searchParameter, modifier, value)` returning `Expression`,
cast to `SearchParameterExpression`. Also confirm `TokenSearchValue.Parse`/`ReferenceSearchValue.Parse`
accept the literal string forms used below (`sys|code` for token, `Patient/123` for reference) by
checking their static `Parse` methods:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using NSubstitute;
using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

public class SearchParameterExpressionParserCompositeTests
{
    private readonly SearchParameterExpressionParser _parser = new(
        Substitute.For<IReferenceSearchValueParser>(),
        Substitute.For<IFhirSchemaProvider>());

    private static SearchParameterInfo CreateReferenceTokenComposite()
    {
        var referenceComponentDefinition = new SearchParameterInfo("relationship-target", "relationship-target", SearchParamType.Reference);
        var codeComponentDefinition = new SearchParameterInfo("relationship-type", "relationship-type", SearchParamType.Token);

        return new SearchParameterInfo(
            "relationship",
            "relationship",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = referenceComponentDefinition },
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeComponentDefinition },
            ]);
    }

    [Fact]
    public void GivenCompositeValue_WhenParsed_ThenEachComponentIsWrappedWithPositionAndEffectiveType()
    {
        var composite = CreateReferenceTokenComposite();

        var result = (SearchParameterExpression)_parser.Parse(composite, modifier: null, "Patient/123$sys|code1");
        var and = (MultiaryExpression)result.Expression;

        and.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        and.Expressions.Count.ShouldBe(2);

        var component0 = (CompositeComponentExpression)and.Expressions[0];
        component0.Position.ShouldBe(0);
        component0.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Reference);

        var component1 = (CompositeComponentExpression)and.Expressions[1];
        component1.Position.ShouldBe(1);
        component1.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Token);
    }

    [Fact]
    public void GivenValueThatDivergesFromStaticDefinition_WhenParsed_ThenEffectiveTypeIsValueInferredNotStatic()
    {
        // Static definitions say [Reference, Token] (position 0 = Reference, position 1 = Token),
        // but position 0's actual value ("sys|code0") looks Token-shaped and position 1's actual
        // value ("Patient/123") looks Reference-shaped - the DocumentReference "relationship" swap.
        var composite = CreateReferenceTokenComposite();

        var result = (SearchParameterExpression)_parser.Parse(composite, modifier: null, "sys|code0$Patient/123");
        var and = (MultiaryExpression)result.Expression;

        var component0 = (CompositeComponentExpression)and.Expressions[0];
        component0.Position.ShouldBe(0);
        component0.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Token);

        var component1 = (CompositeComponentExpression)and.Expressions[1];
        component1.Position.ShouldBe(1);
        component1.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Reference);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchParameterExpressionParserCompositeTests"`
Expected: FAIL — `and.Expressions[0]`/`[1]` are today's bare `Build(...)` output (e.g. `StringExpression`),
not `CompositeComponentExpression`; the cast throws `InvalidCastException`.

- [ ] **Step 3: Wrap each component at construction**

In `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs`, replace:

```csharp
                        compositeExpressions[componentIndex] = Build(
                            effectiveSearchParameter,
                            null,
                            componentIndex,
                            componentValue);
```

with:

```csharp
                        Expression componentExpression = Build(
                            effectiveSearchParameter,
                            null,
                            componentIndex,
                            componentValue);

                        compositeExpressions[componentIndex] = new CompositeComponentExpression(
                            effectiveSearchParameter,
                            componentIndex,
                            componentExpression);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchParameterExpressionParserCompositeTests"`
Expected: PASS, 2/2.

- [ ] **Step 5: Full build and test run to check for regressions**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test All.sln`
Expected: this task's change makes every composite search parameter's expression tree shape change
(each component is now wrapped), so failures are expected here until Task 4 updates
`SearchParameterQueryGenerator` to understand the new shape. Confirm the *newly* failing tests are all
composite-related (in `CompositeSearchParameterQueryGeneratorTests.cs`'s consumers or
`SearchParameterQueryGenerator`-level composite tests) and that non-composite tests are unaffected. Do
not attempt to fix composite query generation in this task - that's Task 4. Record the exact list of
newly-failing composite tests in the task report so Task 4's implementer can confirm the same tests go
green.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs test/Ignixa.Application.Tests/Search/SearchParameterExpressionParserCompositeTests.cs
git commit -m "feat(search): wrap composite components in CompositeComponentExpression at parse time"
```

---

### Task 3: `ExpressionRewriter` regression test — composite Date range wrapper survives `DateTimeEqualityRewriter`

**REVISED after a real discovery during first execution attempt** — see note below before implementing.

**Files:**
- Modify: `src/Core/Ignixa.Search/Ignixa.Search.csproj`
- Test: `test/Ignixa.Application.Tests/Search/DateTimeEqualityRewriterCompositeTests.cs` (new)

**Interfaces:**
- Consumes: `CompositeComponentExpression` (Task 1), `DateTimeEqualityRewriter` (existing,
  `src/Core/Ignixa.Search/Expressions/DateTimeEqualityRewriter.cs`, `internal`).
- Produces: no new production logic — this task is pure regression coverage for the specific bug
  Task 1 Step 7's rebuild-if-changed implementation prevents, plus the `InternalsVisibleTo` grant this
  test needs to exist. If the test's assertions fail, Task 1 was not implemented correctly; do not
  "fix" it by changing this test.

**Why this task no longer goes through the parser or the `eq` comparator:** the first execution
attempt correctly discovered and refused to paper over a real, pre-existing, unrelated bug — commit
`23c18854` (2025-12-09, six weeks before this phase) changed `SearchComparator.Eq`'s Date output from
containment semantics (`And(GE(DateTimeStart, x), LE(DateTimeEnd, y))`, the shape
`DateTimeEqualityRewriter.MatchPattern` looks for) to overlap-check semantics (`And(LE(DateTimeStart, y),
GE(DateTimeEnd, x))`, a shape it doesn't recognize). `DateTimeEqualityRewriter`'s 3-expression rewrite
has therefore been dead for every `eq` Date search (composite or plain) since that commit — this is
completely independent of Tasks 1/2/3 in this phase. No safe local fix exists (Fable-verified: under
containment, the third clause is a provably-redundant tightening via the `Start<=End` invariant; under
overlap, no analogous bound is derivable — the real fix needs a range-width-split query redesign,
tracked as follow-up #3 in `docs/superpowers/specs/2026-07-11-composite-structure-preservation-design.md`'s
"Out of scope" section, already written). **The rewriter is not fully dead** — the `ap` (approximate)
comparator still emits the old containment shape, so this task builds the input tree directly in that
still-live shape rather than depending on the parser's (currently unverified for this exact case)
`ap`-on-composite wiring.

This directly exercises the regression surface Task 1's own `ExpressionRewriterCompositeComponentTests.cs`
(synthetic `ReplacingRewriter`, calls `VisitCompositeComponent` directly) does NOT cover: the real
`DateTimeEqualityRewriter` overrides `VisitMultiary` and `VisitSearchParameter` but inherits the default
`VisitCompositeComponent`. The interaction between its overridden `VisitMultiary` (walking the OUTER
`And` of `CompositeComponentExpression` wrappers, correctly falling through to descend rather than
misfiring `MatchPattern` on adjacent wrappers) and the inherited rebuild-on-the-INNER-`And` is exactly
what needs proving, and nothing else in this plan proves it. Do not treat this task as redundant with
Task 1's test or skip it.

- [ ] **Step 1: Grant `Ignixa.Application.Tests` access to `Ignixa.Search`'s internals**

In `src/Core/Ignixa.Search/Ignixa.Search.csproj`, add a new `ItemGroup` (matching the exact pattern
used by `src/Core/Ignixa.FhirPath/Ignixa.FhirPath.csproj`, `src/Core/Ignixa.Validation/Ignixa.Validation.csproj`,
etc.), immediately after the existing `PackageReference` `ItemGroup` (ends at line 33):

```xml

  <ItemGroup>
    <InternalsVisibleTo Include="Ignixa.Application.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Write the test**

Create `test/Ignixa.Application.Tests/Search/DateTimeEqualityRewriterCompositeTests.cs`. Build the
composite expression tree directly (bypassing the parser entirely) in the containment shape that
`DateTimeEqualityRewriter.MatchPattern` still recognizes today (the shape `ap` produces).
`DateTimeEqualityRewriter` exposes `internal static readonly DateTimeEqualityRewriter Instance` and
`public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)`
— call that directly with `context: null` (confirmed unused by this rewriter's logic):

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Application.Tests.Search;

/// <summary>
/// Regression coverage for the specific bug ExpressionRewriter.VisitCompositeComponent's
/// rebuild-if-changed semantics (Task 1) exist to prevent: DateTimeEqualityRewriter rewrites a
/// composite Date component's inner range expression, and the CompositeComponentExpression wrapper
/// around it must survive that rewrite intact. Builds the input tree directly in the containment shape
/// (And(GE(DateTimeStart,x), LE(DateTimeEnd,y))) that DateTimeEqualityRewriter.MatchPattern still
/// recognizes today (the shape the `ap` comparator produces) — NOT via the parser's `eq` comparator,
/// whose output shape changed in commit 23c18854 and no longer matches (tracked as a separate,
/// pre-existing, out-of-scope follow-up; see the design spec).
/// </summary>
public class DateTimeEqualityRewriterCompositeTests
{
    private static SearchParameterInfo CreateTokenDateComposite()
    {
        var tokenComponentDefinition = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var dateComponentDefinition = new SearchParameterInfo("value-date", "value-date", SearchParamType.Date);

        return new SearchParameterInfo(
            "code-value-date",
            "code-value-date",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = tokenComponentDefinition },
                new SearchParameterComponentInfo { ResolvedSearchParameter = dateComponentDefinition },
            ]);
    }

    [Fact]
    public void GivenCompositeContainmentShapedDateRange_WhenDateTimeEqualityRewriterRuns_ThenDateComponentWrapperSurvivesRewrite()
    {
        var composite = CreateTokenDateComposite();
        var tokenComponent = new CompositeComponentExpression(
            composite.Component[0].ResolvedSearchParameter,
            0,
            Expression.StringEquals(FieldName.TokenCode, 0, "8480-6", false));
        var dateComponent = new CompositeComponentExpression(
            composite.Component[1].ResolvedSearchParameter,
            1,
            Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, 1, new DateTime(2020, 6, 1)),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, 1, new DateTime(2020, 6, 1, 23, 59, 59))));
        var parsed = Expression.SearchParameter(composite, Expression.And(tokenComponent, dateComponent));

        var rewritten = (SearchParameterExpression)DateTimeEqualityRewriter.Instance.VisitSearchParameter(parsed, context: null);

        var and = (MultiaryExpression)rewritten.Expression;
        var rewrittenDateComponent = and.Expressions.OfType<CompositeComponentExpression>().Single(c => c.Position == 1);

        // Still wrapped (not stripped) and still carries its Date identity.
        rewrittenDateComponent.ComponentSearchParameter.Type.ShouldBe(SearchParamType.Date);

        // The rewrite fired: the inner expression grew from 2 to 3 range-bound expressions.
        var innerAnd = (MultiaryExpression)rewrittenDateComponent.WrappedExpression;
        innerAnd.Expressions.Count.ShouldBe(3);
    }
}
```

- [ ] **Step 3: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~DateTimeEqualityRewriterCompositeTests"`
Expected: PASS, 1/1 — because Task 1 already implemented the correct rebuild semantics, and this input
tree is in the shape `MatchPattern` still recognizes today. If this fails, stop: either Task 1's rebuild
semantics regressed, or `MatchPattern`'s containment-shape recognition itself has drifted further since
this plan was written — re-examine both `ExpressionRewriter.VisitCompositeComponent` (Task 1 Step 7) and
`DateTimeEqualityRewriter.MatchPattern` before proceeding. Do not weaken this test to make it pass.

- [ ] **Step 4: Full build check**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s) — confirms the new `InternalsVisibleTo` grant doesn't conflict with
anything and every other project still builds clean.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.Search/Ignixa.Search.csproj test/Ignixa.Application.Tests/Search/DateTimeEqualityRewriterCompositeTests.cs
git commit -m "test(search): regression coverage for composite date range wrapper surviving ExpressionRewriter"
```

---

### Task 4: Rewrite `SearchParameterQueryGenerator`'s composite extraction and dispatch

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs:146-295`
  (`ProcessCompositeExpressionAsync`, `ExtractComponentExpressions`)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorCompositeTests.cs` (new)

**Interfaces:**
- Consumes: `CompositeComponentExpression` (Task 1), parser wrapping (Task 2),
  `CompositeSearchParameterQueryGenerator`'s existing unchanged public methods (`GenerateTokenTokenQueryAsync`,
  `GenerateTokenQuantityQueryAsync`, `GenerateTokenStringQueryAsync`, `GenerateTokenDateTimeQueryAsync`,
  `GenerateReferenceTokenQueryAsync`, all `(short? resourceTypeId, short searchParamId, Expression
  component0, Expression component1, CancellationToken cancellationToken) => Task<IQueryable<long>>`),
  `CompositeSearchParameterQueryGenerator.DetermineCompositeType(SearchParameterInfo) => CompositeType`
  (unchanged).
- Produces: `ExtractComponentGroups(Expression) => List<List<CompositeComponentExpression>>`,
  `ExtractSingleGroup(Expression) => List<CompositeComponentExpression>`,
  `GenerateGroupQueryAsync(short?, short, CompositeType, List<CompositeComponentExpression>,
  CancellationToken) => Task<IQueryable<long>>`, `GenerateReferenceTokenGroupQueryAsync(short?, short,
  List<CompositeComponentExpression>, CancellationToken) => Task<IQueryable<long>>`,
  `UnwrapCompositeComponents(Expression) => Expression`, `CombineWithOr(List<IQueryable<long>>) =>
  IQueryable<long>` (all `private static` on `SearchParameterQueryGenerator`). `ExtractComponentExpressions`
  is deleted.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorCompositeTests.cs`.
This drives the parser and `SearchParameterQueryGenerator` end-to-end (unlike
`CompositeSearchParameterQueryGeneratorTests.cs`, which stays untouched and calls the composite
generator directly with hand-built expressions) — read
`test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorDateTimeTests.cs`
first (already read this session) for the constructor/setup pattern (`SearchParameterQueryGenerator` +
`CompositeSearchParameterQueryGenerator` + `SearchParameterExpressionParser`, `Context.SearchParams.Add`
for cache lookups) and reuse it:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.DataLayer.SqlEntityFramework.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.DataLayer.SqlEntityFramework.Tests.Search;

/// <summary>
/// End-to-end coverage (parser through SearchParameterQueryGenerator) for composite structure
/// preservation: replaces ComponentIndex-heuristic extraction with direct CompositeComponentExpression
/// reads, fixes OR-of-value-groups (previously ANDed components across groups), and replaces
/// IsReferenceExpression/IsTokenExpression sniffing with effective-type-based ordering.
/// CompositeSearchParameterQueryGeneratorTests.cs (hand-built expressions, calls the composite
/// generator directly) is untouched by this change - do not add anything there.
/// </summary>
public class SearchParameterQueryGeneratorCompositeTests : TestBase
{
    private const short ObservationTypeId = 3;
    private const short CodeValueQuantityParamId = 200;
    private const short RelationshipParamId = 201;

    private readonly SearchParameterQueryGenerator _generator;
    private readonly SearchParameterExpressionParser _parser;

    public SearchParameterQueryGeneratorCompositeTests()
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
    }

    private static SearchParameterInfo CreateCodeValueQuantityComposite()
    {
        var codeComponent = new SearchParameterInfo("code", "code", SearchParamType.Token);
        var quantityComponent = new SearchParameterInfo("value-quantity", "value-quantity", SearchParamType.Quantity);

        return new SearchParameterInfo(
            "code-value-quantity",
            "code-value-quantity",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeComponent },
                new SearchParameterComponentInfo { ResolvedSearchParameter = quantityComponent },
            ]);
    }

    private static SearchParameterInfo CreateRelationshipComposite()
    {
        var referenceComponent = new SearchParameterInfo("relationship-target", "relationship-target", SearchParamType.Reference);
        var codeComponent = new SearchParameterInfo("relationship-type", "relationship-type", SearchParamType.Token);

        return new SearchParameterInfo(
            "relationship",
            "relationship",
            SearchParamType.Composite,
            components:
            [
                new SearchParameterComponentInfo { ResolvedSearchParameter = referenceComponent },
                new SearchParameterComponentInfo { ResolvedSearchParameter = codeComponent },
            ]);
    }

    private async Task<long> CreateObservationWithTokenQuantityAsync(string resourceId, string code, decimal low, decimal high)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        Context.TokenQuantityCompositeSearchParams.Add(new TokenQuantityCompositeSearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = CodeValueQuantityParamId,
            Code1 = code,
            SystemId1 = null,
            LowValue = low,
            HighValue = high,
        });
        await Context.SaveChangesAsync();

        return resource.ResourceSurrogateId;
    }

    private async Task<long> CreateDocumentReferenceRelationshipAsync(string resourceId, string referenceResourceId, string tokenCode)
    {
        var resource = CreateResource(ObservationTypeId, resourceId);

        Context.ReferenceTokenCompositeSearchParams.Add(new ReferenceTokenCompositeSearchParamEntity
        {
            ResourceTypeId = ObservationTypeId,
            ResourceSurrogateId = resource.ResourceSurrogateId,
            SearchParamId = RelationshipParamId,
            ReferenceResourceType1 = "DocumentReference",
            ReferenceResourceId1 = referenceResourceId,
            Code2 = tokenCode,
            SystemId2 = null,
        });
        await Context.SaveChangesAsync();

        return resource.ResourceSurrogateId;
    }

    private async Task<List<long>> RunCompositeSearchAsync(SearchParameterInfo composite, short searchParamId, string queryValue)
    {
        Context.SearchParams.Add(new SearchParamEntity
        {
            SearchParamId = searchParamId,
            Uri = $"http://example.org/SearchParameter/{composite.Code}",
            Status = "Enabled",
            LastUpdated = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        var expression = (SearchParameterExpression)_parser.Parse(composite, modifier: null, queryValue);
        var query = await _generator.GenerateQueryAsync(ObservationTypeId, expression, CancellationToken.None);
        return await query.ToListAsync();
    }

    [Fact]
    public async Task GivenTokenQuantityComposite_WhenSingleValueGroup_ThenReturnsMatchingResource()
    {
        var matching = await CreateObservationWithTokenQuantityAsync("obs-match", "8462-4", 80m, 80m);
        await CreateObservationWithTokenQuantityAsync("obs-nomatch", "8462-4", 90m, 90m);

        var results = await RunCompositeSearchAsync(CreateCodeValueQuantityComposite(), CodeValueQuantityParamId, "8462-4$80");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenTokenQuantityComposite_WhenOrOfTwoValueGroups_ThenUnionsPerGroupResultsInsteadOfAndingAcrossGroups()
    {
        // Regression coverage for the confirmed pre-existing bug: today's ComponentIndex-based
        // extraction merges components across OR groups by index and ANDs them, so
        // "8462-4$80,8462-5$90" would incorrectly require a single row matching code=8462-4 AND
        // code=8462-5 AND value=80 AND value=90 simultaneously - impossible, always empty.
        // Correct FHIR semantics: each comma-separated group is an independent match candidate,
        // OR'd together.
        var matchesGroup1 = await CreateObservationWithTokenQuantityAsync("obs-group1", "8462-4", 80m, 80m);
        var matchesGroup2 = await CreateObservationWithTokenQuantityAsync("obs-group2", "8462-5", 90m, 90m);
        await CreateObservationWithTokenQuantityAsync("obs-neither", "8462-6", 70m, 70m);

        var results = await RunCompositeSearchAsync(CreateCodeValueQuantityComposite(), CodeValueQuantityParamId, "8462-4$80,8462-5$90");

        results.OrderBy(r => r).ShouldBe(new[] { matchesGroup1, matchesGroup2 }.OrderBy(r => r));
    }

    [Fact]
    public async Task GivenRelationshipComposite_WhenValuesMatchStaticDefinitionOrder_ThenResolvesCorrectly()
    {
        var matching = await CreateDocumentReferenceRelationshipAsync("docref-1", "doc-abc", "replaces");

        var results = await RunCompositeSearchAsync(CreateRelationshipComposite(), RelationshipParamId, "DocumentReference/doc-abc$replaces");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenRelationshipComposite_WhenValuesAreSwappedRelativeToStaticDefinition_ThenStillResolvesCorrectly()
    {
        // Static definition order is [Reference, Token], but the value at position 0 is Token-shaped
        // and position 1 is Reference-shaped - the DocumentReference "relationship" swap this
        // composite type's IsReferenceExpression/IsTokenExpression sniffing existed to handle.
        // GenerateReferenceTokenGroupQueryAsync must resolve by effective type, not position.
        var matching = await CreateDocumentReferenceRelationshipAsync("docref-1", "doc-abc", "replaces");

        var results = await RunCompositeSearchAsync(CreateRelationshipComposite(), RelationshipParamId, "replaces$DocumentReference/doc-abc");

        results.ShouldBe(new[] { matching });
    }

    [Fact]
    public async Task GivenRelationshipComposite_WhenBothValuesInferTheSameEffectiveType_ThenReturnsEmptyWithoutThrowing()
    {
        // Ambiguous order: both values look Reference-shaped. Deliberate behavior change from
        // today's "assume position order, return plausible-garbage filters" fallback to
        // "warn and return empty results" - see Global Constraints.
        await CreateDocumentReferenceRelationshipAsync("docref-1", "doc-abc", "replaces");

        var results = await RunCompositeSearchAsync(CreateRelationshipComposite(), RelationshipParamId, "DocumentReference/doc-abc$DocumentReference/doc-xyz");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenCompositeWithUnknownType_WhenSearched_ThenFallsBackToNonCompositeWithoutThrowing()
    {
        var singleComponent = new SearchParameterInfo(
            "single-component",
            "single-component",
            SearchParamType.Composite,
            components: [new SearchParameterComponentInfo { ResolvedSearchParameter = new SearchParameterInfo("code", "code", SearchParamType.Token) }]);

        await Should.NotThrowAsync(async () => await RunCompositeSearchAsync(singleComponent, 202, "8462-4"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorCompositeTests"`
Expected: FAIL — today's `ExtractComponentExpressions` doesn't recognize `CompositeComponentExpression`
(it checks `is IFieldExpression`, which this type doesn't implement), so every composite search
returns zero components and hits the `< 2 components` warning path, returning empty results for all
of these tests (including the ones that expect matches).

- [ ] **Step 3: Replace `ExtractComponentExpressions` and rewrite `ProcessCompositeExpressionAsync`**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs`, replace
the entire `ProcessCompositeExpressionAsync` method and the entire `ExtractComponentExpressions` method
(and its private local functions `CollectByComponentIndex`/`CollectComponentIndices`) — currently lines
146-295 — with:

```csharp
    /// <summary>
    /// Processes composite search parameter expressions by routing to the appropriate composite table.
    /// </summary>
    private async Task<IQueryable<long>> ProcessCompositeExpressionAsync(
        short? resourceTypeId,
        short searchParamId,
        SearchParameterInfo searchParameter,
        Expression expr,
        CancellationToken ct)
    {
        _logger.LogDebug("Processing composite search parameter: {Code}", searchParameter.Code);

        // Determine the composite type based on static component definitions - this is a routing
        // decision (which physical SQL table to use), orthogonal to the per-value effective-type
        // correction carried by CompositeComponentExpression below.
        var compositeType = _compositeQueryGenerator.DetermineCompositeType(searchParameter);

        if (compositeType == CompositeType.Unknown)
        {
            _logger.LogWarning(
                "Unknown composite type for parameter {Code}, falling back to non-composite search",
                searchParameter.Code);
            return await ProcessExpressionAsync(resourceTypeId, searchParamId, UnwrapCompositeComponents(expr), ct);
        }

        var groups = ExtractComponentGroups(expr);

        if (groups.Count == 0 || groups.Any(g => g.Count < 2))
        {
            _logger.LogWarning(
                "Composite parameter {Code} requires at least 2 components in every OR group",
                searchParameter.Code);
            return Enumerable.Empty<long>().AsQueryable();
        }

        var groupQueries = new List<IQueryable<long>>(groups.Count);
        foreach (var group in groups)
        {
            groupQueries.Add(await GenerateGroupQueryAsync(resourceTypeId, searchParamId, compositeType, group, ct));
        }

        return groupQueries.Count == 1 ? groupQueries[0] : CombineWithOr(groupQueries);
    }

    /// <summary>
    /// Generates the resource-ID query for a single composite type over a single OR'd value group's
    /// components (already ordered by Position).
    /// </summary>
    private async Task<IQueryable<long>> GenerateGroupQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        CompositeType compositeType,
        List<CompositeComponentExpression> group,
        CancellationToken ct)
    {
        return compositeType switch
        {
            CompositeType.TokenToken => await _compositeQueryGenerator.GenerateTokenTokenQueryAsync(
                resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

            CompositeType.TokenQuantity => await _compositeQueryGenerator.GenerateTokenQuantityQueryAsync(
                resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

            CompositeType.TokenString => await _compositeQueryGenerator.GenerateTokenStringQueryAsync(
                resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

            CompositeType.TokenDateTime => await _compositeQueryGenerator.GenerateTokenDateTimeQueryAsync(
                resourceTypeId, searchParamId, group[0].WrappedExpression, group[1].WrappedExpression, ct),

            CompositeType.ReferenceToken => await GenerateReferenceTokenGroupQueryAsync(resourceTypeId, searchParamId, group, ct),

            _ => Enumerable.Empty<long>().AsQueryable()
        };
    }

    /// <summary>
    /// Resolves the Reference and Token components by effective type rather than position, replacing
    /// IsReferenceExpression/IsTokenExpression sniffing of the built expression shape. When both
    /// components resolve to the same effective type (ambiguous order - e.g. both values look like
    /// references), returns empty results with a warning rather than assuming position order and
    /// returning plausible-but-possibly-wrong filtered results.
    /// </summary>
    private async Task<IQueryable<long>> GenerateReferenceTokenGroupQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        List<CompositeComponentExpression> group,
        CancellationToken ct)
    {
        var referenceComponent = group.FirstOrDefault(c => c.ComponentSearchParameter.Type == SearchParamType.Reference);
        var tokenComponent = group.FirstOrDefault(c => c.ComponentSearchParameter.Type == SearchParamType.Token);

        if (referenceComponent == null || tokenComponent == null)
        {
            _logger.LogWarning(
                "Reference|Token composite SearchParamId={SearchParamId} did not resolve one Reference and one Token component",
                searchParamId);
            return Enumerable.Empty<long>().AsQueryable();
        }

        return await _compositeQueryGenerator.GenerateReferenceTokenQueryAsync(
            resourceTypeId, searchParamId, referenceComponent.WrappedExpression, tokenComponent.WrappedExpression, ct);
    }

    /// <summary>
    /// Splits a composite expression into its OR'd value groups, each containing its
    /// CompositeComponentExpression components ordered by Position. Composite expressions from
    /// SearchParameterExpressionParser are either And(component, component, ...) for a single value
    /// group, or Or(And(...), And(...), ...) for multiple comma-separated value groups.
    /// </summary>
    private static List<List<CompositeComponentExpression>> ExtractComponentGroups(Expression expr) =>
        expr is MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } orExpr
            ? orExpr.Expressions.Select(ExtractSingleGroup).ToList()
            : [ExtractSingleGroup(expr)];

    private static List<CompositeComponentExpression> ExtractSingleGroup(Expression expr) => expr switch
    {
        CompositeComponentExpression cce => [cce],
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } andExpr =>
            andExpr.Expressions.OfType<CompositeComponentExpression>().OrderBy(c => c.Position).ToList(),
        _ => []
    };

    /// <summary>
    /// Strips CompositeComponentExpression wrappers, restoring the tree shape
    /// ProcessExpressionAsync understands - used only by the CompositeType.Unknown fallback, which
    /// predates CompositeComponentExpression and must keep degrading gracefully rather than throwing.
    /// </summary>
    private static Expression UnwrapCompositeComponents(Expression expr) => expr switch
    {
        CompositeComponentExpression cce => UnwrapCompositeComponents(cce.WrappedExpression),
        MultiaryExpression m => new MultiaryExpression(m.MultiaryOperation, m.Expressions.Select(UnwrapCompositeComponents).ToList()),
        NotExpression n => new NotExpression(UnwrapCompositeComponents(n.Expression)),
        _ => expr
    };

    /// <summary>
    /// Combines multiple resource-ID queries with OR semantics. Uses Concat+Distinct instead of
    /// chained Union to avoid deeply nested expression trees - chained Union creates
    /// q0.Union(q1).Union(q2)...Union(qN), which nests deeply and can cause a stack overflow in EF
    /// Core's ExpressionTreeFuncletizer with many queries. Mirrors
    /// SearchExpressionQueryBuilder.CombineWithOr exactly.
    /// </summary>
    private static IQueryable<long> CombineWithOr(List<IQueryable<long>> queries)
    {
        if (queries.Count == 0)
        {
            throw new ArgumentException("Cannot combine zero queries", nameof(queries));
        }

        var result = queries.Aggregate((current, next) => current.Concat(next));
        return result.Distinct();
    }
```

Also delete the now-unused `LogExpressionTree` private method if `ExtractComponentExpressions` was its
only caller (search the file for other callers before deleting — if `LogExpressionTree` is called
elsewhere, leave it).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorCompositeTests"`
Expected: PASS, 6/6.

- [ ] **Step 5: Confirm the untouched composite generator test suite is unaffected**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: PASS, unchanged from before this task (this file was never modified — confirms the blast-radius
boundary held).

Run: `git diff --stat test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`
Expected: no output (zero changes to either file) — `CompositeSearchParameterQueryGenerator.cs`'s own
cleanup (deleting `IsReferenceExpression`/`IsTokenExpression`) is Task 5, not this task.

- [ ] **Step 6: Full build and test run to check for regressions**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test All.sln`
Expected: the composite-related failures introduced by Task 2 (documented in Task 2 Step 5's report)
are now resolved. Compare the list of failing tests against Task 2's report — every test that was
failing solely because of the unresolved composite-shape change should now pass. Remaining failures
should match the Global Constraints' documented pre-existing 5.

- [ ] **Step 7: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/SearchParameterQueryGeneratorCompositeTests.cs
git commit -m "fix(sql): extract composite components by type instead of ComponentIndex heuristics, fix OR-of-groups"
```

---

### Task 5: Delete `IsReferenceExpression`/`IsTokenExpression` from `CompositeSearchParameterQueryGenerator`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`
  (`GenerateReferenceTokenQueryAsync`, `IsReferenceExpression`, `IsTokenExpression`)

**Interfaces:**
- Consumes: nothing new — `GenerateReferenceTokenGroupQueryAsync` (Task 4) now determines
  reference/token order before calling `GenerateReferenceTokenQueryAsync`, making the sniffing inside
  this method dead code.
- Produces: `GenerateReferenceTokenQueryAsync`'s signature and body are otherwise unchanged (still
  takes `component0`/`component1` in the caller-determined order and extracts values from them
  directly) — only the type-detection preamble and the two now-unused private methods are removed.

This task has no new test — it is a dead-code deletion verified by the existing (untouched)
`CompositeSearchParameterQueryGeneratorTests.cs` suite staying green, since that suite already exercises
`GenerateReferenceTokenQueryAsync` directly with hand-ordered `component0`/`component1` and never
depended on the internal sniffing to produce correct results (the caller already passes them in the
right order in every existing test case).

- [ ] **Step 1: Confirm no other callers of the two methods to be deleted**

Run: `grep -rn "IsReferenceExpression\|IsTokenExpression" src/ test/`
Expected: matches only within `CompositeSearchParameterQueryGenerator.cs` itself (the two method
definitions and their two call sites inside `GenerateReferenceTokenQueryAsync`). If any test file
references them directly (rather than through `GenerateReferenceTokenQueryAsync`), stop and report —
that would mean this task needs to update that test instead of a clean deletion.

- [ ] **Step 2: Simplify `GenerateReferenceTokenQueryAsync` and delete the two sniffing methods**

In `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`,
replace:

```csharp
    public async Task<IQueryable<long>> GenerateReferenceTokenQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Reference|Token composite query for SearchParamId={SearchParamId}", searchParamId);

        // Detect actual component types from the expressions to handle FHIR spec inconsistencies
        // (e.g., DocumentReference "relationship" parameter has swapped component definitions)
        var comp0IsReference = IsReferenceExpression(component0);
        var comp0IsToken = IsTokenExpression(component0);
        var comp1IsReference = IsReferenceExpression(component1);
        var comp1IsToken = IsTokenExpression(component1);

        // Determine which component is the reference and which is the token
        Expression referenceExpr;
        Expression tokenExpr;

        if (comp0IsReference && comp1IsToken)
        {
            // Expected order: Reference first, Token second
            referenceExpr = component0;
            tokenExpr = component1;
        }
        else if (comp0IsToken && comp1IsReference)
        {
            // Swapped order: Token first, Reference second (e.g., DocumentReference relationship)
            _logger.LogDebug("Detected swapped component order for SearchParamId={SearchParamId}: Token in position 0, Reference in position 1", searchParamId);
            referenceExpr = component1;
            tokenExpr = component0;
        }
        else
        {
            // Fallback to original assumption if we can't determine types
            _logger.LogWarning("Unable to determine component types for Reference|Token composite SearchParamId={SearchParamId}, using assumed order", searchParamId);
            referenceExpr = component0;
            tokenExpr = component1;
        }

        // Extract reference value
        var reference = ExtractReferenceValue(referenceExpr);
```

with:

```csharp
    public async Task<IQueryable<long>> GenerateReferenceTokenQueryAsync(
        short? resourceTypeId,
        short searchParamId,
        Expression component0,
        Expression component1,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Reference|Token composite query for SearchParamId={SearchParamId}", searchParamId);

        // Caller (SearchParameterQueryGenerator.GenerateReferenceTokenGroupQueryAsync) already
        // resolved component order by effective type before calling this method - component0 is
        // always the reference, component1 is always the token.
        var reference = ExtractReferenceValue(component0);
```

Then replace the remaining reference to `tokenExpr` immediately below (`var token =
ExtractTokenValues(tokenExpr);`) with `var token = ExtractTokenValues(component1);`, and delete the
`IsReferenceExpression` and `IsTokenExpression` method definitions entirely (currently ~lines 396-432).

- [ ] **Step 3: Run the untouched composite generator test suite to confirm no regression**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~CompositeSearchParameterQueryGeneratorTests"`
Expected: PASS, unchanged.

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj --filter "FullyQualifiedName~SearchParameterQueryGeneratorCompositeTests"`
Expected: PASS, unchanged from Task 4 (this confirms the swapped-order and ambiguous-order end-to-end
tests still pass now that the sniffing they used to indirectly exercise is gone).

- [ ] **Step 4: Full build and test run**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

Run: `dotnet test All.sln`
Expected: same as Task 4 Step 6.

- [ ] **Step 5: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs
git commit -m "refactor(sql): delete IsReferenceExpression/IsTokenExpression, superseded by upstream effective-type ordering"
```

---

### Task 6: End-to-end regression pass and final verification

**Files:** none (verification only)

**Interfaces:** none.

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 2: Full solution test run with named failures captured**

Run: `dotnet test All.sln`
Expected: capture the full list of failing test names (not just a count). Confirm the list matches
exactly the pre-existing 5 documented in Global Constraints (same test names as the Phase 0/1 plan's
final task recorded — cross-reference `docs/superpowers/plans/2026-07-11-comparator-semantics-canonicalization.md`'s
final task if needed). Any other failure is a regression introduced by this plan and must be fixed
before proceeding.

- [ ] **Step 3: Verify the blast-radius boundary held across the whole plan**

Run: `git diff --stat main...HEAD -- test/Ignixa.DataLayer.SqlEntityFramework.Tests/Search/CompositeSearchParameterQueryGeneratorTests.cs src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompositeSearchParameterQueryGenerator.cs`
Expected: `CompositeSearchParameterQueryGeneratorTests.cs` shows zero changes. `CompositeSearchParameterQueryGenerator.cs`
shows only Task 5's deletion (no other changes) — confirm by reading the diff, not just the stat line.

- [ ] **Step 4: Verify all 6 composite types have end-to-end coverage**

Confirm `SearchParameterQueryGeneratorCompositeTests.cs` (Task 4) or `CompositeSearchParameterQueryGeneratorTests.cs`
(pre-existing, untouched) together cover all 6 `CompositeType` values (`TokenToken`, `TokenQuantity`,
`TokenString`, `ReferenceToken`, `TokenDateTime`, and `TokenNumberNumber` — the last has no generator
method and correctly falls to the empty-result default in both the old and new switch, per the design
spec's non-goal; confirm this by grep, not by adding a generator for it). List which test class covers
each type in the task report.

- [ ] **Step 5: Update the design spec's status if any deviation occurred**

If any step in Tasks 1-5 deviated from `docs/superpowers/specs/2026-07-11-composite-structure-preservation-design.md`
(e.g. an interface member name changed, a helper was named differently), update the spec to match
reality before finishing — the spec should describe the code as it exists, not as originally drafted.

- [ ] **Step 6: Record the two follow-up items from the design spec's "Out of scope" section**

Add a short note (in this plan file, "Post-Plan" section below, or wherever this repo tracks
cross-plan follow-ups — check `docs/superpowers/plans/2026-07-11-sql-datalayer-cleanup-phase-0-1.md`'s
own Post-Plan section for the established pattern and match it) recording:
1. `CompositeSearchParameterQueryGenerator.ApplyDateTimeFilter`'s last-writer-wins overwrite bug
   (composite DateTime `eq` searches can drop the lower bound) — confirmed real, out of scope for this
   plan, needs a dedicated fix.
2. Leaf-level `ComponentIndex` removal (superseded by `Position`) — deferred, still load-bearing for
   `DateTimeEqualityRewriter.MatchPattern` and expression equality/hashing.

- [ ] **Step 7: Final commit if Step 5 or Step 6 produced changes**

```bash
git add -A
git commit -m "docs: reconcile composite structure preservation spec/plan with final implementation, record follow-ups"
```

If Steps 5-6 produced no file changes, skip this commit — there's nothing to commit.

---

## Post-Plan

(To be filled in during execution: Fable whole-branch review findings and their resolution, final HEAD
commit SHA, and confirmation both follow-up items from Step 6 were recorded in the appropriate
cross-plan tracking location.)
