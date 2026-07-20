# Search Trace Provenance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an end-to-end provenance chain — source text span → typed IR node → plan CTE → emitted SQL text range — plus per-parameter outcomes, so a developer playground can trace and debug FHIR searches.

**Architecture:** Spans are always populated on the types we own (the internal `Syntax` records and the two typed-IR classes) and never participate in identity or rendering. Plan provenance rides *alongside* `QueryPlan` (records would pollute equality) and stores IR **node references**, not bare spans. SQL text ranges are produced by the same assembly that produces the SQL, via a section-scoped writer. A single orchestration entry point (`SearchCompiler.CompileAsync`) serves both tracing and future production wiring.

**Tech Stack:** C# (.NET 9 + .NET 10 multi-target), xUnit, Shouldly, `Ignixa.Search`, `Ignixa.Search.Sql`.

**Spec:** `docs/superpowers/specs/2026-07-20-search-trace-provenance-design.md`

## Global Constraints

- Target frameworks: `net9.0;net10.0`. Run tests with `-f net10.0`.
- `TreatWarningsAsErrors` is on repo-wide. Zero warnings, zero errors.
- **Nullability differs by project — check before adding a file:**
  - `Ignixa.Search` is `<Nullable>disable</Nullable>` at the project level, and every file opts in with an
    explicit `#nullable enable` pragma above the namespace. **Every new file added to this project must
    include that pragma**, matching its siblings.
  - `Ignixa.Search.Sql` is `<Nullable>enable</Nullable>` project-wide; no pragma needed there.
  - Never add `#nullable disable` to a new file.
- **One type per file.**
- File-scoped namespaces; System usings first, outside the namespace.
- Test naming: `GivenContext_WhenAction_ThenResult`. AAA pattern. Shouldly assertions.
- Do NOT add inline comments except to explain a non-obvious *why*.
- **Spans never participate in identity or rendering.** `ToString()`, `ValueInsensitiveEquals`, `AddValueInsensitiveHashCode`, and record `Equals` must all ignore spans.
- **The traced path and the production path are the same path.** No `RunTraced` twins, no parallel pipelines.
- `SearchParserOldVsNewParityTests` compares `Expression.ToString()` — it must stay green throughout.
- `EmitTests` goldens are exact-match on SQL text and `@pN` ordinals — they must stay green and act as the parameter-order guard.
- `Ignixa.Search.Sql` is alpha with no production call sites, so signature changes there are permitted.

## File Structure

**Created**
- `src/Core/Ignixa.Search/Expressions/SourceSpan.cs` — public span struct + `SourceOrigin` (public because the public IR types expose it).
- `src/Core/Ignixa.Search/Expressions/Parsers/SyntaxNode.cs` — public projected syntax DTO.
- `src/Core/Ignixa.Search/Expressions/Parsers/ParseResult.cs` — public parse result.
- `src/Core/Ignixa.Search/Expressions/Parsers/SyntaxProjector.cs` — internal: `Syntax` → `SyntaxNode`.
- `src/Core/Ignixa.Search/Parsing/ParameterOutcome.cs` + `ParameterTrace.cs` — trace records for the parse side.
- `src/Core/Ignixa.Search.Sql/Symbols/ResolvedSymbols.cs`
- `src/Core/Ignixa.Search.Sql/Lowering/LoweredPlan.cs` + `PlanProvenance.cs` + `CteOrigin.cs`
- `src/Core/Ignixa.Search.Sql/Builders/SqlTextWriter.cs` + `SqlTextRange.cs` + `EmitOptions.cs`
- `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs` + `SearchTrace.cs` + `QueryPlanTrace.cs` + `EmittedSqlTrace.cs` + `CteProvenance.cs` + `TraceStage.cs`

**Modified**
- All 12 `Expressions/Parsers/Syntax/*.cs` — add `Span`, hand-written equality.
- `SearchKeySyntaxParser.cs`, `SearchValueSyntaxParser.cs` — populate spans.
- `SearchParameterPredicateExpression.cs`, `CompositeComponentExpression.cs` — add `Span`.
- `SearchPredicateExpressionBuilder.cs`, `SearchExpressionBinder.cs` — thread spans.
- `IExpressionParser.cs`, `ExpressionParser.cs`, `ISearchParameterExpressionParser.cs`, `SearchParameterExpressionParser.cs` — `ParseWithSyntax`.
- `SearchOptionsBuilder.cs` — outcome-collector overload.
- `Symbols/Resolve.cs`, `Lowering/Lower.cs`, `Lowering/StructuralContext.cs`, `Builders/SqlBuilder.cs`, `Ast/EmittedSql.cs`.

---

### Task 1: `SourceSpan` and spans on the `Syntax` records

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/SourceSpan.cs`
- Modify: all 12 files in `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SourceSpanTests.cs`

**Interfaces:**
- Produces: `public readonly record struct SourceSpan(SourceOrigin Origin, int Start, int Length)`; `public enum SourceOrigin { Key, Value }`. Every `SearchValueSyntax`/`SearchKeySyntax` gains `public SourceSpan Span { get; init; }`.

**Critical convention:** an `AtomicValueSyntax` span covers the **whole token including any comparator prefix**. `ParseAtomic` already receives `(source, start, length)` for the whole token and only slices past the prefix when building `RawText`, so the span is exactly `new SourceSpan(SourceOrigin.Value, start, length)`. Do **not** use the post-prefix offsets.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SourceSpanTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SourceSpanTests
{
    [Fact]
    public void GivenAScalarValue_WhenScanned_ThenTheSpanExtractsTheWholeToken()
    {
        const string source = "Smith";

        var syntax = (AtomicValueSyntax)SearchValueSyntaxParser.Parse(
            SearchParamType.String, modifier: null, source);

        source.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("Smith");
        syntax.Span.Origin.ShouldBe(SourceOrigin.Value);
    }

    [Fact]
    public void GivenAComparatorPrefixedValue_WhenScanned_ThenTheSpanIncludesThePrefix()
    {
        const string source = "gt2000";

        var syntax = (AtomicValueSyntax)SearchValueSyntaxParser.Parse(
            SearchParamType.Date, modifier: null, source);

        syntax.RawText.ShouldBe("2000");
        source.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("gt2000");
    }

    [Fact]
    public void GivenCommaAlternatives_WhenScanned_ThenEachItemSpanExtractsItsOwnText()
    {
        const string source = "alpha,beta";

        var syntax = (AlternativesValueSyntax)SearchValueSyntaxParser.Parse(
            SearchParamType.String, modifier: null, source);

        source.Substring(syntax.Items[0].Span.Start, syntax.Items[0].Span.Length).ShouldBe("alpha");
        source.Substring(syntax.Items[1].Span.Start, syntax.Items[1].Span.Length).ShouldBe("beta");
    }

    [Fact]
    public void GivenTwoValuesDifferingOnlyBySpan_WhenCompared_ThenTheyAreEqual()
    {
        var a = new AtomicValueSyntax("x", SearchComparator.Eq) { Span = new SourceSpan(SourceOrigin.Value, 0, 1) };
        var b = new AtomicValueSyntax("x", SearchComparator.Eq) { Span = new SourceSpan(SourceOrigin.Value, 5, 1) };

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void GivenAModifiedKey_WhenScanned_ThenTheSpanCoversTheWholeKey()
    {
        const string key = "name:exact";

        var syntax = SearchKeySyntaxParser.ParseParameter(key);

        key.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("name:exact");
        syntax.Span.Origin.ShouldBe(SourceOrigin.Key);
    }

    [Fact]
    public void GivenAForwardChainKey_WhenScanned_ThenTheChainSpanCoversTheWholeKey()
    {
        const string key = "general-practitioner.name";

        var syntax = (ForwardChainKeySyntax)SearchKeySyntaxParser.ParseParameter(key);

        key.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("general-practitioner.name");
        key.Substring(syntax.Next.Span.Start, syntax.Next.Span.Length).ShouldBe("name");
    }

    [Fact]
    public void GivenAReverseChainKey_WhenScanned_ThenTheChainSpanCoversTheWholeKey()
    {
        const string key = "_has:Observation:patient:code";

        var syntax = (ReverseChainKeySyntax)SearchKeySyntaxParser.ParseParameter(key);

        key.Substring(syntax.Span.Start, syntax.Span.Length).ShouldBe("_has:Observation:patient:code");
        key.Substring(syntax.Next.Span.Start, syntax.Next.Span.Length).ShouldBe("code");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SourceSpanTests"`
Expected: FAIL — compile error, `SourceSpan` and `Span` do not exist.

- [ ] **Step 3: Create `SourceSpan`**

Create `src/Core/Ignixa.Search/Expressions/SourceSpan.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Which of a search parameter's two source strings a <see cref="SourceSpan"/> indexes into.
/// </summary>
public enum SourceOrigin
{
    Key,
    Value,
}

/// <summary>
/// A range within one search parameter's key or value string. Offsets are relative to that string —
/// the enclosing parameter's ordinal supplies which parameter instance it belongs to.
/// </summary>
public readonly record struct SourceSpan(SourceOrigin Origin, int Start, int Length);
```

- [ ] **Step 4: Add `Span` to every syntax record**

Add `{ get; init; }` span to all 12 records. Abstract bases carry the property so children inherit it. Example — `Syntax/SearchValueSyntax.cs`:

```csharp
/// <summary>The scanned structure of a search value (the right side of a search parameter), before it is typed.</summary>
internal abstract record SearchValueSyntax
{
    public SourceSpan Span { get; init; }
}
```

Same shape for `Syntax/SearchKeySyntax.cs` (with its own `Span` property). The 10 concrete records keep their positional parameters unchanged and inherit `Span`.

Add `using Ignixa.Search.Expressions;` to each file that needs it.

- [ ] **Step 5: Exclude `Span` from equality on the concrete records**

Record equality compares *all* fields including inherited ones, so `Span` must be excluded explicitly. Add to each concrete record — example for `Syntax/AtomicValueSyntax.cs`:

```csharp
/// <summary>A single scanned value with its comparator prefix separated out (e.g. <c>gt2000</c> → text <c>2000</c>, comparator <c>gt</c>).</summary>
internal sealed record AtomicValueSyntax(
    string RawText,
    SearchComparator Comparator) : SearchValueSyntax
{
    public bool Equals(AtomicValueSyntax? other)
        => other is not null && RawText == other.RawText && Comparator == other.Comparator;

    public override int GetHashCode() => HashCode.Combine(RawText, Comparator);
}
```

Apply the same pattern to the other 9 concrete records, comparing exactly their positional members and nothing else.

**Caution:** `AlternativesValueSyntax` and `CompositeValueSyntax` hold `ImmutableArray<T>`. Preserve the existing struct-equality semantics — compare the arrays with `==` as the synthesized code did; do **not** switch to element-wise `SequenceEqual`, which would silently change what existing tests assert.

- [ ] **Step 6: Populate spans in `SearchValueSyntaxParser`**

In `ParseAtomic`, attach the whole-token span (both return sites):

```csharp
private static AtomicValueSyntax ParseAtomic(
    string source,
    int start,
    int length,
    bool supportsComparator)
{
    if (length == 0)
    {
        throw SyntaxError(source, start, "nonempty value");
    }

    var span = new SourceSpan(SourceOrigin.Value, start, length);

    if (supportsComparator)
    {
        ReadOnlySpan<char> value = source.AsSpan(start, length);

        foreach ((string literal, SearchComparator comparator) in SearchComparators)
        {
            if (value.StartsWith(literal.AsSpan(), StringComparison.Ordinal))
            {
                return new AtomicValueSyntax(
                    Slice(source, start + literal.Length, length - literal.Length),
                    comparator) { Span = span };
            }
        }
    }

    return new AtomicValueSyntax(Slice(source, start, length), SearchComparator.Eq) { Span = span };
}
```

Attach spans at every other construction site in the file using the offsets already in scope:
- `ParseMissing`, `ParseText` → `new SourceSpan(SourceOrigin.Value, 0, source.Length)`.
- `ParseScalar`/`ParseOfType`/`ParseComposite` alternatives wrappers → `new SourceSpan(SourceOrigin.Value, 0, source.Length)`.
- `ParseOfTypeItem` → `new SourceSpan(SourceOrigin.Value, start, length)`.
- `ParseCompositeItem` → `new SourceSpan(SourceOrigin.Value, start, length)`.

- [ ] **Step 7: Populate spans in `SearchKeySyntaxParser`**

Use `SourceOrigin.Key` and the `Cursor`'s existing `_offset`. The capture pattern, applied to every
`Parse*` method that constructs a `SearchKeySyntax`:

1. At the **head** of the method, capture the start: `var start = _offset;`
2. Construct the node **after** any recursion completes, so the end offset is final.
3. Set `Span = new SourceSpan(SourceOrigin.Key, start, _offset - start)`.

Two subtleties that will produce wrong spans if missed:

- **`TryParseReverse` uses lookahead.** It advances a local `lookaheadOffset` and only commits `_offset`
  on success. Capture the start **before** consuming `_has:` — i.e. the offset as it stood on entry, not
  `_offset` at construction time, which by then has moved past the whole chain head.
- **Chain nodes are constructed after recursing** into `Next`. That makes the *end* offset naturally
  correct, but the *start* must have been captured before the recursion began. Do not read `_offset` for
  the start at construction time.

The three key-span tests added in Step 1 cover exactly these cases: a plain modified key, a forward
chain, and a reverse chain.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SourceSpanTests"`
Expected: PASS — 7 tests (4 value-origin, 3 key-origin).

- [ ] **Step 9: Verify existing parser tests are unaffected**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~Search.Expressions"`
Expected: PASS — all existing tests, including `SearchValueSyntaxParserTests` (value-compares syntax records) and `SearchParserOldVsNewParityTests`.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/SourceSpan.cs \
        src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ \
        src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs \
        src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs \
        test/Ignixa.Application.Tests/Search/Expressions/Parsers/SourceSpanTests.cs
git commit -m "feat(search): add SourceSpan and populate spans on scanned syntax nodes"
```

---

### Task 2: Spans on the typed IR

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/SearchParameterPredicateExpression.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchPredicateExpressionBuilder.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Test: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/IrSpanTests.cs`

**Interfaces:**
- Consumes: `SourceSpan` (Task 1).
- Produces: `SearchParameterPredicateExpression.Span` and `CompositeComponentExpression.Span`, both `SourceSpan?`. `SearchPredicateExpressionBuilder.Build(SearchParameterInfo parameter, SearchModifier? modifier, SearchComparator comparator, ISearchValue value, SourceSpan? span)`.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.Application.Tests/Search/Expressions/Parsers/IrSpanTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class IrSpanTests
{
    private static readonly string[] Patient = ["Patient"];

    [Fact]
    public void GivenAParsedPredicate_WhenInspected_ThenItsSpanExtractsTheValueText()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        const string value = "Smith";

        var parsed = (SearchParameterExpression)context.Parser.Parse(Patient, "name", value);
        var predicate = (SearchParameterPredicateExpression)parsed.Expression;

        predicate.Span.ShouldNotBeNull();
        value.Substring(predicate.Span!.Value.Start, predicate.Span.Value.Length).ShouldBe("Smith");
    }

    [Fact]
    public void GivenTwoPredicatesDifferingOnlyBySpan_WhenComparedValueInsensitively_ThenTheyMatch()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://x/name"));
        var a = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, null, new StringSearchValue("s"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 1),
        };
        var b = new SearchParameterPredicateExpression(parameter, SearchComparator.Eq, null, new StringSearchValue("s"))
        {
            Span = new SourceSpan(SourceOrigin.Value, 7, 1),
        };

        a.ValueInsensitiveEquals(b).ShouldBeTrue();
        a.ToString().ShouldBe(b.ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~IrSpanTests"`
Expected: FAIL — compile error, `Span` does not exist on the IR types.

- [ ] **Step 3: Add `Span` to the two IR types**

In `SearchParameterPredicateExpression.cs`, add the property. Do **not** touch `ToString`, `ValueInsensitiveEquals`, or `AddValueInsensitiveHashCode`:

```csharp
    public ISearchValue Value { get; }

    /// <summary>Where this predicate came from in the parameter's source text. Never part of identity or rendering.</summary>
    public SourceSpan? Span { get; init; }
```

Add the identical property to `CompositeComponentExpression.cs`.

- [ ] **Step 4: Thread the span through the builder**

In `SearchPredicateExpressionBuilder.cs`. The new parameter **must** have a `= null` default:
`SearchPredicateExpressionBuilderTests.cs:25,44` call the four-argument form and would otherwise fail to
compile.

```csharp
    public SearchParameterPredicateExpression Build(
        SearchParameterInfo parameter,
        SearchModifier? modifier,
        SearchComparator comparator,
        ISearchValue value,
        SourceSpan? span = null)
    {
        EnsureArg.IsNotNull(parameter, nameof(parameter));
        EnsureArg.IsNotNull(value, nameof(value));

        return new SearchParameterPredicateExpression(parameter, comparator, modifier, value) { Span = span };
    }
```

- [ ] **Step 5: Pass the syntax span from the binder**

In `SearchExpressionBinder.BindAtomic`, pass `syntax.Span` (the whole-token span from Task 1) to the builder:

```csharp
        return new SearchPredicateExpressionBuilder().Build(
            searchParameter, modifier, syntax.Comparator, value, syntax.Span);
```

In `NormalizeCompositeComparator`, when fabricating a replacement `AtomicValueSyntax`, **copy the original's span verbatim** — the prefix-inclusive convention makes it valid for the re-concatenated text:

```csharp
        return new AtomicValueSyntax(rebuiltText, comparator) { Span = original.Span };
```

Where `BindComposite` constructs `CompositeComponentExpression`, set `Span = componentSyntax.Span`.

- [ ] **Step 6: Preserve the span when `ExpressionRewriter` rebuilds a composite component**

`src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs:111` reconstructs `CompositeComponentExpression`
when a rewriter visits one. Without this, any rewriter pass silently drops provenance:

```csharp
        return ReferenceEquals(rewrittenExpression, expression.WrappedExpression)
            ? expression
            : new CompositeComponentExpression(
                expression.ComponentSearchParameter,
                expression.Position,
                rewrittenExpression) { Span = expression.Span };
```

Match the existing constructor argument order in that file rather than the illustrative order above.

- [ ] **Step 7: Add a rewriter round-trip test**

Append to `IrSpanTests.cs`:

```csharp
    [Fact]
    public void GivenACompositeComponent_WhenRebuiltByARewriter_ThenTheSpanSurvives()
    {
        var parameter = new SearchParameterInfo("code", "code", SearchParamType.Token, new Uri("http://x/code"));
        var inner = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, null, new TokenSearchValue(null, "abc", null))
        {
            Span = new SourceSpan(SourceOrigin.Value, 2, 3),
        };
        var component = new CompositeComponentExpression(parameter, 0, inner)
        {
            Span = new SourceSpan(SourceOrigin.Value, 0, 6),
        };

        var rewritten = (CompositeComponentExpression)component.AcceptVisitor(
            new ReplacingRewriter(), context: null);

        ReferenceEquals(rewritten, component).ShouldBeFalse();
        rewritten.Span.ShouldBe(component.Span);
    }

    /// <summary>Returns a fresh inner instance so the rebuild path is actually taken.</summary>
    private sealed class ReplacingRewriter : ExpressionRewriter<object?>
    {
        public override Expression VisitSearchParameterPredicate(
            SearchParameterPredicateExpression expression, object? context)
            => new SearchParameterPredicateExpression(
                expression.Parameter, expression.Comparator, expression.Modifier, expression.Value)
            {
                Span = expression.Span,
            };
    }
```

**Why the rewriter must return a new instance:** `ExpressionRewriter.VisitCompositeComponent` short-circuits
with `ReferenceEquals` when the child is unchanged (lines 108–111), returning the *original* component. An
identity rewriter therefore never reaches the rebuild line, and the assertion would pass even if Step 6's
span copy were omitted entirely. The `ShouldBeFalse()` on reference equality pins that the rebuild path ran.

Adjust the `CompositeComponentExpression` constructor arguments to match its real signature.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~IrSpanTests"`
Expected: PASS — 3 tests.

- [ ] **Step 9: Verify parity tests still pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SearchParser"`
Expected: PASS. If `SearchParserOldVsNewParityTests` fails, a span has leaked into `ToString()` — fix that rather than updating the test.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/SearchParameterPredicateExpression.cs \
        src/Core/Ignixa.Search/Expressions/CompositeComponentExpression.cs \
        src/Core/Ignixa.Search/Expressions/Parsers/SearchPredicateExpressionBuilder.cs \
        src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs \
        test/Ignixa.Application.Tests/Search/Expressions/Parsers/IrSpanTests.cs
git commit -m "feat(search): carry source spans on the typed predicate IR"
```

---

### Task 3: Projected syntax tree and `ParseWithSyntax`

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SyntaxNode.cs`, `ParseResult.cs`, `SyntaxProjector.cs`
- Modify: `IExpressionParser.cs`, `ExpressionParser.cs`, `ISearchParameterExpressionParser.cs`, `SearchParameterExpressionParser.cs`
- Modify: `Parsers/Legacy/LegacyExpressionParser.cs`, `Parsers/Legacy/LegacySearchParameterExpressionParser.cs` — **these implement the widened interfaces and will not compile otherwise**
- Test: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SyntaxProjectionTests.cs`

**Interfaces:**
- Produces: `public sealed record SyntaxNode(string Kind, SourceSpan Span, IReadOnlyList<SyntaxNode> Children)`; `public sealed record ParseResult(Expression Expression, SyntaxNode KeySyntax, SyntaxNode? ValueSyntax)`; `IExpressionParser.ParseWithSyntax(string[] resourceTypes, string key, string value)`.

**Critical:** `Parse` must NOT delegate to `ParseWithSyntax` — that would allocate a projection on every production parse. Both share the scan→bind core; projection is a tail only `ParseWithSyntax` runs.

- [ ] **Step 1: Write the failing test**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SyntaxProjectionTests
{
    private static readonly string[] Patient = ["Patient"];

    [Fact]
    public void GivenAlternatives_WhenParsedWithSyntax_ThenEachChildSpanExtractsItsText()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        const string value = "alpha,beta";

        var result = context.Parser.ParseWithSyntax(Patient, "name", value);

        result.ValueSyntax.ShouldNotBeNull();
        result.ValueSyntax!.Kind.ShouldBe("Alternatives");
        result.ValueSyntax.Children.Count.ShouldBe(2);
        value.Substring(result.ValueSyntax.Children[0].Span.Start, result.ValueSyntax.Children[0].Span.Length)
            .ShouldBe("alpha");
        value.Substring(result.ValueSyntax.Children[1].Span.Start, result.ValueSyntax.Children[1].Span.Length)
            .ShouldBe("beta");
    }

    [Fact]
    public void GivenAnOrdinaryParameter_WhenParsedWithSyntax_ThenTheExpressionMatchesPlainParse()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        var plain = context.Parser.Parse(Patient, "name", "Smith");
        var withSyntax = context.Parser.ParseWithSyntax(Patient, "name", "Smith");

        withSyntax.Expression.ToString().ShouldBe(plain.ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SyntaxProjectionTests"`
Expected: FAIL — `ParseWithSyntax` does not exist.

- [ ] **Step 3: Create the DTOs**

`SyntaxNode.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// A public, serializable projection of one scanned syntax node. The scanner's own types stay internal;
/// this is the shape a trace or tooling consumes. Ancestry is resolved by span containment, ties by depth.
/// </summary>
public sealed record SyntaxNode(string Kind, SourceSpan Span, IReadOnlyList<SyntaxNode> Children);
```

`ParseResult.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// A parsed search parameter plus its projected syntax. <see cref="ValueSyntax"/> is null for shapes with
/// no value tree, such as <c>_not-referenced</c> and <c>_include</c>/<c>_revinclude</c>.
/// </summary>
public sealed record ParseResult(Expression Expression, SyntaxNode KeySyntax, SyntaxNode? ValueSyntax);
```

- [ ] **Step 4: Create the projector**

`SyntaxProjector.cs` — maps each internal record to a `Kind` string and recurses:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers.Syntax;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>Projects the internal scanned syntax into the public <see cref="SyntaxNode"/> shape.</summary>
internal static class SyntaxProjector
{
    public static SyntaxNode Project(SearchValueSyntax syntax) => syntax switch
    {
        AtomicValueSyntax a => new SyntaxNode("Atomic", a.Span, []),
        MissingValueSyntax m => new SyntaxNode("Missing", m.Span, []),
        OfTypeValueSyntax o => new SyntaxNode("OfType", o.Span, []),
        AlternativesValueSyntax alt => new SyntaxNode(
            "Alternatives", alt.Span, alt.Items.Select(Project).ToList()),
        CompositeValueSyntax c => new SyntaxNode(
            "Composite", c.Span, c.Components.Select(component => Project(component)).ToList()),
        _ => throw new NotSupportedException($"No syntax projection for {syntax.GetType().Name}."),
    };

    public static SyntaxNode Project(SearchKeySyntax syntax) => syntax switch
    {
        ParameterKeySyntax p => new SyntaxNode("ParameterKey", p.Span, []),
        ForwardChainKeySyntax f => new SyntaxNode("ForwardChain", f.Span, [Project(f.Next)]),
        ReverseChainKeySyntax r => new SyntaxNode("ReverseChain", r.Span, [Project(r.Next)]),
        IncludeKeySyntax i => new SyntaxNode("IncludeKey", i.Span, []),
        NotReferencedKeySyntax n => new SyntaxNode("NotReferencedKey", n.Span, []),
        _ => throw new NotSupportedException($"No syntax projection for {syntax.GetType().Name}."),
    };
}
```

- [ ] **Step 5: Widen the interfaces and add `ParseWithSyntax`**

In `ISearchParameterExpressionParser.cs`, add a syntax-returning overload beside the existing `Parse`:

```csharp
    Expression Parse(SearchParameterInfo searchParameter, SearchModifier modifier, string value);

    (Expression Expression, SyntaxNode ValueSyntax) ParseWithSyntax(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value);
```

Implement it in `SearchParameterExpressionParser` by having both methods call one private core that returns the internal syntax plus the expression; `Parse` discards the syntax without projecting, `ParseWithSyntax` projects it.

In `IExpressionParser.cs` add `ParseResult ParseWithSyntax(string[] resourceTypes, string key, string value);` and implement it in `ExpressionParser` alongside the existing `Parse`, projecting the key syntax it already scans and passing `null` for `ValueSyntax` on the `_not-referenced` branch.

- [ ] **Step 6: Keep the frozen legacy oracle parsers compiling**

`LegacyExpressionParser` (`Legacy/LegacyExpressionParser.cs:38`) implements `IExpressionParser`, and
`LegacySearchParameterExpressionParser` (`Legacy/LegacySearchParameterExpressionParser.cs:24`) implements
`ISearchParameterExpressionParser`. Both break the moment Step 5 widens those interfaces.

These are the **frozen parity oracle** — they must keep behaving exactly as they do today. Do **not**
implement real syntax projection in them. Add one throwing member to each:

```csharp
    public ParseResult ParseWithSyntax(string[] resourceTypes, string key, string value)
        => throw new NotSupportedException(
            "The frozen legacy oracle parser does not produce syntax projections.");
```

and the matching member on `LegacySearchParameterExpressionParser`:

```csharp
    public (Expression Expression, SyntaxNode ValueSyntax) ParseWithSyntax(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
        => throw new NotSupportedException(
            "The frozen legacy oracle parser does not produce syntax projections.");
```

The oracle is only ever driven through `Parse` by the parity tests, so these are unreachable in practice.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SyntaxProjectionTests"`
Expected: PASS — 2 tests.

- [ ] **Step 8: Verify the parity oracle still runs**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SearchParserOldVsNewParityTests"`
Expected: PASS — the oracle is exercised through `Parse`, never `ParseWithSyntax`.

- [ ] **Step 9: Commit**

```bash
git add src/Core/Ignixa.Search/Expressions/Parsers/ \
        test/Ignixa.Application.Tests/Search/Expressions/Parsers/SyntaxProjectionTests.cs
git commit -m "feat(search): project scanned syntax into a public SyntaxNode tree via ParseWithSyntax"
```

---

### Task 4: `Resolve` surfaces unresolved symbols

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Symbols/ResolvedSymbols.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolvedSymbolsTests.cs`

**Interfaces:**
- Produces: `public sealed record ResolvedSymbols(SymbolTable Symbols, IReadOnlyList<SearchParameterInfo> Unresolved)`. `Resolve.RunAsync` returns `ResolvedSymbols` instead of `SymbolTable`.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Sql.Tests.Symbols;

public class ResolvedSymbolsTests
{
    private sealed class NullResolver : ISymbolResolver
    {
        public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
            => Task.FromResult<short?>(null);

        public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
            => Task.FromResult<short?>(103);
    }

    [Fact]
    public async Task GivenAnUnresolvableParameter_WhenResolved_ThenItIsReportedAsUnresolved()
    {
        var parameter = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://x/name"));
        var predicate = new SearchParameterPredicateExpression(
            parameter, SearchComparator.Eq, null, new StringSearchValue("Smith"));

        var resolved = await Resolve.RunAsync(
            predicate, includes: [], revIncludes: [], sort: [], new NullResolver(), "Patient", CancellationToken.None);

        resolved.Unresolved.ShouldContain(p => p.Code == "name");
    }
}
```

Add the usings the file needs (`Ignixa.Search.Expressions`, `Ignixa.Search.Indexing.SearchValues`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ResolvedSymbolsTests"`
Expected: FAIL — `RunAsync` returns `SymbolTable`, which has no `Unresolved`.

- [ ] **Step 3: Create `ResolvedSymbols`**

```csharp
using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// The resolved symbol table plus the parameters the resolver could not find. Unresolved parameters are
/// reported rather than silently dropped, so callers can explain the failure instead of hitting a
/// KeyNotFoundException later in lowering.
/// </summary>
public sealed record ResolvedSymbols(SymbolTable Symbols, IReadOnlyList<SearchParameterInfo> Unresolved);
```

- [ ] **Step 4: Change `Resolve.RunAsync` to collect and return misses**

In `Resolve.cs`, change the return type to `Task<ResolvedSymbols>` and collect misses in the existing loop:

```csharp
        var searchParamIds = new Dictionary<string, short>();
        var unresolved = new List<SearchParameterInfo>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);
            if (id.HasValue)
            {
                searchParamIds[parameter.Url.ToString()] = id.Value;
            }
            else
            {
                unresolved.Add(parameter);
            }
        }
```

and return `new ResolvedSymbols(new SymbolTable(searchParamIds, resourceTypeIds, compartmentMembership), unresolved);`.

- [ ] **Step 5: Update existing call sites**

Update every `Resolve.RunAsync` caller in `test/Ignixa.Search.Sql.Tests/` and `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompiledSearchEndToEndTests.cs` to use `.Symbols`.

- [ ] **Step 6: Run the full Search.Sql suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0`
Expected: PASS — all tests including the new one.

- [ ] **Step 7: Build the whole solution**

This task edits `CompiledSearchEndToEndTests.cs` in a project the test command above does **not** compile.
Verify nothing else references the old return type:

Run: `dotnet build All.sln`
Expected: 0 errors. (`Ignixa.DataLayer.LegacySqlEF.Tests` has pre-existing failures unrelated to this
work — ignore those specific files, but no new errors may appear.)

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Symbols/ test/Ignixa.Search.Sql.Tests/ \
        test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/
git commit -m "feat(search-sql): report unresolved search parameters from Resolve"
```

---

### Task 5: Plan provenance (`LoweredPlan`)

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/LoweredPlan.cs`, `PlanProvenance.cs`, `CteOrigin.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, `StructuralContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/PlanProvenanceTests.cs`

**Interfaces:**
- Produces: `public sealed record CteOrigin(int CteIndex, Expression SourceNode)`; `public sealed record PlanProvenance(IReadOnlyList<CteOrigin> Origins)`; `public sealed record LoweredPlan(QueryPlan Plan, PlanProvenance Provenance)`. `Lower.Run` returns `LoweredPlan`.

**Critical:** at the `:not` site in `LowerSearchParameter`, record provenance against the **original** `predicate`, not the clone. The clone sets `modifier: null` while the original carries `:not`, so any equality-based fallback is provably dead.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Lowering;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class PlanProvenanceTests
{
    [Fact]
    public void GivenALeafPredicate_WhenLowered_ThenItsCteTracesBackToThatNodeByReference()
    {
        // Build the same predicate + symbols the existing LowerTests fixtures use.
        var (expression, symbols) = LowerTestFixtures.SingleStringPredicate();

        var lowered = Lower.Run(
            expression, symbols, targetResourceType: "Patient",
            includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        var origin = lowered.Provenance.Origins.ShouldHaveSingleItem();
        ReferenceEquals(origin.SourceNode, expression).ShouldBeTrue();
    }

    [Fact]
    public void GivenANotModifiedPredicate_WhenLowered_ThenProvenanceIsTheOriginalNotTheClone()
    {
        var (wrapper, inner, symbols) = LowerTestFixtures.NotModifiedPredicate();

        var lowered = Lower.Run(
            wrapper, symbols, targetResourceType: "Patient",
            includes: [], revIncludes: [], includeLimit: 0,
            sort: [], sortPhase: SortPhase.Valued, page: null);

        lowered.Provenance.Origins.ShouldContain(o => ReferenceEquals(o.SourceNode, inner));
    }
}
```

Create `LowerTestFixtures` in the same folder exposing the two builders, mirroring the arrangement already used by `LowerTests` (a `SymbolTable` with the parameter's URL mapped to a `SearchParamId` and `"Patient"` mapped to a `ResourceTypeId`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0 --filter "FullyQualifiedName~PlanProvenanceTests"`
Expected: FAIL — `Lower.Run` returns `QueryPlan`, which has no `Provenance`.

- [ ] **Step 3: Create the provenance records**

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Sql.Ast;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>Links one CTE to the IR node that produced it. Holds the node, not a span: the plan is
/// per-search, so a bare span would be ambiguous across repeated parameters.</summary>
public sealed record CteOrigin(int CteIndex, Expression SourceNode);

/// <summary>CTE-to-IR links for a lowered plan. Partial by construction — see the design spec.</summary>
public sealed record PlanProvenance(IReadOnlyList<CteOrigin> Origins);

/// <summary>A lowered plan and its provenance. Provenance rides alongside the plan, never inside it,
/// because QueryPlan and its nodes are records where an added field would land in generated equality.</summary>
public sealed record LoweredPlan(QueryPlan Plan, PlanProvenance Provenance);
```

- [ ] **Step 4: Record origins in `StructuralContext`**

Add a provenance list and an explicit-provenance overload:

```csharp
    private readonly List<CteOrigin> _origins = [];

    public IReadOnlyList<CteOrigin> Origins => _origins;

    public CteRef Lower(SearchParameterPredicateExpression predicate, string resourceType)
        => Lower(predicate, resourceType, provenanceNode: predicate);

    public CteRef Lower(SearchParameterPredicateExpression predicate, string resourceType, Expression provenanceNode)
    {
        RejectResourceColumnCode(predicate.Parameter.Code);
        var resourceTypeId = _leafContext.ResourceTypeId(resourceType);
        var cte = LeafLoweringDispatcher.Lower(predicate, _leafContext, resourceTypeId);
        _ctes.Add(cte);
        var index = _ctes.Count - 1;
        _origins.Add(new CteOrigin(index, provenanceNode));
        return new CteRef(index);
    }
```

Do the same in `LowerComposite`, recording the composite's `SearchParameterExpression` wrapper.

- [ ] **Step 5: Pass the original node at the `:not` clone site**

In `Lower.LowerSearchParameter`:

```csharp
        if (sp.Expression is SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } predicate)
        {
            var positiveMatch = new SearchParameterPredicateExpression(
                predicate.Parameter, predicate.Comparator, modifier: null, predicate.Value) { Span = predicate.Span };
            return context.LowerNot(context.Lower(positiveMatch, resourceType, provenanceNode: predicate), resourceType);
        }
```

- [ ] **Step 6: Change `Lower.Run` to return `LoweredPlan`**

Replace the final return:

```csharp
        return new LoweredPlan(
            new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages, sortSpec, page, countOnly),
            new PlanProvenance(context.Origins));
```

and change the method's return type to `LoweredPlan`.

- [ ] **Step 7: Update existing call sites**

Update `LowerTests`, `EndToEndCompilationTests`, and `CompiledSearchEndToEndTests` to use `.Plan`.

- [ ] **Step 8: Run the full Search.Sql suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0`
Expected: PASS — all tests, including both new provenance tests.

- [ ] **Step 9: Build the whole solution**

This task edits `CompiledSearchEndToEndTests.cs` in a project the test command above does **not** compile.

Run: `dotnet build All.sln`
Expected: 0 errors. (`Ignixa.DataLayer.LegacySqlEF.Tests` has pre-existing failures unrelated to this
work — ignore those specific files, but no new errors may appear.)

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/ test/Ignixa.Search.Sql.Tests/ \
        test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/
git commit -m "feat(search-sql): return plan provenance linking CTEs to their IR nodes"
```

---

### Task 6: SQL text ranges via a section-scoped writer

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Builders/SqlTextWriter.cs`, `SqlTextRange.cs`, `EmitOptions.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`, `src/Core/Ignixa.Search.Sql/Builders/EmittedSql.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Builders/SqlTextRangeTests.cs`

**Interfaces:**
- Produces: `public sealed record SqlTextRange(string Label, int Start, int Length)`; `public sealed record EmitOptions(bool IncludeTextRanges)`; `SqlBuilder.Run(QueryPlan plan, EmitOptions? options = null)`; `EmittedSql` gains `IReadOnlyList<SqlTextRange>? TextRanges`.

**Critical:** do not reorder any `Emit*` **call**. `@pN` ordinals are assigned as content is built, so reordering silently changes parameter numbering. Replace only the concatenation of results.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Builders;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Tests.Builders;

public class SqlTextRangeTests
{
    private static QueryPlan LeafPlan()
    {
        var table = SqlCatalog.Default.Table("StringSearchParam");
        var predicate = new Predicate.Equal(
            new SqlColumnRef(table.TableName, "Text"), new SqlParameterRef("Smith"));
        return new QueryPlan([new CteDefinition.ParamSource(table, 103, 202, predicate)], new CteRef(0), Top: 10);
    }

    [Fact]
    public void GivenTracingEnabled_WhenEmitted_ThenEachRangeExtractsTheSectionItClaims()
    {
        var emitted = SqlBuilder.Run(LeafPlan(), new EmitOptions(IncludeTextRanges: true));

        emitted.TextRanges.ShouldNotBeNull();
        foreach (var range in emitted.TextRanges!)
        {
            var text = emitted.Sql.Substring(range.Start, range.Length);
            text.ShouldNotBeNullOrWhiteSpace();
        }

        var cte0 = emitted.TextRanges!.First(r => r.Label == "cte0");
        emitted.Sql.Substring(cte0.Start, cte0.Length).ShouldContain("StringSearchParam");
    }

    [Fact]
    public void GivenTracingDisabled_WhenEmitted_ThenSqlAndParametersAreByteIdentical()
    {
        var traced = SqlBuilder.Run(LeafPlan(), new EmitOptions(IncludeTextRanges: true));
        var plain = SqlBuilder.Run(LeafPlan());

        plain.Sql.ShouldBe(traced.Sql);
        plain.Parameters.Select(p => p.Name).ShouldBe(traced.Parameters.Select(p => p.Name));
        plain.TextRanges.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SqlTextRangeTests"`
Expected: FAIL — `EmitOptions` does not exist.

- [ ] **Step 3: Create `SqlTextRange` and `EmitOptions`**

```csharp
namespace Ignixa.Search.Sql.Builders;

/// <summary>A labelled range of emitted SQL text — which characters a plan section produced.</summary>
public sealed record SqlTextRange(string Label, int Start, int Length);
```

```csharp
namespace Ignixa.Search.Sql.Builders;

/// <summary>Emission options. Text ranges are opt-in because they allocate per call.</summary>
public sealed record EmitOptions(bool IncludeTextRanges);
```

- [ ] **Step 4: Create the writer**

```csharp
using System.Text;

namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// Builds the emitted SQL while recording where each section landed. The current offset is simply the
/// buffer length, so ranges come from the same assembly that produces the text — there is no second copy
/// of the concatenation arithmetic to drift. Sections nest; recording is skipped entirely when not asked for.
/// </summary>
internal sealed class SqlTextWriter(bool recordRanges)
{
    private readonly StringBuilder _buffer = new();
    private readonly List<SqlTextRange>? _ranges = recordRanges ? [] : null;

    public IReadOnlyList<SqlTextRange>? Ranges => _ranges;

    public void Append(string text) => _buffer.Append(text);

    public void AppendJoin(string separator, IReadOnlyList<string> values, string labelPrefix)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                _buffer.Append(separator);
            }

            using (Section($"{labelPrefix}{i}"))
            {
                _buffer.Append(values[i]);
            }
        }
    }

    public SectionScope Section(string label) => new(this, label, _buffer.Length);

    public override string ToString() => _buffer.ToString();

    private void Close(string label, int start)
        => _ranges?.Add(new SqlTextRange(label, start, _buffer.Length - start));

    internal readonly struct SectionScope(SqlTextWriter writer, string label, int start) : IDisposable
    {
        public void Dispose() => writer.Close(label, start);
    }
}
```

- [ ] **Step 5: Add `TextRanges` to `EmittedSql`**

```csharp
public sealed record EmittedSql(
    string Sql,
    IReadOnlyList<EmittedSqlParameter> Parameters,
    IReadOnlyList<SqlTextRange>? TextRanges = null);
```

- [ ] **Step 6: Rewrite `SqlBuilder.Run`'s final assembly**

Change the signature to `Run(QueryPlan plan, EmitOptions? options = null)`, create `var writer = new SqlTextWriter(options?.IncludeTextRanges ?? false);`, and replace each `string.Join`/interpolated assembly with sequential `writer.Append` calls wrapped in `writer.Section(...)` scopes. Keep every `Emit*` call in its existing order. Example for the plain (no-includes) path:

```csharp
        writer.Append(";WITH ");
        writer.AppendJoin(",\n", cteBlocks, labelPrefix: "cte");
        writer.Append("\n");

        writer.Append($"SELECT {top}m.T1, m.Sid1{sortColumns} FROM cte{plan.Match.Index} m{sortJoins}{resourceJoin}");

        if (whereClauses.Count > 0)
        {
            writer.Append("\nWHERE ");
            using (writer.Section("where"))
            {
                writer.Append(string.Join(" AND ", whereClauses));
            }
        }

        using (writer.Section("orderBy"))
        {
            writer.Append(orderBy);
        }

        return new EmittedSql(writer.ToString(), parameters, writer.Ranges);
```

**The label map is fixed — do not auto-number the whole `cteBlocks` list on the includes path.** By the
time the includes path assembles, `cteBlocks` has `cteMatchPage`, `inc{i}`, and `inc{i}lim` blocks
*appended after* the plan CTEs, so a blanket `AppendJoin(..., labelPrefix: "cte")` would mislabel them
`cte7`, `cte8`, … Required labels:

| Section | Label |
|---|---|
| plan CTE at index *i* | `cte{i}` |
| match-page CTE (includes path) | `cteMatchPage` |
| include stage *i* | `inc{i}` |
| include stage *i* limit wrapper | `inc{i}lim` |
| outer WHERE | `where` |
| keyset seek predicate | `seek` |
| final ORDER BY | `orderBy` |

So on the includes path, `AppendJoin` covers only the first `plan.Ctes.Count` elements; the appended tail
blocks are emitted with individual `writer.Section("cteMatchPage")` / `Section($"inc{i}")` /
`Section($"inc{i}lim")` scopes.

Apply the same decomposition to the `CountOnly` and includes paths, adding `Section` scopes for each CTE,
`cteMatchPage`, each `incN`/`incNlim`, the seek predicate, and the final ORDER BY.

- [ ] **Step 7: Add an includes-path label assertion**

`EmitTests` guards the SQL bytes but nothing guards the *labels*. Append to `SqlTextRangeTests`:

```csharp
    [Fact]
    public void GivenAnIncludesPlan_WhenEmitted_ThenTailBlocksAreLabelledNotAutoNumbered()
    {
        var plan = IncludesPlan();

        var emitted = SqlBuilder.Run(plan, new EmitOptions(IncludeTextRanges: true));

        var labels = emitted.TextRanges!.Select(r => r.Label).ToList();
        labels.ShouldContain("cteMatchPage");
        labels.ShouldContain("inc0");
        labels.ShouldContain("inc0lim");
        labels.ShouldNotContain(label => label.StartsWith("cte", StringComparison.Ordinal)
            && label != "cteMatchPage"
            && int.TryParse(label.AsSpan(3), out var i)
            && i >= plan.Ctes.Count);
    }
```

Build `IncludesPlan()` by copying the includes-path plan construction from the existing `EmitTests`
flagship include case, so the shapes stay in step.

- [ ] **Step 8: Run the new tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SqlTextRangeTests"`
Expected: PASS — 3 tests.

- [ ] **Step 9: Verify the goldens are byte-identical**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0`
Expected: PASS — all 255+ tests. `EmitTests` failing means the assembly or an `Emit*` call order changed; fix the assembly rather than updating a golden.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Builders/ test/Ignixa.Search.Sql.Tests/Builders/
git commit -m "feat(search-sql): record SQL section text ranges via a section-scoped writer"
```

---

### Task 7: `SearchOptionsBuilder` outcome collector

**Files:**
- Create: `src/Core/Ignixa.Search/Parsing/TraceStage.cs`, `ParameterOutcome.cs`, `ParameterTrace.cs`
- Modify: `src/Core/Ignixa.Search/Parsing/SearchOptionsBuilder.cs`
- Modify: `src/Core/Ignixa.Search/Parsing/ISearchOptionsBuilder.cs` — the overload goes on the **interface** too
- Test: `test/Ignixa.Application.Tests/Search/Parsing/ParameterOutcomeTests.cs`

**The collector overload must be added to `ISearchOptionsBuilder`, not just the concrete class.**
`ISearchOptionsBuilderFactory.Create(...)` returns `ISearchOptionsBuilder`, and DI registers only the
interface — so Task 8's `CompileAsync` receives the interface and could not reach a concrete-only method
without a downcast. `SearchOptionsBuilder` is the sole implementer (no fakes, no decorators), so widening
the interface breaks nothing.

**Three decisions pinned here so Task 8's implementer does not have to re-derive them:**

1. **`ParameterTrace.Syntax` comes from `ParseWithSyntax`, and only when tracing.** Call
   `ParseWithSyntax` when `outcomes is not null`, plain `Parse` otherwise. Calling it unconditionally
   would allocate a projection on every production search — the cost rule this design is built on.
2. **Only `ParameterCategory.Search` parameters get trace entries**, and `Ordinal` counts only those.
   `_count`, `_sort`, `_include`, and formatting parameters produce no `ParameterTrace` in v1.
3. **`Ignored.Span` is always `null` in v1.** Do not attempt to recover offsets from
   `InvalidSearchOperationException` messages.

**Interfaces:**
- Produces: `ParameterOutcome` (abstract, with `Compiled`/`Ignored`/`Failed`), `ParameterTrace`, and a `Build` overload accepting `IList<ParameterTrace>? outcomes`.

**Critical:** the assembler must never own the parameter loop — `Ignored` is observable only inside `SearchOptionsBuilder`'s existing per-parameter catch.

- [ ] **Step 1: Write the failing test**

```csharp
#nullable enable

using Ignixa.Search.Parsing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Parsing;

public class ParameterOutcomeTests
{
    [Fact]
    public void GivenAnUnsupportedModifier_WhenBuilt_ThenTheParameterIsReportedAsIgnored()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("birthdate", SearchParamType.Date));
        var outcomes = new List<ParameterTrace>();

        harness.Build([("birthdate:exact", "2000-01-01")], outcomes);

        var trace = outcomes.ShouldHaveSingleItem();
        trace.Key.ShouldBe("birthdate:exact");
        trace.Outcome.ShouldBeOfType<ParameterOutcome.Ignored>();
    }

    [Fact]
    public void GivenAValidParameter_WhenBuilt_ThenItIsReportedAsCompiled()
    {
        var harness = SearchOptionsBuilderHarness.ForPatient(("name", SearchParamType.String));
        var outcomes = new List<ParameterTrace>();

        harness.Build([("name", "Smith")], outcomes);

        outcomes.ShouldHaveSingleItem().Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
    }
}
```

Create `SearchOptionsBuilderHarness` in the same folder, wiring a real `SearchOptionsBuilder` over `SearchParserTestContext`'s parser and definition manager.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ParameterOutcomeTests"`
Expected: FAIL — `ParameterOutcome` does not exist.

- [ ] **Step 3: Create the outcome records**

First `TraceStage.cs` — it lives in `Ignixa.Search` (not `Ignixa.Search.Sql`) so the outcome can be
strongly typed. `Search.Sql` references `Search`, so it can use this enum; the reverse would be a layer
violation:

```csharp
namespace Ignixa.Search.Parsing;

/// <summary>Which compilation stage produced an outcome.</summary>
public enum TraceStage
{
    Parse,
    Resolve,
    Lower,
    Emit,
}
```

Then `ParameterOutcome.cs`:

```csharp
using Ignixa.Search.Expressions;

namespace Ignixa.Search.Parsing;

/// <summary>What happened to one search parameter during parsing.</summary>
public abstract record ParameterOutcome
{
    /// <summary>The parameter parsed and contributed to the search expression.</summary>
    public sealed record Compiled : ParameterOutcome;

    /// <summary>The parameter was dropped by FHIR lenient handling rather than failing the request.</summary>
    public sealed record Ignored(string Reason, SourceSpan? Span) : ParameterOutcome;

    /// <summary>The parameter failed at a named stage.</summary>
    public sealed record Failed(TraceStage Stage, string Message, SourceSpan? Span) : ParameterOutcome;
}
```

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;

namespace Ignixa.Search.Parsing;

/// <summary>One parameter's trace: its position, source text, projected syntax, IR, and outcome.</summary>
public sealed record ParameterTrace(
    int Ordinal,
    string Key,
    string Value,
    SyntaxNode? Syntax,
    Expression? Ir,
    ParameterOutcome Outcome);
```

- [ ] **Step 4: Add the collector overload to `SearchOptionsBuilder`**

Add an optional `IList<ParameterTrace>? outcomes` parameter to `Build`. Inside the existing per-parameter loop, on the `ParameterCategory.Search` branch record `Compiled` with the parsed expression; in the two existing catch blocks record `Ignored` with the exception message as the reason. Do not restructure the loop.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0 --filter "FullyQualifiedName~ParameterOutcomeTests"`
Expected: PASS — 2 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search/Parsing/ test/Ignixa.Application.Tests/Search/Parsing/
git commit -m "feat(search): report per-parameter outcomes from SearchOptionsBuilder"
```

---

### Task 8: `SearchCompiler.CompileAsync` and `SearchTrace`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Tracing/SearchCompiler.cs`, `SearchTrace.cs`, `QueryPlanTrace.cs`, `EmittedSqlTrace.cs`, `CteProvenance.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Tracing/SearchTraceTests.cs`

**Interfaces:**
- Consumes: `ResolvedSymbols` (Task 4), `LoweredPlan` (Task 5), `EmitOptions`/`SqlTextRange` (Task 6), `ParameterTrace`/`ParameterOutcome`/`TraceStage` (Task 7 — `TraceStage` lives in `Ignixa.Search.Parsing`; do **not** redefine it here).
- Produces — the exact entry point, which both tracing and future production wiring must use:

```csharp
public static async Task<SearchTrace> CompileAsync(
    string resourceType,
    IReadOnlyList<QueryParameter> parameters,
    ISearchOptionsBuilder optionsBuilder,
    ISymbolResolver resolver,
    CancellationToken cancellationToken,
    ICompartmentDefinitionManager? compartmentDefinitionManager = null,
    ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
```

It takes **`ISearchOptionsBuilder`**, the interface. `ISearchOptionsBuilderFactory.Create(...)` returns the
interface and DI registers only the interface, so a concrete-typed parameter would force
`(SearchOptionsBuilder)factory.Create(...)` into the first real caller — and the spec requires production
wiring to consume `CompileAsync`, which a downcast would make brittle the day the factory returns a
decorator. Task 7 therefore puts the collector overload on the interface as well.

The parameter order mirrors `Resolve.RunAsync`, which likewise places `cancellationToken` before its two
optional definition managers — consistency with the sibling API this sits directly on top of.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Tracing;

namespace Ignixa.Search.Sql.Tests.Tracing;

public class SearchTraceTests
{
    [Fact]
    public async Task GivenALeafSearch_WhenTraced_ThenTheChainReachesFromSpanToSqlRange()
    {
        var trace = await SearchTraceFixtures.TracePatientNameSmithAsync();

        var parameter = trace.Parameters.ShouldHaveSingleItem();
        parameter.Outcome.ShouldBeOfType<ParameterOutcome.Compiled>();
        parameter.Ir.ShouldNotBeNull();

        trace.Plan.ShouldNotBeNull();
        trace.Plan!.Ctes.ShouldContain(c => c.ParameterOrdinal == parameter.Ordinal);

        trace.Sql.ShouldNotBeNull();
        trace.Sql!.Ranges.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenAnUnregisteredParameter_WhenTraced_ThenItIsReportedAtTheResolveStage()
    {
        var trace = await SearchTraceFixtures.TraceUnregisteredParameterAsync();

        var failed = trace.Parameters
            .Select(p => p.Outcome)
            .OfType<ParameterOutcome.Failed>()
            .ShouldHaveSingleItem();

        failed.Stage.ShouldBe(TraceStage.Resolve);
    }
}
```

Create `SearchTraceFixtures` in the same folder building both scenarios through `SearchCompiler.CompileAsync`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SearchTraceTests"`
Expected: FAIL — `SearchCompiler` does not exist.

- [ ] **Step 3: Create the trace records**

```csharp
using Ignixa.Search.Expressions;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>One CTE's link back to the parameter that produced it. Null ordinal where exempt —
/// :missing, compartment, and structural CTEs have no source text.</summary>
public sealed record CteProvenance(int CteIndex, int? ParameterOrdinal, SourceSpan? Span);
```

```csharp
namespace Ignixa.Search.Sql.Tracing;

/// <summary>The explained plan plus each CTE's provenance.</summary>
public sealed record QueryPlanTrace(string Explain, IReadOnlyList<CteProvenance> Ctes);
```

```csharp
using Ignixa.Search.Sql.Builders;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>The emitted SQL plus its section ranges.</summary>
public sealed record EmittedSqlTrace(string Sql, IReadOnlyList<SqlTextRange> Ranges);
```

```csharp
using Ignixa.Search.Parsing;

namespace Ignixa.Search.Sql.Tracing;

/// <summary>A full pipeline trace: per-parameter outcomes plus the plan and SQL they produced.</summary>
public sealed record SearchTrace(
    string ResourceType,
    IReadOnlyList<ParameterTrace> Parameters,
    QueryPlanTrace? Plan,
    EmittedSqlTrace? Sql);
```

- [ ] **Step 4: Attach spans to leaf/composite failures at the dispatch choke points**

Leaf and composite rules throw `NotSupportedException` without a node reference. Enrich at the two
dispatchers, which already hold the predicate — one place each, rather than a dozen throw sites. In
`src/Core/Ignixa.Search.Sql/Lowering/Leaf/LeafLoweringDispatcher.cs`, wrap the dispatch:

```csharp
    public static CteDefinition.ParamSource Lower(
        SearchParameterPredicateExpression predicate, LeafContext context, short resourceTypeId)
    {
        try
        {
            return LowerCore(predicate, context, resourceTypeId);
        }
        catch (NotSupportedException ex) when (predicate.Span is { } span && !ex.Data.Contains(SpanDataKey))
        {
            ex.Data[SpanDataKey] = span;
            throw;
        }
    }

    internal const string SpanDataKey = "Ignixa.SourceSpan";
```

Rename the existing body to `LowerCore`. Apply the same wrapper in
`Lowering/Composite/CompositeLoweringDispatcher.cs` — that dispatcher holds a *components list*, not a
single predicate, so use `components[0].Span` **after** its existing `OrderBy(c => c.Position)` ordering.

Enriching `Exception.Data` and rethrowing preserves the exception type, so production callers see exactly
what they see today — only the trace assembler reads the key.

- [ ] **Step 5: Implement `SearchCompiler.CompileAsync`**

Create `SearchCompiler.cs`. It must:
1. Call `SearchOptionsBuilder.Build` with an outcome collector (never re-implement the loop).
2. Call `Resolve.RunAsync`, and for every entry in `ResolvedSymbols.Unresolved` replace that parameter's outcome with `Failed(TraceStage.Resolve, …, span)`. **Match rule:** walk each `ParameterTrace.Ir` for a `SearchParameterPredicateExpression` whose `Parameter` is that same `SearchParameterInfo` instance. Repeated parameters sharing one definition instance will *both* be marked `Failed` — that is correct, not a bug.
3. Call `Lower.Run`, then map each `CteOrigin.SourceNode` to its owning `ParameterTrace` by **reference identity** against each trace's `Ir` subtree; where no match is found, emit `CteProvenance` with a null ordinal.
4. Call `SqlBuilder.Run(plan, new EmitOptions(IncludeTextRanges: true))`.
5. Catch `NotSupportedException` and `KeyNotFoundException` **at this boundary only**, recording `Failed` with the stage rather than letting it escape. Read the span from `ex.Data[LeafLoweringDispatcher.SpanDataKey]` when present.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0 --filter "FullyQualifiedName~SearchTraceTests"`
Expected: PASS — 2 tests.

- [ ] **Step 7: Add the chain-completeness test**

Add to `SearchTraceTests` a theory covering leaf, composite, chain, include, sort, `:not`, and `:missing`. For each: every `SearchParameterPredicateExpression`/`CompositeComponentExpression` in the IR has a non-null `Span`; every leaf/composite-derived `ParamSource` CTE has a non-null `ParameterOrdinal`; every CTE has a `SqlTextRange`. `:text` and `:of-type` are exempt on the IR side; `:missing`, compartment, and structural CTEs are exempt on the plan side.

- [ ] **Step 8: Run the full suites and build the solution**

This is the plan's exit gate — the only point where everything is verified together.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj -f net10.0`
Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj -f net10.0`
Run: `dotnet build All.sln`
Expected: both suites green; solution builds with 0 errors. (`Ignixa.DataLayer.LegacySqlEF.Tests` has
pre-existing failures unrelated to this work — no *new* errors may appear.)

- [ ] **Step 9: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Tracing/ test/Ignixa.Search.Sql.Tests/Tracing/
git commit -m "feat(search-sql): add SearchCompiler.CompileAsync and the end-to-end SearchTrace"
```
