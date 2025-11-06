# Ignixa.FhirMappingLanguage - Implementation Gaps & Future Work

This document identifies gaps in the current implementation compared to the full FHIR Mapping Language specification and outlines future work needed for complete compliance.

## Current Implementation Status

### ✅ Fully Implemented

1. **Lexer/Tokenizer**
   - All keywords (map, uses, as, alias, imports, group, extends, etc.)
   - All operators (→, ::, =, ., etc.)
   - String, integer, decimal, boolean literals
   - URL recognition
   - Comments (line and block)
   - Delimited identifiers (backtick and double-quote)
   - Trivia mode for round-tripping

2. **Parser/Grammar**
   - Map declarations with URL and identifier
   - Uses declarations with all modes (source, target, queried, produced)
   - Imports declarations
   - Group definitions with parameters and extends
   - Rule declarations with sources and targets
   - Source expressions with variables, types, conditions
   - Target expressions with variables, transforms, list modes
   - Transform function calls with arguments
   - Qualified identifiers (dot notation)
   - Nested dependent rules (then blocks)
   - Named rules

3. **Expression Tree (AST)**
   - Complete expression hierarchy (15+ types)
   - Source position tracking
   - ToString() for debugging

4. **Basic Evaluator**
   - Group execution
   - Rule execution
   - Source/target binding
   - Variable management
   - Transform function hooks
   - FHIRPath integration hooks
   - Nested rule execution

## 🔶 Partially Implemented / Needs Enhancement

### 1. Transform Functions

**Status**: Framework exists but no built-in functions implemented

**Missing Transform Functions** (from FHIR spec):
- ✅ `create(type)` - Framework exists
- ❌ `copy(source)` - Not implemented
- ❌ `truncate(source, length)` - Not implemented
- ❌ `escape(source, format)` - Not implemented
- ❌ `cast(source, type)` - Not implemented
- ❌ `append(source)` - Not implemented
- ❌ `translate(source, map_uri, output)` - Not implemented
- ❌ `reference(source)` - Not implemented
- ❌ `evaluate(source, path)` - Not implemented
- ❌ `cc(system, code)` - Create CodeableConcept
- ❌ `c(system, code)` - Create Coding
- ❌ `qty(value, unit)` - Create Quantity
- ❌ `id(value)` - Create Identifier
- ❌ `cp(system, value)` - Create ContactPoint
- ❌ `uuid()` - Generate UUID
- ❌ `pointer(source)` - JSON Pointer
- ❌ `dateOp(value, operation)` - Date operations

**Required Work**:
- Implement all standard transform functions
- Add unit tests for each transform
- Document transform function parameters and behavior

### 2. FHIRPath Integration

**Status**: Hook exists but no actual integration

**Missing**:
- Actual FHIRPath parser/evaluator integration
- FHIRPath expression parsing from parenthesized strings
- Support for FHIRPath in where, check, log, and default value contexts

**Required Work**:
- Integrate with `Ignixa.FhirPath` library
- Parse embedded FHIRPath expressions
- Handle FHIRPath evaluation context correctly
- Add integration tests with real FHIRPath expressions

### 3. Type System

**Status**: Basic type annotations parsed but not enforced

**Missing**:
- Type validation during parsing
- Type checking during evaluation
- Type inheritance/polymorphism (FHIR type hierarchy)
- Type coercion rules
- Primitive type handling (string, integer, decimal, boolean, etc.)
- Complex type handling (HumanName, Address, etc.)

**Required Work**:
- Implement type resolution system
- Add type validation
- Support FHIR type hierarchy
- Handle polymorphic elements

### 4. Conceptual Model Resolution

**Status**: Framework exists but not fully utilized

**Missing**:
- Structure definition loading
- Element definition lookup
- Cardinality enforcement
- Type profile resolution
- Slicing support

**Required Work**:
- Integrate with FHIR structure definitions
- Load and cache structure definitions
- Validate against structure definitions
- Support slicing and discriminators

### 5. ConceptMap Integration

**Status**: Not implemented

**Missing**:
- ConceptMap resource loading
- ConceptMap URL resolution
- Terminology translation using ConceptMaps
- Fallback handling for unmapped codes

**Required Work**:
- Implement ConceptMap loader
- Add ConceptMap translation in transform functions
- Support concept map groups and elements
- Handle unmapped codes gracefully

### 6. Debugging and Tracing

**Status**: Basic structure exists but minimal functionality

**Missing**:
- `log()` statement execution
- `check()` condition validation with error messages
- Execution tracing for debugging
- Stack trace on errors
- Variable inspection during execution

**Required Work**:
- Implement logging infrastructure
- Add check condition enforcement
- Create debugging API
- Add execution tracing mode
- Provide detailed error messages with context

## ❌ Not Implemented

### 1. Advanced FHIRPath Features

- FHIRPath function extensions specific to mapping
- Custom FHIRPath functions defined in mappings
- FHIRPath type checking in mapping context

### 2. Group Inheritance and Overriding

**Status**: `extends` keyword parsed but not evaluated

**Missing**:
- Group extension/inheritance
- Rule overriding in derived groups
- Parameter inheritance
- Abstract groups

**Required Work**:
- Implement group inheritance resolution
- Support rule overriding
- Handle abstract groups
- Add tests for inheritance scenarios

### 3. List Mode Semantics

**Status**: List modes parsed but not enforced

**List Modes** (from spec):
- `first` - Use only first element
- `not_first` - Skip first element
- `last` - Use only last element
- `not_last` - Skip last element
- `only_one` - Error if more than one element
- `share` - Share target between rules
- `single` - Create single target

**Required Work**:
- Implement list mode enforcement
- Add tests for each list mode
- Handle errors for `only_one`
- Support target sharing for `share`

### 4. Default Values

**Status**: `default` keyword recognized but not implemented

**Missing**:
- Default value assignment
- Default value expressions
- Conditional defaults

**Required Work**:
- Implement default value evaluation
- Support default expressions
- Add conditional default logic

### 5. Performance Optimizations

**Missing**:
- Expression compilation/caching
- Lazy evaluation
- Parallel rule execution (where safe)
- Memory optimization for large datasets
- Streaming transformations

**Required Work**:
- Profile execution performance
- Identify bottlenecks
- Implement compilation/caching
- Add benchmarks

### 6. Error Handling and Recovery

**Missing**:
- Graceful error recovery
- Partial transformation results
- Error collection and reporting
- Validation mode (check without executing)

**Required Work**:
- Improve error messages
- Add error collection
- Support validation-only mode
- Provide detailed error context

### 7. Resource Creation and Management

**Missing**:
- Actual FHIR resource creation
- Resource ID management
- Reference resolution
- Contained resource handling

**Required Work**:
- Integrate with FHIR resource models
- Implement resource factory
- Handle resource references
- Support contained resources

### 8. Import Resolution

**Status**: Imports parsed but not resolved

**Missing**:
- Import URL resolution
- Imported map loading
- Imported group invocation
- Circular import detection

**Required Work**:
- Implement import resolver
- Add map registry/cache
- Support group calls across maps
- Detect circular imports

### 9. Queried and Produced Modes

**Status**: Modes recognized but not implemented

**Missing**:
- Queried resource lookup
- Produced resource tracking
- Side-effect management

**Required Work**:
- Implement resource query mechanism
- Track produced resources
- Handle side effects correctly

### 10. Advanced Source Patterns

**Missing**:
- List variable binding (e.g., `src.name[0]`)
- Where clause evaluation with FHIRPath
- Check clause enforcement
- Log clause execution
- Cardinality-based filtering

**Required Work**:
- Implement list indexing
- Integrate FHIRPath for conditions
- Add check enforcement
- Implement logging
- Support cardinality patterns

## Testing Gaps

### ✅ Implemented Tests

1. **Lexer/Tokenizer Tests** (MappingTokenizerTests.cs)
   - All keywords
   - All operators
   - All literal types
   - Comments and whitespace
   - Error cases

2. **Parser Tests** (MappingGrammarTests.cs)
   - Map declarations
   - Uses and imports
   - Groups with parameters
   - Rules with sources and targets
   - Transforms
   - Nested rules
   - Error cases

3. **Evaluator Tests** (MappingEvaluatorTests.cs)
   - Basic execution
   - Variable binding
   - Transform hooks
   - FHIRPath hooks
   - Navigation

4. **Integration Tests** (RealWorldMappingTests.cs)
   - Tutorial examples
   - Cross-version mappings
   - Complex nested rules
   - Multiple sources/targets

### ❌ Missing Tests

1. **Transform Function Tests**
   - Individual tests for each built-in transform
   - Transform error handling
   - Transform argument validation

2. **Type System Tests**
   - Type validation
   - Type coercion
   - Type hierarchy

3. **FHIRPath Integration Tests**
   - Embedded FHIRPath expressions
   - FHIRPath in conditions
   - FHIRPath in transforms

4. **List Mode Tests**
   - Each list mode behavior
   - List mode error cases

5. **Performance Tests**
   - Large mapping execution
   - Memory usage
   - Benchmark suite

6. **Error Handling Tests**
   - Graceful degradation
   - Error collection
   - Detailed error messages

## Specification Compliance

### Compliant Areas

- ✅ Basic syntax and grammar
- ✅ Map structure
- ✅ Group definitions
- ✅ Rule syntax
- ✅ Source/target patterns
- ✅ Transform syntax

### Non-Compliant Areas

- ❌ Transform function implementations (0 of ~18)
- ❌ FHIRPath integration (hook exists, not wired up)
- ❌ Type system enforcement (parsed but not validated)
- ❌ List mode semantics (parsed but not enforced)
- ❌ Group inheritance (parsed but not evaluated)
- ❌ Import resolution (parsed but not loaded)
- ❌ ConceptMap integration (not started)
- ❌ Where/check/log execution (parsed but not evaluated)

## Priority Roadmap

### Phase 1: Core Functionality (High Priority)

1. Implement standard transform functions
2. Integrate FHIRPath evaluation
3. Add basic type checking
4. Implement group inheritance
5. Add comprehensive error messages

### Phase 2: Advanced Features (Medium Priority)

1. Implement list mode semantics
2. Add import resolution
3. Implement ConceptMap integration
4. Add debugging and tracing
5. Support where/check/log execution

### Phase 3: Production Readiness (Medium Priority)

1. Performance optimization
2. Memory optimization
3. Add benchmarks
4. Improve error handling
5. Add validation-only mode

### Phase 4: Advanced Scenarios (Low Priority)

1. Streaming transformations
2. Parallel execution
3. Custom FHIRPath functions
4. Advanced type system features
5. Resource management optimizations

## Contributing

When implementing missing features:

1. Follow the existing architecture (Lexer → Parser → AST → Evaluator)
2. Add unit tests for new functionality
3. Update this document to reflect completed work
4. Add integration tests for complex scenarios
5. Document any deviations from the FHIR spec

## References

- [FHIR Mapping Language Specification](https://hl7.org/fhir/mapping-language.html)
- [FHIR Mapping Tutorial](https://hl7.org/fhir/mapping-tutorial.html)
- [FHIR Cross-Version Mapping Pack](https://build.fhir.org/ig/HL7/fhir-cross-version/)
- [StructureMap Resource](https://hl7.org/fhir/structuremap.html)

## Version History

- **v0.1.0** (2025-01-XX): Initial implementation with parser and basic evaluator
  - ✅ Complete lexer and parser
  - ✅ Expression tree
  - ✅ Basic evaluator framework
  - ✅ Comprehensive unit tests
  - ❌ Transform functions (0/18)
  - ❌ FHIRPath integration (not wired)
  - ❌ Type system (not enforced)
