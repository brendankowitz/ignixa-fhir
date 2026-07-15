# Phase 2 — Semantic IR Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `Ignixa.Search` a typed semantic predicate IR (`SearchParameterPredicateExpression`, reusing the existing `ISearchValue` hierarchy) instead of today's untyped `object Value` on `BinaryExpression`/`StringExpression`, wire it in as the canonical parse output, and migrate `InMemoryIndex`'s `SearchQueryInterpreter` to consume it directly.

**Architecture:** Per `docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md` (read this first — this plan implements it task-by-task and does not re-explain the *why*). One new leaf node (`SearchParameterPredicateExpression`), one adopted wrapper node (`CompositeComponentExpression`, ported unchanged from `origin/worktree-sql-datalayer-architecture`), a new sibling builder (`SearchPredicateExpressionBuilder`) that produces the new tree from the same `ISearchValue` the parser already builds, a new `LegacyExpressionLowerer` that converts back to the old shape (proven correct against PR #332's frozen `Legacy.*` parser as an oracle), and a migration of `SearchQueryInterpreter` (the only in-repo consumer of the old shape) to consume the new tree directly, retiring the lowering step for that one consumer.

**Tech Stack:** No new dependencies. xUnit + Shouldly, matching repo convention.

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- `IExpressionVisitor<TContext,TOutput>` additions (`VisitSearchParameterPredicate`, `VisitCompositeComponent`) must be **default interface methods** throwing `NotSupportedException`, not required abstract methods — this is a binary-breaking-change mitigation (the interface is `public`, `Ignixa.Search` is `IsPackable=true`) already flagged as a risk in the design doc. Any external implementor that hasn't overridden them gets a clear runtime error, not a compile break.
- Do not modify `Ignixa.Search.Expressions.Parsers.Legacy.*` (PR #332's frozen rollback-lever parser) — it is read-only reference/oracle material for this plan's differential test, never a target for changes.
- Do not modify the indexing/write path beyond Task 1's rename — `ElementSearchIndexer` and the `RowGenerators/*` stay otherwise untouched.
- Follow repo convention: file-scoped namespaces, `Nullable=enable`, AAA test structure, `GivenContext_WhenAction_ThenResult` naming, no `#region`, one type per file.
- Sequencing matters and must not be reordered: Task 5 (the lowerer) must land and be proven correct via its differential test *before* Task 6 (the `SearchQueryInterpreter` migration) uses it as an oracle.

---

### Task 1: Rename `CompositeSearchValue` → `CompositeIndexSearchValue`

**Files:**
- Modify: `src/Core/Ignixa.Search/Indexing/SearchValues/CompositeSearchValue.cs` → rename file to `CompositeIndexSearchValue.cs`
- Modify: `src/Core/Ignixa.Search/Indexing/SearchValues/ISearchValueVisitor.cs`
- Modify: `src/Core/Ignixa.Search/Indexing/ElementSearchIndexer.cs` (construction site, `:151`)
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs` (its throwing `Visit` implementation for this type)

**Interfaces:**
- Consumes: nothing new.
- Produces: `CompositeIndexSearchValue` — the new name every later task's references to the old `CompositeSearchValue` type must use instead.

- [ ] **Step 1: Read the current type before renaming**

```bash
git show HEAD:src/Core/Ignixa.Search/Indexing/SearchValues/CompositeSearchValue.cs
```

Confirm its current shape matches what this plan assumes: `IReadOnlyList<IReadOnlyList<ISearchValue>> Components`, `IsValidAsCompositeComponent => false`. If it differs, STOP and report NEEDS_CONTEXT — later tasks depend on this shape being as described.

- [ ] **Step 2: Rename the class and file**

```bash
git mv src/Core/Ignixa.Search/Indexing/SearchValues/CompositeSearchValue.cs src/Core/Ignixa.Search/Indexing/SearchValues/CompositeIndexSearchValue.cs
```

Edit the file: rename the class from `CompositeSearchValue` to `CompositeIndexSearchValue`. Update its XML doc comment to state explicitly (this is the whole point of the rename — verified this session, not previously documented):

```csharp
/// <summary>
/// Represents a composite search-parameter value during indexing. Constructed only by
/// <see cref="ElementSearchIndexer"/> on the write path -- neither the legacy query parser
/// (<see cref="Ignixa.Search.Expressions.Parsers.Legacy.LegacySearchParameterExpressionParser"/>)
/// nor the current query parser (<see cref="Ignixa.Search.Expressions.Parsers.SearchExpressionBinder"/>)
/// ever construct or consume this type. Composite query-side handling decomposes into per-component
/// atomic values before any aggregate value exists -- see
/// docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
```

- [ ] **Step 3: Update every reference**

```bash
grep -rln "CompositeSearchValue" src/ test/
```

For each hit, rename `CompositeSearchValue` to `CompositeIndexSearchValue`. Expected hits: `ISearchValueVisitor.cs` (the `void Visit(CompositeSearchValue x)` method signature), `SearchValueExpressionBuilderHelper.cs` (its `Visit` override, which throws — keep the throw, just rename the parameter type), `ElementSearchIndexer.cs:151` (the construction site), and any existing unit tests referencing the type name directly.

- [ ] **Step 4: Build and run the existing indexing tests**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~Indexing|FullyQualifiedName~ElementSearchIndexer" --nologo
```

**Expected:** 0 warnings, 0 errors, all matching tests still pass — this is a pure rename, no behavior change.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(search): rename CompositeSearchValue to CompositeIndexSearchValue

Confirmed this session: its only construction site is ElementSearchIndexer
(the write/indexing path); neither the legacy nor current query parser ever
builds or consumes one. The old name invited confusion with the query-side
composite handling this design introduces. Not a Legacy.* rename -- this
type isn't frozen/deprecated, just indexing-scoped."
```

---

### Task 2: Add `SearchParameterPredicateExpression` and `CompositeComponentExpression`

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/SearchParameterPredicateExpression.cs`
- Create: `src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/SearchParameterPredicateExpressionTests.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/CompositeComponentExpressionTests.cs`

**Interfaces:**
- Consumes: `SearchParameterInfo` (`Ignixa.Search.Models`), `SearchComparator`/`SearchModifier` (already real, from PR #332), `ISearchValue` (`Ignixa.Search.Indexing.SearchValues`).
- Produces: `SearchParameterPredicateExpression`, `CompositeComponentExpression`, and `IExpressionVisitor<TContext,TOutput>.VisitSearchParameterPredicate`/`VisitCompositeComponent` — every later task in this plan constructs or dispatches on these.

- [ ] **Step 1: Write `SearchParameterPredicateExpression`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents a single typed predicate over one search parameter: the parameter's identity,
/// how the value is compared, and the value itself, typed as the same <see cref="ISearchValue"/>
/// the parser already builds during parsing rather than an untyped <see cref="object"/>.
/// See docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
public sealed class SearchParameterPredicateExpression : Expression
{
    public SearchParameterPredicateExpression(SearchParameterInfo parameter, SearchComparator comparator, SearchModifier? modifier, ISearchValue value)
    {
        EnsureArg.IsNotNull(parameter, nameof(parameter));
        EnsureArg.IsNotNull(value, nameof(value));

        Parameter = parameter;
        Comparator = comparator;
        Modifier = modifier;
        Value = value;
    }

    public SearchParameterInfo Parameter { get; }

    public SearchComparator Comparator { get; }

    public SearchModifier? Modifier { get; }

    public ISearchValue Value { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));

        return visitor.VisitSearchParameterPredicate(this, context);
    }

    public override string ToString()
        => $"(Predicate {Parameter.Code} {Comparator}{(Modifier == null ? null : $":{Modifier}")} {Value})";

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(SearchParameterPredicateExpression));
        hashCode.Add(Parameter);
        hashCode.Add(Comparator);
        hashCode.Add(Modifier);
    }

    public override bool ValueInsensitiveEquals(Expression other)
        => other is SearchParameterPredicateExpression p &&
           p.Parameter.Equals(Parameter) &&
           p.Comparator == Comparator &&
           p.Modifier == Modifier;
}
```

Model this file's structure (usings, header, `EnsureArg` pattern, `AddValueInsensitiveHashCode`/`ValueInsensitiveEquals` pattern) on the existing `src/Core/Ignixa.Search/Expressions/SearchParameterExpression.cs` — match its conventions exactly, don't invent a different style for the new node.

- [ ] **Step 2: Add `CompositeComponentExpression` — ported unchanged from the sibling branch**

```bash
git show origin/worktree-sql-datalayer-architecture:src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs > src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs
```

This file is already correct as-is — verified this session, full content already matches the design doc's adoption decision. Do not modify it beyond what compilation requires (it should compile cleanly once `VisitCompositeComponent` exists on the interface, added in Step 3).

- [ ] **Step 3: Add both visitor methods as default interface methods**

Modify `src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs`. Add, after the existing 15 methods (do not reorder or touch the existing 15):

```csharp
    /// <summary>
    /// Visits a <see cref="SearchParameterPredicateExpression"/>.
    /// </summary>
    /// <param name="expression">The expression to visit.</param>
    /// <param name="context">The input.</param>
    /// <remarks>
    /// Default-implemented to throw for any implementor that hasn't overridden it, since adding a
    /// required method to this public, IsPackable interface would be a binary-breaking change to
    /// external implementors. See docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md, Risks.
    /// </remarks>
    TOutput VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, TContext context)
        => throw new NotSupportedException($"{GetType().Name} does not implement {nameof(VisitSearchParameterPredicate)}.");

    /// <summary>
    /// Visits a <see cref="CompositeComponentExpression"/>.
    /// </summary>
    /// <param name="expression">The expression to visit.</param>
    /// <param name="context">The input.</param>
    /// <remarks>
    /// Same binary-compatibility rationale as <see cref="VisitSearchParameterPredicate"/>.
    /// </remarks>
    TOutput VisitCompositeComponent(CompositeComponentExpression expression, TContext context)
        => throw new NotSupportedException($"{GetType().Name} does not implement {nameof(VisitCompositeComponent)}.");
```

Confirm the interface/file already has `using System;` for `NotSupportedException` (it should, given existing methods likely already reference framework types) — add it if not.

- [ ] **Step 4: Add default (structural pass-through) implementations to `DefaultExpressionVisitor` and `ExpressionRewriter`**

Read `src/Core/Ignixa.Search/Expressions/DefaultExpressionVisitor.cs` and `src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs` in full first — match their existing pattern for a leaf node exactly (look at how they each implement `VisitBinary`, since `SearchParameterPredicateExpression` is a leaf node like `BinaryExpression`, and how they implement `VisitSearchParameter`, since `CompositeComponentExpression` wraps a child `Expression` like `SearchParameterExpression` wraps its `Expression` property — the pattern for "wraps and recurses into one child" should already exist there to copy).

Do not add overrides to `SearchQueryInterpreter` in this task — it has no base class (hand-implements the interface directly per the design doc's finding), so it inherits the interface's own default (throw) until Task 6 deliberately gives it real behavior. Leaving it un-overridden until Task 6 is intentional, not an oversight.

- [ ] **Step 5: Write the failing tests**

`test/Ignixa.Application.Tests/Search/Expressions/SearchParameterPredicateExpressionTests.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Expressions;

public class SearchParameterPredicateExpressionTests
{
    [Fact]
    public void GivenAPredicateExpression_WhenAccepted_ThenDispatchesToVisitSearchParameterPredicate()
    {
        // Arrange
        var parameter = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var value = new StringSearchValue("example");
        var predicate = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, modifier: null, value);
        var visitor = new RecordingVisitor();

        // Act
        var result = predicate.AcceptVisitor(visitor, context: null);

        // Assert
        result.ShouldBe("visited-predicate");
        visitor.LastVisited.ShouldBeSameAs(predicate);
    }

    private sealed class RecordingVisitor : IExpressionVisitor<object?, string>
    {
        public SearchParameterPredicateExpression? LastVisited { get; private set; }

        public string VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
        {
            LastVisited = expression;
            return "visited-predicate";
        }

        // remaining IExpressionVisitor<object?, string> members: implement each to throw
        // NotImplementedException with the method name, following this test file's own minimal-fake
        // convention -- do not rely on the interface's default-throw for members this test doesn't need,
        // since a thin test fake should be explicit about what it doesn't support.
    }
}
```

Check `SearchParameterInfo`'s actual constructor signature (`grep -n "public SearchParameterInfo(" src/Core/Ignixa.Search/Models/SearchParameterInfo.cs`) before finalizing this test — the sketch above may not match its exact required arguments; correct the test to compile against the real constructor rather than guessing further.

`test/Ignixa.Application.Tests/Search/Expressions/CompositeComponentExpressionTests.cs`: mirror the sibling branch's own `test/Ignixa.Application.Tests/Search/CompositeComponentExpressionTests.cs` (`git show origin/worktree-sql-datalayer-architecture:test/Ignixa.Application.Tests/Search/CompositeComponentExpressionTests.cs` to read it) — port it the same way Step 2 ported the production file, adjusting only what compilation requires (e.g. if it wraps a field-level `Expression` in its own tests, that's fine to keep as-is for this structural test; it doesn't need to wrap a `SearchParameterPredicateExpression` specifically until Task 4).

- [ ] **Step 6: Run tests, confirm failure only where expected, then pass**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~SearchParameterPredicateExpressionTests|FullyQualifiedName~CompositeComponentExpressionTests" --nologo
```

**Expected:** 0 warnings, 0 errors, both new test files pass. No other test should have changed behavior — this task adds new dead-code-reachable-only-by-tests, nothing wires it into the live parse path yet (that's Task 4).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search): add SearchParameterPredicateExpression and CompositeComponentExpression

New leaf node carrying a typed ISearchValue (already built by the parser
today, previously discarded into an untyped object) plus SearchComparator/
SearchModifier. CompositeComponentExpression ported unchanged from
worktree-sql-datalayer-architecture. Both visitor methods are default
interface methods (throw NotSupportedException) to avoid a hard binary
break on Ignixa.Search's public, packable IExpressionVisitor.

Not yet wired into any live parse path -- see phase2 plan tasks 3-6."
```

---

### Task 3: Build `SearchPredicateExpressionBuilder`

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchPredicateExpressionBuilder.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchPredicateExpressionBuilderTests.cs`

**Interfaces:**
- Consumes: `ISearchValueVisitor` (`Ignixa.Search.Indexing.SearchValues`, 9 methods — read `src/Core/Ignixa.Search/Indexing/SearchValues/ISearchValueVisitor.cs` in full before starting, and read `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs` in full as the pattern to follow — same interface, same dispatch shape, different terminal construction).
- Produces: `SearchPredicateExpressionBuilder.Build(SearchParameterInfo parameter, SearchModifier? modifier, SearchComparator comparator, ISearchValue value) : SearchParameterPredicateExpression` — Task 4 calls this instead of `SearchValueExpressionBuilderHelper.Build(...)`.

**Before writing code:** read `SearchValueExpressionBuilderHelper.cs` in full. It implements `ISearchValueVisitor`'s 9 `void Visit(<Type> x)` methods by setting a private `_outputExpression` field, then `Build(...)` calls `searchValue.AcceptVisitor(this)` and returns that field. This plan's new builder follows the identical mutate-then-return-field pattern for consistency with the existing codebase — do not invent a cleaner return-based dispatch shape in this task; that's a separate refactor with its own review, out of scope here.

- [ ] **Step 1: Read the existing builder and the `ISearchValueVisitor` interface**

```bash
cat src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs
cat src/Core/Ignixa.Search/Indexing/SearchValues/ISearchValueVisitor.cs
```

Note every `Visit` method's exact signature and what field-level construction the existing helper does for each concrete `ISearchValue` type (e.g. does `StringSearchValue` get different treatment for `:exact` vs `:contains`? Does `TokenSearchValue` handle a `null` system specially?). The new builder's job is much simpler than the old one's per-type logic — it doesn't need to replicate any of that field/collation logic, since it isn't producing SQL-shape decisions, only a typed node — but reading it tells you every concrete type's real name and constructor shape, which the new builder's signature needs.

- [ ] **Step 2: Write `SearchPredicateExpressionBuilder`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Builds a <see cref="SearchParameterPredicateExpression"/> from the same typed
/// <see cref="ISearchValue"/> the parser already constructs during parsing -- the sibling of
/// <see cref="SearchValueExpressionBuilderHelper"/>, which flattens that same typed value into
/// the old untyped field-level tree. See docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
internal sealed class SearchPredicateExpressionBuilder
{
    public SearchParameterPredicateExpression Build(SearchParameterInfo parameter, SearchModifier? modifier, SearchComparator comparator, ISearchValue value)
    {
        EnsureArg.IsNotNull(parameter, nameof(parameter));
        EnsureArg.IsNotNull(value, nameof(value));

        return new SearchParameterPredicateExpression(parameter, comparator, modifier, value);
    }
}
```

**This builder does not need to implement `ISearchValueVisitor` at all** — unlike `SearchValueExpressionBuilderHelper`, which must dispatch per concrete type because it makes per-type field/collation decisions, this builder's job is uniform across every `ISearchValue` type: wrap it, unchanged, in a `SearchParameterPredicateExpression`. If Step 1's reading reveals a reason per-type dispatch is actually needed here (e.g. a type that needs special-casing even at this level), STOP and report NEEDS_CONTEXT rather than silently adding dispatch logic the design doc didn't call for — this simplicity is a deliberate design outcome (see the design doc's *Node granularity* decision), not an oversight to "fix."

- [ ] **Step 3: Write the tests**

`test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchPredicateExpressionBuilderTests.cs`:

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchPredicateExpressionBuilderTests
{
    [Fact]
    public void GivenAStringValue_WhenBuilt_ThenReturnsPredicateCarryingTheSameValue()
    {
        // Arrange
        var parameter = TestSearchParameterInfoFactory.Create("name", SearchParamType.String);
        var value = new StringSearchValue("Smith");
        var builder = new SearchPredicateExpressionBuilder();

        // Act
        var predicate = builder.Build(parameter, modifier: null, SearchComparator.Eq, value);

        // Assert
        predicate.Parameter.ShouldBeSameAs(parameter);
        predicate.Comparator.ShouldBe(SearchComparator.Eq);
        predicate.Modifier.ShouldBeNull();
        predicate.Value.ShouldBeSameAs(value);
    }

    [Fact]
    public void GivenAModifierAndComparator_WhenBuilt_ThenBothArePreservedOnThePredicate()
    {
        // Arrange
        var parameter = TestSearchParameterInfoFactory.Create("birthdate", SearchParamType.Date);
        var value = new DateTimeSearchValue(DateTime.UtcNow, DateTime.UtcNow);
        var builder = new SearchPredicateExpressionBuilder();

        // Act
        var predicate = builder.Build(parameter, SearchModifier.Missing, SearchComparator.Ge, value);

        // Assert
        predicate.Comparator.ShouldBe(SearchComparator.Ge);
        predicate.Modifier.ShouldBe(SearchModifier.Missing);
    }
}
```

`TestSearchParameterInfoFactory` almost certainly does not exist yet under this exact name — search the test project for how existing tests construct a `SearchParameterInfo` for unit tests (`grep -rn "new SearchParameterInfo(" test/Ignixa.Application.Tests/ | head -5`) and use whichever existing helper or direct-construction pattern the codebase already has, rather than inventing a new factory class for this one test file. Adjust both tests above to use the real pattern. Also verify `DateTimeSearchValue`'s actual constructor signature before using it (`grep -n "public DateTimeSearchValue(" src/Core/Ignixa.Search/Indexing/SearchValues/DateTimeSearchValue.cs`) — the two-arg guess above may not match.

- [ ] **Step 4: Run tests**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~SearchPredicateExpressionBuilderTests" --nologo
```

**Expected:** 0 warnings, 0 errors, both tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(search): add SearchPredicateExpressionBuilder

Sibling to SearchValueExpressionBuilderHelper -- same ISearchValue input,
different terminal construction (typed predicate node instead of flattened
field-level Expression). Not yet called from SearchExpressionBinder -- see
phase2 plan task 4."
```

---

### Task 4: Wire `SearchExpressionBinder` to build the new tree as canonical output

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderPredicateTests.cs`

**Interfaces:**
- Consumes: `SearchPredicateExpressionBuilder` (Task 3).
- Produces: `SearchExpressionBinder`'s output trees now contain `SearchParameterPredicateExpression`/`CompositeComponentExpression` instead of `BinaryExpression`/`StringExpression` — every downstream consumer (Task 5's lowerer, Task 6's `SearchQueryInterpreter`) depends on this.

- [ ] **Step 1: Read the exact current binder code before changing it**

```bash
sed -n '180,320p' src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs
```

Confirm `BindComposite` (around line 186) loops components, resolves each component's effective `SearchParameterInfo` (the DocumentReference-ordinal-inference logic), calls `BindAtomic(effective, modifier: null, index, componentSyntax)` per component, then combines the results with `Expression.And(expressions)`. Confirm `BindAtomic` (around line 295) parses an `ISearchValue value = atomicValueParser.Parse(...)`, then its final line constructs the return value via `new SearchValueExpressionBuilderHelper().Build(searchParameter.Code, modifier, syntax.Comparator, componentIndex, value)`. If either method's actual current shape has drifted from this description, STOP and report NEEDS_CONTEXT — this task's remaining steps assume this shape.

- [ ] **Step 2: Change `BindAtomic`'s terminal construction**

Replace the final `return new SearchValueExpressionBuilderHelper().Build(...)` line in `BindAtomic` with:

```csharp
return new SearchPredicateExpressionBuilder().Build(searchParameter, modifier, syntax.Comparator, value);
```

Note the change from `searchParameter.Code` (a string) to `searchParameter` (the full `SearchParameterInfo`) — the new builder needs the full object, not just its code, since `SearchParameterPredicateExpression.Parameter` is typed `SearchParameterInfo`. Also note `componentIndex` is dropped from this call — the new predicate node doesn't carry it; composite positional identity moves to `CompositeComponentExpression.Position` instead (Step 3 below). If `componentIndex` was being used for anything else in `BindAtomic` beyond this `Build` call, do not remove those other usages — only change the terminal construction.

- [ ] **Step 3: Change `BindComposite`'s wrapping**

`BindComposite` currently builds `expressions[i] = BindAtomic(effective, modifier: null, index, componentSyntax)` per component, then `Expression.And(expressions)`. Wrap each result before the `And`:

```csharp
expressions[i] = new CompositeComponentExpression(effective, index, BindAtomic(effective, modifier: null, index, componentSyntax));
```

(Adjust variable names to match the actual loop structure found in Step 1 — this is the shape, not necessarily the exact variable names in the real file.) The `And` call itself stays unchanged; it now ANDs `CompositeComponentExpression` nodes instead of bare predicate nodes.

- [ ] **Step 4: Build and observe what breaks**

```bash
dotnet build All.sln --nologo 2>&1 | grep -E "error|Error"
```

**Expected:** compile errors in whatever consumes `SearchExpressionBinder`'s output expecting the old field-level shape — most likely `SearchQueryInterpreter` (via `VisitBinary`/`VisitString` no longer being reached, which isn't a compile error, but a behavior change) and possibly nowhere at compile time at all, since `Expression` is the common base type either way. If nothing breaks at compile time, that's expected — the break is behavioral (Task 5/6 handle it), not structural. Confirm by running the full suite next.

- [ ] **Step 5: Run the full suite, expect and characterize new failures**

```bash
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo 2>&1 | tail -100
```

**Expected:** failures wherever a test exercises the live search path end-to-end and asserts on the old field-level tree shape, or wherever `SearchQueryInterpreter` is exercised (it will now hit the interface's default `NotSupportedException` for any query touching a leaf predicate, since Task 6 hasn't given it real behavior yet). **Do not fix these failures in this task** — record exactly which tests fail and why in this task's report; Task 6 fixes the `SearchQueryInterpreter` ones, and any that were asserting on the now-obsolete field-level tree shape need updating to assert on the new shape instead (do this now, in this task, since it's this task's own change that obsoletes them — don't leave already-broken assertions for a later task to discover).

- [ ] **Step 6: Update obsoleted golden/characterization tests, add new ones**

For each test found in Step 5 that asserted on the old field-level tree shape for a query that now produces a predicate-node tree, update its assertion to the new shape. Add new characterization tests (following `ExpressionParserCharacterizationTests.cs`'s existing pattern — read it first) asserting, for representative query strings, the exact `SearchParameterPredicateExpression`/`CompositeComponentExpression` tree produced: at minimum, one simple string search (`Patient?name=Smith`), one with a modifier (`Patient?name:exact=Smith`), one composite (`Observation?component-code-value-quantity=...`, matching an existing composite test fixture already in the repo — find one via `grep -rln "component-code-value-quantity" test/`).

- [ ] **Step 7: Run tests, confirm the only remaining failures are `SearchQueryInterpreter`-related (owned by Task 6)**

```bash
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo 2>&1 | tail -100
```

**Expected:** 0 warnings, 0 errors on build. Any remaining test failures should be attributable specifically to `SearchQueryInterpreter` receiving a tree shape it can't yet handle (default-throws) — confirm this by reading each failure's stack trace for `NotSupportedException`/`VisitSearchParameterPredicate`. If a failure is NOT attributable to this, investigate before proceeding — it may be a real regression this task introduced.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(search): SearchExpressionBinder builds the typed predicate tree as its canonical output

BindAtomic now calls SearchPredicateExpressionBuilder instead of
SearchValueExpressionBuilderHelper directly; BindComposite wraps each
component in CompositeComponentExpression before ANDing. This makes
SearchParameterPredicateExpression the primary parse result, not an
alternate tree built alongside the old one.

Known, expected, not-yet-fixed: SearchQueryInterpreter (InMemory) throws
NotSupportedException on any query reaching a leaf predicate, since it
doesn't yet implement VisitSearchParameterPredicate/VisitCompositeComponent.
Fixed in task 5 (LegacyExpressionLowerer) + task 6 (SearchQueryInterpreter
migration) of docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-phase2-semantic-ir.md."
```

---

### Task 5: Build `LegacyExpressionLowerer`, prove it correct against the frozen `Legacy.*` parser

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/LegacyExpressionLowerer.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/LegacyExpressionLowererParityTests.cs`

**Interfaces:**
- Consumes: `SearchParameterPredicateExpression`, `CompositeComponentExpression` (Task 2), `SearchValueExpressionBuilderHelper.Build(string, SearchModifier, SearchComparator, int?, ISearchValue)` (existing, unmodified), `Ignixa.Search.Expressions.Parsers.Legacy.LegacyExpressionParser`/`LegacySearchParameterExpressionParser` (PR #332's frozen rollback-lever parser — read-only oracle for this task's test, per Global Constraints).
- Produces: `LegacyExpressionLowerer` — Task 6's `SearchQueryInterpreter` migration uses this as its pre-migration behavioral oracle (per the design doc's sequencing).

- [ ] **Step 1: Confirm `SearchValueExpressionBuilderHelper.Build`'s exact signature**

```bash
grep -n "public Expression Build" src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs
```

**Expected:** `public Expression Build(string searchParameterName, SearchModifier modifier, SearchComparator comparator, int? componentIndex, ISearchValue searchValue)`. If it differs, STOP and report NEEDS_CONTEXT — Step 3 below depends on this exact shape.

- [ ] **Step 2: Read `IExpressionVisitor`, `DefaultExpressionVisitor`, and `ExpressionRewriter` to decide the lowerer's base**

`LegacyExpressionLowerer` needs to pass every structural node (`And`/`Or`/`Chained`/`Include`/`Compartment`/`Sort`/`Not`/etc.) through **unchanged in shape**, recursing into children, and only do real work at the two new leaf kinds. Check whether `ExpressionRewriter<TContext>` (identity pass-through defaults, per Task 2 Step 4's findings) is a suitable base class to derive from, overriding only the two new methods plus any structural node whose children need re-visiting rather than passing through as-is. Prefer deriving from `ExpressionRewriter<TContext>` if its existing pass-through behavior already does the right thing for every structural node — don't hand-implement all 17 methods if a base class already gives you 15 of them for free.

- [ ] **Step 3: Write `LegacyExpressionLowerer`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Expressions.Parsers;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Converts the typed predicate tree (<see cref="SearchParameterPredicateExpression"/>,
/// <see cref="CompositeComponentExpression"/>) back to the old untyped field-level shape, for
/// consumers that haven't migrated to consume the typed tree directly.
/// See docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md.
/// </summary>
public sealed class LegacyExpressionLowerer : ExpressionRewriter<object?>
{
    public override Expression VisitSearchParameterPredicate(SearchParameterPredicateExpression expression, object? context)
        => new SearchValueExpressionBuilderHelper().Build(expression.Parameter.Code, expression.Modifier, expression.Comparator, componentIndex: null, expression.Value);

    public override Expression VisitCompositeComponent(CompositeComponentExpression expression, object? context)
    {
        // The wrapped expression is expected to be a SearchParameterPredicateExpression (that's the
        // only thing BindComposite ever wraps, per task 4) -- lower it directly with this component's
        // Position, rather than lowering generically and re-stamping, since Build's own componentIndex
        // parameter already exists for exactly this.
        if (expression.WrappedExpression is SearchParameterPredicateExpression predicate)
        {
            return new SearchValueExpressionBuilderHelper().Build(predicate.Parameter.Code, predicate.Modifier, predicate.Comparator, expression.Position, predicate.Value);
        }

        throw new NotSupportedException($"{nameof(LegacyExpressionLowerer)} can only lower a {nameof(CompositeComponentExpression)} whose {nameof(CompositeComponentExpression.WrappedExpression)} is a {nameof(SearchParameterPredicateExpression)}, found {expression.WrappedExpression.GetType().Name}.");
    }
}
```

Verify `ExpressionRewriter<TContext>`'s actual base method signatures match `override` here correctly (its existing methods may not be `virtual`/`override`-compatible with this exact shape — check `ExpressionRewriter.cs` directly, and adjust to match its real base-class contract rather than assuming the sketch above compiles as-is).

- [ ] **Step 4: Confirm the `Legacy.*` parser's exact entry point for the oracle test**

```bash
grep -n "public.*Expression.*Parse\|class LegacyExpressionParser\|class LegacySearchParameterExpressionParser" src/Core/Ignixa.Search/Expressions/Parsers/Legacy/LegacyExpressionParser.cs src/Core/Ignixa.Search/Expressions/Parsers/Legacy/LegacySearchParameterExpressionParser.cs
```

Find the exact method that takes a resource type + raw query string and returns the old-shape `Expression` tree — this is what PR #332's own `SearchParserOldVsNewParityTests.cs` already calls (`grep -n "Legacy" test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserOldVsNewParityTests.cs | head -10` to see the exact invocation pattern already established — reuse it, don't invent a new way to call the legacy parser).

- [ ] **Step 5: Write the parity test**

`test/Ignixa.Application.Tests/Search/Expressions/LegacyExpressionLowererParityTests.cs` — for a representative set of query strings (reuse `SearchParserOldVsNewParityTests.cs`'s existing test-case list where practical, or a meaningful subset), parse each two ways and assert the results are `ValueInsensitiveEquals`:

```csharp
[Theory]
[InlineData("Patient", "name=Smith")]
[InlineData("Patient", "name:exact=Smith")]
[InlineData("Patient", "birthdate=ge2020-01-01")]
[InlineData("Observation", "component-code-value-quantity=http://loinc.org|1234-5$gt10")]
public void GivenAQueryString_WhenParsedBothWays_ThenLoweredNewTreeMatchesLegacyTree(string resourceType, string query)
{
    // Arrange
    var legacyExpression = /* invoke the Legacy.* parser's entry point found in Step 4, same signature its own parity tests use */;
    var newExpression = /* invoke SearchExpressionBinder's entry point for the same input -- find its own public entry point, likely already exercised by SearchParserOldVsNewParityTests.cs */;
    var lowerer = new LegacyExpressionLowerer();

    // Act
    var lowered = newExpression.AcceptVisitor(lowerer, context: null);

    // Assert
    lowered.ValueInsensitiveEquals(legacyExpression).ShouldBeTrue($"Legacy: {legacyExpression}\nLowered: {lowered}");
}
```

Both `/* ... */` placeholders must be replaced with the real invocation code from Step 4's research and from `SearchParserOldVsNewParityTests.cs`'s own existing setup (read that file's `[Theory]` test bodies for the exact pattern — do not guess the entry-point signatures).

- [ ] **Step 6: Run the parity test**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName~LegacyExpressionLowererParityTests" --nologo
```

**Expected:** 0 warnings, 0 errors, all cases pass. If any fails, the failure tells you either `LegacyExpressionLowerer` has a bug or `SearchPredicateExpressionBuilder`/`SearchExpressionBinder`'s wiring from Task 3/4 has one — do not weaken the test to make it pass; fix the actual lowering/building logic.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search): add LegacyExpressionLowerer, proven correct against the frozen Legacy parser

Converts the typed predicate tree back to the old field-level shape by
reusing SearchValueExpressionBuilderHelper unmodified -- no new flattening
logic. Proven correct via a parity test against Ignixa.Search.Expressions.
Parsers.Legacy.* (PR #332's frozen rollback-lever parser), the same oracle
SearchParserOldVsNewParityTests.cs already established as trustworthy.

This is the proof task 6's SearchQueryInterpreter migration depends on."
```

---

### Task 6: Migrate `SearchQueryInterpreter` to consume the typed tree directly

**Files:**
- Modify: `src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.FileSystem/FileSystem/FileBasedSearchService.cs` (three call sites)
- Test: `test/Ignixa.Application.Tests/InMemory/SearchQueryInterpreterPredicateTests.cs` (or wherever existing `SearchQueryInterpreter` tests already live — find them first)

**Interfaces:**
- Consumes: `SearchParameterPredicateExpression`/`CompositeComponentExpression` (Task 2), `LegacyExpressionLowerer` (Task 5, used only as the pre-migration oracle for this task's behavioral test, not shipped as a runtime dependency of the migrated code).
- Produces: `SearchQueryInterpreter` handling the typed tree natively — no later task depends on this, it's the terminal task of this plan.

**Before writing any evaluation logic:** read `src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs` in full — specifically its current `VisitBinary` and `VisitString` implementations (the closest existing analogues to what the new leaf methods need to do: evaluate a resource's actual field value against a predicate). This plan does not pre-write that evaluation logic because it hasn't been read this session — do not guess FHIR search-matching semantics; replicate what `VisitBinary`/`VisitString` already do today, retargeted to read from `predicate.Value`'s concrete `ISearchValue` type instead of an untyped `object` plus `FieldName`.

- [ ] **Step 1: Read the current implementation**

```bash
cat src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs
```

Note: the class name, its context type (`SearchQueryInterpreter.Context`), its output type (`SearchPredicate`), and exactly how `VisitBinary`/`VisitString` currently evaluate a match — what do they read from the resource being tested, and how do they compare it against `BinaryExpression.Value`/`StringExpression.Value`? This logic needs replicating against `ISearchValue`'s concrete types, not reinvented.

- [ ] **Step 2: Find and read the existing test suite for this class**

```bash
grep -rln "SearchQueryInterpreter" test/ | grep -v "\.cs.orig"
```

Read whichever test file(s) already exercise `VisitBinary`/`VisitString` — these are the test cases Step 4 below needs a typed-tree equivalent of, and they tell you what correct behavior looks like today.

- [ ] **Step 3: Implement `VisitSearchParameterPredicate` and `VisitCompositeComponent`**

Write real (non-throwing) overrides on `SearchQueryInterpreter`, following the exact evaluation semantics found in Step 1's `VisitBinary`/`VisitString`, but dispatching on `predicate.Value`'s concrete `ISearchValue` type (a `switch` expression, one arm per concrete type, matching the design doc's *Lower's eventual leaf-rule dispatch* section's shape — though this is in-memory predicate evaluation, not SQL CTE construction, so each arm evaluates against the resource directly rather than building a `CteDefinition`). `VisitCompositeComponent` recurses into `WrappedExpression` via `AcceptVisitor(this, context)` and combines with `Position` the same way `VisitBinary`'s existing `ComponentIndex` handling does today (found in Step 1).

If Step 1's actual current logic is significantly more complex than this sketch anticipates (e.g. it delegates to several helper classes, not a simple inline comparison), STOP and report DONE_WITH_CONCERNS with a description of what you found, rather than forcing a mismatched replication.

- [ ] **Step 4: Write the behavioral equivalence test**

Per the design doc's *Testing* item 5: for a representative set of query strings and a small in-memory resource fixture, run the search **before** this task's changes (via `LegacyExpressionLowerer`-lowered tree fed to the *old* `VisitBinary`/`VisitString` path — i.e., construct the comparison by feeding a `LegacyExpressionLowerer`-lowered version of the same predicate tree through `SearchQueryInterpreter`'s pre-existing structural methods) and **after** (the new tree fed directly to the new `VisitSearchParameterPredicate`/`VisitCompositeComponent` overrides), asserting identical `SearchPredicate` results for the same resource data:

```csharp
[Theory]
[InlineData(/* representative cases, reusing Step 2's existing test fixtures where practical */)]
public void GivenAQuery_WhenEvaluatedDirectlyOrViaLoweredLegacyTree_ThenResultsMatch(/* params */)
{
    // Arrange: build both the new-shape tree (via SearchExpressionBinder) and its
    // LegacyExpressionLowerer-lowered equivalent, over the same resource fixture.

    // Act: evaluate both through SearchQueryInterpreter.

    // Assert: results are equal.
}
```

Fill in the real setup using Step 2's existing test fixtures and construction patterns — do not invent a new resource-fixture format for this one test file.

- [ ] **Step 5: Update `FileBasedSearchService.cs`'s three call sites — remove the lowering step, it's no longer needed for this consumer**

At each of the three call sites (`options.Expression.AcceptVisitor(_searchQueryInterpreter, default)`), confirm no change is actually needed — `options.Expression` is already the new-shape tree since Task 4, and `SearchQueryInterpreter` now handles it directly per Step 3. **No `LegacyExpressionLowerer` call belongs in this file at all** — that class exists for future cross-repo consumers (per the design doc, primarily anticipating `fhir-server`'s eventual Cosmos adoption at step 10), not for `FileBasedSearchService`. If Step 1-3's investigation reveals `FileBasedSearchService` needs a different change than "nothing," report exactly what and why rather than silently doing something else.

- [ ] **Step 6: Run the full suite**

```bash
dotnet build All.sln --nologo
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests" --nologo
```

**Expected:** 0 warnings, 0 errors, everything green — including every test that failed in Task 4 Step 5/7 specifically because `SearchQueryInterpreter` threw `NotSupportedException`. Confirm those specific previously-failing tests now pass, not just that the aggregate count is green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(search): migrate SearchQueryInterpreter to consume the typed predicate tree directly

VisitSearchParameterPredicate/VisitCompositeComponent now evaluate
predicates against the resource directly, dispatching on ISearchValue's
concrete type -- no more untyped-object coercion in InMemory's own
evaluation path. FileBasedSearchService's three call sites need no change:
the tree Task 4 produces is already what this consumer now expects
natively. LegacyExpressionLowerer keeps zero in-repo callers after this
commit -- retained as forward-looking infrastructure for fhir-server's
eventual Cosmos adoption (design doc step 10), not dead code.

Closes phase 2 of docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md."
```

## Self-Review

- **Spec coverage:** every decision in `docs/superpowers/specs/2026-07-15-search-semantic-ir-design.md` maps to a task — node shape (Task 2), value reuse + `CompositeIndexSearchValue` rename (Task 1), composite adoption (Task 2/4), construction (Task 3/4), legacy lowering (Task 5), and the in-scope `InMemoryIndex` migration (Task 6), sequenced exactly as the design doc's *In scope* section specifies (lowerer proven first, then migration).
- **Placeholder scan:** a few steps (Task 2 Step 5's `SearchParameterInfo` constructor args, Task 3 Step 3's `DateTimeSearchValue` constructor, Task 5 Step 5's parser entry points, Task 6 Steps 3-4's evaluation logic) are deliberately marked "verify/read before finalizing" rather than pre-written as fact, because their exact current shape wasn't read this session and guessing would risk writing incorrect code — this is the same honest-deferral pattern used successfully in the Step 0 plan's data-seeding task, not an unscoped placeholder. Every one of them names the exact command to run to resolve the unknown before writing the real code.
- **Type consistency:** `SearchParameterPredicateExpression(SearchParameterInfo, SearchComparator, SearchModifier?, ISearchValue)`, `CompositeComponentExpression(SearchParameterInfo, int, Expression)`, and `SearchPredicateExpressionBuilder.Build(SearchParameterInfo, SearchModifier?, SearchComparator, ISearchValue)` are used identically across Tasks 2-6 — checked for drift, none found.
