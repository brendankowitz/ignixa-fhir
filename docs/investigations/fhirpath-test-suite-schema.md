# FHIRPath Test Suite XML Schema

**Source**: https://raw.githubusercontent.com/FHIR/fhir-test-cases/master/r4/fhirpath/tests-fhir-r4.xml

## Root Element

```xml
<tests name="FHIRPathTestSuite"
       description="FHIRPath Test Suite"
       reference="http://hl7.org/fhirpath|2.0.0">
```

- **Element**: `<tests>`
- **Attributes**:
  - `name`: Test suite identifier ("FHIRPathTestSuite")
  - `description`: Human-readable description
  - `reference`: FHIRPath specification version reference

## Hierarchical Organization

```
<tests>
  └── <group> (multiple)
       ├── name: Category identifier (e.g., "testBasics", "testEquality")
       ├── description: Category explanation
       └── <test> (multiple)
            ├── Core attributes
            ├── <expression> child
            └── <output> children (0 or more)
```

## Test Group Structure

Groups organize related tests by functional area:

```xml
<group name="testBasics" description="Basic FHIRPath functionality">
  <test>...</test>
  <test>...</test>
</group>
```

**Common Groups**:
- `comments` - Comment syntax tests
- `testBasics` - Fundamental operations
- `testEquality` - Equality/comparison operators
- `testType` - Type system tests
- `testCollections` - Collection operations
- `testFunctions` - Built-in functions (where, select, exists, etc.)
- `testArithmetic` - Math operations
- `testBoolean` - Boolean logic
- `testConversions` - Type conversions

## Test Element Attributes

| Attribute | Required | Values | Purpose |
|-----------|----------|--------|---------|
| `name` | Yes | String | Unique test identifier |
| `inputfile` | No | Filename | Reference to test data (XML/JSON) |
| `description` | No | String | Human-readable explanation |
| `mode` | No | "strict", etc. | Execution mode (strict validation) |
| `version` | No | Semver | Minimum FHIRPath version required |
| `ordered` | No | "true"/"false" | Whether result order matters (default: true) |
| `predicate` | No | "true"/"false" | Evaluate in boolean predicate context |
| `checkOrderedFunctions` | No | "true"/"false" | Validate function ordering requirements |

## Child Elements

### `<expression>` Element

Contains the FHIRPath expression to evaluate.

**Attributes**:
- `invalid` (optional): Expected failure type
  - `"syntax"` - Expression should fail parsing
  - `"semantic"` - Expression should fail validation (type errors, undefined names)
  - `"execution"` - Expression should fail at runtime

**Examples**:
```xml
<!-- Valid expression -->
<expression>Patient.name.given</expression>

<!-- Syntax error test -->
<expression invalid="syntax">Patient.name..given</expression>

<!-- Semantic error test (undefined property) -->
<expression invalid="semantic">Patient.undefinedProperty</expression>
```

### `<output>` Elements

Zero or more expected result values. Multiple outputs represent collection results.

**Attributes**:
- `type` (required): Data type of expected result
  - Primitives: `"boolean"`, `"integer"`, `"decimal"`, `"string"`, `"date"`, `"time"`, `"dateTime"`
  - FHIR types: `"code"`, `"Quantity"`, `"Coding"`, `"BackboneElement"`, etc.

**Content**: Expected value as string

**No outputs = empty collection expected**:
```xml
<test name="testEmptyResult">
  <expression>Patient.name.where(use='unknown')</expression>
  <!-- No <output> elements means empty result -->
</test>
```

**Multiple outputs = collection**:
```xml
<test name="testMultipleResults">
  <expression>Patient.name.given</expression>
  <output type="string">Peter</output>
  <output type="string">James</output>
</test>
```

## Test Types/Modes

### 1. **Standard Evaluation Tests**
Execute expression and compare output:
```xml
<test name="testBasicPath" inputfile="patient-example.xml">
  <expression>Patient.name.family</expression>
  <output type="string">Chalmers</output>
</test>
```

### 2. **Invalid Expression Tests**
Verify error detection:
```xml
<test name="testSyntaxError">
  <expression invalid="syntax">1 + + 2</expression>
</test>

<test name="testSemanticError" inputfile="patient-example.xml">
  <expression invalid="semantic">Patient.nonExistentProperty</expression>
</test>

<test name="testExecutionError" inputfile="patient-example.xml">
  <expression invalid="execution">1 / 0</expression>
</test>
```

### 3. **Predicate Context Tests**
Evaluate as boolean predicate (implicit `exists()` and `all()`):
```xml
<test name="testPredicate" inputfile="patient-example.xml" predicate="true">
  <expression>Patient.name.given</expression>
  <output type="boolean">true</output>
</test>
```

### 4. **Unordered Collection Tests**
Results can be in any order:
```xml
<test name="testUnorderedSet" inputfile="patient-example.xml" ordered="false">
  <expression>Patient.name.given</expression>
  <output type="string">James</output>
  <output type="string">Peter</output>
  <!-- Order doesn't matter: [Peter, James] or [James, Peter] both valid -->
</test>
```

### 5. **Strict Mode Tests**
Enforce stricter validation:
```xml
<test name="testStrictMode" inputfile="patient-example.xml" mode="strict">
  <expression>Patient.name.given</expression>
  <output type="string">Peter</output>
</test>
```

## Input Files

Tests reference external XML/JSON files containing FHIR resources:

**Common input files**:
- `patient-example.xml` - Patient resource
- `observation-example.xml` - Observation resource
- `questionnaire-example.xml` - Questionnaire resource
- `parameters-example.xml` - Parameters resource

**Input file locations**: Same directory as test suite XML or specified relative path.

## Complete Test Examples

### Example 1: Basic Path Navigation
```xml
<test name="testSimplePath" inputfile="patient-example.xml">
  <expression>Patient.name.family</expression>
  <output type="string">Chalmers</output>
</test>
```

### Example 2: Boolean Comparison
```xml
<test name="testBooleanEquals" inputfile="patient-example.xml">
  <expression>Patient.active = true</expression>
  <output type="boolean">true</output>
</test>
```

### Example 3: Collection with Multiple Results
```xml
<test name="testMultipleGivenNames" inputfile="patient-example.xml">
  <expression>Patient.name.first().given</expression>
  <output type="string">Peter</output>
  <output type="string">James</output>
</test>
```

### Example 4: Empty Collection
```xml
<test name="testWhereNoMatch" inputfile="patient-example.xml">
  <expression>Patient.name.where(use='official').given</expression>
  <!-- Empty result - no outputs -->
</test>
```

### Example 5: Syntax Error Test
```xml
<test name="testInvalidSyntax">
  <expression invalid="syntax">Patient..name</expression>
</test>
```

### Example 6: Semantic Error Test
```xml
<test name="testInvalidProperty" inputfile="patient-example.xml" mode="strict">
  <expression invalid="semantic">Patient.unknownField</expression>
</test>
```

### Example 7: Function Test
```xml
<test name="testExists" inputfile="patient-example.xml">
  <expression>Patient.name.exists()</expression>
  <output type="boolean">true</output>
</test>
```

### Example 8: Arithmetic Test
```xml
<test name="testAddition">
  <expression>1 + 2</expression>
  <output type="integer">3</output>
</test>
```

### Example 9: Type Conversion
```xml
<test name="testToInteger">
  <expression>'42'.toInteger()</expression>
  <output type="integer">42</output>
</test>
```

### Example 10: Unordered Collection
```xml
<test name="testUnionUnordered" ordered="false">
  <expression>(1 | 2 | 3)</expression>
  <output type="integer">1</output>
  <output type="integer">2</output>
  <output type="integer">3</output>
  <!-- Any permutation valid -->
</test>
```

## Schema Summary

**Key Design Principles**:
1. **Hierarchical**: Suite → Groups → Tests
2. **Declarative**: Expression + expected outputs
3. **Typed**: All outputs have explicit types
4. **Error Testing**: `invalid` attribute for negative tests
5. **Flexible Ordering**: `ordered` attribute for set semantics
6. **Context-Aware**: `predicate` and `mode` attributes
7. **Reusable Data**: External input files shared across tests

**Test Execution Model**:
1. Load input file (if specified)
2. Parse expression
3. If `invalid` attribute present:
   - Verify error occurs at specified stage
4. Else:
   - Evaluate expression
   - Compare result count and values with `<output>` elements
   - Apply ordering rules (`ordered` attribute)

**Coverage Areas**:
- Syntax validation (parsing)
- Semantic validation (type checking, name resolution)
- Runtime execution (operations, functions)
- Collection semantics (ordering, uniqueness)
- Type system (conversions, polymorphism)
- FHIR-specific features (navigation, resource references)
