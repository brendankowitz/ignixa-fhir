# Investigation: Superpower Search Expression Parser

**Feature**: search
**Status**: Implemented - Revised Option 3 Accepted 2026-07-11
**Created**: 2026-07-10
**Updated**: 2026-07-11

## Executive Summary

The Superpower search expression parser was implemented without changing public parser contracts, but its measured per-request cost was unacceptable. The tokenizer/grammar layer was therefore replaced with direct handwritten syntax scanners.

The implemented design retains:

- immutable `SearchKeySyntax` and `SearchValueSyntax` records;
- schema-aware semantic binders;
- the existing `IExpressionParser` and `ISearchParameterExpressionParser` facades;
- canonical atomic search value parsers and the parity/binder test suite.

Handwritten scanners now own key and value syntax parsing without a token-list intermediate. Tenant- and FHIR-version-specific lookup remains isolated in the semantic binders.

This parser concerns `Ignixa.Search.Expressions`. It is separate from the FHIRPath expression parser; the FHIRPath and Mapping Language parsers are relevant only as established in-repository Superpower patterns.

## Benchmark Outcome and Revised Decision (2026-07-11)

The full Superpower grammar described below was implemented (commit `02eb4a5 Reimplement search parser with Superpower`) and benchmarked against the handwritten baseline exactly as this document's Testing Strategy required. It failed the document's own acceptance bar and is **rejected as specified**. This section is the record of that outcome; the rest of the document is retained as the design rationale for the parts that are kept (see below), not as the adopted plan.

### Measured results

`bench/Ignixa.Benchmarks/SearchExpressionParserBenchmarks.cs`, BenchmarkDotNet, .NET 10.0, 15 iterations:

| Case | Baseline (handwritten) | Superpower replacement | Slowdown | Allocation increase |
|---|---:|---:|---:|---:|
| Simple | 142.2 ns / 544 B | 1.774 μs / 4.49 KB | ~12.5x | ~8.5x |
| Modified | 157.5 ns / 608 B | 2.100 μs / 5.25 KB | ~13.3x | ~8.6x |
| TypedChain | 277.7 ns / 1152 B | 2.949 μs / 7.26 KB | ~10.6x | ~6.3x |
| NestedReverseChain | 560.0 ns / 2208 B | 6.886 μs / 13.19 KB | ~12.3x | ~6.0x |
| EscapedAlternative | 522.0 ns / 1904 B | 3.485 μs / 8.07 KB | ~6.7x | ~4.2x |
| Composite | 922.6 ns / 3736 B | 4.859 μs / 11.76 KB | ~5.3x | ~3.1x |

This is not a marginal regression within the document's "no material throughput or allocation regression" bar — it is 5-13x slower and 3-9x more allocation across every case, with no case exempt.

### Why: inherent workload mismatch, not an implementation defect

The implementation was reviewed against the actual code (not just the benchmark numbers) and judged mostly **inherent** to the approach for this workload. Regex-based key tokenization, substring-heavy value segment joining, and a per-parse token-list allocation are identifiable optimization opportunities, but no measured improvement factor is available and no estimate is treated as fact.

The measurements and implementation-phase probe show a workload mismatch this document's "consistency with the existing FHIRPath/Mapping Language Superpower pattern" argument did not account for. FHIRPath expressions are low-cardinality and cached — parsed once, evaluated many times, so tokenizer/combinator cost amortizes toward zero. Search key and value strings are parsed **fresh on every HTTP request** with unbounded value cardinality (arbitrary names, dates, quantities), so this implementation paid tokenizer, token-list, and grammar overhead on every short input. That observed overhead dominated the six measured cases and justifies a direct scanner as the next experiment. The benchmark does not establish that every possible Superpower implementation must lose to every scanner.

### What is kept vs. rejected

The rewrite produced two separable things, and only one of them failed:

- **Kept**: the syntax-node model (`Syntax/` types), `SearchKeyBinder`, `SearchExpressionBinder`, and the new characterization/parity/binder test suite (~1,600 lines). This is the durable value of the migration — it fixes the actual problem this document opened with (parsing, schema resolution, validation, and expression construction interleaved across ~66 branches, untestable independent of schema mocks). None of this is Superpower-specific; the facade (`ExpressionParser`) already isolates the grammar layer from everything downstream.
- **Rejected**: the Superpower tokenizers and grammars (`SearchKeyTokenizer`, `SearchKeyGrammar`, `SearchValueTokenizer`, `SearchValueGrammar`, `SearchParseExceptionMapper` — under 500 lines total). This is the thin, replaceable surface actually responsible for the regression.

The "Key syntax plus semantic binder" hybrid (Superpower for key parsing only, handwritten value parsing) considered in Approaches Considered below was evaluated and **rejected**. The *Simple* case (`name=Smith`, no chains/modifiers/composites) regressed 142ns→1.77μs, but that public-facade benchmark parses both the key and value and therefore does not isolate key-parser cost. The end-to-end result, the per-request workload mismatch, and the added complexity of two parsing strategies do not justify retaining Superpower for either side.

### Revised plan

Adopt **Option 3** from Approaches Considered ("Handwritten parser with extracted syntax model"), which this document originally rejected only because it "does not meet the parser consistency objective" — an objective this benchmark result invalidates for this specific hot path. Concretely: replace the Superpower tokenizer/grammar layer with handwritten recursive-descent/span scanners that emit the same `SearchKeySyntax`/`SearchValueSyntax` nodes the binders already consume, so nothing downstream of the grammar layer changes. The binders and parity/characterization test suite from the rejected implementation are reused. The unchanged six-case harness must be rerun after key cutover for diagnosis and after final cutover for acceptance against the original handwritten baseline; no near-baseline outcome is assumed. Remove the `Superpower` package reference from `Ignixa.Search.csproj` only after no search-parser use remains.

**Ratified by the feature owner on 2026-07-11**: retain the syntax-node/binder separation, replace the Superpower tokenizer/grammar layer with allocation-conscious handwritten scanners, and drop "parsing-library consistency with FHIRPath/Mapping Language" as a design goal for the search expression parser specifically. Performance acceptance remains measurement-driven; any threshold violation requires investigation and separate explicit user acceptance.

The feature owner ratified revised Option 3 on 2026-07-11. The Superpower tokenizer/grammar layer remains rejected; the immutable syntax records, binders, facades, canonical atomic parsing, and characterization/parity/binder tests are retained. The replacement uses direct handwritten source scanners with positioned diagnostics and no token-list intermediate.

The [final locked comparison](../benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md) classified the replacement as **Mixed**, with a geometric-mean time change of -6.31%. Correctness passed, no blocking regression was detected, and all ratified per-case time, allocation, and Gen0 limits passed. The replacement was not classified as **Faster** under the stricter criteria, so no speedup is claimed.

## Current State

Search parsing uses a syntax-scanner and semantic-binder pipeline:

| Component | Responsibility |
|---|---|
| `SearchKeySyntaxParser` | Ordinary parameters, modifiers, forward/reverse chains, includes, and `_not-referenced` syntax |
| `SearchValueSyntaxParser` | Comparators, alternatives, composites, escaping, and modifier-specific value syntax |
| `SearchKeyBinder` / `SearchExpressionBinder` | Tenant- and FHIR-version-specific semantic validation and expression construction |
| `ExpressionParser` / `SearchParameterExpressionParser` | Stable public parser facades |

Malformed syntax now reports positioned line/column diagnostics. Schema lookup and atomic value conversion remain outside the scanners.

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
| Key syntax plus semantic binder | Parameters, modifiers, chains, includes, `_not-referenced` | 4-6 days | Lower migration risk; clear grammar/semantic boundary | Retains handwritten value delimiter parsing. **Evaluated post-benchmark and rejected**: the end-to-end `Simple` case, which parses both key and value, regressed ~12.5x; it does not isolate key-parser cost, and a mixed parsing strategy is not justified by the overall workload result. |
| ~~Full search expression grammar~~ | Key syntax plus comparators, alternatives, composites, escaping, and modifier-specific value forms | 7-10 days | Consistent parsing architecture across the complete search expression pipeline | Larger parity surface; context-sensitive value grammar. **Implemented and benchmarked; rejected** — 5-13x slower and 3-9x more allocation than baseline across all cases, failing this document's own performance acceptance bar. See "Benchmark Outcome and Revised Decision" above. |
| **Handwritten parser with extracted syntax model** | No Superpower parsing | Historical estimate: ~~2-3 days~~. Follow-up effort is governed by the detailed implementation plan and measured acceptance gates. | Lowest-risk structural cleanup | ~~Does not meet the parser consistency objective~~ **Selected**: the parser-library-consistency objective is invalidated by the benchmark data for this per-request, unbounded-cardinality hot path (see above). Structural/testability goals are retained via the kept syntax-node and binder layers. |

The full search expression grammar was originally selected because the goal was to replace handwritten delimiter parsing across the complete search expression pipeline rather than only reorganize the existing implementation. That goal is retained for the syntax-node/binder structure; the Superpower-specific tokenizer/grammar layer used to reach it is not, per the benchmark outcome above.

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
| Declarative grammar makes accepted syntax inspectable | Larger migration than parsing only the key |
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

**Superseded 2026-07-11 — see "Benchmark Outcome and Revised Decision" above for the current status.**

~~The full Superpower search expression grammar is viable and selected for production implementation.~~
~~Proceed with a compatibility-preserving rewrite behind the existing parser interfaces. Treat escaped value tokenization and error parity as first-class deliverables, not follow-up cleanup.~~

This verdict was implemented and benchmarked as required by this document's own Testing Strategy. The full Superpower grammar measured 5-13x slower and 3-9x more allocation than the handwritten baseline across every representative case, failing the stated "no material throughput or allocation regression" acceptance bar. The grammar/tokenizer layer is rejected; the syntax-node model, semantic binders, and test suite it produced are retained. Current direction: replace the Superpower tokenizer/grammar layer with handwritten recursive-descent/span scanners emitting the same syntax nodes (Approaches Considered, Option 3). Follow-up effort and completion are governed by the focused implementation plan and measured acceptance gates, not a shortened estimate. Compatibility-preserving behavior behind the existing `IExpressionParser`/`ISearchParameterExpressionParser` interfaces remains the goal; escaped value tokenization and error parity remain first-class deliverables for the replacement scanner, proven against the parity tests already written for this migration.
