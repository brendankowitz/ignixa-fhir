# Investigation: _not-referenced Search Parameter

**Feature**: search
**Status**: Implemented
**Created**: 2025-12-27
**Implemented**: 2025-12-28

## Problem Statement

Healthcare systems accumulate "orphaned" resources - resources that are no longer referenced by any other resource. Examples include:
- Observations without a patient (deleted patient)
- DocumentReferences not linked to any encounter
- Practitioners not assigned to any organization

The `_not-referenced` search parameter enables finding these orphaned resources for cleanup, audit, and data quality workflows.

## Approach

Implement the `_not-referenced` search parameter as documented in Microsoft FHIR Server:

### Syntax

```
# Find all patients not referenced by any resource
GET /Patient?_not-referenced=*:*

# Find patients not referenced by any Observation
GET /Patient?_not-referenced=Observation:*

# Find patients not referenced by Observation.subject specifically
GET /Patient?_not-referenced=Observation:subject
```

### Semantics

| Pattern | Meaning |
|---------|---------|
| `*:*` | Not referenced by any resource via any reference path |
| `{ResourceType}:*` | Not referenced by the specified resource type |
| `{ResourceType}:{path}` | Not referenced via the specific reference path |

## Implementation Options

### Option A: Subquery-Based (SQL)

Generate SQL subquery checking for absence of references:

```sql
SELECT r.* FROM Resource r
WHERE r.ResourceTypeId = @PatientTypeId
  AND NOT EXISTS (
    SELECT 1 FROM ReferenceSearchParam ref
    WHERE ref.ReferenceResourceId = r.ResourceId
      AND (@sourceType IS NULL OR ref.ResourceTypeId = @sourceType)
      AND (@refPath IS NULL OR ref.SearchParamId = @refPath)
  )
```

**Pros:**
- Leverages existing ReferenceSearchParam index
- Single query execution
- Standard SQL pattern

**Cons:**
- NOT EXISTS can be slow without proper indexing
- May need index hints for large datasets

### Option B: Two-Phase Query

1. Find all referenced resource IDs (distinct)
2. Return resources NOT IN that set

```csharp
var referencedIds = await _context.ReferenceSearchParams
    .Where(r => r.ReferenceResourceTypeId == targetTypeId)
    .Select(r => r.ReferenceResourceId)
    .Distinct()
    .ToListAsync();

return await _context.Resources
    .Where(r => r.ResourceTypeId == targetTypeId && !referencedIds.Contains(r.ResourceId))
    .ToListAsync();
```

**Pros:**
- Simpler query logic
- Easier to debug

**Cons:**
- Memory pressure for large ID sets
- Two round-trips

### Option C: Left Anti-Join (Recommended)

Use LEFT JOIN with NULL check (anti-join pattern):

```sql
SELECT r.* FROM Resource r
LEFT JOIN ReferenceSearchParam ref
  ON ref.ReferenceResourceId = r.ResourceId
  AND (@sourceType IS NULL OR ref.ResourceTypeId = @sourceType)
WHERE r.ResourceTypeId = @PatientTypeId
  AND ref.ResourceSurrogateId IS NULL
```

**Pros:**
- Often better query plan than NOT EXISTS
- Single query
- Well-optimized by SQL Server

**Cons:**
- Requires careful NULL handling
- Join may materialize large intermediate result

## Tradeoffs

| Pros | Cons |
|------|------|
| Enables orphan cleanup workflows | Performance impact on large datasets |
| Supports data quality audits | Complex query generation |
| Useful for compliance reporting | Additional index may be needed |
| Microsoft FHIR Server compatible | Wildcard patterns add complexity |

## Alignment

- [x] Follows layer rules - SearchParameterQueryGenerator handles SQL generation
- [x] F5 Developer Experience - Standard search parameter, no config needed
- [ ] FHIR spec compliance - Extension (not in base spec, common pattern)
- [x] Consistent with existing patterns - Extends existing reference search infrastructure

## Evidence

### Microsoft FHIR Server Implementation

From `NotReferencedSearchTests.cs` (4 tests):
1. `_not-referenced=*:*` - Wildcard finds completely orphaned resources
2. `_not-referenced=Observation:*` - Resource type filter
3. `_not-referenced=Observation:subject` - Specific path filter
4. Invalid parameter returns warning, not error (lenient handling)

### Existing Codebase Patterns

Reference search handling in:
- `SearchParameterQueryGenerator.cs` - Reference parameter SQL generation
- `ReferenceSearchParam` entity - Stores reference relationships

### Index Requirements

Current `ReferenceSearchParam` table has:
- `IX_ReferenceSearchParam_ResourceId` on `(ResourceId, SearchParamId)`

May need additional index for target-side queries:
- `IX_ReferenceSearchParam_ReferenceResourceId` on `(ReferenceResourceId, ReferenceResourceTypeId)`

## Alternative Investigations

1. **inverse-references-cache** - Maintain materialized view of reverse references
2. **orphan-detection-job** - Background job that flags orphaned resources
3. **reference-integrity-events** - Event-driven orphan detection on reference delete

## Verdict

**Implemented** - Using Option C (Left Anti-Join) pattern for optimal SQL Server performance. In-memory search is not supported and throws `SearchOperationNotSupportedException`.

## Implementation Summary

The `_not-referenced` search parameter enables finding orphaned FHIR resources that are not referenced by any other resource. This is useful for data cleanup, audit, and data quality workflows.

### Files Created
- `src/Core/Ignixa.Search/Expressions/NotReferencedExpression.cs` - Expression class representing _not-referenced search

### Files Modified
- `src/Core/Ignixa.Search/Expressions/Expression.cs` - Factory method for NotReferencedExpression
- `src/Core/Ignixa.Search/Expressions/IExpressionVisitor.cs` - Visitor interface method
- `src/Core/Ignixa.Search/Expressions/DefaultExpressionVisitor.cs` - Default visitor implementation
- `src/Core/Ignixa.Search/Expressions/ExpressionRewriter.cs` - Rewriter implementation
- `src/Core/Ignixa.Search/InMemory/SearchQueryInterpreter.cs` - In-memory search (not supported, throws)
- `src/Core/Ignixa.Search/Parsing/QueryParameter.cs` - Parameter classification
- `src/Core/Ignixa.Search/Expressions/Parsers/ExpressionParser.cs` - Parsing logic
- `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchParameterQueryGenerator.cs` - Query generation
- `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs` - Expression handling

### Query Pattern

Pre-filtering `ReferenceSearchParams` before the join improves performance on large tables:

```csharp
var filteredRefs = from rp in _context.ReferenceSearchParams
                   where (!sourceTypeId.HasValue || rp.ResourceTypeId == sourceTypeId.Value)
                       && (!searchParamId.HasValue || rp.SearchParamId == searchParamId.Value)
                   select rp;

var query = from r in _context.Resources
            where (!resourceTypeId.HasValue || r.ResourceTypeId == resourceTypeId.Value)
                && !r.IsHistory && !r.IsDeleted
            join refParam in filteredRefs
                on new { ReferenceResourceId = r.ResourceId, ReferenceResourceTypeId = (short?)r.ResourceTypeId }
                equals new { refParam.ReferenceResourceId, refParam.ReferenceResourceTypeId }
                into refGroup
            from refParam in refGroup.DefaultIfEmpty()
            where refParam == null
            select r.ResourceSurrogateId;
```

### Usage Examples

```http
# Find all patients not referenced by any resource
GET /Patient?_not-referenced=*:*

# Find patients not referenced by any Observation
GET /Patient?_not-referenced=Observation:*

# Find patients not referenced by Observation.subject specifically
GET /Patient?_not-referenced=Observation:subject
```

### Performance Characteristics

- **SQL Query Pattern**: Uses LEFT ANTI-JOIN which SQL Server query optimizer handles efficiently
- **Index Utilization**: Leverages existing `ReferenceSearchParam` indexes for optimal performance
- **Recommended Index**: For large datasets (>1M resources), consider adding index on `(ReferenceResourceId, ReferenceResourceTypeId)` to improve join performance
- **In-Memory Search**: Not supported - throws `SearchOperationNotSupportedException` when using in-memory search provider

### Supported Patterns

| Pattern | SQL Storage | In-Memory Search |
|---------|-------------|------------------|
| `_not-referenced=*:*` | Supported | Not Supported |
| `_not-referenced=ResourceType:*` | Supported | Not Supported |
| `_not-referenced=ResourceType:path` | Supported | Not Supported |
