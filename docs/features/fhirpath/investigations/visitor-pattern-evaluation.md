# Investigation: Visitor Pattern Evaluation

**Feature**: fhirpath
**Status**: Completed
**Created**: 2026-01-09
**Completed**: 2026-01-09

## Implementation Status

| Phase | Status | Files | Tests | Notes |
|-------|--------|-------|-------|-------|
| **Phase 1: Core Visitor Pattern** | ✅ Complete | 13 files | All passing | AcceptVisitor infrastructure, visitor interface, base class |
| **Phase 2A: Type Infrastructure** | ✅ Complete | 3 files | All passing | FhirPathType, FhirPathTypeSet, ValidationIssue |
| **Phase 2B: Context Management** | ✅ Complete | 2 files | All passing | FhirPathVisitorContext, SymbolTable |
| **Phase 3: AST Normalization** | ✅ Complete | 8 files | All passing | PropertyAccessExpression, parser updates |
| **Phase 4A: Function Registry** | ✅ Complete | 2 files | All passing | SymbolTable with source generator support |
| **Phase 4B: Testing Infrastructure** | ✅ Complete | 2 files | 500+ passing | SymbolTableTests, VisitorPatternTests |
| **Phase 4C: Documentation** | ✅ Complete | 3 files | N/A | Quick start guide, updated investigation docs |

### Summary

All phases completed successfully. The visitor pattern refactoring is production-ready:

- **13 expression types** with AcceptVisitor implementations
- **FhirPathType** and **FhirPathTypeSet** for type tracking
- **SymbolTable** with function signature registry
- **PropertyAccessExpression** for semantic clarity
- **500+ tests** passing (existing + new visitor tests)
- **Comprehensive documentation** for future developers

## Approach

Replace the current switch-statement based evaluation in `FhirPathEvaluator.cs` with a true visitor pattern similar to what's used in the Search expression tree (`Ignixa.Search.Expressions`).

### What Would We Build?

1. **Add visitor infrastructure to FhirPath Expression base class**:
   - Add `AcceptVisitor<TContext, TOutput>` abstract method to `Ignixa.FhirPath.Expressions.Expression`
   - Each expression type (BinaryExpression, FunctionCallExpression, ChildExpression, etc.) implements `AcceptVisitor` to call the appropriate visitor method

2. **Create FhirPath visitor interface**:
   - `IFhirPathExpressionVisitor<TContext, TOutput>` with methods for each expression type:
     - `VisitBinary(BinaryExpression, TContext)`
     - `VisitUnary(UnaryExpression, TContext)`
     - `VisitFunctionCall(FunctionCallExpression, TContext)`
     - `VisitChild(ChildExpression, TContext)`
     - `VisitConstant(ConstantExpression, TContext)`
     - `VisitIdentifier(IdentifierExpression, TContext)`
     - `VisitVariable(VariableRefExpression, TContext)`
     - `VisitAxis(AxisExpression, TContext)`
     - `VisitIndexer(IndexerExpression, TContext)`
     - `VisitParenthesized(ParenthesizedExpression, TContext)`
     - `VisitQuantity(QuantityExpression, TContext)`
     - `VisitEmpty(EmptyExpression, TContext)`

3. **Refactor FhirPathEvaluator to implement visitor**:
   - `FhirPathEvaluator` implements `IFhirPathExpressionVisitor<EvaluationContext, IEnumerable<IElement>>`
   - Replace `EvaluateExpression(focus, expr, context)` switch statement with `expr.AcceptVisitor(this, context)`
   - Each `EvaluateXxx` method becomes a `VisitXxx` method

4. **Optional: Create base visitor for reuse**:
   - `DefaultFhirPathExpressionVisitor<TContext, TOutput>` provides default traversal logic
   - Useful for analysis/transformation passes (optimization, AST rewriting, etc.)

### How Would It Work?

**Current (Switch-based)**:
```csharp
private IEnumerable<IElement> EvaluateExpression(IEnumerable<IElement> focus, Expression expr, EvaluationContext context)
{
    return expr switch
    {
        ChildExpression child => EvaluateChildExpression(focus, child, context),
        BinaryExpression binary => EvaluateBinaryExpression(focus, binary, context),
        UnaryExpression unary => EvaluateUnary(focus, unary, context),
        // ... 8 more cases
        _ => throw new NotSupportedException($"Expression type {expr.GetType().Name} is not yet supported")
    };
}
```

**With Visitor Pattern**:
```csharp
// In Expression.cs
public abstract TOutput AcceptVisitor<TContext, TOutput>(
    IFhirPathExpressionVisitor<TContext, TOutput> visitor,
    TContext context);

// In BinaryExpression.cs
public override TOutput AcceptVisitor<TContext, TOutput>(
    IFhirPathExpressionVisitor<TContext, TOutput> visitor,
    TContext context)
{
    return visitor.VisitBinary(this, context);
}

// In FhirPathEvaluator.cs
private IEnumerable<IElement> EvaluateExpression(IEnumerable<IElement> focus, Expression expr, EvaluationContext context)
{
    return expr.AcceptVisitor(this, context);
}

public IEnumerable<IElement> VisitBinary(BinaryExpression binary, EvaluationContext context)
{
    // Current EvaluateBinaryExpression logic
}
```

## Tradeoffs

| Pros | Cons |
|------|------|
| **Extensibility**: Add new visitors (optimizer, type checker, debugger) without modifying expression classes (Open-Closed Principle) | **Boilerplate**: 12 `AcceptVisitor` implementations (mechanical, one-time cost) |
| **Type hierarchy safety**: Double dispatch eliminates case ordering fragility (compiler enforces correctness) | **Migration effort**: ~16 files touched, 2-3 hours (pure refactoring, tests provide safety net) |
| **Consistency**: Matches `Ignixa.Search.Expressions` visitor pattern (already proven in production) | **Virtual dispatch overhead**: +1 virtual call vs pattern match (~1-2ns, negligible) |
| **Code reuse**: `DefaultFhirPathExpressionVisitor` provides shared traversal logic for all visitors | **Learning curve**: Visitor pattern less familiar than switch (but already used in Search expressions) |
| **Separation of concerns**: Expression classes stay structural, logic lives in visitors | |
| **Testability**: Mock visitor interface to test traversal independently from evaluation logic | |

## Alignment

- [x] **Follows architectural layering rules**: Visitor interface lives in Core layer (Ignixa.FhirPath), evaluator implements it
- [x] **Developer Experience**: Existing codebase already uses this pattern in Search expressions, so developers know it
- [x] **Specification compliance**: No impact on FHIRPath spec compliance (internal refactoring)
- [x] **Consistent with existing patterns**: `Ignixa.Search.Expressions` already uses visitor pattern with `IExpressionVisitor<TContext, TOutput>` and `AcceptVisitor` (357:C:\src\ignixa-fhir\src\Core\Ignixa.Search\Expressions\Expression.cs)

## Evidence

### 1. Search Expression Tree Already Uses Visitor Pattern

`Ignixa.Search.Expressions.Expression` (356:C:\src\ignixa-fhir\src\Core\Ignixa.Search\Expressions\Expression.cs):
```csharp
public abstract TOutput AcceptVisitor<TContext, TOutput>(
    IExpressionVisitor<TContext, TOutput> visitor,
    TContext context);
```

**Visitor Interface** (`IExpressionVisitor.cs`):
- 13 distinct `VisitXxx` methods for different expression types
- Used by `SearchQueryInterpreter`, `ExpressionRewriter`, `ComparisonValueVisitor`

**Default Implementation** (`DefaultExpressionVisitor.cs`):
- Provides base traversal logic with customizable aggregation
- Subclasses override only relevant methods (reduces boilerplate)

### 2. Current FhirPath Switch Statement Issues

`FhirPathEvaluator.EvaluateExpression` (33:C:\src\ignixa-fhir\src\Core\Ignixa.FhirPath\Evaluation\FhirPathEvaluator.cs):
- 12-case switch expression (lines 35-51)
- Must check specific types before base types (comment on line 37: "ChildExpression/BinaryExpression/UnaryExpression/IndexerExpression inherit from FunctionCallExpression")
- **Type hierarchy fragility**: Adding new expression types requires understanding inheritance relationships to place cases correctly

**Type Hierarchy (from prototype findings)**:
```
Expression (abstract)
├── AxisExpression
├── BinaryExpression (extends FunctionCallExpression!)
├── ChildExpression (extends FunctionCallExpression!)
├── ConstantExpression
├── EmptyExpression
├── FunctionCallExpression
├── IdentifierExpression
├── IndexerExpression (extends FunctionCallExpression!)
├── ParenthesizedExpression
├── QuantityExpression
├── UnaryExpression (extends FunctionCallExpression!)
└── VariableRefExpression
```

Critical: 4 expression types inherit from `FunctionCallExpression`, requiring careful case ordering in switch statements. Visitor pattern eliminates this fragility through double dispatch.

`FhirPathEvaluator.EvaluateFunctionCall` (81:C:\src\ignixa-fhir\src\Core\Ignixa.FhirPath\Evaluation\FhirPathEvaluator.cs):
- 66-case switch for function names (lines 81-193)
- This would remain unchanged (different concern: dispatch by function name string, not expression type)

### 3. Performance Comparison (Prior Art)

**Switch Expression (Current)**:
- Single virtual dispatch on `expr.GetType()` via switch pattern matching
- ~O(1) with jump table optimization (12 cases)
- No heap allocations for dispatch

**Visitor Pattern**:
- Two virtual calls: `expr.AcceptVisitor(this, context)` → `visitor.VisitXxx(expr, context)`
- ~O(1) virtual method lookup (method table)
- No heap allocations for dispatch

**Real-world impact**: Negligible. Virtual method dispatch is ~1-2ns on modern hardware. FhirPath evaluation bottleneck is:
1. FhirPath function logic (string manipulation, comparisons)
2. IElement tree traversal (Children() enumeration)
3. Collection allocations (LINQ operations)

Evidence from `docs/features/fhirpath/investigations/performance-optimization.md`:
- 7x speedup from **caching compiled FhirPath expressions** (AST + compiled delegates)
- Visitor pattern dispatch cost is noise compared to parsing and function execution

### 4. Prototype Validator Experience (Static Analysis Use Case)

**Source:** `C:\src\Hl7.Fhir.FhirPath.Validator\src-ignixa\PROTOTYPE-FINDINGS.md` (2026-01-09)

A prototype FhirPath static validator was built using Ignixa's FhirPath AST without a visitor pattern. Key findings:

**Challenges with switch-based traversal**:
1. **AST ambiguity detection**: Simple identifiers like `name` parse as `FunctionCallExpression` with `AxisExpression` focus:
   ```csharp
   // Distinguish property access from function calls
   if (func.Focus is AxisExpression axis && axis.AxisName == "that" && func.Arguments.Count == 0) {
       // Property access, not a function call
   }
   ```
   - Visitor pattern wouldn't eliminate this (semantic issue, not structural)
   - But would centralize detection in one `VisitFunctionCall` method

2. **Type hierarchy ordering**: Prototype notes "Pattern matching must check derived types BEFORE base types" (line 131)
   - Same fragility as evaluator switch statement
   - Visitor pattern eliminates this via double dispatch

3. **Manual traversal logic**: Validator implements recursive AST walking with custom logic
   - With visitor pattern + `DefaultFhirPathExpressionVisitor`, most traversal logic would be inherited
   - Custom validators override only relevant `VisitXxx` methods

**Verdict from prototype author**: "AST traversal for static analysis is feasible" (line 196)
- 38 tests pass across R4, R4B, R5
- Average validation time < 5ms
- Implementation complexity acceptable

**Implication**: A second visitor (static validator) now exists beyond the evaluator. This strengthens the case for visitor pattern refactoring.

### 5. Alternative Approaches (Not Investigated)

If this is the first investigation for FhirPath evaluation architecture, consider these alternatives:

1. **Interpreter with Bytecode**: Compile FhirPath AST to bytecode, evaluate with stack-based VM (like Python/JVM)
   - Pros: Fastest evaluation, no virtual dispatch, better optimization opportunities
   - Cons: High complexity, debug harder, significant implementation effort

2. **Expression Compilation to Delegates**: Emit IL/use Expression<T> to compile to .NET methods
   - Pros: Native performance, leverages JIT optimizations
   - Cons: Complex for dynamic operations (element tree traversal), compilation overhead

3. **Polished Current Approach**: Keep switch-based but improve with source generators
   - Pros: Zero runtime cost, compiler-verified exhaustiveness
   - Cons: Doesn't solve extensibility (can't add new visitors without modifying evaluator)

## Verdict

**Decision: Implement visitor pattern refactoring** ✅

This is the technically correct solution. The switch-based approach has fundamental limitations that visitor pattern solves.

**Technical justification**:

1. **Type hierarchy fragility**: 4 expression types inherit from `FunctionCallExpression`
   - Current switch requires careful case ordering (explicit warning comment in code)
   - Brittle: adding new expression types requires understanding inheritance relationships
   - Visitor pattern eliminates via double dispatch (compiler enforces correctness)

2. **Multiple visitors now required**: Evaluator + static validator + future needs
   - Validator prototype implemented manual AST traversal (duplicates dispatch logic)
   - Both hit same type hierarchy ordering issues
   - Pattern duplication violates DRY principle

3. **Consistency with existing architecture**: Search expressions already use visitor pattern
   - `IExpressionVisitor<TContext, TOutput>` proven in production
   - `DefaultExpressionVisitor` provides reusable traversal logic
   - Unified pattern across all expression trees

4. **Extensibility requirement**: Known future visitors needed:
   - **Type checker**: Static type inference for FhirPath expressions
   - **Optimizer**: Constant folding, dead code elimination
   - **Debugger/tracer**: Step-through evaluation with breakpoints
   - **SQL translator**: Compile FhirPath to SQL for search parameter extraction
   - Each requires full AST traversal with different logic

5. **No performance penalty**: Virtual dispatch cost is negligible
   - ~1-2ns per call (noise compared to FhirPath function execution)
   - Evidence: 7x speedup from expression caching dominates any dispatch overhead

**Implementation approach** (phased, incremental delivery):

**IMPORTANT**: This plan has been revised based on production validator findings. See [implementation-plan-enhancement-analysis.md](./implementation-plan-enhancement-analysis.md) for complete analysis of why the effort estimate increased from 7-10 hours to 12-17 hours.

See "Developer Experience Analysis" section below for detailed rationale and code examples. For practical implementation patterns, see [visitor-pattern-implementation-guide.md](./visitor-pattern-implementation-guide.md).

### **Phase 1: Core Visitor Pattern** (2-3 hours) - IMMEDIATE VALUE

1. **Rename `AxisExpression` → `ScopeExpression`** across codebase
   - Update parser, evaluator, tests (~12+ files)
   - Rename `AxisName` property → `ScopeName`

2. **Add `AcceptVisitor` infrastructure**:
   - `Expression.AcceptVisitor<TContext, TOutput>` abstract method
   - Implement in all 13 expression types (mechanical, 5 min each)

3. **Create visitor interface + base class**:
   - `IFhirPathExpressionVisitor<TContext, TOutput>` with 13 visit methods
   - `DefaultFhirPathExpressionVisitor<TContext, TOutput>` with default traversal

4. **Refactor evaluator**:
   - Implement `IFhirPathExpressionVisitor<EvaluationContext, IEnumerable<IElement>>`
   - Replace `EvaluateExpression` switch → `expr.AcceptVisitor(this, context)`
   - Rename `EvaluateXxx` → `VisitXxx`

5. **Refactor validator prototype**:
   - Inherit from `DefaultFhirPathExpressionVisitor`
   - Remove manual dispatch switch
   - All tests pass (38+ tests)

**Risk**: Low. Mechanical changes, type-checked by compiler, full test coverage.

**Deliverables**: Validator can be refactored immediately, eliminating 200+ lines of switch logic duplication.

---

### **Phase 2: Developer Experience Enhancements** (3-4 hours) - QUALITY OF LIFE

6. **Immutable context object**:
   - Replace mutable stacks with immutable `FhirPathVisitorContext` record
   - Simplifies visitor method signatures
   - Eliminates push/pop bugs

7. **Expression metadata helpers**:
   - `Expression.IsImplicitPropertyAccess()` - encapsulates AST ambiguity detection
   - `Expression.TryGetTypeIdentifier()` - handles ofType/as argument extraction
   - Eliminates 100+ lines of repeated logic across visitors

8. **Visitor composition utilities**:
   - `DebugVisitor<TContext, TOutput>` wrapper for logging
   - Separates concerns (traversal vs. debug output)
   - Testable, composable

**Risk**: Low. Additive changes, backwards compatible.

**Deliverables**: Developers write 50% less code for new visitors. Cleaner, more maintainable.

---

### **Phase 2A: Type System Infrastructure** (2-3 hours) - CRITICAL FOUNDATION

**NEW PHASE**: Not in original plan, but production validator showed this is critical.

6. **FhirPathType struct** (~130 LOC):
   - Represents single type node with collection tracking
   - `AsSingle()`, `AsCollection()`, `WithPath()` methods
   - Immutable value type for performance

7. **FhirPathTypeSet class** (~210 LOC):
   - Container holding multiple possible types (for union operators, choice types)
   - `CanBeOfType()`, `IsCollection()` query methods
   - Type normalization (`code` → `string`, `date` → `datetime`)

8. **Error reporting infrastructure** (~150 LOC):
   - `ValidationIssue` record with severity, location, message
   - `OperationOutcomeBuilder` with semantic helpers (AddPropertyNotFound, AddVariableNotFound)
   - Uses `ISourcePositionInfo` for IDE integration

**Risk**: Low. Pure infrastructure, well-tested in production validator.

**Deliverables**: Type system ready for Phase 2B and Phase 4. Unit tests for collection tracking.

---

### **Phase 2B: Context Management** (2-3 hours) - SEMANTIC REQUIREMENTS

9. **Variable registration pattern** (~100 LOC):
   - `Dictionary<string, FhirPathTypeSet>` for variables
   - `SetContext(string definitionPath)` with path navigation
   - Register standard variables: `%resource`, `%rootResource`, `%context`, `%ucum`, `%sct`, `%loinc`

10. **Context stacks** (~80 LOC):
    - `Stack<FhirPathTypeSet>` for property context
    - `Stack<FhirPathTypeSet>` for expression context (where/select args)
    - `Stack<FhirPathTypeSet>` for aggregate accumulator

11. **Builtin variable handling**:
    - `builtin.that` → property context stack
    - `builtin.this` → expression context stack (single item)
    - `builtin.total` → aggregate accumulator stack
    - `builtin.index` → integer
    - `builtin.children` → focus children

**Risk**: Low. Proven pattern, critical for where() clauses.

**Deliverables**: Can evaluate `Patient.name.where(use = 'official')`. Test suite for context propagation.

---

### **Phase 3: AST Normalization** (2-3 hours) - SEMANTIC CORRECTNESS

12. **Add `PropertyAccessExpression`**:
   - New expression type for `name`, `family`, etc. (currently disguised as `FunctionCallExpression`)
   - Update parser to emit `PropertyAccessExpression` instead of ambiguous `FunctionCallExpression`

13. **Eliminate builtin function magic strings**:
    - `builtin.children` → explicit AST node
    - Clearer semantics, easier to understand

14. **Update visitors**:
    - Add `VisitPropertyAccess` method
    - Remove special-case detection logic
    - Simplify function call handling

15. **Expression metadata helpers**:
    - `Expression.IsImplicitPropertyAccess()` - encapsulates AST ambiguity detection
    - `Expression.TryGetTypeIdentifier()` - handles ofType/as argument extraction

**Risk**: Medium. Parser changes require careful testing. Backwards compatible via inheritance.

**Deliverables**: AST structure matches FhirPath semantics. Future visitors need no special-case logic.

---

### **Phase 4: Production Readiness** (4-5 hours) - COMPLETENESS

**NEW PHASE**: Not in original plan, but required for production-quality implementation.

#### 4A: Function Signature Registry (3-4 hours)

16. **Attribute + Source Generator approach** (~200 LOC + generator):

    **Step 1: Define FhirPathFunctionAttribute** (30 min):
    ```csharp
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FhirPathFunctionAttribute : Attribute
    {
        public string Name { get; }
        public string SupportedContexts { get; set; } = "any";
        public string ReturnType { get; set; } = "any";
        public bool SupportsCollections { get; set; }
        public bool SupportedAtRoot { get; set; }
        public int? MinArguments { get; set; }
        public int? MaxArguments { get; set; }

        public FhirPathFunctionAttribute(string name) => Name = name;
    }
    ```

    **Step 2: Create Source Generator** (1-2 hours):
    - ISourceGenerator implementation
    - Discovers all methods with [FhirPathFunction] attribute
    - Generates SymbolTable.RegisterStandardFunctions() method
    - Validates metadata at compile-time

    **Step 3: Annotate Existing Functions** (1-2 hours):
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
        EvaluationContext context)
    {
        // Existing implementation unchanged
    }

    [FhirPathFunction("length",
        SupportedContexts = "string",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0)]
    public static IEnumerable<IElement> Length(IEnumerable<IElement> focus)
    {
        // Existing implementation unchanged
    }
    // ... 58+ more annotations
    ```

    **Generated Output** (SymbolTable.g.cs):
    ```csharp
    partial class SymbolTable
    {
        partial void RegisterStandardFunctions()
        {
            // Auto-generated from [FhirPathFunction] attributes
            Add(new FunctionDefinition("where", supportsCollections: true)
                .AddContexts("any-any")
                .AddValidation(ValidateArgumentCount(1, 1)));

            Add(new FunctionDefinition("length")
                .AddContexts("string-integer")
                .AddValidation(ValidateNoArguments));

            // ... 60+ more functions auto-generated
        }
    }
    ```

**Benefits**:
- ✅ Single source of truth (metadata + implementation co-located)
- ✅ Zero runtime cost (generated at compile-time)
- ✅ No duplication or drift
- ✅ Compile-time validation of metadata
- ✅ AOT/trimming friendly
- ✅ Reduces manual code from 900 LOC → ~200 LOC

**Risk**: Low. Source generators are first-class in .NET 9+. Production validator can be refactored to use this approach.

**Deliverables**: Function signature validation working. Type inference for all 60+ functions. Eliminates manual registration maintenance burden.

#### 4B: Testing Infrastructure (1-2 hours)

17. **Parameterized test framework**:
    - `CreateSchemaProvider(string version)` factory for R4/R4B/R5
    - `[DataRow("R4")]`, `[DataRow("R4B")]`, `[DataRow("R5")]` test decorations
    - Test suites:
      - BasicNavigationTests (20+ tests)
      - CollectionTrackingTests (15+ tests)
      - ErrorReportingTests (10+ tests)
      - FunctionValidationTests (30+ tests)

**Risk**: Low. Patterns proven in production validator.

**Deliverables**: 75+ tests passing across R4/R4B/R5. Confidence in multi-version support.

#### 4C: Documentation (1 hour)

18. **Integrate existing documentation**:
    - Review [visitor-pattern-implementation-guide.md](./visitor-pattern-implementation-guide.md) (1387 lines)
    - Review [visitor-quick-reference.md](./visitor-quick-reference.md) (448 lines)
    - Review [README-visitor-documentation.md](./README-visitor-documentation.md) (154 lines)
    - Integrate high-priority enhancements from [visitor-pattern-evaluation-enhancements.md](./visitor-pattern-evaluation-enhancements.md)
    - Add cross-references between investigation and implementation guide

**Risk**: Low. Documentation already exists, just needs review and integration.

**Deliverables**: Complete documentation suite for future developers. Implementation guide with 10 sections. Quick reference with copy-pasteable patterns.

---

### **Total Effort**: 13-18 hours across 4 phases (revised)

**Incremental delivery**: Each phase ships independently, delivers value immediately.

**Why revised estimate?** Production validator revealed missing infrastructure:
- Type system (FhirPathType, FhirPathTypeSet) - critical for collection tracking
- Function registry (SymbolTable) - source generator + 60+ function annotations
- Error reporting (ValidationIssue, OperationOutcomeBuilder) - locations for IDE integration
- Testing infrastructure - parameterized multi-version tests
- Comprehensive documentation - implementation guide + quick reference

**Phase Breakdown**:
- Phase 1 (Core Visitor): 2-3 hours
- Phase 2A (Type Infrastructure): 2-3 hours
- Phase 2B (Context Management): 2-3 hours
- Phase 3 (AST Normalization): 2-3 hours
- Phase 4A (Function Registry): 3-4 hours (**+1h for source generator**, but saves 700 LOC of manual code)
- Phase 4B (Testing): 1-2 hours
- Phase 4C (Documentation): 1 hour

**ROI**: Additional 6-8 hours pays back after 3-4 visitors (validator, optimizer, type checker, debugger already planned). Future developers save 2-4 hours per visitor. Source generator approach eliminates maintenance burden of 900 LOC manual registration.

**See**: [implementation-plan-enhancement-analysis.md](./implementation-plan-enhancement-analysis.md) for detailed breakdown.

---

## Developer Experience Analysis (From Validator Prototype)

### Current Pain Points for Visitor Authors

Analysis of `C:\src\Hl7.Fhir.FhirPath.Validator\src-ignixa\Ignixa.FhirPath.Validator\BaseFhirPathExpressionVisitor.cs` reveals significant developer friction:

#### 1. **Manual Dispatch Switch Statement** (lines 188-206)
Every visitor must implement the same 12-case switch:
```csharp
public FhirPathTypeSet Visit(Expression expression)
{
    return expression switch
    {
        BinaryExpression binary => VisitBinaryExpression(binary),
        UnaryExpression unary => VisitUnaryExpression(unary),
        IndexerExpression indexer => VisitIndexerExpression(indexer),
        ChildExpression child => VisitChildExpression(child),
        FunctionCallExpression func => VisitFunctionCallExpression(func),
        // ... 7 more cases
        _ => VisitUnknownExpression(expression)
    };
}
```
**Problem**: Type hierarchy ordering fragility duplicated in every visitor implementation.

#### 2. **Context Stack Management Complexity** (lines 36-38)
Three separate stacks manually managed throughout traversal:
```csharp
private readonly Stack<FhirPathTypeSet> _propertyContextStack = new();
private readonly Stack<FhirPathTypeSet> _expressionContextStack = new();
private readonly Stack<FhirPathTypeSet> _aggregateTotalStack = new();
```
**Problem**: Push/pop logic scattered across 13 visitor methods. Easy to forget, causes bugs.

#### 3. **AST Ambiguity Detection** (lines 417-420)
Special case for when `FunctionCallExpression` is actually property access:
```csharp
if (expression.Focus is ScopeExpression scope && scope.ScopeName == "that" && expression.Arguments.Count == 0)
{
    return VisitPropertyAccessOnContext(functionName, expression);
}
```
**Problem**: Every visitor reimplements this logic. Should be handled once in base class or eliminated via better AST design.

#### 4. **Builtin Function Detection** (lines 407-414)
More special cases for disguised expressions:
```csharp
if (functionName == "builtin.children" && expression is not ChildExpression)
{
    // This is raw children access
    var builtinFocusResult = expression.Focus != null
        ? Visit(expression.Focus)
        : (_propertyContextStack.Count > 0 ? _propertyContextStack.Peek() : RootContext);
    return builtinFocusResult;
}
```
**Problem**: Magic strings, unclear semantics, should be explicit in AST.

#### 5. **Special Argument Handling** (lines 442-503)
`ofType()` and `as()` take type identifiers, not expressions - requires custom extraction:
```csharp
if ((functionName.Equals("ofType", ...) || functionName.Equals("as", ...)) && expression.Arguments.Count > 0)
{
    var typeArg = expression.Arguments[0];
    string? typeName = ExtractTypeName(typeArg);  // Complex helper method
    // ... 60 lines of special logic
}
```
**Problem**: Visitor must understand FhirPath semantics deeply, not just traverse structure.

#### 6. **Debug Output Coupling** (lines 959-982)
Debug formatting mixed into visitor logic:
```csharp
protected void Append(string text) { ... }
protected void AppendLine(string text) { ... }
protected void IncrementTab() => _indent++;
```
**Problem**: Concerns mixed (traversal + type inference + debug output). Hard to test, hard to extend.

### Proposed Developer Experience Improvements

#### Solution 1: **Built-in Visitor Pattern with AcceptVisitor**
Eliminates manual dispatch switch:
```csharp
// Developer writes:
public class MyCustomVisitor : DefaultFhirPathExpressionVisitor<MyContext, MyResult>
{
    // Override only relevant methods
    public override MyResult VisitBinary(BinaryExpression expr, MyContext context)
    {
        // Focus on logic, not dispatch
    }
}

// AST handles dispatch automatically:
var result = expression.AcceptVisitor(visitor, context);
```

**Benefits**:
- No switch statement duplication
- Type hierarchy handled by double dispatch
- Compiler enforces complete coverage

#### Solution 2: **Immutable Context Object**
Replace three stacks with single context object:
```csharp
public sealed record FhirPathVisitorContext(
    FhirPathTypeSet PropertyContext,
    FhirPathTypeSet? ExpressionContext = null,
    FhirPathTypeSet? AggregateTotal = null,
    ImmutableStack<FhirPathTypeSet>? PropertyStack = null
)
{
    public FhirPathVisitorContext PushPropertyContext(FhirPathTypeSet props) =>
        this with { PropertyStack = (PropertyStack ?? ImmutableStack<FhirPathTypeSet>.Empty).Push(props) };

    public FhirPathVisitorContext PopPropertyContext() =>
        this with { PropertyStack = PropertyStack?.Pop() };
}
```

**Benefits**:
- Immutable = no side effects, easier to reason about
- Single parameter passed through visitor methods
- Stack operations explicit in method signatures

#### Solution 3: **Normalize AST - Eliminate Ambiguity**
Make AST structure match semantics:
```csharp
// Instead of: FunctionCallExpression { FunctionName="name", Focus=ScopeExpression, Arguments=[] }
// Parse as:    PropertyAccessExpression { PropertyName="name", Focus=ScopeExpression }

public sealed class PropertyAccessExpression : Expression
{
    public Expression? Focus { get; }
    public string PropertyName { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context) => visitor.VisitPropertyAccess(this, context);
}
```

**Benefits**:
- AST structure matches FhirPath semantics
- No special-case detection logic needed
- Clearer for all visitors (evaluator, validator, optimizer)

**Migration Path**: Parser change only, backwards compatible via `ChildExpression` base type.

#### Solution 4: **Expression Metadata Helpers**
Provide built-in helpers for common patterns:
```csharp
// In Expression base class:
public abstract class Expression
{
    public abstract TOutput AcceptVisitor<TContext, TOutput>(...);

    // Helper: Is this a property access on implicit context?
    public bool IsImplicitPropertyAccess() =>
        this is FunctionCallExpression { Focus: ScopeExpression { ScopeName: "that" }, Arguments.Count: 0 };

    // Helper: Extract type identifier from ofType/as arguments
    public string? TryGetTypeIdentifier() =>
        this switch {
            ConstantExpression { Value: string typeName } => typeName,
            FunctionCallExpression { Focus: ScopeExpression, Arguments.Count: 0 } func => func.FunctionName,
            IdentifierExpression id => id.Name,
            _ => null
        };
}
```

**Benefits**:
- Encapsulates AST ambiguity handling
- Visitors use helpers instead of reimplementing detection
- Single source of truth for AST semantic patterns

#### Solution 5: **Separate Concerns via Visitor Composition**
Split visitor responsibilities:
```csharp
// Core visitor: Pure AST traversal
public abstract class FhirPathExpressionVisitor<TContext, TOutput> : IFhirPathExpressionVisitor<TContext, TOutput>
{
    // Default implementations handle traversal only
}

// Type inference visitor: Extends core with type tracking
public class TypeInferenceVisitor : FhirPathExpressionVisitor<TypeContext, FhirPathTypeSet>
{
    // Override only type-related logic
}

// Debug visitor: Wraps another visitor, adds formatting
public class DebugVisitor<TContext, TOutput> : IFhirPathExpressionVisitor<TContext, TOutput>
{
    private readonly IFhirPathExpressionVisitor<TContext, TOutput> _inner;

    public TOutput VisitBinary(BinaryExpression expr, TContext context)
    {
        _debugOutput.Append("Binary: ");
        var result = _inner.VisitBinary(expr, context);
        _debugOutput.AppendLine($" -> {result}");
        return result;
    }
}
```

**Benefits**:
- Single Responsibility Principle
- Easier to test (mock inner visitor)
- Composable (debug wrapper, logging wrapper, metrics wrapper)

### Effort Estimate (Revised)

Original estimate (2-3 hours) covered basic visitor pattern only.

**Complete developer experience improvements**:
- Basic visitor pattern: 2-3 hours
- Immutable context object: 1-2 hours
- AST normalization (PropertyAccessExpression): 2-3 hours
- Expression metadata helpers: 1 hour
- Visitor composition pattern: 1 hour
- **Total: 7-10 hours**

**Incremental approach** (can ship in stages):
1. **Phase 1** (2-3 hours): Basic visitor pattern + ScopeExpression rename
2. **Phase 2** (3-4 hours): Immutable context + Expression helpers
3. **Phase 3** (2-3 hours): AST normalization + Visitor composition

Each phase delivers value independently. Phase 1 unblocks validator refactoring immediately.

---

## Summary

**Decision: Implement 3-phase visitor pattern refactoring** ✅

The validator prototype revealed that switch-based traversal imposes significant developer friction:
- **200+ lines of duplicated dispatch logic** per visitor
- **3 mutable stacks** manually managed across 13 visitor methods
- **Complex AST ambiguity detection** reimplemented in every visitor
- **Mixed concerns** (traversal + type inference + debug output)

**Phase 1** (core visitor pattern) solves type hierarchy fragility and dispatch duplication.
**Phase 2** (immutable context + helpers) eliminates 50% of boilerplate code for visitor authors.
**Phase 3** (AST normalization) makes AST structure match FhirPath semantics, eliminating special-case logic.

**ROI**: 7-10 hours investment enables:
- **Validator refactoring** - remove 200+ lines of switch logic
- **Type checker** - static type inference for FhirPath expressions
- **Optimizer** - constant folding, dead code elimination
- **Debugger** - step-through evaluation
- **SQL translator** - compile FhirPath to SQL for search parameter extraction

All future visitors benefit from shared infrastructure. Each phase delivers value independently.
