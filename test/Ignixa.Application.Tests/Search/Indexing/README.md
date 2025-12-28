# Composite Search Indexing Diagnostic Tests

## Overview

This directory contains diagnostic unit tests for the composite search indexing pipeline. These tests were created to debug why `code-value-quantity` composite search wasn't working in E2E tests.

## Test Results

The diagnostic tests in `CompositeSearchIndexingDiagnosticTests.cs` confirm that **the indexing pipeline is working correctly at the unit level**:

### ✅ What's Working

1. **Search Parameter Extraction**: The `code-value-quantity` search parameter is correctly identified and extracted from Observation resources
2. **CompositeSearchValue Creation**: The indexer creates `CompositeSearchValue` with 2 components
3. **Component 0 (Token)**: Correctly extracts `TokenSearchValue` from `Observation.code.coding`:
   - System: `http://loinc.org`
   - Code: `9272-6`
   - Text: `1 minute Apgar Score`
4. **Component 1 (Quantity)**: Correctly extracts `QuantitySearchValue` from `Observation.valueQuantity`:
   - Low: `10`
   - High: `10`
   - System: `http://unitsofmeasure.org`
   - Code: `{score}`
5. **FHIRPath Evaluation**: Both component expressions evaluate correctly:
   - Component 0: `code` → extracts CodeableConcept
   - Component 1: `value.as(Quantity)` → extracts Quantity element
6. **Converters**: Both `TokenSearchValue` and `QuantitySearchValue` converters are registered and working

## Test Data

The tests use the same Observation structure as the E2E tests:
- Code: `9272-6` (LOINC) - "1 minute Apgar Score"
- ValueQuantity: `10 {score}` (UCUM)

## Next Steps

Since the indexing pipeline is working correctly at the unit level, the E2E test failures must be caused by:

1. **Query-side Issue**: The SQL query generation or execution for composite searches
2. **Storage Issue**: How composite indices are persisted to the database
3. **Search Parameter Mapping**: Mismatch between indexed values and search parameter definitions in the database

To identify the root cause, investigate:
- `SearchParameterQueryGenerator` - SQL query generation for composites
- Row generators for composite types (e.g., `TokenQuantityCompositeRowGenerator`)
- Database schema for composite search parameter tables

## Running the Tests

```bash
# Run all diagnostic tests
dotnet test --filter "FullyQualifiedName~CompositeSearchIndexingDiagnosticTests"

# Run specific test
dotnet test --filter "FullyQualifiedName~GivenObservationWithCodeAndValueQuantity_WhenIndexing"
```

## Console Output

The tests produce detailed console output showing:
- All extracted search indices
- Component structure breakdown
- FHIRPath expression evaluation results
- Converter verification

This output is useful for understanding the indexing pipeline flow.
