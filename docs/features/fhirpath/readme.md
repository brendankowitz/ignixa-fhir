# Feature: FHIRPath

FHIRPath expression evaluation engine for FHIR resource querying and data extraction.

## Status

In Progress

## Overview

This feature provides FHIRPath expression evaluation capabilities used throughout the FHIR server for search parameters, invariants, validation, and data extraction.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [Performance Optimization](investigations/performance-optimization.md) | Complete | 2025-10-16 | Performance analysis and optimization strategies for FHIRPath evaluation |
| [Gap Analysis](investigations/gap-analysis.md) | Complete | 2025-11-18 | Analysis of FHIRPath implementation gaps and missing functionality |
| [Visitor Pattern Evaluation](investigations/visitor-pattern-evaluation.md) | Complete | 2026-01-09 | Comparison of switch-based vs visitor pattern for FhirPath AST traversal |
| [Performance vs Firely SDK](investigations/fhirpath-performance-analysis.md) | Complete | 2026-01-11 | Deep analysis proving 3,220x speedup over Firely through compiled delegates |
| [Official Test Suite Integration](investigations/official-test-suite-integration.md) | Complete | 2026-01-12 | Leveraging HL7's official FHIRPath test cases (2,328 tests, 76.9% pass rate) for specification compliance validation |

### Performance Comparison: Ignixa vs Firely

A comprehensive investigation (Jan 2026) analyzed why Ignixa's FHIRPath implementation is dramatically faster than the Firely .NET SDK. Key findings:

**Benchmark Results** (Expression: `name.family`):
- **Mean Time**: 85.02 ns (Ignixa) vs 273,682 ns (Firely) = **3,220x faster**
- **Allocations**: 728 B vs 97.18 KB = **136x less memory**
- **Code Size**: 7,457 B vs 56,356 B native code = **7.5x smaller**

**Architecture Differences**:
- **Ignixa**: Pattern-based `Func<>` delegate compilation (92% coverage) with dual-level caching (AST + delegates)
- **Firely**: Universal interpreter using `Invokee` delegate chains with Dictionary-based Closure context

**Evidence** (with IL and assembly code proof):
- [Performance Analysis](investigations/fhirpath-performance-analysis.md) - Architectural comparison
- [IL Code Analysis](investigations/il-analysis.md) - IL disassembly proving compiled delegates
- [Assembly Comparison](investigations/assembly-code-comparison.md) - Native x86-64 code analysis
- [Full Disassembly](investigations/assembly-disassembly.md) - BenchmarkDotNet output (590 KB)

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
