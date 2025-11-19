# FHIR Mapping Language Implementation Analysis

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
