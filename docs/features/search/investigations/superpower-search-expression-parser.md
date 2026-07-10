# Investigation: Superpower Search Expression Parser

**Feature**: search
**Status**: Viable - Selected for Implementation
**Created**: 2026-07-10

## Executive Summary

The Ignixa search expression parser can be reimplemented with Superpower without changing the public parser contracts, the existing `Ignixa.Search.Expressions` model, or downstream query execution.

The selected approach is a production-ready replacement of both handwritten parser layers:

- `ExpressionParser` key parsing for parameters, modifiers, forward chains, reverse chains, includes, and `_not-referenced`
- `SearchParameterExpressionParser` value syntax for comparators, alternatives, composites, escaping, and modifier-specific values

Superpower will own syntactic parsing. Tenant- and FHIR-version-specific search parameter resolution will remain a separate semantic binding step. Existing atomic search value parsers will remain the canonical implementation for date, number, quantity, reference, string, token, and URI conversion.

Estimated effort is **7-10 engineering days** for one engineer familiar with the search subsystem. The largest uncertainties are escaped value tokenization and compatibility with existing error behavior.

This parser concerns `Ignixa.Search.Expressions`. It is separate from the FHIRPath expression parser; the FHIRPath and Mapping Language parsers are relevant only as established in-repository Superpower patterns.

## Current State

Search parsing is split across three handwritten components:

| Component | Responsibility | Size |
|---|---|---:|
| `ExpressionParser` | Search keys, modifiers, chains, includes, `_not-referenced`, schema-aware expression construction | Approximately 380 lines |
| `SearchParameterExpressionParser` | Comparators, comma alternatives, composites, special modifiers, typed value dispatch | Approximately 400 lines |
| `StringExtensions` | Escaped splitting for `,`, `$`, and `|` | 175 lines |

The parser code contains approximately 66 conditional or loop branches. Parsing, schema resolution, validation, and expression construction are interleaved. This makes the accepted grammar difficult to inspect independently and produces manually authored errors without source positions.

The public integration surface is small:

```csharp
public interface IExpressionParser
{
    Expression Parse(string[] resourceTypes, string key, string value);

    IncludeExpression ParseInclude(
        string[] resourceTypes,
        string includeValue,
        bool isReversed,
        bool iterate);
}
```

`SearchOptionsBuilder` is the primary consumer. Parser instances are built per tenant and FHIR version because search parameter definitions and reference parsing are context-specific.

## Goals

- Replace handwritten search expression syntax parsing with Superpower.
- Preserve `IExpressionParser` and `ISearchParameterExpressionParser` public behavior.
- Preserve the existing search expression tree and all downstream visitors.
- Separate syntax parsing from tenant- and version-specific semantic binding.
- Parse all currently supported search syntax, including escaped values.
- Improve malformed-syntax diagnostics with token and source-position information.
- Remove the handwritten production parser after parity is established.

## Non-Goals

- Reimplement the FHIRPath parser.
- Change search expression evaluation or SQL translation.
- Add new FHIR search features as part of the migration.
- Replace atomic `ISearchValue.Parse` implementations.
- Preserve whitespace, comments, or source text for round-tripping.
- Keep two production parser paths or silently fall back to the old parser.

## Supported Syntax

The replacement must preserve the current supported surface:

- Ordinary parameters: `name=Smith`
- Modifiers: `name:exact=Smith`, `identifier:of-type=system|code|value`
- Reference target qualifiers: `subject:Patient=123`
- Forward chains: `subject.name=Smith`
- Explicitly typed chains: `subject:Patient.name=Smith`
- Reverse chains: `_has:Observation:subject:code=1234-5`
- Nested forward and reverse chains
- Includes and reverse includes, including target types, wildcards, and iteration
- `_not-referenced` resource/path and wildcard forms
- Comparator prefixes for date, number, and quantity values
- Comma-separated alternatives
- Dollar-separated composite components
- FHIR search escaping for `\,`, `\$`, `\|`, and `\\`
- Special modifier payloads such as `:missing` and `:of-type`

## Worked Example

For:

```text
Observation?patient:Patient._has:Group:member:_tag=http://example.org/tags|reviewed
```

the current API receives:

```text
resourceTypes = ["Observation"]
key           = "patient:Patient._has:Group:member:_tag"
value         = "http://example.org/tags|reviewed"
```

The key grammar produces:

```text
ForwardChain
  reference = patient
  targetType = Patient
  next = ReverseChain
    sourceType = Group
    reference = member
    next = Parameter
      name = _tag
      modifier = none
```

The semantic binder then:

1. Resolves `patient` for `Observation`.
2. Validates `Patient` against the parameter's allowed targets.
3. Resolves `member` for `Group`.
4. Resolves `_tag` for the resulting target context.
5. Selects the token value grammar.
6. Parses the terminal token value.
7. Builds the existing nested `ChainedExpression` and `SearchParameterExpression` objects.

For a composite value:

```text
http://loinc.org|8480-6$gt120,29463-7$lt80
```

the value grammar produces:

```text
Alternatives
  Composite
    Atomic("http://loinc.org|8480-6")
    Comparator(gt, "120")
  Composite
    Atomic("29463-7")
    Comparator(lt, "80")
```

The binder validates component count, determines each component's effective search type, invokes the existing atomic value parser, and constructs:

```text
(code = http://loinc.org|8480-6 AND value > 120)
OR
(code = 29463-7 AND value < 80)
```

## Approaches Considered

| Option | Superpower Scope | Effort | Advantages | Disadvantages |
|---|---|---:|---|---|
| Key syntax plus semantic binder | Parameters, modifiers, chains, includes, `_not-referenced` | 4-6 days | Lower migration risk; clear grammar/semantic boundary | Retains handwritten value delimiter parsing |
| **Full search expression grammar** | Key syntax plus comparators, alternatives, composites, escaping, and modifier-specific value forms | **7-10 days** | Consistent parsing architecture across the complete search expression pipeline | Larger parity surface; context-sensitive value grammar |
| Handwritten parser with extracted syntax model | No Superpower parsing | 2-3 days | Lowest-risk structural cleanup | Does not meet the parser consistency objective |

The full search expression grammar was selected because the goal is to replace handwritten delimiter parsing across the complete search expression pipeline rather than only reorganize the existing implementation.

## Proposed Architecture

```text
(key, value, resourceTypes)
        |
        v
SearchKeyTokenizer + SearchKeyGrammar
        |
        v
SearchKeySyntax
        |
        v
SearchKeyBinder
  - resolves tenant/version SearchParameterInfo
  - validates targets and ambiguity
        |
        v
BoundSearchKey + terminal parameter type/modifier
        |
        v
SearchValueTokenizer + selected SearchValueGrammar
        |
        v
SearchValueSyntax
        |
        v
SearchExpressionBinder
        |
        v
Existing Ignixa.Search.Expressions.Expression tree
```

This boundary follows the same broad tokenizer, grammar, syntax, and semantic build pattern used by the in-repository FHIRPath parser while accounting for search parsing's tenant- and schema-dependent semantics.

### Key Parsing

Key parsing is independent of search parameter definitions. It identifies structural syntax but does not decide whether a named parameter, modifier, or target type is valid.

`SearchKeyTokenizer` can use `TokenizerBuilder<SearchTokenKind>` because key punctuation has stable meaning. `SearchKeyGrammar` will parse immutable syntax nodes representing:

- terminal parameters
- modifiers
- forward-chain segments
- reverse-chain segments
- optional reference target types
- includes
- `_not-referenced`

The grammar must support arbitrary nesting through `Parse.Ref()` rather than a fixed chain depth.

### Semantic Key Binding

`SearchKeyBinder` receives the parsed key and the request's resource type context. It owns:

- search parameter definition lookup
- validation that parameters are common to multi-resource searches
- reference type enforcement for chain segments
- explicit target type validation
- target intersection and chain ambiguity detection
- unsupported-target filtering
- resolution of the terminal search parameter and modifier

These behaviors cannot be encoded correctly in a context-free grammar because definitions vary by FHIR version and tenant.

### Value Parsing

Value syntax is selected after key binding identifies the terminal search parameter type and modifier.

Shared value combinators will parse:

- escaped atomic text
- unescaped comma alternatives
- unescaped dollar composite components
- unescaped token separators where required
- comparator prefixes

Specialized grammars will handle:

- ordinary scalar and alternative values
- comparator-bearing date, number, and quantity values
- composite values
- `:missing` booleans
- `:of-type` triplets
- reference target modifiers

A custom Superpower tokenizer is preferred for values. Whether `,`, `$`, `|`, and `\` are structural depends on escaping and the selected grammar. `TokenizerBuilder` is appropriate for the stable key grammar but would make escaped value handling harder to reason about.

Atomic text remains input to existing `ISearchValue` parsers. The new grammar will not duplicate date precision, quantity, URI, token, or reference validation.

### Expression Binding

`SearchExpressionBinder` converts bound key and value syntax into the existing expression types. It owns:

- modifier applicability validation
- composite component resolution and count validation
- current effective-type inference for composite components
- construction of AND/OR/NOT expressions
- comparator propagation
- reference target modifier application
- wrapping terminal values in `SearchParameterExpression`
- construction of forward and reverse `ChainedExpression` nodes

The expression model and all query visitors remain unchanged.

## Proposed Components

| Component | Responsibility |
|---|---|
| `SearchTokenKind` | Search key and value token categories |
| `SearchKeyTokenizer` | Tokenize stable key punctuation and text |
| `SearchKeyGrammar` | Parse parameter, chain, include, and `_not-referenced` syntax |
| `SearchValueTokenizer` | Tokenize escaped and structural value characters |
| `SearchValueGrammar` | Shared and type-selected value combinators |
| Key syntax node types | Immutable parameter, chain, include, and `_not-referenced` syntax |
| Value syntax node types | Immutable atomic, comparator, alternatives, composite, missing, and of-type syntax |
| `SearchKeyBinder` | Resolve definitions and validate key semantics |
| `SearchExpressionBinder` | Convert bound syntax into existing expression nodes |
| `SearchParseExceptionMapper` | Map syntax failures to search exceptions and diagnostics |
| `ExpressionParser` | Compatibility facade retaining the public interface |

Each syntax node type should be in its own file. Parser components should remain independent of HTTP and data-layer concerns.

## Error Handling

The migration will preserve externally observable exception categories:

- `SearchParameterNotSupportedException` for unsupported terminal or chain parameters
- `BadSearchRequestException` for common-parameter and typed-value request failures
- `InvalidSearchOperationException` for invalid search syntax or semantic combinations

Existing resource-backed messages will remain for semantic failures such as:

- unsupported modifiers
- invalid or unsupported target resource types
- ambiguous chains
- non-reference chain parameters
- invalid include forms
- invalid typed values

New malformed-syntax failures can include Superpower's token expectation and source position. There will be no broad catch that hides unexpected exceptions and no successful fallback to the old parser.

## Compatibility Strategy

The migration will use one public facade and temporarily retain the old implementation only for test-time parity comparison.

1. Add the new parser as internal components.
2. Characterize current behavior with direct unit tests.
3. Compare normalized expression trees from old and new implementations for valid cases.
4. Compare exception categories and stable semantic messages for invalid cases.
5. Switch the public facade to the new implementation.
6. Remove the old parser and parity adapter before merge.

The production assembly will contain one active parsing path.

## Testing Strategy

### Characterization Tests

Add direct unit tests for `ExpressionParser` and `SearchParameterExpressionParser`. Port relevant parser cases from Microsoft FHIR Server and cover Ignixa-specific behavior:

- `_not-referenced`
- include target validation
- nested `_has`
- `:of-type`
- URI `:above` and `:below`
- tenant/version-specific search parameter lookup

### Tokenizer and Grammar Tests

Test valid and invalid forms independently of semantic binding:

- ordinary, modified, typed, chained, and reverse-chained keys
- arbitrary nested chain combinations
- include, reverse include, wildcard, and iterate syntax
- escaped `\,`, `\$`, `\|`, and `\\`
- trailing escape characters
- empty alternatives and composite components
- multiple modifiers
- extra or missing chain separators
- unexpected tokens and incomplete inputs

### Parity Tests

For valid inputs, compare normalized old and new expression trees rather than object identity. For invalid inputs, assert:

- exception type
- stable semantic message
- source position for new syntax errors

### Regression Tests

Retain the existing search test surface as downstream validation. The current search-focused suites contain approximately 264 facts and 30 theories across data types, modifiers, chaining, includes, sorting, and indexing.

### Performance

Add `SearchExpressionParserBenchmarks` to the existing BenchmarkDotNet project at `bench/Ignixa.Benchmarks`. Capture and retain a baseline from the current parser before changing production parsing code, then rerun the identical benchmark cases after the Superpower cutover.

Representative cases must include:

- simple string parameter
- modified token parameter
- typed forward chain
- nested reverse chain
- escaped alternative values
- multi-component composite values

Record mean execution time, operations per second, allocated bytes, and Gen0 collections for each case. Report the absolute results and percentage change between implementations.

Correctness remains mandatory even if the new parser is faster. Performance acceptance is no material throughput or allocation regression; any regression must be explained and explicitly accepted before merge. Superpower adoption alone is not evidence of a performance improvement, and the implementation must not claim a speedup unless the before/after results demonstrate one.

## Estimated Effort

| Activity | Estimate |
|---|---:|
| Characterization and direct parser tests | 1.5-2.0 days |
| Tokenizers, grammars, and syntax models | 2.0-3.0 days |
| Semantic binding and facade integration | 1.5-2.0 days |
| Error compatibility and regression fixes | 1.0-2.0 days |
| Documentation and review allowance | 1.0 day |
| **Total** | **7-10 engineering days** |

The estimate assumes:

- one engineer familiar with the search subsystem
- behavior parity rather than new search features
- no change to the expression model or query execution
- focused unit tests plus the existing search suites
- normal review and integration overhead

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Escaped separators are tokenized incorrectly | Incorrect OR, composite, or token semantics | Custom value tokenizer and exhaustive escaped-delimiter tests |
| Grammar performs schema-dependent work | Tight coupling and hard-to-test parser | Keep definition resolution in binders |
| Existing edge behavior is undocumented | Regressions discovered late in E2E tests | Characterization and old/new parity tests before facade switch |
| Error text changes unexpectedly | API compatibility and test failures | Preserve semantic messages; improve only syntax diagnostics |
| Superpower adds allocations on common paths | Search request throughput regression | Benchmark representative cases and avoid allocation-heavy combinator patterns |
| Old and new paths coexist | Maintenance divergence and hidden fallback | Remove old parser and parity adapter before merge |
| Transitive dependency is relied upon accidentally | Fragile package graph | Add a direct `Superpower` reference to `Ignixa.Search` |

## Tradeoffs

| Pros | Cons |
|---|---|
| Declarative grammar makes accepted syntax inspectable | Larger migration than key-only parsing |
| Consistent tokenizer/grammar architecture with other Ignixa parsers | Value syntax requires a custom tokenizer |
| Better malformed-input diagnostics | Exact error compatibility requires deliberate mapping |
| Syntax can be tested without schema mocks | Semantic binding remains a separate complex phase |
| Removes duplicated delimiter logic | Temporary parity infrastructure adds implementation work |
| Public and downstream expression contracts remain stable | Superpower becomes a direct dependency of `Ignixa.Search` |

## Alignment

- [x] Follows architectural layering rules
- [x] Developer Experience (works with minimal setup)
- [x] Specification compliance
- [x] Consistent with existing patterns

## Evidence

- `ExpressionParser` currently combines key parsing, schema resolution, validation, and expression construction in `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs`.
- `SearchParameterExpressionParser` currently combines value splitting, comparator handling, modifiers, composite binding, and typed value conversion in `src/Core/Ignixa.Search/Expressions/Parsers/SearchParameterExpressionParser.cs`.
- FHIR escaped delimiter splitting is implemented manually in `src/Core/Ignixa.Search/Indexing/StringExtensions.cs`.
- `SearchOptionsBuilder` consumes the parser only through `IExpressionParser`, allowing the implementation to change without affecting callers.
- `SearchOptionsBuilderFactory` constructs parser instances per tenant and FHIR version, confirming that schema resolution belongs outside the grammar.
- `Ignixa.FhirPath` and `Ignixa.FhirMappingLanguage` provide in-repository Superpower tokenizer and grammar examples.
- Superpower 3.1.0 is already centrally versioned in `Directory.Packages.props`.
- The existing `bench/Ignixa.Benchmarks` project uses BenchmarkDotNet with memory diagnostics and can host repeatable before/after parser benchmarks without introducing new tooling.
- The FHIR R5 search specification defines reverse chaining, composites, alternatives, and backslash escaping: <https://hl7.org/fhir/R5/search.html>.
- Superpower supports token-driven parsing, parser combinators, source-positioned errors, and custom tokenizers: <https://github.com/datalust/superpower>.
- Microsoft FHIR Server's `ExpressionParserTests` provide useful upstream characterization cases for chain, modifier, include, and invalid-input behavior.

## Verdict

The full Superpower search expression grammar is viable and selected for production implementation.

Proceed with a compatibility-preserving rewrite behind the existing parser interfaces. Treat escaped value tokenization and error parity as first-class deliverables, not follow-up cleanup.
