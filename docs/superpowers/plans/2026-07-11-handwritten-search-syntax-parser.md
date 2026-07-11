# Handwritten Search Syntax Parser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace only the allocation-heavy Superpower search tokenizers and grammars with positioned handwritten span scanners that emit the existing immutable syntax records while preserving parser behavior, public contracts, and tenant/version-aware semantic binding.

**Architecture:** `SearchKeySyntaxParser` and `SearchValueSyntaxParser` scan source strings directly with indexes and `ReadOnlySpan<char>`; they create the existing `SearchKeySyntax` and `SearchValueSyntax` records without an intermediate token list. `SearchSyntaxExceptionFactory` converts a zero-based source offset to the existing resource-backed line/column diagnostic, while `SearchKeyBinder`, `SearchExpressionBinder`, `SearchAtomicValueParser`, the public facades, and all semantic resolution remain unchanged.

**Tech Stack:** C# latest on .NET 10, `ReadOnlySpan<char>`, immutable records and `ImmutableArray<T>`, xUnit, Shouldly, NSubstitute, BenchmarkDotNet, PowerShell, MSBuild central package management.

---

## Ratified scope and non-goals

This plan supersedes future work in [the 2026-07-10 Superpower implementation plan](2026-07-10-superpower-search-expression-parser.md). Tasks 1-15 in that plan are retained as historical implementation and benchmark evidence; its Task 16 is not the completion path for this revision.

Keep these current components:

- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/*.cs`, except the two token-kind enums listed for deletion below.
- `src/Core/Ignixa.Search/Expressions/Parsers/Binding/*.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/IExpressionParser.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/ISearchParameterExpressionParser.cs`.
- Characterization, facade, binder, include/not-referenced, and error-parity test coverage.
- The unchanged six-case public-facade benchmark harness and all 2026-07-10 baseline/rejected-Superpower artifacts.

Replace or delete only this parser-library layer:

- `SearchKeyTokenizer`, `SearchKeyGrammar`, and `SearchKeyTokenKind`.
- `SearchValueTokenizer`, `SearchValueGrammar`, and `SearchValueTokenKind`.
- `SearchParseExceptionMapper`.
- The direct `Superpower` package reference in `src/Core/Ignixa.Search/Ignixa.Search.csproj` after the final search-parser use is gone.

Do not:

- Recreate the pre-`02eb4a5` schema-aware `TrySplit`/`TryConsume` parser.
- Put `ISearchParameterDefinitionManager`, `IFhirSchemaProvider`, `SearchParameterInfo`, target-resource validation, or modifier support checks into either syntax parser.
- Add a fallback, dual parser, token-list intermediate, parser cache, or second public parser contract.
- Change `IExpressionParser`, `ISearchParameterExpressionParser`, `SearchOptionsBuilderFactory`, or tenant/FHIR-version construction.
- Change the six benchmark cases or the locked harness manifest.
- Delete or rewrite the rejected Superpower benchmark artifacts.
- Claim that the handwritten syntax parser is faster or near baseline before the final measurements satisfy the documented criteria.

## File map

### Production files to create

- `src/Core/Ignixa.Search/Expressions/Parsers/SearchSyntaxExceptionFactory.cs` — maps a source offset to the `Resources.MalformedSearchSyntax` line/column message.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs` — syntax-only recursive-descent parser for terminal parameters, forward/reverse chains, includes, wildcard includes, and `_not-referenced`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs` — syntax-only escape validator and delimiter scanner for scalar, alternative, composite, `:missing`, `:text`, and `:of-type` values.

Every new production file starts with:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
```

Static fields and combinators use PascalCase, such as `SearchComparators`; do not introduce `s_` prefixes.

### Production files to modify

- `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:63-72,109` — call `SearchKeySyntaxParser`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:46-49` — call `SearchValueSyntaxParser`.
- `src/Core/Ignixa.Search/Resources.resx:290-292` — retain the localized value and replace the Superpower-specific resource comment.
- `src/Core/Ignixa.Search/Ignixa.Search.csproj:29-34` — remove the direct Superpower package reference after cutover.
- `tools/benchmarks/Compare-SearchParserBenchmarks.ps1:1-17,273-346` — make the ratified acceptance limits explicit without weakening the pre-existing stricter `Faster` classification.

### Production files to delete after cutover

- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyTokenizer.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeyTokenKind.cs`.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs`.

### Test files to create or rename

- Create `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSyntaxExceptionFactoryTests.cs`.
- Rename `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs` to `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeySyntaxParserTests.cs`.
- Create `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSpecialKeySyntaxParserTests.cs` from the direct syntax cases currently mixed into `IncludeAndNotReferencedParserTests.cs`.
- Rename `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs` to `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueSyntaxParserTests.cs`.
- Delete `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs` only after its escape cases are present in `SearchValueSyntaxParserTests.cs`.
- Create `tools/benchmarks/tests/Test-Compare-SearchParserBenchmarks.ps1`.

### Test files to retain and update

- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/ExpressionParserCharacterizationTests.cs`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserFacadeTests.cs`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserTestContext.cs`.

### Benchmark and documentation files to create or modify only after final measurement

- Create `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.csv`.
- Create `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.md`.
- Create `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md`.
- Modify `docs/features/search/investigations/superpower-search-expression-parser.md`.
- Modify `docs/features/search/readme.md`.
- Modify `docs/site/docs/core-sdk/search.md` only after the final benchmark gate is accepted.

## Baseline facts to preserve

- Commit `02eb4a5` passes 113 focused parser cases and builds `All.sln`.
- The unchanged `Simple` benchmark calls `_parser.Parse(["Patient"], "name", "Smith")`; it parses both key and value.
- The original handwritten baseline is `docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv`.
- The rejected Superpower result remains in `2026-07-10-superpower-parser.csv`, `2026-07-10-superpower-parser.md`, and `2026-07-10-superpower-search-expression-parser-comparison.md`.
- Final acceptance requires correctness plus all of:
  - geometric-mean mean-time regression `<= 10%`;
  - no individual case mean-time regression `> 20%`;
  - no individual case allocated-byte regression `> 25%`;
  - no individual case Gen0 regression `> 25%`.
- Any violation blocks completion and requires investigation plus explicit user acceptance. Commit approval is not performance-regression acceptance.
- The existing stricter `Faster` classification remains unchanged: geometric-mean time improvement of at least 5%, no individual mean regression above 5%, and no allocation or Gen0 increase.

### Task 1: Add the positioned syntax exception boundary

**Files:**
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSyntaxExceptionFactoryTests.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchSyntaxExceptionFactory.cs`
- Modify: `src/Core/Ignixa.Search/Resources.resx:290-292`

- [ ] **Step 1: Write the failing offset-to-position tests**

Create the test file with these cases:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchSyntaxExceptionFactoryTests
{
    [Theory]
    [InlineData("patient..name", 8, 1, 9)]
    [InlineData("first\nsecond", 6, 2, 1)]
    [InlineData("first\rsecond", 6, 2, 1)]
    [InlineData("first\r\nsecond", 7, 2, 1)]
    [InlineData(@"value\", 5, 1, 6)]
    public void GivenSourceOffset_WhenCreatingException_ThenReportsOneBasedPosition(
        string source,
        int offset,
        int expectedLine,
        int expectedColumn)
    {
        var exception = SearchSyntaxExceptionFactory.Create(
            source,
            offset,
            "search value",
            "expected valid syntax");

        exception.Message.ShouldBe(
            $"Malformed search value at line {expectedLine}, column {expectedColumn}: expected valid syntax");
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchSyntaxExceptionFactoryTests" --no-restore
```

Expected: build failure `CS0103` or `CS0246` naming `SearchSyntaxExceptionFactory`; no test passes because the production type does not exist.

- [ ] **Step 3: Implement the exception factory**

Create `SearchSyntaxExceptionFactory.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchSyntaxExceptionFactory
{
    internal static InvalidSearchOperationException Create(
        string source,
        int offset,
        string subject,
        string detail)
    {
        int boundedOffset = Math.Clamp(offset, 0, source.Length);
        int line = 1;
        int column = 1;

        for (int index = 0; index < boundedOffset; index++)
        {
            if (source[index] == '\r')
            {
                line++;
                column = 1;
                if (index + 1 < boundedOffset && source[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new InvalidSearchOperationException(string.Format(
            CultureInfo.InvariantCulture,
            Resources.MalformedSearchSyntax,
            subject,
            line,
            column,
            detail));
    }
}
```

Keep the resource value unchanged and replace only its comment:

```xml
<data name="MalformedSearchSyntax" xml:space="preserve">
  <value>Malformed {0} at line {1}, column {2}: {3}</value>
  <comment>{0}=syntax subject, {1}=line, {2}=column, {3}=positioned syntax detail</comment>
</data>
```

Do not hand-edit `Resources.Designer.cs`; the localized value is unchanged, so its generated property remains valid.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchSyntaxExceptionFactoryTests" --no-restore
```

Expected: `Passed! - Failed: 0` and all five position cases pass, including standalone CR, standalone LF, and CRLF counted as one line break.

- [ ] **Step 5: Request approval for the first checkpoint commit**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchSyntaxExceptionFactory.cs src/Core/Ignixa.Search/Resources.resx test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSyntaxExceptionFactoryTests.cs
git status --short
```

Proposed subject: `Add positioned search syntax errors`

Ask the user to approve the commit. Only after explicit approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchSyntaxExceptionFactory.cs src/Core/Ignixa.Search/Resources.resx test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSyntaxExceptionFactoryTests.cs
git commit -m "Add positioned search syntax errors"
```

### Task 2: Parse ordinary, forward, and recursive reverse keys without tokens

**Files:**
- Rename: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs` to `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeySyntaxParserTests.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs`

- [ ] **Step 1: Rename the direct grammar test and point it at the wished-for API**

Run:

```powershell
git mv test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeySyntaxParserTests.cs
```

Rename the class to `SearchKeySyntaxParserTests` and replace all `SearchKeyGrammar.ParseParameter` calls with `SearchKeySyntaxParser.ParseParameter`. Add these syntax-only cases:

```csharp
[Theory]
[InlineData("")]
[InlineData(".name")]
[InlineData("patient..name")]
[InlineData("name:exact:contains")]
[InlineData("_has:Observation:subject")]
[InlineData("_has::subject:code")]
public void GivenMalformedParameterKey_WhenParsing_ThenThrowsPositionedSyntaxError(string key)
{
    var exception = Should.Throw<InvalidSearchOperationException>(
        () => SearchKeySyntaxParser.ParseParameter(key));

    exception.Message.ShouldContain("Malformed search key");
    exception.Message.ShouldContain("line 1");
    exception.Message.ShouldContain("column");
}
```

- [ ] **Step 2: Run the renamed test and verify RED**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchKeySyntaxParserTests" --no-restore
```

Expected: build failure naming missing `SearchKeySyntaxParser`.

- [ ] **Step 3: Implement the direct recursive-descent key parser**

Create `SearchKeySyntaxParser.cs` with one major symbol and a private nested cursor. Reverse detection must use allocation-free structural lookahead so `_has` only commits to reverse parsing when the remaining text matches `_has:<identifier>:<identifier>:`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers.Syntax;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchKeySyntaxParser
{
    internal static SearchKeySyntax ParseParameter(string source)
    {
        var cursor = new Cursor(source, "search key");
        SearchKeySyntax syntax = cursor.ParseKey();
        cursor.RequireEnd();
        return syntax;
    }

    internal static IncludeKeySyntax ParseInclude(string source)
    {
        var cursor = new Cursor(source, "include key");
        IncludeKeySyntax syntax = cursor.ParseInclude();
        cursor.RequireEnd();
        return syntax;
    }

    internal static NotReferencedKeySyntax ParseNotReferenced(string source)
    {
        var cursor = new Cursor(source, "_not-referenced value");
        NotReferencedKeySyntax syntax = cursor.ParseNotReferenced();
        cursor.RequireEnd();
        return syntax;
    }

    private ref struct Cursor
    {
        private readonly string _source;
        private readonly string _subject;
        private int _offset;

        internal Cursor(string source, string subject)
        {
            _source = source;
            _subject = subject;
            _offset = 0;
        }

        private bool AtEnd => _offset == _source.Length;

        internal SearchKeySyntax ParseKey()
        {
            return LooksLikeReverse()
                ? ParseReverse()
                : ParseParameterOrForward();
        }

        internal IncludeKeySyntax ParseInclude()
        {
            string sourceResourceType = Consume('*')
                ? "*"
                : ParseIdentifier("include source resource type");
            Require(':', "':' after include source resource type");

            if (Consume('*'))
            {
                return new IncludeKeySyntax(sourceResourceType, null, null, true);
            }

            string searchParameterName = ParseIdentifier("include search parameter");
            string? targetResourceType = Consume(':')
                ? ParseIdentifier("include target resource type")
                : null;
            return new IncludeKeySyntax(
                sourceResourceType,
                searchParameterName,
                targetResourceType,
                false);
        }

        internal NotReferencedKeySyntax ParseNotReferenced()
        {
            string? sourceResourceType = Consume('*')
                ? null
                : ParseIdentifier("_not-referenced source resource type");
            Require(':', "':' after _not-referenced source resource type");

            if (Consume('*'))
            {
                return new NotReferencedKeySyntax(sourceResourceType, null);
            }

            int pathOffset = _offset;
            string referencePath = ParseIdentifier("_not-referenced reference path");
            if (!IsAsciiLetter(referencePath[0]))
            {
                throw Error(pathOffset, "reference path beginning with a letter");
            }

            return new NotReferencedKeySyntax(sourceResourceType, referencePath);
        }

        internal void RequireEnd()
        {
            if (!AtEnd)
            {
                throw Error(_offset, "end of input");
            }
        }

        private SearchKeySyntax ParseParameterOrForward()
        {
            string name = ParseIdentifier("search parameter name");
            string? qualifier = Consume(':')
                ? ParseIdentifier("modifier or target resource type")
                : null;

            return Consume('.')
                ? new ForwardChainKeySyntax(name, qualifier, ParseKey())
                : new ParameterKeySyntax(name, qualifier);
        }

        private ReverseChainKeySyntax ParseReverse()
        {
            RequireLiteral("_has", "'_has'");
            Require(':', "':' after _has");
            string sourceResourceType = ParseIdentifier("_has source resource type");
            Require(':', "':' after _has source resource type");
            string referenceName = ParseIdentifier("_has reference search parameter");
            Require(':', "':' before nested _has search key");
            return new ReverseChainKeySyntax(
                sourceResourceType,
                referenceName,
                ParseKey());
        }

        private string ParseIdentifier(string expectation)
        {
            int start = _offset;
            if (AtEnd || !IsIdentifierStart(_source[_offset]))
            {
                throw Error(_offset, expectation);
            }

            _offset++;
            while (!AtEnd && IsIdentifierPart(_source[_offset]))
            {
                _offset++;
            }

            return _source[start.._offset];
        }

        private bool StartsWith(string literal)
        {
            return _source.AsSpan(_offset).StartsWith(
                literal,
                StringComparison.Ordinal);
        }

        private bool LooksLikeReverse()
        {
            int lookaheadOffset = _offset;
            if (!ConsumeLiteralIfAt("_has:", ref lookaheadOffset))
            {
                return false;
            }

            if (!TrySkipIdentifier(ref lookaheadOffset))
            {
                return false;
            }

            if (!ConsumeIfAt(':', ref lookaheadOffset))
            {
                return false;
            }

            if (!TrySkipIdentifier(ref lookaheadOffset))
            {
                return false;
            }

            return ConsumeIfAt(':', ref lookaheadOffset);
        }

        private void RequireLiteral(string literal, string expectation)
        {
            if (!StartsWith(literal))
            {
                throw Error(_offset, expectation);
            }

            _offset += literal.Length;
        }

        private bool Consume(char expected)
        {
            if (AtEnd || _source[_offset] != expected)
            {
                return false;
            }

            _offset++;
            return true;
        }

        private void Require(char expected, string expectation)
        {
            if (!Consume(expected))
            {
                throw Error(_offset, expectation);
            }
        }

        private InvalidSearchOperationException Error(int offset, string expectation)
        {
            return SearchSyntaxExceptionFactory.Create(
                _source,
                offset,
                _subject,
                $"expected {expectation}");
        }

        private static bool IsIdentifierStart(char value)
        {
            return IsAsciiLetter(value) || value == '_';
        }

        private static bool IsIdentifierPart(char value)
        {
            return IsAsciiLetter(value) ||
                value is >= '0' and <= '9' ||
                value is '_' or '-';
        }

        private static bool IsAsciiLetter(char value)
        {
            return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
        }
    }
}
```

The parser may allocate strings for syntax-node values; it must not allocate a token collection. It is syntax-only: the qualifier remains an uninterpreted string until `SearchKeyBinder`.

- [ ] **Step 4: Run direct key syntax tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchKeySyntaxParserTests" --no-restore
```

Expected: `Passed! - Failed: 0`; terminal modifiers, typed/untyped forward chains, recursive `_has`, mixed chains, and malformed positions pass.

- [ ] **Step 5: Verify scanner boundaries**

Run:

```powershell
rg "Tokenizer|TokenList|Superpower|ISearchParameterDefinitionManager|IFhirSchemaProvider|SearchParameterInfo|TrySplit|TryConsume" src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs
```

Expected: no matches and exit code 1.

### Task 3: Parse include and `_not-referenced` syntax, then cut over key parsing

**Files:**
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSpecialKeySyntaxParserTests.cs`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs:24-80,95,109,122,135,148,155-162`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs:105`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs:19-35`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:63-72,109`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyTokenizer.cs`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeyTokenKind.cs`

- [ ] **Step 1: Move direct special-key cases into a syntax-parser test**

Create `SearchSpecialKeySyntaxParserTests.cs` and move, rather than duplicate, the direct include/not-referenced cases:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchSpecialKeySyntaxParserTests
{
    [Theory]
    [InlineData("Observation:subject", "Observation", "subject", null, false)]
    [InlineData("Observation:subject:Patient", "Observation", "subject", "Patient", false)]
    [InlineData("Observation:*", "Observation", null, null, true)]
    [InlineData("*:*", "*", null, null, true)]
    public void GivenIncludeSyntax_WhenParsing_ThenReturnsExpectedSyntax(
        string value,
        string expectedSourceResourceType,
        string? expectedSearchParameterName,
        string? expectedTargetResourceType,
        bool expectedWildcard)
    {
        IncludeKeySyntax syntax = SearchKeySyntaxParser.ParseInclude(value);

        syntax.SourceResourceType.ShouldBe(expectedSourceResourceType);
        syntax.SearchParameterName.ShouldBe(expectedSearchParameterName);
        syntax.TargetResourceType.ShouldBe(expectedTargetResourceType);
        syntax.Wildcard.ShouldBe(expectedWildcard);
    }

    [Theory]
    [InlineData("*:*", null, null)]
    [InlineData("Observation:*", "Observation", null)]
    [InlineData("Observation:subject", "Observation", "subject")]
    public void GivenNotReferencedSyntax_WhenParsing_ThenReturnsExpectedSyntax(
        string value,
        string? expectedSourceResourceType,
        string? expectedReferencePath)
    {
        NotReferencedKeySyntax syntax = SearchKeySyntaxParser.ParseNotReferenced(value);

        syntax.SourceResourceType.ShouldBe(expectedSourceResourceType);
        syntax.ReferencePath.ShouldBe(expectedReferencePath);
    }

    [Theory]
    [InlineData("Observation:")]
    [InlineData("Observation:subject.name")]
    [InlineData("Observation:subject:extra")]
    [InlineData("Observation:subject name")]
    public void GivenMalformedNotReferencedSyntax_WhenParsing_ThenThrowsPositionedInvalidSearchOperation(
        string value)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseNotReferenced(value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Fact]
    public void GivenIncludeWithTrailingTargetColon_WhenParsing_ThenThrowsPositionedInvalidSearchOperation()
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeySyntaxParser.ParseInclude("Observation:subject:"));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column 21");
    }
}
```

- [ ] **Step 2: Keep binder tests semantic-only**

In `IncludeAndNotReferencedParserTests.cs`, remove the moved direct syntax tests and replace parser setup with explicit records:

```csharp
var syntax = new IncludeKeySyntax("Observation", "subject", null, false);
```

```csharp
var syntax = new IncludeKeySyntax("Patient", null, null, true);
```

```csharp
var syntax = new IncludeKeySyntax("Observation", null, null, true);
```

```csharp
var syntax = new IncludeKeySyntax("Observation", "subject", "FakeType", false);
```

```csharp
var syntax = new NotReferencedKeySyntax("FakeType", "subject");
```

In `SearchKeyBinderTests.cs`, replace its `SearchKeyGrammar.ParseParameter(...)` setup with:

```csharp
var syntax = new ReverseChainKeySyntax(
    "Group",
    "member",
    new ParameterKeySyntax("_tag", null));
```

This keeps `SearchKeyBinderTests` independent of the scanner.

In `SearchParserErrorParityTests.cs`, rename
`GivenMalformedKey_WhenParsing_ThenReportsSuperpowerPosition` to
`GivenMalformedKey_WhenParsing_ThenReportsSyntaxPosition`; keep its subject,
line, and exact column assertions unchanged.

- [ ] **Step 3: Run special-key and binder tests before facade cutover**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "(FullyQualifiedName~SearchSpecialKeySyntaxParserTests|FullyQualifiedName~IncludeAndNotReferencedParserTests|FullyQualifiedName~SearchKeyBinderTests)" --no-restore
```

Expected: `Passed! - Failed: 0`; direct syntax cases exercise `SearchKeySyntaxParser`, while binder cases use syntax records directly.

- [ ] **Step 4: Cut the public facade over to the key syntax parser**

In `ExpressionParser.cs`, make only these substitutions:

```csharp
NotReferencedKeySyntax syntax =
    SearchKeySyntaxParser.ParseNotReferenced(value);
```

```csharp
SearchKeySyntax keySyntax = SearchKeySyntaxParser.ParseParameter(key);
```

```csharp
IncludeKeySyntax syntax = SearchKeySyntaxParser.ParseInclude(includeValue);
```

Retain the existing `_not-referenced` branch, include trailing-colon resource message, binders, and public method signatures unchanged.

- [ ] **Step 5: Run all key/facade/parity tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "(FullyQualifiedName~SearchKeySyntaxParserTests|FullyQualifiedName~SearchSpecialKeySyntaxParserTests|FullyQualifiedName~SearchKeyBinderTests|FullyQualifiedName~IncludeAndNotReferencedParserTests|FullyQualifiedName~SearchParserFacadeTests|FullyQualifiedName~SearchParserErrorParityTests|FullyQualifiedName~ExpressionParserCharacterizationTests)" --no-restore
```

Expected: `Passed! - Failed: 0`; `patient..name` reports column 9, `name:exact:contains` reports column 11, facade ASTs are unchanged, and include resource messages retain exact parity.

- [ ] **Step 6: Delete the obsolete key parser-library layer**

Run:

```powershell
git rm src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyTokenizer.cs
git rm src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs
git rm src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeyTokenKind.cs
rg "SearchKeyTokenizer|SearchKeyGrammar|SearchKeyTokenKind" src/Core/Ignixa.Search test/Ignixa.Application.Tests/Search/Expressions/Parsers
```

Expected: the `rg` command has no matches and exits 1. Do not remove the Superpower package yet because value parsing still uses it.

- [ ] **Step 7: Request approval for the key-scanner checkpoint commit**

Run:

```powershell
git --no-pager diff --stat
git --no-pager diff --check
git status --short
```

Proposed subject: `Replace Superpower search key parsing`

Ask for explicit approval. Only after approval, stage the exact Task 2-3 paths and run:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeySyntaxParserTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchSpecialKeySyntaxParserTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs
git commit -m "Replace Superpower search key parsing"
```

The earlier `git mv` and `git rm` commands already stage the rename and
deletions; do not restage nonexistent old paths.

### Task 4: Run the unchanged six-case harness after key cutover

**Files:**
- Verify unchanged: `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj`
- Verify unchanged: `bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs`
- Verify unchanged: `bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs`
- Verify unchanged: `bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs`
- Verify unchanged: `docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256`

- [ ] **Step 1: Prove the harness has not changed**

Run:

```powershell
$manifest = 'docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256'
$mismatches = foreach ($line in Get-Content -LiteralPath $manifest) {
    $parts = $line -split '\s{2}', 2
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $parts[1]).Hash
    if ($actual -ne $parts[0]) {
        "$($parts[1]) expected $($parts[0]) but was $actual"
    }
}
if ($mismatches) {
    throw "Benchmark harness changed:`n$($mismatches -join [Environment]::NewLine)"
}
```

Expected: no output and exit code 0.

- [ ] **Step 2: Build and run the key-cutover diagnostic**

Run:

```powershell
dotnet build bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-restore
dotnet run --project bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-build -- --filter "*SearchExpressionParserBenchmarks*" --artifacts "BenchmarkDotNet.Artifacts/search-parser-key-scanner-diagnostic" --launchCount 1 --warmupCount 5 --iterationCount 15
```

Expected: build succeeds and BenchmarkDotNet emits exactly the six `Parse` rows `Simple`, `Modified`, `TypedChain`, `NestedReverseChain`, `EscapedAlternative`, and `Composite`, each with Mean, Gen0, and Allocated values.

- [ ] **Step 3: Record the diagnostic without treating it as acceptance**

Inspect:

```powershell
Get-Content BenchmarkDotNet.Artifacts/search-parser-key-scanner-diagnostic/results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report-github.md
git status --short
```

Expected: the report is readable; only ignored `BenchmarkDotNet.Artifacts` output is new. Do not copy this intermediate report into `docs/`, do not update feature status, and do not infer key-only cost from `Simple` because every case parses both key and value.

### Task 5: Implement the complete handwritten value syntax parser

**Files:**
- Rename: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs` to `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueSyntaxParserTests.cs`
- Read: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs`

- [ ] **Step 1: Rename the direct tests and write the complete RED behavior matrix**

Run:

```powershell
git mv test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueSyntaxParserTests.cs
```

Rename the class to `SearchValueSyntaxParserTests` and replace
`SearchValueGrammar.Parse` with `SearchValueSyntaxParser.Parse`. Re-express the
invalid-escape cases from `SearchValueTokenizerTests` in this parser-level test;
leave the old tokenizer test file unchanged until Task 6 deletes it:

```csharp
[Theory]
[InlineData(@"\", 1)]
[InlineData(@"\q", 1)]
[InlineData(@"value\", 6)]
[InlineData(@"value\q", 6)]
public void GivenInvalidFhirEscape_WhenParsing_ThenReportsEscapePosition(
    string value,
    int expectedColumn)
{
    var exception = Should.Throw<InvalidSearchOperationException>(
        () => SearchValueSyntaxParser.Parse(
            SearchParamType.String,
            null,
            value));

    exception.Message.ShouldContain("Malformed search value");
    exception.Message.ShouldContain("line 1");
    exception.Message.ShouldContain($"column {expectedColumn}");
    exception.Message.ShouldContain("valid FHIR escape");
}

[Fact]
public void GivenEscapedSeparators_WhenParsingScalar_ThenPreservesRawEscapedText()
{
    SearchValueSyntax syntax = SearchValueSyntaxParser.Parse(
        SearchParamType.Token,
        null,
        @"a\,b\$c\|d\\e");

    syntax.ShouldBe(new AtomicValueSyntax(
        @"a\,b\$c\|d\\e",
        SearchComparator.Eq));
}
```

Retain the existing comparator theory proving that only date, number, and quantity scalar types consume comparator prefixes. Add `SearchParamType.Token` with `gtcode` and expect `SearchComparator.Eq` plus raw text `gtcode`.

Add the special-form and composite edge cases before creating production code:

```csharp
[Fact]
public void GivenTextModifier_WhenParsing_ThenTreatsAllSeparatorsAsLiteral()
{
    SearchValueSyntax result = SearchValueSyntaxParser.Parse(
        SearchParamType.Token,
        new SearchModifier(SearchModifierCode.Text),
        "alpha,beta$gamma|delta");

    result.ShouldBe(new AtomicValueSyntax(
        "alpha,beta$gamma|delta",
        SearchComparator.Eq));
}

[Fact]
public void GivenOfTypeEscapedPipe_WhenParsing_ThenDoesNotSplitEscapedPipe()
{
    SearchValueSyntax result = SearchValueSyntaxParser.Parse(
        SearchParamType.Token,
        new SearchModifier(SearchModifierCode.OfType),
        @"http://example.org\|v2|MR|123");

    result.ShouldBe(new OfTypeValueSyntax(
        @"http://example.org\|v2",
        "MR",
        "123"));
}

[Theory]
[InlineData("a$$b", 3)]
[InlineData("$a", 1)]
[InlineData("a$", 3)]
public void GivenEmptyCompositeComponent_WhenParsing_ThenReportsPosition(
    string value,
    int expectedColumn)
{
    var exception = Should.Throw<InvalidSearchOperationException>(
        () => SearchValueSyntaxParser.Parse(
            SearchParamType.Composite,
            null,
            value));

    exception.Message.ShouldContain($"column {expectedColumn}");
}
```

Keep the migrated scalar, escaped-alternative, composite-alternative,
`:missing`, `:text`, `:of-type`, invalid arity, empty input, and empty-part
cases in the same test class. After Task 6 deletes the obsolete tokenizer test,
the parser-level cases above preserve all externally relevant escape behavior.

- [ ] **Step 2: Run the complete direct syntax suite and verify RED**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchValueSyntaxParserTests" --no-restore
```

Expected: build failure `CS0103` or `CS0246` naming missing
`SearchValueSyntaxParser`. Every direct value behavior is specified before the
production type exists.

- [ ] **Step 3: Implement the complete value syntax parser**

Create `SearchValueSyntaxParser.cs` starting with:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Immutable;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;
```

Add the class declaration, PascalCase comparator table, and entry point:

```csharp
internal static class SearchValueSyntaxParser
{
    private static readonly (string Literal, SearchComparator Comparator)[] SearchComparators =
        Enum.GetValues<SearchComparator>()
            .Select(comparator => (comparator.GetLiteral(), comparator))
            .OrderByDescending(pair => pair.Item1.Length)
            .ToArray();

    internal static SearchValueSyntax Parse(
        SearchParamType searchType,
        SearchModifier? modifier,
        string source)
    {
        if (modifier?.SearchModifierCode == SearchModifierCode.Missing)
        {
            return ParseMissing(source);
        }

        ValidateEscapes(source);

        return modifier?.SearchModifierCode switch
        {
            SearchModifierCode.Text => source.Length == 0
                ? throw SyntaxError(source, 0, "non-empty text modifier value")
                : new AtomicValueSyntax(source, SearchComparator.Eq),
            SearchModifierCode.OfType => ParseOfType(source),
            _ when searchType == SearchParamType.Composite => ParseComposite(source),
            _ => ParseScalar(
                source,
                searchType is
                    SearchParamType.Date or
                    SearchParamType.Number or
                    SearchParamType.Quantity),
        };
    }
```

Use direct index scanning, not `Split`, regular expressions, or a token collection:

```csharp
private static void ValidateEscapes(string source)
{
    for (int index = 0; index < source.Length; index++)
    {
        if (source[index] != '\\')
        {
            continue;
        }

        if (index + 1 >= source.Length ||
            source[index + 1] is not ('\\' or ',' or '$' or '|'))
        {
            throw SearchSyntaxExceptionFactory.Create(
                source,
                index,
                "search value",
                "expected valid FHIR escape for backslash, comma, dollar, or pipe");
        }

        index++;
    }
}

private static int FindUnescaped(string source, char delimiter, int start)
{
    for (int index = start; index < source.Length; index++)
    {
        if (source[index] == '\\')
        {
            index++;
            continue;
        }

        if (source[index] == delimiter)
        {
            return index;
        }
    }

    return -1;
}
```

Implement the no-comma fast path before allocating an alternatives builder:

```csharp
private static SearchValueSyntax ParseScalar(
    string source,
    bool supportsComparator)
{
    if (source.Length == 0)
    {
        throw SyntaxError(source, 0, "non-empty search value");
    }

    int comma = FindUnescaped(source, ',', 0);
    if (comma < 0)
    {
        return ParseAtomic(source, 0, source.Length, supportsComparator);
    }

    var items = ImmutableArray.CreateBuilder<SearchValueSyntax>();
    int start = 0;
    while (comma >= 0)
    {
        items.Add(ParseAtomic(source, start, comma - start, supportsComparator));
        start = comma + 1;
        comma = FindUnescaped(source, ',', start);
    }

    items.Add(ParseAtomic(source, start, source.Length - start, supportsComparator));
    return new AlternativesValueSyntax(items.ToImmutable());
}

private static AtomicValueSyntax ParseAtomic(
    string source,
    int start,
    int length,
    bool supportsComparator)
{
    if (length == 0)
    {
        throw SyntaxError(source, start, "non-empty search value part");
    }

    ReadOnlySpan<char> raw = source.AsSpan(start, length);
    if (supportsComparator)
    {
        foreach ((string literal, SearchComparator comparator) in SearchComparators)
        {
            if (raw.StartsWith(literal, StringComparison.Ordinal))
            {
                string value = source.Substring(
                    start + literal.Length,
                    length - literal.Length);
                return new AtomicValueSyntax(value, comparator);
            }
        }
    }

    string rawText = start == 0 && length == source.Length
        ? source
        : source.Substring(start, length);
    return new AtomicValueSyntax(rawText, SearchComparator.Eq);
}

private static InvalidSearchOperationException SyntaxError(
    string source,
    int offset,
    string expectation)
{
    return SearchSyntaxExceptionFactory.Create(
        source,
        offset,
        "search value",
        $"expected {expectation}");
}
```

Add `:missing`, `:of-type`, composite, and slicing helpers to the same class:

```csharp
private static MissingValueSyntax ParseMissing(string source)
{
    if (!bool.TryParse(source, out bool isMissing))
    {
        throw new InvalidSearchOperationException(
            Resources.InvalidValueTypeForMissingModifier);
    }

    return new MissingValueSyntax(isMissing);
}

private static SearchValueSyntax ParseOfType(string source)
{
    if (source.Length == 0)
    {
        throw SyntaxError(source, 0, "system|code|value");
    }

    int comma = FindUnescaped(source, ',', 0);
    if (comma < 0)
    {
        return ParseOfTypeItem(source, 0, source.Length);
    }

    var items = ImmutableArray.CreateBuilder<SearchValueSyntax>();
    int start = 0;
    while (comma >= 0)
    {
        items.Add(ParseOfTypeItem(source, start, comma - start));
        start = comma + 1;
        comma = FindUnescaped(source, ',', start);
    }

    items.Add(ParseOfTypeItem(source, start, source.Length - start));
    return new AlternativesValueSyntax(items.ToImmutable());
}

private static OfTypeValueSyntax ParseOfTypeItem(
    string source,
    int start,
    int length)
{
    int end = start + length;
    int firstPipe = FindUnescaped(source, '|', start);
    int secondPipe = firstPipe < 0
        ? -1
        : FindUnescaped(source, '|', firstPipe + 1);
    int thirdPipe = secondPipe < 0
        ? -1
        : FindUnescaped(source, '|', secondPipe + 1);

    if (firstPipe < 0 || firstPipe >= end ||
        secondPipe < 0 || secondPipe >= end ||
        (thirdPipe >= 0 && thirdPipe < end))
    {
        int offset = thirdPipe >= 0 && thirdPipe < end
            ? thirdPipe
            : end;
        throw SyntaxError(
            source,
            offset,
            "exactly three '|' delimited :of-type segments");
    }

    return new OfTypeValueSyntax(
        Slice(source, start, firstPipe - start),
        Slice(source, firstPipe + 1, secondPipe - firstPipe - 1),
        Slice(source, secondPipe + 1, end - secondPipe - 1));
}

private static SearchValueSyntax ParseComposite(string source)
{
    if (source.Length == 0)
    {
        throw SyntaxError(source, 0, "non-empty composite value");
    }

    int comma = FindUnescaped(source, ',', 0);
    if (comma < 0)
    {
        return ParseCompositeItem(source, 0, source.Length);
    }

    var items = ImmutableArray.CreateBuilder<SearchValueSyntax>();
    int start = 0;
    while (comma >= 0)
    {
        items.Add(ParseCompositeItem(source, start, comma - start));
        start = comma + 1;
        comma = FindUnescaped(source, ',', start);
    }

    items.Add(ParseCompositeItem(source, start, source.Length - start));
    return new AlternativesValueSyntax(items.ToImmutable());
}

private static CompositeValueSyntax ParseCompositeItem(
    string source,
    int start,
    int length)
{
    int end = start + length;
    var components = ImmutableArray.CreateBuilder<AtomicValueSyntax>();
    int componentStart = start;
    int dollar = FindUnescaped(source, '$', componentStart);

    while (dollar >= 0 && dollar < end)
    {
        components.Add(ParseAtomic(
            source,
            componentStart,
            dollar - componentStart,
            supportsComparator: true));
        componentStart = dollar + 1;
        dollar = FindUnescaped(source, '$', componentStart);
    }

    components.Add(ParseAtomic(
        source,
        componentStart,
        end - componentStart,
        supportsComparator: true));
    return new CompositeValueSyntax(components.ToImmutable());
}

private static string Slice(string source, int start, int length)
{
    return start == 0 && length == source.Length
        ? source
        : source.Substring(start, length);
}
}
```

`$` remains literal in all three `:of-type` segments. Escaped delimiters
remain in raw text for canonical atomic parsers. Composite syntax recognizes
comparator-shaped prefixes so `SearchExpressionBinder.NormalizeCompositeComparator`
can retain them only for effective date, number, and quantity components and
restore them as literal text for other component types.

Keep the static field name `SearchComparators`.

- [ ] **Step 4: Run the complete direct value syntax suite and verify GREEN**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~SearchValueSyntaxParserTests" --no-restore
```

Expected: `Passed! - Failed: 0`; scalar fast path, comparator selection,
escaped delimiters, alternatives, composites, `:missing`, literal `:text`,
three-segment `:of-type`, empty parts, and positioned failures all pass.

- [ ] **Step 5: Verify the complete scanner has no token-list or semantic dependency**

Run:

```powershell
rg "Tokenizer|TokenList|Superpower|ISearchParameterDefinitionManager|IFhirSchemaProvider|SearchParameterInfo|TrySplit|TryConsume" src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs
```

Expected: no matches and exit code 1.

- [ ] **Step 6: Request approval for the complete value-parser checkpoint**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueSyntaxParserTests.cs
git --no-pager diff --check
git status --short
```

Proposed subject: `Add handwritten search value syntax parser`

Ask for explicit approval. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueSyntaxParserTests.cs
git commit -m "Add handwritten search value syntax parser"
```

The earlier `git mv` already stages removal of
`SearchValueGrammarTests.cs`; adding the renamed file updates the staged
content.

### Task 6: Cut over value parsing and remove Superpower

**Files:**
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:46-49`
- Delete: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs`
- Delete: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs`
- Modify: `src/Core/Ignixa.Search/Ignixa.Search.csproj:29-34`

- [ ] **Step 1: Cut over the value facade and binder test setup**

In `SearchParameterExpressionParser.cs`, use:

```csharp
SearchValueSyntax syntax = SearchValueSyntaxParser.Parse(
    searchParameter.Type,
    modifier,
    value);
```

In `SearchExpressionBinderTests.cs`, replace setup-only calls from `SearchValueGrammar.Parse` to `SearchValueSyntaxParser.Parse`. Do not change any expected AST, exception category, resource message, or canonical parsed value.

- [ ] **Step 2: Run all 113-or-more focused parser cases**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-restore
```

Expected: `Passed! - Failed: 0`; test discovery reports at least the original 113 cases, with additional scanner edge cases allowed.

- [ ] **Step 3: Remove the obsolete value parser-library layer and package reference**

Run:

```powershell
git rm test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs
git rm src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs
git rm src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs
git rm src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs
git rm src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs
```

Remove only this line from `src/Core/Ignixa.Search/Ignixa.Search.csproj`:

```xml
<PackageReference Include="Superpower" />
```

Keep the central `Directory.Packages.props` version because `Ignixa.FhirPath` and `Ignixa.FhirMappingLanguage` still consume Superpower.

- [ ] **Step 4: Build and re-run focused tests after package removal**

Run:

```powershell
dotnet restore src/Core/Ignixa.Search/Ignixa.Search.csproj
dotnet build src/Core/Ignixa.Search/Ignixa.Search.csproj --no-restore
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-restore
```

Expected: restore/build succeed with `0 Warning(s), 0 Error(s)` and focused tests report `Failed: 0`.

- [ ] **Step 5: Audit the completed cutover**

Run:

```powershell
rg "SearchValueTokenizer|SearchValueGrammar|SearchParseExceptionMapper|SearchValueTokenKind|Superpower" src/Core/Ignixa.Search/Expressions/Parsers src/Core/Ignixa.Search/Ignixa.Search.csproj test/Ignixa.Application.Tests/Search/Expressions/Parsers
```

Expected: no matches and exit code 1. This audit occurs only after the complete
parser has passed its direct tests and the facade suite.

- [ ] **Step 6: Request approval for the value-cutover checkpoint commit**

Run:

```powershell
git --no-pager diff --stat
git --no-pager diff --check
git status --short
```

Proposed subject: `Replace Superpower search value parsing`

Ask for explicit approval. Only after approval, stage the exact Task 6 paths:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs src/Core/Ignixa.Search/Ignixa.Search.csproj test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git commit -m "Replace Superpower search value parsing"
```

The Task 6 `git rm` commands already stage all deleted production and test
paths.

### Task 7: Encode the ratified benchmark limits with tests

**Files:**
- Create: `tools/benchmarks/tests/Test-Compare-SearchParserBenchmarks.ps1`
- Modify: `tools/benchmarks/Compare-SearchParserBenchmarks.ps1`

- [ ] **Step 1: Write a failing script-level acceptance test**

Create a self-contained PowerShell test that keeps the committed `docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv` as the fixture source. Do not replace it with synthetic standalone data: this script validates the real six-case schema and column set. Generate every mutated baseline or replacement fixture in the temp directory from that committed source, then invoke the comparison script in child `pwsh` processes.

The helper layer must:
- assign a unique report path per child invocation;
- delete or prove absence before launch;
- capture exit code, stdout/stderr, and report existence;
- distinguish three outcomes: accepted, blocked-by-performance, rejected-without-report.

Add report-oracle helpers that:
- parse the exact `**Geometric mean time change:** ...` line;
- parse the Markdown row for an exact case, verify it has 13 cells, and return the requested `Mean Δ`, `Allocation Δ`, or `Gen0 Δ` cell;
- assert blocking reports contain `Blocking regression: Yes`, the ratified-limit acceptance wording, and the configured-limit summary.

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).ProviderPath
$baseline = Join-Path $root 'docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv'
$script = Join-Path $root 'tools/benchmarks/Compare-SearchParserBenchmarks.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "ignixa-search-parser-$([Guid]::NewGuid().ToString('N'))"
$script:reportCounter = 0
[double] $comparisonTolerance = 1e-9
[IO.Directory]::CreateDirectory($temporary) | Out-Null

function Write-FixtureCsv {
    param([string] $Path, [scriptblock] $Mutate)

    $rows = @(Import-Csv -LiteralPath $baseline)
    & $Mutate $rows
    $rows | Export-Csv -LiteralPath $Path -NoTypeInformation
}

function Invoke-Comparison {
    param(
        [string] $ReplacementCsv,
        [string] $ScenarioName,
        [string] $BaselineOverride = $baseline,
        [string[]] $AdditionalArguments = @()
    )

    $script:reportCounter++
    $report = Join-Path $temporary ("report-{0:D2}-{1}.md" -f $script:reportCounter, $ScenarioName)
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
    if (Test-Path -LiteralPath $report) { throw "Expected no pre-existing report at '$report'." }

    $output = & pwsh -NoProfile -File $script `
        -BaselineCsv $BaselineOverride `
        -ReplacementCsv $ReplacementCsv `
        -CorrectnessStatus Passed `
        -OutputPath $report `
        @AdditionalArguments 2>&1

    [pscustomobject]@{
        ExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        Output = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        ReportExists = Test-Path -LiteralPath $report
        Report = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Raw } else { '' }
    }
}

function Get-CaseDeltaCell {
    param([psobject] $Result, [string] $CaseName, [string] $ColumnName)

    $line = @($Result.Report -split '\r?\n' | Where-Object {
        $_ -match "^\|\s*$([Text.RegularExpressions.Regex]::Escape($CaseName))\s*\|"
    })
    if ($line.Count -ne 1) { throw "Expected exactly one markdown row for case '$CaseName'." }

    $cells = @($line[0].Trim() -replace '^\|', '' -replace '\|$', '' -split '\|' | ForEach-Object { $_.Trim() })
    if ($cells.Count -ne 13) { throw "Expected 13 markdown cells for case '$CaseName'; found $($cells.Count)." }

    @{
        'Mean Δ' = $cells[3]
        'Allocation Δ' = $cells[9]
        'Gen0 Δ' = $cells[12]
    }[$ColumnName]
}

function Assert-ParameterValidationFailure {
    param([psobject] $Result, [string] $ParameterName)

    if ($Result.ExitCode -ne 1) { throw 'Parameter validation failures must exit 1.' }
    if ($Result.ReportExists) { throw 'Parameter validation failures must not create a report.' }
    if (-not $Result.Output.Contains($ParameterName)) { throw "Validation output missing $ParameterName." }
    if (-not $Result.Output.Contains('must be a finite number greater than or equal to 0.')) {
        throw 'Validation output missing finite/non-negative guidance.'
    }
}

try {
    $replacement = Join-Path $temporary 'replacement.csv'
    $baselineOverride = Join-Path $temporary 'baseline.csv'

    # identical baseline-vs-baseline exits 0, reports Blocking regression: No,
    # geometric mean 0.00%, and is not classified Faster.

    foreach ($invalidLimitCase in @(
        @{ Parameter = 'MaximumGeometricMeanRegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumGeometricMeanRegressionPercent'; Value = 'NaN' },
        @{ Parameter = 'MaximumGeometricMeanRegressionPercent'; Value = 'Infinity' },
        @{ Parameter = 'MaximumMeanRegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumMeanRegressionPercent'; Value = 'NaN' },
        @{ Parameter = 'MaximumMeanRegressionPercent'; Value = 'Infinity' },
        @{ Parameter = 'MaximumAllocationRegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumAllocationRegressionPercent'; Value = 'NaN' },
        @{ Parameter = 'MaximumAllocationRegressionPercent'; Value = 'Infinity' },
        @{ Parameter = 'MaximumGen0RegressionPercent'; Value = '-1' },
        @{ Parameter = 'MaximumGen0RegressionPercent'; Value = 'NaN' },
        @{ Parameter = 'MaximumGen0RegressionPercent'; Value = 'Infinity' }
    )) {
        $result = Invoke-Comparison -ReplacementCsv $replacement -ScenarioName "invalid-$($invalidLimitCase.Parameter)" -AdditionalArguments @(
            "-$($invalidLimitCase.Parameter)",
            $invalidLimitCase.Value
        )
        Assert-ParameterValidationFailure -Result $result -ParameterName $invalidLimitCase.Parameter
    }

    # Raw metric validation:
    #   - negative baseline Mean rejects before report write and identifies case + metric;
    #   - tiny positive baseline/replacement Mean values that overflow derived operations
    #     per second reject before report write and identify baseline/replacement
    #     operations per second;
    #   - huge numeric replacement Mean / Allocated values reject before report write even if
    #     parsing overflows to +∞;
    #   - negative replacement Allocated / Gen0 reject before report write;
    #   - replacement Gen0 NaN / Infinity reject before report write.

    # Preserve zero-denominator semantics:
    #   - baseline Allocated '-' to replacement nonzero yields Allocation Δ +∞% and blocks;
    #   - baseline Gen0 '-' to replacement nonzero yields Gen0 Δ +∞% and blocks.

    # Preserve default acceptance and just-over blocking:
    #   - Simple mean at +20.00% accepted; +20.01% blocks and the Mean Δ cell is asserted;
    #   - Simple Allocated at +25.00% accepted; just above blocks and the Allocation Δ cell is asserted;
    #   - Simple Gen0 at +25.00% accepted; just above blocks and the Gen0 Δ cell is asserted;
    #   - all six means at +10.00% accepted; +10.01% geometric mean blocks and the exact geometric-mean line is asserted.

    # Add tolerance-adjacent mean checks:
    #   - +20.00% + 0.5e-9 accepted;
    #   - +20.00% + 2e-9 blocks.

    # Add non-default wiring checks that stay within defaults but block under stricter explicit limits:
    #   - +9.00% geometric mean with -MaximumGeometricMeanRegressionPercent 8.5;
    #   - opposing extreme but finite mean ratios whose summed log differences cancel to 0,
    #     using a large explicit mean limit so Blocking remains No and the exact geometric-mean
    #     line stays 0.00%;
    #   - identical baseline-vs-baseline with -MaximumGeometricMeanRegressionPercent 1e-16 and
    #     an exact acceptance-limits line using nonzero G17 formatting;
    #   - +18.00% Simple mean with -MaximumMeanRegressionPercent 17.5;
    #   - +19.49% Simple allocation with -MaximumAllocationRegressionPercent 19;
    #   - +20.00% Simple Gen0 with -MaximumGen0RegressionPercent 19.5.
}
finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
```

- [ ] **Step 2: Run the script test and verify RED**

Run:

```powershell
pwsh -NoProfile -File tools/benchmarks/tests/Test-Compare-SearchParserBenchmarks.ps1
```

Expected: non-zero exit showing the first unmet oracle — for example `Negative baseline Mean must be rejected before report generation. Missing process output text: Case 'Simple' baseline Mean` or a Gen0 non-finite case that still writes a report — because the existing script does not yet produce case/metric-aware raw-metric validation.

- [ ] **Step 3: Add explicit acceptance parameters and blocking logic**

Add these parameters after `OutputPath`, each with finite and non-negative validation:

```powershell
[ValidateScript({
    if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
        throw 'MaximumGeometricMeanRegressionPercent must be a finite number greater than or equal to 0.'
    }

    $true
})]
[double] $MaximumGeometricMeanRegressionPercent = 10.0,
[ValidateScript({
    if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
        throw 'MaximumMeanRegressionPercent must be a finite number greater than or equal to 0.'
    }

    $true
})]
[double] $MaximumMeanRegressionPercent = 20.0,
[ValidateScript({
    if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
        throw 'MaximumAllocationRegressionPercent must be a finite number greater than or equal to 0.'
    }

    $true
})]
[double] $MaximumAllocationRegressionPercent = 25.0,
[ValidateScript({
    if ([double]::IsNaN($_) -or [double]::IsInfinity($_) -or $_ -lt 0.0) {
        throw 'MaximumGen0RegressionPercent must be a finite number greater than or equal to 0.'
    }

    $true
})]
[double] $MaximumGen0RegressionPercent = 25.0,
```

Add raw-metric parsing and validation helpers so every invalid raw metric fails before report write:

```powershell
function Parse-InvariantDoubleLiteral {
    param([string] $Value, [string] $Case, [string] $SourceLabel, [string] $Metric)

    try {
        return [double]::Parse($Value, $culture)
    }
    catch {
        throw "Case '$Case' $SourceLabel $Metric value '$Value' is not a valid numeric literal."
    }
}

function Assert-ValidMetricValue {
    param([double] $Value, [string] $Case, [string] $SourceLabel, [string] $Metric, [double] $Minimum = 0.0, [switch] $ExclusiveMinimum)

    $requirementText = if ($ExclusiveMinimum) {
        "finite and greater than $($Minimum.ToString('G17', $culture))"
    } else {
        "finite and greater than or equal to $($Minimum.ToString('G17', $culture))"
    }

    if ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) {
        throw "Case '$Case' $SourceLabel $Metric must be $requirementText."
    }

    if ($ExclusiveMinimum) {
        if ($Value -le $Minimum) { throw "Case '$Case' $SourceLabel $Metric must be $requirementText." }
    } elseif ($Value -lt $Minimum) {
        throw "Case '$Case' $SourceLabel $Metric must be $requirementText."
    }

    return $Value
}

function Convert-DurationToNanoseconds {
    param([string] $Value, [string] $Case, [string] $SourceLabel)

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -notmatch '^([+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\s*(ns|us|μs|µs|ms|s)$') {
        throw "Case '$Case' $SourceLabel Mean value '$Value' is not a supported duration."
    }

    $number = Parse-InvariantDoubleLiteral -Value $Matches[1] -Case $Case -SourceLabel $SourceLabel -Metric 'Mean'
    $multiplier = switch ($Matches[2]) {
        'ns' { 1.0; break }
        'us' { 1e3; break }
        'μs' { 1e3; break }
        'µs' { 1e3; break }
        'ms' { 1e6; break }
        's' { 1e9; break }
        default { throw "Unsupported duration unit '$($Matches[2])'." }
    }

    return Assert-ValidMetricValue -Value ($number * $multiplier) -Case $Case -SourceLabel $SourceLabel -Metric 'Mean' -ExclusiveMinimum
}

function Convert-Bytes {
    param([string] $Value, [string] $Case, [string] $SourceLabel)

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -eq '-') { return 0.0 }
    if ($normalized -notmatch '^([+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)\s*(B|KB|MB|GB)$') {
        throw "Case '$Case' $SourceLabel Allocated value '$Value' is not a supported allocation."
    }

    $number = Parse-InvariantDoubleLiteral -Value $Matches[1] -Case $Case -SourceLabel $SourceLabel -Metric 'Allocated'
    $multiplier = switch ($Matches[2]) {
        'B' { 1.0; break }
        'KB' { 1024.0; break }
        'MB' { 1024.0 * 1024.0; break }
        'GB' { 1024.0 * 1024.0 * 1024.0; break }
        default { throw "Unsupported allocation unit '$($Matches[2])'." }
    }

    return Assert-ValidMetricValue -Value ($number * $multiplier) -Case $Case -SourceLabel $SourceLabel -Metric 'Allocated'
}

function Convert-Gen0 {
    param([string] $Value, [string] $Case, [string] $SourceLabel)

    $normalized = $Value.Trim().Replace(',', '')
    if ($normalized -eq '-') { return 0.0 }

    $parsed = Parse-InvariantDoubleLiteral -Value $normalized -Case $Case -SourceLabel $SourceLabel -Metric 'Gen0'
    return Assert-ValidMetricValue -Value $parsed -Case $Case -SourceLabel $SourceLabel -Metric 'Gen0'
}

$baselineOpsPerSecond = Assert-ValidMetricValue -Value (1e9 / $baselineMeanNs) -Case $caseName -SourceLabel 'baseline' -Metric 'operations per second' -ExclusiveMinimum
$replacementOpsPerSecond = Assert-ValidMetricValue -Value (1e9 / $replacementMeanNs) -Case $caseName -SourceLabel 'replacement' -Metric 'operations per second' -ExclusiveMinimum

$geometricMeanRatio = [Math]::Exp(
    ($comparisons |
        ForEach-Object { [Math]::Log($_.ReplacementMeanNs) - [Math]::Log($_.BaselineMeanNs) } |
        Measure-Object -Average).Average)
```

Keep the existing `$faster` expression unchanged. Keep the ratified blocking wording. Continue using the tiny explicit comparison tolerance, but add a NaN guard for any derived percent that should never happen with valid finite raw inputs:

```powershell
function Test-PercentExceedsLimit {
    param([double] $Value, [double] $Limit, [string] $Label = 'Comparison metric')

    if ([double]::IsNaN($Value)) {
        throw "$Label is NaN."
    }

    return ($Value - $Limit) -gt 1e-9
}
```

Replace the existing single summary statement
`$lines.Add('Thresholds: Faster requires ...')`; do not append a second,
conflicting threshold summary. Its replacement is exactly:

```powershell
function Format-LimitPercentValue {
    param([double] $Value)

    return $Value.ToString('G17', $culture)
}

$lines.Add("Acceptance limits: geometric-mean mean-time regression <= $(Format-LimitPercentValue -Value $MaximumGeometricMeanRegressionPercent)%; each individual case mean regression <= $(Format-LimitPercentValue -Value $MaximumMeanRegressionPercent)%; each individual case allocated-byte regression <= $(Format-LimitPercentValue -Value $MaximumAllocationRegressionPercent)%; each individual case Gen0 regression <= $(Format-LimitPercentValue -Value $MaximumGen0RegressionPercent)%.")
$lines.Add('Faster remains stricter: geometric mean time <= -5%, no per-case mean > +5%, and no allocation or Gen0 increase.')
```

- [ ] **Step 4: Test raw-metric validation, boundary acceptance, tolerance-adjacent behavior, just-over blocking, and custom-limit wiring**

Run:

```powershell
pwsh -NoProfile -File tools/benchmarks/tests/Test-Compare-SearchParserBenchmarks.ps1
pwsh -NoProfile -File tools/benchmarks/Compare-SearchParserBenchmarks.ps1 -BaselineCsv docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv -ReplacementCsv docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv -CorrectnessStatus Passed -OutputPath "$env:TEMP/ignixa-identical-search-parser-comparison.md"
```

Expected: both commands exit 0. The pure-PowerShell test proves:
- all four configurable limits reject `-1`, `NaN`, and positive infinity with exit code 1 and no report file;
- raw baseline/replacement metrics reject before report write when Mean is non-positive or non-finite, when Allocated is negative or non-finite, and when Gen0 is negative or non-finite;
- derived baseline/replacement operations per second reject before report write when a tiny but finite positive Mean would overflow to `+∞`;
- a committed-baseline-derived zero allocation or Gen0 still yields a blocking `+∞%` delta rather than being rejected as invalid raw input;
- the exact default boundaries are accepted;
- a value just inside the 1e-9 comparison tolerance accepts, while a value just outside blocks;
- opposing extreme but finite mean ratios still report a stable geometric mean of `0.00%` because the geometric-mean term uses `Log(replacement) - Log(baseline)` rather than `Log(replacement / baseline)`;
- the just-over default scenarios exit 1, emit reports with `Blocking regression: Yes`, use the ratified-limit acceptance wording, and include the intended case or geometric-mean evidence plus the configured limit;
- the case-specific `Mean Δ`, `Allocation Δ`, and `Gen0 Δ` assertions read the correct Markdown column rather than scanning the whole report;
- stricter custom limits block values that still pass under the defaults, proving each parameter is wired rather than hardcoded;
- a tiny configured limit such as `1e-16` is rendered nonzero via invariant `G17` formatting in the acceptance-limits summary;
- the identical comparison reports `Blocking regression: No`, geometric mean `0.00%`, and is not classified `Faster`.

### Task 8: Run the final benchmark and enforce acceptance

**Files:**
- Verify unchanged: `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj`
- Verify unchanged: `bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs`
- Verify unchanged: `bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs`
- Verify unchanged: `bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs`
- Create from output: `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.csv`
- Create from output: `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.md`
- Create from comparison: `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md`
- Retain unchanged: `docs/features/search/benchmarks/2026-07-10-superpower-parser.csv`
- Retain unchanged: `docs/features/search/benchmarks/2026-07-10-superpower-parser.md`
- Retain unchanged: `docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md`

- [ ] **Step 1: Revalidate correctness and the locked harness**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-restore
$manifest = 'docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256'
$mismatches = foreach ($line in Get-Content -LiteralPath $manifest) {
    $parts = $line -split '\s{2}', 2
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $parts[1]).Hash
    if ($actual -ne $parts[0]) { $parts[1] }
}
if ($mismatches) { throw "Benchmark harness changed: $($mismatches -join ', ')" }
```

Expected: focused tests report `Failed: 0`; hash validation emits no output.

- [ ] **Step 2: Build and run the final six-case benchmark**

Run:

```powershell
dotnet build bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-restore
dotnet run --project bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-build -- --filter "*SearchExpressionParserBenchmarks*" --artifacts "BenchmarkDotNet.Artifacts/search-parser-handwritten-syntax-final" --launchCount 1 --warmupCount 5 --iterationCount 15
```

Expected: BenchmarkDotNet completes exactly six rows without exceptions and emits Mean, Gen0, and Allocated for every case. No expected performance result is assumed.

- [ ] **Step 3: Copy final raw artifacts with the ratified names**

Run:

```powershell
$results = 'BenchmarkDotNet.Artifacts/search-parser-handwritten-syntax-final/results'
$destination = 'docs/features/search/benchmarks'
Copy-Item -LiteralPath "$results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report.csv" -Destination "$destination/2026-07-11-handwritten-syntax-parser.csv"
Copy-Item -LiteralPath "$results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report-github.md" -Destination "$destination/2026-07-11-handwritten-syntax-parser.md"
```

Expected: both 2026-07-11 files exist and the CSV contains exactly the six expected case names.

- [ ] **Step 4: Verify matching benchmark environments**

Run:

```powershell
$baselineHeader = (Get-Content docs/features/search/benchmarks/2026-07-10-handwritten-parser.md -TotalCount 10) -join "`n"
$replacementHeader = (Get-Content docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.md -TotalCount 10) -join "`n"
if ($baselineHeader -ne $replacementHeader) {
    throw 'Benchmark environments differ; rerun both measurements in one environment before comparison.'
}
```

Expected: no output and exit code 0. If the runtime, OS, CPU, SDK, job, launch count, warmup count, or iteration count differs, stop rather than compare unlike environments.

- [ ] **Step 5: Compare against the original handwritten baseline**

Run:

```powershell
pwsh -NoProfile -File tools/benchmarks/Compare-SearchParserBenchmarks.ps1 `
    -BaselineCsv docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv `
    -ReplacementCsv docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.csv `
    -CorrectnessStatus Passed `
    -OutputPath docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md `
    -MaximumGeometricMeanRegressionPercent 10 `
    -MaximumMeanRegressionPercent 20 `
    -MaximumAllocationRegressionPercent 25 `
    -MaximumGen0RegressionPercent 25
```

Expected on acceptance: exit code 0, `Correctness: Passed`, `Blocking regression: No`, all six cases, geometric-mean change, and per-case Mean/Allocated/Gen0 deltas.

Expected on violation: exit code 1 and `Blocking regression: Yes`. Stop the plan. Investigate the measurements and implementation; do not update public docs or claim completion. Proceed only after the user explicitly accepts the measured regression, then rerun with `-AcceptBlockingRegression`. Approval to commit code does not satisfy this requirement.

- [ ] **Step 6: Enforce comparison completeness and honest claims**

Run:

```powershell
$report = Get-Content docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md -Raw
foreach ($case in @('Simple', 'Modified', 'TypedChain', 'NestedReverseChain', 'EscapedAlternative', 'Composite')) {
    if ($report -notmatch "\|\s*$case\s*\|") { throw "Missing benchmark case $case" }
}
foreach ($metric in @('Mean Δ', 'Allocation Δ', 'Gen0 Δ', 'Geometric mean time change')) {
    if (-not $report.Contains($metric)) { throw "Missing metric $metric" }
}
if (-not $report.Contains('**Correctness:** **Passed**')) { throw 'Correctness is not Passed.' }
if ($report.Contains('**Blocking regression:** **Yes**') -and
    -not $report.Contains('Accepted only because -AcceptBlockingRegression')) {
    throw 'Unaccepted benchmark violation blocks completion.'
}
```

Expected: no output and exit code 0 before documentation work starts.

### Task 9: Update public and feature documentation only after acceptance

**Files:**
- Modify: `docs/features/search/investigations/superpower-search-expression-parser.md`
- Modify: `docs/features/search/readme.md:18-23`
- Modify: `docs/site/docs/core-sdk/search.md`
- Verify: `docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md`

- [ ] **Step 1: Record the measured result without changing historical evidence**

Update the investigation's revised decision with:

```markdown
The feature owner ratified revised Option 3 on 2026-07-11. The Superpower tokenizer/grammar layer remains rejected; the immutable syntax records, binders, facades, canonical atomic parsing, and characterization/parity/binder tests are retained. The replacement uses direct handwritten source scanners with positioned diagnostics and no token-list intermediate.
```

Add the final comparison link and copy its actual classification and acceptance wording. Do not alter the 2026-07-10 Superpower table or artifacts.

- [ ] **Step 2: Update the feature table accurately**

Use `Implemented` only if correctness and performance are accepted:

```markdown
| [superpower-search-expression-parser](investigations/superpower-search-expression-parser.md) | Implemented (revised Option 3) | Handwritten syntax scanners emit the retained immutable syntax model; semantic binders and public parser facades remain unchanged, with measured results recorded against the original baseline |
```

If the user accepted a benchmark violation, state that explicitly in the summary instead of implying the limits passed.

- [ ] **Step 3: Document the positioned syntax diagnostic**

Add or update the search parsing section in `docs/site/docs/core-sdk/search.md`:

```markdown
## Search expression parsing

`IExpressionParser` remains the entry point used by `SearchOptionsBuilder`. Parser instances are created per tenant and FHIR version, so `SearchParameterInfo` lookup and reference-target validation use the active definition manager and schema.

Handwritten syntax scanners parse ordinary parameters, modifiers, typed forward chains, nested `_has`, include/revinclude forms, `_not-referenced`, escaped separators (`\,`, `\$`, `\|`, `\\`), comma alternatives, dollar composites, comparator prefixes, `:missing`, `:text`, and `:of-type`. The scanners emit immutable syntax records; semantic binders remain the only schema-aware layer.

Malformed key or value syntax raises `InvalidSearchOperationException` with a positioned line/column diagnostic. Semantic failures retain the existing `SearchParameterNotSupportedException`, `BadSearchRequestException`, and resource-backed `InvalidSearchOperationException` messages. Atomic date, number, quantity, reference, string, token, and URI conversion continues to use the existing `*SearchValue.Parse` implementations.

The mandatory BenchmarkDotNet result and acceptance decision are recorded in [the handwritten syntax parser comparison](../../../features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md). The comparison uses the unchanged public-facade harness and six inputs against the original handwritten baseline.
```

Append a performance sentence matching the report:

- Use `The replacement was classified as **Faster** under the stricter documented criteria.` only when the comparison says `Classification: Faster`.
- Otherwise use `The replacement was not classified as **Faster**; no speedup is claimed.` and state whether the ratified limits passed or required explicit user acceptance.

- [ ] **Step 4: Verify documentation wording**

Run:

```powershell
rg "Superpower line/column|key-only|expect near-baseline|estimated 2-3x|estimated 2–3x|open for the feature owner to ratify" docs/features/search/investigations/superpower-search-expression-parser.md docs/features/search/readme.md docs/site/docs/core-sdk/search.md
rg "2026-07-11-handwritten-syntax-parser-comparison|positioned line/column diagnostic|ratified revised Option 3" docs/features/search/investigations/superpower-search-expression-parser.md docs/site/docs/core-sdk/search.md
```

Expected: the first command has no matches and exits 1; the second finds the comparison link, positioned diagnostic wording, and ratification record.

- [ ] **Step 5: Request approval for benchmark/documentation commit**

Run:

```powershell
git --no-pager diff -- docs/features/search/benchmarks docs/features/search/investigations/superpower-search-expression-parser.md docs/features/search/readme.md docs/site/docs/core-sdk/search.md tools/benchmarks
git status --short
```

Proposed subject: `Validate handwritten search parser performance`

Ask for explicit approval. Only after approval, stage the exact Task 7-9 paths and run:

```powershell
git add tools/benchmarks/Compare-SearchParserBenchmarks.ps1 tools/benchmarks/tests/Test-Compare-SearchParserBenchmarks.ps1 docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.csv docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.md docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md docs/features/search/investigations/superpower-search-expression-parser.md docs/features/search/readme.md docs/site/docs/core-sdk/search.md
git commit -m "Validate handwritten search parser performance"
```

### Task 10: Run full validation and final architecture audit

**Files:**
- Verify: `All.sln`
- Verify: `src/Core/Ignixa.Search/Expressions/Parsers/`
- Verify: `src/Core/Ignixa.Search/Ignixa.Search.csproj`
- Verify: `src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs`
- Verify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/`
- Verify: `docs/features/search/benchmarks/`

- [ ] **Step 1: Restore and build the complete solution**

Run:

```powershell
dotnet restore All.sln
dotnet build All.sln --no-restore
```

Expected: restore succeeds or is up-to-date; build reports `Build succeeded.`, `0 Warning(s)`, and `0 Error(s)`. A build failure is not covered by the known test caveats and blocks completion.

- [ ] **Step 2: Re-run the focused parser suite**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-build
```

Expected: `Passed! - Failed: 0`. Any focused failure is parser-caused until proven otherwise and blocks completion.

- [ ] **Step 3: Run the full solution tests and classify known baseline failures**

Run:

```powershell
$output = dotnet test All.sln --no-build 2>&1
$exitCode = $LASTEXITCODE
$output | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0) {
    $knownBuckets = @(
        'IdentifierOfTypeIndexing',
        'sql-on-fhir-tests',
        'Ignixa.SqlOnFhir.Tests',
        'Ignixa.RepoGuards.Tests'
    )
    foreach ($bucket in $knownBuckets) {
        if ($output -match [regex]::Escape($bucket)) {
            Write-Host "Observed documented baseline bucket: $bucket"
        }
    }
    Write-Host 'The full suite is not green. Compare every failed test name with the documented baseline buckets before continuing.'
}
```

Expected in the current environment: the command may exit non-zero only for unchanged `IdentifierOfTypeIndexingTests` FHIRPath behavior, `SqlOnFhirReportCoverageTests`/official-suite tests when the SQL-on-FHIR submodule is absent, and RepoGuards repository-root detection in a worktree. Record those failures explicitly; do not report the full suite as green. Any other failure, or any changed failure in those areas caused by this branch, blocks completion.

- [ ] **Step 4: Run cross-version compatibility validation**

Run:

```powershell
pwsh -File .\run-compat-tests.ps1
```

Expected: exit code 0 and no parser-related compatibility failure across configured FHIR versions. If environment prerequisites prevent execution, record the exact prerequisite failure separately; do not convert it into a pass.

- [ ] **Step 5: Prove no Superpower parser production/package use remains in Ignixa.Search**

Run:

```powershell
rg "Superpower|SearchKeyTokenizer|SearchValueTokenizer|SearchKeyGrammar|SearchValueGrammar|SearchParseExceptionMapper|SearchKeyTokenKind|SearchValueTokenKind" src/Core/Ignixa.Search/Expressions/Parsers src/Core/Ignixa.Search/Ignixa.Search.csproj
```

Expected: no matches and exit code 1.

Then run:

```powershell
rg "PackageReference Include=\"Superpower\"" src/Core/Ignixa.FhirPath/Ignixa.FhirPath.csproj src/Core/Ignixa.FhirMappingLanguage/Ignixa.FhirMappingLanguage.csproj
```

Expected: two matches, proving the central version remains required outside `Ignixa.Search`.

- [ ] **Step 6: Prove syntax/semantic separation and naming rules**

Run:

```powershell
rg "TrySplit|TryConsume|Tokenizer|TokenList|ISearchParameterDefinitionManager|IFhirSchemaProvider|SearchParameterInfo" src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs
rg "\bs_[A-Za-z]" src/Core/Ignixa.Search/Expressions/Parsers/SearchSyntaxExceptionFactory.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs
rg "Copyright \(c\) Ignixa Contributors" src/Core/Ignixa.Search/Expressions/Parsers/SearchSyntaxExceptionFactory.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeySyntaxParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueSyntaxParser.cs
```

Expected: the first two commands have no matches and exit 1; the attribution command finds all three new production files. `SearchValueSyntaxParser` may select syntax by `SearchParamType` and `SearchModifierCode`, but it must not resolve schemas, definitions, or modifier support.

- [ ] **Step 7: Prove canonical atomic parsing and public contracts are unchanged**

Run:

```powershell
rg "DateTimeSearchValue.Parse|NumberSearchValue.Parse|QuantitySearchValue.Parse|referenceParser.Parse|StringSearchValue.Parse|TokenSearchValue.Parse|UriSearchValue.Parse" src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs
git --no-pager diff 02eb4a5 -- src/Core/Ignixa.Search/Expressions/Parsers/IExpressionParser.cs src/Core/Ignixa.Search/Expressions/Parsers/ISearchParameterExpressionParser.cs src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs
rg "new SearchParameterExpressionParser|new ExpressionParser|GetSearchParameterDefinitionManager\(fhirVersion, tenantId\)" src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs
```

Expected: all seven canonical parser dispatches are found; the `git diff` is empty; construction search finds tenant-specific definition lookup and the same two public facade constructors.

- [ ] **Step 8: Revalidate final benchmark acceptance and retained evidence**

Run:

```powershell
$report = Get-Content docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md -Raw
if (-not $report.Contains('**Correctness:** **Passed**')) { throw 'Correctness failed.' }
if ($report.Contains('**Blocking regression:** **Yes**') -and
    -not $report.Contains('Accepted only because -AcceptBlockingRegression')) {
    throw 'Performance remains unaccepted.'
}
Get-Item docs/features/search/benchmarks/2026-07-10-superpower-parser.csv, docs/features/search/benchmarks/2026-07-10-superpower-parser.md, docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md, docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.csv, docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser.md, docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md
```

Expected: no exception; all six historical/final evidence files are present.

- [ ] **Step 9: Inspect the complete change set**

Run:

```powershell
git --no-pager diff --stat 02eb4a5
git --no-pager diff --check
git status --short
```

Expected: `git diff --check` emits no output. The change set contains only the planned parser layer, tests, benchmark tooling/artifacts, and documentation, plus any explicitly preserved pre-existing user edits.

- [ ] **Step 10: Request approval for any verification-fix commit**

If validation required file changes, show their diff and propose `Complete handwritten search parser verification`. Commit only after explicit user approval:

```powershell
git commit -m "Complete handwritten search parser verification"
```

If validation changed no files, state that no verification commit is needed. Never stage or commit merely because this plan contains a checkpoint.
