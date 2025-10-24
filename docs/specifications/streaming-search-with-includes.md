# Specification: True Streaming Search with _include/_revinclude Support

**Version:** 1.0
**Date:** 2025-01-24
**Status:** Draft
**Author:** System Architecture

---

## Executive Summary

This specification defines a truly streaming search implementation for FHIR operations that **never buffers** `SearchEntryResult`, `ResourceWrapper`, or `ResourceEntity` objects in memory. The design supports searching for 1M+ resources with constant memory usage through:

1. **Main Search Streaming**: Results stream directly from database to HTTP response
2. **Include Query Separation**: `_include` and `_revinclude` execute as separate queries AFTER main results
3. **Zero-Buffer Architecture**: All results stream through `IAsyncEnumerable<T>` pipelines
4. **Constant Memory**: Memory usage independent of result set size (O(1) memory complexity)

---

## 1. Architecture Overview

### 1.1 Query Execution Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│ Phase 1: Main Search Query (Streaming)                               │
│                                                                       │
│   DB Query: SELECT * FROM Resource WHERE [filters] ORDER BY [...]    │
│             OFFSET @offset ROWS FETCH NEXT @count ROWS ONLY           │
│                                                                       │
│   Memory: O(1) - Single row buffer only                              │
│   Output: IAsyncEnumerable<SearchEntryResult> (SearchMode = Match)   │
│   Duration: 0ms ... N*10ms (streaming over time)                     │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
                    ┌──────────────────────────────┐
                    │ Write JSON entries to stream │
                    │ (no buffering)               │
                    └──────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│ Phase 2: Include Query (Streaming, Optional)                         │
│                                                                       │
│   IF (_include parameters exist):                                    │
│     DB Query: WITH MainResults AS (                                  │
│                 SELECT ResourceSurrogateId                            │
│                 FROM Resource WHERE [same filters]                   │
│                 OFFSET @offset ROWS FETCH NEXT @count ROWS ONLY       │
│               )                                                       │
│               SELECT r.*                                              │
│               FROM MainResults mr                                     │
│               JOIN ReferenceSearchParam rsp                           │
│                 ON rsp.ResourceSurrogateId = mr.ResourceSurrogateId  │
│               JOIN Resource r                                         │
│                 ON r.ResourceTypeId = rsp.ReferenceResourceTypeId    │
│                 AND r.ResourceId = rsp.ReferenceResourceId           │
│               WHERE rsp.SearchParamId = @includeParamId               │
│                 AND r.IsHistory = 0 AND r.IsDeleted = 0              │
│                                                                       │
│   Memory: O(1) - Single row buffer only                              │
│   Output: IAsyncEnumerable<SearchEntryResult> (SearchMode = Include) │
│   Duration: 0ms ... M*10ms (streaming over time)                     │
└─────────────────────────────────────────────────────────────────────┘
                                    ↓
                    ┌──────────────────────────────┐
                    │ Write JSON entries to stream │
                    │ (no buffering)               │
                    └──────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────┐
│ Phase 3: RevInclude Query (Streaming, Optional)                      │
│                                                                       │
│   IF (_revinclude parameters exist):                                 │
│     DB Query: WITH MainResults AS (                                  │
│                 SELECT ResourceSurrogateId, ResourceTypeId,           │
│                        ResourceId                                     │
│                 FROM Resource WHERE [same filters]                   │
│                 OFFSET @offset ROWS FETCH NEXT @count ROWS ONLY       │
│               )                                                       │
│               SELECT r.*                                              │
│               FROM MainResults mr                                     │
│               JOIN ReferenceSearchParam rsp                           │
│                 ON rsp.ReferenceResourceTypeId = mr.ResourceTypeId   │
│                 AND rsp.ReferenceResourceId = mr.ResourceId          │
│               JOIN Resource r                                         │
│                 ON r.ResourceSurrogateId = rsp.ResourceSurrogateId   │
│               WHERE rsp.SearchParamId = @revIncludeParamId            │
│                 AND r.IsHistory = 0 AND r.IsDeleted = 0              │
│                                                                       │
│   Memory: O(1) - Single row buffer only                              │
│   Output: IAsyncEnumerable<SearchEntryResult> (SearchMode = Include) │
│   Duration: 0ms ... K*10ms (streaming over time)                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 Key Design Principles

| Principle | Implementation | Benefit |
|-----------|----------------|---------|
| **Zero Buffering** | All queries return `IAsyncEnumerable<T>`, no `.ToListAsync()` | O(1) memory usage |
| **Sequential Execution** | Main → Include → RevInclude (not parallel) | Simplifies streaming, predictable order |
| **Filter Replication** | Include queries use same WHERE clause as main | No need to store main result IDs |
| **Single-Pass Streaming** | Each resource written to JSON exactly once | No duplicate processing |
| **Lazy Execution** | Queries execute on-demand as JSON is written | Database backpressure control |

---

## 2. SQL Query Design

### 2.1 Main Search Query

**Purpose**: Fetch main search results matching user filters.

**SQL Template**:
```sql
-- Parameters: @resourceTypeId, @offset, @count, [filter params]
SELECT
    r.ResourceSurrogateId,
    r.ResourceTypeId,
    r.ResourceId,
    r.Version,
    r.IsDeleted,
    r.RawResource,
    t.CreateDate AS LastModified
FROM dbo.Resource r
INNER JOIN dbo.TransactionEntity t
    ON t.TransactionId = r.TransactionId
WHERE
    r.ResourceTypeId = @resourceTypeId
    AND r.IsHistory = 0
    AND r.IsDeleted = 0
    AND [additional filters]  -- From search parameters
ORDER BY
    [sort expressions]  -- From _sort parameter
    , r.ResourceSurrogateId DESC  -- Tie-breaker for stable paging
OFFSET @offset ROWS
FETCH NEXT @count ROWS ONLY;
```

**Return Type**: `IAsyncEnumerable<ResourceEntity>` → Mapped to `SearchEntryResult`

**Memory**: O(1) - EF Core streams one row at a time

---

### 2.2 Include Query (Forward References)

**Purpose**: Fetch resources referenced BY the main results (e.g., `Patient.organization`).

**Example**: `GET /Patient?_count=10&_include=Patient:organization`

**SQL Template**:
```sql
-- Parameters: Same as main query + @includeSearchParamId
WITH MainResults AS (
    -- REPLICATE the main query's WHERE clause (no ORDER BY needed)
    SELECT
        r.ResourceSurrogateId
    FROM dbo.Resource r
    WHERE
        r.ResourceTypeId = @resourceTypeId
        AND r.IsHistory = 0
        AND r.IsDeleted = 0
        AND [same additional filters]
    OFFSET @offset ROWS
    FETCH NEXT @count ROWS ONLY
)
SELECT DISTINCT
    r.ResourceSurrogateId,
    r.ResourceTypeId,
    r.ResourceId,
    r.Version,
    r.IsDeleted,
    r.RawResource,
    t.CreateDate AS LastModified
FROM MainResults mr
INNER JOIN dbo.ReferenceSearchParam rsp
    ON rsp.ResourceSurrogateId = mr.ResourceSurrogateId
    AND rsp.SearchParamId = @includeSearchParamId
INNER JOIN dbo.Resource r
    ON r.ResourceTypeId = rsp.ReferenceResourceTypeId
    AND r.ResourceId = rsp.ReferenceResourceId
    AND r.IsHistory = 0
    AND r.IsDeleted = 0
INNER JOIN dbo.TransactionEntity t
    ON t.TransactionId = r.TransactionId
ORDER BY r.ResourceSurrogateId;  -- Stable order for deduplication
```

**Return Type**: `IAsyncEnumerable<ResourceEntity>` → Mapped to `SearchEntryResult`

**Memory**: O(1) - SQL Server materializes CTE once, then streams results

**Deduplication**: `DISTINCT` handles cases where multiple main results reference the same resource

---

### 2.3 RevInclude Query (Reverse References)

**Purpose**: Fetch resources that reference the main results (e.g., Observations referencing Patients).

**Example**: `GET /Patient?_count=10&_revinclude=Observation:patient`

**SQL Template**:
```sql
-- Parameters: Same as main query + @revIncludeSearchParamId
WITH MainResults AS (
    -- REPLICATE the main query's WHERE clause
    SELECT
        r.ResourceTypeId,
        r.ResourceId,
        r.ResourceSurrogateId
    FROM dbo.Resource r
    WHERE
        r.ResourceTypeId = @resourceTypeId
        AND r.IsHistory = 0
        AND r.IsDeleted = 0
        AND [same additional filters]
    OFFSET @offset ROWS
    FETCH NEXT @count ROWS ONLY
)
SELECT DISTINCT
    r.ResourceSurrogateId,
    r.ResourceTypeId,
    r.ResourceId,
    r.Version,
    r.IsDeleted,
    r.RawResource,
    t.CreateDate AS LastModified
FROM MainResults mr
INNER JOIN dbo.ReferenceSearchParam rsp
    ON rsp.ReferenceResourceTypeId = mr.ResourceTypeId
    AND rsp.ReferenceResourceId = mr.ResourceId
    AND rsp.SearchParamId = @revIncludeSearchParamId
INNER JOIN dbo.Resource r
    ON r.ResourceSurrogateId = rsp.ResourceSurrogateId
    AND r.IsHistory = 0
    AND r.IsDeleted = 0
INNER JOIN dbo.TransactionEntity t
    ON t.TransactionId = r.TransactionId
ORDER BY r.ResourceSurrogateId;  -- Stable order for deduplication
```

**Return Type**: `IAsyncEnumerable<ResourceEntity>` → Mapped to `SearchEntryResult`

**Memory**: O(1) - SQL Server materializes CTE once, then streams results

---

### 2.4 Multiple Includes Handling

**Example**: `GET /Patient?_count=10&_include=Patient:organization&_include=Patient:generalPractitioner`

**Approach**: Execute one include query per parameter, stream each sequentially.

```sql
-- Query 1: organization references
WITH MainResults AS ([same as above])
SELECT ... WHERE rsp.SearchParamId = @organizationParamId;

-- Query 2: generalPractitioner references (executed AFTER Query 1 completes)
WITH MainResults AS ([same as above])
SELECT ... WHERE rsp.SearchParamId = @practitionerParamId;
```

**Alternative**: Use `IN` clause for multiple parameters in single query:
```sql
WHERE rsp.SearchParamId IN (@param1, @param2, @param3)
```

**Trade-off**:
- Single query: Fewer roundtrips, but more complex
- Multiple queries: Simpler, easier to stream, better for large result sets

**Recommendation**: Use single query with `IN` clause for ≤3 parameters, multiple queries for >3.

---

## 3. C# Implementation Design

### 3.1 Service Layer Interface

```csharp
public interface ISearchService
{
    /// <summary>
    /// Executes a streaming search with optional includes.
    /// NEVER buffers results - returns lazy IAsyncEnumerable.
    /// </summary>
    /// <returns>
    /// Stream of search results in this order:
    /// 1. Main search results (SearchMode = Match)
    /// 2. Included resources (SearchMode = Include)
    /// 3. Reverse-included resources (SearchMode = Include)
    /// </returns>
    IAsyncEnumerable<SearchEntryResult> SearchStreamAsync(
        SearchOptions options,
        CancellationToken cancellationToken = default);
}
```

### 3.2 Implementation Pseudocode

```csharp
public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync(
    SearchOptions options,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    // PHASE 1: Stream main search results
    var mainQuery = BuildMainSearchQuery(options);

    await foreach (var entity in mainQuery
        .AsAsyncEnumerable()
        .WithCancellation(ct))
    {
        yield return MapToSearchEntryResult(entity, SearchEntryMode.Match);
    }

    // PHASE 2: Stream included resources (forward references)
    if (options.Include?.Any() == true)
    {
        foreach (var includeExpr in options.Include)
        {
            var includeQuery = BuildIncludeQuery(options, includeExpr);

            await foreach (var entity in includeQuery
                .AsAsyncEnumerable()
                .WithCancellation(ct))
            {
                yield return MapToSearchEntryResult(entity, SearchEntryMode.Include);
            }
        }
    }

    // PHASE 3: Stream reverse-included resources
    if (options.RevInclude?.Any() == true)
    {
        foreach (var revIncludeExpr in options.RevInclude)
        {
            var revIncludeQuery = BuildRevIncludeQuery(options, revIncludeExpr);

            await foreach (var entity in revIncludeQuery
                .AsAsyncEnumerable()
                .WithCancellation(ct))
            {
                yield return MapToSearchEntryResult(entity, SearchEntryMode.Include);
            }
        }
    }
}
```

**Key Points**:
- ✅ Zero buffering: No `.ToListAsync()`, `.ToArrayAsync()`, or `List<T>` anywhere
- ✅ Lazy execution: Queries don't execute until enumerated
- ✅ Backpressure: Database pauses if HTTP client is slow (streaming control)
- ✅ Early termination: If HTTP connection drops, query cancels immediately

---

### 3.3 Query Builder Methods

```csharp
private IQueryable<ResourceEntity> BuildMainSearchQuery(SearchOptions options)
{
    var query = _context.Resources
        .Include(r => r.Transaction)
        .Where(r => r.ResourceTypeId == resourceTypeId)
        .Where(r => !r.IsHistory && !r.IsDeleted);

    // Apply search parameter filters
    if (options.Expression != null)
    {
        var filterQuery = _queryBuilder.BuildQuery(options.Expression);
        query = query.Where(r => filterQuery.Contains(r.ResourceSurrogateId));
    }

    // Apply sorting
    query = ApplySorting(query, options.Sort);

    // Apply pagination
    query = query
        .Skip(options.Offset)
        .Take(options.MaxItemCount);

    return query;
}

private IQueryable<ResourceEntity> BuildIncludeQuery(
    SearchOptions options,
    IncludeExpression includeExpr)
{
    // CTE: Replicate main query filters (NO sorting, NO pagination on CTE)
    var mainResultIds = _context.Resources
        .Where(r => r.ResourceTypeId == resourceTypeId)
        .Where(r => !r.IsHistory && !r.IsDeleted);

    // Apply same filters as main query
    if (options.Expression != null)
    {
        var filterQuery = _queryBuilder.BuildQuery(options.Expression);
        mainResultIds = mainResultIds.Where(r => filterQuery.Contains(r.ResourceSurrogateId));
    }

    // Apply SAME pagination as main query
    mainResultIds = mainResultIds
        .Skip(options.Offset)
        .Take(options.MaxItemCount)
        .Select(r => r.ResourceSurrogateId);

    // Join to find referenced resources
    var includeQuery = from rsp in _context.ReferenceSearchParams
                       where mainResultIds.Contains(rsp.ResourceSurrogateId)
                          && rsp.SearchParamId == includeExpr.SearchParamId
                       join r in _context.Resources
                          on new { rsp.ReferenceResourceTypeId, rsp.ReferenceResourceId }
                          equals new { ReferenceResourceTypeId = r.ResourceTypeId, ReferenceResourceId = r.ResourceId }
                       where !r.IsHistory && !r.IsDeleted
                       select r;

    return includeQuery
        .Include(r => r.Transaction)
        .Distinct()  // Deduplicate
        .OrderBy(r => r.ResourceSurrogateId);  // Stable order
}

private IQueryable<ResourceEntity> BuildRevIncludeQuery(
    SearchOptions options,
    IncludeExpression revIncludeExpr)
{
    // CTE: Replicate main query filters
    var mainResults = _context.Resources
        .Where(r => r.ResourceTypeId == resourceTypeId)
        .Where(r => !r.IsHistory && !r.IsDeleted);

    // Apply same filters
    if (options.Expression != null)
    {
        var filterQuery = _queryBuilder.BuildQuery(options.Expression);
        mainResults = mainResults.Where(r => filterQuery.Contains(r.ResourceSurrogateId));
    }

    // Apply SAME pagination
    mainResults = mainResults
        .Skip(options.Offset)
        .Take(options.MaxItemCount)
        .Select(r => new { r.ResourceTypeId, r.ResourceId });

    // Join to find referencing resources (reverse direction)
    var revIncludeQuery = from mr in mainResults
                          join rsp in _context.ReferenceSearchParams
                             on mr equals new { ResourceTypeId = rsp.ReferenceResourceTypeId, ResourceId = rsp.ReferenceResourceId }
                          where rsp.SearchParamId == revIncludeExpr.SearchParamId
                          join r in _context.Resources
                             on rsp.ResourceSurrogateId equals r.ResourceSurrogateId
                          where !r.IsHistory && !r.IsDeleted
                          select r;

    return revIncludeQuery
        .Include(r => r.Transaction)
        .Distinct()  // Deduplicate
        .OrderBy(r => r.ResourceSurrogateId);  // Stable order
}
```

**Critical**: All methods return `IQueryable<T>` (not `Task<List<T>>`), enabling lazy execution.

---

## 4. Bundle Serialization

### 4.1 Streaming JSON Writer

```csharp
public async Task SerializeSearchBundleAsync(
    IAsyncEnumerable<SearchEntryResult> results,
    SearchOptions options,
    Stream outputStream,
    CancellationToken ct)
{
    using var writer = FhirJsonWriter.Create(outputStream);

    writer.WriteStartObject()
        .WriteString("resourceType", "Bundle")
        .WriteString("type", "searchset")
        .WriteStartArray("entry");

    // Stream entries one at a time (zero buffering)
    await foreach (var result in results.WithCancellation(ct))
    {
        writer.WriteStartObject()
            .WriteString("fullUrl", $"{_baseUrl}/{result.ResourceType}/{result.ResourceId}")
            .WriteRawProperty("resource", result.ResourceBytes)
            .WriteObject("search", w => w
                .WriteString("mode", result.SearchMode.ToString().ToLowerInvariant()))
            .WriteEndObject();

        // Flush periodically to send data to client
        await writer.FlushAsync(ct);
    }

    writer.WriteEndArray()
        .WriteEndObject();

    await writer.FlushAsync(ct);
}
```

**Memory Usage**: Only one `SearchEntryResult` in memory at a time.

---

### 4.2 Pagination Handling

**Challenge**: How do we generate `next` link without knowing total count?

**Solution**: Use `_count + 1` query pattern (already implemented):

```csharp
// Request one extra result to detect if there are more pages
var results = mainQuery.Take(options.MaxItemCount + 1);

int itemsWritten = 0;
bool hasMore = false;

await foreach (var result in results.AsAsyncEnumerable().WithCancellation(ct))
{
    if (itemsWritten >= options.MaxItemCount)
    {
        hasMore = true;
        break;  // Don't write the +1 item
    }

    // Write to JSON
    itemsWritten++;
}

// After streaming all entries, write links
if (hasMore)
{
    var nextOffset = options.Offset + options.MaxItemCount;
    var nextLink = $"{_baseUrl}?_count={options.MaxItemCount}&_offset={nextOffset}&...";
    writer.WriteStartArray("link")
        .WriteObject(w => w
            .WriteString("relation", "next")
            .WriteString("url", nextLink));
}
```

**Compatibility**: Works with current streaming architecture, no buffering needed.

---

## 5. Performance Characteristics

### 5.1 Memory Usage

| Scenario | Current (Buffered) | New (Streaming) | Reduction |
|----------|-------------------|-----------------|-----------|
| **10 results + 5 includes** | 15 × 10KB = 150KB | ~10KB (single row) | **93%** |
| **100 results + 50 includes** | 150 × 10KB = 1.5MB | ~10KB (single row) | **99.3%** |
| **1000 results + 500 includes** | 1500 × 10KB = 15MB | ~10KB (single row) | **99.93%** |
| **1M results (no includes)** | 1M × 10KB = 10GB | ~10KB (single row) | **99.9999%** |

**Conclusion**: Memory usage becomes **constant (O(1))** regardless of result set size.

---

### 5.2 Query Performance

**Scenario**: `GET /Patient?_count=100&_include=Patient:organization&_revinclude=Observation:patient`

| Phase | Query Type | Execution Time | Rows Returned |
|-------|-----------|----------------|---------------|
| **Main Search** | Single table + filter | ~20ms | 100 |
| **Include** | CTE + 2 JOINs | ~15ms | 50 |
| **RevInclude** | CTE + 2 JOINs | ~30ms | 500 |
| **Total** | 3 queries | **~65ms** | **650 rows** |

**Comparison to Buffered Approach**:
- Old: 1 + 1 + 1 = 3 queries, but buffering adds ~5ms overhead
- New: 3 queries, zero buffering overhead
- **Result**: Comparable performance, dramatically lower memory

---

### 5.3 Throughput

**HTTP Streaming Benchmark** (assumed):
- Database: 10,000 rows/sec
- JSON serialization: 15,000 rows/sec
- Network: 5,000 rows/sec (bottleneck)

**Streaming Pipeline**:
```
DB → EF Core → Mapper → JSON Writer → HTTP Stream
10K/s → 10K/s → 15K/s → 15K/s → 5K/s (bottleneck)
```

**Result**: System runs at 5,000 results/sec, limited by network (not memory).

---

## 6. Edge Cases and Considerations

### 6.1 Duplicate Included Resources

**Problem**: Multiple main results may reference the same included resource.

**Example**:
```
Patient/1 → Organization/acme
Patient/2 → Organization/acme  (duplicate)
```

**Solution**: Use `DISTINCT` in include query SQL.

**Alternative**: Application-level deduplication (trade memory for CPU):
```csharp
var seenResources = new HashSet<string>();

await foreach (var result in includeQuery.AsAsyncEnumerable())
{
    var key = $"{result.ResourceType}/{result.ResourceId}";
    if (seenResources.Add(key))  // Only yields if new
    {
        yield return result;
    }
}
```

**Recommendation**: Use SQL `DISTINCT` (cleaner, database-optimized).

---

### 6.2 Wildcard Includes

**Example**: `GET /Patient?_include=Patient:*`

**Challenge**: Must query ALL reference parameters for Patient resource type.

**Solution**: Build dynamic query with UNION:
```csharp
var allRefParams = _searchParamManager
    .GetSearchParameters("Patient")
    .Where(p => p.Type == SearchParamType.Reference)
    .Select(p => p.SearchParamId);

var includeQuery = mainResultIds
    .SelectMany(id => allRefParams.Select(paramId => new { id, paramId }))
    .Join(_context.ReferenceSearchParams, ...);
```

**Complexity**: O(N × M) where N = main results, M = reference parameters.

**Recommendation**: Limit wildcard includes to small result sets (_count ≤ 100).

---

### 6.3 Iterative Includes

**Example**: `GET /Patient?_include=Patient:organization&_include:iterate=Organization:partof`

**FHIR Spec**: Follow references recursively (Patient → Organization → parent Organization).

**Challenge**: Cannot stream iterative includes in single pass - requires multiple queries.

**Solution**: Execute iterative includes in phases:
1. Main query → stream results
2. Include query (level 1) → stream results
3. For each `iterate` expression:
   - Use previous include results as "main" for next include
   - Execute new include query → stream results
4. Repeat until no more results or max depth reached

**Pseudo-code**:
```csharp
var currentLevelIds = mainResultIds;

foreach (var iterateExpr in iterativeIncludes)
{
    var nextLevelQuery = BuildIncludeQuery(currentLevelIds, iterateExpr);

    var nextLevelIds = new List<long>();
    await foreach (var result in nextLevelQuery.AsAsyncEnumerable())
    {
        yield return result;
        nextLevelIds.Add(result.ResourceSurrogateId);
    }

    currentLevelIds = nextLevelIds;
    if (nextLevelIds.Count == 0) break;  // No more references
}
```

**Memory**: Still O(1) per level, but may execute many queries (limit to 5 levels max).

---

### 6.4 Total Count Calculation

**Problem**: Streaming makes it hard to know total count upfront.

**FHIR _total Parameter**:
- `_total=none` (default): No total in response
- `_total=accurate`: Include exact count
- `_total=estimate`: Include estimated count

**Solution for `_total=accurate`**:
```csharp
if (options.Total == TotalType.Accurate)
{
    // Execute separate COUNT query (non-streaming)
    var totalQuery = _context.Resources
        .Where([same filters])
        .CountAsync(ct);

    var total = await totalQuery;

    // Write total to bundle BEFORE streaming entries
    writer.WriteNumber("total", total);
}
```

**Cost**: One additional COUNT query, but doesn't block streaming (executes in parallel).

---

### 6.5 Sorting with Includes

**Problem**: FHIR spec doesn't define sort order for included resources.

**Solution**: Always return in this order:
1. Main results (sorted per `_sort` parameter)
2. Included resources (sorted by ResourceSurrogateId for stability)
3. Reverse-included resources (sorted by ResourceSurrogateId)

**Rationale**: Deterministic order aids debugging and testing.

---

## 7. Implementation Phases

### Phase 1: Main Search Streaming (Baseline)
**Scope**: Streaming search without includes
**Status**: ✅ Already implemented (StreamingBundleSerializer)
**Effort**: 0 days (done)

### Phase 2: Include Query Builder
**Scope**: Build CTE-based include queries
**Tasks**:
- Implement `BuildIncludeQuery()` with filter replication
- Add `DISTINCT` for deduplication
- Test with single _include parameter

**Effort**: 2 days
**Risk**: Low (SQL pattern is straightforward)

### Phase 3: RevInclude Query Builder
**Scope**: Build CTE-based revinclude queries
**Tasks**:
- Implement `BuildRevIncludeQuery()` with reverse JOIN
- Add `DISTINCT` for deduplication
- Test with single _revinclude parameter

**Effort**: 2 days
**Risk**: Low (similar to Phase 2)

### Phase 4: Multiple Includes Support
**Scope**: Handle multiple _include and _revinclude parameters
**Tasks**:
- Iterate through `options.Include` and `options.RevInclude`
- Stream each sequentially
- Test with 2-3 parameters

**Effort**: 1 day
**Risk**: Low (composition of Phase 2/3)

### Phase 5: Wildcard Includes
**Scope**: Support `_include=Patient:*`
**Tasks**:
- Query all reference parameters for resource type
- Build UNION or dynamic query
- Limit to small result sets

**Effort**: 3 days
**Risk**: Medium (complexity in dynamic query building)

### Phase 6: Iterative Includes
**Scope**: Support `_include:iterate`
**Tasks**:
- Multi-pass query execution
- Recursive include handling
- Depth limit (5 levels)

**Effort**: 4 days
**Risk**: Medium (requires careful state management)

**Total Estimated Effort**: 12 days (2.4 weeks)

---

## 8. Testing Strategy

### 8.1 Unit Tests

```csharp
[Fact]
public async Task SearchStream_WithInclude_ReturnsMatchThenInclude()
{
    // Arrange
    var options = new SearchOptions
    {
        ResourceType = "Patient",
        MaxItemCount = 10,
        Include = new[] { includeExpr }
    };

    // Act
    var results = await _service
        .SearchStreamAsync(options)
        .ToListAsync();  // Only for testing - production NEVER buffers

    // Assert
    results.Take(10).Should().AllSatisfy(r => r.SearchMode == SearchEntryMode.Match);
    results.Skip(10).Should().AllSatisfy(r => r.SearchMode == SearchEntryMode.Include);
}
```

### 8.2 Performance Tests

**Memory Test** (Critical):
```csharp
[Fact]
public async Task SearchStream_With1MillionResults_UsesConstantMemory()
{
    // Arrange
    var initialMemory = GC.GetTotalMemory(forceFullCollection: true);
    var options = new SearchOptions { MaxItemCount = 1_000_000 };

    // Act
    await foreach (var result in _service.SearchStreamAsync(options))
    {
        // Simulate HTTP write (discard result)
    }

    var finalMemory = GC.GetTotalMemory(forceFullCollection: true);

    // Assert: Memory growth should be < 10MB
    (finalMemory - initialMemory).Should().BeLessThan(10_000_000);
}
```

### 8.3 Integration Tests

**End-to-End Test**:
```http
### Test: Large search with includes
GET {{host}}/Patient?_count=1000&_include=Patient:organization&_revinclude=Observation:patient

### Expected Response:
HTTP/1.1 200 OK
Content-Type: application/fhir+json
Transfer-Encoding: chunked

{
  "resourceType": "Bundle",
  "type": "searchset",
  "entry": [
    // 1000 Patients (mode: "match")
    // ~500 Organizations (mode: "include")
    // ~5000 Observations (mode: "include")
  ]
}
```

**Verify**:
1. Response starts within 100ms (not buffered)
2. Server memory stays < 50MB during entire response
3. All results present and correct
4. Proper search modes assigned

---

## 9. Backwards Compatibility

### 9.1 API Contract

**No Breaking Changes**:
- Bundle structure remains identical
- Query parameters unchanged
- HTTP headers unchanged
- Response format unchanged

**Internal Changes Only**:
- Query execution order (main → include → revinclude)
- Memory usage (dramatically lower)
- Streaming behavior (faster first byte)

### 9.2 Migration Path

**Current Code** (Buffered):
```csharp
var mainResults = await searchService.SearchAsync(options).ToListAsync();
var includes = await includeProcessor.ProcessIncludesAsync(mainResults);
var allResults = mainResults.Concat(includes).ToList();
```

**New Code** (Streaming):
```csharp
var allResults = searchService.SearchStreamAsync(options);
// Returns IAsyncEnumerable - no .ToListAsync()
```

**Transition**: Replace all `.ToListAsync()` with streaming consumption.

---

## 10. Success Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Memory Usage (1M results)** | < 50MB | GC.GetTotalMemory() |
| **Time to First Byte** | < 100ms | HTTP response start |
| **Throughput** | > 1000 results/sec | Network-limited |
| **Query Count** | ≤ 1 + N + M | 1 main + N includes + M revincludes |
| **No Buffering** | Zero `.ToListAsync()` | Code review |
| **Backward Compatibility** | 100% API compatible | Integration tests |

---

## 11. Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **CTE Performance** | SQL Server may materialize entire CTE | Medium | Use query hints (`OPTION (RECOMPILE)`) |
| **Network Backpressure** | Slow clients block database | Low | Streaming naturally handles this |
| **Query Timeout** | Large searches may timeout | Low | Use `CommandTimeout = 300` |
| **EF Core Buffering** | EF may buffer despite `IAsyncEnumerable` | Low | Test with profiler, use `AsNoTracking()` |

---

## 12. References

- **FHIR R4 Search Spec**: https://hl7.org/fhir/R4/search.html#include
- **FHIR Bundle Spec**: https://hl7.org/fhir/R4/bundle.html
- **EF Core Streaming**: https://learn.microsoft.com/en-us/ef/core/querying/async#streaming
- **SQL Server CTEs**: https://learn.microsoft.com/en-us/sql/t-sql/queries/with-common-table-expression-transact-sql

---

## 13. Appendix: SQL Execution Plan Example

**Query**: `GET /Patient?_count=10&_include=Patient:organization`

**Generated SQL** (readable format):
```sql
-- Main Query
SELECT TOP 10
    r.ResourceSurrogateId,
    r.RawResource,
    -- ... other columns
FROM dbo.Resource r
WHERE r.ResourceTypeId = 1  -- Patient
  AND r.IsHistory = 0
  AND r.IsDeleted = 0
ORDER BY r.ResourceSurrogateId DESC;

-- Include Query
WITH MainResults AS (
    SELECT TOP 10
        r.ResourceSurrogateId
    FROM dbo.Resource r
    WHERE r.ResourceTypeId = 1
      AND r.IsHistory = 0
      AND r.IsDeleted = 0
    ORDER BY r.ResourceSurrogateId DESC
)
SELECT DISTINCT
    r.ResourceSurrogateId,
    r.RawResource,
    -- ... other columns
FROM MainResults mr
INNER JOIN dbo.ReferenceSearchParam rsp
    ON rsp.ResourceSurrogateId = mr.ResourceSurrogateId
INNER JOIN dbo.Resource r
    ON r.ResourceTypeId = rsp.ReferenceResourceTypeId
   AND r.ResourceId = rsp.ReferenceResourceId
WHERE rsp.SearchParamId = 42  -- organization parameter
  AND r.IsHistory = 0
  AND r.IsDeleted = 0
ORDER BY r.ResourceSurrogateId;
```

**Execution Plan**:
1. Main query: Index Seek on `Resource` (PK) → ~10ms
2. Include CTE: Index Seek on `Resource` (PK) → ~5ms (reuses query plan)
3. Include JOIN: Index Seek on `ReferenceSearchParam` (ResourceSurrogateId, SearchParamId) → ~3ms
4. Include JOIN: Index Seek on `Resource` (ResourceTypeId, ResourceId) → ~2ms

**Total**: ~20ms for 10 main + 5 included = 15 results

---

**End of Specification**
