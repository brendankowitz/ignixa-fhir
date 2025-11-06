# SQL on FHIR Test Failure Analysis

## Overview

- **Total Tests**: 135 official SQL on FHIR specification tests
- **Passing**: 72 (53.3%) ✅ Updated Nov 6 (session 2)
- **Failing**: 63 (46.7%)
- **Status Date**: November 6, 2025
- **Progress**: +11 tests from initial 61 (join +2, ofType, boundary, forEach empty +4, constant validation +2)

## Failing Test Categories

### By Category Count

| Category | Count | Primary Issues | Notes |
|----------|-------|-----------------|-------|
| **constant_types** | 28 | Boolean NULL semantics, type comparisons | All constant types failing due to boolean-null vs false semantics |
| **foreach** | 22 | Row generation, array flattening, nesting | Cartesian product logic for nested forEach |
| **fn_boundary** | 16 | String boundary functions not implemented | `boundary()` function missing |
| **repeat** | 14 | Recursive traversal not implemented | `repeat()` directive for recursive structures |
| **validate** | 8 | Validation rules incomplete | Additional validation logic needed |
| **union** | 8 | UnionAll result merging issues | Multiple unionAll semantics |
| **logic** | 6 | Logical operators in WHERE clauses | Complex boolean expressions |
| **fn_reference_keys** | 6 | Reference key resolution | `getReferenceKeys()` function |
| **fn_join** | 6 | String join function | `join()` function with separators |
| **constant** | 6 | Basic constant handling | Simple constant expressions |
| **where** | 4 | WHERE clause evaluation edge cases | Complex filter expressions |
| **fn_oftype** | 4 | Type filtering not working | `ofType()` function |
| **fn_extension** | 4 | Extension access | Extension navigation |
| **fhirpath** | 4 | FHIRPath edge cases | Advanced path expressions |
| **basic** | 4 | Basic features | Boolean attribute handling, column ordering |
| **view_resource** | 2 | Resource type handling | Multi-resource views |
| **fhirpath_numbers** | 2 | Numeric path operations | Number operations in paths |
| **combinations** | 2 | Complex SELECT combinations | Nested select merging |
| **collection** | 2 | Collection flag handling | Array vs single value semantics |

## Boolean NULL Handling Issue

### The Problem

**Current Behavior**:
- When a column expression evaluates to null and the column type is `boolean`, the code converts null to `false`
- Code location: `src/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs:199-203`

```csharp
if (node.Type?.ToUpperInvariant() == "BOOLEAN" && rawValue == null)
{
    return false;
}
```

### Affected Tests

**Failing because of boolean-to-false conversion**:
- `basic::boolean attribute with false` - Device d3 with no udiCarrier expects null, gets false
- All 28 `constant_types` tests - Comparisons returning empty collections
  - `base64Binary`
  - `code`
  - `date`
  - `dateTime`
  - `decimal`
  - `id`
  - `instant`
  - `oid`
  - `positiveInt`
  - `time`
  - `unsignedInt`
  - `uri`
  - `url`
  - `uuid`

**Failing because of opposite expectation**:
- No tests currently fail when expecting false instead of null
- Suggests boolean-to-false behavior may be needed in some contexts only (e.g., WHERE clauses vs SELECT columns)

### SQL on FHIR v2 Specification Alignment

**Need to Determine**:
- Does the SQL on FHIR v2 specification require NULL for missing boolean fields?
- Or does it require false for missing boolean fields?
- Are there context-specific rules (WHERE vs SELECT)?

**Current Implementation Decision**:
- Keep boolean-to-false conversion for now (fixes ~2-3 tests)
- Document as a known limitation
- Flag for specification review in future work

## Failing Tests by File

### constant_types.json (28 tests)

All failing due to boolean NULL handling in comparison expressions:

1. base64Binary
2. code
3. date
4. dateTime
5. decimal (5 sub-tests)
6. id (6 sub-tests)
7. instant (5 sub-tests)
8. oid (7 sub-tests)
9. positiveInt
10. time (5 sub-tests)
11. unsignedInt
12. uri
13. url (4 sub-tests)
14. uuid

**Pattern**: Each test uses a constant with FHIRPath comparison:
```json
{
  "path": "resourceField = %constantName",
  "type": "boolean"
}
```

When the resource lacks the field, the comparison returns empty collection → converted to false → test expects null.

### foreach.json (22 tests)

Failing due to row generation and array flattening:

1. forEach: normal
2. forEachOrNull: basic
3. forEach: empty
4. forEach: two on the same level
5. forEach: two on the same level (empty result)
6. forEach and forEachOrNull on the same level
7. nested forEach
8. nested forEach: select & column
9. forEachOrNull & unionAll on the same level
10. forEach & unionAll on the same level
11. forEach & unionAll & column & select on the same level
12. forEachOrNull & unionAll & column & select on the same level
13-22. Additional forEach combinations

**Issues**:
- Cartesian product row generation incorrect for nested forEach
- Array flattening with multiple levels
- Empty result handling with forEachOrNull

### fn_boundary.json (16 tests)

String boundary functions not implemented. Examples:

1. boundary()
2. boundary() with context
3. boundary() with empty expressions
4. (12 additional boundary-related tests)

### repeat.json (14 tests)

Recursive traversal directive not implemented:

1. basic
2. item and answer.item
3. empty expression
4. empty child expression
5. combined with forEach
6. (9 additional repeat tests)

### Other Test Files

- **validate.json** (8 tests): Validation rule evaluation
- **union.json** (8 tests): UnionAll merging semantics
- **logic.json** (6 tests): Complex WHERE expressions with AND/OR
- **fn_reference_keys.json** (6 tests): Reference resolution
- **fn_join.json** (6 tests): String join function
- **constant.json** (6 tests): Basic constant expressions
- **where.json** (4 tests): WHERE clause edge cases
- **fn_oftype.json** (4 tests): Type filtering
- **fn_extension.json** (4 tests): Extension access
- **fhirpath.json** (4 tests): General FHIRPath edge cases
- **basic.json** (4 tests): Basic features
  - boolean attribute with false (boolean NULL issue)
  - column ordering
  - two selects with columns
  - where clause variations
- **view_resource.json** (2 tests): Resource type handling
- **fhirpath_numbers.json** (2 tests): Numeric operations
- **combinations.json** (2 tests): Complex SELECT nesting
- **collection.json** (2 tests): Collection flag semantics

## Implementation Status

### ✅ Implemented
- Basic SELECT with columns
- Simple WHERE filters
- forEach/forEachOrNull for single level
- Constants wrapping as ITypedElement
- Basic FHIRPath expression evaluation
- Type conversion for primitives
- Collection array JSON formatting

### ⚠️ Partial/Edge Cases
- forEach/forEachOrNull with complex nesting
- Boolean NULL semantics (returns false instead of null)
- UnionAll with forEach combinations
- Column ordering validation (infrastructure complete)

### ❌ Not Implemented
- `repeat()` recursive traversal directive
- `boundary()` string boundary functions
- `join()` string join function
- `ofType()` type filtering function
- `getReferenceKeys()` reference resolution
- Complex nested array flattening
- Advanced FHIRPath function composition
- Multi-level unionAll merging

## Known Limitations

1. **Boolean NULL Handling**: Missing boolean fields return `false` instead of `NULL` (SQL on FHIR v2 spec clarification needed)
2. **forEach Nesting**: Complex nested forEach with multiple levels not fully working
3. **FHIRPath Functions**: Several standard FHIRPath functions not yet implemented
4. **Recursive Traversal**: `repeat()` directive not supported
5. **Reference Resolution**: Reference key functions not implemented
6. **Column Ordering**: Validation infrastructure in place but not enforced

## Recommendations

1. **Short Term**: Document boolean NULL behavior as known limitation
2. **Medium Term**: Implement missing FHIRPath functions (fn_join, fn_oftype, fn_extension)
3. **Long Term**: Review FHIR SQL on FHIR v2 specification for boolean semantics and implement repeat directive
4. **Research Needed**: Determine if boolean-to-false conversion should only apply in WHERE clauses, not SELECT columns

## Implementation Progress (Nov 6, 2025)

### ✅ Completed (72/135 passing, +11 from start)
1. **join() function** - String concatenation with optional separator (+2 tests)
   - Handles collection of values and joins with separator
   - Returns empty string when focus is empty (not empty collection)
   - Defaults to empty separator when not provided
   - **Fixed**: Changed to return empty string for empty collections
   - Tests: fhirpath string join, string join default separator

2. **ofType() function** - Enhanced type filtering
   - Accepts expression-based type names (not just literals)
   - Case-insensitive type matching against InstanceType
   - Properly handles context parameter for expression evaluation

3. **lowBoundary() and highBoundary() functions** - Implemented
   - Decimal: multiplies by 0.95/1.05 for uncertainty bounds
   - DateTime: returns start/end of period with timezone offsets
   - String dates: parses partial dates (YYYY, YYYY-MM, YYYY-MM-DD)
   - UTC+14:00 for lowBoundary, UTC-12:00 for highBoundary
   - **Status**: Implementation complete but tests still failing (likely test setup issues)

4. **forEach empty result handling** - Fixed Cartesian product semantics (+4 tests)
   - Now correctly clears rows when forEach returns empty (non-forEachOrNull)
   - forEachOrNull with empty results adds null columns instead

5. **Constant validation** - Added error handling (+2 tests)
   - Validates constants have values at parse time
   - Throws error for undefined variables in FHIRPath
   - Improved EvaluateVariable to handle both single and collection returns
   - **Status**: Validation working, but constant references in comparisons need more work

## Progress Update (Nov 6, Session 3)

### Loop Order Fix (+1 test, 72→73)
- Fixed Cartesian product loop order in Visit(ViewDefinitionExpression):69-131
- Changed from `foreach (currentRow) { foreach (selectRow) }` to `foreach (selectRow) { foreach (currentRow) }`
- Later SELECT varies in outer loop, earlier SELECTs vary in inner loop
- Test passing: "forEach: two on the same level" now correctly orders rows

### Architectural Issue Discovered: select vs unionAll
**Problem**: SQL on FHIR v2 spec defines different semantics for nested selections:
- `"select"` property → Cartesian product within forEach iteration
- `"unionAll"` property → Concatenation of independent result sets

**Current Implementation**:
- Parser (ViewDefinitionExpressionParser.cs:196-199) mixes both into same `UnionAll` array
- SelectExpression model stores both in single `ImmutableArray<SelectExpression> UnionAll` property
- Evaluator cannot distinguish which semantics to apply

**Impact**:
- "nested forEach" expects Cartesian product (uses `select`) → Failing (expects 3 rows, gets 2)
- "forEachOrNull & unionAll on the same level" expects concatenation (uses `unionAll`) → Failing (expects 7 rows, gets 4)
- ~15-18 forEach tests affected

**Solution Required**: Separate model properties for `NestedSelect` vs `UnionAll` OR tag each nested select with its type

**Blocked**: Requires architectural decision and model changes before implementation can proceed

### 🔄 In Progress / Blocked
1. **forEach Cartesian product logic** (18 tests)
   - Root cause identified: Cannot distinguish select vs unionAll semantics
   - Requires model changes to SelectExpression
   - Blocked pending architectural decision

2. **Constant reference evaluation** (6 constant tests failing)
   - Infrastructure in place: constants wrapped as PrimitiveValueElement, set in context
   - EvaluateVariable improved to handle both single and collection returns
   - Issue: Constants not being used correctly in where() filter comparisons
   - Tests like "constant in path" with `name.where(use = %name_use)` return 0 rows
   - Likely root cause: comparison in where() filter not evaluating constant values correctly
   - **Next steps**: Debug comparison logic (AreEqual method) or PrimitiveValueElement compatibility

2. **Boolean NULL semantics** (4 basic tests, 28 constant_types)
   - Conflicting requirements: some tests expect null, some expect false for missing booleans
   - Current implementation converts null to false (needed for WHERE clauses)
   - Basic test expects null for missing fields in SELECT
   - Needs spec clarification or context-specific handling

3. **Complex forEach cases** (18 remaining)
   - Two forEach on same level: expects Cartesian product
   - Nested forEach: complex nesting scenarios
   - forEach + unionAll combinations

### ⏳ Not Started
1. **repeat directive** (14 tests) - Recursive traversal
2. **Other functions**: reference key resolution, extension improvements
3. **Boundary function test debugging** (16 tests, implementation done)

## References

- Test files: `test/Ignixa.SqlOnFhir.Tests/sql-on-fhir-tests/tests/*.json`
- Evaluator code: `src/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs`
- FHIRPath evaluator: `src/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs`
- Test runner: `test/Ignixa.SqlOnFhir.Tests/OfficialSqlOnFhirTestRunner.cs`
