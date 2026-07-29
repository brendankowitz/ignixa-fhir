# FML Parser Conformance and Oracle Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the six defects that make Ignixa's FML parser reject 100% of real-world `.fml`/`.map` files, then wire the official HL7 `fhir-test-cases` structure-mapping corpus in as a permanent oracle-test suite.

**Architecture:** Two halves executed in order. Part A is pure lexer/grammar work in `src/Core/Ignixa.FhirMappingLanguage/` — each defect gets a hand-written unit test first, then the minimal fix. Part B vendors the HL7 `fhir-test-cases` zip into `test/Ignixa.FhirMappingLanguage.Tests/` via the same MSBuild download target already proven in `Ignixa.FhirPath.Tests`, then adds a manifest-driven `[Theory]` runner that executes each `<fml-tests>` case end-to-end and compares against the reference-produced expected resource with canonical JSON equality. Out-of-scope cases (CDA logical-model output) go on a frozen exclusion list with written rationale, per `docs/adr/adr-2607-validation-oracle-conformance.md`.

**Tech Stack:** .NET 9/10, C#, Superpower (lexer/parser combinators), xUnit + Shouldly, `System.Text.Json` / `JsonNode`, MSBuild `DownloadFile`/`Unzip` tasks, `Ignixa.Specification` schema providers, `Ignixa.FhirPath`.

**Input spec:** `docs/features/structuremap/investigations/fml-oracle-conformance-corpus.md`

---

## Background: what is actually broken

A throwaway probe fed every file from two corpora through `MappingParser.Parse()`:

| Corpus | Files | Parsed |
|---|---|---|
| `brianpos/fhir-r6-maps` | 355 `.fml` | **0** |
| `FHIR/fhir-test-cases` `r4b`+`r5` `structure-mapping` | 27 `.map` | **0** |

Six root causes, in dependency order:

| # | Defect | Symptom | Blast radius |
|---|---|---|---|
| 1 | Tokenizer has no `+ - % / \| & <= >=` tokens | `Syntax error: unexpected '+'` at the *lexer* | blocks everything downstream |
| 2 | `FhirPathExpression` re-joins tokens with `string.Join("")` | `linkId.value in ('x')` becomes `linkId.valuein('x')` | silently corrupts embedded FHIRPath |
| 3 | `"..."` lexes as `DelimitedIdentifier`, not a string literal | `map "url" = "name"` fails at line 1 col 5 | 21 of 27 official cases (~78%) |
| 4 | No `<<types>>` / `<<type+>>` group annotation | `unexpected '+'` / `unexpected '<'` | all cross-version maps |
| 5 | No `///` metadata declarations; `map` header mandatory | `unexpected uses 'uses', expected map` | all R6-form maps |
| 6 | Wildcard `imports ".../*4to6"` not resolvable | runtime, not parse | deferred — see Follow-ups |

Defects 1-5 are in scope. Defect 6 is deferred (it is an `ImportResolver` concern, not a parser concern, and no in-scope oracle case needs it).

---

## File Structure

### Part A — parser (production code)

| File | Action | Responsibility |
|---|---|---|
| `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenKind.cs` | Modify | Add `Plus`, `Minus`, `Percent`, `Slash`, `Pipe`, `Ampersand`, `LessOrEqual`, `GreaterOrEqual`, `DoubleQuotedString`, `MetadataLine` |
| `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs` | Modify | Two near-identical builders (`CreateWithTrivia()` and `Create()`) — **every change must be applied to both** |
| `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs` | Modify | String-literal widening, source-text reconstruction, group type-mode rule, metadata + optional `map` header |
| `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingParser.cs` | Modify | Guard against a "successfully parsed nothing" map |
| `src/Core/Ignixa.FhirMappingLanguage/Expressions/GroupTypeMode.cs` | Create | Enum for `<<types>>` / `<<type+>>` |
| `src/Core/Ignixa.FhirMappingLanguage/Expressions/GroupExpression.cs` | Modify | Carry `TypeMode` |
| `src/Core/Ignixa.FhirMappingLanguage/Expressions/MapExpression.cs` | Modify | Carry `Metadata` dictionary |
| `src/Core/Ignixa.FhirMappingLanguage/Serialization/FmlSerializer.cs` | Modify | Round-trip the new syntax |

### Part A — parser (tests)

| File | Action | Responsibility |
|---|---|---|
| `test/Ignixa.FhirMappingLanguage.Tests/Lexer/MappingTokenizerTests.cs` | Modify | Update the `"quoted"` → `DelimitedIdentifier` assertion; add operator-token cases |
| `test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs` | Create | One focused test per defect, using the smallest reproducing snippet |

### Part B — oracle harness

| File | Action | Responsibility |
|---|---|---|
| `test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj` | Modify | `DownloadFhirTestCases` MSBuild target |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTestCasesLocator.cs` | Create | Resolve the vendored corpus root; single source of truth for paths |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlOracleCase.cs` | Create | Immutable record for one manifest case |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlOracleExclusions.cs` | Create | Frozen exclusion list + rationale |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlManifestLoader.cs` | Create | Parse `<fml-tests>` out of `manifest.xml` |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/CanonicalJson.cs` | Create | Order-insensitive JSON canonicalisation for comparison |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlCorpusParseTests.cs` | Create | Parse-rate gate over every `.map` in the corpus |
| `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTransformOracleTests.cs` | Create | The manifest-driven end-to-end transform oracle |

### Docs

| File | Action |
|---|---|
| `docs/features/structuremap/investigations/fml-oracle-conformance-corpus.md` | Modify — record measured outcome, set Verdict |

---

## Conventions you must follow

From `AGENTS.md` / `CLAUDE.md` in this repo:

- One type per file. No `#region`.
- Test naming: `GivenContext_WhenAction_ThenResult`. AAA layout. xUnit + Shouldly.
- 4-space indent, file-scoped namespaces, nullable enabled, warnings-as-errors.
- Async parameters named `cancellationToken`, never `ct`.
- **No inline comments** unless the surrounding file already uses them for the same purpose. The tokenizer/grammar files do use structural comments — match their existing style there.
- **Never `git commit` without explicit user approval.** Each task below ends with a commit step; run it only once the user has approved that batch.

**Critical gotcha:** `test/Ignixa.FhirMappingLanguage.Tests/` has **no global usings**. Every new test file needs explicit `using Xunit;` and `using Shouldly;`.

**Verified API facts** (corrected during Task 2 — earlier drafts of this plan got these wrong):

- `MappingParser.Parse` is an **instance** method, not static. Call it as `new MappingParser().Parse(fml)`.
- A source condition is `SourceExpression.Condition`, typed `Expression?`. To read the FHIRPath text, cast and use `PathExpression`: `((FhirPathExpression)source.Condition!).PathExpression`. There is no `.Expression` property.
- `MapExpression.Groups`, `GroupExpression.Rules`, and `RuleExpression.Sources` are all `IReadOnlyList<T>` and named as this plan assumes.
- `ImplicitUsings` is enabled in the test project, which covers `System.*` — but **not** `Xunit` or `Shouldly`, which still need explicit usings (see gotcha above).

Any other member name quoted in this plan is an assumption, not a verified fact. Open the source and confirm before relying on it; report the correction rather than working around it silently.

**Critical gotcha:** `MappingTokenizer.cs` contains **two near-identical tokenizer builders** — `CreateWithTrivia()` (lines 24-111) and `Create()` (lines 117-204). Every tokenizer change must be applied to **both**. Because the operator blocks are byte-identical between them, a naive `edit` anchor will fail with "not unique" — the replacements below include enough trailing context (`.Build();` vs `// Whitespace (ignore for standard parsing)`) to disambiguate.

---

# Part A — Parser defects

## Task 1: Operator tokens (`+ - % / | & <= >=`)

The tokenizer has no rule for these characters, so Superpower fails at the *lexing* stage before the grammar is ever consulted. Errors look like `Syntax error (line 12, column 91): unexpected '+'`.

Real occurrences:
- `qr2patfordates.map:9` — `tgt.birthDate = (%value + 5 days)`
- `syntax.map:17` — `('urn:uuid:' + r.lower())`
- tutorial `whereclause` — `a2.length <= 20`
- cross-version maps — `<<type+>>`

**Files:**
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenKind.cs:83` (after `RightBracket`)
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs:86-110` and `:176-201`
- Test: `test/Ignixa.FhirMappingLanguage.Tests/Lexer/MappingTokenizerTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `test/Ignixa.FhirMappingLanguage.Tests/Lexer/MappingTokenizerTests.cs`, inside the existing class, before the closing brace:

```csharp
    [Theory]
    [InlineData("+", MappingTokenKind.Plus)]
    [InlineData("-", MappingTokenKind.Minus)]
    [InlineData("%", MappingTokenKind.Percent)]
    [InlineData("/", MappingTokenKind.Slash)]
    [InlineData("|", MappingTokenKind.Pipe)]
    [InlineData("&", MappingTokenKind.Ampersand)]
    [InlineData("<=", MappingTokenKind.LessOrEqual)]
    [InlineData(">=", MappingTokenKind.GreaterOrEqual)]
    public void GivenAnArithmeticOperator_WhenTokenizing_ThenTheOperatorTokenIsProduced(string input, MappingTokenKind expected)
    {
        var tokenizer = MappingTokenizer.Create();

        var tokens = tokenizer.Tokenize(input).ToList();

        tokens.Count.ShouldBe(1);
        tokens[0].Kind.ShouldBe(expected);
    }

    [Fact]
    public void GivenAPercentConstantWithDateArithmetic_WhenTokenizing_ThenAllTokensAreProduced()
    {
        var tokenizer = MappingTokenizer.Create();

        var kinds = tokenizer.Tokenize("%value + 5 days").Select(t => t.Kind).ToList();

        kinds.ShouldBe(new[]
        {
            MappingTokenKind.Percent,
            MappingTokenKind.Identifier,
            MappingTokenKind.Plus,
            MappingTokenKind.IntegerLiteral,
            MappingTokenKind.Identifier
        });
    }

    [Fact]
    public void GivenAnArrow_WhenTokenizing_ThenItIsStillASingleArrowNotMinus()
    {
        var tokenizer = MappingTokenizer.Create();

        var kinds = tokenizer.Tokenize("src -> tgt").Select(t => t.Kind).ToList();

        kinds.ShouldBe(new[]
        {
            MappingTokenKind.Identifier,
            MappingTokenKind.Arrow,
            MappingTokenKind.Identifier
        });
    }

    [Fact]
    public void GivenALineComment_WhenTokenizingWithTrivia_ThenItIsStillACommentNotTwoSlashes()
    {
        var tokenizer = MappingTokenizer.CreateWithTrivia();

        var kinds = tokenizer.Tokenize("// hello").Select(t => t.Kind).ToList();

        kinds.ShouldBe(new[] { MappingTokenKind.LineComment });
    }
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~MappingTokenizerTests"
```

Expected: build error `CS0117: 'MappingTokenKind' does not contain a definition for 'Plus'`.

- [ ] **Step 3: Add the token kinds**

In `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenKind.cs`, replace:

```csharp
    LeftBracket,         // [
    RightBracket,        // ]
```

with:

```csharp
    LeftBracket,         // [
    RightBracket,        // ]

    // Arithmetic and FHIRPath operators (appear inside embedded FHIRPath and in <<type+>>)
    Plus,                // +
    Minus,               // -
    Percent,             // % (FHIRPath environment variable prefix)
    Slash,               // /
    Pipe,                // |
    Ampersand,           // &
    LessOrEqual,         // <=
    GreaterOrEqual,      // >=
```

- [ ] **Step 4: Patch `CreateWithTrivia()`**

In `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs`, replace this block (it is the one that ends in `.Build();` — that trailing context makes the anchor unique):

```csharp
            .Match(Span.EqualTo("<-"), MappingTokenKind.LeftArrow)

            // Single-character operators and delimiters
            .Match(Character.EqualTo('='), MappingTokenKind.Equals)
            .Match(Character.EqualTo(':'), MappingTokenKind.Colon)
            .Match(Character.EqualTo('.'), MappingTokenKind.Dot)
            .Match(Character.EqualTo('*'), MappingTokenKind.Asterisk)
            .Match(Character.EqualTo(','), MappingTokenKind.Comma)
            .Match(Character.EqualTo(';'), MappingTokenKind.Semicolon)
            .Match(Character.EqualTo('('), MappingTokenKind.LeftParen)
            .Match(Character.EqualTo(')'), MappingTokenKind.RightParen)
            .Match(Character.EqualTo('{'), MappingTokenKind.LeftBrace)
            .Match(Character.EqualTo('}'), MappingTokenKind.RightBrace)
            .Match(Character.EqualTo('<'), MappingTokenKind.LeftAngle)
            .Match(Character.EqualTo('>'), MappingTokenKind.RightAngle)
            .Match(Character.EqualTo('['), MappingTokenKind.LeftBracket)
            .Match(Character.EqualTo(']'), MappingTokenKind.RightBracket)

            .Build();
```

with:

```csharp
            .Match(Span.EqualTo("<-"), MappingTokenKind.LeftArrow)
            .Match(Span.EqualTo("<="), MappingTokenKind.LessOrEqual)
            .Match(Span.EqualTo(">="), MappingTokenKind.GreaterOrEqual)

            // Single-character operators and delimiters
            .Match(Character.EqualTo('='), MappingTokenKind.Equals)
            .Match(Character.EqualTo(':'), MappingTokenKind.Colon)
            .Match(Character.EqualTo('.'), MappingTokenKind.Dot)
            .Match(Character.EqualTo('*'), MappingTokenKind.Asterisk)
            .Match(Character.EqualTo(','), MappingTokenKind.Comma)
            .Match(Character.EqualTo(';'), MappingTokenKind.Semicolon)
            .Match(Character.EqualTo('('), MappingTokenKind.LeftParen)
            .Match(Character.EqualTo(')'), MappingTokenKind.RightParen)
            .Match(Character.EqualTo('{'), MappingTokenKind.LeftBrace)
            .Match(Character.EqualTo('}'), MappingTokenKind.RightBrace)
            .Match(Character.EqualTo('<'), MappingTokenKind.LeftAngle)
            .Match(Character.EqualTo('>'), MappingTokenKind.RightAngle)
            .Match(Character.EqualTo('['), MappingTokenKind.LeftBracket)
            .Match(Character.EqualTo(']'), MappingTokenKind.RightBracket)

            // Arithmetic / FHIRPath operators (after multi-character forms so -> and <= win)
            .Match(Character.EqualTo('+'), MappingTokenKind.Plus)
            .Match(Character.EqualTo('-'), MappingTokenKind.Minus)
            .Match(Character.EqualTo('%'), MappingTokenKind.Percent)
            .Match(Character.EqualTo('/'), MappingTokenKind.Slash)
            .Match(Character.EqualTo('|'), MappingTokenKind.Pipe)
            .Match(Character.EqualTo('&'), MappingTokenKind.Ampersand)

            .Build();
```

- [ ] **Step 5: Patch `Create()`**

Replace the equivalent block — the one whose trailing context is the whitespace-ignore comment:

```csharp
            .Match(Span.EqualTo("<-"), MappingTokenKind.LeftArrow)

            // Single-character operators and delimiters
            .Match(Character.EqualTo('='), MappingTokenKind.Equals)
            .Match(Character.EqualTo(':'), MappingTokenKind.Colon)
            .Match(Character.EqualTo('.'), MappingTokenKind.Dot)
            .Match(Character.EqualTo('*'), MappingTokenKind.Asterisk)
            .Match(Character.EqualTo(','), MappingTokenKind.Comma)
            .Match(Character.EqualTo(';'), MappingTokenKind.Semicolon)
            .Match(Character.EqualTo('('), MappingTokenKind.LeftParen)
            .Match(Character.EqualTo(')'), MappingTokenKind.RightParen)
            .Match(Character.EqualTo('{'), MappingTokenKind.LeftBrace)
            .Match(Character.EqualTo('}'), MappingTokenKind.RightBrace)
            .Match(Character.EqualTo('<'), MappingTokenKind.LeftAngle)
            .Match(Character.EqualTo('>'), MappingTokenKind.RightAngle)
            .Match(Character.EqualTo('['), MappingTokenKind.LeftBracket)
            .Match(Character.EqualTo(']'), MappingTokenKind.RightBracket)

            // Whitespace (ignore for standard parsing)
```

with:

```csharp
            .Match(Span.EqualTo("<-"), MappingTokenKind.LeftArrow)
            .Match(Span.EqualTo("<="), MappingTokenKind.LessOrEqual)
            .Match(Span.EqualTo(">="), MappingTokenKind.GreaterOrEqual)

            // Single-character operators and delimiters
            .Match(Character.EqualTo('='), MappingTokenKind.Equals)
            .Match(Character.EqualTo(':'), MappingTokenKind.Colon)
            .Match(Character.EqualTo('.'), MappingTokenKind.Dot)
            .Match(Character.EqualTo('*'), MappingTokenKind.Asterisk)
            .Match(Character.EqualTo(','), MappingTokenKind.Comma)
            .Match(Character.EqualTo(';'), MappingTokenKind.Semicolon)
            .Match(Character.EqualTo('('), MappingTokenKind.LeftParen)
            .Match(Character.EqualTo(')'), MappingTokenKind.RightParen)
            .Match(Character.EqualTo('{'), MappingTokenKind.LeftBrace)
            .Match(Character.EqualTo('}'), MappingTokenKind.RightBrace)
            .Match(Character.EqualTo('<'), MappingTokenKind.LeftAngle)
            .Match(Character.EqualTo('>'), MappingTokenKind.RightAngle)
            .Match(Character.EqualTo('['), MappingTokenKind.LeftBracket)
            .Match(Character.EqualTo(']'), MappingTokenKind.RightBracket)

            // Arithmetic / FHIRPath operators (after multi-character forms so -> and <= win)
            .Match(Character.EqualTo('+'), MappingTokenKind.Plus)
            .Match(Character.EqualTo('-'), MappingTokenKind.Minus)
            .Match(Character.EqualTo('%'), MappingTokenKind.Percent)
            .Match(Character.EqualTo('/'), MappingTokenKind.Slash)
            .Match(Character.EqualTo('|'), MappingTokenKind.Pipe)
            .Match(Character.EqualTo('&'), MappingTokenKind.Ampersand)

            // Whitespace (ignore for standard parsing)
```

**Why `/` is safe:** both builders match `Comment.CStyle` and `Comment.CPlusPlusStyle` as their *first two* rules, so `//` and `/* */` are consumed before the single-`/` rule is ever reached.

**Why URLs are safe:** the `Url` rule (line 81 / 171) is registered before the operator block and its character class permits `+ - % / &`, so `http://x/y?a=1&b=2` still lexes as one `Url` token.

**Do not** add a `<<` token — `<<` must remain two `LeftAngle` tokens for Task 4.

- [ ] **Step 6: Run tests to verify they pass**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~MappingTokenizerTests"
```

Expected: PASS, 0 failures.

- [ ] **Step 7: Run the whole FML suite for regressions**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0
```

Expected: PASS, 0 failures.

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenKind.cs src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs test/Ignixa.FhirMappingLanguage.Tests/Lexer/MappingTokenizerTests.cs
git commit -m "Add arithmetic and comparison operator tokens to FML lexer"
```

---

## Task 2: Reconstruct embedded FHIRPath from source text

`MappingGrammar.cs:139` and `:224` both rebuild the collected FHIRPath sub-expression with `string.Join("", ...)` — **no separator**. Consequences:

| Source | Currently produced | Correct |
|---|---|---|
| `linkId.value in ('patient.sex')` | `linkId.valuein('patient.sex')` | `linkId.value in ('patient.sex')` |
| `%value + 5 days` | `%value+5days` | `%value + 5 days` |

This silently corrupts the FHIRPath handed to the evaluator. `qr2pat-gender.map` — an in-scope oracle case — hits it directly.

The fix reconstructs the original substring from Superpower's span positions rather than re-serialising tokens.

**Files:**
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs:139` and `:224`, plus a new private helper near `CreatePosition`
- Test: `test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.Linq;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Parser;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Parser;

/// <summary>
/// Regression coverage for FML syntax found in the official HL7 structure-mapping corpus.
/// Each test targets one previously unsupported construct.
/// </summary>
public class FmlSyntaxCoverageTests
{
    [Fact]
    public void GivenAWhereClauseWithSpacedOperators_WhenParsing_ThenTheOriginalSpacingIsPreserved()
    {
        const string Fml = """
            map 'http://example.org/Test' = 'Test'

            group Main(source src, target tgt) {
              src.item as item where linkId.value in ('patient.sex') -> tgt.gender = 'x';
            }
            """;

        var map = MappingParser.Parse(Fml);

        var rule = map.Groups[0].Rules[0];
        var condition = rule.Sources[0].Condition;
        condition.ShouldNotBeNull();
        condition!.Expression.ShouldBe("linkId.value in ('patient.sex')");
    }
}
```

> If `RuleSourceExpression`'s condition property is not named `Condition`/`Expression`, open `src/Core/Ignixa.FhirMappingLanguage/Expressions/RuleSourceExpression.cs` and use the actual names. Do not guess.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlSyntaxCoverageTests"
```

Expected: FAIL — actual `linkId.valuein('patient.sex')`.

- [ ] **Step 3: Add the source-text helper**

In `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs`, insert immediately after the `UnescapeIdentifier` method (which ends at line 62):

```csharp
    // Helper: reconstruct the original source text spanned by a token run.
    // Re-serializing tokens loses whitespace, which corrupts embedded FHIRPath
    // (e.g. "linkId.value in (...)" would collapse to "linkId.valuein(...)").
    private static string SourceTextOf(IReadOnlyList<Token<MappingTokenKind>> tokens)
    {
        if (tokens.Count == 0)
        {
            return string.Empty;
        }

        var source = tokens[0].Span.Source;
        if (source is null)
        {
            return string.Join(" ", tokens.Select(t => t.ToStringValue()));
        }

        var start = (int)tokens[0].Position.Absolute;
        var last = tokens[^1];
        var end = (int)last.Position.Absolute + last.Span.Length;
        return source.Substring(start, end - start);
    }
```

Ensure the file has `using System.Collections.Generic;` and `using System.Linq;` (it already uses `List<>` and `.Select`, so both are present or covered by implicit usings — verify before adding duplicates).

- [ ] **Step 4: Use it at the parenthesized site**

Replace `MappingGrammar.cs:138-140`:

```csharp
                            var expr = new FhirPathExpression(
                                string.Join("", tokens.Select(t => t.ToStringValue())),
                                CreatePosition(lparen.Value, lastToken));
```

with:

```csharp
                            var expr = new FhirPathExpression(
                                SourceTextOf(tokens),
                                CreatePosition(lparen.Value, lastToken));
```

**Important:** `tokens` here deliberately excludes both outer parens, while `lastToken` has been reassigned to the closing `)`. Pass `tokens` to `SourceTextOf` (so the parens are not included in the expression text) but keep `lastToken` in `CreatePosition` (so the reported span covers the full `( ... )`). The `tokens.Count == 0` guard in the helper covers the empty `()` case.

- [ ] **Step 5: Use it at the non-parenthesized site**

Replace `MappingGrammar.cs:223-225`:

```csharp
                var expr = new FhirPathExpression(
                    string.Join("", tokens.Select(t => t.ToStringValue())),
                    CreatePosition(tokens[0], lastToken!.Value));
```

with:

```csharp
                var expr = new FhirPathExpression(
                    SourceTextOf(tokens),
                    CreatePosition(tokens[0], lastToken!.Value));
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlSyntaxCoverageTests"
```

Expected: PASS.

- [ ] **Step 7: Run the whole FML suite**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0
```

Expected: PASS. Some existing tests may have asserted the *glued* form — if any fail, they encoded the bug; update them to the spaced form and note it in the commit message.

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs
git commit -m "Preserve original source text when capturing embedded FHIRPath expressions"
```

---

## Task 3: Double-quoted string literals

FML permits both `'...'` and `"..."` for string literals. The tokenizer currently maps `"..."` to `DelimitedIdentifier`, and `MappingGrammar.Map` (line 512) demands a `StringLiteral`, so every official case that opens with `map "http://..." = "cast"` dies at line 1 column 5. This accounts for 21 of the 27 official-case failures.

The fix introduces a distinct `DoubleQuotedString` kind rather than reusing `StringLiteral`, so the parser can keep accepting `"..."` in identifier positions (where the old behaviour allowed it) without losing the ability to treat it as a string in literal positions.

**Files:**
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenKind.cs`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs:73` and `:163`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs:43-51`, `:65-67`, `:91-97`, `:476`
- Modify: `test/Ignixa.FhirMappingLanguage.Tests/Lexer/MappingTokenizerTests.cs:241-255`
- Test: `test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `FmlSyntaxCoverageTests`:

```csharp
    [Fact]
    public void GivenADoubleQuotedMapHeader_WhenParsing_ThenTheUrlAndNameAreRead()
    {
        const string Fml = """
            map "http://hl7.org/fhir/StructureMap/tutorial" = "tutorial"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        var map = new MappingParser().Parse(Fml);

        map.Url.ShouldBe("http://hl7.org/fhir/StructureMap/tutorial");
        map.Identifier.ShouldBe("tutorial");
    }

    [Fact]
    public void GivenADoubleQuotedStringWithAnEscapedQuote_WhenParsing_ThenTheEscapeIsResolved()
    {
        const string Fml = """
            map "http://example.org/T" = "a \" b"

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        var map = new MappingParser().Parse(Fml);

        map.Identifier.ShouldBe("a \" b");
    }
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlSyntaxCoverageTests"
```

Expected: FAIL with a parse exception at line 1, column 5.

- [ ] **Step 3: Add the token kind**

In `MappingTokenKind.cs`, replace:

```csharp
    Identifier,
    DelimitedIdentifier,
    StringLiteral,
```

with:

```csharp
    Identifier,
    DelimitedIdentifier,
    StringLiteral,
    DoubleQuotedString,
```

- [ ] **Step 4: Retarget the double-quote rule in both tokenizer builders**

There are two occurrences of the following two-line block (line 72-73 and line 162-163). **Both** must change, and they are byte-identical — replace them one at a time, verifying the file after each with `grep -n "DoubleQuotedString" src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs` (expect 2 hits at the end).

Old:

```csharp
            .Match(Span.Regex("`[^`]*`"), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)
            .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)
```

New:

```csharp
            .Match(Span.Regex("`[^`]*`"), MappingTokenKind.DelimitedIdentifier, requireDelimiters: false)
            .Match(Span.Regex("\"([^\"\\\\]|\\\\.)*\""), MappingTokenKind.DoubleQuotedString, requireDelimiters: false)
```

Also update the surrounding comment on the preceding line from `// Delimited identifiers (backtick or double-quote style)` to `// Delimited identifiers (backtick) and double-quoted strings`.

- [ ] **Step 5: Extend `UnescapeString`**

In `MappingGrammar.cs`, replace lines 42-51:

```csharp
    // Helper: Unescape string
    private static string UnescapeString(string str)
    {
        if (str.StartsWith('\'') && str.EndsWith('\''))
        {
            str = str.Substring(1, str.Length - 2);
            str = str.Replace("''", "'", StringComparison.Ordinal);
        }
        return str;
    }
```

with:

```csharp
    // Helper: Unescape string
    private static string UnescapeString(string str)
    {
        if (str.StartsWith('\'') && str.EndsWith('\'') && str.Length >= 2)
        {
            str = str.Substring(1, str.Length - 2);
            str = str.Replace("''", "'", StringComparison.Ordinal);
            return str;
        }

        if (str.StartsWith('"') && str.EndsWith('"') && str.Length >= 2)
        {
            str = str.Substring(1, str.Length - 2);
            str = str.Replace("\\\"", "\"", StringComparison.Ordinal)
                     .Replace("\\\\", "\\", StringComparison.Ordinal);
            return str;
        }

        return str;
    }
```

- [ ] **Step 6: Widen the `StringLiteral` grammar rule**

Replace `MappingGrammar.cs:65-67`:

```csharp
    private static readonly TokenListParser<MappingTokenKind, LiteralExpression> StringLiteral =
        Token.EqualTo(MappingTokenKind.StringLiteral)
            .Select(t => new LiteralExpression(UnescapeString(t.ToStringValue()), CreatePosition(t)));
```

with:

```csharp
    private static readonly TokenListParser<MappingTokenKind, LiteralExpression> StringLiteral =
        Token.EqualTo(MappingTokenKind.StringLiteral)
            .Or(Token.EqualTo(MappingTokenKind.DoubleQuotedString))
            .Select(t => new LiteralExpression(UnescapeString(t.ToStringValue()), CreatePosition(t)));
```

- [ ] **Step 7: Keep `"..."` usable in identifier positions**

Replace `MappingGrammar.cs:91-97`:

```csharp
    private static readonly TokenListParser<MappingTokenKind, IdentifierExpression> Identifier =
        Token.EqualTo(MappingTokenKind.Identifier)
            .Or(Token.EqualTo(MappingTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(MappingTokenKind.Type))  // 'type' can be used as property name
            .Or(Token.EqualTo(MappingTokenKind.Default))  // 'default' can be used as property name
            .Or(Token.EqualTo(MappingTokenKind.Prefix))  // 'prefix' can be used as property name
            .Select(t => new IdentifierExpression(UnescapeIdentifier(t.ToStringValue()), CreatePosition(t)));
```

with:

```csharp
    private static readonly TokenListParser<MappingTokenKind, IdentifierExpression> Identifier =
        Token.EqualTo(MappingTokenKind.Identifier)
            .Or(Token.EqualTo(MappingTokenKind.DelimitedIdentifier))
            .Or(Token.EqualTo(MappingTokenKind.DoubleQuotedString))  // "..." is legal where an identifier is expected
            .Or(Token.EqualTo(MappingTokenKind.Type))  // 'type' can be used as property name
            .Or(Token.EqualTo(MappingTokenKind.Default))  // 'default' can be used as property name
            .Or(Token.EqualTo(MappingTokenKind.Prefix))  // 'prefix' can be used as property name
            .Select(t => new IdentifierExpression(UnescapeIdentifier(t.ToStringValue()), CreatePosition(t)));
```

`UnescapeIdentifier` (lines 54-62) already strips both backticks and double quotes, so it needs no change.

- [ ] **Step 8: Fix the ConceptMap declaration id**

Open `MappingGrammar.cs` around line 476 (`ConceptMapDeclaration`). If it references `Token.EqualTo(MappingTokenKind.DelimitedIdentifier)` directly rather than going through the `Identifier` parser, add `.Or(Token.EqualTo(MappingTokenKind.DoubleQuotedString))` alongside it. If it already uses `Identifier`, no change is needed — record which case applied.

- [ ] **Step 9: Update the tokenizer test that encoded the old behaviour**

`test/Ignixa.FhirMappingLanguage.Tests/Lexer/MappingTokenizerTests.cs:241-255` asserts that `"quoted identifier"` produces `DelimitedIdentifier`. That assertion encodes the defect — in FML, delimited identifiers are backtick-delimited. Change the expected kind to `MappingTokenKind.DoubleQuotedString`, and add a sibling test proving backticks still work:

```csharp
    [Fact]
    public void GivenABacktickDelimitedIdentifier_WhenTokenizing_ThenItIsADelimitedIdentifier()
    {
        var tokenizer = MappingTokenizer.Create();

        var tokens = tokenizer.Tokenize("`quoted identifier`").ToList();

        tokens.Count.ShouldBe(1);
        tokens[0].Kind.ShouldBe(MappingTokenKind.DelimitedIdentifier);
    }
```

- [ ] **Step 10: Run the whole FML suite**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0
```

Expected: PASS. Because `StringLiteral` is tried first inside the `Literal` parser (line 84-88), widening `Identifier` cannot shadow literal parsing.

- [ ] **Step 11: Commit**

```bash
git add src/Core/Ignixa.FhirMappingLanguage/Lexer src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs test/Ignixa.FhirMappingLanguage.Tests
git commit -m "Treat double-quoted text as a string literal in FML"
```

---

## Task 4: Group type-mode annotation `<<types>>` / `<<type+>>`

`MappingGrammar.Group` (lines 488-507) goes straight from the optional `extends` clause to `{`. The `LeftAngle`/`RightAngle` token kinds exist but no rule consumes them, so `group X(...) <<type+>> { ... }` fails.

Occurrences: `ActivityDefinition.map:8`, `syntaxshort.map:10`, `Patient_4to6.fml:12`, and effectively every cross-version map.

**Files:**
- Create: `src/Core/Ignixa.FhirMappingLanguage/Expressions/GroupTypeMode.cs`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Expressions/GroupExpression.cs`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs:488-507`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Serialization/FmlSerializer.cs:191-213`
- Test: `test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `FmlSyntaxCoverageTests`:

```csharp
    [Theory]
    [InlineData("<<types>>", GroupTypeMode.Types)]
    [InlineData("<<type+>>", GroupTypeMode.TypeAndTypes)]
    public void GivenAGroupTypeModeAnnotation_WhenParsing_ThenTheModeIsCaptured(string annotation, GroupTypeMode expected)
    {
        var fml = $$"""
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {{annotation}} {
              src.a as a -> tgt.a = a;
            }
            """;

        var map = new MappingParser().Parse(fml);

        map.Groups[0].TypeMode.ShouldBe(expected);
    }

    [Fact]
    public void GivenNoTypeModeAnnotation_WhenParsing_ThenTheModeIsNone()
    {
        const string Fml = """
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {
              src.a as a -> tgt.a = a;
            }
            """;

        var map = new MappingParser().Parse(Fml);

        map.Groups[0].TypeMode.ShouldBe(GroupTypeMode.None);
    }
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlSyntaxCoverageTests"
```

Expected: build error `CS0246: The type or namespace name 'GroupTypeMode' could not be found`.

- [ ] **Step 3: Create the enum**

Create `src/Core/Ignixa.FhirMappingLanguage/Expressions/GroupTypeMode.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Group type mode annotation for FHIR Mapping Language groups.
 */

namespace Ignixa.FhirMappingLanguage.Expressions;

/// <summary>
/// Indicates how a group participates in type-directed rule selection,
/// declared in FML as a &lt;&lt;types&gt;&gt; or &lt;&lt;type+&gt;&gt; annotation.
/// </summary>
public enum GroupTypeMode
{
    /// <summary>No annotation present. The group is only invoked by explicit reference.</summary>
    None = 0,

    /// <summary>Declared &lt;&lt;types&gt;&gt;: the group is a candidate for type-based dispatch.</summary>
    Types,

    /// <summary>Declared &lt;&lt;type+&gt;&gt;: the group is the default for type-based dispatch.</summary>
    TypeAndTypes
}
```

`None = 0` is deliberate — it makes `.OptionalOrDefault()` yield the right value for groups with no annotation.

- [ ] **Step 4: Add the property to `GroupExpression`**

In `src/Core/Ignixa.FhirMappingLanguage/Expressions/GroupExpression.cs`, add a **trailing optional** constructor parameter (trailing so every existing positional call site keeps compiling) and the matching property:

- Append `, GroupTypeMode typeMode = GroupTypeMode.None` to the constructor signature, after the current last parameter.
- Assign `TypeMode = typeMode;` in the constructor body.
- Add the property alongside the existing ones:

```csharp
    /// <summary>
    /// Gets the type-mode annotation declared for this group.
    /// </summary>
    public GroupTypeMode TypeMode { get; }
```

Leave `ToString()` (lines 33-49) alone — round-tripping is `FmlSerializer`'s job.

- [ ] **Step 5: Add the grammar rule**

In `MappingGrammar.cs`, define this parser immediately **above** the `Group` parser:

```csharp
    // Group type mode annotation: <<types>> or <<type+>>
    // Note: '<<' is two LeftAngle tokens - the lexer deliberately has no '<<' token.
    private static readonly TokenListParser<MappingTokenKind, GroupTypeMode> GroupTypeModeAnnotation =
        from open1 in Token.EqualTo(MappingTokenKind.LeftAngle)
        from open2 in Token.EqualTo(MappingTokenKind.LeftAngle)
        from mode in Token.EqualTo(MappingTokenKind.Types).Value(GroupTypeMode.Types)
            .Or(from typeToken in Token.EqualTo(MappingTokenKind.Type)
                from plus in Token.EqualTo(MappingTokenKind.Plus).Optional()
                select plus.HasValue ? GroupTypeMode.TypeAndTypes : GroupTypeMode.Types)
        from close1 in Token.EqualTo(MappingTokenKind.RightAngle)
        from close2 in Token.EqualTo(MappingTokenKind.RightAngle)
        select mode;
```

Then, in the `Group` parser (lines 488-507), insert between the optional `extends` clause and the opening `{`:

```csharp
        from typeMode in GroupTypeModeAnnotation.OptionalOrDefault(GroupTypeMode.None)
```

and pass `typeMode` as the new trailing argument to the `GroupExpression` constructor in that parser's `select`.

**Lexer note:** `Span.Regex(@"\btype\b")` still matches inside `type+` because `+` is a non-word character, and `\btypes\b` is registered before `\btype\b`, so `types` is never split. Task 1 supplied the `Plus` token this rule needs.

- [ ] **Step 6: Round-trip in `FmlSerializer`**

In `src/Core/Ignixa.FhirMappingLanguage/Serialization/FmlSerializer.cs`, in `SerializeGroup` (starting line 191), emit the annotation **after** the `extends` clause (lines 208-212) and before the opening brace:

```csharp
        if (group.TypeMode != GroupTypeMode.None)
        {
            builder.Append(group.TypeMode == GroupTypeMode.TypeAndTypes ? " <<type+>>" : " <<types>>");
        }
```

Match the surrounding code's `StringBuilder` variable name and spacing conventions rather than copying `builder` verbatim.

- [ ] **Step 7: Add a round-trip test**

Append to `FmlSyntaxCoverageTests`:

```csharp
    [Theory]
    [InlineData("<<types>>")]
    [InlineData("<<type+>>")]
    public void GivenAGroupTypeModeAnnotation_WhenRoundTripping_ThenTheAnnotationSurvives(string annotation)
    {
        var fml = $$"""
            map 'http://example.org/T' = 'T'

            group Main(source src, target tgt) {{annotation}} {
              src.a as a -> tgt.a = a;
            }
            """;

        var reparsed = new MappingParser().Parse(new FmlSerializer().Serialize(new MappingParser().Parse(fml)));

        reparsed.Groups[0].TypeMode.ShouldBe(new MappingParser().Parse(fml).Groups[0].TypeMode);
    }
```

Add `using Ignixa.FhirMappingLanguage.Serialization;` to the file. If `FmlSerializer` exposes a static `Serialize` rather than an instance method, adjust the call — check the file before writing.

- [ ] **Step 8: Run the whole FML suite**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Core/Ignixa.FhirMappingLanguage test/Ignixa.FhirMappingLanguage.Tests
git commit -m "Support group type mode annotations in FML"
```

---

## Task 5: `///` metadata declarations and optional `map` header

R6-era FML replaces the `map "url" = "name"` header with a run of metadata lines:

```
/// url = 'http://hl7.org/fhir/uv/xver/StructureMap/Element4to6'
/// name = 'Element4to6'
/// title = 'Element Transforms: R4 to R6'
```

The tokenizer currently swallows these as `LineComment` (because `Comment.CPlusPlusStyle` matches `//`), and `MappingGrammar.Map` (line 511) requires a leading `Map` token, so parsing fails with `unexpected uses 'uses', expected map`. This blocks every `brianpos/fhir-r6-maps` file and `syntax.map:1`.

**Files:**
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenKind.cs`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Lexer/MappingTokenizer.cs` (before the comment rules in **both** builders)
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingGrammar.cs:509-528`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Expressions/MapExpression.cs`
- Modify: `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingParser.cs` (empty-map guard)
- Test: `test/Ignixa.FhirMappingLanguage.Tests/Parser/FmlSyntaxCoverageTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `FmlSyntaxCoverageTests`:

```csharp
    [Fact]
    public void GivenMetadataDeclarationsInsteadOfAMapHeader_WhenParsing_ThenUrlAndNameAreDerivedFromMetadata()
    {
        const string Fml = """
            /// url = 'http://hl7.org/fhir/uv/xver/StructureMap/Element4to6'
            /// name = 'Element4to6'
            /// title = 'Element Transforms: R4 to R6'

            group Element(source src, target tgt) {
              src.id as v -> tgt.id = v;
            }
            """;

        var map = new MappingParser().Parse(Fml);

        map.Url.ShouldBe("http://hl7.org/fhir/uv/xver/StructureMap/Element4to6");
        map.Identifier.ShouldBe("Element4to6");
        map.Metadata["title"].ShouldBe("Element Transforms: R4 to R6");
    }

    [Fact]
    public void GivenOnlyCommentsAndWhitespace_WhenParsing_ThenAParseExceptionIsThrown()
    {
        const string Fml = """
            // nothing to see here
            """;

        Should.Throw<ParseException>(() => new MappingParser().Parse(Fml));
    }
```

Add `using Ignixa.FhirMappingLanguage.Parser;` if not already present, and reference `ParseException` by its real namespace — confirm it in `MappingParser.cs` before writing.

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlSyntaxCoverageTests"
```

Expected: FAIL — `unexpected group 'group', expected map` (or a `CS1061` on `Metadata`).

- [ ] **Step 3: Add the token kind**

In `MappingTokenKind.cs`, replace:

```csharp
    // Comments (for trivia mode)
    LineComment,         // //
    BlockComment,        // /* */
```

with:

```csharp
    // Metadata declarations (R6 FML header form)
    MetadataLine,        // /// key = 'value'

    // Comments (for trivia mode)
    LineComment,         // //
    BlockComment,        // /* */
```

- [ ] **Step 4: Match `///` before the comment rules in `CreateWithTrivia()`**

Replace:

```csharp
        return new TokenizerBuilder<MappingTokenKind>()
            // Comments (must come before other operators to avoid capturing // as division)
            .Match(Comment.CStyle, MappingTokenKind.BlockComment, requireDelimiters: true)
            .Match(Comment.CPlusPlusStyle, MappingTokenKind.LineComment)

            // Whitespace
            .Match(Span.WhiteSpace, MappingTokenKind.Whitespace)
```

with:

```csharp
        return new TokenizerBuilder<MappingTokenKind>()
            // Metadata declarations (must precede the comment rules - '///' is a prefix of '//')
            .Match(Span.Regex(@"///[^\r\n]*"), MappingTokenKind.MetadataLine, requireDelimiters: false)

            // Comments (must come before other operators to avoid capturing // as division)
            .Match(Comment.CStyle, MappingTokenKind.BlockComment, requireDelimiters: true)
            .Match(Comment.CPlusPlusStyle, MappingTokenKind.LineComment)

            // Whitespace
            .Match(Span.WhiteSpace, MappingTokenKind.Whitespace)
```

- [ ] **Step 5: Match `///` before the comment rules in `Create()`**

Replace:

```csharp
        return new TokenizerBuilder<MappingTokenKind>()
            // Comments (ignore for standard parsing)
            .Ignore(Comment.CStyle)
            .Ignore(Comment.CPlusPlusStyle)
```

with:

```csharp
        return new TokenizerBuilder<MappingTokenKind>()
            // Metadata declarations (must precede the comment rules - '///' is a prefix of '//')
            .Match(Span.Regex(@"///[^\r\n]*"), MappingTokenKind.MetadataLine, requireDelimiters: false)

            // Comments (ignore for standard parsing)
            .Ignore(Comment.CStyle)
            .Ignore(Comment.CPlusPlusStyle)
```

`///` does not collide with `/* */`, and it must win over `//`.

- [ ] **Step 6: Add the `Metadata` property to `MapExpression`**

In `src/Core/Ignixa.FhirMappingLanguage/Expressions/MapExpression.cs`:

- Append a trailing optional constructor parameter `, IReadOnlyDictionary<string, string>? metadata = null`.
- Assign `Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);` in the body.
- Add:

```csharp
    /// <summary>
    /// Gets the metadata declarations (<c>/// key = 'value'</c>) that preceded the map body.
    /// Empty when the map used the classic <c>map "url" = "name"</c> header form.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }
```

The constructor currently throws `ArgumentNullException` on null `url`/`identifier` (lines 25-26) — pass `string.Empty`, never null, from the grammar.

- [ ] **Step 7: Parse metadata lines and make the header optional**

In `MappingGrammar.cs`, add above the `Map` parser:

```csharp
    private static readonly Regex MetadataLinePattern = new(
        @"^///\s*(?<key>[A-Za-z_][A-Za-z0-9_.\-]*)\s*=\s*(?<value>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly TokenListParser<MappingTokenKind, KeyValuePair<string, string>?> MetadataDeclaration =
        Token.EqualTo(MappingTokenKind.MetadataLine)
            .Select(t =>
            {
                var match = MetadataLinePattern.Match(t.ToStringValue());
                if (!match.Success)
                {
                    return (KeyValuePair<string, string>?)null;
                }

                return new KeyValuePair<string, string>(
                    match.Groups["key"].Value,
                    UnescapeString(match.Groups["value"].Value));
            });

    private sealed record MapHeaderInfo(string Url, string Identifier, SourcePosition Position);
```

Use the repo's actual position type in `MapHeaderInfo` — `CreatePosition` (lines 30-40) shows what it returns; copy that type name exactly rather than assuming `SourcePosition`.

Then restructure the `Map` parser (lines 509-528) so that:

1. It starts with `from metadataLines in MetadataDeclaration.Many()`.
2. The existing `map "url" = "name"` header becomes a `MapHeaderInfo`-producing parser applied with `.OptionalOrDefault()` (it is a reference type, so the default is `null`).
3. `url` resolves as `header?.Url ?? metadata.GetValueOrDefault("url", string.Empty)`, and `identifier` as `header?.Identifier ?? metadata.GetValueOrDefault("name", string.Empty)` — the explicit header wins for backwards compatibility. The two forms are mutually exclusive across every corpus file, so this branch is never contentious.
4. The metadata dictionary is built from the non-null `metadataLines` entries with `StringComparer.Ordinal` and passed as the new trailing `MapExpression` argument.

Add `using System.Text.RegularExpressions;` to the file.

- [ ] **Step 8: Guard against a map that parsed to nothing**

Making both the header and the metadata optional means a comments-only file would now "successfully" parse into an empty map. In `src/Core/Ignixa.FhirMappingLanguage/Parser/MappingParser.cs`, immediately after the successful-parse result is unwrapped (after line 69), add:

```csharp
        if (result.Value.Groups.Count == 0 &&
            string.IsNullOrEmpty(result.Value.Url) &&
            string.IsNullOrEmpty(result.Value.Identifier))
        {
            throw new ParseException("The input contains no map header, metadata declarations, or groups.");
        }
```

Use the exact local variable name that exists at that point in `Parse`, and the exception type/constructor the file already throws elsewhere. `Parse` already rejects null/whitespace input at line 45 and enforces `.AtEnd()` at line 60, so this is the only remaining hole.

- [ ] **Step 9: Run the whole FML suite**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0
```

Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.FhirMappingLanguage test/Ignixa.FhirMappingLanguage.Tests
git commit -m "Support R6 metadata declarations and make the FML map header optional"
```

---

# Part B — Oracle harness

## Task 6: Vendor the official `fhir-test-cases` corpus

`test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj:29-60` already contains a proven, race-safe MSBuild target that downloads and unpacks the HL7 `fhir-test-cases` release zip. Copy it verbatim into the FML test project — same pinned version, so both projects agree on corpus contents.

**Files:**
- Modify: `test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj`
- Modify: `.gitignore` (if `TestData/` is not already ignored)

- [ ] **Step 1: Add the download target**

In `test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj`, replace:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Core\Ignixa.FhirMappingLanguage\Ignixa.FhirMappingLanguage.csproj" />
    <ProjectReference Include="..\..\src\Core\Ignixa.Specification\Ignixa.Specification.csproj" />
    <ProjectReference Include="..\Ignixa.Serialization.TestSupport\Ignixa.Serialization.TestSupport.csproj" />
  </ItemGroup>

</Project>
```

with:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Core\Ignixa.FhirMappingLanguage\Ignixa.FhirMappingLanguage.csproj" />
    <ProjectReference Include="..\..\src\Core\Ignixa.Specification\Ignixa.Specification.csproj" />
    <ProjectReference Include="..\Ignixa.Serialization.TestSupport\Ignixa.Serialization.TestSupport.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <FhirTestCasesVersion>1.7.46</FhirTestCasesVersion>
    <FhirTestCasesUrl>https://github.com/FHIR/fhir-test-cases/releases/download/$(FhirTestCasesVersion)/testcases.zip</FhirTestCasesUrl>
    <FhirTestCasesZip>$(MSBuildProjectDirectory)\TestData\testcases.zip</FhirTestCasesZip>
    <FhirTestCasesDir>$(MSBuildProjectDirectory)\TestData\fhir-test-cases</FhirTestCasesDir>
    <FhirTestCasesTempDir>$(MSBuildProjectDirectory)\TestData\temp-extract</FhirTestCasesTempDir>
    <FhirTestCasesMarker>$(FhirTestCasesDir)\.downloaded</FhirTestCasesMarker>
  </PropertyGroup>

  <!-- Runs once in the outer (multi-targeting) build via DispatchToInnerBuilds so the parallel
       per-TFM inner builds don't race on the shared testcases.zip; BeforeBuild covers single-TFM builds. -->
  <Target Name="DownloadFhirTestCases" BeforeTargets="DispatchToInnerBuilds;BeforeBuild" Condition="!Exists('$(FhirTestCasesMarker)')">
    <Message Text="Downloading FHIR test cases from $(FhirTestCasesUrl)..." Importance="high" />
    <MakeDir Directories="$(MSBuildProjectDirectory)\TestData" />
    <DownloadFile SourceUrl="$(FhirTestCasesUrl)" DestinationFolder="$(MSBuildProjectDirectory)\TestData" DestinationFileName="testcases.zip" SkipUnchangedFiles="true" />

    <Message Text="Extracting FHIR test cases to temporary location..." Importance="high" />
    <RemoveDir Directories="$(FhirTestCasesTempDir)" Condition="Exists('$(FhirTestCasesTempDir)')" />
    <Unzip SourceFiles="$(FhirTestCasesZip)" DestinationFolder="$(FhirTestCasesTempDir)" OverwriteReadOnlyFiles="true" />

    <ItemGroup>
      <ExtractedFiles Include="$(FhirTestCasesTempDir)\fhir-test-cases\**\*.*" />
    </ItemGroup>

    <Message Text="Moving extracted files from nested directory to $(FhirTestCasesDir)..." Importance="high" />
    <RemoveDir Directories="$(FhirTestCasesDir)" Condition="Exists('$(FhirTestCasesDir)')" />
    <Move SourceFiles="@(ExtractedFiles)" DestinationFolder="$(FhirTestCasesDir)\%(RecursiveDir)" />

    <RemoveDir Directories="$(FhirTestCasesTempDir)" />
    <Touch Files="$(FhirTestCasesMarker)" AlwaysCreate="true" />
    <Message Text="FHIR test cases downloaded and extracted successfully." Importance="high" />
  </Target>

</Project>
```

- [ ] **Step 2: Build to trigger the download**

```bash
dotnet build test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj
```

Expected: `Downloading FHIR test cases from https://github.com/FHIR/fhir-test-cases/releases/download/1.7.46/testcases.zip...` then a successful build. This is a ~100 MB download on first run.

- [ ] **Step 3: Verify the corpus landed**

```bash
Get-ChildItem test/Ignixa.FhirMappingLanguage.Tests/TestData/fhir-test-cases/r5/structure-mapping -Filter *.map | Measure-Object
Get-ChildItem test/Ignixa.FhirMappingLanguage.Tests/TestData/fhir-test-cases/r4b/structure-mapping -Filter *.map | Measure-Object
```

Expected: 16 and 12 respectively. If the counts differ, the pinned version changed upstream — record the actual counts and use them in Task 8.

- [ ] **Step 4: Confirm `TestData/` is not committed**

```bash
git status --short test/Ignixa.FhirMappingLanguage.Tests/TestData
```

Expected: no output. If the directory shows as untracked, add `test/Ignixa.FhirMappingLanguage.Tests/TestData/` to `.gitignore` (check how `Ignixa.FhirPath.Tests/TestData` is ignored and mirror it).

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj .gitignore
git commit -m "Vendor the HL7 fhir-test-cases corpus into the FML test project"
```

---

## Task 7: Corpus locator

One place that knows where the vendored corpus lives, so no test hard-codes relative paths.

**Files:**
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTestCasesLocator.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTestCasesLocatorTests.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.IO;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

public class FmlTestCasesLocatorTests
{
    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    public void GivenAVendoredCorpus_WhenLocatingStructureMappingDirectory_ThenTheDirectoryExists(string version)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(version);

        Directory.Exists(directory).ShouldBeTrue($"Expected the vendored corpus at {directory}. Run 'dotnet build' to download it.");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlTestCasesLocatorTests"
```

Expected: `CS0103: The name 'FmlTestCasesLocator' does not exist`.

- [ ] **Step 3: Implement the locator**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTestCasesLocator.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Resolves paths into the vendored HL7 fhir-test-cases corpus.
 */

using System;
using System.IO;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Resolves paths into the vendored HL7 <c>fhir-test-cases</c> corpus.
/// The corpus is downloaded by the <c>DownloadFhirTestCases</c> MSBuild target
/// into <c>TestData/fhir-test-cases</c> beside the project file.
/// </summary>
public static class FmlTestCasesLocator
{
    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    /// <summary>
    /// Gets the root of the vendored corpus.
    /// </summary>
    public static string Root => RootDirectory.Value;

    /// <summary>
    /// Gets the <c>structure-mapping</c> directory for a FHIR version folder such as
    /// <c>r5</c> or <c>r4b</c>.
    /// </summary>
    public static string StructureMappingDirectory(string version) =>
        Path.Combine(Root, version, "structure-mapping");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "TestData", "fhir-test-cases");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the vendored fhir-test-cases corpus. Run 'dotnet build' on " +
            "test/Ignixa.FhirMappingLanguage.Tests to download it.");
    }
}
```

Walking up from `AppContext.BaseDirectory` (which is `bin/Debug/net9.0/`) reaches the project directory in four hops and works identically for both target frameworks.

- [ ] **Step 4: Run to verify it passes**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlTestCasesLocatorTests"
```

Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.FhirMappingLanguage.Tests/Conformance
git commit -m "Add a locator for the vendored FML conformance corpus"
```

---

## Task 8: Corpus parse-rate gate

This is the regression net for Part A. It parses every `.map` in the official corpus and asserts an exact pass count — so a future grammar change that silently drops support for a construct fails the build rather than quietly regressing.

**Files:**
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlCorpusParseTests.cs`

- [ ] **Step 1: Write the test with a deliberately impossible expectation**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlCorpusParseTests.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ignixa.FhirMappingLanguage.Parser;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Parses every map in the official HL7 structure-mapping corpus and asserts an exact
/// pass count. The count is a ratchet: raising it is a feature, lowering it is a regression.
/// </summary>
public class FmlCorpusParseTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("r5", 16)]
    [InlineData("r4b", 12)]
    public void GivenTheOfficialCorpus_WhenParsingEveryMap_ThenTheExpectedNumberParse(string version, int expectedParsed)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(version);
        var files = Directory.GetFiles(directory, "*.map").OrderBy(f => f, StringComparer.Ordinal).ToList();

        var failures = new List<string>();
        var parsed = 0;

        foreach (var file in files)
        {
            try
            {
                new MappingParser().Parse(File.ReadAllText(file, Encoding.UTF8));
                parsed++;
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var report = new StringBuilder();
        report.AppendLine($"{version}: parsed {parsed}/{files.Count}");
        foreach (var failure in failures)
        {
            report.AppendLine("  FAIL " + failure);
        }

        output.WriteLine(report.ToString());

        parsed.ShouldBe(expectedParsed, report.ToString());
    }
}
```

- [ ] **Step 2: Run it and read the report**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlCorpusParseTests" --logger "console;verbosity=detailed"
```

The failure message lists every file that still fails and why. Two outcomes:

- **All parse** → the test passes as written. Move on.
- **Some fail** → read each message. If the cause is a construct in scope for Tasks 1-5, fix it there. If it is `imports "…/*4to6"` wildcard resolution or another deferred item, lower the `expectedParsed` numbers to the actual counts and add a comment above the `[InlineData]` attributes naming the excluded files and the reason.

Do **not** silently lower the number without recording why.

- [ ] **Step 3: Record the measured numbers**

Update the `[InlineData]` values to the counts you actually achieved and re-run to confirm green.

- [ ] **Step 4: Commit**

```bash
git add test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlCorpusParseTests.cs
git commit -m "Gate FML parser on the official structure-mapping corpus parse rate"
```

---

## Task 9: Manifest model, loader, and exclusions

The corpus declares its transform oracle cases in `manifest.xml`:

```xml
<fml-tests>
  <test name="…/qr2patgender" source="qr.json" map="qr2pat-gender.map" output="qr2pat-gender-res.json" />
</fml-tests>
```

`r5/manifest.xml` holds 10 cases; `r4b/manifest.xml` holds a copy of the same 10. Four of them produce CDA logical-model XML, which Ignixa's transform pipeline does not target — those go on a frozen exclusion list with written rationale, per `docs/adr/adr-2607-validation-oracle-conformance.md`.

**Files:**
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlOracleCase.cs`
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlOracleExclusions.cs`
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlManifestLoader.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlManifestLoaderTests.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.Linq;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

public class FmlManifestLoaderTests
{
    [Fact]
    public void GivenTheR5Manifest_WhenLoadingFmlTests_ThenTheDeclaredCasesAreReturned()
    {
        var cases = FmlManifestLoader.Load("r5").ToList();

        cases.ShouldNotBeEmpty();
        cases.ShouldContain(c => c.MapFile == "qr2pat-gender.map"
                                 && c.SourceFile == "qr.json"
                                 && c.OutputFile == "qr2pat-gender-res.json");
    }

    [Fact]
    public void GivenTheR5Manifest_WhenFilteringToSupportedCases_ThenCdaCasesAreExcluded()
    {
        var supported = FmlManifestLoader.Load("r5")
            .Where(c => !FmlOracleExclusions.IsExcluded(c.Name))
            .ToList();

        supported.ShouldNotBeEmpty();
        supported.ShouldNotContain(c => c.Name.Contains("cda", System.StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlManifestLoaderTests"
```

Expected: `CS0103: The name 'FmlManifestLoader' does not exist`.

- [ ] **Step 3: Create the case record**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlOracleCase.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * One <test> entry from an <fml-tests> manifest section.
 */

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// A single transform oracle case declared in the corpus <c>manifest.xml</c>.
/// </summary>
/// <param name="Version">Corpus version folder, e.g. <c>r5</c>.</param>
/// <param name="Name">Case name as declared in the manifest.</param>
/// <param name="SourceFile">Input resource file name, relative to the structure-mapping directory.</param>
/// <param name="MapFile">FML map file name, relative to the structure-mapping directory.</param>
/// <param name="OutputFile">Expected output file name, relative to the structure-mapping directory.</param>
public sealed record FmlOracleCase(
    string Version,
    string Name,
    string SourceFile,
    string MapFile,
    string OutputFile)
{
    /// <inheritdoc />
    public override string ToString() => $"{Version}/{Name}";
}
```

The `ToString()` override drives readable xUnit theory display names.

This record is passed through `IEnumerable<object[]>` `MemberData`, matching the precedent set by `FhirPathTestCase` in `test/Ignixa.FhirPath.Tests/OfficialTestSuiteRunner.cs:169-212`. It does not need to implement `IXunitSerializable` — that runner proves the pattern works in this repo's xUnit v2 setup.

- [ ] **Step 4: Create the exclusion list**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlOracleExclusions.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Frozen list of official FML oracle cases outside Ignixa's supported scope.
 */

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Cases from the official corpus that Ignixa deliberately does not attempt.
/// Per ADR-2607, the exclusion list is frozen and each entry carries a written
/// rationale: conformance is reported as a percentage of supported scope, never
/// inflated by quietly skipping hard cases.
/// </summary>
public static class FmlOracleExclusions
{
    private static readonly FrozenDictionary<string, string> Excluded =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["qr2cda"] = "Targets the CDA logical model and produces XML output; Ignixa's transform pipeline emits FHIR JSON only.",
            ["qr2cdaxsi"] = "CDA logical model with xsi:type discrimination; XML output is out of scope.",
            ["qr2cd-eval-json"] = "CDA logical model target; XML output is out of scope.",
            ["qr2cd-eval-fml"] = "CDA logical model target; XML output is out of scope."
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a manifest case name is excluded from the supported scope.
    /// Manifest names are URLs, so the final path segment is used for matching.
    /// </summary>
    public static bool IsExcluded(string caseName) => Excluded.ContainsKey(LastSegment(caseName));

    /// <summary>
    /// Gets the written rationale for an excluded case, or <c>null</c> if it is not excluded.
    /// </summary>
    public static string? RationaleFor(string caseName) =>
        Excluded.TryGetValue(LastSegment(caseName), out var rationale) ? rationale : null;

    /// <summary>
    /// Gets every excluded case name paired with its rationale.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All => Excluded;

    private static string LastSegment(string caseName) =>
        caseName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? caseName;
}
```

> The four names above are the CDA cases observed in the pinned corpus. When you run Task 11 for the first time, confirm the manifest's actual `name` attributes and correct these keys if the last path segment differs. Do not add new entries just to make a test green — an entry is only legitimate if the case is genuinely outside Ignixa's target scope, and the rationale must say why.

- [ ] **Step 5: Create the manifest loader**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlManifestLoader.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Reads <fml-tests> entries out of the corpus manifest.
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Reads the <c>&lt;fml-tests&gt;</c> section of a corpus <c>manifest.xml</c>.
/// </summary>
public static class FmlManifestLoader
{
    /// <summary>
    /// Loads every declared transform oracle case for a corpus version folder
    /// such as <c>r5</c> or <c>r4b</c>.
    /// </summary>
    public static IReadOnlyList<FmlOracleCase> Load(string version)
    {
        var manifestPath = Path.Combine(FmlTestCasesLocator.StructureMappingDirectory(version), "manifest.xml");

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Corpus manifest not found at {manifestPath}.", manifestPath);
        }

        var document = XDocument.Load(manifestPath);

        return document.Descendants("fml-tests")
            .Elements("test")
            .Select(element => new FmlOracleCase(
                version,
                (string?)element.Attribute("name") ?? string.Empty,
                (string?)element.Attribute("source") ?? string.Empty,
                (string?)element.Attribute("map") ?? string.Empty,
                (string?)element.Attribute("output") ?? string.Empty))
            .Where(c => c.MapFile.Length > 0 && c.SourceFile.Length > 0 && c.OutputFile.Length > 0)
            .ToList();
    }
}
```

- [ ] **Step 6: Run to verify it passes**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlManifestLoaderTests"
```

Expected: PASS, 2 tests.

If `manifest.xml` for `r4b` is a copy of `r5`'s and its `source`/`map`/`output` files do not exist under `r4b/structure-mapping`, note it — Task 11 filters on file existence, so this is handled, but the expected case count changes.

- [ ] **Step 7: Commit**

```bash
git add test/Ignixa.FhirMappingLanguage.Tests/Conformance
git commit -m "Load FML transform oracle cases from the corpus manifest"
```

---

## Task 10: Canonical JSON comparison

Expected outputs are pretty-printed by the reference implementation (`{"resourceType" : "Patient", "gender" : "female"}` — note the spaces around the colons), so text comparison is useless. Property order in FHIR JSON is also not semantically significant. Comparison must be structural.

**Files:**
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/CanonicalJson.cs`

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/CanonicalJsonTests.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

public class CanonicalJsonTests
{
    [Fact]
    public void GivenTwoObjectsDifferingOnlyByWhitespace_WhenCanonicalizing_ThenTheResultsMatch()
    {
        var a = CanonicalJson.Canonicalize("""{"resourceType" : "Patient", "gender" : "female"}""");
        var b = CanonicalJson.Canonicalize("""{"resourceType":"Patient","gender":"female"}""");

        a.ShouldBe(b);
    }

    [Fact]
    public void GivenTwoObjectsDifferingOnlyByPropertyOrder_WhenCanonicalizing_ThenTheResultsMatch()
    {
        var a = CanonicalJson.Canonicalize("""{"gender":"female","resourceType":"Patient"}""");
        var b = CanonicalJson.Canonicalize("""{"resourceType":"Patient","gender":"female"}""");

        a.ShouldBe(b);
    }

    [Fact]
    public void GivenArraysInDifferentOrder_WhenCanonicalizing_ThenTheResultsDiffer()
    {
        var a = CanonicalJson.Canonicalize("""{"given":["a","b"]}""");
        var b = CanonicalJson.Canonicalize("""{"given":["b","a"]}""");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void GivenNonAsciiText_WhenCanonicalizing_ThenTheCharactersAreNotEscaped()
    {
        var canonical = CanonicalJson.Canonicalize("""{"family":"Brönnimann-Bertholet"}""");

        canonical.ShouldContain("Brönnimann-Bertholet");
    }
}
```

Array order is significant in FHIR, so the third test asserts they must **not** compare equal.

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~CanonicalJsonTests"
```

Expected: `CS0103: The name 'CanonicalJson' does not exist`.

- [ ] **Step 3: Implement**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/CanonicalJson.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Structural JSON canonicalization for oracle comparison.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Renders JSON in a canonical form so that formatting and object property order
/// do not affect comparison. Array order is preserved because it is semantically
/// significant in FHIR.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Parses <paramref name="json"/> and re-renders it with object properties sorted
    /// by ordinal name.
    /// </summary>
    public static string Canonicalize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        return Sort(node)?.ToJsonString(WriteOptions) ?? "null";
    }

    private static JsonNode? Sort(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var property in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    sorted[property.Key] = Sort(property.Value?.DeepClone());
                }

                return sorted;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var item in array)
                {
                    result.Add(Sort(item?.DeepClone()));
                }

                return result;
            }

            default:
                return node?.DeepClone();
        }
    }
}
```

`UnsafeRelaxedJsonEscaping` keeps non-ASCII characters readable, which matters because `qr.json` contains `Brönnimann-Bertholet`; and `DeepClone()` is required because a `JsonNode` cannot have two parents.

- [ ] **Step 4: Run to verify it passes**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~CanonicalJsonTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.FhirMappingLanguage.Tests/Conformance/CanonicalJson.cs test/Ignixa.FhirMappingLanguage.Tests/Conformance/CanonicalJsonTests.cs
git commit -m "Add canonical JSON comparison for FML oracle tests"
```

---

## Task 11: The transform oracle runner

Executes each supported manifest case end-to-end and compares the produced resource against the reference-produced expected output.

The execution wiring mirrors `src/Application/Ignixa.Application.Operations/Features/Transform/TransformResourceHandler.cs:88-163` — read that file before writing this, and copy the real type and member names from it rather than trusting the sketch below.

**Files:**
- Create: `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTransformOracleTests.cs`

- [ ] **Step 1: Write the runner**

Create `test/Ignixa.FhirMappingLanguage.Tests/Conformance/FmlTransformOracleTests.cs`:

```csharp
/*
 * Copyright (c) 2025, Ignixa Contributors
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirPath;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

/// <summary>
/// Executes every in-scope case from the official <c>&lt;fml-tests&gt;</c> manifest and
/// compares the produced resource against the reference implementation's expected output.
/// </summary>
public class FmlTransformOracleTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> SupportedCases()
    {
        foreach (var version in new[] { "r5", "r4b" })
        {
            var directory = FmlTestCasesLocator.StructureMappingDirectory(version);

            foreach (var oracleCase in FmlManifestLoader.Load(version))
            {
                if (FmlOracleExclusions.IsExcluded(oracleCase.Name))
                {
                    continue;
                }

                if (!oracleCase.OutputFile.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(directory, oracleCase.MapFile)) ||
                    !File.Exists(Path.Combine(directory, oracleCase.SourceFile)) ||
                    !File.Exists(Path.Combine(directory, oracleCase.OutputFile)))
                {
                    continue;
                }

                yield return [oracleCase];
            }
        }
    }

    [Theory]
    [MemberData(nameof(SupportedCases))]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenAnOfficialFmlTestCase_WhenExecutingTheMap_ThenTheResultMatchesTheReferenceOutput(FmlOracleCase oracleCase)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(oracleCase.Version);
        var fhirVersion = oracleCase.Version == "r4b" ? FhirVersion.R4B : FhirVersion.R5;
        var schema = fhirVersion.GetSchemaProvider();

        var map = new MappingParser().Parse(File.ReadAllText(Path.Combine(directory, oracleCase.MapFile), Encoding.UTF8));

        var source = ResourceJsonNode.Parse(File.ReadAllText(Path.Combine(directory, oracleCase.SourceFile), Encoding.UTF8));
        var targetType = DetermineTargetType(map);
        var target = JsonSourceNodeFactory.Parse<ResourceJsonNode>($"{{\"resourceType\":\"{targetType}\"}}");

        var fhirPathEvaluator = new FhirPathEvaluator();
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            ResourceCreator = type => JsonSourceNodeFactory.Parse<ResourceJsonNode>($"{{\"resourceType\":\"{type}\"}}").ToElement(schema),
            Logger = message => output.WriteLine(message),
            FhirPathEvaluator = (expression, element) => fhirPathEvaluator.Evaluate(expression, element)
        };

        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);

        var mutator = new JsonNodeMutator(fhirPathEvaluator, new FhirPathParser(), () => schema);
        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

        var expected = CanonicalJson.Canonicalize(File.ReadAllText(Path.Combine(directory, oracleCase.OutputFile), Encoding.UTF8));
        var actual = CanonicalJson.Canonicalize(target.ToJson());

        output.WriteLine("EXPECTED:");
        output.WriteLine(expected);
        output.WriteLine("ACTUAL:");
        output.WriteLine(actual);

        actual.ShouldBe(expected);
    }

    private static string DetermineTargetType(MapExpression map)
    {
        var targetUses = map.Uses.LastOrDefault(u => u.Mode == UsesMode.Target)
            ?? throw new InvalidDataException($"Map '{map.Url}' declares no target 'uses' statement.");

        return targetUses.Url.Split('/').Last();
    }
}
```

**Names you must verify before running** (all exist, but the sketch above may have the wrong spelling — take the real ones from `TransformResourceHandler.cs`, `MappingContext.cs`, `UsesExpression.cs`, and `ResourceJsonNode`):
- `ErrorMode.Strict` and the `MappingContext` initialiser property names
- `ResourceCreator`'s delegate signature — `TransformResourceHandler` assigns `CreateResourceElement`; match its parameter and return types
- `UsesExpression`'s mode property and enum (`Mode` / `UsesMode.Target` above are placeholders until confirmed)
- The method that renders a `ResourceJsonNode` back to JSON text (`ToJson()` above)
- `FhirPathEvaluator.Evaluate(expression, element)`'s exact signature and return type

- [ ] **Step 2: Run it**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlTransformOracleTests" --logger "console;verbosity=detailed"
```

Expected on first run: compile clean, then a mix of passes and failures. The `EXPECTED:` / `ACTUAL:` output for each failure is the diagnostic.

- [ ] **Step 3: Triage each failure honestly**

For every failing case, classify it:

1. **Ignixa bug** — fix it. This is the point of the exercise.
2. **Genuinely outside supported scope** — add it to `FmlOracleExclusions` with a rationale naming the specific unsupported capability. Never add an entry whose rationale is "it fails".
3. **Harness bug** — fix the runner.

Record the classification for each case; it feeds Task 13.

- [ ] **Step 4: Re-run until green**

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/Ignixa.FhirMappingLanguage.Tests.csproj -f net9.0 --filter "FullyQualifiedName~FmlTransformOracleTests"
```

Expected: PASS, with the supported-scope case count printed in the run summary.

- [ ] **Step 5: Run the whole solution to check for collateral damage**

```bash
dotnet test All.sln
```

Expected: PASS. The Part A parser changes touch shared code, so this is not optional.

- [ ] **Step 6: Commit**

```bash
git add test/Ignixa.FhirMappingLanguage.Tests/Conformance
git commit -m "Add FML transform oracle tests driven by the official test manifest"
```

---

## Task 12: Wire the FML validation cases into the existing validator oracle

`validator/manifest.json` in the same corpus already drives `ValidatorConformanceRunner`, and it includes `map-general-test.fml` and `map-general-test2.fml` with recorded Java outcomes at `validator/outcomes/java/R5.map-general-test-base.json` and `validator/outcomes/java/R5.map-general-test2-base.json`. These grade FML *diagnostics* — the errors and warnings a conforming implementation should raise — which the transform oracle cannot.

**Files:**
- Modify: the case-selection logic in `ValidatorConformanceRunner` (locate it with `grep -rn "ValidatorConformanceRunner" test/`)

- [ ] **Step 1: Locate the runner and its case filter**

```bash
grep -rn "class ValidatorConformanceRunner" test/
grep -rn "manifest.json" test/
```

Read the file. Determine how it decides which of the 972 `test-cases` entries to execute — most likely an allow-list or an extension filter.

- [ ] **Step 2: Check whether the two FML cases already run**

```bash
dotnet test <the validator test project> -f net9.0 --filter "FullyQualifiedName~ValidatorConformance" --logger "console;verbosity=detailed" 2>&1 | Select-String "map-general"
```

If both appear and pass, this task is already satisfied — record that and skip to Step 5.

- [ ] **Step 3: Add them to the executed set**

Extend the runner's filter to include `map-general-test.fml` and `map-general-test2.fml`, following whatever mechanism the file already uses. Do not restructure the runner.

- [ ] **Step 4: Run and triage**

Compare Ignixa's diagnostics against the recorded Java outcomes. Per ADR-2607, the bar is: no over-strict diagnostics (never report an error the reference does not), and a recorded pass rate for the rest. Add exclusions only with written rationale.

- [ ] **Step 5: Commit**

```bash
git add test/
git commit -m "Include FML validation cases in the validator conformance oracle"
```

---

## Task 13: Record the outcome in the investigation

**Files:**
- Modify: `docs/features/structuremap/investigations/fml-oracle-conformance-corpus.md`
- Modify: `docs/features/structuremap/readme.md`

- [ ] **Step 1: Replace the measured-baseline table with before/after numbers**

In the Evidence section, keep the original `0/355` and `0/27` figures as the "before" column and add an "after" column with the counts measured in Tasks 8 and 11. Do not round or estimate — use the exact numbers the test output printed.

- [ ] **Step 2: Fill in the Verdict**

Set status to **Viable** (or **Partially viable** if material cases remain excluded) and state:
- corpus parse rate, per version
- transform oracle pass rate as a fraction of supported scope
- every exclusion, with its rationale
- which of the five defects are fully closed

- [ ] **Step 3: Update the index**

In `docs/features/structuremap/readme.md`, change the investigation row's status from `In Progress` to the final status.

- [ ] **Step 4: Commit**

```bash
git add docs/features/structuremap
git commit -m "Record measured FML conformance results in the investigation"
```

---

# Explicitly out of scope

These were surveyed and deliberately deferred. Each is a candidate for its own investigation and plan.

| Item | Why deferred |
|---|---|
| `HL7/fhir-cross-version` (1,201 `.fml`) | Grammar corpus only — no source/expected instance pairs, so it can grade parsing but not transformation. Valuable as a Phase 2 parse ratchet once the official corpus is green. |
| `brianpos/fhir-r6-maps` (355 `.fml`) | Staging area for the above; not HL7-governed. Same limitation. |
| `ahdis/fhir-mapping-tutorial` (36 tutorial results + 75 careconnect pairs) | Genuine oracle data and 3-4× the case count, but it is a community-maintained fixture repo, needs its own vendoring mechanism, and depends on 28 logical StructureDefinitions Ignixa would have to load. Do this after the official corpus passes. |
| Serialization oracles (`.map` / `.json` / `.xml` triples in the tutorial repo) | Would grade `StructureMapParser`, `StructureMapBuilder`, and `FmlSerializer` against reference output. Separate concern from the parser and evaluator. |
| Wildcard `imports "…/*4to6"` | An `ImportResolver` glob-semantics feature, not a parser defect. No in-scope oracle case needs it. |
| Date arithmetic evaluation (`%value + 5 days`) | Task 1 and Task 2 make it *tokenize and parse* correctly. Making the evaluator compute it is a distinct feature. |
| CDA logical-model / XML output | Four excluded oracle cases. Requires XML serialization of logical models, which Ignixa's transform pipeline does not target. |
