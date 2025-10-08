# ADR 2510: FHIR Server v2 - Vertical Slice Implementation Roadmap

## Context

Following the architectural foundation established in ADR 2501, this document provides a comprehensive implementation roadmap based on extensive investigation of production patterns, performance optimizations, and modern .NET capabilities. The roadmap emphasizes **vertical slices** that deliver end-to-end functionality incrementally, ensuring each phase produces a working, deployable system while progressively adding complexity.

### Key Design Principles

**F5 Developer Experience**: Every implementation phase must support the principle that a developer can press F5 and run the solution with minimal setup. This means:
- In-memory storage by default (no database required)
- Embedded resources and schema files
- Sensible defaults that "just work"
- Production features are opt-in, not required

**Vertical Slices Over Horizontal Layers**: Build complete features end-to-end rather than finishing entire architectural layers before moving on.

**Nail the Fundamentals**: Focus on correctness, performance, and developer experience for core FHIR operations before adding advanced capabilities.

### Investigation Summary

Our investigation phase produced detailed analysis across multiple dimensions:

**Core Patterns** (from legacy feature analysis):
- 150+ FHIR operations across 9 functional categories
- Multi-version challenges (STU3, R4, R4B, R5)
- Multi-tenant requirements
- Complex search capabilities
- Transaction integrity patterns

**Performance Optimizations** (from memory-efficient patterns):
- Span<T> and Memory<T> for zero-allocation parsing
- RecyclableMemoryStream for GC pressure reduction
- ArrayPool<T> for temporary collections
- Record types for immutable value objects
- Achieves 50-70% reduction in allocations

**Storage Architecture** (from Cosmos 10PB+ investigation):
- Separation of resource data from search indices
- Smart partitioning strategies
- Compact search index patterns (proven in issue #2686)
- 500 physical partition SDK limitation handling
- Sub-second reads, efficient cross-partition queries

**Transaction Management** (from transaction table abstraction):
- Append-only transaction log
- Sequential visibility advancement
- Heartbeat-based timeout detection
- Provider-agnostic design (Cosmos, SQL Server, In-Memory)

**Infrastructure Patterns**:
- Standardized caching (in-memory → Redis)
- Message bus abstraction (Medino → Redis)
- SMART on FHIR v2 native support
- Multi-version IG loading system
- Multi-provider identity abstraction

## Decision

We will implement FHIR Server v2 through **vertical slices** organized into phases. Each phase delivers a complete, working feature set that builds on previous phases, with clear success criteria and production-ready code.

### Phase Organization Principles

1. **Each phase is independently deployable**
2. **Each phase includes 80% minimum test coverage** (xUnit + NSubstitute)
3. **Each phase maintains backward compatibility**
4. **Production optimizations are built-in, not retrofitted**
5. **Documentation and examples ship with code**
6. **Time estimates in Claude Code hours** (assuming ~10x human developer productivity)

## Vertical Slice Roadmap

### Phase 1: Foundation - "Hello FHIR" (~20 Claude Code hours)

**Goal**: Developers can F5 and have a working FHIR server that handles basic Patient CRUD operations with R4.

**Vertical Slice**: `GET /Patient/{id}` and `POST /Patient` working end-to-end.

**Testing**: 80%+ coverage with xUnit + NSubstitute

#### Deliverables

**Core Abstractions** (Week 1):
```csharp
// Essential interfaces only
public interface IFhirRepository
{
    ValueTask<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken ct = default);
    ValueTask<ResourceKey> CreateAsync(ResourceWrapper resource, CancellationToken ct = default);
    ValueTask<ResourceKey> UpdateAsync(ResourceWrapper resource, CancellationToken ct = default);
    ValueTask DeleteAsync(ResourceKey key, CancellationToken ct = default);
}

public record ResourceWrapper(
    string ResourceType,
    string ResourceId,
    string VersionId,
    DateTimeOffset LastModified,
    ISourceNode Resource,
    bool IsDeleted = false);

public record ResourceKey(string ResourceType, string Id, string? VersionId = null);
```

**In-Memory Storage** (Week 1):
- `ConcurrentDictionary`-based storage
- No database required
- Thread-safe implementation
- Resource versioning support
- ~300 lines of code

**R4 Schema Provider** (Week 1):
- Integrate existing `IFhirSchemaProvider` for R4
- Embedded R4 specification files
- Patient resource validation
- Zero configuration required

**Memory-Efficient Serialization** (Week 2):
- `JsonSourceNodeFactory` integration
- `RecyclableMemoryStream` for all I/O
- Span-based JSON parsing for Patient resources
- Achieve <5ms resource parse times

**Minimal API Surface** (Week 2):
```csharp
// ASP.NET Core Minimal API
app.MapGet("/Patient/{id}", async (string id, IFhirRepository repo) =>
{
    var key = new ResourceKey("Patient", id);
    var resource = await repo.GetAsync(key);
    return resource != null ? Results.Ok(resource.Resource) : Results.NotFound();
});

app.MapPost("/Patient", async (HttpRequest request, IFhirRepository repo) =>
{
    using var stream = FhirStreamManager.GetStream("CreatePatient");
    await request.Body.CopyToAsync(stream);

    var sourceNode = JsonSourceNodeFactory.Parse(stream.GetBuffer().AsSpan(0, (int)stream.Length));
    var resource = new ResourceWrapper("Patient", Guid.NewGuid().ToString(), "1", DateTimeOffset.UtcNow, sourceNode);

    var key = await repo.CreateAsync(resource);
    return Results.Created($"/Patient/{key.Id}", resource.Resource);
});
```

**Basic Testing** (Week 3):
- Unit tests for in-memory repository
- Integration tests for API endpoints
- Performance benchmarks (target: <10ms per operation)
- Example Patient resources

**Success Criteria**:
- ✅ Clone repo, run `dotnet run`, server starts
- ✅ POST Patient resource, receive 201 Created
- ✅ GET Patient by ID, receive 200 OK
- ✅ Update Patient, version increments
- ✅ Delete Patient (soft delete)
- ✅ All operations <10ms in-memory
- ✅ Zero external dependencies
- ✅ 80%+ test coverage

**E2E Test Success Criteria** (from src-old/test):
- ✅ `CreateTests.cs` - All basic create scenarios pass
- ✅ `ReadTests.cs` - All read scenarios pass
- ✅ `UpdateTests.cs` - All update scenarios pass
- ✅ `DeleteTests.cs` - All delete scenarios pass
- ✅ `VReadTests.cs` - Versioned resource reads pass
- ✅ `HistoryTests.cs` - Resource history retrieval passes
- ✅ `MetadataTests.cs` - CapabilityStatement endpoint returns valid metadata
- ✅ `HealthTests.cs` - Health check endpoints operational

**Deliverable**: Working FHIR server in ~1,500 lines of code that demonstrates core patterns.

---

### Phase 2: Search Foundation - "Find Patients" (Weeks 4-6)

**Goal**: Add basic search capabilities for Patient resources.

**Vertical Slice**: `GET /Patient?name=John` returns matching patients.

#### Deliverables

**Search Abstraction** (Week 4):
```csharp
public interface ISearchService
{
    ValueTask<SearchResult> SearchAsync(
        string resourceType,
        IReadOnlyDictionary<string, StringValues> parameters,
        int? count = null,
        string? continuationToken = null,
        CancellationToken ct = default);
}

public record SearchResult(
    IReadOnlyList<ResourceWrapper> Resources,
    int? TotalCount,
    string? ContinuationToken);

public record SearchParameter(
    string Name,
    SearchParameterType Type,
    ReadOnlyMemory<char> Value,
    SearchModifier Modifier = SearchModifier.None);
```

**Search Index Extraction** (Week 4):
- Integrate existing `SearchIndexerFactory`
- Extract search parameters from Patient resources
- String, Token, Date parameter types
- Build in-memory search indices

**In-Memory Search Implementation** (Week 5):
```csharp
public class InMemorySearchService : ISearchService
{
    private readonly ConcurrentDictionary<string, List<SearchIndexEntry>> _indices = new();

    public async ValueTask<SearchResult> SearchAsync(...)
    {
        // Use ArrayPool for temporary result aggregation
        var resultsBuilder = new FhirSearchResultBuilder(estimatedSize: 50);

        // Span-based parameter matching
        foreach (var entry in _indices[resourceType])
        {
            if (MatchesSearchParameters(entry, parameters))
            {
                resultsBuilder.Add(entry.Resource);
            }
        }

        return resultsBuilder.Build();
    }
}
```

**Search Parameter Support** (Week 5):
- `name` (string)
- `family` (string)
- `given` (string)
- `birthdate` (date)
- `identifier` (token)
- `_id` (special)
- `_lastUpdated` (special)

**Search Modifiers** (Week 6):
- `:exact` for string matching
- `:contains` for partial matching
- `:missing` for null checks
- Result sorting
- Pagination with continuation tokens

**Bundle Support** (Week 6):
```csharp
public class BundleBuilder
{
    public Bundle CreateSearchBundle(SearchResult result, string requestUrl)
    {
        using var stream = FhirStreamManager.GetStream("SearchBundle");
        // Build Bundle with search results
        // Include pagination links
        // Add search metadata
    }
}
```

**Success Criteria**:
- ✅ Search by name returns correct patients
- ✅ Search by multiple parameters (AND logic)
- ✅ Pagination works with large result sets
- ✅ Search completes in <50ms for 10,000 resources
- ✅ Memory usage stays bounded with ArrayPool
- ✅ Bundle format follows FHIR spec
- ✅ 80%+ test coverage for search

**E2E Test Success Criteria** (from src-old/test):
- ✅ `Search/BasicSearchTests.cs` - Multi-parameter search passes
- ✅ `Search/StringSearchTests.cs` - String search with modifiers passes
- ✅ `Search/TokenSearchTests.cs` - Token/identifier search passes
- ✅ `Search/DateSearchTests.cs` - Date range search passes
- ✅ `Search/SortTests.cs` - Result sorting passes
- ⚠️ Reference/chaining tests deferred to Phase 5

**Deliverable**: Production-quality search for Patient resources with excellent performance.

---

### Phase 3: Multi-Resource CRUD - "Observation & Encounters" (Weeks 7-9)

**Goal**: Extend CRUD operations to additional resource types.

**Vertical Slice**: Support Observation and Encounter resources with references.

#### Deliverables

**Generic Resource Handling** (Week 7):
- Extend repository to support any resource type
- Dynamic schema resolution via `IFhirSchemaProvider`
- Resource type validation
- Reference validation

**Resource References** (Week 7):
```csharp
public record FhirReference(string Type, string Id, string? Version = null, string? Display = null);

public static class ReferenceResolver
{
    public static async ValueTask<bool> ValidateReferenceAsync(
        FhirReference reference,
        IFhirRepository repository,
        CancellationToken ct)
    {
        var key = new ResourceKey(reference.Type, reference.Id);
        var resource = await repository.GetAsync(key, ct);
        return resource != null && !resource.IsDeleted;
    }
}
```

**Observation Support** (Week 8):
- Observation CRUD operations
- Reference to Patient (subject)
- Reference to Encounter (encounter)
- CodeableConcept handling
- Quantity value handling

**Encounter Support** (Week 8):
- Encounter CRUD operations
- Reference to Patient (subject)
- Period handling
- Status codes

**Search Extension** (Week 9):
- Observation search by patient
- Observation search by date
- Encounter search by patient
- Encounter search by date
- Reference chaining: `Observation?patient.name=John`

**Success Criteria**:
- ✅ Create Observation with valid Patient reference
- ✅ Reject Observation with invalid reference
- ✅ Search Observations by patient
- ✅ Search Observations by date range
- ✅ Chain search across references
- ✅ Support all three resource types
- ✅ Performance remains <10ms for CRUD, <50ms for search

**E2E Test Success Criteria** (from src-old/test):
- ✅ `CreateTests.cs` - All resource types can be created
- ✅ `Search/ReferenceSearchTests.cs` - Reference parameter search passes
- ✅ `Search/ChainingSearchTests.cs` - Chained search (Patient.name) passes
- ✅ `ExceptionTests.cs` - Invalid reference validation errors handled correctly
- ⚠️ Full reference search deferred to Phase 5

**Deliverable**: Multi-resource FHIR server with reference integrity.

---

### Phase 4: Transaction Support - "Bundle Transactions" (Weeks 10-12)

**Goal**: Add FHIR bundle transaction support with ACID guarantees.

**Vertical Slice**: `POST /` with transaction bundle creates multiple resources atomically.

#### Deliverables

**Transaction Abstraction** (Week 10):
```csharp
public interface ITransactionContext
{
    TransactionId? CurrentTransactionId { get; }
    ValueTask<ITransactionScope> BeginTransactionAsync(
        int resourceCount,
        string? definition = null,
        CancellationToken ct = default);
}

public interface ITransactionScope : IAsyncDisposable
{
    TransactionId TransactionId { get; }
    ValueTask UpdateHeartbeatAsync(CancellationToken ct = default);
    ValueTask CommitAsync(CancellationToken ct = default);
    ValueTask FailAsync(string reason, CancellationToken ct = default);
}
```

**In-Memory Transaction Implementation** (Week 10):
- Transaction log in memory
- Append-only transaction entries
- Sequential visibility watermark
- Heartbeat-based timeout detection
- Rollback support

**Bundle Processor** (Week 11):
```csharp
public class BundleProcessor
{
    public async ValueTask<Bundle> ProcessTransactionAsync(
        Bundle bundle,
        CancellationToken ct)
    {
        var entryCount = bundle.Entry?.Count ?? 0;
        using var transaction = await _transactionContext.BeginTransactionAsync(entryCount);

        try
        {
            var results = new List<BundleEntry>();

            // First pass: resolve references
            var referenceMap = BuildReferenceMap(bundle);

            // Second pass: execute operations
            foreach (var entry in bundle.Entry)
            {
                await transaction.UpdateHeartbeatAsync(ct);
                var result = await ProcessEntryAsync(entry, referenceMap, ct);
                results.Add(result);
            }

            await transaction.CommitAsync(ct);
            return CreateResponseBundle(results);
        }
        catch (Exception ex)
        {
            await transaction.FailAsync(ex.Message, ct);
            throw;
        }
    }
}
```

**Conditional Operations** (Week 11):
- Conditional create (`If-None-Exist`)
- Conditional update (`If-Match`)
- Conditional delete (search-based)
- Reference resolution within bundle

**Transaction Watchdog** (Week 12):
```csharp
public class TransactionWatchdogService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessVisibilityAsync(stoppingToken);
            await ProcessTimeoutsAsync(stoppingToken);
            await Task.Delay(_options.CheckInterval, stoppingToken);
        }
    }
}
```

**Success Criteria**:
- ✅ Transaction bundle creates multiple resources atomically
- ✅ Rollback on failure leaves no partial state
- ✅ Reference resolution works within bundle
- ✅ Conditional create prevents duplicates
- ✅ Watchdog advances visibility correctly
- ✅ Timeout detection and recovery works
- ✅ Performance: 100-resource bundle in <500ms

**E2E Test Success Criteria** (from src-old/test):
- ✅ `BundleTransactionTests.cs` - **ALL transaction tests must pass**
- ✅ `BundleBatchTests.cs` - **ALL batch tests must pass**
- ✅ `BundleEdgeCaseTests.cs` - **ALL edge case tests must pass**
- ✅ `ConditionalCreateTests.cs` - ALL conditional create tests pass
- ✅ `ConditionalUpdateTests.cs` - ALL conditional update tests pass
- ✅ `ConditionalDeleteTests.cs` - ALL conditional delete tests pass
- ⚠️ **Note**: CosmosDB transaction support NOT required (legacy returns 405)

**Deliverable**: Production-grade transaction support with integrity guarantees.

---

### Phase 5: File Storage - "Persistent Data" (Weeks 13-15)

**Goal**: Add file-based storage for persistence without database setup.

**Vertical Slice**: Data survives server restart using file storage.

#### Deliverables

**File Storage Implementation** (Week 13):
```csharp
public class FileSystemFhirRepository : IFhirRepository
{
    private readonly string _basePath;
    private readonly MemoryPool<byte> _bufferPool = MemoryPool<byte>.Shared;

    public async ValueTask<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken ct)
    {
        var filePath = GetResourcePath(key);
        if (!File.Exists(filePath)) return null;

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var memory = _bufferPool.Rent((int)fileStream.Length);

        await fileStream.ReadAsync(memory.Memory[..(int)fileStream.Length], ct);
        return DeserializeResource(memory.Memory[..(int)fileStream.Length].Span);
    }
}
```

**Storage Partitioning** (Week 13):
- Partition by resource type
- Partition by year-month
- Efficient directory structure:
  ```
  ./data/
    Patient/
      2025-01/
        {id}.json
        {id}.meta.json
    Observation/
      2025-01/
        {id}.json
        {id}.meta.json
  ```

**Search Index Files** (Week 14):
- Separate index files per resource type
- Memory-mapped file support for large indices
- Incremental index updates
- Compact binary format for search metadata

**Transaction Log File** (Week 14):
- Append-only transaction log file
- Visibility watermark persistence
- Crash recovery support
- Log compaction

**Performance Optimization** (Week 15):
- File handle pooling
- Read-ahead caching
- Write-behind buffering
- Lock-free concurrent reads

**Success Criteria**:
- ✅ Create 1,000 resources, restart server, all resources present
- ✅ Search indices rebuild from resource files
- ✅ Transaction log recovers in-flight transactions
- ✅ Performance within 2x of in-memory (CRUD <20ms, search <100ms)
- ✅ Handles 100,000+ resources efficiently
- ✅ No data corruption under concurrent access
- ✅ Graceful handling of disk full

**Deliverable**: Production-ready file storage with crash recovery.

---

### Phase 6: Multi-Version Support - "STU3, R4, R4B, R5" (Weeks 16-20)

**Goal**: Support all FHIR versions from single deployment.

**Vertical Slice**: `GET /{version}/Patient/{id}` routes to correct schema provider.

#### Deliverables

**FHIR Version Context** (Week 16):
```csharp
public record FhirVersionContext(
    FhirVersion Version,
    IFhirSchemaProvider SchemaProvider,
    ICapabilityStatement CapabilityStatement);

public interface IFhirVersionResolver
{
    ValueTask<FhirVersionContext> ResolveVersionAsync(HttpRequest request, CancellationToken ct = default);
}
```

**Schema Provider Factory** (Week 16):
```csharp
public class FhirSchemaProviderFactory
{
    private static readonly ConcurrentDictionary<FhirVersion, IFhirSchemaProvider> _providers = new();

    public IFhirSchemaProvider GetProvider(FhirVersion version)
    {
        return _providers.GetOrAdd(version, CreateProvider);
    }

    private static IFhirSchemaProvider CreateProvider(FhirVersion version)
    {
        return version switch
        {
            FhirVersion.Stu3 => new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.STU3),
            FhirVersion.R4 => new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4),
            FhirVersion.R4B => new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R4B),
            FhirVersion.R5 => new FhirJsonSchemaStructureDefinitionSummaryProvider(FhirSpecification.R5),
            _ => throw new NotSupportedException($"FHIR version {version} not supported")
        };
    }
}
```

**Version-Specific Routes** (Week 17):
```csharp
app.MapGroup("/stu3")
   .WithFhirVersion(FhirVersion.Stu3)
   .MapFhirEndpoints();

app.MapGroup("/R4")
   .WithFhirVersion(FhirVersion.R4)
   .MapFhirEndpoints();

app.MapGroup("/R4B")
   .WithFhirVersion(FhirVersion.R4B)
   .MapFhirEndpoints();

app.MapGroup("/R5")
   .WithFhirVersion(FhirVersion.R5)
   .MapFhirEndpoints();
```

**Version-Agnostic Storage** (Week 17):
- Store version information in metadata
- Version-aware serialization
- Cross-version compatibility checks
- Migration utilities

**Capability Statements** (Week 18):
- Generate capability statement per version
- Embedded capability statement templates
- Runtime capability computation
- Extension support

**Search Parameter Mapping** (Week 19):
- Version-specific search parameters
- Cross-version search parameter mapping
- Handle deprecated parameters
- Version negotiation

**Testing & Validation** (Week 20):
- Test suite for each FHIR version
- Cross-version compatibility tests
- Performance regression tests
- Example resources for all versions

**Success Criteria**:
- ✅ All 4 FHIR versions work from single deployment
- ✅ Capability statements accurate for each version
- ✅ Search parameters correct per version
- ✅ Storage handles version differences
- ✅ Performance consistent across versions
- ✅ Zero version-specific code duplication
- ✅ 90%+ test coverage per version

**Deliverable**: True multi-version FHIR server with zero code duplication.

---

### Phase 7: Multi-Tenant Foundation - "Tenant Isolation" (Weeks 21-24)

**Goal**: Support multiple tenants with data isolation.

**Vertical Slice**: `/{tenantId}/R4/Patient/{id}` provides tenant-scoped access.

#### Deliverables

**Tenant Context** (Week 21):
```csharp
public record TenantContext(
    string TenantId,
    TenantConfiguration Configuration,
    FhirVersionContext FhirVersion);

public interface ITenantResolver
{
    ValueTask<TenantContext> ResolveTenantAsync(HttpRequest request, CancellationToken ct = default);
}

public record TenantConfiguration(
    string TenantId,
    FhirVersion[] SupportedVersions,
    IReadOnlyDictionary<string, object> Settings);
```

**Tenant-Scoped Repository** (Week 21):
```csharp
public class TenantScopedRepository : IFhirRepository
{
    private readonly IFhirRepository _innerRepository;
    private readonly string _tenantId;

    public async ValueTask<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken ct)
    {
        // Add tenant prefix to key
        var tenantKey = new ResourceKey($"{_tenantId}/{key.ResourceType}", key.Id, key.VersionId);
        return await _innerRepository.GetAsync(tenantKey, ct);
    }
}
```

**Tenant Routing** (Week 22):
```csharp
app.MapGroup("/{tenantId}/R4")
   .WithTenantResolution()
   .WithFhirVersion(FhirVersion.R4)
   .MapFhirEndpoints();
```

**Tenant Storage Isolation** (Week 22):
- File storage: `./data/{tenantId}/Patient/...`
- Search indices per tenant
- Transaction log per tenant
- No cross-tenant data leakage

**Tenant Configuration Service** (Week 23):
```csharp
public interface ITenantConfigurationService
{
    ValueTask<TenantConfiguration> GetConfigurationAsync(string tenantId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<string>> GetAllTenantsAsync(CancellationToken ct = default);
    ValueTask CreateTenantAsync(TenantConfiguration config, CancellationToken ct = default);
}
```

**Tenant Management API** (Week 23):
- Create new tenant
- Update tenant configuration
- List tenants
- Delete tenant (with data)
- Tenant health status

**Tenant-Aware Caching** (Week 24):
```csharp
public interface ITenantCache
{
    ICache GetTenantCache(string tenantId);
    ValueTask ClearTenantAsync(string tenantId, CancellationToken ct = default);
}
```

**Success Criteria**:
- ✅ Create tenant, POST resource, GET resource works
- ✅ Tenant A cannot access tenant B's data
- ✅ Search scoped correctly per tenant
- ✅ Transactions isolated per tenant
- ✅ Performance: 1,000 tenants with no degradation
- ✅ Tenant configuration dynamic
- ✅ 100% data isolation verified

**Deliverable**: Secure multi-tenant FHIR server with complete data isolation.

---

### Phase 8: Advanced Search - "Chaining & Includes" (Weeks 25-28)

**Goal**: Add advanced search capabilities.

**Vertical Slice**: `GET /Observation?patient.name=John&_include=Observation:patient` works.

#### Deliverables

**Chained Search** (Week 25):
```csharp
public class ChainedSearchResolver
{
    public async ValueTask<SearchResult> ResolveChainAsync(
        string resourceType,
        SearchParameter chainParameter,
        CancellationToken ct)
    {
        // Parse: patient.name=John
        // 1. Search Patient?name=John
        // 2. Get patient IDs
        // 3. Search Observation?patient={ids}

        var (refParam, chainedParam) = ParseChain(chainParameter);

        // Execute chained search
        var referencedResources = await _searchService.SearchAsync(
            refParam.TargetType,
            new[] { chainedParam },
            ct);

        var ids = referencedResources.Resources.Select(r => r.ResourceId);

        return await _searchService.SearchAsync(
            resourceType,
            new[] { new SearchParameter(refParam.Name, string.Join(",", ids)) },
            ct);
    }
}
```

**Reverse Chained Search** (Week 25):
```csharp
// _has parameter support
// Patient?_has:Observation:patient:code=123
```

**Include Support** (Week 26):
```csharp
public class IncludeProcessor
{
    public async ValueTask<SearchResult> ProcessIncludesAsync(
        SearchResult baseResult,
        string[] includeParams,
        CancellationToken ct)
    {
        var includedResources = new List<ResourceWrapper>();

        foreach (var includeParam in includeParams)
        {
            // Parse: Observation:patient
            var (resourceType, refPath) = ParseInclude(includeParam);

            // Extract references from search results
            var references = ExtractReferences(baseResult.Resources, refPath);

            // Fetch referenced resources
            foreach (var reference in references)
            {
                var resource = await _repository.GetAsync(new ResourceKey(reference.Type, reference.Id), ct);
                if (resource != null)
                {
                    includedResources.Add(resource);
                }
            }
        }

        return baseResult with { IncludedResources = includedResources };
    }
}
```

**RevInclude Support** (Week 27):
```csharp
// _revinclude parameter support
// Patient?_revinclude=Observation:patient
// Returns patients and observations that reference them
```

**Composite Search Parameters** (Week 27):
```csharp
public record CompositeSearchParameter(
    string Name,
    SearchParameter[] Components);

// Example: Observation?component-code-value-quantity=http://loinc.org|8480-6$lt60
```

**Advanced Modifiers** (Week 28):
- `:not` (negation)
- `:above` (hierarchical codes)
- `:below` (hierarchical codes)
- `:text` (narrative search)
- `:in` (ValueSet membership)
- `:not-in` (ValueSet exclusion)

**Success Criteria**:
- ✅ Chained search returns correct results
- ✅ _include adds referenced resources
- ✅ _revinclude adds referencing resources
- ✅ Composite parameters work correctly
- ✅ Performance: chained search <200ms
- ✅ Handles circular references
- ✅ Memory-efficient with large include sets

**Deliverable**: Advanced search matching major EHR capabilities.

---

### Phase 9: Distributed Infrastructure - "Scale Out" (Weeks 29-34)

**Goal**: Support web farm deployment with distributed caching and messaging.

**Vertical Slice**: Deploy to multiple servers with shared state.

#### Deliverables

**Distributed Caching (Redis)** (Week 29-30):
```csharp
public class RedisCache : ICache
{
    private readonly IDatabase _database;
    private readonly ISerializer _serializer;

    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        var value = await _database.StringGetAsync(key);
        if (!value.HasValue) return default;

        using var stream = FhirStreamManager.GetStream(value, "CacheGet");
        return await _serializer.DeserializeAsync<T>(stream, ct);
    }

    public async ValueTask SetAsync<T>(string key, T value, TimeSpan? expiration, CancellationToken ct)
    {
        using var stream = FhirStreamManager.GetStream("CacheSet");
        await _serializer.SerializeAsync(stream, value, ct);
        var serializedValue = stream.ToArray();

        await _database.StringSetAsync(key, serializedValue, expiration);
    }
}
```

**Distributed Messaging (Redis)** (Week 30-31):
```csharp
public class RedisMessageBus : IMessageBus
{
    private readonly ISubscriber _subscriber;
    private readonly IDatabase _database;

    public async ValueTask<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct)
    {
        // Serialize command to Redis queue
        var channel = GetCommandChannel(command.GetType());
        var responseChannel = GetResponseChannel(command.MessageId);

        // Send command
        await _database.ListLeftPushAsync(channel, SerializeCommand(command));

        // Wait for response on pub/sub
        var response = await WaitForResponseAsync<TResponse>(responseChannel, ct);
        return response;
    }
}
```

**Configuration Abstraction** (Week 31):
```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFhirInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cacheType = config.GetValue<string>("Cache:Type", "Memory");
        var messagingType = config.GetValue<string>("Messaging:Type", "Medino");

        // Caching
        if (cacheType == "Redis")
        {
            services.AddRedisCache(config);
        }
        else
        {
            services.AddMemoryCache(config);
        }

        // Messaging
        if (messagingType == "Redis")
        {
            services.AddRedisMessageBus(config);
        }
        else
        {
            services.AddMedinoMessageBus(config);
        }

        return services;
    }
}
```

**Message Processor Worker** (Week 32):
```csharp
public class RedisMessageProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>
        {
            ProcessCommandsAsync(stoppingToken),
            ProcessQueriesAsync(stoppingToken),
            ProcessEventsAsync(stoppingToken)
        };

        await Task.WhenAll(tasks);
    }
}
```

**Health Checks** (Week 33):
```csharp
public class FhirServerHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        // Check repository
        // Check cache
        // Check message bus
        // Check storage

        return HealthCheckResult.Healthy("All systems operational");
    }
}
```

**Load Testing** (Week 34):
- JMeter test scripts
- 1,000 concurrent users
- Mixed read/write workload
- Performance baselines
- Scalability verification

**Success Criteria**:
- ✅ Deploy to 3 nodes, all serve requests
- ✅ Cache shared across nodes
- ✅ Messages processed by any node
- ✅ No session affinity required
- ✅ Linear scalability up to 10 nodes
- ✅ Health checks report status
- ✅ Zero downtime deployment

**Deliverable**: Web farm ready FHIR server with distributed infrastructure.

---

### Phase 10: SQL Server Storage - "Enterprise Database" (Weeks 35-40)

**Goal**: Add SQL Server storage implementation.

**Vertical Slice**: Production deployment with SQL Server backend.

#### Deliverables

**SQL Server Repository** (Week 35-36):
```csharp
public class SqlServerFhirRepository : IFhirRepository
{
    private readonly SqlConnection _connection;

    public async ValueTask<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken ct)
    {
        const string sql = @"
            SELECT ResourceJson, VersionId, LastModified, IsDeleted
            FROM Resources
            WHERE TenantId = @TenantId
              AND ResourceType = @ResourceType
              AND ResourceId = @ResourceId
              AND (@VersionId IS NULL OR VersionId = @VersionId)
            ORDER BY VersionId DESC
            OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY";

        using var cmd = new SqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@TenantId", _tenantId);
        cmd.Parameters.AddWithValue("@ResourceType", key.ResourceType);
        cmd.Parameters.AddWithValue("@ResourceId", key.Id);
        cmd.Parameters.AddWithValue("@VersionId", (object?)key.VersionId ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var json = (byte[])reader["ResourceJson"];
        return DeserializeResource(json);
    }
}
```

**Database Schema** (Week 36):
```sql
CREATE TABLE Resources (
    TenantId NVARCHAR(64) NOT NULL,
    ResourceType NVARCHAR(64) NOT NULL,
    ResourceId NVARCHAR(64) NOT NULL,
    VersionId NVARCHAR(64) NOT NULL,
    ResourceJson VARBINARY(MAX) NOT NULL,
    LastModified DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    IsDeleted BIT NOT NULL DEFAULT 0,
    Hash AS CAST(HASHBYTES('SHA2_256', ResourceJson) AS NVARCHAR(64)),

    CONSTRAINT PK_Resources PRIMARY KEY CLUSTERED (
        TenantId, ResourceType, ResourceId, VersionId DESC
    )
);

CREATE NONCLUSTERED INDEX IX_Resources_LastModified
    ON Resources(TenantId, ResourceType, LastModified DESC);

CREATE NONCLUSTERED INDEX IX_Resources_IsDeleted
    ON Resources(TenantId, ResourceType, IsDeleted)
    WHERE IsDeleted = 0;
```

**Search Indices Table** (Week 37):
```sql
CREATE TABLE SearchIndices (
    TenantId NVARCHAR(64) NOT NULL,
    ResourceType NVARCHAR(64) NOT NULL,
    ResourceId NVARCHAR(64) NOT NULL,
    VersionId NVARCHAR(64) NOT NULL,
    ParameterName NVARCHAR(128) NOT NULL,
    ParameterType NVARCHAR(32) NOT NULL,
    StringValue NVARCHAR(256),
    TokenSystem NVARCHAR(256),
    TokenCode NVARCHAR(256),
    NumberValue DECIMAL(18,6),
    DateStart DATETIMEOFFSET,
    DateEnd DATETIMEOFFSET,
    ReferenceType NVARCHAR(64),
    ReferenceId NVARCHAR(64),

    CONSTRAINT PK_SearchIndices PRIMARY KEY CLUSTERED (
        TenantId, ResourceType, ParameterName, ResourceId
    )
);

CREATE NONCLUSTERED INDEX IX_SearchIndices_String
    ON SearchIndices(TenantId, ResourceType, ParameterName, StringValue)
    WHERE ParameterType = 'String';

CREATE NONCLUSTERED INDEX IX_SearchIndices_Token
    ON SearchIndices(TenantId, ResourceType, ParameterName, TokenSystem, TokenCode)
    WHERE ParameterType = 'Token';

CREATE NONCLUSTERED INDEX IX_SearchIndices_Date
    ON SearchIndices(TenantId, ResourceType, ParameterName, DateStart, DateEnd)
    WHERE ParameterType = 'Date';
```

**SQL Transaction Implementation** (Week 38):
```csharp
public class SqlServerTransactionRepository : ITransactionRepository
{
    public async ValueTask<TransactionEntry> BeginTransactionAsync(
        int resourceCount,
        string? definition,
        CancellationToken ct)
    {
        const string sql = @"
            DECLARE @TransactionId BIGINT;
            EXEC sp_GetTransactionIdRange @ResourceCount, @TransactionId OUTPUT;

            INSERT INTO Transactions (TransactionId, FirstId, LastId, ResourceCount, Definition, CreateDate, HeartbeatDate)
            VALUES (@TransactionId, @TransactionId, @TransactionId + @ResourceCount - 1, @ResourceCount, @Definition, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

            SELECT TransactionId, FirstId, LastId FROM Transactions WHERE TransactionId = @TransactionId;";

        // Execute and return transaction entry
    }
}
```

**Search Performance Optimization** (Week 39):
- Indexed views for common searches
- Full-text search integration
- Query plan optimization
- Statistics maintenance

**Migration & Deployment** (Week 40):
- Migration scripts from file storage
- Zero-downtime migration strategy
- Backup and restore procedures
- Performance tuning guide

**Success Criteria**:
- ✅ All CRUD operations work with SQL Server
- ✅ Search performance <100ms for 1M resources
- ✅ Transactions maintain ACID guarantees
- ✅ Concurrent writes handle correctly
- ✅ Migration from file storage succeeds
- ✅ Backup/restore procedures tested
- ✅ Performance meets or exceeds file storage

**Deliverable**: Production-ready SQL Server storage implementation.

---

### Phase 11: Cosmos DB Storage - "Planet Scale" (Weeks 41-48)

**Goal**: Add Cosmos DB storage for massive scale.

**Vertical Slice**: Deploy with Cosmos DB handling 10PB+ scenarios.

#### Deliverables

**Cosmos DB Container Design** (Week 41-42):
```csharp
// Resources Container
var resourcesContainer = await database.CreateContainerIfNotExistsAsync(
    new ContainerProperties
    {
        Id = "Resources",
        PartitionKeyPath = "/partitionKey", // {ResourceType}|{YearMonth}|{TenantId}
        IndexingPolicy = new IndexingPolicy
        {
            Automatic = true,
            IndexingMode = IndexingMode.Consistent,
            IncludedPaths = { new IncludedPath { Path = "/*" } },
            ExcludedPaths = { new ExcludedPath { Path = "/resource/*" } } // Don't index resource body
        }
    },
    throughput: 400); // Autoscale

// SearchIndices Container (optimized pattern from issue #2686)
var searchContainer = await database.CreateContainerIfNotExistsAsync(
    new ContainerProperties
    {
        Id = "SearchIndices",
        PartitionKeyPath = "/partitionKey", // {ResourceType}|{ParameterName}|{TenantId}
        IndexingPolicy = OptimizedSearchIndexingPolicy()
    });
```

**Cosmos Repository Implementation** (Week 42-43):
```csharp
public class CosmosDbFhirRepository : IFhirRepository
{
    private readonly Container _resourcesContainer;
    private readonly Container _searchContainer;

    public async ValueTask<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken ct)
    {
        // Direct point read (fastest operation in Cosmos)
        var partitionKey = CreateResourcePartitionKey(key.ResourceType, DateTime.UtcNow, _tenantId);
        var documentId = $"{key.ResourceType}-{key.Id}";

        try
        {
            var response = await _resourcesContainer.ReadItemAsync<CosmosResourceDocument>(
                documentId,
                new PartitionKey(partitionKey),
                cancellationToken: ct);

            return response.Resource.ToResourceWrapper();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Try historical partitions
            return await SearchHistoricalPartitionsAsync(key, ct);
        }
    }
}
```

**Optimized Search Implementation** (Week 43-44):
```csharp
public class CosmosDbSearchService : ISearchService
{
    public async ValueTask<SearchResult> SearchAsync(
        string resourceType,
        IReadOnlyDictionary<string, StringValues> parameters,
        int? count,
        string? continuationToken,
        CancellationToken ct)
    {
        // Use optimized search pattern from issue #2686
        var searchQuery = BuildOptimizedSearchQuery(resourceType, parameters);

        // Query search indices container
        var searchIterator = _searchContainer.GetItemQueryIterator<SearchDocument>(searchQuery);

        var results = new List<ResourceWrapper>();
        while (searchIterator.HasMoreResults && results.Count < (count ?? 50))
        {
            var searchResponse = await searchIterator.ReadNextAsync(ct);

            // Fetch actual resources (could be optimized with projection)
            foreach (var searchDoc in searchResponse)
            {
                var resource = await GetAsync(new ResourceKey(resourceType, searchDoc.ResourceId), ct);
                if (resource != null)
                {
                    results.Add(resource);
                }
            }
        }

        return new SearchResult(results, null, null);
    }
}
```

**Partition Management** (Week 44-45):
- Automatic partition key generation
- Historical partition queries
- Cross-partition query optimization
- 500 physical partition limit handling

**Cosmos Transaction Support** (Week 45-46):
```csharp
public class CosmosDbTransactionRepository : ITransactionRepository
{
    public async ValueTask<TransactionEntry> BeginTransactionAsync(
        int resourceCount,
        string? definition,
        CancellationToken ct)
    {
        // Use Cosmos stored procedure for transaction ID allocation
        var transactionId = await AllocateTransactionIdAsync(resourceCount, ct);

        var transactionDoc = new TransactionDocument
        {
            Id = transactionId.ToString(),
            PartitionKey = $"tx|{_tenantId}",
            FirstId = transactionId,
            LastId = transactionId + resourceCount - 1,
            ResourceCount = resourceCount,
            Definition = definition,
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        };

        await _transactionsContainer.CreateItemAsync(transactionDoc, new PartitionKey(transactionDoc.PartitionKey), cancellationToken: ct);

        return transactionDoc.ToTransactionEntry();
    }
}
```

**Bulk Operations** (Week 46-47):
```csharp
public class CosmosBulkOperations
{
    public async ValueTask<BulkWriteResult> BulkWriteAsync(
        IReadOnlyList<ResourceWrapper> resources,
        CancellationToken ct)
    {
        var tasks = new List<Task<ItemResponse<CosmosResourceDocument>>>();

        // Cosmos SDK supports 100 concurrent operations per partition
        using var semaphore = new SemaphoreSlim(100);

        foreach (var resource in resources)
        {
            await semaphore.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var doc = CosmosResourceDocument.FromResourceWrapper(resource);
                    return await _resourcesContainer.CreateItemAsync(doc, new PartitionKey(doc.PartitionKey), cancellationToken: ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        var results = await Task.WhenAll(tasks);

        return new BulkWriteResult
        {
            SuccessfulResources = results.Count(r => r.StatusCode == HttpStatusCode.Created),
            TotalRU = results.Sum(r => r.RequestCharge)
        };
    }
}
```

**Performance Testing** (Week 48):
- 10 million resource load test
- Cross-partition search benchmarks
- RU optimization
- Scaling verification

**Success Criteria**:
- ✅ Point reads <5ms, <5 RU
- ✅ Single-partition search <50ms, <50 RU
- ✅ Cross-partition search <500ms, <500 RU
- ✅ Bulk write 1,000 resources <30s
- ✅ Handles 10M+ resources
- ✅ Automatic scaling works
- ✅ Cost-optimized queries

**Deliverable**: Planet-scale Cosmos DB storage implementation.

---

### Phase 12: SMART on FHIR - "OAuth 2.0 Integration" (Weeks 49-54)

**Goal**: Add SMART on FHIR v2 authorization.

**Vertical Slice**: Third-party app can authorize and access patient data.

#### Deliverables

**SMART Configuration** (Week 49):
```csharp
public class SmartConfigurationService : ISmartConfiguration
{
    public ValueTask<SmartWellKnownConfiguration> GetWellKnownConfigurationAsync(
        string tenantId,
        CancellationToken ct)
    {
        var config = new SmartWellKnownConfiguration
        {
            Issuer = $"https://fhir.example.com/{tenantId}",
            JwksUri = new Uri($"https://fhir.example.com/{tenantId}/smart/.well-known/jwks.json"),
            ScopesSupported = new[]
            {
                "openid", "profile", "email", "offline_access",
                "patient/*.read", "patient/*.write",
                "user/*.read", "user/*.write",
                "system/*.read", "system/*.write"
            },
            ResponseTypesSupported = new[] { "code" },
            Capabilities = SmartCapabilities.LaunchStandalone |
                          SmartCapabilities.ClientConfidential |
                          SmartCapabilities.SsoOpenidConnect |
                          SmartCapabilities.PermissionPatient
        };

        return ValueTask.FromResult(config);
    }
}
```

**Authorization Endpoints** (Week 50):
```csharp
[ApiController]
[Route("{tenantId}/smart")]
public class SmartController : ControllerBase
{
    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize([FromQuery] SmartAuthorizationRequest request)
    {
        // Validate client
        var client = await _clientService.GetClientAsync(request.TenantId, request.ClientId);
        if (client == null) return BadRequest("Invalid client");

        // Validate scopes
        var scopes = request.Scope.Split(' ');
        var validatedScopes = await _authService.ValidateScopesAsync(scopes);

        // Generate authorization code
        var authCode = await _authService.GenerateAuthorizationCodeAsync(request);

        // Redirect to callback
        var redirectUri = new UriBuilder(request.RedirectUri);
        redirectUri.Query = $"code={authCode}&state={request.State}";

        return Redirect(redirectUri.ToString());
    }

    [HttpPost("token")]
    public async Task<ActionResult<SmartTokenResponse>> Token([FromForm] SmartTokenRequest request)
    {
        var response = await _tokenService.ExchangeCodeAsync(request);
        return Ok(response);
    }
}
```

**Scope Parsing** (Week 51):
```csharp
public static class SmartScopeParser
{
    private static readonly Regex ScopePattern = new(
        @"^(?<context>patient|user|system)\/(?<resource>\*|[A-Z][a-zA-Z]*(?:\.[a-zA-Z]+)*)\.(?<interaction>read|write|\*|c|r|u|d|s)(?<constraint>:\w+(?:[|&]\w+)*)?$",
        RegexOptions.Compiled);

    public static SmartScope ParseScope(ReadOnlySpan<char> scopeString)
    {
        var match = ScopePattern.Match(scopeString.ToString());
        if (!match.Success)
        {
            // Handle special scopes: openid, profile, email, offline_access, launch
            return ParseSpecialScope(scopeString);
        }

        return new SmartScope(
            ParseScopeContext(match.Groups["context"].ValueSpan),
            match.Groups["resource"].Value,
            ParseInteraction(match.Groups["interaction"].ValueSpan),
            match.Groups["constraint"].Success ? match.Groups["constraint"].Value[1..] : null);
    }
}
```

**Authorization Handler** (Week 51-52):
```csharp
public class SmartAuthorizationHandler : AuthorizationHandler<SmartRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SmartRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var accessToken = ExtractAccessToken(httpContext);

        var tokenClaims = await _tokenService.ValidateTokenAsync(accessToken);
        if (tokenClaims == null)
        {
            context.Fail();
            return;
        }

        var scopes = tokenClaims.Scopes.Select(SmartScopeParser.ParseScope).ToArray();

        if (IsOperationAllowed(requirement, scopes, httpContext))
        {
            httpContext.Items["SmartTokenClaims"] = tokenClaims;
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}
```

**Client Management** (Week 52-53):
```csharp
public interface ISmartClientService
{
    ValueTask<SmartClient?> GetClientAsync(string tenantId, string clientId, CancellationToken ct = default);
    ValueTask<SmartClient> RegisterClientAsync(string tenantId, SmartClientRegistration registration, CancellationToken ct = default);
    ValueTask<bool> ValidateClientAsync(string tenantId, string clientId, string? clientSecret, CancellationToken ct = default);
}

public record SmartClient(
    string ClientId,
    string ClientName,
    SmartClientType ClientType,
    string[] RedirectUris,
    string[] Scopes);
```

**Identity Provider Integration** (Week 53-54):
```csharp
public class EntraIdSmartIdentityProvider : ISmartIdentityProvider
{
    public async ValueTask<IdentityProviderTokenResponse> ExchangeCodeAsync(
        IdentityProviderTokenRequest request,
        CancellationToken ct)
    {
        var result = await _clientApp
            .AcquireTokenByAuthorizationCode(request.Scope?.Split(' '), request.Code)
            .WithPkceCodeVerifier(request.CodeVerifier)
            .ExecuteAsync(ct);

        return new IdentityProviderTokenResponse
        {
            AccessToken = result.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = (int)(result.ExpiresOn - DateTimeOffset.UtcNow).TotalSeconds,
            RefreshToken = result.RefreshToken,
            IdToken = result.IdToken
        };
    }
}
```

**Success Criteria**:
- ✅ App can authorize with PKCE
- ✅ Access token grants scoped access
- ✅ Patient scope limits data to patient
- ✅ User scope limits data to user context
- ✅ System scope allows backend services
- ✅ Token refresh works
- ✅ Well-known endpoints return correct metadata
- ✅ Integration with Entra ID works

**Deliverable**: Full SMART on FHIR v2 implementation with multiple identity providers.

---

### Phase 13: Implementation Guides - "US Core & Beyond" (Weeks 55-60)

**Goal**: Support Implementation Guide profiles and validation.

**Vertical Slice**: Validate US Core Patient profile compliance.

#### Deliverables

**IG Package Loader** (Week 55-56):
```csharp
public class NpmImplementationGuidePackageLoader : IImplementationGuidePackageLoader
{
    public async ValueTask<ImplementationGuidePackage> LoadPackageAsync(
        Uri packageSource,
        CancellationToken ct)
    {
        // Download from packages.fhir.org
        var packageData = await _httpClient.GetByteArrayAsync(packageSource, ct);

        // Extract .tar.gz
        var resources = await ExtractTgzResourcesAsync(packageData, ct);

        // Parse package.json
        var packageInfo = await ParsePackageInfoAsync(resources, ct);

        return new ImplementationGuidePackage
        {
            Info = packageInfo,
            PackageData = packageData,
            Format = PackageFormat.NpmTgz,
            Resources = resources
        };
    }
}
```

**IG Resolution** (Week 56-57):
```csharp
public class HeaderBasedIGResolver : IImplementationGuideResolver
{
    public async ValueTask<ImplementationGuideContext> ResolveContextAsync(
        string tenantId,
        FhirVersion fhirVersion,
        HttpRequest request,
        CancellationToken ct)
    {
        // Check X-FHIR-Profile header
        if (request.Headers.TryGetValue("X-FHIR-Profile", out var profileHeaders))
        {
            return await ResolveFromProfileHeadersAsync(tenantId, fhirVersion, profileHeaders, ct);
        }

        // Check Accept header
        var profiles = ExtractProfilesFromAcceptHeader(request.Headers.Accept);
        if (profiles.Any())
        {
            return await ResolveFromProfileUrlsAsync(tenantId, fhirVersion, profiles, ct);
        }

        // Fall back to tenant defaults
        return await GetDefaultIGContextAsync(tenantId, fhirVersion, ct);
    }
}
```

**Composite Schema Provider** (Week 57-58):
```csharp
public class CompositeSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary> _profileCache = new();

    public CompositeSchemaProvider(FhirVersion fhirVersion, IEnumerable<StructureDefinition> profiles)
    {
        Version = GetFhirSpecification(fhirVersion);
        _baseProvider = new FhirJsonSchemaStructureDefinitionSummaryProvider(Version);

        foreach (var profile in profiles)
        {
            if (!string.IsNullOrEmpty(profile.Url))
            {
                _profileCache[profile.Url] = CreateProfileSummary(profile);
            }
        }
    }

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        return _profileCache.TryGetValue(canonical, out var profile) ? profile : _baseProvider.Provide(canonical);
    }
}
```

**Profile Validation** (Week 58-59):
```csharp
public class ProfileValidator : IResourceValidator
{
    public async ValueTask<OperationOutcome> ValidateAsync(
        ISourceNode resource,
        string profile,
        ITenantContext context,
        CancellationToken ct)
    {
        var igContext = await _igResolver.ResolveFromResourceAsync(context.TenantId, resource, context.FhirVersion, ct);

        // Validate using composite schema provider
        var validator = new Validator(igContext.SchemaProvider);
        var result = validator.Validate(resource, profile);

        return CreateOperationOutcome(result);
    }
}
```

**US Core Support** (Week 59-60):
- Load US Core IG packages
- Validate US Core Patient
- Validate US Core Observation
- Must Support element validation
- Cardinality validation
- ValueSet binding validation

**Success Criteria**:
- ✅ Load US Core 6.1.0 package
- ✅ Validate Patient against us-core-patient profile
- ✅ Reject non-compliant resources
- ✅ Support multiple IG versions
- ✅ Handle IG dependencies
- ✅ Profile resolution from headers
- ✅ Performance: validation <50ms

**Deliverable**: Implementation Guide support with US Core validation.

---

### Phase 14: Bulk Operations - "$export Support" (Weeks 61-66)

**Goal**: Add FHIR Bulk Data export capabilities.

**Vertical Slice**: `GET /$export` initiates asynchronous export.

#### Deliverables

**Bulk Export API** (Week 61-62):
```csharp
[HttpGet("$export")]
public async Task<IActionResult> InitiateExport(
    [FromQuery] string? _since,
    [FromQuery] string? _type,
    [FromQuery] string? _outputFormat)
{
    var exportRequest = new BulkExportRequest
    {
        Since = ParseDateParameter(_since),
        ResourceTypes = _type?.Split(','),
        OutputFormat = _outputFormat ?? "application/fhir+ndjson"
    };

    var exportJob = await _bulkExportService.InitiateExportAsync(exportRequest);

    Response.Headers.Add("Content-Location", $"/bulkstatus/{exportJob.Id}");
    return Accepted();
}

[HttpGet("/bulkstatus/{jobId}")]
public async Task<IActionResult> GetExportStatus(string jobId)
{
    var status = await _bulkExportService.GetExportStatusAsync(jobId);

    if (status.State == ExportState.InProgress)
    {
        Response.Headers.Add("X-Progress", $"{status.ProcessedResources}/{status.TotalResources}");
        return Accepted();
    }

    if (status.State == ExportState.Completed)
    {
        return Ok(new
        {
            transactionTime = status.TransactionTime,
            request = status.RequestUrl,
            requiresAccessToken = true,
            output = status.OutputFiles,
            error = status.ErrorFiles
        });
    }

    return BadRequest(status.ErrorMessage);
}
```

**Export Job Processing** (Week 62-63):
```csharp
public class BulkExportProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var jobs = await _jobQueue.DequeueAsync(stoppingToken);

            foreach (var job in jobs)
            {
                await ProcessExportJobAsync(job, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessExportJobAsync(BulkExportJob job, CancellationToken ct)
    {
        using var stream = FhirStreamManager.GetStream("BulkExport");
        var writer = new Utf8JsonWriter(stream);

        // Stream resources to NDJSON
        var searchResult = await _searchService.SearchAsync(job.ResourceType, new Dictionary<string, StringValues>(), ct: ct);

        foreach (var resource in searchResult.Resources)
        {
            SerializeResourceToNdjson(writer, resource);
            await stream.FlushAsync(ct);

            job.ProcessedResources++;
            if (job.ProcessedResources % 100 == 0)
            {
                await UpdateJobProgressAsync(job, ct);
            }
        }

        // Upload to blob storage
        await UploadExportFileAsync(job, stream.ToArray(), ct);
        await CompleteJobAsync(job, ct);
    }
}
```

**NDJSON Serialization** (Week 63-64):
```csharp
public static class NdjsonSerializer
{
    public static void SerializeToNdjson(Utf8JsonWriter writer, ISourceNode resource)
    {
        // Write single JSON object
        JsonSerializer.Serialize(writer, resource, JsonOptions);

        // Write newline
        writer.Flush();
        writer.WriteRawValue("\n"u8);
    }

    public static async IAsyncEnumerable<ISourceNode> DeserializeFromNdjsonAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            var resource = JsonSourceNodeFactory.Parse(Encoding.UTF8.GetBytes(line));
            yield return resource;
        }
    }
}
```

**Blob Storage Integration** (Week 64-65):
```csharp
public class AzureBlobExportStorage : IExportStorage
{
    private readonly BlobContainerClient _containerClient;

    public async ValueTask<Uri> UploadExportFileAsync(
        string jobId,
        string fileName,
        ReadOnlyMemory<byte> data,
        CancellationToken ct)
    {
        var blobClient = _containerClient.GetBlobClient($"{jobId}/{fileName}");

        using var stream = new MemoryStream(data.ToArray());
        await blobClient.UploadAsync(stream, overwrite: true, ct);

        // Generate SAS URL with 24-hour expiration
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _containerClient.Name,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blobClient.GenerateSasUri(sasBuilder);
    }
}
```

**Patient & Group Export** (Week 65-66):
```csharp
[HttpGet("/Patient/$export")]
public async Task<IActionResult> InitiatePatientExport([FromQuery] string? _type)
{
    // Export all patients and related resources
    var exportRequest = new BulkExportRequest
    {
        Scope = ExportScope.AllPatients,
        ResourceTypes = _type?.Split(',')
    };

    var exportJob = await _bulkExportService.InitiateExportAsync(exportRequest);
    return Accepted();
}

[HttpGet("/Group/{id}/$export")]
public async Task<IActionResult> InitiateGroupExport(string id, [FromQuery] string? _type)
{
    // Export group members and related resources
    var group = await _repository.GetAsync(new ResourceKey("Group", id));
    if (group == null) return NotFound();

    var exportRequest = new BulkExportRequest
    {
        Scope = ExportScope.Group,
        GroupId = id,
        ResourceTypes = _type?.Split(',')
    };

    var exportJob = await _bulkExportService.InitiateExportAsync(exportRequest);
    return Accepted();
}
```

**Success Criteria**:
- ✅ $export initiates async export
- ✅ Status endpoint shows progress
- ✅ NDJSON files generated correctly
- ✅ SAS URLs provide secure access
- ✅ Patient/$export exports patient compartment
- ✅ Group/$export exports group members
- ✅ Performance: 10,000 resources/minute
- ✅ Memory-efficient streaming

**Deliverable**: Production bulk data export capability.

---

### Phase 15: Production Readiness - "Observability & Operations" (Weeks 67-72)

**Goal**: Add comprehensive observability, monitoring, and operational tools.

**Vertical Slice**: Deploy to production with full observability.

#### Deliverables

**Structured Logging** (Week 67):
```csharp
public class FhirOperationLogger
{
    public void LogResourceCreated(string resourceType, string resourceId, TimeSpan duration, string tenantId)
    {
        _logger.LogInformation(
            "Resource created: {ResourceType}/{ResourceId} in {Duration}ms for tenant {TenantId}",
            resourceType, resourceId, duration.TotalMilliseconds, tenantId);
    }

    public void LogSearchExecuted(string resourceType, int resultCount, TimeSpan duration, string tenantId, IReadOnlyDictionary<string, StringValues> parameters)
    {
        _logger.LogInformation(
            "Search executed: {ResourceType} returned {ResultCount} results in {Duration}ms for tenant {TenantId}. Parameters: {@Parameters}",
            resourceType, resultCount, duration.TotalMilliseconds, tenantId, parameters);
    }
}
```

**OpenTelemetry Integration** (Week 67-68):
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("FhirServer")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("FhirServer")
        .AddPrometheusExporter());

public class FhirServerMetrics
{
    private static readonly Counter<long> ResourceCreatedCounter =
        Meter.CreateCounter<long>("fhir.resource.created", "resources");

    private static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("fhir.request.duration", "ms");

    public void RecordResourceCreated(string resourceType, string tenantId)
    {
        ResourceCreatedCounter.Add(1,
            new KeyValuePair<string, object?>("resource.type", resourceType),
            new KeyValuePair<string, object?>("tenant.id", tenantId));
    }
}
```

**Health Checks** (Week 68):
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RepositoryHealthCheck>("repository")
    .AddCheck<CacheHealthCheck>("cache")
    .AddCheck<MessageBusHealthCheck>("message_bus")
    .AddCheck<StorageHealthCheck>("storage");

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});
```

**Audit Logging** (Week 69):
```csharp
public class AuditLogger : IAuditLogger
{
    public async ValueTask LogAuditEventAsync(AuditEvent auditEvent, CancellationToken ct)
    {
        var auditRecord = new
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = auditEvent.Type,
            Actor = auditEvent.Actor,
            TenantId = auditEvent.TenantId,
            ResourceType = auditEvent.ResourceType,
            ResourceId = auditEvent.ResourceId,
            Action = auditEvent.Action,
            Outcome = auditEvent.Outcome,
            ClientIp = auditEvent.ClientIp,
            UserAgent = auditEvent.UserAgent
        };

        // Log to structured sink (e.g., Azure Event Hub, Elasticsearch)
        await _auditSink.WriteAsync(auditRecord, ct);
    }
}
```

**Rate Limiting** (Week 69-70):
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var tenantId = context.GetTenantId();

        return RateLimitPartition.GetTokenBucketLimiter(tenantId, _ =>
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = 1000,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 1000,
                AutoReplenishment = true
            });
    });
});
```

**Error Handling & Resilience** (Week 70):
```csharp
builder.Services.AddResiliencePipeline("fhir-operations", builder =>
{
    builder
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(100),
            BackoffType = DelayBackoffType.Exponential
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(10),
            MinimumThroughput = 20,
            BreakDuration = TimeSpan.FromSeconds(30)
        })
        .AddTimeout(TimeSpan.FromSeconds(30));
});
```

**Configuration Management** (Week 71):
```csharp
// appsettings.json structure
{
  "FhirServer": {
    "DefaultVersion": "R4",
    "EnabledVersions": ["R4", "R4B", "R5"],
    "Storage": {
      "Type": "FileSystem", // Memory, FileSystem, SqlServer, CosmosDb
      "Path": "./data",
      "MaxConcurrentWrites": 100
    },
    "Cache": {
      "Type": "Memory", // Memory, Redis
      "ExpirationMinutes": 30
    },
    "Messaging": {
      "Type": "Medino", // Medino, Redis
      "ProcessorCount": 10
    },
    "RateLimiting": {
      "Enabled": true,
      "TokenLimit": 1000,
      "ReplenishmentPeriod": "00:01:00"
    }
  }
}
```

**Deployment Guide** (Week 71-72):
- Docker containerization
- Kubernetes manifests
- Azure App Service configuration
- Load balancer configuration
- SSL/TLS setup
- Backup/restore procedures
- Disaster recovery plan
- Scaling guidelines

**Monitoring Dashboards** (Week 72):
- Grafana dashboards for metrics
- Application Insights workbooks
- Prometheus alerting rules
- SLO/SLI definitions
- Runbooks for common issues

**Success Criteria**:
- ✅ Distributed tracing works end-to-end
- ✅ Metrics exported to Prometheus
- ✅ Health checks report accurate status
- ✅ Audit logs capture all operations
- ✅ Rate limiting protects from abuse
- ✅ Circuit breaker prevents cascade failures
- ✅ Zero-downtime deployment works
- ✅ Documentation complete

**Deliverable**: Production-ready FHIR server with enterprise observability.

---

## Status

Proposed

## Consequences

### Positive Consequences

1. **Incremental Value Delivery**: Each phase delivers working features
2. **Risk Mitigation**: Problems discovered early in simple scenarios
3. **Developer Confidence**: F5 experience maintained throughout
4. **Performance Built-In**: Memory optimizations from day one
5. **Production Ready**: Each phase includes tests, docs, and monitoring
6. **Predictable Schedule**: 72-week roadmap with clear milestones
7. **Technology Validation**: Modern patterns proven incrementally
8. **Team Velocity**: Vertical slices enable parallel workstreams

### Negative Consequences

1. **Long Timeline**: 18 months to full feature parity
2. **Refactoring Risk**: Early abstractions may need adjustment
3. **Integration Complexity**: Later phases integrate many systems
4. **Performance Validation**: Scale testing deferred to later phases

### Risk Mitigation Strategies

1. **Continuous Integration**: Every commit builds and tests
2. **Performance Benchmarks**: Track metrics from Phase 1
3. **Architecture Reviews**: Validate abstractions before building on them
4. **Parallel Prototyping**: Spike complex features early
5. **Community Feedback**: Share early and often
6. **Documentation First**: Write docs before code

### Success Metrics

**Phase-Level Metrics**:
- All acceptance criteria met
- 80%+ test coverage (xUnit + NSubstitute)
- E2E tests from src-old/test pass for phase scope
- Performance targets achieved
- Zero critical bugs
- Documentation complete

**System-Level Metrics** (by Phase 15):
- <10ms CRUD operations (in-memory)
- <50ms simple search (in-memory)
- <200ms chained search (in-memory)
- <20ms CRUD operations (file storage)
- <100ms simple search (file storage)
- <100ms CRUD operations (SQL Server)
- <200ms search (SQL Server with 1M resources)
- <5ms point reads (Cosmos DB)
- <50ms single-partition search (Cosmos DB)
- 10,000 resources/minute bulk export
- 1,000 concurrent users supported
- Linear scalability to 10 nodes
- 99.9% uptime SLA
- <1% error rate under load

### Code Size Estimates

| Phase | Cumulative LOC | Key Components |
|-------|----------------|----------------|
| 1 | 1,500 | Core abstractions, in-memory storage, basic API |
| 2 | 3,500 | Search service, indexing, bundle support |
| 3 | 5,000 | Multi-resource, references |
| 4 | 7,500 | Transactions, bundle processor |
| 5 | 10,000 | File storage, index files |
| 6 | 13,000 | Multi-version support |
| 7 | 16,000 | Multi-tenant foundation |
| 8 | 19,000 | Advanced search |
| 9 | 22,000 | Distributed infrastructure |
| 10 | 28,000 | SQL Server storage |
| 11 | 35,000 | Cosmos DB storage |
| 12 | 40,000 | SMART on FHIR |
| 13 | 45,000 | Implementation Guides |
| 14 | 50,000 | Bulk operations |
| 15 | 55,000 | Production readiness |

**Final System**: ~55,000 lines of production code (75% reduction from legacy 220,000+ LOC)

### Implementation Notes

1. **Start Simple**: Each phase begins with simplest working implementation
2. **Optimize Later**: Performance optimizations after correctness proven
3. **Test Everything**: Unit tests + integration tests + performance tests
4. **Document Continuously**: Architecture decision records for key choices
5. **Review Rigorously**: Code review every change
6. **Measure Always**: Performance benchmarks every sprint
7. **Refactor Ruthlessly**: Don't live with bad abstractions

## Definition of Done

**FHIR Server v2 is complete when ALL E2E tests from src-old/test pass.**

The legacy codebase contains **118 E2E/Integration test files** covering all FHIR operations. These tests represent the gold standard for feature parity. Our implementation is done when:

### Test Execution Criteria

1. **All E2E Test Suites Pass**:
   ```bash
   dotnet test src-old/test/Microsoft.Health.Fhir.Shared.Tests.E2E
   dotnet test src-old/test/Microsoft.Health.Fhir.R4.Tests.E2E
   dotnet test src-old/test/Microsoft.Health.Fhir.Shared.Tests.Integration
   ```
   - **Target**: 100% of tests passing
   - **Minimum per phase**: 80% of tests for that phase's feature scope

2. **Test Execution Environment**:
   - Developer can run `dotnet test` locally (F5 experience)
   - No external dependencies required (in-memory storage)
   - All tests complete in under 10 minutes
   - Tests run in parallel where possible

3. **Test Migration Strategy**:
   - Copy E2E tests from src-old/test to v2 test projects
   - Update namespace imports to reference v2 assemblies
   - **Keep test logic unchanged** - if test behavior changes, implementation is wrong
   - Legacy tests in src-old remain as gold standard until 100% parity

### Phase-Specific E2E Test Requirements

See [legacy-feature-analysis.md](../investigations/legacy-feature-analysis.md) for comprehensive test inventory.

**Phase 1** - Basic CRUD:
- ✅ `CreateTests.cs`
- ✅ `ReadTests.cs`
- ✅ `UpdateTests.cs`
- ✅ `DeleteTests.cs`
- ✅ `VReadTests.cs`
- ✅ `HistoryTests.cs`
- ✅ `MetadataTests.cs`
- ✅ `HealthTests.cs`

**Phase 2** - Search Foundation:
- ✅ `Search/BasicSearchTests.cs`
- ✅ `Search/StringSearchTests.cs`
- ✅ `Search/TokenSearchTests.cs`
- ✅ `Search/DateSearchTests.cs`
- ✅ `Search/SortTests.cs`

**Phase 4** - Bundle & Transactions:
- ✅ `BundleTransactionTests.cs` (**ALL** tests)
- ✅ `BundleBatchTests.cs` (**ALL** tests)
- ✅ `BundleEdgeCaseTests.cs` (**ALL** tests)
- ✅ `ConditionalCreateTests.cs`
- ✅ `ConditionalUpdateTests.cs`
- ✅ `ConditionalDeleteTests.cs`
- ✅ `ConditionalPatchTests.cs`

**Phase 5** - Advanced Search:
- ✅ `Search/NumberSearchTests.cs`
- ✅ `Search/QuantitySearchTests.cs`
- ✅ `Search/CompositeSearchTests.cs`
- ✅ `Search/ChainingSearchTests.cs`
- ✅ `Search/IncludeSearchTests.cs`
- ✅ `Search/CustomSearchParamTests.cs`

**Phase 6** - Patch Operations:
- ✅ `JsonPatchTests.cs`
- ✅ `FhirPathPatchTests.cs`

**Phase 7** - Validation:
- ✅ `ValidateTests.cs`

**Phase 8** - Bulk Export:
- ✅ `Export/ExportTests.cs`
- ✅ `Export/ExportDataTests.cs`
- ✅ `Export/ExportDataValidationTests.cs`

**Phase 9** - Bulk Import:
- ✅ `Import/ImportTests.cs`
- ✅ All `Import/*SearchTests.cs` (10 files)

**Phase 10+** - Advanced Operations:
- ✅ `EverythingOperationTests.cs`
- ✅ `MemberMatchTests.cs`
- ✅ `ConvertDataTests.cs`
- ✅ `AuditTests.cs`
- ✅ `BasicAuthTests.cs`
- ✅ `Shared.Tests.Smart/SmartProxy/*`

**Final Gate** - Crucible Conformance:
- ✅ `Shared.Tests.Crucible/*` - HL7 Crucible test suite validates FHIR conformance

### Acceptance Criteria Summary

| Criterion | Target | Measurement |
|-----------|--------|-------------|
| E2E Test Pass Rate | 100% | `dotnet test src-old/test/**/*.E2E.csproj` |
| Integration Test Pass Rate | 100% | `dotnet test src-old/test/**/*.Integration.csproj` |
| Unit Test Coverage | 80%+ | Code coverage tools |
| Performance (CRUD) | <10ms in-memory | Benchmarks |
| Performance (Search) | <50ms in-memory | Benchmarks |
| F5 Experience | Zero setup | Manual verification |
| Documentation | All ADRs complete | Review |

**When ALL criteria are met, FHIR Server v2 is production-ready and can replace the legacy system.**

### Next Steps

1. **Week 0**: Setup repository, CI/CD, project structure
2. **Week 1**: Begin Phase 1 implementation
3. **Week 3**: Demo Phase 1 to stakeholders
4. **Week 4**: Begin Phase 2 based on feedback
5. **Ongoing**: Weekly demos, monthly retrospectives, quarterly reviews
6. **Each Phase**: Review E2E test pass rate, adjust implementation until 100% pass
