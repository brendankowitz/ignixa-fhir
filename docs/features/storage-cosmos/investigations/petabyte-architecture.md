# Investigation: Cosmos DB 10PB+ FHIR Storage Architecture

**Feature**: cosmos-storage
**Status**: Viable
**Created**: 2025-10-22
**Original ADR**: N/A

This document proposes a Cosmos DB storage architecture designed to handle >10PB of FHIR data with optimal performance, based on separated resource and search data patterns.

## Executive Summary

**Architecture Goals:**
- Support >10PB of FHIR data with sub-second read performance
- Optimize for fast key-based resource reads
- Compact search indices for efficient cross-partition searches
- Work within Cosmos SDK limitations (500 parallel partitions)
- Implement proven optimization patterns from issue #2686

**Key Design Principles:**
1. **Separation of Concerns**: Raw resource data separate from search indices
2. **Partition Optimization**: Smart partitioning to maximize throughput and minimize hot spots
3. **Search Compaction**: Minimal search data to reduce RU costs and improve performance
4. **Key-Based Access**: All primary reads use partition key + id for O(1) performance

## Architecture Overview

### Container Design Pattern

```
┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────────┐
│   Resources         │  │   SearchIndices     │  │   SearchAggregates  │
│   Container         │  │   Container         │  │   Container         │
├─────────────────────┤  ├─────────────────────┤  ├─────────────────────┤
│ • Raw FHIR JSON     │  │ • Compact indices   │  │ • Cross-partition   │
│ • 20GB per logical  │  │ • Query-optimized   │  │   search results    │
│   partition         │  │ • Parameter-keyed   │  │ • Cached counts     │
│ • Key-based reads   │  │ • Range queries     │  │ • Statistics        │
└─────────────────────┘  └─────────────────────┘  └─────────────────────┘
```

## Container 1: Resources (Primary Data Store)

### Purpose
Store complete FHIR resources with optimal read performance for known resource IDs.

### Document Structure
```json
{
  "id": "Patient-123-abc-def",
  "pk": "Patient|2024-12|tenant-1",
  "resourceType": "Patient",
  "resourceId": "123-abc-def",
  "tenantId": "tenant-1",
  "versionId": "1",
  "lastModified": "2024-12-07T10:30:00Z",
  "resource": { /* Complete FHIR Resource JSON */ },
  "meta": {
    "size": 2048,
    "hash": "sha256:abc123...",
    "compressed": false
  },
  "_etag": "W/\"datetime'2024-12-07T10%3A30%3A00.0000000Z'\""
}
```

### Partitioning Strategy
```csharp
// Partition Key: ResourceType|Year-Month|TenantId
public static string CreateResourcePartitionKey(string resourceType, string tenantId, DateTime timestamp)
{
    return $"{resourceType}|{timestamp:yyyy-MM}|{tenantId}";
}
```

**Benefits:**
- Even distribution across time periods
- Tenant isolation at partition level
- Natural data lifecycle management
- Supports 20GB per logical partition (≈10,000 average FHIR resources)

### Scale Calculations
```
Resources per month per tenant: ~100,000 average
Average resource size: 2KB
Monthly data per tenant per resource type: ~200MB
Annual data per tenant per resource type: ~2.4GB
10PB total = ~4,166 tenant-years of data
```

## Container 2: SearchIndices (Optimized Query Store)

### Purpose
Store compact, query-optimized search indices using the proven optimization from issue #2686.

### Optimized Document Structure (Based on #2686 Pattern)
```json
{
  "id": "Patient-123-abc-def-search",
  "pk": "Patient|active|tenant-1",
  "resourceType": "Patient",
  "resourceId": "123-abc-def",
  "tenantId": "tenant-1",
  "lastModified": "2024-12-07T10:30:00Z",

  // Optimized search structure (parameter as top-level key)
  "searchIndices": {
    "active": [
      { "st": "token", "c": "true" }
    ],
    "birthdate": [
      { "st": "date", "sv": "1990-01-01", "ev": "1990-01-01" }
    ],
    "name": [
      { "st": "string", "sv": "john", "ev": "john" },
      { "st": "string", "sv": "doe", "ev": "doe" }
    ],
    "_lastUpdated": [
      { "st": "timestamp", "sv": "2024-12-07T10:30:00Z", "ev": "2024-12-07T10:30:00Z" }
    ]
  },

  // Compact resource reference
  "resourceRef": {
    "container": "resources",
    "pk": "Patient|2024-12|tenant-1",
    "id": "Patient-123-abc-def"
  }
}
```

### Partitioning Strategy for Search
```csharp
// Partition Key: ResourceType|ParameterName|TenantId
public static string CreateSearchPartitionKey(string resourceType, string parameterName, string tenantId)
{
    return $"{resourceType}|{parameterName}|{tenantId}";
}
```

**Benefits:**
- Groups similar search parameters together
- Enables efficient range queries within partitions
- Reduces cross-partition query requirements
- Optimizes based on proven #2686 pattern

### Index Optimization Techniques

**1. Parameter-First Structure (From #2686)**
```json
// Before (Original - slower)
"searchIndices": [
  { "p": "_lastUpdated", "st": "timestamp", "et": "timestamp" }
]

// After (Optimized - faster)
"searchIndices": {
  "_lastUpdated": [{ "st": "timestamp", "et": "timestamp" }]
}
```

**2. Compact Value Encoding**
```csharp
public record SearchValue(
    string Type,           // "st" - search type
    string StartValue,     // "sv" - start value (for ranges)
    string EndValue,       // "ev" - end value (for ranges)
    string Code = null,    // "c" - code (for tokens)
    string System = null   // "s" - system (for tokens)
);
```

**3. Memory-Efficient Serialization**
```csharp
public static ReadOnlyMemory<byte> SerializeSearchIndex(SearchIndex index)
{
    using var stream = FhirStreamManager.GetStream("SearchIndex");

    // Use System.Text.Json with minimal object allocation
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    JsonSerializer.Serialize(stream, index, options);
    return stream.GetReadOnlySequence().ToArray();
}
```

## Container 3: SearchAggregates (Cross-Partition Optimization)

### Purpose
Handle the 500 physical partition SDK limitation through pre-computed aggregates and intelligent query routing.

### Document Structure
```json
{
  "id": "Patient-name-aggregate-2024-12",
  "pk": "aggregates|Patient|name",
  "resourceType": "Patient",
  "parameter": "name",
  "timeWindow": "2024-12",
  "tenantId": "tenant-1",

  "statistics": {
    "totalResources": 50000,
    "distinctValues": 25000,
    "partitionDistribution": {
      "Patient|name|tenant-1": 15000,
      "Patient|name|tenant-2": 10000,
      // ... up to 500 partitions
    },
    "valueRanges": {
      "a-e": { "count": 10000, "partitions": ["Patient|name|tenant-1"] },
      "f-m": { "count": 20000, "partitions": ["Patient|name|tenant-1", "Patient|name|tenant-2"] },
      "n-z": { "count": 20000, "partitions": ["Patient|name|tenant-2"] }
    }
  },

  "queryHints": {
    "optimalPartitions": ["Patient|name|tenant-1", "Patient|name|tenant-2"],
    "searchStrategy": "range-based",
    "estimatedRU": 150
  }
}
```

## Query Optimization Strategies

### 1. Intelligent Partition Selection
```csharp
public class PartitionOptimizedQueryExecutor
{
    private const int MAX_PARALLEL_PARTITIONS = 500;

    public async ValueTask<SearchResult> ExecuteSearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Get query plan from aggregates
        var queryPlan = await GetOptimalQueryPlanAsync(query, cancellationToken);

        if (queryPlan.TargetPartitions.Count <= MAX_PARALLEL_PARTITIONS)
        {
            // Direct execution within partition limits
            return await ExecuteDirectQueryAsync(query, queryPlan, cancellationToken);
        }
        else
        {
            // Use batched execution with result merging
            return await ExecuteBatchedQueryAsync(query, queryPlan, cancellationToken);
        }
    }

    private async ValueTask<QueryPlan> GetOptimalQueryPlanAsync(
        SearchQuery query,
        CancellationToken cancellationToken)
    {
        // Query aggregates container for statistics
        var aggregateQuery = new QueryDefinition(
            "SELECT * FROM c WHERE c.resourceType = @resourceType AND c.parameter = @parameter")
            .WithParameter("@resourceType", query.ResourceType)
            .WithParameter("@parameter", query.Parameters.First().Name);

        using var aggregateIterator = _aggregatesContainer.GetItemQueryIterator<SearchAggregate>(aggregateQuery);
        var aggregates = await aggregateIterator.ReadNextAsync(cancellationToken);

        return CreateOptimalPlan(query, aggregates.First());
    }
}
```

### 2. Range-Based Partition Targeting
```csharp
public class RangeBasedPartitionSelector
{
    public async ValueTask<string[]> SelectPartitionsForQuery(SearchParameter parameter)
    {
        return parameter.Type switch
        {
            SearchParameterType.String => await SelectStringRangePartitions(parameter),
            SearchParameterType.Date => await SelectDateRangePartitions(parameter),
            SearchParameterType.Number => await SelectNumberRangePartitions(parameter),
            SearchParameterType.Token => await SelectTokenPartitions(parameter),
            _ => await SelectAllPartitions(parameter.ResourceType)
        };
    }

    private async ValueTask<string[]> SelectStringRangePartitions(SearchParameter parameter)
    {
        var value = parameter.Value.ToString();
        var prefix = value.Length >= 2 ? value[..2].ToLowerInvariant() : value.ToLowerInvariant();

        // Use aggregate data to find partitions containing this prefix range
        var partitionQuery = new QueryDefinition(
            "SELECT c.queryHints.optimalPartitions FROM c WHERE c.resourceType = @resourceType " +
            "AND c.parameter = @parameter AND CONTAINS(c.statistics.valueRanges, @prefix)")
            .WithParameter("@resourceType", parameter.ResourceType)
            .WithParameter("@parameter", parameter.Name)
            .WithParameter("@prefix", prefix);

        using var iterator = _aggregatesContainer.GetItemQueryIterator<dynamic>(partitionQuery);
        var result = await iterator.ReadNextAsync();

        return result.First().queryHints.optimalPartitions.ToObject<string[]>();
    }
}
```

### 3. Batched Cross-Partition Execution
```csharp
public class BatchedQueryExecutor
{
    public async ValueTask<SearchResult> ExecuteBatchedQueryAsync(
        SearchQuery query,
        QueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        var batches = plan.TargetPartitions
            .Chunk(MAX_PARALLEL_PARTITIONS)
            .ToArray();

        var allResults = new List<FhirResource>();
        var totalCount = 0;

        foreach (var batch in batches)
        {
            var batchResults = await ExecutePartitionBatchAsync(query, batch, cancellationToken);
            allResults.AddRange(batchResults.Resources);
            totalCount += batchResults.TotalCount;

            // Check if we have enough results
            if (allResults.Count >= query.Count && !query.IncludeTotal)
            {
                break;
            }
        }

        // Sort and truncate final results
        var sortedResults = SortResults(allResults, query.Sort);
        var finalResults = sortedResults.Take(query.Count).ToArray();

        return new SearchResult
        {
            Resources = finalResults,
            TotalCount = query.IncludeTotal ? totalCount : null,
            ContinuationToken = CreateContinuationToken(query, finalResults.LastOrDefault())
        };
    }
}
```

## Performance Optimizations

### 1. Resource Read Performance
```csharp
public class OptimizedResourceReader
{
    public async ValueTask<FhirResource> GetResourceAsync(
        string resourceType,
        string resourceId,
        string tenantId,
        string versionId = null,
        CancellationToken cancellationToken = default)
    {
        // Direct partition key + ID read (fastest possible)
        var partitionKey = CreateResourcePartitionKey(resourceType, tenantId, DateTime.UtcNow);
        var documentId = versionId == null
            ? $"{resourceType}-{resourceId}"
            : $"{resourceType}-{resourceId}-{versionId}";

        try
        {
            var response = await _resourcesContainer.ReadItemAsync<ResourceDocument>(
                documentId,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken);

            return response.Resource.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Try historical partitions if not found in current month
            return await SearchHistoricalPartitionsAsync(resourceType, resourceId, tenantId, versionId, cancellationToken);
        }
    }
}
```

### 2. Bulk Operations Optimization
```csharp
public class BulkOptimizedWriter
{
    public async ValueTask<BulkWriteResult> BulkWriteResourcesAsync(
        IReadOnlyList<FhirResource> resources,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var resourceTasks = new List<Task<ItemResponse<ResourceDocument>>>();
        var searchTasks = new List<Task<ItemResponse<SearchDocument>>>();

        using var resourceSemaphore = new SemaphoreSlim(100); // Limit parallelism
        using var searchSemaphore = new SemaphoreSlim(100);

        foreach (var resource in resources)
        {
            // Write resource document
            resourceTasks.Add(WriteResourceWithSemaphoreAsync(resource, tenantId, resourceSemaphore, cancellationToken));

            // Write search indices
            var searchIndices = await _searchIndexer.ExtractAsync(resource);
            foreach (var searchDoc in CreateSearchDocuments(resource, searchIndices, tenantId))
            {
                searchTasks.Add(WriteSearchWithSemaphoreAsync(searchDoc, searchSemaphore, cancellationToken));
            }
        }

        // Execute all writes in parallel
        var resourceResults = await Task.WhenAll(resourceTasks);
        var searchResults = await Task.WhenAll(searchTasks);

        return new BulkWriteResult
        {
            SuccessfulResources = resourceResults.Count(r => r.StatusCode == HttpStatusCode.Created),
            SuccessfulSearchIndices = searchResults.Count(r => r.StatusCode == HttpStatusCode.Created),
            TotalRU = resourceResults.Sum(r => r.RequestCharge) + searchResults.Sum(r => r.RequestCharge)
        };
    }
}
```

## Scale Projections and Limits

### Physical Partition Distribution
```
10PB Total Data:
├── Resources Container: ~7PB (70%)
│   ├── Physical Partitions: ~7,000 (1TB each)
│   ├── Logical Partitions: ~350,000 (20GB each)
│   └── Throughput: 70M RU/s (10K per physical partition)
│
├── SearchIndices Container: ~2.5PB (25%)
│   ├── Physical Partitions: ~2,500 (1TB each)
│   ├── Logical Partitions: ~125,000 (20GB each)
│   └── Throughput: 25M RU/s (10K per physical partition)
│
└── SearchAggregates Container: ~0.5PB (5%)
    ├── Physical Partitions: ~500 (1TB each)
    ├── Logical Partitions: ~25,000 (20GB each)
    └── Throughput: 5M RU/s (10K per physical partition)
```

### Query Performance Targets
```
Resource Read (by ID): <5ms, <5 RU
Simple Search (single partition): <50ms, <50 RU
Complex Search (cross-partition, <500 partitions): <500ms, <500 RU
Complex Search (cross-partition, >500 partitions): <2s, <2000 RU
Bulk Operations (1000 resources): <30s, <10000 RU
```

## Implementation Roadmap

### Phase 1: Core Infrastructure (Month 1)
- Implement three-container architecture
- Basic partition key strategies
- Resource read/write operations
- Memory-efficient serialization

### Phase 2: Search Optimization (Month 2)
- Implement #2686 optimization pattern
- Build search document structure
- Basic single-partition search queries
- Search result caching

### Phase 3: Cross-Partition Handling (Month 3)
- Implement aggregates container
- Build partition selection algorithms
- Handle 500-partition SDK limitation
- Batched query execution

### Phase 4: Performance Optimization (Month 4)
- Bulk operation optimization
- Advanced caching strategies
- Query plan optimization
- Performance monitoring and tuning

### Phase 5: Scale Testing (Month 5)
- Load testing at TB scale
- Partition redistribution strategies
- Performance validation
- Cost optimization

This architecture provides a robust foundation for >10PB FHIR data storage while maintaining optimal performance through intelligent partitioning, proven optimization patterns, and careful management of Cosmos DB limitations.