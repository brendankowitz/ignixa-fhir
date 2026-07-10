# Superpower Search Expression Parser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the handwritten FHIR search key and value parsing in `Ignixa.Search` with a positioned Superpower parser while preserving public parser contracts, semantic exception behavior, atomic value parsing, and the existing expression AST.

**Architecture:** Stable key punctuation is tokenized with `TokenizerBuilder<SearchKeyTokenKind>` and parsed into immutable recursive key syntax records; a schema-aware `SearchKeyBinder` then resolves tenant/version-specific `SearchParameterInfo`, reference targets, common parameters, includes, and `_not-referenced`. A custom `Tokenizer<SearchValueTokenKind>` preserves FHIR escapes while exposing only unescaped separators, type-selected value grammars create immutable value syntax records, and `SearchExpressionBinder` delegates atomic conversion to the existing `*SearchValue.Parse` methods before constructing the current expression model. `ExpressionParser` and `SearchParameterExpressionParser` remain the only public facades, with no fallback or second production path.

**Tech Stack:** C# latest on .NET 9/.NET 10, Superpower 3.1.0, immutable records and `ImmutableArray<T>`, xUnit, Shouldly, NSubstitute, BenchmarkDotNet, MSBuild central package management.

---

## File map

### Production files to create

- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeyTokenKind.cs` — stable key token categories.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeySyntax.cs` — abstract immutable key syntax root.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ParameterKeySyntax.cs` — terminal parameter and optional modifier.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ForwardChainKeySyntax.cs` — reference name, optional target type, and recursive next key.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ReverseChainKeySyntax.cs` — `_has` source type/reference and recursive next key.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/IncludeKeySyntax.cs` — include source, parameter, optional target, and wildcard.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/NotReferencedKeySyntax.cs` — nullable resource/path wildcard representation.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyTokenizer.cs` — `TokenizerBuilder` for identifiers, `:`, `.`, and `*`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs` — ordinary, forward, recursive reverse, include, and `_not-referenced` grammars.
- `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundSearchKey.cs` — abstract semantic key root.
- `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundParameterKey.cs` — resolved terminal parameter/modifier.
- `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundChainKey.cs` — resolved chain metadata and recursively bound next key.
- `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundIncludeKey.cs` — resolved include metadata.
- `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundNotReferencedKey.cs` — validated `_not-referenced` metadata.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs` — tenant/version definition lookup, common-parameter checks, target validation/intersection, unsupported-target filtering, and ambiguity detection.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs` — escaped text and unescaped separator token categories.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueSyntax.cs` — abstract immutable value syntax root.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/AtomicValueSyntax.cs` — raw escaped atomic text plus comparator.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/AlternativesValueSyntax.cs` — immutable comma-alternative collection.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/CompositeValueSyntax.cs` — immutable dollar-component collection.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/MissingValueSyntax.cs` — parsed `:missing` boolean.
- `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/OfTypeValueSyntax.cs` — parsed `system|code|value` payload.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs` — custom tokenizer that recognizes `\,`, `\$`, `\|`, and `\\`.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs` — type/modifier-selected scalar, comparator, alternative, composite, missing, and of-type grammars.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs` — canonical dispatch to existing atomic parsers with existing `BadSearchRequestException` mapping.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs` — value syntax binding plus current AST and chain/include construction.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs` — positioned Superpower syntax failures mapped to `InvalidSearchOperationException`.

### Production files to modify

- `src/Core/Ignixa.Search/Ignixa.Search.csproj:29-33` — add direct `<PackageReference Include="Superpower" />`.
- `src/Core/Ignixa.Search/Properties/AssemblyInfo.cs:6-10` — expose parser internals to `Ignixa.Application.Tests`.
- `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:22-383` — retain its constructor/public methods but replace span splitting, recursion, include, and `_not-referenced` parsing with grammar/binder calls.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:22-402` — retain its public constructor/`Parse` contract but delegate value grammar and expression binding.
- `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs:15-381` — seal the helper and keep its existing comparator/modifier AST behavior as the atomic expression leaf builder.
- `src/Core/Ignixa.Search/Indexing/StringExtensions.cs:56-95,145-173` — remove production comma/composite splitting after cutover; retain token splitting and escaping used by canonical atomic parsers.
- `src/Core/Ignixa.Search/Resources.resx:279-312` — add the positioned malformed search syntax resource while preserving all existing semantic messages.
- `src/Core/Ignixa.Search/Resources.Designer.cs` — add the generated strongly typed property matching the new resource.
- `docs/features/search/readme.md:18-23` — mark the selected investigation implemented after full verification.
- `docs/site/docs/core-sdk/search.md:7-17` — document parser contracts, supported syntax, positioned syntax errors, and unchanged semantic exceptions.
- `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj:16-24` — reference `Ignixa.Search`.

### Test and benchmark files to create

- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserTestContext.cs` — shared schema, substitute definition manager, parameter registration, and public parser factory.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/ExpressionParserCharacterizationTests.cs` — pre-cutover public behavior lock.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs` — ordinary, modifier, typed forward, reverse, and nested key syntax.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs` — common parameter, target, reference, unsupported target, and ambiguity semantics.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs` — include/revinclude/wildcard/iterate and `_not-referenced`.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs` — escaped separators, escaped slash, invalid escape, and trailing slash.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs` — ordinary/comparator/alternative/composite/special syntax.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs` — canonical atomic conversion and AST construction.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs` — semantic exception category/message parity and positioned malformed syntax.
- `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserFacadeTests.cs` — final public-facade regression and no-fallback coverage.
- `bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs` — stable names for the six before/after cases.
- `bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs` — deterministic benchmark-only search definitions.
- `bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs` — unchanged public-facade harness for simple, modified, typed chain, nested `_has`, escaped alternative, and composite cases.
- `tools/benchmarks/Compare-SearchParserBenchmarks.ps1` — normalize BenchmarkDotNet CSV units, calculate percentage changes, classify results, and write the comparison report.
- `docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256` — generated hash manifest proving the harness is unchanged between runs.
- `docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv` — generated current-parser summary measurements.
- `docs/features/search/benchmarks/2026-07-10-handwritten-parser.md` — generated current-parser BenchmarkDotNet environment and summary.
- `docs/features/search/benchmarks/2026-07-10-superpower-parser.csv` — generated replacement-parser summary measurements.
- `docs/features/search/benchmarks/2026-07-10-superpower-parser.md` — generated replacement-parser BenchmarkDotNet environment and summary.
- `docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md` — generated per-case percentage comparison, classification, and acceptance decision.

### Files intentionally unchanged

- `src/Core/Ignixa.Search/Expressions/Parsers/IExpressionParser.cs:8-13` and `ISearchParameterExpressionParser.cs:11-17` remain byte-for-byte compatible.
- Existing expression model types directly under `src/Core/Ignixa.Search/Expressions/` remain unchanged; only the `Parsers/` subtree changes.
- `src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs:93-119` continues constructing the same public parser classes per tenant/FHIR version.
- Existing `DateTimeSearchValue.Parse`, `NumberSearchValue.Parse`, `QuantitySearchValue.Parse`, `IReferenceSearchValueParser.Parse`, `StringSearchValue.Parse`, `TokenSearchValue.Parse`, and `UriSearchValue.Parse` implementations remain canonical.
- `test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj:12-35` already contains all required test packages and project references; no new test project or package is added.

## Implementation constraints

- Run commands from the repository root.
- Do not add `Hl7.Fhir.*` dependencies to core or application projects.
- Keep one major type per file, file-scoped namespaces, immutable syntax/bound records, and primary constructors where they reduce boilerplate.
- Attribute every new parser production and parser test C# file to `Ignixa Contributors` using the repository's standard MIT header; do not copy legacy Microsoft attribution from older parser files.
- Begin every newly created production C# file with `#nullable enable`; `Ignixa.Search.csproj:7` otherwise disables nullable annotations and the planned `string?`/nullable record signatures would not compile warning-free.
- Do not add a compatibility switch, broad `catch (Exception)`, or successful fallback to the handwritten parser.
- Do not begin parser production changes until the six-case current-parser BenchmarkDotNet baseline and harness hash manifest are recorded. Run the replacement benchmark on the same machine, .NET SDK/runtime, Release configuration, BenchmarkDotNet job settings, inputs, and harness; a hash mismatch or environment mismatch invalidates the comparison and requires both runs to be repeated.
- A checkpoint proposes a commit but does not authorize one. Never run `git add` or `git commit` until the user explicitly approves that exact checkpoint.

### Task 1: Characterize the current public parser

**Files:**
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserTestContext.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/ExpressionParserCharacterizationTests.cs`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:70-213,216-310,345-382`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:51-180,183-299,332-401`

- [x] **Step 1: Add the reusable parser test context**

```csharp
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.Generated;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

internal sealed class SearchParserTestContext
{
    private readonly Dictionary<string, List<SearchParameterInfo>> _parameters =
        new(StringComparer.OrdinalIgnoreCase);

    public SearchParserTestContext()
    {
        SchemaProvider = new R4CoreSchemaProvider();
        DefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        ValueParser = new SearchParameterExpressionParser(
            new ReferenceSearchValueParser(SchemaProvider),
            SchemaProvider);
        Parser = new ExpressionParser(() => DefinitionManager, ValueParser, SchemaProvider);
    }

    public R4CoreSchemaProvider SchemaProvider { get; }

    public ISearchParameterDefinitionManager DefinitionManager { get; }

    public SearchParameterExpressionParser ValueParser { get; }

    public ExpressionParser Parser { get; }

    public SearchParameterInfo Add(
        string resourceType,
        string code,
        SearchParamType type,
        IReadOnlyList<string>? targets = null,
        IReadOnlyList<SearchParameterComponentInfo>? components = null)
    {
        var parameter = new SearchParameterInfo(
            code,
            code,
            type,
            components: components,
            targetResourceTypes: targets,
            baseResourceTypes: [resourceType]);

        if (!_parameters.TryGetValue(resourceType, out var parameters))
        {
            parameters = [];
            _parameters.Add(resourceType, parameters);
            DefinitionManager.GetSearchParameters(resourceType).Returns(parameters);
        }

        parameters.Add(parameter);
        DefinitionManager.GetSearchParameter(resourceType, code).Returns(parameter);
        return parameter;
    }

    public void AddCommon(SearchParameterInfo parameter, params string[] resourceTypes)
    {
        foreach (string resourceType in resourceTypes)
        {
            DefinitionManager.GetSearchParameter(resourceType, parameter.Code).Returns(parameter);
        }
    }
}
```

- [x] **Step 2: Add characterization tests through `IExpressionParser` and `ISearchParameterExpressionParser`**

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class ExpressionParserCharacterizationTests
{
    [Fact]
    public void GivenOrdinaryStringParameter_WhenParsing_ThenBuildsExistingExpressionShape()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);
        IExpressionParser parser = context.Parser;

        var result = parser.Parse(["Patient"], "name", "Smith");

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        search.Parameter.ShouldBeSameAs(parameter);
        var value = search.Expression.ShouldBeOfType<StringExpression>();
        value.StringOperator.ShouldBe(StringOperator.StartsWith);
        value.Value.ShouldBe("Smith");
        value.IgnoreCase.ShouldBeTrue();
    }

    [Fact]
    public void GivenTypedForwardAndNestedReverseChain_WhenParsing_ThenBuildsNestedChains()
    {
        var context = new SearchParserTestContext();
        var patient = context.Add("Observation", "patient", SearchParamType.Reference, ["Patient"]);
        var member = context.Add("Group", "member", SearchParamType.Reference, ["Patient"]);
        var tag = context.Add("Group", "_tag", SearchParamType.Token);

        var result = context.Parser.Parse(
            ["Observation"],
            "patient:Patient._has:Group:member:_tag",
            "http://example.org/tags|reviewed");

        var forward = result.ShouldBeOfType<ChainedExpression>();
        forward.ReferenceSearchParameter.ShouldBeSameAs(patient);
        forward.TargetResourceTypes.ShouldBe(["Patient"]);
        forward.Reversed.ShouldBeFalse();
        var reverse = forward.Expression.ShouldBeOfType<ChainedExpression>();
        reverse.ReferenceSearchParameter.ShouldBeSameAs(member);
        reverse.TargetResourceTypes.ShouldBe(["Patient"]);
        reverse.Reversed.ShouldBeTrue();
        reverse.Expression.ShouldBeOfType<SearchParameterExpression>()
            .Parameter.ShouldBeSameAs(tag);
    }

    [Fact]
    public void GivenEscapedAlternativeAndNotModifier_WhenParsing_ThenEscapesBeforeBuildingNotOr()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "code", SearchParamType.Token);

        var result = context.ValueParser.Parse(
            parameter,
            new SearchModifier(SearchModifierCode.Not),
            @"http://example.org|a\,b,http://example.org|c");

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var not = search.Expression.ShouldBeOfType<NotExpression>();
        not.Expression.ShouldBeOfType<MultiaryExpression>()
            .MultiaryOperation.ShouldBe(MultiaryOperator.Or);
    }

    [Theory]
    [InlineData("*:*", null, null)]
    [InlineData("Observation:*", "Observation", null)]
    [InlineData("Observation:subject", "Observation", "subject")]
    public void GivenNotReferencedValue_WhenParsing_ThenPreservesWildcardSemantics(
        string value,
        string? expectedType,
        string? expectedPath)
    {
        var context = new SearchParserTestContext();

        var result = context.Parser.Parse(["Patient"], "_not-referenced", value);

        var notReferenced = result.ShouldBeOfType<NotReferencedExpression>();
        notReferenced.SourceResourceType.ShouldBe(expectedType);
        notReferenced.ReferencePath.ShouldBe(expectedPath);
    }
}
```

- [x] **Step 3: Run the characterization tests and verify the existing implementation passes**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.ExpressionParserCharacterizationTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 6, Skipped: 0` (the theory contributes three cases). If a characterization assertion exposes a real current shape difference, correct the assertion to the observed public AST without changing production code, then rerun to the same passing summary.

- [x] **Step 4: Checkpoint prepared; commit remains unapproved**

Run:

```powershell
git --no-pager diff -- test/Ignixa.Application.Tests/Search/Expressions/Parsers
git status --short
```

Proposed commit subject: `Characterize search expression parser behavior`

Proposed commit message:

```text
Characterize search expression parser behavior

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserTestContext.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/ExpressionParserCharacterizationTests.cs
git commit -m "Characterize search expression parser behavior" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 2: Record the mandatory current-parser benchmark baseline

**Files:**
- Modify: `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj:16-24`
- Create: `bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs`
- Create: `bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs`
- Create: `bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs`
- Create: `tools/benchmarks/Compare-SearchParserBenchmarks.ps1`
- Create from benchmark output: `docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256`
- Create from benchmark output: `docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv`
- Create from benchmark output: `docs/features/search/benchmarks/2026-07-10-handwritten-parser.md`
- Reference: `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj:1-31`
- Reference: `Directory.Packages.props:92-93`

- [x] **Step 1: Confirm the existing benchmark project and centrally managed tooling are suitable**

Run:

```powershell
rg "BenchmarkDotNet" Directory.Packages.props bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj
dotnet build bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj --no-restore
```

Expected: `Directory.Packages.props` reports BenchmarkDotNet 0.15.8, `Ignixa.Benchmarks.csproj` already references `BenchmarkDotNet`, and the existing benchmark project builds with `0 Warning(s)` and `0 Error(s)`. Therefore reuse this project; do not create another benchmark project or add benchmark tooling.

- [x] **Step 2: Add the direct search reference and stable case names**

Add to `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj`:

```xml
<ProjectReference Include="..\..\src\Core\Ignixa.Search\Ignixa.Search.csproj" />
```

Create the case enum:

```csharp
namespace Ignixa.Benchmarks;

public enum SearchParserBenchmarkCase
{
    Simple,
    Modified,
    TypedChain,
    NestedReverseChain,
    EscapedAlternative,
    Composite,
}
```

- [x] **Step 3: Add deterministic benchmark-only search definitions**

```csharp
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Benchmarks;

internal sealed class BenchmarkSearchParameterDefinitionManager :
    ISearchParameterDefinitionManager
{
    private readonly Dictionary<string, SearchParameterInfo> _parameters =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<SearchParameterInfo>> _parametersByResource =
        new(StringComparer.OrdinalIgnoreCase);

    public BenchmarkSearchParameterDefinitionManager()
    {
        Register("Patient", Parameter("Patient", "name", SearchParamType.String));
        Register("Patient", Parameter("Patient", "identifier", SearchParamType.Token));
        Register(
            "Observation",
            Parameter(
                "Observation",
                "subject",
                SearchParamType.Reference,
                ["Patient"]));
        Register(
            "Observation",
            Parameter(
                "Observation",
                "patient",
                SearchParamType.Reference,
                ["Patient"]));
        Register(
            "Group",
            Parameter(
                "Group",
                "member",
                SearchParamType.Reference,
                ["Patient"]));
        Register("Group", Parameter("Group", "_tag", SearchParamType.Token));
        Register("Observation", Parameter("Observation", "code", SearchParamType.Token));

        var code = Parameter("Observation", "component-code", SearchParamType.Token);
        var quantity = Parameter(
            "Observation",
            "component-value-quantity",
            SearchParamType.Quantity);
        var codeComponent = new SearchParameterComponentInfo(
            new Uri("http://example.org/SearchParameter/component-code"))
        {
            ResolvedSearchParameter = code,
        };
        var quantityComponent = new SearchParameterComponentInfo(
            new Uri("http://example.org/SearchParameter/component-value-quantity"))
        {
            ResolvedSearchParameter = quantity,
        };
        Register(
            "Observation",
            new SearchParameterInfo(
                "code-value-quantity",
                "code-value-quantity",
                SearchParamType.Composite,
                components: [codeComponent, quantityComponent],
                baseResourceTypes: ["Observation"]));
    }

    public IEnumerable<SearchParameterInfo> AllSearchParameters =>
        _parameters.Values;

    public IReadOnlyDictionary<string, string> SearchParameterHashMap { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<SearchParameterInfo> GetSearchParameters(
        string resourceType) =>
        _parametersByResource.TryGetValue(resourceType, out var parameters)
            ? parameters
            : [];

    public bool TryGetSearchParameters(
        string resourceType,
        out IEnumerable<SearchParameterInfo> searchParameters)
    {
        bool found = _parametersByResource.TryGetValue(
            resourceType,
            out var parameters);
        searchParameters = parameters ?? [];
        return found;
    }

    public bool TryGetSearchParameter(
        string resourceType,
        string code,
        out SearchParameterInfo searchParameter) =>
        _parameters.TryGetValue(Key(resourceType, code), out searchParameter!);

    public SearchParameterInfo GetSearchParameter(
        string resourceType,
        string code) =>
        TryGetSearchParameter(resourceType, code, out var parameter)
            ? parameter
            : throw new SearchParameterNotSupportedException(resourceType, code);

    public bool TryGetSearchParameter(
        Uri definitionUri,
        out SearchParameterInfo value)
    {
        value = null!;
        return false;
    }

    public SearchParameterInfo GetSearchParameter(Uri definitionUri) =>
        throw new SearchParameterNotSupportedException(definitionUri);

    public void UpdateSearchParameterHashMap(
        Dictionary<string, string> updatedSearchParamHashMap) =>
        throw MutationNotSupported();

    public string GetSearchParameterHashForResourceType(string resourceType) =>
        "benchmark";

    public void AddNewSearchParameters(
        IReadOnlyCollection<IElement> searchParameters,
        bool calculateHash = true) =>
        throw MutationNotSupported();

    public void DeleteSearchParameter(
        string url,
        bool calculateHash = true) =>
        throw MutationNotSupported();

    private static SearchParameterInfo Parameter(
        string resourceType,
        string code,
        SearchParamType type,
        IReadOnlyList<string>? targets = null) =>
        new(
            code,
            code,
            type,
            targetResourceTypes: targets,
            baseResourceTypes: [resourceType]);

    private void Register(
        string resourceType,
        SearchParameterInfo parameter)
    {
        _parameters.Add(Key(resourceType, parameter.Code), parameter);
        if (!_parametersByResource.TryGetValue(resourceType, out var parameters))
        {
            parameters = [];
            _parametersByResource.Add(resourceType, parameters);
        }

        parameters.Add(parameter);
    }

    private static string Key(string resourceType, string code) =>
        $"{resourceType}|{code}";

    private static NotSupportedException MutationNotSupported() =>
        new("Search parser benchmarks use immutable definitions.");
}
```

- [x] **Step 4: Add the public-facade benchmark harness**

```csharp
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Generated;

namespace Ignixa.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
[CsvExporter(CsvSeparator.Comma)]
[MarkdownExporterAttribute.GitHub]
public class SearchExpressionParserBenchmarks
{
    private static readonly string[] Patient = ["Patient"];
    private static readonly string[] Observation = ["Observation"];
    private IExpressionParser _parser = null!;

    [ParamsAllValues]
    public SearchParserBenchmarkCase Case { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var definitions = new BenchmarkSearchParameterDefinitionManager();
        var valueParser = new SearchParameterExpressionParser(
            new ReferenceSearchValueParser(schemaProvider),
            schemaProvider);
        _parser = new ExpressionParser(
            () => definitions,
            valueParser,
            schemaProvider);

        foreach (SearchParserBenchmarkCase benchmarkCase in
                 Enum.GetValues<SearchParserBenchmarkCase>())
        {
            _ = Parse(benchmarkCase);
        }
    }

    [Benchmark]
    public Expression Parse() => Parse(Case);

    private Expression Parse(SearchParserBenchmarkCase benchmarkCase) =>
        benchmarkCase switch
        {
            SearchParserBenchmarkCase.Simple =>
                _parser.Parse(Patient, "name", "Smith"),
            SearchParserBenchmarkCase.Modified =>
                _parser.Parse(Patient, "name:exact", "Smith"),
            SearchParserBenchmarkCase.TypedChain =>
                _parser.Parse(
                    Observation,
                    "subject:Patient.name",
                    "Smith"),
            SearchParserBenchmarkCase.NestedReverseChain =>
                _parser.Parse(
                    Observation,
                    "patient:Patient._has:Group:member:_tag",
                    "http://example.org/tags|reviewed"),
            SearchParserBenchmarkCase.EscapedAlternative =>
                _parser.Parse(
                    Observation,
                    "code",
                    @"http://example.org|a\,b,http://example.org|c"),
            SearchParserBenchmarkCase.Composite =>
                _parser.Parse(
                    Observation,
                    "code-value-quantity",
                    "http://loinc.org|8480-6$gt120,29463-7$lt80"),
            _ => throw new UnreachableException(),
        };
}
```

Add `using System.Diagnostics;`. This file is the single before/after harness: later tasks must not edit its inputs, parser construction, attributes, or benchmark job.

- [x] **Step 5: Add the deterministic CSV comparison report generator**

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BaselineCsv,

    [Parameter(Mandatory)]
    [string] $ReplacementCsv,

    [Parameter(Mandatory)]
    [ValidateSet('Passed', 'Failed')]
    [string] $CorrectnessStatus,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [switch] $AcceptBlockingRegression
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$cases = @(
    'Simple',
    'Modified',
    'TypedChain',
    'NestedReverseChain',
    'EscapedAlternative',
    'Composite'
)

function Convert-DurationToNanoseconds([string] $value) {
    $normalized = $value.Trim().Replace(',', '')
    if ($normalized -notmatch '^([0-9.]+)\s*(ns|us|μs|ms|s)$') {
        throw "Unsupported duration '$value'."
    }

    $number = [double]::Parse($Matches[1], $culture)
    $multiplier = switch ($Matches[2]) {
        'ns' { 1 }
        'us' { 1e3 }
        'μs' { 1e3 }
        'ms' { 1e6 }
        's' { 1e9 }
    }
    return $number * $multiplier
}

function Convert-Bytes([string] $value) {
    $normalized = $value.Trim().Replace(',', '')
    if ($normalized -eq '-') {
        return 0.0
    }

    if ($normalized -notmatch '^([0-9.]+)\s*(B|KB|MB|GB)$') {
        throw "Unsupported allocation '$value'."
    }

    $number = [double]::Parse($Matches[1], $culture)
    $multiplier = switch ($Matches[2]) {
        'B' { 1 }
        'KB' { 1024 }
        'MB' { 1024 * 1024 }
        'GB' { 1024 * 1024 * 1024 }
    }
    return $number * $multiplier
}

function Convert-Gen0([string] $value) {
    $normalized = $value.Trim().Replace(',', '')
    if ($normalized -eq '-') {
        return 0.0
    }

    return [double]::Parse($normalized, $culture)
}

function Get-PercentChange([double] $before, [double] $after) {
    if ($before -eq 0.0) {
        if ($after -eq 0.0) {
            return 0.0
        }

        return [double]::PositiveInfinity
    }

    return (($after - $before) / $before) * 100.0
}

function Format-Number([double] $value) {
    return $value.ToString('N2', $culture)
}

function Format-Percent([double] $value) {
    if ([double]::IsPositiveInfinity($value)) {
        return '+∞%'
    }

    return "$($value.ToString('+0.00;-0.00;0.00', $culture))%"
}

$baselineRows = Import-Csv -LiteralPath $BaselineCsv -Delimiter ','
$replacementRows = Import-Csv -LiteralPath $ReplacementCsv -Delimiter ','
$baselineByCase = @{}
$replacementByCase = @{}
$baselineRows | ForEach-Object { $baselineByCase[$_.Case] = $_ }
$replacementRows | ForEach-Object { $replacementByCase[$_.Case] = $_ }

$comparisons = foreach ($case in $cases) {
    if (!$baselineByCase.ContainsKey($case) -or
        !$replacementByCase.ContainsKey($case)) {
        throw "Both CSV files must contain case '$case'."
    }

    $baseline = $baselineByCase[$case]
    $replacement = $replacementByCase[$case]
    $beforeMean = Convert-DurationToNanoseconds $baseline.Mean
    $afterMean = Convert-DurationToNanoseconds $replacement.Mean
    $beforeOps = 1e9 / $beforeMean
    $afterOps = 1e9 / $afterMean
    $beforeAllocated = Convert-Bytes $baseline.Allocated
    $afterAllocated = Convert-Bytes $replacement.Allocated
    $beforeGen0 = Convert-Gen0 $baseline.Gen0
    $afterGen0 = Convert-Gen0 $replacement.Gen0

    [pscustomobject]@{
        Case = $case
        BeforeMean = $beforeMean
        AfterMean = $afterMean
        MeanChange = Get-PercentChange $beforeMean $afterMean
        BeforeOps = $beforeOps
        AfterOps = $afterOps
        OpsChange = Get-PercentChange $beforeOps $afterOps
        BeforeAllocated = $beforeAllocated
        AfterAllocated = $afterAllocated
        AllocatedChange = Get-PercentChange $beforeAllocated $afterAllocated
        BeforeGen0 = $beforeGen0
        AfterGen0 = $afterGen0
        Gen0Change = Get-PercentChange $beforeGen0 $afterGen0
    }
}

$meanRatio = [Math]::Exp(
    ($comparisons |
        ForEach-Object { [Math]::Log($_.AfterMean / $_.BeforeMean) } |
        Measure-Object -Average).Average)
$geometricMeanChange = ($meanRatio - 1.0) * 100.0
$blockingRegression = $comparisons | Where-Object {
    $_.MeanChange -gt 10.0 -or
    $_.AllocatedChange -gt 10.0 -or
    $_.Gen0Change -gt 10.0
}
$faster = $geometricMeanChange -le -5.0 -and
    !($comparisons | Where-Object { $_.MeanChange -gt 5.0 }) -and
    !($comparisons | Where-Object { $_.AllocatedChange -gt 0.0 }) -and
    !($comparisons | Where-Object { $_.Gen0Change -gt 0.0 })
$classification = if ($faster) {
    'Faster'
} elseif ($geometricMeanChange -ge 5.0) {
    'Slower'
} elseif ([Math]::Abs($geometricMeanChange) -lt 5.0 -and
          !$blockingRegression) {
    'Equivalent within the 5% threshold'
} else {
    'Mixed'
}
$blockingRegressionText = if ($blockingRegression) { 'Yes' } else { 'No' }
$acceptance = if ($CorrectnessStatus -ne 'Passed') {
    'Rejected: correctness is mandatory.'
} elseif ($blockingRegression -and -not $AcceptBlockingRegression) {
    'Blocked: investigate every >10% time, allocation, or Gen0 regression and obtain explicit user acceptance before merge.'
} elseif ($blockingRegression) {
    'Accepted by explicit user approval after investigation of the blocking regression.'
} else {
    'Accepted: correctness passed and no blocking performance regression was measured.'
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Superpower Search Expression Parser Benchmark Comparison')
$lines.Add('')
$lines.Add("**Correctness:** **$CorrectnessStatus**")
$lines.Add('')
$lines.Add("**Performance classification:** **$classification**")
$lines.Add('')
$lines.Add("**Blocking regression detected:** **$blockingRegressionText**")
$lines.Add('')
$lines.Add("**Acceptance:** $acceptance")
$lines.Add('')
$lines.Add("**Geometric mean time change:** $(Format-Percent $geometricMeanChange)")
$lines.Add('')
$lines.Add('| Case | Baseline mean (ns) | Replacement mean (ns) | Mean Δ | Baseline ops/s | Replacement ops/s | Ops/s Δ | Baseline allocated (B) | Replacement allocated (B) | Allocated Δ | Baseline Gen0 | Replacement Gen0 | Gen0 Δ |')
$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach ($row in $comparisons) {
    $lines.Add(
        "| $($row.Case) | $(Format-Number $row.BeforeMean) | $(Format-Number $row.AfterMean) | $(Format-Percent $row.MeanChange) | $(Format-Number $row.BeforeOps) | $(Format-Number $row.AfterOps) | $(Format-Percent $row.OpsChange) | $(Format-Number $row.BeforeAllocated) | $(Format-Number $row.AfterAllocated) | $(Format-Percent $row.AllocatedChange) | $(Format-Number $row.BeforeGen0) | $(Format-Number $row.AfterGen0) | $(Format-Percent $row.Gen0Change) |")
}

$lines.Add('')
$lines.Add('Mean and operations/sec changes are calculated per case; operations/sec is `1,000,000,000 / mean nanoseconds`. Allocation and Gen0 percentages use the handwritten parser as the denominator. A zero-to-zero change is 0%; a zero-to-nonzero change is +∞%.')
$lines.Add('')
$lines.Add('“Faster” requires at least a 5% geometric-mean time improvement, no case slower by more than 5%, and no allocation or Gen0 increase. A regression above 10% in mean time, allocated bytes, or Gen0 for any case blocks acceptance pending investigation and explicit user approval. Correctness failures always reject the replacement.')

$directory = [IO.Path]::GetDirectoryName(
    [IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($directory) | Out-Null
[IO.File]::WriteAllLines($OutputPath, $lines)
$lines -join [Environment]::NewLine
```

- [x] **Step 6: Restore and build the benchmark harness before changing parser production code**

Run:

```powershell
dotnet restore bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj
dotnet build bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-restore
```

Expected: restore succeeds and the Release build reports `0 Warning(s)` and `0 Error(s)`.

- [x] **Step 7: Run and record the current handwritten parser**

Run:

```powershell
dotnet run --project bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-build -- --filter "*SearchExpressionParserBenchmarks*" --artifacts "BenchmarkDotNet.Artifacts/search-parser-baseline" --launchCount 1 --warmupCount 5 --iterationCount 15
```

Expected: BenchmarkDotNet completes exactly six `Parse` rows (`Simple`, `Modified`, `TypedChain`, `NestedReverseChain`, `EscapedAlternative`, and `Composite`) without exceptions; each row reports a mean, Gen0, and allocated bytes.

- [x] **Step 8: Copy the baseline summaries and lock the harness hash**

Run:

```powershell
$results = 'BenchmarkDotNet.Artifacts/search-parser-baseline/results'
$destination = 'docs/features/search/benchmarks'
[IO.Directory]::CreateDirectory($destination) | Out-Null
Copy-Item -LiteralPath "$results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report.csv" -Destination "$destination/2026-07-10-handwritten-parser.csv"
Copy-Item -LiteralPath "$results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report-github.md" -Destination "$destination/2026-07-10-handwritten-parser.md"
$harnessFiles = @(
    'bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj',
    'bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs',
    'bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs',
    'bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs'
)
$hashes = $harnessFiles | ForEach-Object {
    "$((Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash)  $_"
}
[IO.File]::WriteAllLines(
    "$destination/2026-07-10-search-parser-harness.sha256",
    $hashes)
```

Expected: the benchmark directory contains the CSV, GitHub Markdown environment report, and four-entry SHA-256 manifest. Open the CSV and verify all six case names have populated `Mean`, `Gen0`, and `Allocated` columns.

- [x] **Step 9: Re-run current correctness tests and block production work if either baseline failed**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.ExpressionParserCharacterizationTests" --no-restore
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers src/Core/Ignixa.Search/Indexing/StringExtensions.cs
```

Expected: characterization reports `Passed! - Failed: 0, Passed: 6, Skipped: 0`; the production parser diff is empty. Do not start Task 3 unless both this test and the six-case baseline completed successfully.

- [x] **Step 10: Checkpoint prepared; commit remains unapproved**

Run:

```powershell
git --no-pager diff -- bench/Ignixa.Benchmarks tools/benchmarks/Compare-SearchParserBenchmarks.ps1 docs/features/search/benchmarks
git status --short
```

Proposed commit subject: `Record handwritten search parser benchmark`

Proposed commit message:

```text
Record handwritten search parser benchmark

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs tools/benchmarks/Compare-SearchParserBenchmarks.ps1 docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256 docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv docs/features/search/benchmarks/2026-07-10-handwritten-parser.md
git commit -m "Record handwritten search parser benchmark" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 3: Parse ordinary, modified, and typed forward keys

**Files:**
- Modify: `src/Core/Ignixa.Search/Ignixa.Search.csproj:29-33`
- Modify: `src/Core/Ignixa.Search/Properties/AssemblyInfo.cs:6-10`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeyTokenKind.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeySyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ParameterKeySyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ForwardChainKeySyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyTokenizer.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs`

- [x] **Step 1: Write the failing key grammar tests**

```csharp
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchKeyGrammarTests
{
    [Theory]
    [InlineData("name", "name", null)]
    [InlineData("name:exact", "name", "exact")]
    [InlineData("identifier:of-type", "identifier", "of-type")]
    public void GivenTerminalKey_WhenParsing_ThenReturnsParameterSyntax(
        string key,
        string expectedName,
        string? expectedModifier)
    {
        var result = SearchKeyGrammar.ParseParameter(key);

        var parameter = result.ShouldBeOfType<ParameterKeySyntax>();
        parameter.Name.ShouldBe(expectedName);
        parameter.Modifier.ShouldBe(expectedModifier);
    }

    [Theory]
    [InlineData("subject.name", "subject", null, "name")]
    [InlineData("subject:Patient.name", "subject", "Patient", "name")]
    public void GivenForwardChain_WhenParsing_ThenReturnsRecursiveSyntax(
        string key,
        string expectedReference,
        string? expectedTarget,
        string expectedTerminal)
    {
        var result = SearchKeyGrammar.ParseParameter(key);

        var chain = result.ShouldBeOfType<ForwardChainKeySyntax>();
        chain.ReferenceName.ShouldBe(expectedReference);
        chain.TargetResourceType.ShouldBe(expectedTarget);
        chain.Next.ShouldBeOfType<ParameterKeySyntax>().Name.ShouldBe(expectedTerminal);
    }
}
```

- [x] **Step 2: Run the tests and verify they fail before the grammar exists**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyGrammarTests" --no-restore
```

Expected: build failure with `CS0246`/`CS0103` for `SearchKeyGrammar`, `ParameterKeySyntax`, or `ForwardChainKeySyntax`.

- [x] **Step 3: Add the direct package reference and internal visibility**

Add to `Ignixa.Search.csproj`:

```xml
<PackageReference Include="Superpower" />
```

Add to `AssemblyInfo.cs`:

```csharp
[assembly: InternalsVisibleTo("Ignixa.Application.Tests")]
```

Do not add a version to the project file; `Directory.Packages.props:64` centrally pins Superpower 3.1.0.

- [x] **Step 4: Restore the graph after adding the direct package**

Run:

```powershell
dotnet restore test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj
```

Expected: `Restore succeeded` (or `All projects are up-to-date for restore`) with Superpower 3.1.0 resolved directly for `Ignixa.Search`.

- [x] **Step 5: Add immutable key syntax types and tokenizer**

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal enum SearchKeyTokenKind
{
    Identifier,
    Colon,
    Dot,
    Asterisk,
}
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal abstract record SearchKeySyntax;
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record ParameterKeySyntax(string Name, string? Modifier) : SearchKeySyntax;
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record ForwardChainKeySyntax(
    string ReferenceName,
    string? TargetResourceType,
    SearchKeySyntax Next) : SearchKeySyntax;
```

```csharp
using Ignixa.Search.Expressions.Parsers.Syntax;
using Superpower;
using Superpower.Parsers;
using Superpower.Tokenizers;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchKeyTokenizer
{
    public static Tokenizer<SearchKeyTokenKind> Instance { get; } =
        new TokenizerBuilder<SearchKeyTokenKind>()
            .Match(Span.Regex("[A-Za-z_][A-Za-z0-9_-]*"), SearchKeyTokenKind.Identifier, requireDelimiters: false)
            .Match(Character.EqualTo(':'), SearchKeyTokenKind.Colon)
            .Match(Character.EqualTo('.'), SearchKeyTokenKind.Dot)
            .Match(Character.EqualTo('*'), SearchKeyTokenKind.Asterisk)
            .Build();
}
```

- [x] **Step 6: Add the first recursive key grammar**

```csharp
using Ignixa.Search.Expressions.Parsers.Syntax;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchKeyGrammar
{
    private static readonly TokenListParser<SearchKeyTokenKind, string> Identifier =
        Token.EqualTo(SearchKeyTokenKind.Identifier).Select(token => token.ToStringValue());

    private static readonly TokenListParser<SearchKeyTokenKind, string?> OptionalQualifier =
        (from colon in Token.EqualTo(SearchKeyTokenKind.Colon)
         from qualifier in Identifier
         select (string?)qualifier)
        .OptionalOrDefault();

    private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Parameter =
        from name in Identifier
        from modifier in OptionalQualifier
        select (SearchKeySyntax)new ParameterKeySyntax(name, modifier);

    private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Forward =
        from reference in Identifier
        from target in OptionalQualifier
        from dot in Token.EqualTo(SearchKeyTokenKind.Dot)
        from next in Parse.Ref(() => Key)
        select (SearchKeySyntax)new ForwardChainKeySyntax(reference, target, next);

    private static TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Key =>
        Forward.Try().Or(Parameter);

    public static SearchKeySyntax ParseParameter(string source)
    {
        var tokenization = SearchKeyTokenizer.Instance.TryTokenize(source);
        if (!tokenization.HasValue)
        {
            throw SearchParseExceptionMapper.FromTokenization("search key", tokenization);
        }

        var parsing = Key.AtEnd().TryParse(tokenization.Value);
        if (!parsing.HasValue)
        {
            throw SearchParseExceptionMapper.FromParsing("search key", parsing);
        }

        return parsing.Value;
    }
}
```

At this increment, add the mapper method signatures so the grammar compiles; Task 14 supplies the localized final message:

```csharp
using Ignixa.Search.Indexing;
using Superpower.Model;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchParseExceptionMapper
{
    public static InvalidSearchOperationException FromTokenization<T>(
        string subject,
        Result<T> result) =>
        new($"Malformed {subject} at line {result.ErrorPosition.Line}, column {result.ErrorPosition.Column}: {result.FormatErrorMessageFragment()}");

    public static InvalidSearchOperationException FromParsing<TKind, TValue>(
        string subject,
        TokenListParserResult<TKind, TValue> result) =>
        new($"Malformed {subject} at line {result.ErrorPosition.Line}, column {result.ErrorPosition.Column}: {result.FormatErrorMessageFragment()}");
}
```

- [x] **Step 7: Run the focused tests and verify they pass**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyGrammarTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

- [x] **Step 8: Checkpoint prepared; commit remains unapproved**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Ignixa.Search.csproj src/Core/Ignixa.Search/Properties/AssemblyInfo.cs src/Core/Ignixa.Search/Expressions/Parsers test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs
git status --short
```

Proposed commit subject: `Parse search parameter keys with Superpower`

Proposed commit message:

```text
Parse search parameter keys with Superpower

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Ignixa.Search.csproj src/Core/Ignixa.Search/Properties/AssemblyInfo.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeyTokenKind.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ParameterKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ForwardChainKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyTokenizer.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs
git commit -m "Parse search parameter keys with Superpower" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 4: Add recursive reverse and mixed chain grammar

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ReverseChainKeySyntax.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs`

- [x] **Step 1: Add failing nested reverse-chain tests**

```csharp
[Fact]
public void GivenReverseChain_WhenParsing_ThenReturnsReverseSyntax()
{
    var result = SearchKeyGrammar.ParseParameter("_has:Observation:subject:code");

    var reverse = result.ShouldBeOfType<ReverseChainKeySyntax>();
    reverse.SourceResourceType.ShouldBe("Observation");
    reverse.ReferenceName.ShouldBe("subject");
    reverse.Next.ShouldBeOfType<ParameterKeySyntax>().Name.ShouldBe("code");
}

[Fact]
public void GivenForwardThenReverseChain_WhenParsing_ThenPreservesArbitraryNesting()
{
    var result = SearchKeyGrammar.ParseParameter(
        "patient:Patient._has:Group:member:_tag");

    var forward = result.ShouldBeOfType<ForwardChainKeySyntax>();
    forward.ReferenceName.ShouldBe("patient");
    forward.TargetResourceType.ShouldBe("Patient");
    var reverse = forward.Next.ShouldBeOfType<ReverseChainKeySyntax>();
    reverse.SourceResourceType.ShouldBe("Group");
    reverse.ReferenceName.ShouldBe("member");
    reverse.Next.ShouldBeOfType<ParameterKeySyntax>().Name.ShouldBe("_tag");
}
```

- [x] **Step 2: Run the tests and verify the reverse syntax is absent**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyGrammarTests" --no-restore
```

Expected: build failure `CS0246: The type or namespace name 'ReverseChainKeySyntax' could not be found`.

- [x] **Step 3: Add the reverse syntax record**

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record ReverseChainKeySyntax(
    string SourceResourceType,
    string ReferenceName,
    SearchKeySyntax Next) : SearchKeySyntax;
```

- [x] **Step 4: Put the recursive reverse parser before the forward parser**

Add this parser and update `Key`:

```csharp
private static readonly TokenListParser<SearchKeyTokenKind, string> Has =
    Identifier.Where(value => string.Equals(value, "_has", StringComparison.Ordinal));

private static readonly TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Reverse =
    from marker in Has
    from firstColon in Token.EqualTo(SearchKeyTokenKind.Colon)
    from sourceType in Identifier
    from secondColon in Token.EqualTo(SearchKeyTokenKind.Colon)
    from reference in Identifier
    from thirdColon in Token.EqualTo(SearchKeyTokenKind.Colon)
    from next in Parse.Ref(() => Key)
    select (SearchKeySyntax)new ReverseChainKeySyntax(sourceType, reference, next);

private static TokenListParser<SearchKeyTokenKind, SearchKeySyntax> Key =>
    Reverse.Try().Or(Forward.Try()).Or(Parameter);
```

`Parse.Ref(() => Key)` is required in both chain directions; do not impose a chain-depth limit or recursively invoke the public string parser.

- [x] **Step 5: Run the grammar tests and verify all nesting cases pass**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyGrammarTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0`.

- [x] **Step 6: Checkpoint prepared; commit remains unapproved**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ReverseChainKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs
git status --short
```

Proposed commit subject: `Parse nested reverse search chains`

Proposed commit message:

```text
Parse nested reverse search chains

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/Syntax/ReverseChainKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyGrammarTests.cs
git commit -m "Parse nested reverse search chains" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 5: Bind parameter and chain semantics

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundSearchKey.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundParameterKey.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundChainKey.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:176-213,216-310`

- [x] **Step 1: Write failing semantic binding tests**

```csharp
using Ignixa.Search;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchKeyBinderTests
{
    [Fact]
    public void GivenTypedReferenceChain_WhenBinding_ThenResolvesTargetAndTerminal()
    {
        var context = new SearchParserTestContext();
        var subject = context.Add("Observation", "subject", SearchParamType.Reference, ["Patient", "Group"]);
        var name = context.Add("Patient", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseParameter("subject:Patient.name");

        var result = binder.Bind(["Observation"], syntax);

        var chain = result.ShouldBeOfType<BoundChainKey>();
        chain.ReferenceSearchParameter.ShouldBeSameAs(subject);
        chain.ResourceTypes.ShouldBe(["Observation"]);
        chain.TargetResourceTypes.ShouldBe(["Patient"]);
        chain.Reversed.ShouldBeFalse();
        chain.Next.ShouldBeOfType<BoundParameterKey>()
            .SearchParameter.ShouldBeSameAs(name);
    }

    [Fact]
    public void GivenNonReferenceForwardSegment_WhenBinding_ThenPreservesSemanticException()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "code", SearchParamType.Token);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseParameter("code.name");

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.Bind(["Observation"], syntax));

        exception.Message.ShouldBe(Resources.ChainedParameterMustBeReferenceSearchParamType);
    }

    [Fact]
    public void GivenUntypedAmbiguousForwardChain_WhenBinding_ThenRequiresTargetType()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient", "Group"]);
        context.Add("Patient", "name", SearchParamType.String);
        context.Add("Group", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseParameter("subject.name");

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => binder.Bind(["Observation"], syntax));

        exception.Message.ShouldContain("subject:Patient");
        exception.Message.ShouldContain("subject:Group");
    }

    [Fact]
    public void GivenOnlyOneTargetSupportsTerminal_WhenBinding_ThenFiltersUnsupportedTarget()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient", "Group"]);
        context.Add("Patient", "name", SearchParamType.String);
        context.DefinitionManager
            .GetSearchParameter("Group", "name")
            .Returns(_ => throw new SearchParameterNotSupportedException(
                "Group",
                "name"));
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseParameter("subject.name");

        var result = binder.Bind(["Observation"], syntax);

        result.ShouldBeOfType<BoundChainKey>()
            .TargetResourceTypes.ShouldBe(["Patient"]);
    }

    [Fact]
    public void GivenDifferentDefinitionManagers_WhenBinding_ThenUsesActiveTenantVersionContext()
    {
        var first = new SearchParserTestContext();
        var firstName = first.Add("Patient", "name", SearchParamType.String);
        var second = new SearchParserTestContext();
        var secondName = second.Add("Patient", "name", SearchParamType.Token);
        var syntax = new ParameterKeySyntax("name", null);

        var firstBound = new SearchKeyBinder(
            first.DefinitionManager,
            first.SchemaProvider).Bind(["Patient"], syntax);
        var secondBound = new SearchKeyBinder(
            second.DefinitionManager,
            second.SchemaProvider).Bind(["Patient"], syntax);

        firstBound.ShouldBeOfType<BoundParameterKey>()
            .SearchParameter.ShouldBeSameAs(firstName);
        secondBound.ShouldBeOfType<BoundParameterKey>()
            .SearchParameter.ShouldBeSameAs(secondName);
    }

    [Fact]
    public void GivenDifferentMultiResourceDefinitions_WhenBinding_ThenRejectsNonCommonParameter()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);
        context.Add("Practitioner", "name", SearchParamType.String);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);

        var exception = Should.Throw<BadSearchRequestException>(
            () => binder.Bind(
                ["Patient", "Practitioner"],
                new ParameterKeySyntax("name", null)));

        exception.Message.ShouldContain("must be common");
    }

    [Fact]
    public void GivenLiteralTypeModifier_WhenBinding_ThenRejectsUnsupportedModifier()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);

        Should.Throw<InvalidSearchOperationException>(
            () => binder.Bind(
                ["Observation"],
                new ParameterKeySyntax("subject", "type")));
    }
}
```

- [x] **Step 2: Run the binder tests and verify the bound model is missing**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyBinderTests" --no-restore
```

Expected: build failure with `CS0246` for `SearchKeyBinder`, `BoundChainKey`, or `BoundParameterKey`.

- [x] **Step 3: Add the immutable bound key model**

```csharp
namespace Ignixa.Search.Expressions.Parsers.Binding;

internal abstract record BoundSearchKey;
```

```csharp
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers.Binding;

internal sealed record BoundParameterKey(
    SearchParameterInfo SearchParameter,
    SearchModifier? Modifier) : BoundSearchKey;
```

```csharp
using System.Collections.Immutable;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers.Binding;

internal sealed record BoundChainKey(
    ImmutableArray<string> ResourceTypes,
    SearchParameterInfo ReferenceSearchParameter,
    ImmutableArray<string> TargetResourceTypes,
    bool Reversed,
    BoundSearchKey Next) : BoundSearchKey;
```

- [x] **Step 4: Implement definition, modifier, and target binding**

Create `SearchKeyBinder` with these concrete entry points and semantic branches:

```csharp
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using Ignixa.Abstractions;
using Ignixa.Search.Definition;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions.Parsers.Binding;
using System.Diagnostics;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

internal sealed class SearchKeyBinder(
    ISearchParameterDefinitionManager definitionManager,
    IFhirSchemaProvider schemaProvider)
{
    private static readonly FrozenDictionary<string, SearchModifierCode> Modifiers =
        Enum.GetValues<SearchModifierCode>()
            .Where(value => value != SearchModifierCode.Type)
            .ToFrozenDictionary(value => value.GetLiteral(), StringComparer.Ordinal);

    public BoundSearchKey Bind(string[] resourceTypes, SearchKeySyntax syntax) =>
        syntax switch
        {
            ParameterKeySyntax parameter => BindParameter(resourceTypes, parameter),
            ForwardChainKeySyntax forward => BindForward(resourceTypes, forward),
            ReverseChainKeySyntax reverse => BindReverse(resourceTypes, reverse),
            _ => throw new UnreachableException(),
        };

    private BoundParameterKey BindParameter(
        string[] resourceTypes,
        ParameterKeySyntax syntax)
    {
        SearchParameterInfo parameter = GetCommonParameter(resourceTypes, syntax.Name);
        return new BoundParameterKey(parameter, BindModifier(parameter, syntax.Modifier));
    }

    private BoundSearchKey BindForward(
        string[] resourceTypes,
        ForwardChainKeySyntax syntax)
    {
        SearchParameterInfo reference = GetCommonParameter(resourceTypes, syntax.ReferenceName);
        EnsureReference(reference);

        string[] candidates = GetForwardTargets(reference, syntax.TargetResourceType);
        var matches = new List<BoundChainKey>();
        foreach (string candidate in candidates)
        {
            try
            {
                BoundSearchKey next = Bind([candidate], syntax.Next);
                matches.Add(new BoundChainKey(
                    resourceTypes.ToImmutableArray(),
                    reference,
                    [candidate],
                    false,
                    next));
            }
            catch (SearchParameterNotSupportedException)
            {
                // Preserve the existing unsupported-target filtering boundary only.
            }
        }

        return matches.Count switch
        {
            0 => throw new InvalidSearchOperationException(Resources.ChainedParameterNotSupported),
            1 => matches[0],
            _ => throw new InvalidSearchOperationException(string.Format(
                CultureInfo.CurrentCulture,
                Resources.ChainedParameterSpecifyType,
                reference.Name,
                string.Join(
                    Resources.OrDelimiter,
                    matches.Select(match => $"{reference.Code}:{match.TargetResourceTypes[0]}")))),
        };
    }

    private BoundSearchKey BindReverse(
        string[] resourceTypes,
        ReverseChainKeySyntax syntax)
    {
        if (!schemaProvider.ResourceTypeNames.Contains(syntax.SourceResourceType))
        {
            throw new InvalidSearchOperationException(
                string.Format(Resources.ResourceNotSupported, syntax.SourceResourceType));
        }

        SearchParameterInfo reference =
            definitionManager.GetSearchParameter(syntax.SourceResourceType, syntax.ReferenceName);
        EnsureReference(reference);
        string[] targets = reference.TargetResourceTypes.Intersect(
            resourceTypes,
            StringComparer.OrdinalIgnoreCase).ToArray();
        if (targets.Length == 0)
        {
            throw new InvalidSearchOperationException(Resources.ChainedParameterNotSupported);
        }

        BoundSearchKey next = Bind([syntax.SourceResourceType], syntax.Next);
        return new BoundChainKey(
            [syntax.SourceResourceType],
            reference,
            targets.ToImmutableArray(),
            true,
            next);
    }
```

Complete the same file with these helpers; the catch remains narrow and semantic messages remain resource-backed:

```csharp
    private SearchParameterInfo GetCommonParameter(string[] resourceTypes, string code)
    {
        SearchParameterInfo first = definitionManager.GetSearchParameter(resourceTypes[0], code);
        foreach (string resourceType in resourceTypes.Skip(1))
        {
            SearchParameterInfo next = definitionManager.GetSearchParameter(resourceType, code);
            if (!ReferenceEquals(first, next))
            {
                throw new BadSearchRequestException(string.Format(
                    Resources.SearchParameterMustBeCommon,
                    code,
                    resourceTypes[0],
                    resourceType));
            }
        }

        return first;
    }

    private SearchModifier? BindModifier(
        SearchParameterInfo parameter,
        string? modifier)
    {
        if (modifier is null)
        {
            return null;
        }

        if (Modifiers.TryGetValue(modifier, out SearchModifierCode code))
        {
            return new SearchModifier(code);
        }

        if (parameter.Type == SearchParamType.Reference &&
            parameter.TargetResourceTypes.Contains(modifier, StringComparer.OrdinalIgnoreCase))
        {
            return new SearchModifier(SearchModifierCode.Type, modifier);
        }

        throw new InvalidSearchOperationException(
            string.Format(Resources.ModifierNotSupported, modifier, parameter.Code));
    }

    private string[] GetForwardTargets(
        SearchParameterInfo reference,
        string? requestedTarget)
    {
        if (requestedTarget is null)
        {
            return reference.TargetResourceTypes.ToArray();
        }

        if (!schemaProvider.ResourceTypeNames.Contains(requestedTarget))
        {
            throw new InvalidSearchOperationException(
                string.Format(Resources.ResourceNotSupported, requestedTarget));
        }

        return reference.TargetResourceTypes
            .Where(target => string.Equals(
                target,
                requestedTarget,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static void EnsureReference(SearchParameterInfo parameter)
    {
        if (parameter.Type != SearchParamType.Reference)
        {
            throw new InvalidSearchOperationException(
                Resources.ChainedParameterMustBeReferenceSearchParamType);
        }
    }
}
```

`SearchModifierCode.Type` is deliberately excluded from `Modifiers`: it represents a concrete reference target such as `subject:Patient`, not a standalone `:type` modifier. The current parser leaks an `ArgumentException` for `subject:type` by constructing `SearchModifierCode.Type` without a resource type. Treat that malformed semantic case as `InvalidSearchOperationException(Resources.ModifierNotSupported)` instead.

- [x] **Step 5: Run binder tests and the key grammar regression**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyBinderTests|FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKeyGrammarTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 14, Skipped: 0`.

- [x] **Step 6: Checkpoint prepared; commit remains unapproved**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/Binding src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs
git status --short
```

Proposed commit subject: `Bind search key semantics`

Proposed commit message:

```text
Bind search key semantics

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/Binding src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchKeyBinderTests.cs
git commit -m "Bind search key semantics" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 6: Parse and bind includes and `_not-referenced`

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/IncludeKeySyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/NotReferencedKeySyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundIncludeKey.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundNotReferencedKey.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:78-155,345-382`

- [x] **Step 1: Write failing include and `_not-referenced` tests**

```csharp
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Binding;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class IncludeAndNotReferencedParserTests
{
    [Theory]
    [InlineData("Observation:subject", "Observation", "subject", null, false)]
    [InlineData("Observation:subject:Patient", "Observation", "subject", "Patient", false)]
    [InlineData("Observation:*", "Observation", null, null, true)]
    [InlineData("*:*", "*", null, null, true)]
    public void GivenIncludeValue_WhenParsing_ThenReturnsIncludeSyntax(
        string value,
        string sourceType,
        string? parameter,
        string? targetType,
        bool wildcard)
    {
        var result = SearchKeyGrammar.ParseInclude(value);

        result.SourceResourceType.ShouldBe(sourceType);
        result.SearchParameterName.ShouldBe(parameter);
        result.TargetResourceType.ShouldBe(targetType);
        result.Wildcard.ShouldBe(wildcard);
    }

    [Theory]
    [InlineData("*:*", null, null)]
    [InlineData("Observation:*", "Observation", null)]
    [InlineData("Observation:subject", "Observation", "subject")]
    public void GivenNotReferencedValue_WhenParsing_ThenReturnsWildcardAwareSyntax(
        string value,
        string? sourceType,
        string? path)
    {
        var result = SearchKeyGrammar.ParseNotReferenced(value);

        result.SourceResourceType.ShouldBe(sourceType);
        result.ReferencePath.ShouldBe(path);
    }

    [Theory]
    [InlineData("Observation:")]
    [InlineData("Observation:subject.name")]
    public void GivenMalformedNotReferencedValue_WhenParsing_ThenReturnsPositionedSyntaxError(
        string value)
    {
        var exception = Should.Throw<InvalidSearchOperationException>(
            () => SearchKeyGrammar.ParseNotReferenced(value));

        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column");
    }

    [Fact]
    public void GivenReverseIncludeWithoutTarget_WhenBinding_ThenDefaultsToSearchType()
    {
        var context = new SearchParserTestContext();
        var subject = context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
        var binder = new SearchKeyBinder(context.DefinitionManager, context.SchemaProvider);
        var syntax = SearchKeyGrammar.ParseInclude("Observation:subject");

        var result = binder.BindInclude(["Patient"], syntax, isReversed: true, iterate: false);

        result.ReferenceSearchParameter.ShouldBeSameAs(subject);
        result.TargetResourceType.ShouldBe("Patient");
    }
}
```

- [x] **Step 2: Run the tests and verify the special key APIs are absent**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.IncludeAndNotReferencedParserTests" --no-restore
```

Expected: build failure with `CS0117` for `SearchKeyGrammar.ParseInclude` or `ParseNotReferenced`, followed by missing syntax/bound types.

- [x] **Step 3: Add immutable syntax and bound records**

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record IncludeKeySyntax(
    string SourceResourceType,
    string? SearchParameterName,
    string? TargetResourceType,
    bool Wildcard) : SearchKeySyntax;
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record NotReferencedKeySyntax(
    string? SourceResourceType,
    string? ReferencePath) : SearchKeySyntax;
```

```csharp
using System.Collections.Immutable;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers.Binding;

internal sealed record BoundIncludeKey(
    SearchParameterInfo? ReferenceSearchParameter,
    string SourceResourceType,
    string? TargetResourceType,
    ImmutableArray<string> ReferencedTypes,
    bool Wildcard);
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Binding;

internal sealed record BoundNotReferencedKey(
    string? SourceResourceType,
    string? ReferencePath);
```

- [x] **Step 4: Add dedicated include and `_not-referenced` grammar entry points**

```csharp
private static readonly TokenListParser<SearchKeyTokenKind, string> IncludeSource =
    Token.EqualTo(SearchKeyTokenKind.Asterisk)
        .Select(_ => "*")
        .Or(Identifier);

private static readonly TokenListParser<SearchKeyTokenKind, IncludeKeySyntax> Include =
    from source in IncludeSource
    from firstColon in Token.EqualTo(SearchKeyTokenKind.Colon)
    from tail in
        Token.EqualTo(SearchKeyTokenKind.Asterisk)
            .Select(_ => new IncludeKeySyntax(source, null, null, true))
            .Or(
                from parameter in Identifier
                from target in OptionalQualifier
                select new IncludeKeySyntax(source, parameter, target, false))
    select tail;

private static readonly TokenListParser<SearchKeyTokenKind, string?> WildcardOrIdentifier =
    Token.EqualTo(SearchKeyTokenKind.Asterisk)
        .Select(_ => (string?)null)
        .Or(Identifier.Select(value => (string?)value));

private static readonly TokenListParser<SearchKeyTokenKind, string?> ReferencePath =
    Token.EqualTo(SearchKeyTokenKind.Asterisk)
        .Select(_ => (string?)null)
        .Or(Identifier
            .Where(value => char.IsLetter(value[0]))
            .Select(value => (string?)value));

private static readonly TokenListParser<SearchKeyTokenKind, NotReferencedKeySyntax> NotReferenced =
    from source in WildcardOrIdentifier
    from colon in Token.EqualTo(SearchKeyTokenKind.Colon)
    from path in ReferencePath
    select new NotReferencedKeySyntax(source, path);

public static IncludeKeySyntax ParseInclude(string source) =>
    Parse(source, "include", Include);

public static NotReferencedKeySyntax ParseNotReferenced(string source) =>
    Parse(source, "_not-referenced", NotReferenced);

private static TValue Parse<TValue>(
    string source,
    string subject,
    TokenListParser<SearchKeyTokenKind, TValue> parser)
{
    var tokenization = SearchKeyTokenizer.Instance.TryTokenize(source);
    if (!tokenization.HasValue)
    {
        throw SearchParseExceptionMapper.FromTokenization(subject, tokenization);
    }

    var parsing = parser.AtEnd().TryParse(tokenization.Value);
    if (!parsing.HasValue)
    {
        throw SearchParseExceptionMapper.FromParsing(subject, parsing);
    }

    return parsing.Value;
}
```

Refactor `ParseParameter` to call the same `Parse(source, "search key", Key)` helper. This makes `Observation:subject:` fail in syntax parsing rather than becoming an empty semantic target.

The source wildcard in `*:*` is separate from the search-parameter wildcard. Both are required to preserve the existing `_revinclude=*:*` behavior covered by `IncludeAdvancedTests`.

- [x] **Step 5: Add include and `_not-referenced` semantic binding**

Add these methods to `SearchKeyBinder`:

```csharp
public BoundIncludeKey BindInclude(
    string[] resourceTypes,
    IncludeKeySyntax syntax,
    bool isReversed,
    bool iterate)
{
    if (resourceTypes.Length == 1 &&
        resourceTypes[0].Equals("DomainResource", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidSearchOperationException(Resources.IncludeCannotBeAgainstBase);
    }

    if (syntax.TargetResourceType is { } target &&
        !schemaProvider.ResourceTypeNames.Contains(target))
    {
        throw new InvalidSearchOperationException(string.Format(
            Resources.IncludeInvalidTargetResourceType,
            isReversed ? "_revinclude" : "_include",
            syntax.SourceResourceType,
            syntax.SearchParameterName,
            target));
    }

    SearchParameterInfo? reference = syntax.Wildcard
        ? null
        : definitionManager.GetSearchParameter(
            syntax.SourceResourceType,
            syntax.SearchParameterName!);

    string? targetType = syntax.TargetResourceType;
    if (isReversed && !iterate && targetType is null && resourceTypes.Length > 0)
    {
        targetType = resourceTypes[0];
    }

    ImmutableArray<string> referencedTypes = syntax.Wildcard
        ? resourceTypes
            .SelectMany(definitionManager.GetSearchParameters)
            .Where(parameter => parameter.Type == SearchParamType.Reference)
            .SelectMany(parameter => parameter.TargetResourceTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray()
        : [];

    return new BoundIncludeKey(
        reference,
        syntax.SourceResourceType,
        targetType,
        referencedTypes,
        syntax.Wildcard);
}

public BoundNotReferencedKey BindNotReferenced(NotReferencedKeySyntax syntax)
{
    if (syntax.SourceResourceType is { } source &&
        !schemaProvider.ResourceTypeNames.Contains(source))
    {
        throw new InvalidSearchOperationException(
            $"Invalid resource type in _not-referenced: '{source}'");
    }

    return new BoundNotReferencedKey(syntax.SourceResourceType, syntax.ReferencePath);
}
```

The dedicated `ReferencePath` parser enforces the existing `_not-referenced` path rule (letter start, then alphanumeric, `_`, or `-`) and rejects empty paths, extra colons, and whitespace as positioned syntax errors.

- [x] **Step 6: Run the special key tests and all key tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.IncludeAndNotReferencedParserTests|FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchKey" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 27, Skipped: 0`.

- [ ] **Step 7: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/Syntax src/Core/Ignixa.Search/Expressions/Parsers/Binding src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs
git status --short
```

Proposed commit subject: `Parse include and not-referenced keys`

Proposed commit message:

```text
Parse include and not-referenced keys

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/Syntax/IncludeKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/NotReferencedKeySyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundIncludeKey.cs src/Core/Ignixa.Search/Expressions/Parsers/Binding/BoundNotReferencedKey.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyGrammar.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchKeyBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/IncludeAndNotReferencedParserTests.cs
git commit -m "Parse include and not-referenced keys" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 7: Tokenize escaped search values

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs`
- Reference: `src/Core/Ignixa.Search/Indexing/StringExtensions.cs:18-53,145-173`

- [x] **Step 1: Write failing tokenizer tests for all FHIR escapes**

```csharp
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchValueTokenizerTests
{
    [Fact]
    public void GivenEscapedAndUnescapedSeparators_WhenTokenizing_ThenOnlyUnescapedSeparatorsAreStructural()
    {
        var result = SearchValueTokenizer.Instance.TryTokenize(
            @"a\,b\$c\|d\\e,f$g|h");

        result.HasValue.ShouldBeTrue(result.ToString());
        result.Value.Select(token => (token.Kind, token.ToStringValue())).ShouldBe(
        [
            (SearchValueTokenKind.Text, @"a\,b\$c\|d\\e"),
            (SearchValueTokenKind.Comma, ","),
            (SearchValueTokenKind.Text, "f"),
            (SearchValueTokenKind.Dollar, "$"),
            (SearchValueTokenKind.Text, "g"),
            (SearchValueTokenKind.Pipe, "|"),
            (SearchValueTokenKind.Text, "h"),
        ]);
    }

    [Theory]
    [InlineData(@"value\")]
    [InlineData(@"value\q")]
    public void GivenInvalidEscape_WhenTokenizing_ThenReturnsPositionedFailure(string value)
    {
        var result = SearchValueTokenizer.Instance.TryTokenize(value);

        result.HasValue.ShouldBeFalse();
        result.ErrorPosition.Line.ShouldBe(1);
        result.ErrorPosition.Column.ShouldBe(6);
        result.Expectations.ShouldContain("valid FHIR escape");
    }
}
```

- [x] **Step 2: Run the tokenizer tests and verify the tokenizer is missing**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchValueTokenizerTests" --no-restore
```

Expected: build failure with `CS0103`/`CS0246` for `SearchValueTokenizer` and `SearchValueTokenKind`.

- [x] **Step 3: Add value token categories**

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal enum SearchValueTokenKind
{
    Text,
    Comma,
    Dollar,
    Pipe,
}
```

- [x] **Step 4: Implement the custom tokenizer**

```csharp
using Ignixa.Search.Expressions.Parsers.Syntax;
using Superpower;
using Superpower.Model;

namespace Ignixa.Search.Expressions.Parsers;

internal sealed class SearchValueTokenizer : Tokenizer<SearchValueTokenKind>
{
    private static readonly SearchValueTokenizer s_instance = new();

    public static SearchValueTokenizer Instance => s_instance;

    protected override IEnumerable<Result<SearchValueTokenKind>> Tokenize(TextSpan span)
    {
        while (!span.IsAtEnd)
        {
            TextSpan start = span;
            SearchValueTokenKind? separator = span[0] switch
            {
                ',' => SearchValueTokenKind.Comma,
                '$' => SearchValueTokenKind.Dollar,
                '|' => SearchValueTokenKind.Pipe,
                _ => null,
            };

            if (separator is { } kind)
            {
                TextSpan remainder = span.Skip(1);
                yield return Result.Value(kind, start, remainder);
                span = remainder;
                continue;
            }

            int length = 0;
            while (length < span.Length)
            {
                char current = span[length];
                if (current is ',' or '$' or '|')
                {
                    break;
                }

                if (current == '\\')
                {
                    if (length + 1 >= span.Length ||
                        span[length + 1] is not ('\\' or ',' or '$' or '|'))
                    {
                        yield return Result.Empty<SearchValueTokenKind>(
                            span.Skip(length),
                            "valid FHIR escape");
                        yield break;
                    }

                    length += 2;
                }
                else
                {
                    length++;
                }
            }

            TextSpan textRemainder = span.Skip(length);
            yield return Result.Value(SearchValueTokenKind.Text, start, textRemainder);
            span = textRemainder;
        }
    }
}
```

Do not unescape in the tokenizer. The token span retains the original backslash so canonical atomic parsers continue receiving escaped text.

- [x] **Step 5: Run tokenizer tests and verify they pass**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchValueTokenizerTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 3, Skipped: 0`.

- [ ] **Step 6: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs
git status --short
```

Proposed commit subject: `Tokenize escaped FHIR search values`

Proposed commit message:

```text
Tokenize escaped FHIR search values

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueTokenKind.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueTokenizer.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueTokenizerTests.cs
git commit -m "Tokenize escaped FHIR search values" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 8: Parse scalar, comparator, alternative, composite, and special value syntax

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueSyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/AtomicValueSyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/AlternativesValueSyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/CompositeValueSyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/MissingValueSyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/Syntax/OfTypeValueSyntax.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs`

- [x] **Step 1: Write failing type-selected value grammar tests**

```csharp
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchValueGrammarTests
{
    [Theory]
    [InlineData(SearchParamType.String, "gtSmith", SearchComparator.Eq, "gtSmith")]
    [InlineData(SearchParamType.Date, "gt2026-07-10", SearchComparator.Gt, "2026-07-10")]
    [InlineData(SearchParamType.Number, "le120", SearchComparator.Le, "120")]
    [InlineData(SearchParamType.Quantity, "ap5.4|mg", SearchComparator.Ap, "5.4|mg")]
    public void GivenScalarValue_WhenParsing_ThenComparatorDependsOnSearchType(
        SearchParamType type,
        string value,
        SearchComparator comparator,
        string rawText)
    {
        var result = SearchValueGrammar.Parse(type, null, value);

        var atomic = result.ShouldBeOfType<AtomicValueSyntax>();
        atomic.Comparator.ShouldBe(comparator);
        atomic.RawText.ShouldBe(rawText);
    }

    [Fact]
    public void GivenEscapedCommaAlternatives_WhenParsing_ThenPreservesEscapedText()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            null,
            @"system|a\,b,system|c");

        var alternatives = result.ShouldBeOfType<AlternativesValueSyntax>();
        alternatives.Items.Length.ShouldBe(2);
        alternatives.Items[0].ShouldBeOfType<AtomicValueSyntax>()
            .RawText.ShouldBe(@"system|a\,b");
        alternatives.Items[1].ShouldBeOfType<AtomicValueSyntax>()
            .RawText.ShouldBe("system|c");
    }

    [Fact]
    public void GivenCompositeAlternatives_WhenParsing_ThenBuildsComponentsBeforeAlternatives()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Composite,
            null,
            "http://loinc.org|8480-6$gt120,29463-7$lt80");

        var alternatives = result.ShouldBeOfType<AlternativesValueSyntax>();
        var first = alternatives.Items[0].ShouldBeOfType<CompositeValueSyntax>();
        first.Components[0].RawText.ShouldBe("http://loinc.org|8480-6");
        first.Components[1].Comparator.ShouldBe(SearchComparator.Gt);
        first.Components[1].RawText.ShouldBe("120");
    }

    [Fact]
    public void GivenMissingModifier_WhenParsing_ThenBuildsBooleanSyntax()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.String,
            new SearchModifier(SearchModifierCode.Missing),
            "true");

        result.ShouldBe(new MissingValueSyntax(true));
    }

    [Fact]
    public void GivenOfTypeModifier_WhenParsing_ThenBuildsTripletSyntax()
    {
        var result = SearchValueGrammar.Parse(
            SearchParamType.Token,
            new SearchModifier(SearchModifierCode.OfType),
            "http://terminology.hl7.org|MR|123");

        result.ShouldBe(new OfTypeValueSyntax(
            "http://terminology.hl7.org",
            "MR",
            "123"));
    }
}
```

- [x] **Step 2: Run the tests and verify value syntax types do not exist**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchValueGrammarTests" --no-restore
```

Expected: build failure with `CS0246`/`CS0103` for `SearchValueGrammar` and its syntax records.

- [x] **Step 3: Add immutable value syntax records**

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal abstract record SearchValueSyntax;
```

```csharp
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record AtomicValueSyntax(
    string RawText,
    SearchComparator Comparator) : SearchValueSyntax;
```

```csharp
using System.Collections.Immutable;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record AlternativesValueSyntax(
    ImmutableArray<SearchValueSyntax> Items) : SearchValueSyntax;
```

```csharp
using System.Collections.Immutable;

namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record CompositeValueSyntax(
    ImmutableArray<AtomicValueSyntax> Components) : SearchValueSyntax;
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record MissingValueSyntax(bool IsMissing) : SearchValueSyntax;
```

```csharp
namespace Ignixa.Search.Expressions.Parsers.Syntax;

internal sealed record OfTypeValueSyntax(
    string TypeSystem,
    string TypeCode,
    string IdentifierValue) : SearchValueSyntax;
```

- [x] **Step 4: Add token combinators and comparator parsing**

Create `SearchValueGrammar` with these parsers:

```csharp
using System.Collections.Immutable;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using Superpower;
using Superpower.Model;
using Superpower.Parsers;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchValueGrammar
{
    private static readonly TokenListParser<SearchValueTokenKind, string> Text =
        Token.EqualTo(SearchValueTokenKind.Text)
            .Select(token => token.ToStringValue());

    private static readonly TokenListParser<SearchValueTokenKind, string> PipeText =
        Token.EqualTo(SearchValueTokenKind.Pipe).Select(token => token.ToStringValue());

    private static readonly TokenListParser<SearchValueTokenKind, string> DollarText =
        Token.EqualTo(SearchValueTokenKind.Dollar).Select(token => token.ToStringValue());

    private static TokenListParser<SearchValueTokenKind, string> Segment(bool includeDollar)
    {
        TokenListParser<SearchValueTokenKind, string> part = includeDollar
            ? Text.Or(PipeText).Or(DollarText)
            : Text.Or(PipeText);
        return part.AtLeastOnce().Select(parts => string.Concat(parts));
    }

    private static AtomicValueSyntax ParseAtomic(
        string rawText,
        bool supportsComparator)
    {
        if (supportsComparator)
        {
            foreach (SearchComparator comparator in Enum.GetValues<SearchComparator>())
            {
                string literal = comparator.GetLiteral();
                if (rawText.StartsWith(literal, StringComparison.Ordinal))
                {
                    return new AtomicValueSyntax(
                        rawText[literal.Length..],
                        comparator);
                }
            }
        }

        return new AtomicValueSyntax(rawText, SearchComparator.Eq);
    }
```

- [x] **Step 5: Add selected scalar/composite/special grammars and the parse facade**

Complete `SearchValueGrammar`:

```csharp
    private static TokenListParser<SearchValueTokenKind, SearchValueSyntax> Scalar(
        bool supportsComparator)
    {
        var atomic = Segment(includeDollar: true)
            .Select(raw => (SearchValueSyntax)ParseAtomic(raw, supportsComparator));
        return WrapAlternatives(
            atomic.AtLeastOnceDelimitedBy(Token.EqualTo(SearchValueTokenKind.Comma)));
    }

    private static TokenListParser<SearchValueTokenKind, SearchValueSyntax> Composite()
    {
        var component = Segment(includeDollar: false)
            .Select(raw => ParseAtomic(raw, supportsComparator: true));
        var composite = component
            .AtLeastOnceDelimitedBy(Token.EqualTo(SearchValueTokenKind.Dollar))
            .Select(components => (SearchValueSyntax)new CompositeValueSyntax(
                components.ToImmutableArray()));
        return WrapAlternatives(
            composite.AtLeastOnceDelimitedBy(Token.EqualTo(SearchValueTokenKind.Comma)));
    }

    private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> Missing =
        Text.Select(value => (SearchValueSyntax)new MissingValueSyntax(
            bool.Parse(value)));

    private static readonly TokenListParser<SearchValueTokenKind, string?> OptionalText =
        Text.Select(value => (string?)value).OptionalOrDefault();

    private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> OfType =
        from system in OptionalText
        from firstPipe in Token.EqualTo(SearchValueTokenKind.Pipe)
        from code in OptionalText
        from secondPipe in Token.EqualTo(SearchValueTokenKind.Pipe)
        from identifier in OptionalText
        select (SearchValueSyntax)new OfTypeValueSyntax(
            system ?? string.Empty,
            code ?? string.Empty,
            identifier ?? string.Empty);

    public static SearchValueSyntax Parse(
        SearchParamType searchType,
        SearchModifier? modifier,
        string source)
    {
        TokenListParser<SearchValueTokenKind, SearchValueSyntax> parser =
            modifier?.SearchModifierCode switch
            {
                SearchModifierCode.Missing => Missing,
                SearchModifierCode.OfType => OfType,
                _ when searchType == SearchParamType.Composite => Composite(),
                _ => Scalar(searchType is
                    SearchParamType.Date or
                    SearchParamType.Number or
                    SearchParamType.Quantity),
            };

        var tokenization = SearchValueTokenizer.Instance.TryTokenize(source);
        if (!tokenization.HasValue)
        {
            throw SearchParseExceptionMapper.FromTokenization(
                "search value",
                tokenization);
        }

        var parsing = parser.AtEnd().TryParse(tokenization.Value);
        if (!parsing.HasValue)
        {
            throw SearchParseExceptionMapper.FromParsing(
                "search value",
                parsing);
        }

        return parsing.Value;
    }

    private static TokenListParser<SearchValueTokenKind, SearchValueSyntax> WrapAlternatives(
        TokenListParser<SearchValueTokenKind, SearchValueSyntax[]> parser) =>
        parser.Select(items => items.Length == 1
            ? items[0]
            : new AlternativesValueSyntax(items.ToImmutableArray()));
}
```

Superpower 3.1.0 returns `T[]` from `AtLeastOnceDelimitedBy`, so the `WrapAlternatives` signature above is the exact target signature.

- [x] **Step 6: Add malformed delimiter cases and verify positioned failure**

Append:

```csharp
[Theory]
[InlineData(SearchParamType.Token, "a,,b")]
[InlineData(SearchParamType.Composite, "a$$b")]
[InlineData(SearchParamType.Composite, "$a")]
[InlineData(SearchParamType.Composite, "a$")]
public void GivenEmptyValuePart_WhenParsing_ThenRejectsSyntax(
    SearchParamType type,
    string value)
{
    var exception = Should.Throw<InvalidSearchOperationException>(
        () => SearchValueGrammar.Parse(type, null, value));

    exception.Message.ShouldContain("line 1");
    exception.Message.ShouldContain("column");
}
```

- [x] **Step 7: Run value grammar and tokenizer tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchValueGrammarTests|FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchValueTokenizerTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 15, Skipped: 0`.

- [ ] **Step 8: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/Syntax src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs
git status --short
```

Proposed commit subject: `Parse typed FHIR search value syntax`

Proposed commit message:

```text
Parse typed FHIR search value syntax

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/Syntax/SearchValueSyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/AtomicValueSyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/AlternativesValueSyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/CompositeValueSyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/MissingValueSyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/Syntax/OfTypeValueSyntax.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchValueGrammarTests.cs
git commit -m "Parse typed FHIR search value syntax" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 9: Bind ordinary and comparator values through canonical atomic parsers

**Files:**
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs`
- Create: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs:15`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:24-49,183-230,301-321`

- [x] **Step 1: Write failing ordinary/comparator binding tests**

```csharp
using Ignixa.Search;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchExpressionBinderTests
{
    [Fact]
    public void GivenStringSyntax_WhenBinding_ThenUsesCanonicalStringParser()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Patient", "name", SearchParamType.String);
        var binder = CreateBinder(context);
        var syntax = SearchValueGrammar.Parse(
            SearchParamType.String,
            null,
            @"Smith\,Jones");

        var result = binder.BindValue(parameter, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var value = search.Expression.ShouldBeOfType<StringExpression>();
        value.Value.ShouldBe("Smith,Jones");
        value.StringOperator.ShouldBe(StringOperator.StartsWith);
    }

    [Fact]
    public void GivenGreaterThanNumberSyntax_WhenBinding_ThenBuildsExistingComparatorExpression()
    {
        var context = new SearchParserTestContext();
        var parameter = context.Add("Observation", "value-number", SearchParamType.Number);
        var binder = CreateBinder(context);
        var syntax = SearchValueGrammar.Parse(
            SearchParamType.Number,
            null,
            "gt120");

        var result = binder.BindValue(parameter, null, syntax);

        var search = result.ShouldBeOfType<SearchParameterExpression>();
        var comparison = search.Expression.ShouldBeOfType<BinaryExpression>();
        comparison.BinaryOperator.ShouldBe(BinaryOperator.GreaterThan);
        comparison.FieldName.ShouldBe(FieldName.Number);
        comparison.Value.ShouldBe(120m);
    }

    private static SearchExpressionBinder CreateBinder(
        SearchParserTestContext context) =>
        new(new SearchAtomicValueParser(
            new ReferenceSearchValueParser(context.SchemaProvider),
            context.SchemaProvider));
}
```

- [x] **Step 2: Run the binder tests and verify the binder is absent**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests" --no-restore
```

Expected: build failure with `CS0246` for `SearchExpressionBinder` or `SearchAtomicValueParser`.

- [x] **Step 3: Extract canonical atomic parser dispatch without changing parser behavior**

```csharp
using Ignixa.Abstractions;
using Ignixa.Search;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

internal sealed class SearchAtomicValueParser
{
    private readonly IReadOnlyDictionary<SearchParamType, Func<string, ISearchValue>> _parsers;

    public SearchAtomicValueParser(
        IReferenceSearchValueParser referenceParser,
        IFhirSchemaProvider schemaProvider)
    {
        _parsers = new Dictionary<SearchParamType, Func<string, ISearchValue>>
        {
            [SearchParamType.Date] = DateTimeSearchValue.Parse,
            [SearchParamType.Number] = NumberSearchValue.Parse,
            [SearchParamType.Quantity] = QuantitySearchValue.Parse,
            [SearchParamType.Reference] = referenceParser.Parse,
            [SearchParamType.String] = StringSearchValue.Parse,
            [SearchParamType.Token] = TokenSearchValue.Parse,
            [SearchParamType.Uri] =
                value => UriSearchValue.Parse(value, false, schemaProvider),
        };
    }

    public ISearchValue Parse(SearchParamType type, string rawText) =>
        MapAtomicErrors(() => _parsers[type](rawText));

    public OfTypeTokenSearchValue ParseOfType(string rawText) =>
        MapAtomicErrors(() => OfTypeTokenSearchValue.Parse(rawText));

    private static T MapAtomicErrors<T>(Func<T> parser)
    {
        try
        {
            return parser();
        }
        catch (FormatException exception)
        {
            throw new BadSearchRequestException(exception.Message);
        }
        catch (OverflowException exception)
        {
            throw new BadSearchRequestException(exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new BadSearchRequestException(exception.Message);
        }
    }
}
```

Do not catch `Exception`; programming errors and unexpected faults must escape unchanged.

- [x] **Step 4: Bind atomic syntax to the existing expression tree**

```csharp
using System.Diagnostics;
using Ignixa.Search.Expressions.Parsers.Syntax;
using Ignixa.Search.Indexing;
using Ignixa.Search.Models;

namespace Ignixa.Search.Expressions.Parsers;

internal sealed class SearchExpressionBinder(SearchAtomicValueParser atomicValueParser)
{
    public Expression BindValue(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        SearchValueSyntax syntax)
    {
        Expression body = syntax switch
        {
            AtomicValueSyntax atomic => BindAtomic(
                searchParameter,
                modifier,
                componentIndex: null,
                atomic),
            _ => throw new UnreachableException(),
        };

        return Expression.SearchParameter(searchParameter, body);
    }

    private Expression BindAtomic(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        int? componentIndex,
        AtomicValueSyntax syntax)
    {
        ISearchValue value = atomicValueParser.Parse(
            searchParameter.Type,
            syntax.RawText);
        value = ApplyReferenceTarget(searchParameter, modifier, value);
        return new SearchValueExpressionBuilderHelper().Build(
            searchParameter.Code,
            modifier,
            syntax.Comparator,
            componentIndex,
            value);
    }

    private static ISearchValue ApplyReferenceTarget(
        SearchParameterInfo searchParameter,
        SearchModifier? modifier,
        ISearchValue value)
    {
        if (value is not ReferenceSearchValue reference ||
            modifier?.SearchModifierCode != SearchModifierCode.Type)
        {
            return value;
        }

        if (reference.ResourceType is { } existing)
        {
            if (existing.Equals(
                modifier.ResourceType,
                StringComparison.OrdinalIgnoreCase))
            {
                return reference;
            }

            throw new InvalidSearchOperationException(
                string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
        }

        try
        {
            return new ReferenceSearchValue(
                reference.Kind,
                reference.BaseUri,
                modifier.ResourceType,
                reference.ResourceId);
        }
        catch (ArgumentException)
        {
            throw new InvalidSearchOperationException(
                string.Format(Resources.ModifierNotSupported, modifier, searchParameter.Code));
        }
    }
}
```

Add the necessary `Ignixa.Search.Indexing.SearchValues` and `Ignixa.Specification.ValueSets.Normative` usings. Change only `internal class SearchValueExpressionBuilderHelper` to `internal sealed class SearchValueExpressionBuilderHelper`; retain lines 17-380 unchanged.

- [x] **Step 5: Run ordinary/comparator binder tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0`.

- [ ] **Step 6: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git status --short
```

Proposed commit subject: `Bind canonical search values to expressions`

Proposed commit message:

```text
Bind canonical search values to expressions

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchValueExpressionBuilderHelper.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git commit -m "Bind canonical search values to expressions" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 10: Bind alternatives and `:not`

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:215-270`

- [x] **Step 1: Add failing OR and NOT-OR expression tests**

```csharp
[Fact]
public void GivenTokenAlternatives_WhenBinding_ThenBuildsOrExpression()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Observation", "code", SearchParamType.Token);
    var binder = CreateBinder(context);
    var syntax = SearchValueGrammar.Parse(
        SearchParamType.Token,
        null,
        "http://loinc.org|a,http://loinc.org|b");

    var result = binder.BindValue(parameter, null, syntax);

    var search = result.ShouldBeOfType<SearchParameterExpression>();
    var alternatives = search.Expression.ShouldBeOfType<MultiaryExpression>();
    alternatives.MultiaryOperation.ShouldBe(MultiaryOperator.Or);
    alternatives.Expressions.Count.ShouldBe(2);
}

[Fact]
public void GivenNotTokenAlternatives_WhenBinding_ThenNegatesTheWholeOr()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Observation", "code", SearchParamType.Token);
    var modifier = new SearchModifier(SearchModifierCode.Not);
    var binder = CreateBinder(context);
    var syntax = SearchValueGrammar.Parse(
        SearchParamType.Token,
        modifier,
        "http://loinc.org|a,http://loinc.org|b");

    var result = binder.BindValue(parameter, modifier, syntax);

    var search = result.ShouldBeOfType<SearchParameterExpression>();
    var not = search.Expression.ShouldBeOfType<NotExpression>();
    not.Expression.ShouldBeOfType<MultiaryExpression>()
        .MultiaryOperation.ShouldBe(MultiaryOperator.Or);
}

[Fact]
public void GivenComparatorWithAlternatives_WhenBinding_ThenPreservesComparatorError()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Observation", "date", SearchParamType.Date);
    var binder = CreateBinder(context);
    var syntax = SearchValueGrammar.Parse(
        SearchParamType.Date,
        null,
        "gt2026-01-01,2026-02-01");

    var exception = Should.Throw<InvalidSearchOperationException>(
        () => binder.BindValue(parameter, null, syntax));

    exception.Message.ShouldBe(Resources.SearchComparatorNotSupported);
}
```

- [x] **Step 2: Run the binder tests and verify alternatives are rejected by the current switch**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests" --no-restore
```

Expected: three failures; OR/NOT cases throw from the incomplete syntax switch and the comparator case has the wrong message.

- [x] **Step 3: Add alternative binding with whole-set negation**

Replace the body selection in `BindValue` and add helpers:

```csharp
Expression body = syntax switch
{
    AtomicValueSyntax atomic => BindAtomic(
        searchParameter,
        modifier,
        componentIndex: null,
        atomic),
    AlternativesValueSyntax alternatives => BindAlternatives(
        searchParameter,
        modifier,
        alternatives),
    _ => throw new UnreachableException(),
};

private Expression BindAlternatives(
    SearchParameterInfo searchParameter,
    SearchModifier? modifier,
    AlternativesValueSyntax syntax)
{
    if (syntax.Items
        .OfType<AtomicValueSyntax>()
        .Any(item => item.Comparator != SearchComparator.Eq))
    {
        throw new InvalidSearchOperationException(
            Resources.SearchComparatorNotSupported);
    }

    SearchModifier? itemModifier =
        modifier?.SearchModifierCode == SearchModifierCode.Not
            ? null
            : modifier;
    Expression[] items = syntax.Items
        .Select(item => item switch
        {
            AtomicValueSyntax atomic => BindAtomic(
                searchParameter,
                itemModifier,
                componentIndex: null,
                atomic),
            _ => throw new UnreachableException(),
        })
        .ToArray();
    Expression or = Expression.Or(items);

    return modifier?.SearchModifierCode == SearchModifierCode.Not
        ? Expression.Not(or)
        : or;
}
```

Add `using System.Diagnostics;` and `using Ignixa.Specification.ValueSets.Normative;`. Keep single-value `:not` behavior in `SearchValueExpressionBuilderHelper.Visit(TokenSearchValue)` unchanged.

- [x] **Step 4: Run all binder tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

- [ ] **Step 5: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git status --short
```

Proposed commit subject: `Bind search alternatives and not modifier`

Proposed commit message:

```text
Bind search alternatives and not modifier

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git commit -m "Bind search alternatives and not modifier" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 11: Bind composite values and inferred component types

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs`
- Reference: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:99-169,324-401`

- [x] **Step 1: Add failing composite binding tests**

```csharp
[Fact]
public void GivenCompositeAlternatives_WhenBinding_ThenBuildsOrOfComponentAnds()
{
    var context = new SearchParserTestContext();
    var code = new SearchParameterInfo("code", "code", SearchParamType.Token);
    var quantity = new SearchParameterInfo(
        "value-quantity",
        "value-quantity",
        SearchParamType.Quantity);
    var codeComponent = new SearchParameterComponentInfo(
        new Uri("http://example.org/SearchParameter/code"))
    {
        ResolvedSearchParameter = code,
    };
    var quantityComponent = new SearchParameterComponentInfo(
        new Uri("http://example.org/SearchParameter/value-quantity"))
    {
        ResolvedSearchParameter = quantity,
    };
    var composite = context.Add(
        "Observation",
        "code-value-quantity",
        SearchParamType.Composite,
        components: [codeComponent, quantityComponent]);
    var binder = CreateBinder(context);
    var syntax = SearchValueGrammar.Parse(
        SearchParamType.Composite,
        null,
        "http://loinc.org|8480-6$gt120,29463-7$lt80");

    var result = binder.BindValue(composite, null, syntax);

    var search = result.ShouldBeOfType<SearchParameterExpression>();
    var alternatives = search.Expression.ShouldBeOfType<MultiaryExpression>();
    alternatives.MultiaryOperation.ShouldBe(MultiaryOperator.Or);
    alternatives.Expressions.Count.ShouldBe(2);
    alternatives.Expressions.ShouldAllBe(
        expression => expression.ShouldBeOfType<MultiaryExpression>()
            .MultiaryOperation == MultiaryOperator.And);
}

[Fact]
public void GivenTooManyCompositeComponents_WhenBinding_ThenPreservesResourceMessage()
{
    var context = new SearchParserTestContext();
    var component = new SearchParameterComponentInfo
    {
        ResolvedSearchParameter =
            new SearchParameterInfo("code", "code", SearchParamType.Token),
    };
    var composite = context.Add(
        "Observation",
        "code-value",
        SearchParamType.Composite,
        components: [component]);
    var binder = CreateBinder(context);
    var syntax = SearchValueGrammar.Parse(
        SearchParamType.Composite,
        null,
        "code$value");

    var exception = Should.Throw<InvalidSearchOperationException>(
        () => binder.BindValue(composite, null, syntax));

    exception.Message.ShouldBe(string.Format(
        Resources.NumberOfCompositeComponentsExceeded,
        composite.Code));
}
```

- [x] **Step 2: Run binder tests and verify composite nodes are not bound**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests" --no-restore
```

Expected: two new failures from `UnreachableException`.

- [x] **Step 3: Add composite binding and component validation**

Add `CompositeValueSyntax` to both the top-level and alternative switches:

```csharp
CompositeValueSyntax composite => BindComposite(searchParameter, composite),
```

Then add:

```csharp
private Expression BindComposite(
    SearchParameterInfo searchParameter,
    CompositeValueSyntax syntax)
{
    if (syntax.Components.Length > searchParameter.Component.Count)
    {
        throw new InvalidSearchOperationException(string.Format(
            CultureInfo.InvariantCulture,
            Resources.NumberOfCompositeComponentsExceeded,
            searchParameter.Code));
    }

    var expressions = new Expression[syntax.Components.Length];
    for (int index = 0; index < syntax.Components.Length; index++)
    {
        SearchParameterComponentInfo component = searchParameter.Component[index];
        SearchParameterInfo resolved = component.ResolvedSearchParameter
            ?? throw new InvalidSearchOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "Composite search parameter '{0}' component {1} (definition: {2}) is not properly resolved. This indicates the search parameter was not properly built during initialization.",
                searchParameter.Code,
                index,
                component.DefinitionUrl?.ToString() ?? "unknown"));
        SearchParameterInfo effective = InferEffectiveParameter(
            resolved,
            syntax.Components[index].RawText);
        AtomicValueSyntax componentSyntax = NormalizeCompositeComparator(
            effective.Type,
            syntax.Components[index]);
        expressions[index] = BindAtomic(
            effective,
            modifier: null,
            index,
            componentSyntax);
    }

    return Expression.And(expressions);
}

private static AtomicValueSyntax NormalizeCompositeComparator(
    SearchParamType componentType,
    AtomicValueSyntax syntax)
{
    if (syntax.Comparator == SearchComparator.Eq ||
        componentType is
            SearchParamType.Date or
            SearchParamType.Number or
            SearchParamType.Quantity)
    {
        return syntax;
    }

    return new AtomicValueSyntax(
        $"{syntax.Comparator.GetLiteral()}{syntax.RawText}",
        SearchComparator.Eq);
}
```

- [x] **Step 4: Move the current conservative inference logic into the binder**

```csharp
private static SearchParameterInfo InferEffectiveParameter(
    SearchParameterInfo component,
    string value)
{
    SearchParamType? inferred = InferSearchParamTypeFromValue(value);
    if (inferred is null || inferred == component.Type)
    {
        return component;
    }

    return new SearchParameterInfo(
        component.Name,
        component.Code,
        inferred.Value,
        component.Url,
        component.Component,
        component.Expression,
        component.TargetResourceTypes,
        component.BaseResourceTypes,
        component.Description);
}

private static SearchParamType? InferSearchParamTypeFromValue(string value)
{
    if (value.Contains('/', StringComparison.Ordinal) &&
        !value.Contains('|', StringComparison.Ordinal))
    {
        string[] parts = value.Split('/');
        if (parts.Length >= 2 &&
            parts[0].Length > 0 &&
            char.IsUpper(parts[0][0]) &&
            parts[0].All(char.IsLetterOrDigit))
        {
            return SearchParamType.Reference;
        }
    }

    return value.Contains('|', StringComparison.Ordinal)
        ? SearchParamType.Token
        : null;
}
```

This is the exact inference policy at `SearchParameterExpressionParser.cs:364-400`; do not broaden it to infer number, quantity, date, string, or URI.

- [x] **Step 5: Run composite and ordinary binder tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests" --no-restore
```

Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0`.

- [ ] **Step 6: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git status --short
```

Proposed commit subject: `Bind composite search expressions`

Proposed commit message:

```text
Bind composite search expressions

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs
git commit -m "Bind composite search expressions" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 12: Bind special modifiers and cut over the value facade

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs:22-402`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs`
- Modify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/ExpressionParserCharacterizationTests.cs`

- [x] **Step 1: Add failing special-modifier integration tests**

```csharp
[Fact]
public void GivenMissingModifier_WhenParsingFacade_ThenBuildsMissingSearchParameter()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Patient", "name", SearchParamType.String);

    var result = context.ValueParser.Parse(
        parameter,
        new SearchModifier(SearchModifierCode.Missing),
        "true");

    result.ShouldBeOfType<MissingSearchParameterExpression>()
        .IsMissing.ShouldBeTrue();
}

[Fact]
public void GivenOfTypeAlternatives_WhenParsingFacade_ThenBuildsOr()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Patient", "identifier", SearchParamType.Token);
    var modifier = new SearchModifier(SearchModifierCode.OfType);

    var result = context.ValueParser.Parse(
        parameter,
        modifier,
        "http://terminology.hl7.org|MR|123,http://terminology.hl7.org|SS|456");

    result.ShouldBeOfType<SearchParameterExpression>()
        .Expression.ShouldBeOfType<MultiaryExpression>()
        .MultiaryOperation.ShouldBe(MultiaryOperator.Or);
}

[Theory]
[InlineData(SearchModifierCode.Text, SearchParamType.Token, "display")]
[InlineData(SearchModifierCode.Above, SearchParamType.Uri, "http://example.org/a")]
[InlineData(SearchModifierCode.Below, SearchParamType.Uri, "http://example.org/a")]
public void GivenSupportedSpecialModifier_WhenParsingFacade_ThenBuildsExpression(
    SearchModifierCode modifierCode,
    SearchParamType type,
    string value)
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Resource", "special", type);

    var result = context.ValueParser.Parse(
        parameter,
        new SearchModifier(modifierCode),
        value);

    result.ShouldBeAssignableTo<Expression>();
}

[Fact]
public void GivenTextModifierWithComma_WhenParsingFacade_ThenTreatsCommaAsText()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add("Observation", "code", SearchParamType.Token);

    var result = context.ValueParser.Parse(
        parameter,
        new SearchModifier(SearchModifierCode.Text),
        "alpha,beta");

    result.ShouldBeOfType<SearchParameterExpression>()
        .Expression.ShouldBeOfType<StringExpression>()
        .Value.ShouldBe("alpha,beta");
}

[Fact]
public void GivenReferenceTargetModifier_WhenParsingFacade_ThenAppliesResourceType()
{
    var context = new SearchParserTestContext();
    var parameter = context.Add(
        "Observation",
        "subject",
        SearchParamType.Reference,
        ["Patient"]);

    var result = context.ValueParser.Parse(
        parameter,
        new SearchModifier(SearchModifierCode.Type, "Patient"),
        "123");

    result.ToString().ShouldContain("Patient");
}
```

- [x] **Step 2: Run special-modifier tests against the not-yet-cut-over facade**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchExpressionBinderTests|FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.ExpressionParserCharacterizationTests" --no-restore
```

Expected before the new tests are wired to the facade: at least the `:of-type` alternatives case fails because `SearchValueGrammar.OfType` accepts only one triplet.

- [x] **Step 3: Parse of-type alternatives and preserve invalid-missing semantics**

Rename the existing `OfType` parser to `OfTypeItem`, then add:

```csharp
private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> OfType =
    WrapAlternatives(
        OfTypeItem.AtLeastOnceDelimitedBy(
            Token.EqualTo(SearchValueTokenKind.Comma)));

private static readonly TokenListParser<SearchValueTokenKind, SearchValueSyntax> TextModifier =
    Token.Matching<SearchValueTokenKind>(
            _ => true,
            "text modifier value")
        .AtLeastOnce()
        .Select(tokens => (SearchValueSyntax)new AtomicValueSyntax(
            string.Concat(tokens.Select(token => token.ToStringValue())),
            SearchComparator.Eq));
```

Before tokenization in `Parse`, preserve the existing `:missing` semantic error:

```csharp
if (modifier?.SearchModifierCode == SearchModifierCode.Missing &&
    !bool.TryParse(source, out _))
{
    throw new InvalidSearchOperationException(
        Resources.InvalidValueTypeForMissingModifier);
}
```

Select `TextModifier` before the general scalar branch:

```csharp
SearchModifierCode.Text => TextModifier,
```

This deliberately keeps commas and all other separators literal for `:text`, matching `SearchParameterExpressionParser.cs:73-83`; ordinary values still parse commas as alternatives.

- [x] **Step 4: Bind missing, of-type, and token text syntax**

Extend `BindValue` before wrapping ordinary bodies:

```csharp
if (syntax is MissingValueSyntax missing)
{
    return Expression.MissingSearchParameter(
        searchParameter,
        missing.IsMissing);
}

if (modifier?.SearchModifierCode == SearchModifierCode.Text)
{
    if (searchParameter.Type != SearchParamType.Token ||
        syntax is not AtomicValueSyntax text)
    {
        throw new InvalidSearchOperationException(string.Format(
            CultureInfo.InvariantCulture,
            Resources.ModifierNotSupported,
            modifier,
            searchParameter.Code));
    }

    return Expression.SearchParameter(
        searchParameter,
        Expression.StartsWith(FieldName.TokenText, null, text.RawText, true));
}
```

Add `OfTypeValueSyntax` to the top-level and alternatives switches:

```csharp
OfTypeValueSyntax ofType => BindOfType(searchParameter, ofType),
```

Implement:

```csharp
private Expression BindOfType(
    SearchParameterInfo searchParameter,
    OfTypeValueSyntax syntax)
{
    if (searchParameter.Type != SearchParamType.Token)
    {
        throw new InvalidSearchOperationException(string.Format(
            CultureInfo.InvariantCulture,
            Resources.ModifierNotSupported,
            SearchModifierCode.OfType.GetLiteral(),
            searchParameter.Code));
    }

    string raw = string.Join(
        '|',
        syntax.TypeSystem,
        syntax.TypeCode,
        syntax.IdentifierValue);
    OfTypeTokenSearchValue value = atomicValueParser.ParseOfType(raw);
    return new SearchValueExpressionBuilderHelper().Build(
        searchParameter.Code,
        modifier: null,
        SearchComparator.Eq,
        componentIndex: null,
        value);
}
```

URI `:above`/`:below`, reference target modifiers, string `:exact`/`:contains`, and token `:not` continue through `SearchValueExpressionBuilderHelper`; do not duplicate those AST rules.

- [x] **Step 5: Replace `SearchParameterExpressionParser` internals with grammar/binder delegation**

Replace its fields, constructor body, and `Parse` implementation with:

```csharp
public class SearchParameterExpressionParser : ISearchParameterExpressionParser
{
    private readonly SearchExpressionBinder _binder;

    public SearchParameterExpressionParser(
        IReferenceSearchValueParser referenceSearchValueParser,
        IFhirSchemaProvider fhirSchemaProvider)
    {
        EnsureArg.IsNotNull(
            referenceSearchValueParser,
            nameof(referenceSearchValueParser));
        EnsureArg.IsNotNull(fhirSchemaProvider, nameof(fhirSchemaProvider));
        _binder = new SearchExpressionBinder(
            new SearchAtomicValueParser(
                referenceSearchValueParser,
                fhirSchemaProvider));
    }

    public Expression Parse(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        SearchValueSyntax syntax = SearchValueGrammar.Parse(
            searchParameter.Type,
            modifier,
            value);
        return _binder.BindValue(searchParameter, modifier, syntax);
    }
}
```

Retain the public class name, constructor parameters, and `ISearchParameterExpressionParser.Parse` signature exactly. Delete the old `_parserDictionary`, comparator scan, `Build`, `BuildOfTypeExpression`, `CreateParserWithErrorHandling`, and inference methods from this production file.

- [x] **Step 6: Run direct facade, characterization, and existing composite diagnostic tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers|FullyQualifiedName~CompositeSearchIndexingDiagnosticTests" --no-restore
```

Expected: `Passed! - Failed: 0`; output includes all parser tests plus the existing composite diagnostic theory, with no failed cases.

- [x] **Step 7: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers
git status --short
```

Proposed commit subject: `Cut over Superpower search value parsing`

Proposed commit message:

```text
Cut over Superpower search value parsing

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchValueGrammar.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchExpressionBinderTests.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/ExpressionParserCharacterizationTests.cs
git commit -m "Cut over Superpower search value parsing" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 13: Cut over the key facade and remove handwritten production splitting

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs:22-383`
- Modify: `src/Core/Ignixa.Search/Indexing/StringExtensions.cs:56-95,145-173`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserFacadeTests.cs`
- Reference: `src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs:93-119`

- [x] **Step 1: Add failing final-facade tests**

```csharp
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchParserFacadeTests
{
    [Fact]
    public void GivenNestedMixedChain_WhenParsingPublicFacade_ThenBuildsCurrentAst()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "patient", SearchParamType.Reference, ["Patient"]);
        context.Add("Group", "member", SearchParamType.Reference, ["Patient"]);
        context.Add("Group", "_tag", SearchParamType.Token);
        IExpressionParser parser = context.Parser;

        var result = parser.Parse(
            ["Observation"],
            "patient:Patient._has:Group:member:_tag",
            "http://example.org|reviewed");

        var forward = result.ShouldBeOfType<ChainedExpression>();
        forward.Reversed.ShouldBeFalse();
        var reverse = forward.Expression.ShouldBeOfType<ChainedExpression>();
        reverse.Reversed.ShouldBeTrue();
        reverse.ResourceTypes.ShouldBe(["Group"]);
        reverse.TargetResourceTypes.ShouldBe(["Patient"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void GivenIncludeFlags_WhenParsingPublicFacade_ThenPreservesFlags(
        bool reversed,
        bool iterate)
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);

        var result = context.Parser.ParseInclude(
            ["Patient"],
            "Observation:subject:Patient",
            reversed,
            iterate);

        result.SourceResourceType.ShouldBe("Observation");
        result.TargetResourceType.ShouldBe("Patient");
        result.Reversed.ShouldBe(reversed);
        result.Iterate.ShouldBe(iterate);
    }

    [Fact]
    public void GivenWildcardInclude_WhenParsingPublicFacade_ThenCollectsDistinctTargets()
    {
        var context = new SearchParserTestContext();
        context.Add("Observation", "subject", SearchParamType.Reference, ["Patient"]);
        context.Add("Observation", "encounter", SearchParamType.Reference, ["Encounter", "Patient"]);

        var result = context.Parser.ParseInclude(
            ["Observation"],
            "Observation:*",
            isReversed: false,
            iterate: false);

        result.WildCard.ShouldBeTrue();
        result.ReferencedTypes.ShouldBe(["Patient", "Encounter"], ignoreOrder: true);
    }
}
```

- [x] **Step 2: Run facade tests and confirm production still uses handwritten key parsing**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchParserFacadeTests" --no-restore
```

Expected: the behavior tests may pass through the old parser, which establishes the cutover target. The accompanying source check must still print matches:

```powershell
rg "TrySplit|TryConsume|SplitByOrSeparator|SplitByCompositeSeparator" src/Core/Ignixa.Search/Expressions/Parsers src/Core/Ignixa.Search/Indexing/StringExtensions.cs
```

Expected before implementation: matches in `ExpressionParser.cs`, `SearchParameterExpressionParser.cs` if Task 12 has not removed all old code, and `StringExtensions.cs`.

- [x] **Step 3: Add bound-key AST construction to `SearchExpressionBinder`**

```csharp
using Ignixa.Search.Expressions.Parsers.Binding;

public static Expression BindKey(
    BoundSearchKey key,
    Func<BoundParameterKey, Expression> bindParameter) =>
    key switch
    {
        BoundParameterKey parameter => bindParameter(parameter),
        BoundChainKey chain => Expression.Chained(
            chain.ResourceTypes.ToArray(),
            chain.ReferenceSearchParameter,
            chain.TargetResourceTypes.ToArray(),
            chain.Reversed,
            BindKey(chain.Next, bindParameter)),
        _ => throw new UnreachableException(),
    };

public static IncludeExpression BindInclude(
    string[] resourceTypes,
    BoundIncludeKey include,
    bool isReversed,
    bool iterate) =>
    new(
        resourceTypes,
        include.ReferenceSearchParameter,
        include.SourceResourceType,
        include.TargetResourceType,
        include.ReferencedTypes,
        include.Wildcard,
        isReversed,
        iterate);

public static NotReferencedExpression BindNotReferenced(
    BoundNotReferencedKey notReferenced) =>
    Expression.NotReferenced(
        notReferenced.SourceResourceType,
        notReferenced.ReferencePath);
```

- [x] **Step 4: Replace `ExpressionParser` with the compatibility facade**

Keep the existing namespace, XML summary, public class name, and constructor signature. Replace its private parsing implementation with:

```csharp
public class ExpressionParser : IExpressionParser
{
    private readonly SearchKeyBinder _keyBinder;
    private readonly ISearchParameterExpressionParser _valueParser;

    public ExpressionParser(
        ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver
            searchParameterDefinitionManagerResolver,
        ISearchParameterExpressionParser searchParameterExpressionParser,
        IFhirSchemaProvider schemaProvider)
    {
        EnsureArg.IsNotNull(
            searchParameterDefinitionManagerResolver,
            nameof(searchParameterDefinitionManagerResolver));
        EnsureArg.IsNotNull(
            searchParameterExpressionParser,
            nameof(searchParameterExpressionParser));
        EnsureArg.IsNotNull(schemaProvider, nameof(schemaProvider));

        _keyBinder = new SearchKeyBinder(
            searchParameterDefinitionManagerResolver(),
            schemaProvider);
        _valueParser = searchParameterExpressionParser;
    }

    public Expression Parse(string[] resourceTypes, string key, string value)
    {
        EnsureArg.HasItems(resourceTypes, nameof(resourceTypes));
        EnsureArg.IsNotNullOrWhiteSpace(key, nameof(key));
        EnsureArg.IsNotNullOrWhiteSpace(value, nameof(value));

        if (key.Equals("_not-referenced", StringComparison.OrdinalIgnoreCase))
        {
            NotReferencedKeySyntax syntax =
                SearchKeyGrammar.ParseNotReferenced(value);
            return SearchExpressionBinder.BindNotReferenced(
                _keyBinder.BindNotReferenced(syntax));
        }

        SearchKeySyntax keySyntax = SearchKeyGrammar.ParseParameter(key);
        BoundSearchKey bound = _keyBinder.Bind(resourceTypes, keySyntax);
        return SearchExpressionBinder.BindKey(
            bound,
            parameter => _valueParser.Parse(
                parameter.SearchParameter,
                parameter.Modifier,
                value));
    }

    public IncludeExpression ParseInclude(
        string[] resourceTypes,
        string includeValue,
        bool isReversed,
        bool iterate)
    {
        EnsureArg.HasItems(resourceTypes, nameof(resourceTypes));
        EnsureArg.IsNotNullOrWhiteSpace(includeValue, nameof(includeValue));
        if (!includeValue.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidSearchOperationException(
                isReversed
                    ? Resources.RevIncludeMissingType
                    : Resources.IncludeMissingType);
        }

        IncludeKeySyntax syntax = SearchKeyGrammar.ParseInclude(includeValue);
        BoundIncludeKey bound = _keyBinder.BindInclude(
            resourceTypes,
            syntax,
            isReversed,
            iterate);
        return SearchExpressionBinder.BindInclude(
            resourceTypes,
            bound,
            isReversed,
            iterate);
    }
}
```

Add the exact usings for `EnsureThat`, `Ignixa.Abstractions`, `Ignixa.Search.Definition`, `Ignixa.Search.Expressions.Parsers.Binding`, `Ignixa.Search.Expressions.Parsers.Syntax`, and `Ignixa.Search.Indexing`. Do not retain `TrySplit`, `TryConsume`, `Advance`, regular expressions, modifier dictionaries, or recursive span parsing.

- [x] **Step 5: Remove obsolete comma/composite split methods**

Delete `SplitByCompositeSeparator` (`StringExtensions.cs:69-81`) and `SplitByOrSeparator` (`StringExtensions.cs:83-95`). Keep:

```csharp
public static IReadOnlyList<string> SplitByTokenSeparator(this string s)
{
    EnsureArg.IsNotNull(s, nameof(s));
    return Split(s, TokenSeparator);
}
```

Keep `CompositeSeparator` and `OrSeparator` constants because `EscapeSearchParameterValue`/`UnescapeSearchParameterValue` still escape those characters. Keep the private `Split` method because token, quantity, and of-type atomic parsers still call `SplitByTokenSeparator`.

- [x] **Step 6: Run facade and characterization tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-restore
```

Expected: `Passed! - Failed: 0`; every parser test passes through the new production facades.

- [x] **Step 7: Verify there is one production parsing path**

Run:

```powershell
rg "TrySplit|TryConsume|SplitByOrSeparator|SplitByCompositeSeparator" src/Core/Ignixa.Search/Expressions/Parsers src/Core/Ignixa.Search/Indexing/StringExtensions.cs
```

Expected: no matches and exit code 1. Also run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/IExpressionParser.cs src/Core/Ignixa.Search/Expressions/Parsers/ISearchParameterExpressionParser.cs src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs
```

Expected: no diff; both interfaces and per-tenant/version construction remain unchanged.

- [x] **Step 8: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs src/Core/Ignixa.Search/Indexing/StringExtensions.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserFacadeTests.cs
git status --short
```

Proposed commit subject: `Cut over Superpower search key parsing`

Proposed commit message:

```text
Cut over Superpower search key parsing

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs src/Core/Ignixa.Search/Expressions/Parsers/SearchExpressionBinder.cs src/Core/Ignixa.Search/Indexing/StringExtensions.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserFacadeTests.cs
git commit -m "Cut over Superpower search key parsing" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 14: Finalize positioned syntax errors and semantic error parity

**Files:**
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs`
- Modify: `src/Core/Ignixa.Search/Resources.resx:279-312`
- Modify: `src/Core/Ignixa.Search/Resources.Designer.cs`
- Modify: `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs`
- Create: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs`

- [x] **Step 1: Write failing syntax-position and semantic-parity tests**

```csharp
using Ignixa.Search;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Search.Expressions.Parsers;

public class SearchParserErrorParityTests
{
    [Theory]
    [InlineData("patient..name", "search key", 9)]
    [InlineData("name:exact:contains", "search key", 11)]
    public void GivenMalformedKey_WhenParsing_ThenReportsSuperpowerPosition(
        string key,
        string subject,
        int column)
    {
        var context = new SearchParserTestContext();

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(["Patient"], key, "value"));

        exception.Message.ShouldContain(subject);
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain($"column {column}");
    }

    [Fact]
    public void GivenTrailingValueEscape_WhenParsing_ThenReportsEscapePosition()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(["Patient"], "name", @"value\"));

        exception.Message.ShouldContain("search value");
        exception.Message.ShouldContain("line 1");
        exception.Message.ShouldContain("column 6");
    }

    [Fact]
    public void GivenUnsupportedParameter_WhenBinding_ThenPreservesExceptionCategory()
    {
        var context = new SearchParserTestContext();
        context.DefinitionManager
            .GetSearchParameter("Patient", "unknown")
            .Returns(_ => throw new SearchParameterNotSupportedException(
                "Patient",
                "unknown"));

        Should.Throw<SearchParameterNotSupportedException>(
            () => context.Parser.Parse(["Patient"], "unknown", "value"));
    }

    [Fact]
    public void GivenInvalidMissingValue_WhenParsing_ThenPreservesResourceMessage()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "name", SearchParamType.String);

        var exception = Should.Throw<InvalidSearchOperationException>(
            () => context.Parser.Parse(
                ["Patient"],
                "name:missing",
                "yes"));

        exception.Message.ShouldBe(Resources.InvalidValueTypeForMissingModifier);
    }

    [Fact]
    public void GivenInvalidAtomicValue_WhenParsing_ThenPreservesBadRequestCategory()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "birthdate", SearchParamType.Date);

        Should.Throw<BadSearchRequestException>(
            () => context.Parser.Parse(
                ["Patient"],
                "birthdate",
                "not-a-date"));
    }

    [Fact]
    public void GivenInvalidOfTypeComponent_WhenParsing_ThenPreservesBadRequestCategory()
    {
        var context = new SearchParserTestContext();
        context.Add("Patient", "identifier", SearchParamType.Token);

        Should.Throw<BadSearchRequestException>(
            () => context.Parser.Parse(
                ["Patient"],
                "identifier:of-type",
                "|MR|"));
    }
}
```

- [x] **Step 2: Run error tests and capture failures from provisional messages**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers.SearchParserErrorParityTests" --no-restore
```

Expected: syntax-position assertions fail against the provisional non-resource message and any off-by-one position mapping; semantic category tests must already pass.

- [x] **Step 3: Add the localized positioned syntax resource**

Add to `Resources.resx`:

```xml
<data name="MalformedSearchSyntax" xml:space="preserve">
  <value>Malformed {0} at line {1}, column {2}: {3}</value>
  <comment>{0}=syntax subject, {1}=line, {2}=column, {3}=Superpower expectation</comment>
</data>
```

Add the generated property to `Resources.Designer.cs` in alphabetical position:

```csharp
/// <summary>
///   Looks up a localized string similar to Malformed {0} at line {1}, column {2}: {3}.
/// </summary>
internal static string MalformedSearchSyntax {
    get {
        return ResourceManager.GetString("MalformedSearchSyntax", resourceCulture);
    }
}
```

- [x] **Step 4: Finalize the exception mapper without broad fallback**

```csharp
using System.Globalization;
using Ignixa.Search.Indexing;
using Superpower.Model;

namespace Ignixa.Search.Expressions.Parsers;

internal static class SearchParseExceptionMapper
{
    public static InvalidSearchOperationException FromTokenization<T>(
        string subject,
        Result<T> result) =>
        Create(
            subject,
            result.ErrorPosition,
            result.FormatErrorMessageFragment());

    public static InvalidSearchOperationException FromParsing<TKind, TValue>(
        string subject,
        TokenListParserResult<TKind, TValue> result) =>
        Create(
            subject,
            result.ErrorPosition,
            result.FormatErrorMessageFragment());

    private static InvalidSearchOperationException Create(
        string subject,
        Position position,
        string expectation) =>
        new(string.Format(
            CultureInfo.InvariantCulture,
            Resources.MalformedSearchSyntax,
            subject,
            position.Line,
            position.Column,
            expectation));
}
```

Both mapper overloads consume Superpower's reported `Position`; do not wrap calls in `try/catch` or replace the expectation fragment with a generic message.

- [x] **Step 5: Preserve invalid include form resources at the facade boundary**

Before calling `SearchKeyGrammar.ParseInclude`, classify the two legacy semantic forms:

```csharp
if (!includeValue.Contains(':', StringComparison.Ordinal))
{
    throw new InvalidSearchOperationException(
        isReversed
            ? Resources.RevIncludeMissingType
            : Resources.IncludeMissingType);
}

if (includeValue.EndsWith(':'))
{
    string[] parts = includeValue.Split(':');
    throw new InvalidSearchOperationException(string.Format(
        Resources.IncludeInvalidTargetResourceType,
        isReversed ? "_revinclude" : "_include",
        parts[0],
        parts.Length > 1 ? parts[1] : string.Empty,
        "<empty>"));
}
```

This boundary classification preserves stable semantic resources. It is not a parsing fallback: every accepted include still goes through `SearchKeyGrammar`, and every malformed form not listed above receives the positioned Superpower error.

- [x] **Step 6: Run error parity and all parser tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-restore
```

Expected: `Passed! - Failed: 0`; syntax tests contain line/column diagnostics and semantic tests retain `SearchParameterNotSupportedException`, `BadSearchRequestException`, or `InvalidSearchOperationException` as asserted.

- [x] **Step 7: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs src/Core/Ignixa.Search/Resources.resx src/Core/Ignixa.Search/Resources.Designer.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs
git status --short
```

Proposed commit subject: `Preserve search parser error contracts`

Proposed commit message:

```text
Preserve search parser error contracts

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add src/Core/Ignixa.Search/Expressions/Parsers/SearchParseExceptionMapper.cs src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs src/Core/Ignixa.Search/Resources.resx src/Core/Ignixa.Search/Resources.Designer.cs test/Ignixa.Application.Tests/Search/Expressions/Parsers/SearchParserErrorParityTests.cs
git commit -m "Preserve search parser error contracts" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 15: Run the replacement benchmark, compare, and document

**Files:**
- Verify unchanged: `bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj`
- Verify unchanged: `bench/Ignixa.Benchmarks/SearchParserBenchmarkCase.cs`
- Verify unchanged: `bench/Ignixa.Benchmarks/BenchmarkSearchParameterDefinitionManager.cs`
- Verify unchanged: `bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs`
- Create from benchmark output: `docs/features/search/benchmarks/2026-07-10-superpower-parser.csv`
- Create from benchmark output: `docs/features/search/benchmarks/2026-07-10-superpower-parser.md`
- Create from comparison script: `docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md`
- Modify: `docs/features/search/readme.md:18-23`
- Modify: `docs/site/docs/core-sdk/search.md:7-17`

- [ ] **Step 1: Prove replacement correctness before measuring performance**

Run:

```powershell
dotnet build All.sln --no-restore
dotnet test All.sln --no-build
```

Expected: build succeeds with `0 Warning(s)` and `0 Error(s)`, and every test project reports `Failed: 0`. If correctness fails, stop: do not run the replacement benchmark with `CorrectnessStatus=Passed` and do not accept the parser regardless of performance.

- [x] **Step 2: Verify the benchmark harness and inputs are byte-for-byte unchanged**

Run:

```powershell
$manifest = 'docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256'
$mismatches = foreach ($line in Get-Content -LiteralPath $manifest) {
    $parts = $line -split '\s{2}', 2
    $expectedHash = $parts[0]
    $path = $parts[1]
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actualHash -ne $expectedHash) {
        "$path expected $expectedHash but was $actualHash"
    }
}
if ($mismatches) {
    throw "Benchmark harness changed:`n$($mismatches -join [Environment]::NewLine)"
}
```

Expected: no output and exit code 0. Any mismatch invalidates the baseline; restore the original harness or rerun both baseline and replacement with the revised harness.

- [x] **Step 3: Build the unchanged harness in the same configuration**

Run:

```powershell
dotnet restore bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj
dotnet build bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-restore
```

Expected: restore succeeds and the Release build reports `0 Warning(s)` and `0 Error(s)`.

- [x] **Step 4: Run the replacement with the exact baseline job settings**

Run:

```powershell
dotnet run --project bench/Ignixa.Benchmarks/Ignixa.Benchmarks.csproj -c Release --no-build -- --filter "*SearchExpressionParserBenchmarks*" --artifacts "BenchmarkDotNet.Artifacts/search-parser-replacement" --launchCount 1 --warmupCount 5 --iterationCount 15
```

Expected: BenchmarkDotNet completes the same six case names without exceptions, with populated mean, Gen0, and allocated-byte results.

- [x] **Step 5: Copy replacement output and verify the environment matches**

Run:

```powershell
$results = 'BenchmarkDotNet.Artifacts/search-parser-replacement/results'
$destination = 'docs/features/search/benchmarks'
Copy-Item -LiteralPath "$results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report.csv" -Destination "$destination/2026-07-10-superpower-parser.csv"
Copy-Item -LiteralPath "$results/Ignixa.Benchmarks.SearchExpressionParserBenchmarks-report-github.md" -Destination "$destination/2026-07-10-superpower-parser.md"
Get-Content "$destination/2026-07-10-handwritten-parser.md" -TotalCount 20
Get-Content "$destination/2026-07-10-superpower-parser.md" -TotalCount 20
```

Expected: both report headers show the same BenchmarkDotNet version, OS, processor, .NET SDK/runtime, architecture, and job configuration. If any environment field differs, the comparison is invalid; rerun both measurements in one matching environment.

- [x] **Step 6: Generate the mandatory percentage comparison report**

Run:

```powershell
pwsh -File tools/benchmarks/Compare-SearchParserBenchmarks.ps1 `
    -BaselineCsv docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv `
    -ReplacementCsv docs/features/search/benchmarks/2026-07-10-superpower-parser.csv `
    -CorrectnessStatus Passed `
    -OutputPath docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md
```

Expected: the command prints and writes a six-row Markdown report containing baseline/replacement mean time, operations/sec, allocated bytes, Gen0 collections, and percentage change for every metric.

- [ ] **Step 7: Apply the explicit performance acceptance rules**

Read:

```powershell
Get-Content docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md
```

Expected acceptance behavior:

- Correctness must be `Passed`; a correctness failure always rejects the replacement.
- A `>10%` regression in mean time, allocated bytes, or Gen0 collections for any case blocks merge until investigated and explicitly accepted by the user.
- The report may classify the replacement as **Faster** only when geometric-mean time improves by at least 5%, no case is slower by more than 5%, and neither allocated bytes nor Gen0 increases in any case.
- If classification is `Equivalent within the 5% threshold`, `Mixed`, or `Slower`, state exactly that result and do not claim Superpower is faster.

If a blocking regression exists, stop and present the affected cases and investigation to the user. Only after the user explicitly accepts that performance regression, regenerate the report with:

```powershell
pwsh -File tools/benchmarks/Compare-SearchParserBenchmarks.ps1 `
    -BaselineCsv docs/features/search/benchmarks/2026-07-10-handwritten-parser.csv `
    -ReplacementCsv docs/features/search/benchmarks/2026-07-10-superpower-parser.csv `
    -CorrectnessStatus Passed `
    -OutputPath docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md `
    -AcceptBlockingRegression
```

Expected: the report retains `Blocking regression detected: Yes` and changes acceptance to `Accepted by explicit user approval after investigation of the blocking regression.` Commit approval does not implicitly grant performance-regression acceptance.

- [ ] **Step 8: Document implementation and measured performance without overstating it**

Change the feature table row to:

```markdown
| [superpower-search-expression-parser](investigations/superpower-search-expression-parser.md) | **Implemented** | Superpower key/value grammars with tenant/version-aware semantic binding and positioned syntax errors |
```

Add this section after the `Ignixa.Search` introduction:

```markdown
## Search expression parsing

`IExpressionParser` remains the entry point used by `SearchOptionsBuilder`. Parser instances are created per tenant and FHIR version, so `SearchParameterInfo` lookup and reference-target validation use the active definition manager and schema.

The parser supports ordinary parameters, modifiers, typed forward chains, nested `_has`, include/revinclude forms, `_not-referenced`, escaped separators (`\,`, `\$`, `\|`, `\\`), comma alternatives, dollar composites, comparator prefixes, `:missing`, and `:of-type`.

Malformed key or value syntax raises `InvalidSearchOperationException` with a Superpower line/column diagnostic. Semantic failures retain the existing `SearchParameterNotSupportedException`, `BadSearchRequestException`, and resource-backed `InvalidSearchOperationException` messages. Atomic date, number, quantity, reference, string, token, and URI conversion continues to use the existing `*SearchValue.Parse` implementations.

The mandatory before/after BenchmarkDotNet results and acceptance decision are recorded in [the parser benchmark comparison](../../../features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md). The benchmark uses the same public-facade harness and six inputs for both implementations.
```

Append exactly one result sentence based on the generated classification:

```markdown
The replacement was classified as **Faster** under the documented acceptance thresholds.
```

or:

```markdown
The replacement was classified as **Equivalent within the 5% threshold**; no speedup is claimed.
```

or:

```markdown
The replacement produced **Mixed** performance results; no speedup is claimed.
```

or:

```markdown
The replacement was classified as **Slower**; no speedup is claimed.
```

Use the **Faster** sentence only when the generated report itself says `Performance classification: Faster`.

- [ ] **Step 9: Verify documentation and benchmark evidence**

Run:

```powershell
rg "fallback|legacy parser|old parser|two parser|dual parser" docs/features/search/readme.md docs/site/docs/core-sdk/search.md
rg "Correctness|Performance classification|Mean Δ|Ops/s Δ|Allocated Δ|Gen0 Δ" docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md
```

Expected: the first command has no matches and exits 1; the second finds all six mandatory comparison labels. The implemented feature row and public documentation describe one active parser, and the comparison report contains the explicit correctness/performance decision.

- [ ] **Step 10: Checkpoint and request commit approval**

Run:

```powershell
git --no-pager diff -- docs/features/search/benchmarks docs/features/search/readme.md docs/site/docs/core-sdk/search.md
git status --short
```

Proposed commit subject: `Compare Superpower search parser performance`

Proposed commit message:

```text
Compare Superpower search parser performance

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly ask the user to approve this commit. Only after approval:

```powershell
git add docs/features/search/benchmarks/2026-07-10-superpower-parser.csv docs/features/search/benchmarks/2026-07-10-superpower-parser.md docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md docs/features/search/readme.md docs/site/docs/core-sdk/search.md
git commit -m "Compare Superpower search parser performance" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

### Task 16: Run full verification and perform the final no-legacy audit

**Files:**
- Verify: `src/Core/Ignixa.Search/Expressions/Parsers/`
- Verify: `src/Core/Ignixa.Search/Indexing/StringExtensions.cs`
- Verify: `test/Ignixa.Application.Tests/Search/Expressions/Parsers/`
- Verify: `bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs`
- Verify: `docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256`
- Verify: `docs/features/search/benchmarks/2026-07-10-handwritten-parser.md`
- Verify: `docs/features/search/benchmarks/2026-07-10-superpower-parser.md`
- Verify: `docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md`
- Verify: `All.sln`

- [ ] **Step 1: Run focused parser tests**

Run:

```powershell
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ignixa.Application.Tests.Search.Expressions.Parsers" --no-restore
```

Expected: `Passed! - Failed: 0`; all direct tokenizer, grammar, binder, facade, characterization, and error parity tests pass.

- [ ] **Step 2: Restore the complete solution graph**

Run:

```powershell
dotnet restore All.sln
```

Expected: `Restore succeeded` or `All projects are up-to-date for restore`, with no package downgrade or vulnerability errors.

- [ ] **Step 3: Build every target framework**

Run:

```powershell
dotnet build All.sln --no-restore
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)` for the solution, including both `net9.0` and `net10.0` targets of `Ignixa.Search`.

- [ ] **Step 4: Run the full solution test suite**

Run:

```powershell
dotnet test All.sln --no-build
```

Expected: every test project reports `Failed: 0`; no search, chaining, include, composite, sorting, or indexing regression.

- [ ] **Step 5: Run cross-version compatibility tests**

Run:

```powershell
pwsh -File .\run-compat-tests.ps1
```

Expected: exit code 0 and compatibility summaries for the configured FHIR versions with no failed cases. This step is mandatory because key binding and resource-target validation are FHIR-version-sensitive.

- [ ] **Step 6: Revalidate the locked harness and matching benchmark environments**

Run:

```powershell
$manifest = 'docs/features/search/benchmarks/2026-07-10-search-parser-harness.sha256'
$mismatches = foreach ($line in Get-Content -LiteralPath $manifest) {
    $parts = $line -split '\s{2}', 2
    $expectedHash = $parts[0]
    $path = $parts[1]
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($actualHash -ne $expectedHash) {
        "$path expected $expectedHash but was $actualHash"
    }
}
if ($mismatches) {
    throw "Benchmark harness changed:`n$($mismatches -join [Environment]::NewLine)"
}

$baselineHeader = (Get-Content docs/features/search/benchmarks/2026-07-10-handwritten-parser.md -TotalCount 10) -join "`n"
$replacementHeader = (Get-Content docs/features/search/benchmarks/2026-07-10-superpower-parser.md -TotalCount 10) -join "`n"
if ($baselineHeader -ne $replacementHeader) {
    throw 'Benchmark environment headers differ; rerun both measurements in one environment.'
}
```

Expected: no output and exit code 0; the project, enum, definition manager, and public-facade benchmark hashes match the baseline manifest, and both reports identify the same BenchmarkDotNet/runtime/OS/CPU/job environment.

- [ ] **Step 7: Enforce comparison completeness and acceptance**

Run:

```powershell
$path = 'docs/features/search/benchmarks/2026-07-10-superpower-search-expression-parser-comparison.md'
$report = Get-Content -LiteralPath $path -Raw
$cases = @('Simple', 'Modified', 'TypedChain', 'NestedReverseChain', 'EscapedAlternative', 'Composite')
$columns = @('Mean Δ', 'Ops/s Δ', 'Allocated Δ', 'Gen0 Δ')

foreach ($case in $cases) {
    if ($report -notmatch "\|\s*$case\s*\|") {
        throw "Missing benchmark case: $case"
    }
}
foreach ($column in $columns) {
    if (-not $report.Contains($column)) {
        throw "Missing comparison metric: $column"
    }
}
if (-not $report.Contains('**Correctness:** **Passed**')) {
    throw 'Correctness is not Passed; reject the replacement.'
}
if ($report.Contains('**Blocking regression detected:** **Yes**') -and
    -not $report.Contains('Accepted by explicit user approval after investigation of the blocking regression.')) {
    throw 'A >10% time, allocation, or Gen0 regression requires investigation and explicit user acceptance.'
}

$documentation = Get-Content docs/site/docs/core-sdk/search.md -Raw
$reportSaysFaster = $report.Contains('**Performance classification:** **Faster**')
$documentationClaimsFaster = $documentation -match 'classified as \*\*Faster\*\*'
if ($documentationClaimsFaster -and -not $reportSaysFaster) {
    throw 'Documentation claims Faster without benchmark proof.'
}
```

Expected: no output and exit code 0. All six cases and four percentage metrics are present, correctness is `Passed`, no unaccepted blocking regression exists, and a **Faster** documentation claim appears only when the report proves it. A failure blocks completion; correctness failure rejects the replacement, while a performance regression requires investigation and explicit user acceptance before proceeding.

- [ ] **Step 8: Audit production code for forbidden legacy/fallback behavior**

Run:

```powershell
rg "TrySplit|TryConsume|SplitByOrSeparator|SplitByCompositeSeparator|LegacyExpressionParser|(?i:fallback)|catch \(Exception" src/Core/Ignixa.Search/Expressions/Parsers src/Core/Ignixa.Search/Indexing/StringExtensions.cs
```

Expected: no matches and exit code 1. Then run:

```powershell
rg "DateTimeSearchValue.Parse|NumberSearchValue.Parse|QuantitySearchValue.Parse|referenceParser.Parse|StringSearchValue.Parse|TokenSearchValue.Parse|UriSearchValue.Parse" src/Core/Ignixa.Search/Expressions/Parsers/SearchAtomicValueParser.cs
```

Expected: all seven canonical atomic parser dispatches are present.

- [ ] **Step 9: Verify public contracts and dependency boundaries**

Run:

```powershell
git --no-pager diff -- src/Core/Ignixa.Search/Expressions/Parsers/IExpressionParser.cs src/Core/Ignixa.Search/Expressions/Parsers/ISearchParameterExpressionParser.cs src/Application/Ignixa.Application/Features/Search/SearchOptionsBuilderFactory.cs
git --no-pager diff -- src/Core/Ignixa.Search/Expressions ":(exclude)src/Core/Ignixa.Search/Expressions/Parsers/**"
rg "Hl7\.Fhir" src/Core/Ignixa.Search src/Application/Ignixa.Application
```

Expected: no diff for both parser interfaces, existing expression model files, or `SearchOptionsBuilderFactory`; no newly introduced `Hl7.Fhir.*` use in `Ignixa.Search` or application code.

- [ ] **Step 10: Inspect the final change set**

Run:

```powershell
git --no-pager diff --stat
git --no-pager diff --check
git status --short
```

Expected: `git diff --check` produces no output; status lists only the planned production, tests, benchmark, and documentation changes plus any pre-existing user changes. Do not stage or modify unrelated files.

- [ ] **Step 11: Final checkpoint and request commit approval**

Proposed commit subject if verification fixes were required: `Complete Superpower search parser verification`

Proposed commit message:

```text
Complete Superpower search parser verification

Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
```

Explicitly show the final `git diff --stat` and `git status --short`, then ask the user whether to create this verification-fix commit. If no files changed during verification, state that no commit is needed. Only after explicit approval and only when verification changed files, stage each exact path approved by the user individually and then run:

```powershell
git commit -m "Complete Superpower search parser verification" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```
