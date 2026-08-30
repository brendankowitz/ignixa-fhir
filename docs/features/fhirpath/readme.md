# Feature: FHIRPath

FHIRPath expression evaluation engine for FHIR resource querying and data extraction.

## Status

In Progress

## Overview

This feature provides FHIRPath expression evaluation capabilities used throughout the FHIR server for search parameters, invariants, validation, and data extraction.

## Reference

| Document | Description |
|----------|-------------|
| [Firely 5.11.4 Parity Inventory](firely-parity.md) | Every behaviour that differs between Ignixa and the Firely 5.11.4 engine the [fhir-server seam](https://github.com/microsoft/fhir-server/blob/personal/bkowitz/ignixa-fhirpath-seam/docs/arch/adr-2608-ignixa-fhirpath-seam.md) replaces, ranked by reachability from shipped SearchParameter expressions. Kept current by a differential harness that fails on a new divergence. |

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [Performance Optimization](investigations/performance-optimization.md) | Complete | 2025-10-16 | Performance analysis and optimization strategies for FHIRPath evaluation |
| [Gap Analysis](investigations/gap-analysis.md) | Superseded | 2025-11-18 | Analysis of FHIRPath implementation gaps and missing functionality |
| [Visitor Pattern Evaluation](investigations/visitor-pattern-evaluation.md) | Complete | 2026-01-09 | Comparison of switch-based vs visitor pattern for FhirPath AST traversal |
| [Performance vs Firely SDK](investigations/fhirpath-performance-analysis.md) | Superseded | 2026-01-11 | Architectural comparison of the two engines. Its headline multipliers (2,700-3,220x) were measured against a Firely path dominated by per-call POCO conversion and are not reproducible; see the measured table in this file |
| [Official Test Suite Integration](investigations/official-test-suite-integration.md) | Complete | 2026-01-12 (baseline re-measured 2026-08-24) | Leveraging HL7's official FHIRPath test cases for specification compliance validation. **Conformance baseline (measured 2026-08-24, `fhir-test-cases` 1.7.46 pinned per file by SHA-256, `--filter "Category=OfficialTestSuite"`): 2,884 passed / 0 failed / 16 skipped with named reasons / 3 excluded by scope (CDA), of 2,903 corpus cases.** Measured by a runner whose ability to fail is pinned in CI at both edges |
| [Instance Creation Delegate](investigations/instance-creation-delegate.md) | Implemented | 2026-06-16 | Instance-selector construction is delegated to a host-provided creation delegate on EvaluationContext; documents the spec-silent choices Ignixa makes |
| [FHIRPath Release Readiness](investigations/release-readiness.md) | In Progress | 2026-08-21 | Plan for FHIRPath release readiness covering spec conformance, production defects, evidence-base repair, and official suite validation - corrected in place as execution proceeds (D1 and the E2-E6 evidence-base fixes are done; D4 was resolved by measurement rather than a code guard; a new D6 hard blocker, a `canonical` search-value converter gap, was found along the way) |
| [PR #427 Known Residuals](investigations/pr427-residuals.md) | In Progress | 2026-08-21 | Implementation plan for PR #427 residual issues including analyzer fixes, guard hardening, and tier decisions |

### Performance Comparison: Ignixa vs Firely

Measured 2026-08-28 on .NET 10.0.11 (AMD EPYC 7763, 16 vCPU, Hyper-V) with BenchmarkDotNet 0.15.8.
Reproduce with:

```
dotnet run -c Release --project bench/Ignixa.Benchmarks --framework net10.0 -- --filter '*FhirPathBenchmarks*'
```

**The comparison to quote is `Eval-*`**: both engines pre-compiled, model materialized once, evaluation
only. This is the only like-for-like measurement of engine speed in the suite.

| Expression | Ignixa | Firely 6.0.1 | Faster | Ignixa alloc | Firely alloc | Leaner |
|---|---|---|---|---|---|---|
| Simple (`name.family`) | 242.8 ns | 2,082.5 ns | **8.6x** | 248 B | 4,520 B | **18.2x** |
| Complex (`where` + `first`) | 535.4 ns | 6,461.7 ns | **12.1x** | 648 B | 9,496 B | **14.7x** |
| Search parameter extraction | 1,547.0 ns | 11,117.0 ns | **7.2x** | 1,248 B | 17,696 B | **14.2x** |

End-to-end search parameter extraction over the full R4 core set, which is what a write actually pays:

| Resource | Time | Allocated |
|---|---|---|
| Patient (small) | 57.6 µs | 77.15 KB |
| Observation (medium) | 180.8 µs | 229.72 KB |

**Do not quote the `Execution-*` series.** It shows Firely at ~0.8-1.0 ms for trivial expressions, an
apparent ~3,000x gap, because Firely's `ITypedElement.Select(string)` calls `ToPocoNode`, which
re-deserializes the whole resource into POCOs on every call when the input is source-backed. That series
measures a real and avoidable API cost, not engine speed. The `Hybrid` series (Firely's model, Ignixa's
engine) isolates the same distinction from the other side.

**Architecture Differences**:
- **Ignixa**: Pattern-based `Func<>` delegate compilation with dual-level caching (AST + delegates), over
  an element model that memoises child resolution and materialized wrappers against the source node
- **Firely**: Universal interpreter using `Invokee` delegate chains with Dictionary-based Closure context

**Evidence**:
- [Performance Analysis](investigations/fhirpath-performance-analysis.md) - Architectural comparison
- [IL Code Analysis](investigations/il-analysis.md) - IL disassembly proving compiled delegates
- [Assembly Comparison](investigations/assembly-code-comparison.md) - Native x86-64 code analysis
- [Full Disassembly](investigations/assembly-disassembly.md) - BenchmarkDotNet output (590 KB)

> **Superseded claims.** This section previously reported "3,220x faster", "85.02 ns", and "136x less
> memory". Those came from a benchmark class (`FhirPathILBenchmarks`) that no longer exists in the
> repository, against a Firely denominator inflated by the `ToPocoNode` conversion described above, and
> could not be reproduced at any commit still in history. The three investigation documents linked above
> remain useful on architecture but carry the same multipliers in their own headlines; treat the table
> here as the current figure. The delegate compiler's "92% pattern coverage" and the "7x speedup" in
> `TypedElementExtensions` XML docs were likewise design-phase estimates and have been removed from the
> shipped API documentation rather than restated.

**Key Optimizations**:
1. Eliminates Closure allocations (struct-based context)
2. Zero Dictionary lookups (direct field access)
3. Cached delegates with lazy initialization
4. Instruction cache locality (7.5x smaller code)
5. Pattern-based compilation for common cases (92% of queries)

## Key Components

### Core Architecture

- **FHIRPath Parser** (`FhirPathParser`) - Parses FHIRPath expressions into immutable AST with optional compile-time optimization
- **Expression Evaluator** (`FhirPathEvaluator`) - Visitor-based evaluator that executes FHIRPath expressions against FHIR resources
- **Static Analyzer** (`FhirPathAnalyzer`) - Type inference and validation visitor for compile-time error detection
- **Function Library** - 60+ FHIRPath functions with automatic registration via source generators
- **Symbol Table** (`SymbolTable`) - Function signature registry for static validation and type inference

### Architecture Highlights

#### Visitor Pattern Design

The FHIRPath engine uses the visitor pattern to cleanly separate AST structure from operations:

```csharp
// Expression base class
public abstract class Expression {
    public abstract TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context);
}

// Evaluator implements visitor
public class FhirPathEvaluator : IFhirPathExpressionVisitor<EvaluationContext, IEnumerable<IElement>> {
    public IEnumerable<IElement> VisitBinary(BinaryExpression expr, EvaluationContext context) { ... }
    public IEnumerable<IElement> VisitFunctionCall(FunctionCallExpression expr, EvaluationContext context) { ... }
    // ... 11 more visitor methods
}
```

**Benefits:**
- **Extensibility**: New visitors (optimizer, debugger, SQL translator) added without modifying AST
- **Type Safety**: Compiler enforces handling of all expression types via double dispatch
- **Separation of Concerns**: AST structure decoupled from evaluation/analysis logic
- **Consistency**: Matches `Ignixa.Search.Expressions` visitor pattern used throughout the codebase

#### Immutable Evaluation Context

Pure functional evaluation using immutable context passing:

```csharp
public sealed record EvaluationContext {
    public ImmutableStack<IEnumerable<IElement>> FocusStack { get; init; }
    public ImmutableDictionary<string, IEnumerable<IElement>> Variables { get; init; }

    public EvaluationContext WithFocus(IEnumerable<IElement> focus) =>
        this with { FocusStack = FocusStack.Push(focus) };
}
```

**Benefits:**
- **No Side Effects**: Context mutations return new instances, enabling safe parallel traversal
- **Simplified Reasoning**: No mutable state to track across visitor method calls
- **ReferenceEquals Optimization**: Skip context allocation when focus unchanged (10% faster for simple operations)

#### Compile-Time Optimization

Parser includes optional AST optimization pass:

```csharp
var options = new CompilationOptions { Optimize = true };
var expr = parser.Parse("1 + 1", options);  // Optimized to: ConstantExpression(2)
```

**Optimizations Applied:**
- **Constant Folding**: `1 + 1` → `2`, `'hello' + 'world'` → `'helloworld'`
- **Short-Circuit Evaluation**: `false and X` → `false` (X not evaluated)
- **Algebraic Simplification**: `X or true` → `true`, `X and false` → `false`
- **Identity Operations**: `X + 0` → `X`, `X * 1` → `X`

#### Source Generator Function Registration

Functions automatically registered via `[FhirPathFunction]` attributes:

```csharp
[FhirPathFunction("where",
    SupportedContexts = "any",
    ReturnType = "context",
    SupportsCollections = true,
    MinArguments = 1,
    MaxArguments = 1)]
public static IEnumerable<IElement> Where(
    IEnumerable<IElement> focus,
    Expression criteria,
    EvaluationContext context) { ... }
```

**Benefits:**
- **Single Source of Truth**: Metadata co-located with implementation
- **Compile-Time Validation**: Source generator validates signatures and metadata
- **Zero Runtime Cost**: Registration code generated at compile time
- **Maintainability**: No manual `SymbolTable` updates when adding functions

### Key Features

- **Visitor Pattern Architecture** - Clean separation between AST structure and operations (evaluation, analysis, optimization)
- **Immutable Evaluation** - Pure functional evaluation with immutable context passing for correctness and performance
- **Type Inference** - Static analyzer validates expressions and infers result types before execution
- **Compile-Time Optimization** - AST simplification at parse time (constant folding, short-circuiting, algebraic simplification)
- **PropertyAccessExpression** - Explicit AST node for property access, eliminating ambiguity with function calls
- **Extensibility** - Easy addition of custom functions via attributes and source generators
- **High Performance** - Significant improvements over switch-based evaluator with reduced memory allocation

### Performance Characteristics

The visitor pattern implementation delivers significant performance improvements over the previous switch-based evaluator:

**General Performance:**
- Property navigation and chaining operations are substantially faster
- Function evaluations (where, select, first, etc.) show improved performance
- Binary operations (and, or, comparisons) execute more efficiently
- Memory allocation reduced significantly across all operation types

**Optimization Techniques:**

1. **ReferenceEquals Context Optimization**: Skips unnecessary immutable context allocation when focus hasn't changed between visitor method calls

2. **Constant Indexer Fast Path**: Array indexing with constant indexes (e.g., `name[0]`) uses optimized code path that avoids creating intermediate `IElement` wrappers and context allocations

3. **Expression Caching**: Compiled FHIRPath expressions are cached to avoid re-parsing and re-optimization on repeated evaluations (7x speedup for cached expressions)

**Trade-offs:**
- Small overhead for very fast operations (sub-300ns range) due to virtual dispatch
- Overall improvement in typical FHIRPath expressions outweighs micro-benchmark variations
- Better memory locality and reduced allocations benefit real-world workloads

## Related Features

- [Search](../search/readme.md)
- [Validation](../validation/readme.md)
- [FHIR Operations](../fhir-operations/readme.md)
