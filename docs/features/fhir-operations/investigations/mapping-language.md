# Investigation: FHIR Mapping Language Analysis

**Feature**: fhir-operations
**Status**: Investigation
**Created**: 2025-11-18

**Date**: 2025-01-19
**Status**: Implementation Gap Analysis
**Related**: [FHIR Mapping Language Grammar](https://build.fhir.org/mapping.g4)

## Executive Summary

This document analyzes the Ignixa FHIR Mapping Language implementation against the official FHIR ANTLR4 grammar specification and real-world mapping examples. The implementation provides robust coverage of core mapping constructs but is missing several advanced features defined in the specification.

**Overall Assessment**: ✅ Core features complete, ⚠️ Advanced features missing

---

## Implementation Coverage Matrix

### ✅ Fully Implemented Features

| Feature | Spec Reference | Implementation Status |
|---------|---------------|----------------------|
| **Map declarations** | `structureMap` rule | ✅ Complete - URL and identifier |
| **Uses declarations** | `uses` rule | ✅ Complete - source/target/queried/produced modes |
| **Imports** | `imports` rule | ✅ Complete - URL-based imports |
| **Groups** | `groupDeclaration` | ✅ Complete - parameters, extends, rules |
| **Rules** | `mapRule` | ✅ Complete - sources, targets, dependencies |
| **Source expressions** | `ruleSource` | ✅ Complete - context, type, variable, cardinality |
| **Target expressions** | `ruleTarget` | ✅ Complete - context, variable, transform, list modes |
| **Where clauses** | `whereClause` | ✅ Complete - FHIRPath conditions |
| **Check clauses** | `checkClause` | ✅ Complete - validation expressions |
| **Log statements** | `log` clause | ✅ Complete - logging expressions |
| **Default values** | `defaultValue` | ✅ Complete - fallback expressions |
| **Cardinality** | `0..1`, `1..*` | ✅ Complete - min..max with unbounded (*) |
| **List modes** | `listMode` | ✅ Complete - first, last, notFirst, notLast, onlyOne, share, single |
| **Transforms** | Transform functions | ✅ Complete - function calls with arguments |
| **FHIRPath expressions** | Embedded FHIRPath | ✅ Complete - parenthesized and unparenthesized |
| **Qualified identifiers** | `src.name.given` | ✅ Complete - dot notation chaining |
| **Index expressions** | `src.name[0]` | ✅ Complete - bracket indexing |
| **Group invocations** | `then GroupName()` | ✅ Complete - dependent group calls |
| **Nested rules** | `then { rule; }` | ✅ Complete - inline rule blocks |
| **Comments** | Line and block | ✅ Complete - `//` and `/* */` |
| **String literals** | Single-quoted | ✅ Complete - with escape sequences |
| **Numeric literals** | Integer, decimal | ✅ Complete - int and decimal parsing |
| **Boolean literals** | `true`, `false` | ✅ Complete |

### ❌ Missing Features (Specification Gaps)

| Feature | Spec Reference | Priority | Impact |
|---------|---------------|----------|--------|
| **ConceptMap declarations** | `conceptMapDeclaration` | HIGH | Inline terminology mappings not supported |
| **ConceptMap prefix** | `conceptMapPrefix` | HIGH | Namespace prefixes for code systems missing |
| **Constant declarations** | `constant` rule | MEDIUM | Reusable constant values not supported |
| **Type modes on parameters** | `<<type+>>`, `<<types>>` | MEDIUM | Type casting and collection handling limited |
| **Quantities with units** | `QUANTITY` token | LOW | UCUM quantity literals not parsed |
| **Date/Time/DateTime literals** | `DATE`, `TIME`, `DATETIME` | LOW | Temporal literals treated as strings |
| **Triple-quoted strings** | `'''markdown'''` | LOW | Markdown literals not supported |
| **Delimited identifiers (full)** | Backtick identifiers | LOW | Partial support - may not work in all contexts |
| **Rule names (trailing strings)** | Rule naming | MEDIUM | Rule documentation/debugging harder |

### ⚠️ Partial/Unclear Implementation

| Feature | Status | Notes |
|---------|--------|-------|
| **FHIRPath operators as tokens** | Partial | FHIRPath captured as raw strings, not tokenized operators |
| **Alias on uses** | Complete | Implemented but rarely tested |
| **Queried/Produced modes** | Complete | Implemented but no tests found |
| **Type constraints on sources** | Complete | `src : Type` syntax works, but type validation unclear |

---

## Detailed Gap Analysis

### 1. ConceptMap Declarations (HIGH PRIORITY)

**FHIR Spec Syntax** (from ANTLR grammar):
```
conceptmap "#myConceptMap" {
  prefix s = "http://snomed.info/sct"
  prefix t = "http://loinc.org"

  s:12345 == t:67890
  s:11111 ~= t:22222
}
```

**Current Implementation**: ❌ Not supported

**Impact**:
- Users must define ConceptMaps externally and reference them
- Inline terminology mappings in mapping files not possible
- Reduces self-contained mapping definitions

**Required Changes**:
1. Add tokens: `ConceptMap` keyword (exists as `Prefix` token)
2. Add expression types: `ConceptMapExpression`, `ConceptMapPrefixExpression`, `ConceptMapCodeMapExpression`
3. Add parser rules for conceptMap block
4. Update `MapExpression` to include `ConceptMaps` collection
5. Update evaluator to register inline ConceptMaps in context

**Test Coverage Needed**:
- Parse inline ConceptMap with prefix declarations
- Parse code mappings with `==` (equivalent) and `~=` (relatedto)
- Evaluate inline ConceptMap in translate() transform
- Multiple ConceptMaps in single mapping file

---

### 2. Constant Declarations (MEDIUM PRIORITY)

**FHIR Spec Syntax** (from ANTLR grammar):
```
map "..." = "..."

constant MY_CONSTANT = 'some value'
constant MAX_LENGTH = 100

uses ...
```

**Current Implementation**: ❌ Not supported

**Impact**:
- No way to define reusable constant values
- Hard-coded values scattered throughout rules
- Maintenance burden for repeated values

**Required Changes**:
1. Add token: `Constant` keyword
2. Add expression type: `ConstantExpression`
3. Add parser rule for constant declarations
4. Update `MapExpression` to include `Constants` collection
5. Update evaluator to resolve constant references

**Test Coverage Needed**:
- Parse constant declarations (string, numeric, boolean)
- Reference constants in source/target expressions
- Constants in transform arguments
- Scoping rules for constants

---

### 3. Type Modes on Parameters (MEDIUM PRIORITY)

**FHIR Spec Syntax** (from ANTLR grammar):
```
group PatientToBundle(
  source src : Patient,
  target bundle : Bundle <<type+>>  // type mode suffix
) { ... }

group ProcessList(
  source items : Item <<types>>     // types mode
) { ... }
```

**Current Implementation**: ❌ Not supported

**Impact**:
- Type casting behavior unclear
- Collection handling limited
- Parameter semantics incomplete

**Required Changes**:
1. Add tokens: `<<`, `>>`, `type+`, `types` (or parse as special suffix)
2. Add property to `ParameterExpression`: `TypeMode` enum (None, Type, TypePlus, Types)
3. Update parameter parser to recognize type mode suffixes
4. Document semantics in evaluator

**Test Coverage Needed**:
- Parse `<<type+>>` suffix on parameters
- Parse `<<types>>` suffix
- Behavior difference between modes

---

### 4. Quantities with Units (LOW PRIORITY)

**FHIR Spec Syntax** (from ANTLR grammar):
```
src.value default 5 'mg'
tgt.duration = 30 'd'
```

**Current Implementation**: ❌ Not supported (treated as two separate tokens)

**Impact**:
- Quantity literals must be constructed via transforms
- Less natural syntax for FHIR quantities

**Required Changes**:
1. Add lexer rule for `QUANTITY` token (number + whitespace + quoted unit)
2. Add `QuantityExpression` class
3. Update literal parsers to recognize quantities
4. Create Quantity objects in evaluator

**Test Coverage Needed**:
- Parse integer quantities: `30 'd'`
- Parse decimal quantities: `5.5 'mg'`
- UCUM unit validation (optional)

---

### 5. Date/Time/DateTime Literals (LOW PRIORITY)

**FHIR Spec Syntax** (from ANTLR grammar):
```
@2024-01-15           // DATE
@T14:30:00            // TIME
@2024-01-15T14:30:00  // DATETIME
```

**Current Implementation**: ❌ Not supported (parsed as identifier + literal)

**Impact**:
- Date/time values must be string literals or constructed
- Less type-safe temporal handling

**Required Changes**:
1. Add tokens: `DateLiteral`, `TimeLiteral`, `DateTimeLiteral`
2. Add lexer rules for `@` prefix temporal formats
3. Add temporal literal expression types
4. Parse into appropriate FHIR temporal types

**Test Coverage Needed**:
- Date literals
- Time literals
- DateTime literals with timezones

---

### 6. Triple-Quoted Strings (LOW PRIORITY)

**FHIR Spec Syntax** (from ANTLR grammar):
```
'''
This is a multi-line
markdown literal
'''
```

**Current Implementation**: ❌ Not supported

**Impact**:
- Documentation and markdown content harder to embed
- Long string literals need escaping

**Required Changes**:
1. Add lexer rule for triple-quoted strings (`'''..'''`)
2. Preserve newlines and formatting
3. Return as `LiteralExpression` with string value

**Test Coverage Needed**:
- Multi-line triple-quoted strings
- Embedded quotes in triple-quoted strings

---

### 7. Rule Names with Trailing Strings (MEDIUM PRIORITY)

**FHIR Spec Syntax** (per spec and grammar):
```
src.name -> tgt.name "copy name to target"
```

**Current Implementation**: ❌ Not supported
- Comment in `MappingGrammar.cs:404` says: "Rule names (ruleName::) not supported - not part of FHIR spec"
- Comment says "FHIR spec uses trailing quoted strings for rule names instead"

**Impact**:
- Rules cannot be named/documented inline
- Debugging harder without rule identification
- Less readable mapping files

**Required Changes**:
1. Update `Rule` parser to accept optional trailing `StringLiteral`
2. Set `RuleExpression.Name` from trailing string
3. Use in debugging/error messages

**Test Coverage Needed**:
- Rules with trailing string names
- Rules without names (backward compat)
- Name extraction for error reporting

---

## Real-World Mapping Examples Analysis

Based on the test mappings from [Brian Pos's R5→R6 mappings](https://gist.github.com/brianpos/19a92f824ec0550e24751185103ad219):

### ✅ Patterns Fully Supported

```fml
// Simple property copying
group Address(source src : AddressR5, target tgt : AddressR6) extends DataType {
  src.use -> tgt.use;
  src.type -> tgt.type;
  src.line -> tgt.line;
}
```
**Status**: ✅ Works perfectly

```fml
// Nested transformations with 'then'
group Account(source src : AccountR5, target tgt : AccountR6) extends DomainResource {
  src.coverage as s -> tgt.coverage as t then AccountCoverage(s, t);
}
```
**Status**: ✅ Fully supported

```fml
// Type constraints and cardinality
group Boolean(source src : booleanR5, target tgt : booleanR6) extends PrimitiveType {
  src.value -> tgt.value;
}
```
**Status**: ✅ Works

### ⚠️ Patterns Potentially Problematic

```fml
// Type modes (<<types>>) - NOT SUPPORTED
group ComplexType(source src : Element <<types>>, target tgt) { ... }
```
**Status**: ❌ Would fail to parse

```fml
// Inline documentation with trailing strings - NOT SUPPORTED
src.name -> tgt.name "copy patient name"
```
**Status**: ❌ Trailing string would cause parse error

```fml
// Conceptual constant usage (not in examples, but spec allows) - NOT SUPPORTED
constant SYSTEM_URL = 'http://terminology.hl7.org/CodeSystem/...'
src.system = SYSTEM_URL
```
**Status**: ❌ Would fail - no constant support

---

## Missing Test Coverage

### Core Features Undertested

Based on test file analysis (`test/Ignixa.FhirMappingLanguage.Tests/`):

| Feature | Current Tests | Needed Tests |
|---------|--------------|--------------|
| **Alias on uses** | Basic | Edge cases, resolution in rules |
| **Queried mode** | ❌ None | Parsing, semantics, vs. source mode |
| **Produced mode** | ❌ None | Parsing, semantics, vs. target mode |
| **Type constraints** | Basic | Type validation, mismatch errors |
| **Cardinality violations** | ✅ Good | (Appears covered in CardinalityTests.cs) |
| **Multiple sources** | Partial | Complex multi-source rules |
| **Multiple targets** | Partial | Complex multi-target rules |
| **FHIRPath edge cases** | Basic | Nested parens, operators in unparenthesized form |
| **Delimited identifiers** | ❌ None | Backtick identifiers in all contexts |
| **Comments preservation** | ❌ None | Round-tripping with trivia mode |
| **Error positions** | ❌ None | Accurate line/column reporting |
| **Large mapping files** | ❌ None | Performance, memory usage |

### Integration Tests Needed

1. **Real-world R5→R6 mappings** (from gist)
   - Run actual R5→R6 Patient transformation
   - Verify all property mappings work
   - Test inheritance (extends DataType, extends DomainResource)

2. **Cross-import resolution**
   - Import mapping from external file
   - Resolve group references across files
   - Circular import detection

3. **Terminology integration**
   - ConceptMap resolution (external)
   - translate() transform with real ConceptMaps
   - Code system validation

4. **FHIRPath integration**
   - Complex where() conditions
   - FHIRPath functions in expressions
   - Type mismatches between FHIRPath and mapping

5. **Transform function coverage**
   - All 15+ standard transforms tested
   - Custom transform registration
   - Error handling for missing transforms

6. **Validation scenarios**
   - MappingValidator.cs coverage
   - Validation without execution
   - Circular group dependency detection

---

## Recommended Test Additions

### Priority 1: Core Feature Completeness

```csharp
// Test: Queried and Produced modes
[Fact]
public void GivenQueriedMode_WhenParsing_ThenSetsCorrectMode()
{
    var map = @"
map 'http://example.org/test' = 'Test'
uses 'http://hl7.org/fhir/StructureDefinition/Patient' as queried
group Main(source src : Patient) { }
";
    var result = compiler.Parse(map);
    result.Uses[0].Mode.Should().Be(ModelMode.Queried);
}

// Test: Multiple sources with different conditions
[Fact]
public void GivenMultipleSources_WhenEvaluating_ThenProcessesCartesianProduct()
{
    var map = @"
group Multi(source src : Patient, target tgt : Bundle) {
  src.name as n, src.telecom as t where t.use = 'home' -> tgt.entry;
}
";
    // Test that all combinations of name × telecom[home] are processed
}

// Test: Delimited identifiers with special characters
[Fact]
public void GivenDelimitedIdentifier_WhenParsing_ThenHandlesSpecialChars()
{
    var map = @"
group Main(source src, target tgt) {
  src.`property-with-dashes` -> tgt.`target-property`;
}
";
    var result = compiler.Parse(map);
    result.Groups[0].Rules[0].Sources[0].Context.Should().Be("property-with-dashes");
}

// Test: Error position reporting
[Fact]
public void GivenParseError_WhenParsing_ThenReportsCorrectLineAndColumn()
{
    var map = @"
map 'http://example.org/test' = 'Test'
group Main(source src) {
  src.name -> INVALID SYNTAX HERE
}
";
    var ex = Assert.Throws<ParseException>(() => compiler.Parse(map));
    ex.Line.Should().Be(4);
    ex.Column.Should().BeGreaterThan(0);
}
```

### Priority 2: Real-World Mapping Tests

```csharp
// Test: R5 to R6 Patient transformation (from gist)
[Fact]
public void GivenR5Patient_WhenTransformingToR6_ThenMapsAllProperties()
{
    var mapping = @"
map 'http://example.org/r5tor6' = 'PatientR5ToR6'

uses 'http://hl7.org/fhir/5.0/StructureDefinition/Patient' alias PatientR5 as source
uses 'http://hl7.org/fhir/6.0/StructureDefinition/Patient' alias PatientR6 as target

group Patient(source src : PatientR5, target tgt : PatientR6) {
  src.identifier -> tgt.identifier;
  src.active -> tgt.active;
  src.name -> tgt.name;
  src.telecom -> tgt.telecom;
  src.gender -> tgt.gender;
  src.birthDate -> tgt.birthDate;
}
";

    var compiler = new MappingCompiler();
    var map = compiler.Parse(mapping);

    var context = new MappingContext();
    var srcPatient = CreateR5Patient();
    var tgtPatient = CreateEmptyR6Patient();

    context.SetSource("src", srcPatient);
    context.SetTarget("tgt", tgtPatient);

    var evaluator = new MappingEvaluator();
    evaluator.Execute(map, context);

    // Assert all properties copied
    tgtPatient["identifier"].Should().NotBeNull();
    tgtPatient["name"].Should().NotBeNull();
}

// Test: Nested backbone element transformation
[Fact]
public void GivenNestedBackboneElement_WhenTransforming_ThenInvokesNestedGroup()
{
    var mapping = @"
map 'http://example.org/test' = 'Test'

group Account(source src : Account, target tgt : Account) {
  src.coverage as s -> tgt.coverage as t then AccountCoverage(s, t);
}

group AccountCoverage(source src, target tgt) {
  src.coverage.coverage -> tgt.coverage.coverage;
  src.coverage.priority -> tgt.coverage.priority;
}
";
    // Test nested group invocation works correctly
}
```

### Priority 3: Performance and Edge Cases

```csharp
// Test: Large mapping file parsing performance
[Fact]
public void GivenLargeMappingFile_WhenParsing_ThenCompletesInReasonableTime()
{
    var sb = new StringBuilder();
    sb.AppendLine("map 'http://example.org/large' = 'Large'");
    sb.AppendLine("group Main(source src, target tgt) {");

    // Generate 1000 rules
    for (int i = 0; i < 1000; i++)
    {
        sb.AppendLine($"  src.property{i} -> tgt.property{i};");
    }

    sb.AppendLine("}");

    var sw = Stopwatch.StartNew();
    var result = compiler.Parse(sb.ToString());
    sw.Stop();

    sw.ElapsedMilliseconds.Should().BeLessThan(1000); // <1 second
    result.Groups[0].Rules.Should().HaveCount(1000);
}

// Test: Deep nesting in qualified identifiers
[Fact]
public void GivenDeeplyNestedIdentifier_WhenParsing_ThenHandlesRecursion()
{
    var map = @"
group Main(source src, target tgt) {
  src.a.b.c.d.e.f.g.h.i.j -> tgt.x;
}
";
    var result = compiler.Parse(map);
    // Should parse without stack overflow
}
```

---

## Specification Compliance Checklist

### Parser Coverage

- [x] Map declarations with URL and identifier
- [x] Uses declarations with all modes (source/target/queried/produced)
- [x] Imports
- [x] Groups with parameters
- [x] Group inheritance (extends)
- [x] Rules with sources and targets
- [x] Source type constraints (`:Type`)
- [x] Source cardinality (`0..1`, `1..*`)
- [x] Source default values
- [x] Where clauses
- [x] Check clauses
- [x] Log statements
- [x] Target transforms
- [x] List modes (all 7 modes)
- [x] Group invocations
- [x] Nested rules
- [x] FHIRPath expressions (parenthesized)
- [x] FHIRPath expressions (unparenthesized)
- [x] Qualified identifiers with chaining
- [x] Index expressions
- [x] Comments (line and block)
- [ ] **ConceptMap declarations**
- [ ] **ConceptMap prefix declarations**
- [ ] **Constant declarations**
- [ ] **Type modes on parameters** (`<<type+>>`, `<<types>>`)
- [ ] **Quantity literals** (`5 'mg'`)
- [ ] **Date/Time/DateTime literals** (`@2024-01-15`)
- [ ] **Triple-quoted strings**
- [ ] **Rule names (trailing strings)**

### Evaluator Coverage

- [x] Basic rule execution
- [x] Source variable binding
- [x] Target variable binding
- [x] Transform function calls
- [x] Group invocation
- [x] Nested rule execution
- [x] FHIRPath evaluation integration
- [x] Cardinality validation
- [x] List mode handling
- [x] Where clause filtering
- [x] Check clause validation
- [x] Log statement execution
- [x] Default value application
- [ ] **ConceptMap resolution (inline)**
- [ ] **Constant resolution**
- [ ] **Type mode semantics**

### Standard Transforms

From `StandardTransforms.cs`:

- [x] create(type)
- [x] copy(source)
- [x] truncate(string, length)
- [x] escape(string, format)
- [x] cast(source, type)
- [x] append(source, suffix)
- [x] translate(source, map, field)
- [x] reference(source)
- [x] dateOp(value)
- [x] uuid()
- [x] pointer(object)
- [x] evaluate(context, path)
- [x] codableConcept(...)
- [x] coding(...)
- [x] quantity(...)
- [x] identifier(...)
- [x] contactPoint(...)

**Missing standard transforms** (per FHIR spec):
- [ ] cc(text, coding) - create CodeableConcept
- [ ] c(system, code) - create Coding
- [ ] qty(value, unit) - create Quantity (exists as `quantity`)
- [ ] id(system, value) - create Identifier (exists as `identifier`)
- [ ] cp(system, value) - create ContactPoint (exists as `contactPoint`)

---

## Comparison: Implementation vs. ANTLR Grammar

### Lexer Tokens

| ANTLR Token | Implemented? | Notes |
|-------------|--------------|-------|
| `KW_MAP` | ✅ `Map` | |
| `KW_USES` | ✅ `Uses` | |
| `KW_AS` | ✅ `As` | |
| `KW_ALIAS` | ✅ `Alias` | |
| `KW_IMPORTS` | ✅ `Imports` | |
| `KW_GROUP` | ✅ `Group` | |
| `KW_EXTENDS` | ✅ `Extends` | |
| `KW_DEFAULT` | ✅ `Default` | |
| `KW_WHERE` | ✅ `Where` | |
| `KW_CHECK` | ✅ `Check` | |
| `KW_LOG` | ✅ `Log` | |
| `KW_THEN` | ✅ `Then` | |
| `KW_SOURCE` | ✅ `Source` | |
| `KW_TARGET` | ✅ `Target` | |
| `KW_QUERIED` | ✅ `Queried` | |
| `KW_PRODUCED` | ✅ `Produced` | |
| `KW_CONCEPTMAP` | ⚠️ Token exists as `ConceptMap` | Parser rule missing |
| `KW_PREFIX` | ⚠️ Token exists as `Prefix` | Parser rule missing |
| `KW_TYPES` | ✅ `Types` | Type mode syntax missing |
| `KW_TYPE` | ✅ `Type` | Type mode syntax missing |
| `KW_FIRST` | ✅ `First` | |
| `KW_NOTFIRST` | ✅ `NotFirst` | |
| `KW_LAST` | ✅ `Last` | |
| `KW_NOTLAST` | ✅ `NotLast` | |
| `KW_ONLYONE` | ✅ `OnlyOne` | |
| `KW_SHARE` | ✅ `Share` | |
| `KW_SINGLE` | ✅ `Single` | |
| `KW_CONSTANT` | ❌ Missing | Not implemented |
| `QUANTITY` | ❌ Missing | Not implemented |
| `DATE` | ❌ Missing | Not implemented |
| `TIME` | ❌ Missing | Not implemented |
| `DATETIME` | ❌ Missing | Not implemented |
| `STRING` (triple-quoted) | ❌ Missing | Only single-quoted supported |

### Parser Rules

| ANTLR Rule | Implemented? | Notes |
|------------|--------------|-------|
| `structureMap` | ✅ `Map` | Complete |
| `uses` | ✅ `Uses` | Complete |
| `imports` | ✅ `Imports` | Complete |
| `conceptMapDeclaration` | ❌ Missing | High priority gap |
| `conceptMapPrefix` | ❌ Missing | High priority gap |
| `constant` | ❌ Missing | Medium priority gap |
| `groupDeclaration` | ✅ `Group` | Complete except type modes |
| `mapRule` | ✅ `Rule` | Complete except trailing string names |
| `ruleSource` | ✅ `Source` | Complete |
| `ruleTarget` | ✅ `Target` | Complete |
| `whereClause` | ✅ Embedded in Source | Complete |
| `checkClause` | ✅ Embedded in Source | Complete |
| `logClause` | ✅ Embedded in Source | Complete |
| `defaultValue` | ✅ Embedded in Source | Complete |
| `fhirPath` | ✅ `FhirPathExpression` | Captured as raw string |
| `transform` | ✅ `Transform` | Complete |
| `invocation` | ✅ `GroupInvocation` | Complete |

---

## Recommended Prioritization

### Phase 1: Critical Gaps (High Business Value)

1. **ConceptMap declarations** - Enables self-contained terminology mappings
2. **Rule names (trailing strings)** - Improves debugging and documentation
3. **Constant declarations** - Reduces maintenance burden

**Estimated Effort**: 2-3 weeks
**Business Value**: High - enables complete mapping file authoring

### Phase 2: Enhanced Semantics

1. **Type modes on parameters** - Clarifies collection handling
2. **Quantity literals** - Natural FHIR quantity syntax
3. **Date/Time literals** - Type-safe temporal values

**Estimated Effort**: 1-2 weeks
**Business Value**: Medium - improves developer experience

### Phase 3: Polish and Completeness

1. **Triple-quoted strings** - Better documentation support
2. **Full delimited identifier support** - Edge case handling
3. **Enhanced error reporting** - Precise line/column information

**Estimated Effort**: 1 week
**Business Value**: Low-Medium - quality of life improvements

---

## Test Coverage Recommendations

### Immediate Actions

1. Add tests for `queried` and `produced` modes
2. Add tests for type constraints with validation
3. Add tests for delimited identifiers in all contexts
4. Add error position reporting tests
5. Add real-world R5→R6 transformation tests

### Medium-Term

1. Performance tests for large mapping files (>1000 rules)
2. Integration tests with external ConceptMaps
3. Round-tripping tests with trivia preservation
4. Cross-import resolution tests
5. All standard transform functions tested

### Long-Term

1. Fuzzing tests for parser robustness
2. Compliance test suite against official FHIR examples
3. Benchmark suite for performance regression testing

---

## Appendix A: ANTLR Grammar Comparison

### structureMap Rule

**ANTLR Grammar**:
```antlr
structureMap
  : KW_MAP url EQUALS identifier
    conceptMapDeclaration*
    uses*
    imports*
    constant*
    groupDeclaration*
    EOF
  ;
```

**Ignixa Implementation**:
```csharp
// MappingGrammar.cs line 449
public static readonly TokenListParser<MappingTokenKind, MapExpression> Map =
    from mapToken in Token.EqualTo(MappingTokenKind.Map)
    from url in StringLiteral
    from equalsToken in Token.EqualTo(MappingTokenKind.Equals)
    from identifier in StringLiteral
    from uses in Uses.Many()
    from imports in Imports.Many()
    // Missing: conceptMapDeclaration
    // Missing: constant
    from groups in Group.Many()
    select new MapExpression(url, identifier, uses, imports, groups, ...);
```

**Gap**: Missing `conceptMapDeclaration*` and `constant*` clauses

---

### conceptMapDeclaration Rule

**ANTLR Grammar**:
```antlr
conceptMapDeclaration
  : KW_CONCEPTMAP DELIMITED_IDENTIFIER OPEN_BRACE
      conceptMapPrefix*
      conceptMapCodeMap+
    CLOSE_BRACE
  ;

conceptMapPrefix
  : KW_PREFIX identifier EQUALS STRING
  ;

conceptMapCodeMap
  : codeMapSource (EQUIVALENT | RELATEDTO | NOTRELATEDTO) codeMapTarget
  ;
```

**Ignixa Implementation**: ❌ Not implemented

**Required Implementation**:
```csharp
// Expression classes needed:
public class ConceptMapExpression : Expression
{
    public string Identifier { get; }
    public IReadOnlyList<ConceptMapPrefixExpression> Prefixes { get; }
    public IReadOnlyList<ConceptMapCodeMapExpression> CodeMaps { get; }
}

public class ConceptMapPrefixExpression : Expression
{
    public string PrefixName { get; }
    public string Url { get; }
}

public class ConceptMapCodeMapExpression : Expression
{
    public string SourcePrefix { get; }
    public string SourceCode { get; }
    public ConceptMapEquivalence Equivalence { get; }
    public string TargetPrefix { get; }
    public string TargetCode { get; }
}

public enum ConceptMapEquivalence
{
    Equivalent,      // ==
    RelatedTo,       // ~=
    NotRelatedTo     // !=
}
```

---

## Appendix B: Test Mapping Examples

### Example 1: Simple Property Mapping (✅ Supported)

```fml
map "http://example.org/fhir/StructureMap/PatientR5ToR6" = "PatientR5ToR6"

uses "http://hl7.org/fhir/5.0/StructureDefinition/Patient" alias PatientR5 as source
uses "http://hl7.org/fhir/6.0/StructureDefinition/Patient" alias PatientR6 as target

group Patient(source src : PatientR5, target tgt : PatientR6) {
  src.identifier -> tgt.identifier;
  src.active -> tgt.active;
  src.name -> tgt.name;
  src.telecom -> tgt.telecom;
}
```

**Status**: ✅ Fully supported, would parse and execute correctly

---

### Example 2: Nested Backbone Elements (✅ Supported)

```fml
group Account(source src : AccountR5, target tgt : AccountR6) extends DomainResource {
  src.coverage as s -> tgt.coverage as t then AccountCoverage(s, t);
}

group AccountCoverage(source src, target tgt) extends BackboneElement {
  src.coverage.coverage -> tgt.coverage.coverage;
  src.coverage.priority -> tgt.coverage.priority;
}
```

**Status**: ✅ Fully supported

---

### Example 3: Type Modes (❌ NOT Supported)

```fml
group ProcessTypes(
  source src : Element <<types>>,
  target tgt : Element <<type+>>
) {
  src.value -> tgt.value;
}
```

**Status**: ❌ Would fail to parse - type mode syntax not implemented

---

### Example 4: Inline ConceptMap (❌ NOT Supported)

```fml
map "http://example.org/test" = "Test"

conceptmap "#genderMap" {
  prefix fhir = "http://hl7.org/fhir/administrative-gender"
  prefix v2 = "http://terminology.hl7.org/CodeSystem/v2-0001"

  fhir:male == v2:M
  fhir:female == v2:F
  fhir:other ~= v2:O
}

uses "..." as source
uses "..." as target

group Patient(source src, target tgt) {
  src.gender as g -> tgt.gender = translate(g, '#genderMap', 'code');
}
```

**Status**: ❌ Would fail to parse - ConceptMap declaration not implemented

---

### Example 5: Constants (❌ NOT Supported)

```fml
map "http://example.org/test" = "Test"

constant DEFAULT_SYSTEM = 'http://terminology.hl7.org/CodeSystem/v3-ActCode'
constant MAX_LENGTH = 100

group Patient(source src, target tgt) {
  src.code.system = DEFAULT_SYSTEM;
  src.name.family = truncate(src.name.family, MAX_LENGTH);
}
```

**Status**: ❌ Would fail to parse - constant declarations not implemented

---

## Appendix C: Test File Inventory

Current test files in `test/Ignixa.FhirMappingLanguage.Tests/`:

| Test File | Lines of Code (est.) | Coverage |
|-----------|----------------------|----------|
| `MappingCompilerTests.cs` | ~150 | Basic parsing |
| `MappingTokenizerTests.cs` | ~200 | Lexer tokenization |
| `MappingGrammarTests.cs` | ~300 | Parser grammar |
| `MappingEvaluatorTests.cs` | ~400 | Execution |
| `CardinalityTests.cs` | ~150 | Cardinality validation |
| `ListModeTests.cs` | ~200 | List mode handling |
| `ListIndexingTests.cs` | ~100 | Index expressions |
| `DefaultValueTests.cs` | ~100 | Default values |
| `LogAndCheckTests.cs` | ~150 | Log/check clauses |
| `GroupInheritanceTests.cs` | ~150 | Group extends |
| `RealWorldMappingTests.cs` | ~300 | Integration tests |
| `FhirPathIntegrationTests.cs` | ~200 | FHIRPath embedded |
| `StandardTransformsTests.cs` | ~500 | Transform functions |
| `MappingValidatorTests.cs` | ~200 | Validation |
| `BasicTypeValidatorTests.cs` | ~100 | Type validation |
| `ConceptMapTests.cs` | ~150 | External ConceptMaps |
| `ImportResolutionTests.cs` | ~150 | Import resolution |

**Total**: ~3,400 lines of test code

---

## Summary

The Ignixa FHIR Mapping Language implementation provides **excellent coverage of core mapping constructs** but is **missing several advanced features** defined in the official FHIR ANTLR grammar:

**Critical Gaps**:
1. ConceptMap declarations (inline terminology)
2. Constant declarations
3. Type modes on parameters

**Test Coverage Gaps**:
1. Queried/Produced modes
2. Real-world R5→R6 mappings
3. Error position reporting
4. Performance testing

**Recommendation**: Prioritize Phase 1 (ConceptMap declarations, rule names, constants) to achieve full specification compliance for authoring complete, self-contained mapping files.

---

# Part 2: Using Mapping Files for Conversion in FHIR Services

**Date**: 2025-01-26
**Status**: Implementation Specification
**Related**: [FHIR $transform Operation](https://build.fhir.org/structuremap-operation-transform.html)

This section describes how FHIR Mapping Language files are integrated with FHIR Services to perform resource transformations at runtime.

---

## Overview: The $transform Operation

The FHIR specification defines the `$transform` operation for executing StructureMap transformations. This is the standard mechanism for invoking mapping files within a FHIR Service.

### Operation Endpoints

```
POST [base]/StructureMap/$transform           # Map provided in request
POST [base]/StructureMap/{id}/$transform      # Map retrieved by ID
```

### Input Parameters

| Parameter | Cardinality | Type | Description |
|-----------|-------------|------|-------------|
| `source` | 0..1 | uri | Canonical URL of the StructureMap to apply |
| `sourceMap` | 0..1 | StructureMap | The map resource itself (inline) |
| `supportingMap` | 0..* | StructureMap | Additional maps for dependencies |
| `srcMap` | 0..* | string | Maps in FML text format (R6+) |
| `content` | 1..1 | Resource | The input content to transform |

### Output

| Parameter | Cardinality | Type | Description |
|-----------|-------------|------|-------------|
| `return` | 1..1 | Resource | The transformed result |

### Example Request

```http
POST /StructureMap/patient-r5-to-r6/$transform
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "content",
      "resource": {
        "resourceType": "Patient",
        "id": "example",
        "name": [{"family": "Smith", "given": ["John"]}]
      }
    }
  ]
}
```

### Example Response

```json
{
  "resourceType": "Patient",
  "id": "example",
  "name": [{"family": "Smith", "given": ["John"]}]
}
```

---

## StructureMap Resource vs FML Text

The FHIR Mapping Language (FML) has two representations:

### 1. FML Text (Human-Readable)

```fml
map "http://example.org/PatientR5toR6" = "PatientTransform"

uses "http://hl7.org/fhir/5.0/StructureDefinition/Patient" alias PatientR5 as source
uses "http://hl7.org/fhir/6.0/StructureDefinition/Patient" alias PatientR6 as target

group Patient(source src : PatientR5, target tgt : PatientR6) {
  src.identifier -> tgt.identifier;
  src.active -> tgt.active;
  src.name -> tgt.name;
}
```

### 2. StructureMap Resource (Structured JSON/XML)

```json
{
  "resourceType": "StructureMap",
  "id": "patient-r5-to-r6",
  "url": "http://example.org/PatientR5toR6",
  "name": "PatientTransform",
  "status": "active",
  "structure": [
    {
      "url": "http://hl7.org/fhir/5.0/StructureDefinition/Patient",
      "mode": "source",
      "alias": "PatientR5"
    },
    {
      "url": "http://hl7.org/fhir/6.0/StructureDefinition/Patient",
      "mode": "target",
      "alias": "PatientR6"
    }
  ],
  "group": [
    {
      "name": "Patient",
      "input": [
        {"name": "src", "type": "PatientR5", "mode": "source"},
        {"name": "tgt", "type": "PatientR6", "mode": "target"}
      ],
      "rule": [
        {"name": "identifier", "source": [{"context": "src", "element": "identifier"}], "target": [{"context": "tgt", "element": "identifier"}]},
        {"name": "active", "source": [{"context": "src", "element": "active"}], "target": [{"context": "tgt", "element": "active"}]},
        {"name": "name", "source": [{"context": "src", "element": "name"}], "target": [{"context": "tgt", "element": "name"}]}
      ]
    }
  ]
}
```

**Typed API Access** (Ignixa implementation using `StructureMapJsonNode`):
```csharp
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

// Parse JSON to typed model
var structureMap = JsonSourceNodeFactory.Parse<StructureMapJsonNode>(jsonText);

// Strongly-typed property access
string url = structureMap.Url;  // "http://example.org/PatientR5toR6"
string name = structureMap.Name;  // "PatientTransform"
string status = structureMap.Status;  // "active"

// Access nested elements with IntelliSense support
foreach (var structure in structureMap.Structure ?? [])
{
    Console.WriteLine($"Uses: {structure.Url} ({structure.Mode})");
}

// Access groups and rules
foreach (var group in structureMap.Group ?? [])
{
    Console.WriteLine($"Group: {group.Name}");
    foreach (var rule in group.Rule ?? [])
    {
        Console.WriteLine($"  Rule: {rule.Name}");
    }
}
```

### Key Differences

| Aspect | FML Text | StructureMap Resource |
|--------|----------|----------------------|
| **Format** | Domain-specific language | JSON/XML |
| **Storage** | `.map` files, embedded | FHIR resource store |
| **Authoring** | Human-friendly | Machine-friendly |
| **Versioning** | Via file system | Via FHIR versioning |
| **Validation** | Parser-based | Schema-based |
| **FHIR R6** | `srcMap` parameter | `sourceMap` parameter |

### Conversion Between Formats

Most implementations support bidirectional conversion:

```
FML Text ⇄ StructureMap Resource
```

**Use Cases**:
- **Authoring**: Write in FML text, convert to StructureMap for storage
- **Editing**: Retrieve StructureMap, convert to FML for editing, save back
- **Execution**: Either format can be executed after parsing

---

### 2-Way Lossless Conversion Requirement

**Critical Requirement**: The implementation MUST support lossless bidirectional conversion between:

1. **FML Text** (human-readable mapping language)
2. **StructureMap Resource** (FHIR JSON/XML representation)

```
┌─────────────┐         ┌──────────────────┐         ┌─────────────────┐
│  FML Text   │ ──────> │   Intermediate   │ ──────> │  StructureMap   │
│  (.map)     │ <────── │   Expressions    │ <────── │  Resource       │
└─────────────┘         └──────────────────┘         └─────────────────┘
```

#### The Intermediate Expression Model

The **mapping language expressions** (AST from the parser) serve as the **canonical intermediate representation**:

| Format | Converts To | Converts From |
|--------|-------------|---------------|
| FML Text | `MapExpression` AST via `MappingCompiler.Parse()` | `MapExpression` AST via `FmlSerializer.Serialize()` |
| StructureMap | `MapExpression` AST via `StructureMapParser.Parse()` | `MapExpression` AST via `StructureMapBuilder.Build()` |

#### Lossless Requirements

For **FML → StructureMap → FML** round-trip:
- All semantic information preserved (groups, rules, sources, targets, transforms)
- Comments and whitespace NOT required to be preserved (acceptable loss)
- Rule ordering MUST be preserved
- Variable names MUST be preserved
- FHIRPath expressions MUST be preserved exactly

For **StructureMap → FML → StructureMap** round-trip:
- All resource elements preserved
- Extension data MUST be preserved
- Meta elements (id, version, status) MUST be preserved
- Contained resources MUST be preserved

#### Implementation Classes Needed

```csharp
// FML Text → MapExpression (EXISTING - MappingParser, not MappingCompiler)
public class MappingParser
{
    public MapExpression Parse(string fmlText);
}

// MapExpression → FML Text (EXISTING)
public class FmlSerializer
{
    public string Serialize(MapExpression map);
}

// StructureMap Resource → MapExpression (EXISTING - uses typed models)
public class StructureMapParser
{
    public MapExpression Parse(StructureMapJsonNode resource);
}

// MapExpression → StructureMap Resource (EXISTING - returns typed models)
public class StructureMapBuilder
{
    public StructureMapJsonNode Build(MapExpression map);
}
```

**Implementation Note**: The Ignixa implementation uses strongly-typed models from `Ignixa.Serialization`:
- `StructureMapJsonNode` for the main resource
- `StructureMapGroupJsonNode`, `StructureMapRuleJsonNode`, etc. for nested elements
- All parsers/builders work with these typed models for type safety and IntelliSense support

#### Validation Strategy

Round-trip tests MUST validate lossless conversion:

```csharp
[Fact]
public void FmlToStructureMapToFml_PreservesSemantics()
{
    var originalFml = LoadTestMap("patient-r5-to-r6.map");

    // FML → AST → StructureMap → AST → FML
    var ast1 = parser.Parse(originalFml);
    var structureMap = builder.Build(ast1);  // Returns StructureMapJsonNode
    var ast2 = structureMapParser.Parse(structureMap);  // Accepts StructureMapJsonNode
    var roundTrippedFml = serializer.Serialize(ast2);

    // Re-parse both and compare ASTs (ignores whitespace/formatting)
    var finalAst = parser.Parse(roundTrippedFml);
    AstComparer.AssertEquivalent(ast1, finalAst);
}

[Fact]
public void StructureMapToFmlToStructureMap_PreservesResource()
{
    var originalJson = LoadTestResource("patient-transform.json");
    var originalResource = JsonSourceNodeFactory.Parse<StructureMapJsonNode>(originalJson);

    // StructureMap → AST → FML → AST → StructureMap
    var ast1 = structureMapParser.Parse(originalResource);
    var fml = serializer.Serialize(ast1);
    var ast2 = parser.Parse(fml);
    var roundTrippedResource = builder.Build(ast2);  // Returns StructureMapJsonNode

    // Compare resources (ignoring meta.lastUpdated, etc.)
    roundTrippedResource.Url.Should().Be(originalResource.Url);
    roundTrippedResource.Name.Should().Be(originalResource.Name);
    // ... more assertions
}
```

#### Why This Matters

1. **Authoring Experience**: Users author in FML, store as StructureMap
2. **Editing Workflow**: Load StructureMap, edit as FML, save back
3. **Version Control**: FML is diff-friendly for Git workflows
4. **FHIR API Compliance**: StructureMap is the official resource format
5. **Tooling Interoperability**: Other implementations use StructureMap format

---

## Integration Architecture

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           FHIR Service                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐    ┌───────────────┐    ┌─────────────────────────┐  │
│  │   $transform │───>│ Transform     │───>│ MappingCompiler         │  │
│  │   Endpoint   │    │ Handler       │    │ (Parse FML → AST)       │  │
│  └──────────────┘    └───────────────┘    └─────────────────────────┘  │
│                             │                          │                 │
│                             ▼                          ▼                 │
│  ┌──────────────┐    ┌───────────────┐    ┌─────────────────────────┐  │
│  │ StructureMap │───>│ Map Registry  │───>│ MappingEvaluator        │  │
│  │   Storage    │    │ (URL → AST)   │    │ (Execute transformation)│  │
│  └──────────────┘    └───────────────┘    └─────────────────────────┘  │
│                                                        │                 │
│                                                        ▼                 │
│  ┌──────────────┐    ┌───────────────┐    ┌─────────────────────────┐  │
│  │ ConceptMap   │<───│ Transform     │<───│ MappingContext          │  │
│  │   Service    │    │ Functions     │    │ (Sources, Targets, Vars)│  │
│  └──────────────┘    └───────────────┘    └─────────────────────────┘  │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility |
|-----------|----------------|
| **$transform Endpoint** | HTTP handling, parameter extraction, response formatting |
| **Transform Handler** | Orchestrates the transformation workflow |
| **MappingCompiler** | Parses FML text into executable AST |
| **Map Registry** | Caches compiled maps by URL for reuse |
| **MappingEvaluator** | Executes transformation rules against resources |
| **MappingContext** | Holds execution state (sources, targets, variables) |
| **Transform Functions** | Built-in functions (create, copy, translate, etc.) |
| **ConceptMap Service** | Terminology translation support |

---

## Ignixa Implementation Approach

### Current State

The `Ignixa.FhirMappingLanguage` library provides:

✅ **Complete Parser** - FML text → `MapExpression` AST
✅ **Evaluator Engine** - Executes rules with visitor pattern
✅ **Map Registry** - In-memory storage with URL-based lookup
✅ **Import Resolver** - Handles recursive imports
✅ **Transform Functions** - 15+ standard transforms
✅ **FHIRPath Integration** - Embedded expression evaluation
✅ **Error Handling** - Strict/Graceful modes

❌ **Not Yet Integrated**:
- `$transform` endpoint in API layer
- Application layer handler/command
- StructureMap resource storage/retrieval
- Terminology service connection

### Proposed Implementation

#### 1. API Endpoint

**File**: `src/Ignixa.Api/Infrastructure/TransformEndpoints.cs`

```csharp
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

public static class TransformEndpoints
{
    public static IEndpointRouteBuilder MapTransformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Type-level operation
        endpoints.MapPost("/StructureMap/$transform", HandleTransform);

        // Instance-level operation
        endpoints.MapPost("/StructureMap/{id}/$transform", HandleTransformById);

        return endpoints;
    }

    private static async Task<IResult> HandleTransform(
        HttpContext ctx,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Extract parameters from body
        var parameters = await ctx.Request.ReadFromJsonAsync<Parameters>(cancellationToken);

        var command = new TransformResourceCommand
        {
            Source = parameters.GetParameter<string>("source"),
            SourceMap = parameters.GetParameter<StructureMapJsonNode>("sourceMap"),
            Content = parameters.GetParameter<Resource>("content")
        };

        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Ok(result);
    }
}
```

#### 2. Application Layer Command

**File**: `src/Ignixa.Application/Features/Transform/TransformResourceCommand.cs`

```csharp
using Ignixa.Serialization.Models;

public record TransformResourceCommand : IRequest<Resource>
{
    /// <summary>
    /// Canonical URL of the StructureMap to apply.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Inline StructureMap resource (typed model).
    /// </summary>
    public StructureMapJsonNode? SourceMap { get; init; }

    /// <summary>
    /// Maps in FML text format (R6+).
    /// </summary>
    public IReadOnlyList<string>? SrcMaps { get; init; }

    /// <summary>
    /// Supporting maps for dependencies (typed models).
    /// </summary>
    public IReadOnlyList<StructureMapJsonNode>? SupportingMaps { get; init; }

    /// <summary>
    /// The input content to transform.
    /// </summary>
    public required Resource Content { get; init; }
}
```

#### 3. Application Layer Handler

**File**: `src/Ignixa.Application/Features/Transform/TransformResourceHandler.cs`

```csharp
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

public class TransformResourceHandler : IRequestHandler<TransformResourceCommand, Resource>
{
    private readonly IStructureMapRepository _repository;
    private readonly IMapRegistry _mapRegistry;
    private readonly MappingParser _parser;
    private readonly StructureMapParser _structureMapParser;
    private readonly IConceptMapService _conceptMapService;
    private readonly IFhirPathEngine _fhirPathEngine;

    public TransformResourceHandler(
        IStructureMapRepository repository,
        IMapRegistry mapRegistry,
        MappingParser parser,
        StructureMapParser structureMapParser,
        IConceptMapService conceptMapService,
        IFhirPathEngine fhirPathEngine)
    {
        _repository = repository;
        _mapRegistry = mapRegistry;
        _parser = parser;
        _structureMapParser = structureMapParser;
        _conceptMapService = conceptMapService;
        _fhirPathEngine = fhirPathEngine;
    }

    public async Task<Resource> HandleAsync(
        TransformResourceCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve the mapping
        var map = await ResolveMapAsync(request, cancellationToken);

        // 2. Register supporting maps
        await RegisterSupportingMapsAsync(request.SupportingMaps, cancellationToken);

        // 3. Create execution context
        var context = CreateContext(request.Content);

        // 4. Execute transformation
        var evaluator = new MappingEvaluator();
        evaluator.Execute(map, context);

        // 5. Extract and return result
        return ExtractResult(context);
    }

    private async Task<MapExpression> ResolveMapAsync(
        TransformResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Priority: srcMaps (FML text) > sourceMap (resource) > source (URL)

        if (request.SrcMaps?.Any() == true)
        {
            // Parse FML text directly
            return _parser.Parse(request.SrcMaps.First());
        }

        if (request.SourceMap != null)
        {
            // Parse StructureMapJsonNode to MapExpression
            return _structureMapParser.Parse(request.SourceMap);
        }

        if (!string.IsNullOrEmpty(request.Source))
        {
            // Check registry cache first
            var cached = _mapRegistry.GetByUrl(request.Source);
            if (cached != null) return cached;

            // Load from repository (returns StructureMapJsonNode)
            var structureMap = await _repository.GetByUrlAsync(request.Source, cancellationToken);
            if (structureMap == null)
            {
                throw new InvalidOperationException($"StructureMap not found: {request.Source}");
            }

            // Parse typed model to MapExpression
            var map = _structureMapParser.Parse(structureMap);
            _mapRegistry.Register(map);
            return map;
        }

        throw new InvalidOperationException("No mapping source provided");
    }

    private MappingContext CreateContext(Resource content)
    {
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            Logger = msg => Log.Debug("Mapping: {Message}", msg)
        };

        // Wire up FHIRPath evaluator
        context.FhirPathEvaluator = (expression, element) =>
            _fhirPathEngine.Evaluate(expression, element);

        // Wire up ConceptMap resolver for translate() function
        context.ConceptMapResolver = (sourceCode, mapUrl, targetSystem) =>
            _conceptMapService.Translate(sourceCode, mapUrl, targetSystem);

        // Wire up resource creator for create() function
        context.ResourceCreator = resourceType =>
            ResourceFactory.Create(resourceType);

        // Set the source content
        var typedElement = content.ToTypedElement();
        context.SetSource("src", typedElement);

        // Create empty target
        var targetType = DetermineTargetType(content.TypeName);
        var target = ResourceFactory.Create(targetType);
        context.SetTarget("tgt", target.ToTypedElement());

        return context;
    }
}
```

---

## Runtime Execution Flow

### Step-by-Step Process

```
1. REQUEST RECEIVED
   POST /StructureMap/patient-r5-r6/$transform
   Body: { content: Patient R5 }

2. PARAMETER EXTRACTION
   - source: "http://example.org/PatientR5toR6" (from URL)
   - content: Patient resource (from body)

3. MAP RESOLUTION
   a. Check MapRegistry cache → Miss
   b. Load StructureMap from repository
   c. Convert to FML text (if needed)
   d. Parse to MapExpression AST
   e. Cache in MapRegistry

4. IMPORT RESOLUTION
   a. Scan map for imports
   b. Recursively load imported maps
   c. Register all in MapRegistry

5. CONTEXT SETUP
   a. Create MappingContext
   b. Set source: Patient R5 as ITypedElement
   c. Create empty target: Patient R6
   d. Wire callbacks: FHIRPath, ConceptMap, ResourceCreator

6. EVALUATION
   a. Find entry group (first group or explicit)
   b. Bind parameters to context
   c. For each rule:
      - Evaluate source expression
      - Apply where clause filter
      - Execute check clause validation
      - For each source match:
        - Evaluate target transform
        - Apply to target element
        - Process nested rules/group calls
   d. Log statements executed if present

7. RESULT EXTRACTION
   a. Get target from context
   b. Convert ITypedElement to Resource
   c. Serialize to JSON/XML

8. RESPONSE
   Return transformed Patient R6
```

### Execution Example

**Input**: Patient R5
```json
{
  "resourceType": "Patient",
  "id": "example",
  "identifier": [{"system": "http://example.org", "value": "12345"}],
  "name": [{"family": "Smith", "given": ["John", "Jacob"]}],
  "gender": "male"
}
```

**Mapping Execution Trace**:
```
[TRACE] Starting map: http://example.org/PatientR5toR6
[TRACE] Entering group: Patient
[TRACE]   Binding src → Patient/example
[TRACE]   Creating target: Patient
[TRACE] Rule: src.identifier -> tgt.identifier
[TRACE]   Source: 1 match(es)
[TRACE]   Transform: copy
[TRACE]   Target: identifier[0] = {...}
[TRACE] Rule: src.name -> tgt.name
[TRACE]   Source: 1 match(es)
[TRACE]   Transform: copy
[TRACE]   Target: name[0] = {...}
[TRACE] Rule: src.gender -> tgt.gender
[TRACE]   Source: 1 match(es)
[TRACE]   Transform: copy
[TRACE]   Target: gender = "male"
[TRACE] Completed group: Patient
```

**Output**: Patient R6 (identical structure in this case)

---

## Transform Functions

The mapping evaluator provides standard transform functions:

### Core Transforms

| Function | Description | Example |
|----------|-------------|---------|
| `create(type)` | Creates a new FHIR element | `create('Identifier')` |
| `copy(source)` | Copies source value | `copy(src.name)` |
| `uuid()` | Generates a UUID | `uuid()` |
| `reference(resource)` | Creates a reference | `reference(src)` |

### String Transforms

| Function | Description | Example |
|----------|-------------|---------|
| `truncate(str, len)` | Truncates to length | `truncate(src.text, 100)` |
| `append(str1, str2)` | Concatenates strings | `append(src.prefix, src.family)` |
| `escape(str)` | Escapes special chars | `escape(src.html)` |

### Type Transforms

| Function | Description | Example |
|----------|-------------|---------|
| `cast(value, type)` | Type conversion | `cast(src.value, 'decimal')` |
| `evaluate(expr)` | Evaluates FHIRPath | `evaluate('name.given.first()')` |
| `dateOp(value)` | Date parsing | `dateOp(src.birthDate)` |

### FHIR DataType Constructors

| Function | Description | Example |
|----------|-------------|---------|
| `Coding(system, code)` | Creates Coding | `Coding('http://loinc.org', '12345')` |
| `CodeableConcept(...)` | Creates CC | `CodeableConcept(Coding(...))` |
| `Quantity(value, unit)` | Creates Quantity | `Quantity(5.0, 'mg')` |
| `Identifier(system, value)` | Creates Identifier | `Identifier('urn:oid:...', '123')` |

### Terminology Transforms

| Function | Description | Example |
|----------|-------------|---------|
| `translate(code, map, target)` | Translates via ConceptMap | `translate(src.code, '#genderMap', 'code')` |

---

## Dependency Management

### Import Resolution

Maps can import other maps for reuse:

```fml
map "http://example.org/main" = "MainMap"

imports "http://hl7.org/fhir/StructureMap/datatypes"
imports "http://example.org/extensions"

group Main(source src, target tgt) {
  src.name -> tgt.name then DataType_Name(src.name, tgt.name);
}
```

### Resolution Strategy

```csharp
public class ImportResolver
{
    private readonly IMapRegistry _registry;
    private readonly MappingCompiler _compiler;
    private readonly IMapLoader _loader;
    private readonly HashSet<string> _resolving = new();

    public async Task ResolveImportsAsync(MapExpression map)
    {
        // Detect circular imports
        if (_resolving.Contains(map.Url))
        {
            throw new InvalidOperationException($"Circular import detected: {map.Url}");
        }

        _resolving.Add(map.Url);

        try
        {
            foreach (var import in map.Imports)
            {
                if (!_registry.Contains(import))
                {
                    // Load and parse the imported map
                    var text = await _loader.LoadAsync(import);
                    if (text == null)
                    {
                        throw new InvalidOperationException($"Import not found: {import}");
                    }

                    var importedMap = _compiler.Parse(text);
                    _registry.Register(importedMap);

                    // Recursively resolve nested imports
                    await ResolveImportsAsync(importedMap);
                }
            }
        }
        finally
        {
            _resolving.Remove(map.Url);
        }
    }
}
```

### Map Loader Implementations

```csharp
// File system loader
public class FileSystemMapLoader : IMapLoader
{
    public bool CanLoad(string url) => url.StartsWith("file://");

    public async Task<string?> LoadAsync(string url)
    {
        var path = url.Replace("file://", "");
        return await File.ReadAllTextAsync(path);
    }
}

// HTTP loader
public class HttpMapLoader : IMapLoader
{
    public bool CanLoad(string url) => url.StartsWith("http://") || url.StartsWith("https://");

    public async Task<string?> LoadAsync(string url)
    {
        using var client = new HttpClient();
        return await client.GetStringAsync(url);
    }
}

// FHIR Server loader (fetches StructureMap resource)
public class FhirServerMapLoader : IMapLoader
{
    private readonly IFhirClient _client;
    private readonly FmlSerializer _serializer;

    public bool CanLoad(string url) => url.Contains("/StructureMap/");

    public async Task<string?> LoadAsync(string url)
    {
        // Fetch as StructureMapJsonNode
        var structureMap = await _client.ReadAsync<StructureMapJsonNode>(url);

        // Parse to MapExpression, then serialize to FML text
        var parser = new StructureMapParser();
        var mapExpression = parser.Parse(structureMap);
        return _serializer.Serialize(mapExpression);
    }
}

// Composite loader (chains multiple loaders)
public class CompositeMapLoader : IMapLoader
{
    private readonly List<IMapLoader> _loaders = new();

    public void AddLoader(IMapLoader loader) => _loaders.Add(loader);

    public bool CanLoad(string url) => _loaders.Any(l => l.CanLoad(url));

    public async Task<string?> LoadAsync(string url)
    {
        var loader = _loaders.FirstOrDefault(l => l.CanLoad(url));
        return loader != null ? await loader.LoadAsync(url) : null;
    }
}
```

---

## Terminology Integration

### ConceptMap Resolution

The `translate()` transform function requires terminology service integration:

```csharp
public class ConceptMapService : IConceptMapService
{
    private readonly IConceptMapRepository _repository;

    public string? Translate(string sourceCode, string mapUrl, string targetSystem)
    {
        // Load the ConceptMap
        var conceptMap = _repository.GetByUrl(mapUrl);
        if (conceptMap == null) return null;

        // Find matching group
        var group = conceptMap.Group.FirstOrDefault(g =>
            g.Target == targetSystem || string.IsNullOrEmpty(targetSystem));
        if (group == null) return null;

        // Find mapping for source code
        var element = group.Element.FirstOrDefault(e => e.Code == sourceCode);
        if (element == null) return null;

        // Return first target (or apply equivalence rules)
        return element.Target.FirstOrDefault()?.Code;
    }
}
```

### Integration in MappingContext

```csharp
// Wire up ConceptMap resolver
context.ConceptMapResolver = (sourceCode, mapUrl, targetSystem) =>
{
    // Handle inline ConceptMaps (prefixed with #)
    if (mapUrl.StartsWith("#"))
    {
        var inlineMap = map.ConceptMaps.FirstOrDefault(cm => cm.Identifier == mapUrl);
        return ResolveInlineConceptMap(inlineMap, sourceCode, targetSystem);
    }

    // Delegate to terminology service
    return _conceptMapService.Translate(sourceCode, mapUrl, targetSystem);
};
```

---

## Error Handling

### Error Modes

```csharp
public enum ErrorMode
{
    /// <summary>
    /// Throws exception on first error.
    /// </summary>
    Strict,

    /// <summary>
    /// Collects errors and continues execution.
    /// </summary>
    Graceful
}
```

### Error Types

| Error Type | Cause | Handling |
|------------|-------|----------|
| **ParseException** | Invalid FML syntax | Return 400 Bad Request |
| **TypeValidationException** | Type mismatches | Return 422 Unprocessable |
| **MappingExecutionException** | Runtime evaluation error | Return 500 or include in OperationOutcome |
| **ImportNotFoundException** | Missing import | Return 400 with details |

### OperationOutcome Response

```json
{
  "resourceType": "OperationOutcome",
  "issue": [
    {
      "severity": "error",
      "code": "processing",
      "diagnostics": "Failed to execute rule at line 15: Source element 'birthDate' not found",
      "location": ["StructureMap.group[0].rule[3]"]
    }
  ]
}
```

---

## Performance Considerations

### Caching Strategy

```csharp
public class CachingMapRegistry : IMapRegistry
{
    private readonly ConcurrentDictionary<string, MapExpression> _cache = new();
    private readonly IMapLoader _loader;
    private readonly MappingCompiler _compiler;

    public async Task<MapExpression> GetOrLoadAsync(string url)
    {
        // Check cache
        if (_cache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        // Load and parse
        var text = await _loader.LoadAsync(url);
        var map = _compiler.Parse(text);

        // Cache for future use
        _cache.TryAdd(url, map);

        return map;
    }

    public void Invalidate(string url) => _cache.TryRemove(url, out _);
    public void InvalidateAll() => _cache.Clear();
}
```

### Recommendations

| Aspect | Recommendation |
|--------|----------------|
| **Compilation** | Cache compiled `MapExpression` by URL |
| **Context Reuse** | Create new context per request (not thread-safe) |
| **Large Maps** | Consider lazy group loading |
| **FHIRPath** | Cache compiled FHIRPath expressions |
| **Transforms** | Standard transforms are stateless, safe to reuse |

---

## Available Implementations

### Reference Implementations

| Implementation | Language | Notes |
|----------------|----------|-------|
| **HAPI FHIR** | Java | Full StructureMapUtilities implementation |
| **matchbox** | Java | HAPI-based, production server |
| **fhir-net-mappinglanguage** | C# | Port of Java impl, Firely SDK compatible |
| **Ignixa** | C# | Native implementation, Superpower parser |

### Live Servers

- **Java**: https://test.ahdis.ch/matchboxv3/fhir/StructureMap/$transform
- **C#**: https://fhir-mapping-lab.azurewebsites.net/StructureMap/$transform

### Command-Line Tools

```bash
# FHIR Validator CLI (Java)
java -jar validator_cli.jar input.json \
  -output output.json \
  -transform http://example.org/MyMap \
  -version 4.0 \
  -ig hl7.fhir.r4.core#4.0.1
```

---

## Implementation Checklist

### Phase 1: Core Integration

- [ ] Create `TransformEndpoints.cs` in API layer
- [ ] Create `TransformResourceCommand.cs` in Application layer
- [ ] Create `TransformResourceHandler.cs` with basic execution
- [ ] Register services in DI container
- [ ] Add endpoint routing in `Program.cs`

### Phase 2: Storage Integration

- [ ] Create `IStructureMapRepository` interface
- [ ] Implement repository for StructureMap storage
- [ ] Add StructureMap CRUD endpoints
- [ ] Implement FML ↔ StructureMap conversion

### Phase 3: Advanced Features

- [ ] Integrate terminology services for `translate()`
- [ ] Add caching with invalidation
- [ ] Implement import resolution
- [ ] Add metrics/tracing
- [ ] Performance optimization

### Phase 4: Production Hardening

- [ ] Comprehensive error handling
- [ ] OperationOutcome responses
- [ ] Rate limiting
- [ ] Timeout handling for complex maps
- [ ] Audit logging

---

## References

- [FHIR $transform Operation](https://build.fhir.org/structuremap-operation-transform.html)
- [StructureMap Resource](https://build.fhir.org/structuremap.html)
- [FHIR Mapping Language Grammar](https://build.fhir.org/mapping.g4)
- [FHIR Mapping Tutorial](https://build.fhir.org/mapping-tutorial.html)
- [matchbox Implementation](https://github.com/ahdis/matchbox)
- [fhir-net-mappinglanguage](https://github.com/brianpos/fhir-net-mappinglanguage)
