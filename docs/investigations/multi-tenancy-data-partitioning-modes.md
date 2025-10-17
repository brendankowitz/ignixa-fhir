# Investigation: Multi-Tenancy Data Partitioning Modes

**Status**: Draft
**Last Updated**: 2025-10-08
**Investigator**: Claude Code

## Executive Summary

This investigation explores implementing **first-class multi-tenancy data partitioning** with two operational modes:

1. **Isolation Mode**: Multiple separate customers (tenants) with isolated data stores (database per tenant, schema per tenant, or partition key isolation)
   - Example: SaaS provider hosting Mayo Clinic, Cedars-Sinai, Johns Hopkins as separate customers
   - API: `/tenant/0/Patient`, `/tenant/1/Patient`, `/tenant/2/Patient`

2. **Distributed Mode**: Single customer with data sharded across multiple stores for horizontal scale
   - Example: Acme Hospital with 100M patients sharded across 3 data stores
   - API: `/Patient` (transparent fanout to all shards, merge results)
   - Sharding strategies: Hash-based, geography-based, time-based

The goal is to build these patterns using the existing **`IFhirRepository` abstraction** in Ignixa, allowing seamless switching between modes.

## Problem Statement

### Current Multi-Tenancy Patterns

Traditional FHIR servers handle multi-tenancy through:

1. **Database per Tenant**: Complete isolation, high resource overhead
2. **Schema per Tenant**: Logical isolation within shared database
3. **Partition Key/Discriminator**: Shared schema with tenant ID column
4. **Separate Deployments**: No shared infrastructure

**Limitations**:
- ❌ No horizontal sharding for single customer at scale
- ❌ Cannot scale a single customer beyond one data store
- ❌ Manual data aggregation required when sharding is needed
- ❌ Tenant migration requires application downtime

### Ignixa's Vision

Support **both** isolation and distributed modes as first-class concepts:

```csharp
// Isolation Mode: Multiple separate customers
GET /tenant/0/Patient?name=Smith  → Mayo Clinic only
GET /tenant/1/Patient?name=Smith  → Cedars-Sinai only
GET /tenant/2/Patient?name=Smith  → Johns Hopkins only

// Distributed Mode: Single customer with horizontal sharding
GET /Patient?name=Smith
→ System determines sharding strategy (e.g., hash on name)
→ Fanout to relevant shards (0, 1, 2) for same customer
→ Merge results into unified Bundle
```

**Key Requirements**:
1. ✅ **IFhirRepository Abstraction**: Same repository interface for both modes
2. ✅ **Intelligent Query Distribution**: Parallel fanout to multiple shards
3. ✅ **Result Aggregation**: FHIR-compliant Bundle with continuation tokens
4. ✅ **Sharding Strategies**: Hash-based, geography-based, time-based partitioning
5. ✅ **Transparent API**: Distributed mode uses standard `/Patient` endpoint (no shard ID)
6. ✅ **Zero Overhead**: Pass-through optimization for single shard (no fanout penalty)

## Background Research

### Fanout Broker Query Pattern

Based on [brendankowitz/fhir-server ADR-2506](https://github.com/brendankowitz/fhir-server/blob/personal/bkowitz/copilot/broker/docs/arch/Proposals/adr-2506-fanout-broker-query-service.md), the fanout pattern provides:

**Architecture**:
```
Client Request
    ↓
Query Router (analyze search parameters)
    ↓
Execution Strategy Analyzer
    ↓         ↓
Parallel    Sequential
Execution   Execution
    ↓         ↓
Result Aggregator
    ↓
FHIR Bundle (with continuation tokens)
```

**Execution Strategies**:

| Strategy | Triggers | Behavior |
|----------|----------|----------|
| **Parallel** | Exact ID, identifier, small result sets, sorting | Execute on all data layers simultaneously, merge results |
| **Sequential** | Broad searches, large result sets, date ranges | Execute one layer at a time until quota filled |

**Key Features**:
- ✅ Distributed sorting algorithm
- ✅ Per-layer continuation tokens
- ✅ Early termination (fill factor 0.5)
- ✅ Timeout protection (15s default)
- ✅ Chained search resolution across layers
- ✅ Duplicate resource handling

### Industry Patterns

#### Azure Health Data Services (Multi-Tenant SaaS)
- **Pattern**: Schema per tenant + partition keys
- **Isolation**: Logical (shared database)
- **Distribution**: Not supported (manual export required)

#### Google Cloud Healthcare API
- **Pattern**: Dataset per tenant (GCS buckets)
- **Isolation**: Physical (separate storage)
- **Distribution**: BigQuery export for analytics

#### AWS HealthLake
- **Pattern**: Data store per tenant
- **Isolation**: Physical (separate Aurora instances)
- **Distribution**: Not supported (S3 export for analytics)

#### Smile CDR (Self-Hosted)
- **Pattern**: Configurable (database/schema/partition key)
- **Isolation**: Configurable
- **Distribution**: Custom "Cluster Mode" with federated search

**Insight**: No major vendor provides **built-in** isolation + distributed modes as first-class abstractions. This is a **differentiator** for Ignixa.

### Real-World Use Cases

#### Use Case 1: Multi-Tenant SaaS (Isolation Mode)
**Scenario**: FHIR SaaS provider hosting multiple healthcare organizations

**Requirements**:
- Complete data isolation between organizations
- Each organization is a separate customer
- Different FHIR versions per organization

**Ignixa Solution**:
```csharp
// Different customers, each with isolated data
Tenant 0: Mayo Clinic     → fhir-data/tenants/0/  (FHIR R4)
Tenant 1: Cedars-Sinai    → fhir-data/tenants/1/  (FHIR R4)
Tenant 2: Johns Hopkins   → fhir-data/tenants/2/  (FHIR R5)

// API uses explicit tenant ID in URL
GET /tenant/0/Patient?name=Smith  → Mayo Clinic only
GET /tenant/1/Patient?name=Smith  → Cedars-Sinai only
GET /tenant/2/Patient?name=Smith  → Johns Hopkins only
```

#### Use Case 2: Horizontal Sharding (Distributed Mode)
**Scenario**: Single large hospital with 100M patients needing scale-out storage

**Requirements**:
- All data belongs to same customer (Acme Hospital)
- Shard data across multiple stores for performance
- Transparent queries (users don't specify shard)

**Ignixa Solution**:
```csharp
// Single customer, multiple shards (same organization)
Shard 0: Patients A-M      → fhir-data/0/  (IFhirRepository instance)
Shard 1: Patients N-Z      → fhir-data/1/  (IFhirRepository instance)
Shard 2: Old data (2020-2022) → fhir-data/2/  (IFhirRepository instance)

// API uses transparent endpoint (no tenant/shard ID in URL)
GET /Patient?name=Smith
→ ShardingStrategy determines relevant shards (0, 1)
→ Fanout to IFhirRepository instances for shards 0 and 1 in parallel
→ Merge results
→ Return unified Bundle

// Different sharding strategies:
// - Hash-based: Hash(patientId) % shardCount
// - Geography-based: US East, US West, EU
// - Time-based: Current year, previous years, archive
```

## Proposed Architecture

**IMPORTANT NOTE**: This investigation document originally proposed a new `IDataLayer` abstraction. However, based on team feedback, we will use the **existing `IFhirRepository` pattern** instead. The architecture below is preserved for historical context, but the implementation (see ADR-2523) uses `IFhirRepositoryFactory` to create multiple repository instances for sharding, rather than introducing a new abstraction layer.

**Key Simplification**:
- **Distributed Mode**: Multiple `IFhirRepository` instances (one per shard) + fanout/merge logic
- **Isolation Mode**: `IFhirRepositoryFactory` creates per-tenant repositories
- **No new abstractions**: Reuse existing `IFhirRepository` interface

### Core Abstractions (Historical - See ADR-2523 for Updated Design)

#### 1. Data Layer Registry

**Purpose**: Centralized registry of all data stores, their capabilities, and participation modes

**Note**: In the updated design (ADR-2523), this is replaced by simpler sharding configuration without a full registry abstraction.

```csharp
public interface IDataLayerRegistry
{
    /// <summary>
    /// Register a data layer (isolated or distributed)
    /// </summary>
    Task<DataLayerId> RegisterAsync(DataLayerRegistration registration, CancellationToken ct);

    /// <summary>
    /// Get data layers for a query (isolation or distributed)
    /// </summary>
    Task<IReadOnlyList<IDataLayer>> GetDataLayersForQueryAsync(
        DataLayerQueryContext context,
        CancellationToken ct);

    /// <summary>
    /// Update participation settings for a tenant
    /// </summary>
    Task UpdateParticipationAsync(
        TenantId tenantId,
        DataLayerParticipation participation,
        CancellationToken ct);
}

public record DataLayerRegistration
{
    public required string Name { get; init; }
    public required DataLayerMode Mode { get; init; }  // Isolation or Distributed
    public required IDataStoreConfiguration Configuration { get; init; }
    public DataLayerCapabilities Capabilities { get; init; }
    public DataLayerParticipation? Participation { get; init; }  // For distributed mode
}

public enum DataLayerMode
{
    /// <summary>
    /// Single tenant, isolated data store
    /// </summary>
    Isolation,

    /// <summary>
    /// Multi-tenant, distributed queries with fanout/union
    /// </summary>
    Distributed
}

public record DataLayerCapabilities
{
    public IReadOnlyList<FhirVersion> SupportedVersions { get; init; } = [];
    public IReadOnlyList<string> SupportedResourceTypes { get; init; } = [];
    public bool SupportsTransaction { get; init; }
    public bool SupportsChainedSearch { get; init; }
    public bool SupportsInclude { get; init; }
    public bool SupportsRevInclude { get; init; }
    public int MaxPageSize { get; init; } = 100;
    public TimeSpan QueryTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public record DataLayerParticipation
{
    public ParticipationMode Mode { get; init; } = ParticipationMode.OptIn;
    public IReadOnlyList<string> AllowedDistributedLayers { get; init; } = [];
    public IDataFilter? PreQueryFilter { get; init; }  // e.g., de-identification
    public AggregationLevel AggregationLevel { get; init; } = AggregationLevel.Full;
}

public enum ParticipationMode
{
    OptIn,           // Explicitly allowed distributed layers only
    OptOut,          // All distributed layers except blocked
    AlwaysIncluded,  // Cannot opt-out (enterprise contract)
    NeverIncluded    // Strict isolation (compliance requirement)
}

public enum AggregationLevel
{
    Full,           // Return full resources
    SummaryOnly,    // Return only _summary fields
    CountOnly,      // Return only count (no resources)
    Custom          // Custom projection via filter
}
```

#### 2. Unified Data Layer Interface

**Purpose**: Consistent interface for both isolation and distributed modes

```csharp
public interface IDataLayer
{
    DataLayerId Id { get; }
    DataLayerMode Mode { get; }
    DataLayerCapabilities Capabilities { get; }

    /// <summary>
    /// Execute a FHIR search query
    /// </summary>
    Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken ct);

    /// <summary>
    /// Read a single resource by ID
    /// </summary>
    Task<ResourceWrapper?> ReadAsync(
        string resourceType,
        string id,
        CancellationToken ct);

    /// <summary>
    /// Create a resource
    /// </summary>
    Task<ResourceWrapper> CreateAsync(
        ResourceWrapper resource,
        CancellationToken ct);

    /// <summary>
    /// Update a resource
    /// </summary>
    Task<ResourceWrapper> UpdateAsync(
        ResourceWrapper resource,
        CancellationToken ct);

    /// <summary>
    /// Delete a resource
    /// </summary>
    Task DeleteAsync(
        string resourceType,
        string id,
        CancellationToken ct);

    /// <summary>
    /// Execute a transaction bundle
    /// </summary>
    Task<Bundle> ExecuteTransactionAsync(
        Bundle bundle,
        CancellationToken ct);
}

public record SearchRequest
{
    public required string ResourceType { get; init; }
    public required IReadOnlyList<SearchParameter> Parameters { get; init; }
    public int Count { get; init; } = 10;
    public string? ContinuationToken { get; init; }
    public SortOrder? Sort { get; init; }
    public SummaryType Summary { get; init; } = SummaryType.False;
    public IReadOnlyList<string> Include { get; init; } = [];
    public IReadOnlyList<string> RevInclude { get; init; } = [];
}

public record SearchResult
{
    public required IReadOnlyList<ResourceWrapper> Resources { get; init; }
    public int? TotalCount { get; init; }
    public string? ContinuationToken { get; init; }
    public DataLayerId? SourceLayerId { get; init; }  // For distributed results
    public TimeSpan ExecutionTime { get; init; }
}
```

#### 3. Distributed Query Executor

**Purpose**: Fanout/union logic for distributed mode queries

```csharp
public interface IDistributedQueryExecutor
{
    /// <summary>
    /// Execute a search across multiple data layers
    /// </summary>
    Task<DistributedSearchResult> SearchAsync(
        DistributedSearchRequest request,
        CancellationToken ct);
}

public record DistributedSearchRequest
{
    public required SearchRequest BaseRequest { get; init; }
    public required IReadOnlyList<IDataLayer> TargetLayers { get; init; }
    public ExecutionStrategy Strategy { get; init; } = ExecutionStrategy.Auto;
    public double FillFactor { get; init; } = 0.5;  // Continue until 50% of requested count
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
    public bool AllowPartialResults { get; init; } = true;
}

public enum ExecutionStrategy
{
    Auto,        // Analyzer decides based on query
    Parallel,    // Execute all layers simultaneously
    Sequential   // Execute one layer at a time
}

public record DistributedSearchResult
{
    public required IReadOnlyList<ResourceWrapper> AggregatedResources { get; init; }
    public required IReadOnlyList<LayerSearchResult> LayerResults { get; init; }
    public int? TotalCount { get; init; }  // Sum of all layer counts (if available)
    public string? ContinuationToken { get; init; }  // Composite token for next page
    public ExecutionStrategy UsedStrategy { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public bool IsPartialResult { get; init; }  // True if some layers timed out
}

public record LayerSearchResult
{
    public required DataLayerId LayerId { get; init; }
    public required SearchResult Result { get; init; }
    public LayerExecutionStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum LayerExecutionStatus
{
    Success,
    Timeout,
    Error,
    Skipped   // Query incompatible with layer capabilities
}
```

### Implementation Components

#### Component 1: Execution Strategy Analyzer

**Purpose**: Decide parallel vs sequential execution based on query characteristics

```csharp
public interface IExecutionStrategyAnalyzer
{
    /// <summary>
    /// Analyze search request and recommend execution strategy
    /// </summary>
    ExecutionStrategy AnalyzeQuery(SearchRequest request);
}

public class ExecutionStrategyAnalyzer : IExecutionStrategyAnalyzer
{
    public ExecutionStrategy AnalyzeQuery(SearchRequest request)
    {
        // Parallel triggers
        if (HasExactIdSearch(request)) return ExecutionStrategy.Parallel;
        if (HasExactIdentifierSearch(request)) return ExecutionStrategy.Parallel;
        if (HasSortingRequirement(request)) return ExecutionStrategy.Parallel;
        if (HasSmallCountLimit(request)) return ExecutionStrategy.Parallel;

        // Sequential triggers
        if (HasBroadTextSearch(request)) return ExecutionStrategy.Sequential;
        if (HasBroadDateRange(request)) return ExecutionStrategy.Sequential;
        if (HasLargeCountLimit(request)) return ExecutionStrategy.Sequential;

        // Default to parallel for simple queries
        return ExecutionStrategy.Parallel;
    }

    private bool HasExactIdSearch(SearchRequest request)
    {
        return request.Parameters.Any(p =>
            p.Name == "_id" &&
            p.Modifier == SearchModifier.None);
    }

    private bool HasExactIdentifierSearch(SearchRequest request)
    {
        return request.Parameters.Any(p =>
            p.Type == SearchParamType.Token &&
            p.Name == "identifier" &&
            !string.IsNullOrEmpty(p.Value) &&
            !p.Value.Contains("*"));  // No wildcards
    }

    private bool HasSortingRequirement(SearchRequest request)
    {
        return request.Sort != null;
    }

    private bool HasSmallCountLimit(SearchRequest request)
    {
        return request.Count <= 10;
    }

    private bool HasBroadTextSearch(SearchRequest request)
    {
        return request.Parameters.Any(p =>
            p.Type == SearchParamType.String &&
            p.Modifier == SearchModifier.Contains);
    }

    private bool HasBroadDateRange(SearchRequest request)
    {
        return request.Parameters.Any(p =>
            p.Type == SearchParamType.Date &&
            !HasSpecificIdentifier(request));  // Date range without narrowing
    }

    private bool HasLargeCountLimit(SearchRequest request)
    {
        return request.Count > 50;
    }

    private bool HasSpecificIdentifier(SearchRequest request)
    {
        return request.Parameters.Any(p =>
            p.Name == "_id" ||
            p.Name == "identifier");
    }
}
```

#### Component 2: Distributed Search Executor

**Purpose**: Execute parallel or sequential fanout queries

```csharp
public class DistributedQueryExecutor : IDistributedQueryExecutor
{
    private readonly IExecutionStrategyAnalyzer _strategyAnalyzer;
    private readonly IResultAggregator _resultAggregator;
    private readonly ILogger<DistributedQueryExecutor> _logger;

    public async Task<DistributedSearchResult> SearchAsync(
        DistributedSearchRequest request,
        CancellationToken ct)
    {
        var strategy = request.Strategy == ExecutionStrategy.Auto
            ? _strategyAnalyzer.AnalyzeQuery(request.BaseRequest)
            : request.Strategy;

        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<LayerSearchResult> layerResults = strategy switch
        {
            ExecutionStrategy.Parallel => await ExecuteParallelAsync(request, ct),
            ExecutionStrategy.Sequential => await ExecuteSequentialAsync(request, ct),
            _ => throw new ArgumentException($"Unknown strategy: {strategy}")
        };

        stopwatch.Stop();

        var aggregated = _resultAggregator.Aggregate(
            layerResults,
            request.BaseRequest.Sort,
            request.BaseRequest.Count);

        return new DistributedSearchResult
        {
            AggregatedResources = aggregated.Resources,
            LayerResults = layerResults,
            TotalCount = CalculateTotalCount(layerResults),
            ContinuationToken = aggregated.ContinuationToken,
            UsedStrategy = strategy,
            ExecutionTime = stopwatch.Elapsed,
            IsPartialResult = layerResults.Any(r => r.Status != LayerExecutionStatus.Success)
        };
    }

    private async Task<IReadOnlyList<LayerSearchResult>> ExecuteParallelAsync(
        DistributedSearchRequest request,
        CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var tasks = request.TargetLayers.Select(layer =>
            ExecuteLayerQueryAsync(layer, request.BaseRequest, linkedCts.Token));

        var results = await Task.WhenAll(tasks);

        return results.ToList();
    }

    private async Task<IReadOnlyList<LayerSearchResult>> ExecuteSequentialAsync(
        DistributedSearchRequest request,
        CancellationToken ct)
    {
        var results = new List<LayerSearchResult>();
        var totalRetrieved = 0;
        var targetCount = (int)(request.BaseRequest.Count * request.FillFactor);

        foreach (var layer in request.TargetLayers)
        {
            if (totalRetrieved >= targetCount)
            {
                _logger.LogInformation(
                    "Fill factor {FillFactor} reached ({Retrieved}/{Target}), stopping sequential execution",
                    request.FillFactor,
                    totalRetrieved,
                    targetCount);
                break;
            }

            using var timeoutCts = new CancellationTokenSource(request.Timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var result = await ExecuteLayerQueryAsync(layer, request.BaseRequest, linkedCts.Token);
            results.Add(result);

            if (result.Status == LayerExecutionStatus.Success)
            {
                totalRetrieved += result.Result.Resources.Count;
            }
        }

        return results;
    }

    private async Task<LayerSearchResult> ExecuteLayerQueryAsync(
        IDataLayer layer,
        SearchRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await layer.SearchAsync(request, ct);

            return new LayerSearchResult
            {
                LayerId = layer.Id,
                Result = result,
                Status = LayerExecutionStatus.Success
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new LayerSearchResult
            {
                LayerId = layer.Id,
                Result = new SearchResult { Resources = [] },
                Status = LayerExecutionStatus.Timeout,
                ErrorMessage = "Query timeout"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying layer {LayerId}", layer.Id);

            return new LayerSearchResult
            {
                LayerId = layer.Id,
                Result = new SearchResult { Resources = [] },
                Status = LayerExecutionStatus.Error,
                ErrorMessage = ex.Message
            };
        }
    }

    private int? CalculateTotalCount(IReadOnlyList<LayerSearchResult> layerResults)
    {
        // Only return total if ALL layers provided count
        if (layerResults.All(r => r.Result.TotalCount.HasValue))
        {
            return layerResults.Sum(r => r.Result.TotalCount!.Value);
        }

        return null;  // Partial count is misleading
    }
}
```

#### Component 3: Result Aggregator

**Purpose**: Merge results from multiple layers into unified FHIR Bundle

```csharp
public interface IResultAggregator
{
    /// <summary>
    /// Aggregate search results from multiple layers
    /// </summary>
    AggregatedResult Aggregate(
        IReadOnlyList<LayerSearchResult> layerResults,
        SortOrder? sort,
        int requestedCount);
}

public record AggregatedResult
{
    public required IReadOnlyList<ResourceWrapper> Resources { get; init; }
    public string? ContinuationToken { get; init; }
}

public class ResultAggregator : IResultAggregator
{
    public AggregatedResult Aggregate(
        IReadOnlyList<LayerSearchResult> layerResults,
        SortOrder? sort,
        int requestedCount)
    {
        // Collect all successful results
        var allResources = layerResults
            .Where(r => r.Status == LayerExecutionStatus.Success)
            .SelectMany(r => r.Result.Resources.Select(res => new
            {
                Resource = res,
                SourceLayer = r.LayerId
            }))
            .ToList();

        // Remove duplicates (same resource from multiple layers)
        var uniqueResources = RemoveDuplicates(allResources);

        // Apply sorting if requested
        var sortedResources = sort != null
            ? ApplySorting(uniqueResources, sort)
            : uniqueResources;

        // Take requested count
        var pagedResources = sortedResources.Take(requestedCount).ToList();

        // Generate continuation token if more results available
        var continuationToken = GenerateContinuationToken(
            layerResults,
            sortedResources.Count,
            requestedCount);

        return new AggregatedResult
        {
            Resources = pagedResources.Select(r => r.Resource).ToList(),
            ContinuationToken = continuationToken
        };
    }

    private List<(ResourceWrapper Resource, DataLayerId SourceLayer)> RemoveDuplicates(
        List<(ResourceWrapper Resource, DataLayerId SourceLayer)> resources)
    {
        // Deduplicate by ResourceType + Id
        // Keep first occurrence (preserves layer priority if sequential)
        return resources
            .GroupBy(r => $"{r.Resource.ResourceType}/{r.Resource.ResourceId}")
            .Select(g => g.First())
            .ToList();
    }

    private List<(ResourceWrapper Resource, DataLayerId SourceLayer)> ApplySorting(
        List<(ResourceWrapper Resource, DataLayerId SourceLayer)> resources,
        SortOrder sort)
    {
        // Distributed sorting algorithm
        // Parse sort parameter and extract values from each resource

        var sortedResources = resources.OrderBy(r =>
        {
            // Extract sort value from resource using FHIRPath
            var fhirPathValue = ExtractSortValue(r.Resource, sort.Parameter);
            return fhirPathValue;
        });

        return sort.Direction == SortDirection.Descending
            ? sortedResources.Reverse().ToList()
            : sortedResources.ToList();
    }

    private string? ExtractSortValue(ResourceWrapper resource, string sortParameter)
    {
        // Use FHIRPath evaluator to extract value
        // This is simplified - real implementation uses Hl7.Fhir.FhirPath

        // Example: _lastUpdated → resource.Meta.LastUpdated
        if (sortParameter == "_lastUpdated")
        {
            return resource.LastModified.ToString("o");
        }

        // Example: birthdate → Patient.birthDate
        // Requires FHIRPath evaluation against resource content
        return null;  // Placeholder
    }

    private string? GenerateContinuationToken(
        IReadOnlyList<LayerSearchResult> layerResults,
        int totalAvailable,
        int requestedCount)
    {
        if (totalAvailable <= requestedCount)
        {
            return null;  // No more results
        }

        // Composite continuation token containing per-layer tokens
        var layerTokens = layerResults
            .Where(r => r.Result.ContinuationToken != null)
            .Select(r => new
            {
                LayerId = r.LayerId.Value,
                Token = r.Result.ContinuationToken
            })
            .ToList();

        if (!layerTokens.Any())
        {
            return null;
        }

        // Serialize to base64-encoded JSON
        var json = JsonSerializer.Serialize(layerTokens);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}

public record SortOrder
{
    public required string Parameter { get; init; }
    public SortDirection Direction { get; init; } = SortDirection.Ascending;
}

public enum SortDirection
{
    Ascending,
    Descending
}
```

### Query Execution Flow

**IMPORTANT**: Fanout/union logic is **ONLY** used when:
1. Mode is **Distributed** AND
2. Number of participant layers **> 1**

All other scenarios use direct pass-through:

```csharp
// Decision tree for query execution
if (mode == DataLayerMode.Isolation)
{
    // PASS-THROUGH: Isolation mode always queries single data store
    return await _repository.SearchAsync(request, ct);
}
else if (mode == DataLayerMode.Distributed)
{
    var participantLayers = await GetParticipantLayersAsync(ct);

    if (participantLayers.Count == 0)
    {
        // PASS-THROUGH: No participants, return empty result
        return EmptyResult();
    }
    else if (participantLayers.Count == 1)
    {
        // PASS-THROUGH: Single participant, no aggregation needed
        return await participantLayers[0].SearchAsync(request, ct);
    }
    else
    {
        // FANOUT: Multiple participants, use distributed query executor
        return await _executor.SearchAsync(distributedRequest, ct);
    }
}
```

**Performance Comparison**:

| Scenario | Execution Path | Overhead | Latency (P95) |
|----------|---------------|----------|---------------|
| **Isolation mode** | Pass-through to single repository | None | < 100ms |
| **Distributed mode (1 layer)** | Pass-through to single layer | None | < 100ms |
| **Distributed mode (2-3 layers, parallel)** | Fanout + aggregation + deduplication | ~50ms | < 500ms |
| **Distributed mode (10 layers, parallel)** | Fanout + aggregation + deduplication | ~200ms | < 1s |
| **Distributed mode (3 layers, sequential)** | Sequential + early termination | ~100ms | < 2s |

**Key Insight**: The pass-through optimization ensures that distributed layers with a single participant have **zero overhead** compared to isolation mode. This makes it safe to create distributed layers speculatively without performance penalty until multiple participants join.

### Data Store Implementations

#### Isolation Mode: SQL Server per Tenant

```csharp
public class IsolatedSqlDataLayer : IDataLayer
{
    private readonly string _connectionString;
    private readonly IFhirRepository _repository;

    public DataLayerId Id { get; }
    public DataLayerMode Mode => DataLayerMode.Isolation;
    public DataLayerCapabilities Capabilities { get; }

    public IsolatedSqlDataLayer(
        DataLayerId id,
        string connectionString,
        DataLayerCapabilities capabilities)
    {
        Id = id;
        _connectionString = connectionString;
        Capabilities = capabilities;

        _repository = new SqlFhirRepository(_connectionString);
    }

    public Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken ct)
    {
        // Direct pass-through to repository (single tenant)
        return _repository.SearchAsync(request, ct);
    }

    // Other methods delegate to _repository
}
```

#### Isolation Mode: Schema per Tenant

```csharp
public class SchemaIsolatedSqlDataLayer : IDataLayer
{
    private readonly string _baseConnectionString;
    private readonly string _schemaName;
    private readonly IFhirRepository _repository;

    public DataLayerId Id { get; }
    public DataLayerMode Mode => DataLayerMode.Isolation;
    public DataLayerCapabilities Capabilities { get; }

    public SchemaIsolatedSqlDataLayer(
        DataLayerId id,
        string baseConnectionString,
        string schemaName,
        DataLayerCapabilities capabilities)
    {
        Id = id;
        _baseConnectionString = baseConnectionString;
        _schemaName = schemaName;
        Capabilities = capabilities;

        // Repository configured with schema name
        _repository = new SqlFhirRepository(_baseConnectionString, _schemaName);
    }

    public Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken ct)
    {
        // Repository automatically uses schema prefix
        // Example: SELECT * FROM [tenant_abc].[Resource]
        return _repository.SearchAsync(request, ct);
    }
}
```

#### Distributed Mode: Fanout Data Layer

```csharp
public class DistributedDataLayer : IDataLayer
{
    private readonly IDataLayerRegistry _registry;
    private readonly IDistributedQueryExecutor _executor;
    private readonly IReadOnlyList<DataLayerId> _participantLayerIds;

    public DataLayerId Id { get; }
    public DataLayerMode Mode => DataLayerMode.Distributed;
    public DataLayerCapabilities Capabilities { get; }

    public DistributedDataLayer(
        DataLayerId id,
        IDataLayerRegistry registry,
        IDistributedQueryExecutor executor,
        IReadOnlyList<DataLayerId> participantLayerIds,
        DataLayerCapabilities capabilities)
    {
        Id = id;
        _registry = registry;
        _executor = executor;
        _participantLayerIds = participantLayerIds;
        Capabilities = capabilities;
    }

    public async Task<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken ct)
    {
        // Get participant data layers
        var participantLayers = new List<IDataLayer>();
        foreach (var layerId in _participantLayerIds)
        {
            var layer = await _registry.GetDataLayerAsync(layerId, ct);
            if (layer != null)
            {
                participantLayers.Add(layer);
            }
        }

        // OPTIMIZATION: Pass-through for single layer (no fanout overhead)
        if (participantLayers.Count == 1)
        {
            // No need for distributed query executor - just pass through to single layer
            return await participantLayers[0].SearchAsync(request, ct);
        }

        // OPTIMIZATION: No layers means no results
        if (participantLayers.Count == 0)
        {
            return new SearchResult
            {
                Resources = [],
                TotalCount = 0,
                ExecutionTime = TimeSpan.Zero
            };
        }

        // Execute distributed query with fanout/union (only for 2+ layers)
        var distributedRequest = new DistributedSearchRequest
        {
            BaseRequest = request,
            TargetLayers = participantLayers
        };

        var distributedResult = await _executor.SearchAsync(distributedRequest, ct);

        // Convert to standard SearchResult
        return new SearchResult
        {
            Resources = distributedResult.AggregatedResources,
            TotalCount = distributedResult.TotalCount,
            ContinuationToken = distributedResult.ContinuationToken,
            ExecutionTime = distributedResult.ExecutionTime
        };
    }

    // Write operations not supported in distributed mode
    public Task<ResourceWrapper> CreateAsync(ResourceWrapper resource, CancellationToken ct)
    {
        throw new NotSupportedException("Create operations not supported in distributed mode");
    }

    public Task<ResourceWrapper> UpdateAsync(ResourceWrapper resource, CancellationToken ct)
    {
        throw new NotSupportedException("Update operations not supported in distributed mode");
    }

    public Task DeleteAsync(string resourceType, string id, CancellationToken ct)
    {
        throw new NotSupportedException("Delete operations not supported in distributed mode");
    }
}
```

### API Design

#### Routing Strategy

```csharp
// Isolation mode: Tenant-specific endpoint
[HttpGet("/{tenantId}/{version}/{resourceType}")]
public async Task<IActionResult> SearchIsolated(
    string tenantId,
    string version,
    string resourceType,
    [FromQuery] Dictionary<string, string> queryParams)
{
    // Get isolated data layer for tenant
    var dataLayer = await _registry.GetTenantDataLayerAsync(tenantId, version);

    if (dataLayer.Mode != DataLayerMode.Isolation)
    {
        return BadRequest("Tenant endpoint requires isolation mode");
    }

    var searchRequest = ParseSearchRequest(resourceType, queryParams);
    var result = await dataLayer.SearchAsync(searchRequest, HttpContext.RequestAborted);

    return Ok(CreateBundle(result));
}

// Distributed mode: Named distributed layer endpoint
[HttpGet("/distributed/{layerName}/{version}/{resourceType}")]
public async Task<IActionResult> SearchDistributed(
    string layerName,
    string version,
    string resourceType,
    [FromQuery] Dictionary<string, string> queryParams)
{
    // Get distributed data layer by name
    var dataLayer = await _registry.GetDistributedLayerAsync(layerName, version);

    if (dataLayer.Mode != DataLayerMode.Distributed)
    {
        return BadRequest("Distributed endpoint requires distributed mode");
    }

    var searchRequest = ParseSearchRequest(resourceType, queryParams);
    var result = await dataLayer.SearchAsync(searchRequest, HttpContext.RequestAborted);

    return Ok(CreateBundle(result));
}
```

#### Configuration Example

```json
{
  "DataLayers": {
    "Isolation": [
      {
        "Id": "tenant-hospital-mayo",
        "Name": "Mayo Clinic",
        "Mode": "Isolation",
        "Configuration": {
          "Type": "SqlServer",
          "ConnectionString": "Server=sql-us-east;Database=mayo;",
          "Schema": null
        },
        "Capabilities": {
          "SupportedVersions": ["R4", "R5"],
          "SupportedResourceTypes": ["Patient", "Observation", "Encounter"],
          "SupportsTransaction": true,
          "MaxPageSize": 100
        }
      },
      {
        "Id": "tenant-hospital-cedars",
        "Name": "Cedars-Sinai",
        "Mode": "Isolation",
        "Configuration": {
          "Type": "SqlServer",
          "ConnectionString": "Server=sql-us-west;Database=cedars;",
          "Schema": null
        },
        "Capabilities": {
          "SupportedVersions": ["R4"],
          "SupportedResourceTypes": ["Patient", "Observation"],
          "SupportsTransaction": true,
          "MaxPageSize": 50
        }
      }
    ],
    "Distributed": [
      {
        "Id": "distributed-covid-research",
        "Name": "COVID-19 Research Network",
        "Mode": "Distributed",
        "Configuration": {
          "Type": "Distributed",
          "ParticipantLayers": [
            "tenant-hospital-mayo",
            "tenant-hospital-cedars"
          ]
        },
        "Capabilities": {
          "SupportedVersions": ["R4"],
          "SupportedResourceTypes": ["Observation", "Condition"],
          "SupportsTransaction": false,
          "MaxPageSize": 1000
        },
        "Participation": {
          "Mode": "OptIn",
          "PreQueryFilter": {
            "Type": "DeIdentification",
            "RemoveFields": ["Patient.name", "Patient.telecom", "Patient.address"]
          },
          "AggregationLevel": "Full"
        }
      }
    ]
  }
}
```

## Migration Strategies

### Scenario 1: Single Tenant → Multi-Tenant (Isolation)

**Before**: Monolithic deployment for Acme Hospital

**After**: SaaS with multiple isolated tenants

**Steps**:
1. Create tenant "acme-hospital" with existing database
2. Onboard new tenant "beta-hospital" with new database
3. Update routing to use `/{tenantId}/r4/Patient` instead of `/r4/Patient`

**Code Changes**: Minimal (just routing configuration)

### Scenario 2: Isolation → Distributed Participation

**Before**: 10 isolated hospital tenants

**After**: Hospitals opt-in to research network

**Steps**:
1. Create distributed layer "research-network"
2. Update each hospital's participation settings
```json
{
  "TenantId": "hospital-mayo",
  "Participation": {
    "Mode": "OptIn",
    "AllowedDistributedLayers": ["research-network"]
  }
}
```
3. Deploy distributed endpoint `/research/r4/Observation?code=covid-19`

**Code Changes**: None (configuration only)

### Scenario 3: Shared Database → Dedicated Database

**Before**: Small clinic using partition key in shared DB

**After**: Clinic grows to enterprise, demands dedicated DB

**Steps**:
1. Export clinic data from shared DB
2. Create new SQL database for clinic
3. Import data to new database
4. Update registry:
```json
{
  "Id": "tenant-clinic-123",
  "Configuration": {
    "Type": "SqlServer",
    "ConnectionString": "Server=dedicated-sql;Database=clinic123;"
  }
}
```
5. Update DNS/routing (zero downtime cutover)

**Code Changes**: None (data layer abstraction handles it)

## Implementation Phases

### Phase 1: Core Abstractions (Week 15-16, 32 hours)

**Deliverables**:
- `IDataLayerRegistry` interface and in-memory implementation
- `IDataLayer` interface
- `DataLayerMode` enum and related types
- Unit tests for registry operations

**Example Registration**:
```csharp
var registration = new DataLayerRegistration
{
    Name = "tenant-demo",
    Mode = DataLayerMode.Isolation,
    Configuration = new SqlServerConfiguration
    {
        ConnectionString = "Server=localhost;Database=demo;"
    },
    Capabilities = new DataLayerCapabilities
    {
        SupportedVersions = [FhirVersion.R4],
        SupportedResourceTypes = ["Patient", "Observation"]
    }
};

var layerId = await registry.RegisterAsync(registration);
```

### Phase 2: Isolation Mode Implementation (Week 17-18, 32 hours)

**Deliverables**:
- `IsolatedSqlDataLayer` implementation
- `SchemaIsolatedSqlDataLayer` implementation
- Tenant routing middleware
- Integration tests with multiple tenants

**Example Usage**:
```csharp
// Tenant A: Dedicated database
var layerA = new IsolatedSqlDataLayer(
    id: new DataLayerId("tenant-a"),
    connectionString: "Server=sql1;Database=tenantA;",
    capabilities: capabilities);

// Tenant B: Shared database, schema isolation
var layerB = new SchemaIsolatedSqlDataLayer(
    id: new DataLayerId("tenant-b"),
    baseConnectionString: "Server=sql-shared;Database=multi_tenant;",
    schemaName: "tenant_b",
    capabilities: capabilities);
```

### Phase 3: Execution Strategy Analyzer (Week 19, 16 hours)

**Deliverables**:
- `IExecutionStrategyAnalyzer` interface and implementation
- Query analysis rules (parallel vs sequential)
- Unit tests with various query patterns

**Test Cases**:
```csharp
[Fact]
public void ExactIdSearch_ShouldUseParallel()
{
    var request = new SearchRequest
    {
        ResourceType = "Patient",
        Parameters = [new SearchParameter { Name = "_id", Value = "123" }]
    };

    var strategy = _analyzer.AnalyzeQuery(request);

    Assert.Equal(ExecutionStrategy.Parallel, strategy);
}

[Fact]
public void BroadTextSearch_ShouldUseSequential()
{
    var request = new SearchRequest
    {
        ResourceType = "Patient",
        Parameters = [new SearchParameter
        {
            Name = "name",
            Value = "Smith",
            Modifier = SearchModifier.Contains
        }],
        Count = 100
    };

    var strategy = _analyzer.AnalyzeQuery(request);

    Assert.Equal(ExecutionStrategy.Sequential, strategy);
}
```

### Phase 4: Result Aggregator (Week 20, 16 hours)

**Deliverables**:
- `IResultAggregator` interface and implementation
- Deduplication logic
- Distributed sorting algorithm
- Composite continuation token generation
- Unit tests for aggregation scenarios

**Test Cases**:
```csharp
[Fact]
public void Aggregate_ShouldRemoveDuplicates()
{
    var layerResults = new[]
    {
        new LayerSearchResult
        {
            LayerId = new DataLayerId("layer-1"),
            Result = new SearchResult
            {
                Resources =
                [
                    CreatePatient("123"),
                    CreatePatient("456")
                ]
            },
            Status = LayerExecutionStatus.Success
        },
        new LayerSearchResult
        {
            LayerId = new DataLayerId("layer-2"),
            Result = new SearchResult
            {
                Resources =
                [
                    CreatePatient("123"),  // Duplicate
                    CreatePatient("789")
                ]
            },
            Status = LayerExecutionStatus.Success
        }
    };

    var result = _aggregator.Aggregate(layerResults, sort: null, requestedCount: 10);

    Assert.Equal(3, result.Resources.Count);  // 123, 456, 789 (deduplicated)
}
```

### Phase 5: Distributed Query Executor (Week 21-22, 32 hours)

**Deliverables**:
- `IDistributedQueryExecutor` interface and implementation
- Parallel execution logic with timeout handling
- Sequential execution logic with fill factor
- Error handling and partial result support
- Integration tests with multiple layers

**Example Execution**:
```csharp
var distributedRequest = new DistributedSearchRequest
{
    BaseRequest = new SearchRequest
    {
        ResourceType = "Observation",
        Parameters = [new SearchParameter { Name = "code", Value = "covid-19" }],
        Count = 100
    },
    TargetLayers =
    [
        layer1,  // Mayo Clinic
        layer2,  // Cedars-Sinai
        layer3   // Johns Hopkins
    ],
    Strategy = ExecutionStrategy.Parallel,
    Timeout = TimeSpan.FromSeconds(10)
};

var result = await executor.SearchAsync(distributedRequest, cancellationToken);

// result.AggregatedResources contains merged results from all 3 hospitals
// result.IsPartialResult = true if any layer timed out
// result.LayerResults[i].Status shows per-layer execution status
```

### Phase 6: Distributed Data Layer (Week 23, 16 hours)

**Deliverables**:
- `DistributedDataLayer` implementation
- Read-only operation enforcement
- Participation filtering
- Integration tests with registry + executor

**Example Registration**:
```csharp
var distributedLayerRegistration = new DataLayerRegistration
{
    Name = "covid-research-network",
    Mode = DataLayerMode.Distributed,
    Configuration = new DistributedConfiguration
    {
        ParticipantLayerIds =
        [
            new DataLayerId("tenant-mayo"),
            new DataLayerId("tenant-cedars"),
            new DataLayerId("tenant-hopkins")
        ]
    },
    Capabilities = new DataLayerCapabilities
    {
        SupportedVersions = [FhirVersion.R4],
        SupportedResourceTypes = ["Observation", "Condition"],
        SupportsTransaction = false,  // Distributed layers are read-only
        MaxPageSize = 1000
    },
    Participation = new DataLayerParticipation
    {
        Mode = ParticipationMode.OptIn,
        PreQueryFilter = new DeIdentificationFilter(),
        AggregationLevel = AggregationLevel.Full
    }
};

var distributedLayerId = await registry.RegisterAsync(distributedLayerRegistration);
```

### Phase 7: API Endpoints and Routing (Week 24, 16 hours)

**Deliverables**:
- Isolation mode endpoints (`/{tenantId}/{version}/{resourceType}`)
- Distributed mode endpoints (`/distributed/{layerName}/{version}/{resourceType}`)
- Routing middleware
- OpenAPI/Swagger documentation
- E2E tests

**Example API Calls**:
```bash
# Isolation mode: Query single tenant
GET /tenant-mayo/r4/Patient?name=Smith
→ Routes to Mayo Clinic's isolated database

# Distributed mode: Query research network
GET /distributed/covid-research/r4/Observation?code=covid-19&_count=1000
→ Fanout to all participating hospitals
→ Returns aggregated, de-identified results

# Hybrid: Tenant can still access own data directly
GET /tenant-mayo/r4/Observation?code=covid-19
→ Full data access (not de-identified)
```

### Phase 8: Participation Management (Week 25, 16 hours)

**Deliverables**:
- Participation settings API
- Opt-in/opt-out workflows
- Pre-query filter framework
- Aggregation level controls
- Admin UI mockups (design only)

**Example Participation API**:
```csharp
// Hospital opts-in to research network
PUT /admin/tenants/mayo/participation
{
  "Mode": "OptIn",
  "AllowedDistributedLayers": ["covid-research-network"],
  "PreQueryFilter": {
    "Type": "DeIdentification",
    "RemoveFields": ["Patient.name", "Patient.telecom"]
  },
  "AggregationLevel": "Full"
}

// Hospital opts-out
PUT /admin/tenants/mayo/participation
{
  "Mode": "NeverIncluded"
}
```

### Phase 9: Performance Optimization (Week 26, 16 hours)

**Deliverables**:
- Query result caching for distributed layers
- Parallel execution thread pool tuning
- Continuation token optimization
- Performance benchmarks (1 layer vs 10 layers vs 100 layers)
- Load testing reports

**Performance Targets**:
| Scenario | Target Latency (P95) |
|----------|---------------------|
| Isolation mode (single tenant) | < 100ms |
| Distributed parallel (3 layers) | < 500ms |
| Distributed parallel (10 layers) | < 1s |
| Distributed sequential (3 layers) | < 2s |

## Testing Strategy

### Unit Tests

```csharp
// ExecutionStrategyAnalyzerTests.cs
[Theory]
[InlineData("_id=123", ExecutionStrategy.Parallel)]
[InlineData("identifier=MRN|12345", ExecutionStrategy.Parallel)]
[InlineData("name:contains=Smith&_count=100", ExecutionStrategy.Sequential)]
[InlineData("date=ge2025-01-01&_count=1000", ExecutionStrategy.Sequential)]
public void AnalyzeQuery_ShouldReturnCorrectStrategy(
    string queryString,
    ExecutionStrategy expected)
{
    var request = ParseQueryString(queryString);
    var strategy = _analyzer.AnalyzeQuery(request);
    Assert.Equal(expected, strategy);
}

// ResultAggregatorTests.cs
[Fact]
public void Aggregate_WithSorting_ShouldMergeCorrectly()
{
    var layerResults = CreateLayerResults(
        layer1Resources: [PatientBorn(1990), PatientBorn(1985)],
        layer2Resources: [PatientBorn(1995), PatientBorn(1980)]);

    var result = _aggregator.Aggregate(
        layerResults,
        sort: new SortOrder { Parameter = "birthdate", Direction = SortDirection.Ascending },
        requestedCount: 10);

    Assert.Equal(1980, ExtractBirthYear(result.Resources[0]));
    Assert.Equal(1985, ExtractBirthYear(result.Resources[1]));
    Assert.Equal(1990, ExtractBirthYear(result.Resources[2]));
    Assert.Equal(1995, ExtractBirthYear(result.Resources[3]));
}
```

### Integration Tests

```csharp
// DistributedQueryExecutorIntegrationTests.cs
[Fact]
public async Task SearchAsync_Parallel_ShouldQueryAllLayers()
{
    // Arrange: 3 in-memory data layers with different patients
    var layer1 = CreateInMemoryLayer("layer1", [Patient("Alice"), Patient("Bob")]);
    var layer2 = CreateInMemoryLayer("layer2", [Patient("Charlie"), Patient("Diana")]);
    var layer3 = CreateInMemoryLayer("layer3", [Patient("Eve"), Patient("Frank")]);

    var request = new DistributedSearchRequest
    {
        BaseRequest = new SearchRequest { ResourceType = "Patient", Count = 10 },
        TargetLayers = [layer1, layer2, layer3],
        Strategy = ExecutionStrategy.Parallel
    };

    // Act
    var result = await _executor.SearchAsync(request, CancellationToken.None);

    // Assert
    Assert.Equal(6, result.AggregatedResources.Count);
    Assert.All(result.LayerResults, r => Assert.Equal(LayerExecutionStatus.Success, r.Status));
}

[Fact]
public async Task SearchAsync_WithTimeout_ShouldReturnPartialResults()
{
    // Arrange: 1 fast layer, 1 slow layer (simulated delay)
    var fastLayer = CreateInMemoryLayer("fast", [Patient("Alice")]);
    var slowLayer = CreateSlowLayer("slow", delayMs: 5000);  // 5 second delay

    var request = new DistributedSearchRequest
    {
        BaseRequest = new SearchRequest { ResourceType = "Patient", Count = 10 },
        TargetLayers = [fastLayer, slowLayer],
        Strategy = ExecutionStrategy.Parallel,
        Timeout = TimeSpan.FromSeconds(1)  // 1 second timeout
    };

    // Act
    var result = await _executor.SearchAsync(request, CancellationToken.None);

    // Assert
    Assert.Equal(1, result.AggregatedResources.Count);  // Only fast layer results
    Assert.True(result.IsPartialResult);
    Assert.Equal(LayerExecutionStatus.Success, result.LayerResults[0].Status);
    Assert.Equal(LayerExecutionStatus.Timeout, result.LayerResults[1].Status);
}
```

### E2E Tests

```csharp
// DistributedQueryE2ETests.cs
[Fact]
public async Task CovidResearchNetwork_ShouldAggregateObservations()
{
    // Arrange: Register 3 hospital tenants + 1 research network
    var mayoId = await RegisterTenant("mayo", database: "mayo_db");
    var cedarsId = await RegisterTenant("cedars", database: "cedars_db");
    var hopkinsId = await RegisterTenant("hopkins", database: "hopkins_db");

    await RegisterDistributedLayer("covid-research", participants: [mayoId, cedarsId, hopkinsId]);

    // Seed data
    await SeedObservations(mayoId, count: 100, code: "covid-19");
    await SeedObservations(cedarsId, count: 150, code: "covid-19");
    await SeedObservations(hopkinsId, count: 200, code: "covid-19");

    // Act: Query research network
    var response = await _client.GetAsync(
        "/distributed/covid-research/r4/Observation?code=covid-19&_count=1000");

    // Assert
    response.EnsureSuccessStatusCode();
    var bundle = await response.Content.ReadAsAsync<Bundle>();

    Assert.Equal(450, bundle.Total);  // 100 + 150 + 200
    Assert.Equal(450, bundle.Entry.Count);  // All results fit in single page
}
```

## Security Considerations

### Tenant Isolation

**Requirement**: Tenants in isolation mode MUST NOT access other tenants' data

**Implementation**:
```csharp
public class TenantIsolationMiddleware
{
    public async Task InvokeAsync(HttpContext context, IDataLayerRegistry registry)
    {
        var tenantId = context.Request.RouteValues["tenantId"]?.ToString();

        if (string.IsNullOrEmpty(tenantId))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing tenantId");
            return;
        }

        // Verify tenant exists and user has access
        var layer = await registry.GetTenantDataLayerAsync(tenantId, default);
        if (layer == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Tenant not found");
            return;
        }

        // Verify user authorization for this tenant
        var user = context.User;
        if (!user.HasClaim("tenant", tenantId))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Access denied to this tenant");
            return;
        }

        // Store layer in HttpContext for downstream handlers
        context.Items["DataLayer"] = layer;

        await _next(context);
    }
}
```

### Distributed Query Authorization

**Requirement**: Only authorized users can query distributed layers

**Implementation**:
```csharp
public class DistributedLayerAuthorizationMiddleware
{
    public async Task InvokeAsync(HttpContext context, IDataLayerRegistry registry)
    {
        var layerName = context.Request.RouteValues["layerName"]?.ToString();

        if (string.IsNullOrEmpty(layerName))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Missing layerName");
            return;
        }

        var layer = await registry.GetDistributedLayerAsync(layerName, default);
        if (layer == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Distributed layer not found");
            return;
        }

        // Verify user has research authorization
        var user = context.User;
        if (!user.HasClaim("role", "Researcher") &&
            !user.HasClaim("distributed_layer", layerName))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Unauthorized for distributed queries");
            return;
        }

        context.Items["DataLayer"] = layer;

        await _next(context);
    }
}
```

### Pre-Query Filters for De-Identification

**Requirement**: Distributed layers can enforce de-identification before returning data

**Implementation**:
```csharp
public interface IPreQueryFilter
{
    /// <summary>
    /// Apply filter to search results before aggregation
    /// </summary>
    Task<IReadOnlyList<ResourceWrapper>> ApplyAsync(
        IReadOnlyList<ResourceWrapper> resources,
        CancellationToken ct);
}

public class DeIdentificationFilter : IPreQueryFilter
{
    private readonly IReadOnlyList<string> _fieldsToRemove;

    public DeIdentificationFilter(IReadOnlyList<string> fieldsToRemove)
    {
        _fieldsToRemove = fieldsToRemove;
    }

    public async Task<IReadOnlyList<ResourceWrapper>> ApplyAsync(
        IReadOnlyList<ResourceWrapper> resources,
        CancellationToken ct)
    {
        var deidentified = new List<ResourceWrapper>();

        foreach (var resource in resources)
        {
            var element = resource.ToTypedElement();

            // Remove specified fields using FHIRPath
            foreach (var fieldPath in _fieldsToRemove)
            {
                RemoveField(element, fieldPath);
            }

            // Rebuild resource wrapper
            var deidentifiedResource = ResourceWrapper.FromTypedElement(element);
            deidentified.Add(deidentifiedResource);
        }

        return deidentified;
    }

    private void RemoveField(ITypedElement element, string fhirPath)
    {
        // Example: "Patient.name" → remove all name elements
        // Implementation uses Hl7.Fhir.FhirPath to locate and remove
    }
}

// Usage in DistributedDataLayer
public async Task<SearchResult> SearchAsync(SearchRequest request, CancellationToken ct)
{
    var result = await _executor.SearchAsync(distributedRequest, ct);

    // Apply pre-query filter if configured
    if (_participation?.PreQueryFilter != null)
    {
        var filteredResources = await _participation.PreQueryFilter.ApplyAsync(
            result.AggregatedResources,
            ct);

        return result with { AggregatedResources = filteredResources };
    }

    return result;
}
```

## Open Questions

### 1. Write Operations in Distributed Mode

**Question**: Should distributed layers support write operations (POST/PUT/DELETE)?

**Options**:
1. **Read-only** (recommended): Distributed layers only support queries
   - Pro: Simpler, avoids consistency issues
   - Con: Cannot write to research networks

2. **Write-to-all**: Write operations fan out to all participant layers
   - Pro: Unified data entry
   - Con: Requires distributed transactions, slow, complex

3. **Write-to-primary**: Designate one layer as primary for writes
   - Pro: Balanced complexity
   - Con: Asymmetric (confusing)

**Recommendation**: Start with read-only (Option 1), revisit in Phase 10+ if needed.

### 2. Continuation Token Expiration

**Question**: How long should composite continuation tokens remain valid?

**Options**:
1. **Short-lived (15 min)**: Tokens expire quickly, force re-query
2. **Long-lived (24 hours)**: Tokens persist for extended paging
3. **Persistent**: Store token state in database, never expire

**Recommendation**: Short-lived (15 min) to avoid stale data in distributed queries.

### 3. Chained Search Across Layers

**Question**: How to handle chained searches like `Observation?patient.name=Smith` in distributed mode?

**Current Fanout Pattern** (ADR-2506):
- Step 1: Query Patient layers for `name=Smith` → patient IDs
- Step 2: Query Observation layers for `patient=<id1>,<id2>,...`

**Challenge**: Patient and Observation may be in different layers

**Options**:
1. **Require co-location**: Chained search only works if both resource types in same layer
2. **Cross-layer resolution**: Allow Patient from layer A, Observation from layer B
   - Implementation: Fanout Patient query to all layers, collect IDs, fanout Observation query
   - Pro: Most flexible
   - Con: Expensive (2x fanout)

**Recommendation**: Start with Option 1 (co-location), add Option 2 in Phase 11+ if needed.

### 4. Schema Versioning Across Layers

**Question**: How to handle different FHIR versions across participant layers?

**Scenario**:
- Mayo Clinic: R4
- Cedars-Sinai: R5
- Johns Hopkins: R4

Research network queries for "covid-19" Observation - need to handle both R4 and R5 schemas.

**Options**:
1. **Require same version**: All participants must use same FHIR version
2. **Version conversion**: Convert all results to target version (e.g., R5 → R4)
   - Pro: Flexible
   - Con: Expensive, lossy (see version-conversion investigation)
3. **Version tagging**: Return resources with version tag, client handles
   - Pro: No conversion overhead
   - Con: Client complexity

**Recommendation**: Option 1 (same version) for Phase 1, revisit in Phase 12+.

## Risks and Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Performance degradation with 100+ layers** | High | Medium | Implement layer health checks, auto-exclude slow layers, optimize parallel execution |
| **Continuation token state explosion** | Medium | Medium | Use short expiration (15 min), compress tokens, limit to 1000 layers per query |
| **Tenant data leak via misconfigured participation** | Critical | Low | Mandatory participation review, audit logs, default to NeverIncluded |
| **Distributed query DDoS on participant layers** | High | Medium | Rate limiting per distributed layer, query quota enforcement, circuit breakers |
| **Inconsistent results during pagination** | Medium | High | Document eventual consistency, recommend snapshot queries, consider read replicas |

## Success Metrics

### Phase 1-2 (Isolation Mode)
- ✅ Support 1000+ isolated tenants on shared infrastructure
- ✅ < 100ms P95 latency for single-tenant queries
- ✅ Zero cross-tenant data leaks (security audit)

### Phase 3-8 (Distributed Mode)
- ✅ Support distributed queries across 10 layers with < 1s P95 latency
- ✅ Support distributed queries across 100 layers with < 5s P95 latency
- ✅ 95%+ result accuracy (deduplication, sorting)
- ✅ Handle 10% layer failures gracefully (partial results)

### Phase 9 (Production Readiness)
- ✅ 1M+ distributed queries per day
- ✅ < 0.01% error rate
- ✅ 99.9% uptime for distributed query service

## Alternatives Considered

### Alternative 1: Separate Isolation and Distributed Deployments

**Approach**: Deploy two separate applications - one for isolation mode, one for distributed mode

**Pros**:
- Simpler codebase (no mode switching)
- Independent scaling

**Cons**:
- ❌ Cannot migrate tenants between modes
- ❌ Duplicate code maintenance
- ❌ No hybrid scenarios (tenant participates in distributed while maintaining isolation)

**Decision**: Rejected - unified abstraction is core requirement

### Alternative 2: ETL Pipeline for Distributed Analytics

**Approach**: Use traditional ETL (Extract-Transform-Load) to copy data from isolated tenants to central analytics database

**Pros**:
- Standard pattern
- No query fanout overhead

**Cons**:
- ❌ Data duplication (storage cost)
- ❌ Stale data (ETL lag)
- ❌ Complex de-identification pipeline
- ❌ Cannot support real-time research queries

**Decision**: Rejected - real-time distributed queries are requirement

### Alternative 3: GraphQL Federation

**Approach**: Use GraphQL Federation to federate queries across tenants

**Pros**:
- Standard GraphQL pattern
- Rich query language

**Cons**:
- ❌ FHIR is REST-based, not GraphQL
- ❌ Requires GraphQL→FHIR translation layer
- ❌ Not FHIR-compliant

**Decision**: Rejected - must maintain FHIR REST API compliance

## Next Steps

### Immediate (Before ADR)
1. ✅ Complete this investigation
2. ⏳ Review with stakeholders
3. ⏳ Validate fanout pattern alignment with ADR-2506

### Short-term (Phase 1-2)
1. ⏳ Create ADR-2XXX for Multi-Tenancy Data Partitioning Modes
2. ⏳ Update ADR-2500 master roadmap with new phases
3. ⏳ Implement core abstractions (IDataLayerRegistry, IDataLayer)
4. ⏳ Implement isolation mode (SQL per tenant, schema per tenant)

### Long-term (Phase 3-9)
1. ⏳ Implement distributed query executor
2. ⏳ Implement result aggregator
3. ⏳ Build participation management API
4. ⏳ Performance optimization and load testing

## References

- **Fanout Broker Pattern**: [brendankowitz/fhir-server ADR-2506](https://github.com/brendankowitz/fhir-server/blob/personal/bkowitz/copilot/broker/docs/arch/Proposals/adr-2506-fanout-broker-query-service.md)
- **Multi-Tenancy Patterns**: [Microsoft Azure Multi-Tenant SaaS Guidance](https://docs.microsoft.com/en-us/azure/architecture/guide/multitenant/overview)
- **FHIR Search**: [FHIR R4 Search Specification](https://hl7.org/fhir/R4/search.html)
- **Distributed Sorting**: [Merge Sort for Distributed Systems](https://en.wikipedia.org/wiki/Merge_sort#Use_with_tape_drives)

---

**Investigation Status**: ✅ Complete
**Next Action**: Review with team, create ADR-2XXX, update master roadmap
