# Investigation Summary - FHIR Server v2

This document summarizes all **26 investigations** completed for FHIR Server v2 architecture and provides references to key decisions incorporated into phase-specific ADRs.

---

## Table of Contents

1. [Architecture Investigations](#architecture-investigations)
2. [Storage & Performance Investigations](#storage--performance-investigations)
3. [Feature Implementation Investigations](#feature-implementation-investigations)
4. [Infrastructure Investigations](#infrastructure-investigations)
5. [Security & Identity Investigations](#security--identity-investigations)
6. [Key Decisions Matrix](#key-decisions-matrix)

---

## Architecture Investigations

### FHIR Server v2 Architecture
**File**: `fhir-server-v2-architecture.md`

**Key Findings**:
- Vertical slice architecture over horizontal layers
- Medino messaging abstraction (not MediatR)
- Provider-agnostic storage patterns
- Multi-version support (STU3, R4, R4B, R5) from day one
- F5 developer experience principle

**Incorporated Into**: All phase ADRs (foundation pattern)

---

### Memory-Efficient FHIR Patterns
**File**: `memory-efficient-fhir-patterns.md`

**Key Findings**:
- `Span<T>` and `Memory<T>` for zero-allocation parsing
- `RecyclableMemoryStream` reduces GC pressure by 70%
- `ArrayPool<T>` for temporary collections
- Record types for immutable value objects
- 50-70% reduction in allocations vs legacy

**Performance Impact**:
- Resource parsing: <5ms (vs 15-20ms legacy)
- Search result aggregation: 60% fewer allocations
- Bundle processing: 40% faster

**Incorporated Into**:
- ADR-2501 (Prototype Phase)
- ADR-2502 (Search Phase)
- ADR-2504 (Bundle Processing Phase)

---

### Transaction Table Core Abstraction
**File**: `transaction-table-core-abstraction.md`

**Key Findings**:
- Append-only transaction log pattern
- Sequential visibility advancement (watermark)
- Heartbeat-based timeout detection (60s default)
- Provider-agnostic design (works across storage types)

**Implementation**:
```csharp
public interface ITransactionContext
{
    ValueTask<ITransactionScope> BeginTransactionAsync(int resourceCount, ...);
}

public interface ITransactionScope : IAsyncDisposable
{
    TransactionId TransactionId { get; }
    ValueTask UpdateHeartbeatAsync();
    ValueTask CommitAsync();
    ValueTask FailAsync(string reason);
}
```

**Incorporated Into**: ADR-2504 (Bundle Processing)

---

## Storage & Performance Investigations

### Decoupled Resource and Index Storage
**File**: `decoupled-resource-index-storage.md`

**Key Findings**:
- URN-based storage location pattern (single `StorageLocation varchar(500)` column)
- Separate `IResourceStore` (raw data) and `ISearchIndexStore` (indices)
- Polymorphic `ResourceStorage` types (SQL, Blob, File, Cosmos)
- Hybrid storage strategies (tiered by age, size, resource type)
- SQL schema: Separate Resource (metadata + URN) and RawResource (binary data) tables
- 78% cost savings example (hot SQL + warm/cold Blob vs all SQL)

**URN Formats**:
- SQL: `sql://RawResource/12345`
- Blob: `blob://fhirdata/Patient/2025/01/15/tx-1234567890.ndjson`
- File: `file:///data/Patient/2025/01/15/tx-1234567890.ndjson`
- Cosmos: `cosmos://fhirdb/Patient|2025-01|tenant1/patient-123-v1`

**Incorporated Into**: ADR-2501 (noted for future), ADR-2511 (SQL Storage), ADR-2512 (Cosmos Storage), ADR-2513+ (Hybrid Storage)

---

### Phase 1: File-Based Storage with Search
**File**: `phase1-file-based-storage-with-search.md`

**Key Findings**:
- NDJSON storage with `.metadata.ndjson` sidecar files
- Pre-extracted search indices (10x faster startup)
- Complete InMemory search from microsoft/fhir-server:
  - `SearchQueryInterpreter.cs` - Expression visitor
  - `ComparisonValueVisitor.cs` - Type-specific comparisons
  - `InMemoryIndex.cs` - Index structure

**File System Structure**:
```
/data/Patient/2025/01/15/
  tx-1234567890.ndjson           # Bundle + Resource data
  tx-1234567890.metadata.ndjson  # Search indices + request metadata
```

**Metadata Contains**:
- Search indices (pre-extracted)
- Request information: `RequestMethod`, `RequestUrl`, `IfMatch`, etc.
- Transaction metadata

**Incorporated Into**: ADR-2501 (Prototype Phase), ADR-2502 (Search Phase)

---

### Storage Architecture v2
**File**: `storage-architecture-v2.md`

**Key Findings**:

**SQL Server** - 3-table split:
1. `Resource` (current versions only) - no NULL columns
2. `ResourceHistory` (historical versions) - no NULL columns
3. `RawResource` (binary storage) - compressed, deduped

**Benefits**: 61% storage reduction, 30% row size reduction, eliminated NULL columns

**File System** - Date-based sharding with transaction bundles:
- Path: `/ResourceType/year/month/day/tx-{id}.ndjson`
- First line: Bundle with transaction metadata
- Enables transaction replay and audit trails

**Cosmos DB** - See dedicated investigations

**Incorporated Into**: ADR-2506 (SQL Storage), ADR-2507 (Cosmos Storage)

---

### Cosmos 10PB Storage Architecture
**Files**: `cosmos-10pb-storage-architecture.md`, `cosmos-10pb-storage-architecture-more-options.md`

**Key Findings**:
- Separation of resource data from search indices
- Smart partitioning: `{ResourceType}|{YearMonth}|{TenantId}`
- Compact search index pattern (proven in issue #2686)
- 500 physical partition SDK limitation handling
- Sub-second point reads, efficient cross-partition queries

**Performance Targets**:
- Point reads: <5ms, <5 RU
- Single-partition search: <50ms, <50 RU
- Cross-partition search: <500ms, <500 RU
- Bulk writes: 1,000 resources in <30s

**Incorporated Into**: ADR-2507 (Cosmos DB Storage)

---

### Cosmos Transaction Table Implementation
**File**: `cosmos-transaction-table-implementation.md`

**Key Findings**:
- Stored procedures for transaction ID allocation
- Cosmos change feed for visibility tracking
- Optimistic concurrency with ETags
- Automatic cleanup of completed transactions

**Incorporated Into**: ADR-2507 (Cosmos DB Storage)

---

## Feature Implementation Investigations

### Dynamic Capability Statement Generation
**File**: `dynamic-capability-statement-generation.md`

**Key Findings**:
- Segmented capability statement with different refresh rates (static, quasi-static, dynamic, tenant-specific)
- Version hash tracking per segment for change detection (SHA256)
- Smart caching: only rebuild when segment hash changes
- Event-based cache invalidation when profiles/search params loaded
- Multi-tenant support with separate cache entries per tenant
- Performance: <1.5ms total overhead (parallel segments, incremental hashes)

**Capability Segments**:
- **Static**: Software info, FHIR version (never changes)
- **Quasi-Static**: Resource types, default interactions (rarely changes)
- **Dynamic**: Loaded profiles, custom search params (frequently changes)
- **Tenant-Specific**: Per-tenant capabilities

**Cache Strategy**:
- Composite version hash from all segments
- Cache hit if hash matches (no rebuild needed)
- Event-driven invalidation via `CapabilityChangedEvent`
- 1-hour TTL safety net

**Incorporated Into**: ADR-2503 (Search - basic capability), ADR-2506 (Validation - profile segment), ADR-2509 (Multi-tenant - tenant segments), ADR-2510 (Distributed - Redis cache)

---

### Bundle Processing with Channels
**File**: `bundle-processing-with-channels.md`

**Key Findings**:
- ASP.NET Core pipeline routing (vs switch statements)
- Mini `HttpContext` for each bundle entry
- Automatic handler discovery
- System.Threading.Channels for parallel execution
- Bounded channels for backpressure

**Pattern**:
```csharp
public class BundleEntryExecutor
{
    // Create mini HttpContext
    using var httpContext = _httpContextFactory.Create(...);
    httpContext.Request.Method = entry.HttpVerb;
    httpContext.Request.Path = entry.RequestUrl;

    // Execute through pipeline (automatic routing!)
    await _pipeline(httpContext);
}
```

**Benefits**:
- No switch statements or command mapping
- Supports ANY FHIR interaction automatically
- Parallel execution with channels
- Easy to extend

**Incorporated Into**: ADR-2504 (Bundle Processing)

---

### Bulk Import/Export with Channels
**File**: `bulk-import-export-with-channels.md`

**Key Findings**:
- Legacy import ALREADY uses System.Threading.Channels (proven pattern)
- Producer-consumer pattern with bounded channels
- Streaming NDJSON processing
- Reuses bundle processing patterns

**Implementation**:
```csharp
// Producer: Read NDJSON from blob
var (resourceChannel, loadTask) = LoadResources(...);

// Consumer: Process from channel
await foreach (var resource in resourceChannel.Reader.ReadAllAsync())
{
    resourceBatch.Add(resource);
    if (resourceBatch.Count >= batchSize)
        await ProcessBatch(resourceBatch);
}
```

**Incorporated Into**: ADR-2508 (Bulk Operations)

---

### Two-Tier Validation Architecture
**File**: `two-tier-validation-architecture.md`

**Key Findings**:
- **Tier 1**: Fast structural validation (<50ms) - always runs
  - Required fields, cardinality, data types, ID format, references
  - Built-in, no external dependencies
- **Tier 2**: Profile validation (<5s) - opt-in via `$validate` or `Prefer` header
  - Full Firely SDK validation with caching
  - First validation slow, subsequent 5-10x faster

**Problem Solved**: Legacy uses deprecated Firely library, >1s per resource

**Solution**: Separate fast validation (always) from slow validation (opt-in)

**Incorporated Into**: ADR-2509 (Validation)

---

### Legacy Feature Analysis & Roadmap Gap Analysis
**Files**: `legacy-feature-analysis.md`, `roadmap-gap-analysis.md`

**Key Findings**:
- **118 E2E test files** define feature parity
- **15+ missing features** identified:
  - $bulk-delete, $bulk-update (HIGH priority)
  - $docref (US Core requirement)
  - Compartment search
  - FHIRPath Patch
  - Anonymous export
  - Profile validation

**Timeline Impact**:
- Original estimate: 72 weeks
- Updated estimate: 96 weeks (+24 weeks, +33%)
- New phases needed: 16-19

**Incorporated Into**: All phase ADRs (test success criteria)

---

## Infrastructure Investigations

### Caching Abstraction Architecture
**File**: `caching-abstraction-architecture.md`

**Key Findings**:
- Unified `ICache` abstraction
- Multiple providers: In-Memory → Redis → Multi-tier
- Memory-efficient with `ReadOnlyMemory<byte>`
- TTL support, cache invalidation

**Pattern**:
```csharp
public interface ICache
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);
    ValueTask SetAsync<T>(string key, T value, TimeSpan? expiration, CancellationToken ct = default);
    ValueTask RemoveAsync(string key, CancellationToken ct = default);
}
```

**Incorporated Into**: ADR-2505 (Distributed Infrastructure)

---

### Distributed Messaging Architecture
**File**: `distributed-messaging-architecture.md`

**Key Findings**:
- Medino for in-process messaging
- Redis for distributed messaging
- Unified `IBus` abstraction
- Command/Query/Event patterns

**Pattern**:
```csharp
public interface IBus
{
    ValueTask<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken ct);
    ValueTask SendAsync(ICommand command, CancellationToken ct);
    ValueTask<TResponse> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken ct);
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : IEvent;
}
```

**Incorporated Into**: ADR-2505 (Distributed Infrastructure)

---

## Security & Identity Investigations

### RBAC Authorization with Capability Enforcement
**File**: `rbac-authorization-with-capability-enforcement.md`

**Key Findings**:
- 5-layer authorization pipeline: Authentication → Tenant Isolation → RBAC → SMART Scopes → **Capability Enforcement**
- **Capability Enforcement Handler**: Server MUST respect its CapabilityStatement (if interaction not advertised, deny request)
- Pre-built capability interaction cache for O(1) lookups (<0.1ms per check)
- Patient compartment filtering for `patient/*.read` scopes (automatic query filter)
- Pluggable handler architecture (priority-based execution)
- Total authorization overhead: <1.5ms per request

**Authorization Pipeline**:
1. **Authentication** (Priority 10): Token valid?
2. **Tenant Isolation** (Priority 20): Correct tenant?
3. **RBAC** (Priority 30): Role allows this?
4. **SMART Scopes** (Priority 40): OAuth scopes allow this?
5. **Capability Enforcement** (Priority 50): Server supports this per CapabilityStatement?

**Key Innovation**: CapabilityStatement is a contract - if it says "AuditEvent doesn't support update", authorization layer enforces it!

**Performance Optimization**:
- Capability interaction cache built when CapabilityStatement generated
- Key: `{resourceType}:{interaction}` → Value: `true` (O(1) lookup)
- Cache invalidated when CapabilityStatement changes

**Incorporated Into**: ADR-2503 (Search - basic auth), ADR-2506 (Validation - capability enforcement), ADR-2509 (Multi-tenant - tenant isolation), ADR-2513 (SMART - full SMART auth)

---

### SMART on FHIR v2 Implementation
**File**: `smart-on-fhir-v2-implementation.md`

**Key Findings**:
- SMART on FHIR v2 native support
- Scope parsing with Span-based optimization
- Authorization handler integration
- PKCE support for public clients

**Scope Pattern**: `{context}/{resource}.{interaction}:{constraint}`
- Example: `patient/Observation.read`, `user/*.write`

**Incorporated Into**: ADR-2510 (SMART on FHIR)

---

### SMART Identity Provider Abstraction
**File**: `smart-identity-provider-abstraction.md`

**Key Findings**:
- Multi-provider identity abstraction
- Support for Entra ID, Auth0, Keycloak, etc.
- Pluggable provider architecture
- Configuration-driven selection

**Pattern**:
```csharp
public interface ISmartIdentityProvider
{
    ValueTask<IdentityProviderTokenResponse> ExchangeCodeAsync(...);
    ValueTask<IdentityProviderTokenResponse> RefreshTokenAsync(...);
    ValueTask<IdentityProviderUserInfo> GetUserInfoAsync(...);
}
```

**Incorporated Into**: ADR-2510 (SMART on FHIR)

---

### Multi-Version IG Loading System
**File**: `multi-version-ig-loading-system.md`

**Key Findings**:
- NPM package loading from packages.fhir.org
- Header-based IG resolution (`X-FHIR-Profile`, `Accept`)
- Composite schema provider (base + profiles)
- Tenant-specific IG configuration

**Pattern**:
```csharp
public class CompositeSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;
    private readonly ConcurrentDictionary<string, IStructureDefinitionSummary> _profileCache;

    public IStructureDefinitionSummary? Provide(string canonical)
    {
        return _profileCache.TryGetValue(canonical, out var profile)
            ? profile
            : _baseProvider.Provide(canonical);
    }
}
```

**Incorporated Into**: ADR-2511 (Implementation Guides)

---

### Custom Search Parameters Lifecycle
**File**: `custom-search-parameters-lifecycle.md`

**Problem Statement**:
- Legacy code handles search parameters from three sources poorly: base FHIR spec, Implementation Guides, and custom client-posted parameters
- 86% of US Core search parameters collide with core FHIR codes
- IGs can "override" base parameters using `derivedFrom` and R5+ `constraint` fields
- No clear precedence rules, leading to silent overwrites
- Custom parameters were searchable before reindex completed (incomplete results)

**Key Findings**:
- **Source-aware priority system**: Base (priority 1) > IG (priority 2) > Custom (priority 3)
- **R5+ constraint field**: FHIRPath expression that determines *when* a search parameter applies (the "filter")
- **Multi-status lifecycle**: Supported (defined) → Reindexing → Enabled (searchable) → Disabled
- **Canonical URL as primary key**: Same `code` + different `url` = different parameters (can coexist)
- **derivedFrom relationship**: Links child parameters to parent, allows coexistence

**Architecture**:
```csharp
public class SearchParameterInfo
{
    public required string Url { get; init; }              // PRIMARY KEY
    public required string Code { get; init; }             // Query param name (NOT unique)
    public required string Expression { get; init; }       // FHIRPath for extraction
    public string? Constraint { get; init; }               // FHIRPath filter for when applies (R5+)
    public string? DerivedFrom { get; init; }              // Parent parameter URL
    public required SearchParameterSource Source { get; init; }    // Base/IG/Custom
    public required SearchParameterStatus IndexStatus { get; init; } // Supported/Enabled/Disabled
    public required int Priority { get; init; }            // For conflict resolution
}
```

**Conflict Resolution**:
1. Same canonical URL → Replace if newer version, reject if different source
2. Same code + different URL + no derivedFrom → Use priority (base wins)
3. Same code + derivedFrom relationship → Coexist (highest priority served in searches)
4. Custom parameter conflicts with base → Reject

**Reindexing Orchestration**:
- New parameters start in `Supported` status (NOT searchable)
- POST /$reindex triggers background job
- Status transitions: Supported → Reindexing → Enabled
- Batch processing: 100-1000 resources per batch
- Incremental reindexing: Only resources modified since parameter added

**Performance**:
- Add parameter: <50ms
- Conflict check: <10ms
- Constraint evaluation (cached): <1ms
- Reindex 100K resources (file): <10 minutes
- Reindex 100K resources (SQL): <20 minutes

**Incorporated Into**:
- ADR-2503 (Phase 1.2): Load base search parameters
- ADR-2511 (Phase 11): IG search parameter loading with conflict resolution
- **ADR-2515 (Phase 12)**: Custom search parameters and reindexing

---

### FHIR Version Conversion and Multi-Version Support
**File**: `fhir-version-conversion-and-multi-version-support.md`

**Problem Statement**:
- FHIR has evolved through multiple major versions (STU3, R4, R4B, R5, R6)
- Healthcare organizations have resources in multiple versions
- Automatic conversion is lossy and complex (round-trip not guaranteed)
- Industry needs multi-version support without conversion overhead

**Key Findings**:
- **Industry Practice**: Per-deployment version isolation (Google Cloud, Azure, AWS)
- **Conversion Fidelity**: R4 → R5 → R4 often lossy; clinical data can be lost
- **Conversion Overhead**: ~300ms per request (HAPI FHIR benchmark)
- **Four Storage Patterns Compared**:
  1. Per-version storage (RECOMMENDED - zero overhead, perfect fidelity)
  2. On-the-fly conversion (~300ms overhead, fidelity issues)
  3. Normalized storage (lossy on write, backward conversion problematic)
  4. Dual storage (2x cost, synchronization complexity)

**Architecture Decision**: **Path-Based Multi-Version with NO Automatic Conversion**

```
URL Pattern: /{tenantId}/{version}/{resourceType}/{id}
Examples:
  GET /acme/R4/Patient/123   # R4 Patient
  GET /acme/R5/Patient/456   # R5 Patient

Storage: Version-tagged with FhirVersion field
  {
    "fhirVersion": "4.0",
    "resource": { /* R4 Patient */ }
  }
```

**Key Innovations**:
- `FhirRequestContext` with version resolution
- `VersionEnforcementMiddleware` rejects unsupported resource types per version
- Version-specific search parameters (R5 params not available in R4 endpoints)
- Per-version CapabilityStatement with segment caching

**Conversion Guidance** (when needed):
- External conversion service (not in FHIR server)
- Use HAPI FHIR VersionConvertor or StructureMap implementations
- Document fidelity limitations
- Optional `POST /$convert-version` endpoint (experimental, post-Phase 18)

**Performance**:
- Per-version storage: <10ms (in-memory), <20ms (file), <100ms (SQL)
- On-the-fly conversion: +300ms overhead
- Zero conversion overhead with recommended approach

**Incorporated Into**:
- **ADR-2508 (Phase 5)**: Multi-Version Support (Weeks 15-19)
  - Path-based routing
  - Version-tagged storage
  - Version enforcement middleware
  - NO automatic conversion

---

### Custom Structures and R6 Extensibility
**File**: `custom-structures-and-r6-extensibility.md`

**Problem Statement**:
- Standard FHIR resources don't cover all use cases (proprietary workflows, emerging standards, research)
- Extensions are standard mechanism but need server support for indexing/searching
- R6 introduces **Additional Resources** - HL7-approved resources outside core spec for faster adoption
- How to handle new R6 resource types when R4/R5 clients interact with them?

**Key Findings**:
- **Extensions**: Add data to existing resources (simple, complex, standard)
- **R6 Additional Resources**: Formally registered resource types outside core FHIR spec
  - HL7-approved with canonical URLs
  - Faster innovation cycle (months vs years)
  - Distributed via NPM packages
  - Eventually may be incorporated into core FHIR
- **Custom Resource Types**: Organization-specific (HAPI FHIR pattern) - NOT RECOMMENDED
- **Profiles**: Constraints on existing resources (US Core, Genomics IG)

**R6 Additional Resources Lifecycle**:
```
1. Community proposal → 2. HL7 review → 3. Publish with canonical URL
→ 4. IG references → 5. Eventually core FHIR (maybe)
```

**Architecture Solution**:

**CompositeSchemaProvider** (enhanced):
```csharp
public class CompositeSchemaProvider : IFhirSchemaProvider
{
    private readonly IFhirSchemaProvider _baseProvider;              // Base FHIR R4/R5/R6
    private readonly ConcurrentDictionary<...> _profileCache;        // IG profiles
    private readonly ConcurrentDictionary<...> _additionalResourceCache;  // R6 Additional Resources

    // Unified resolution: Additional Resources → IG Profiles → Base FHIR
}
```

**Extension Indexing**:
- SearchParameter definitions include FHIRPath for extensions
- Extract extension values during indexing
- Search by extension values: `GET /Patient?pet-name=Fluffy`

**Dynamic IG Loading** (hot-reload):
```http
POST /$load-ig
{
  "parameter": [{
    "name": "package",
    "valueString": "hl7.fhir.additional.clinicaltrial#1.0.0"
  }]
}
```

**Effects**: Download NPM package → Register StructureDefinitions → Invalidate capability cache → Rebuild CapabilityStatement

**Use Case Examples**:
1. **Japanese Insurance Extension**: Complex extension on Coverage with search support
2. **Clinical Trial Protocol**: R6 Additional Resource for detailed research protocols
3. **Genomics Extensions**: Real-world HL7 Genomics IG with variant annotations

**Performance**:
- Extension parsing: +5-10% overhead vs base resources
- Load US Core IG (~200 profiles): <1 second
- Search by extension (cached): <1ms additional overhead

**Incorporated Into**:
- ADR-2514 (Phase 11): Implementation Guides - Profile and extension loading
- **ADR-2522 (NEW Phase 19)**: Custom Structures and Extensions (Weeks 91-94)
  - Extension indexing for search
  - R6 Additional Resource hot-loading
  - Dynamic resource type registration
  - Extension validation (Tier 2)

---

### Background Jobs with DurableTask Framework
**File**: `background-jobs-with-durabletask.md`

**Problem Statement**:
- FHIR servers need long-running background operations: $reindex, $export, $import, $bulk-delete
- Legacy approach uses custom task framework with manual state persistence
- Requirements: Persistent state, fault tolerance, progress monitoring, scalability

**Key Findings**:
- **Azure DurableTask**: Enterprise-grade workflow orchestration framework
- **Developed by Microsoft**: Powers Azure Durable Functions
- **License**: MIT (open source)
- **Storage Backends**: SQL Server, Azure Storage, Netherite, Redis, In-Memory Emulator

**Architecture Components**:
1. **Orchestration**: Long-running workflow (async/await code)
2. **Activity**: Stateless, retriable unit of work
3. **Runtime**: Automatic state persistence, retry, coordination

**Workflow Patterns**:
- **Fan-Out/Fan-In**: Parallel batch processing with aggregation
- **Human Interaction**: Wait for external events (approvals)
- **Monitor/Polling**: Periodic checks with timers

**Example** (Reindex Orchestration):
```csharp
public override async Task<ReindexResult> RunTask(
    OrchestrationContext context,
    ReindexRequest input)
{
    var totalCount = await context.ScheduleTask<int>(
        typeof(CountResourcesActivity),
        input.ResourceType);

    for (int i = 0; i < batches; i++)
    {
        var batchResult = await context.ScheduleTask<int>(
            typeof(ReindexBatchActivity),
            batchInput);

        // Progress auto-persisted
        context.SetCustomStatus(new { PercentComplete = ... });
    }

    return new ReindexResult { TotalProcessed = ... };
}
```

**Benefits Over Custom Implementation**:
- ✅ Automatic state persistence (no manual SQL serialization)
- ✅ Built-in retry logic (no custom retry bugs)
- ✅ Fault tolerance (automatic replay from checkpoints)
- ✅ Linear scalability (add workers)
- ✅ Built-in monitoring (status API)
- ✅ In-Memory emulator for testing (F5 experience)
- ✅ 50% less code than custom task framework

**Storage Strategy**:
- **Phase 1-7** (File/InMemory): In-Memory Emulator (zero dependencies)
- **Phase 8+** (SQL Server): SQL Server backend (shared DB)
- **Cloud**: Azure Storage or Netherite (high-performance)

**Performance Considerations**:
- Orchestration code replays on every continuation (keep simple)
- Activities must be idempotent (may retry)
- Keep state size <1MB
- All heavy computation in activities, not orchestrations

**Incorporated Into**:
- ADR-2510 (Phase 7): Configure DurableTask with distributed storage
- **ADR-2515 (Phase 12)**: ReindexOrchestration for search parameter reindexing
- **ADR-2516 (Phase 13)**: ExportOrchestration, ImportOrchestration, BulkDeleteOrchestration, BulkUpdateOrchestration
- ADR-2517 (Phase 14): Custom long-running operations

---

### Multi-Tenancy Data Partitioning Modes
**File**: `multi-tenancy-data-partitioning-modes.md`

**Problem Statement**:
- Healthcare organizations need multiple data partitioning strategies for different use cases
- Research networks require distributed queries across multiple institutions (fanout/union)
- SaaS deployments need pure tenant isolation without query overhead
- Legacy architectures force a single partitioning mode across all scenarios

**Key Findings**:
- **Dual-Mode Architecture**: Isolation vs Distributed as first-class abstractions
- **Pass-Through Optimization**: Zero overhead for single-layer queries (Isolation mode always, Distributed mode with 0-1 layers)
- **Fanout/Union Strategy**: ONLY when Distributed mode AND 2+ participant layers
- **Data Layer Registry**: Centralized registry of data stores with capabilities and participation modes
- **Execution Strategy Analyzer**: Parallel vs Sequential execution based on query characteristics
- **Result Aggregator**: Deduplication, distributed sorting, composite continuation tokens

**Architecture Decision Tree**:
```csharp
if (mode == DataLayerMode.Isolation)
{
    // PASS-THROUGH: Isolation mode always queries single data store
    return await _repository.SearchAsync(request, ct);
}
else if (mode == DataLayerMode.Distributed)
{
    var participantLayers = await GetParticipantLayersAsync(ct);

    if (participantLayers.Count == 0)
        return EmptyResult();  // PASS-THROUGH
    else if (participantLayers.Count == 1)
        return await participantLayers[0].SearchAsync(request, ct);  // PASS-THROUGH
    else
        return await _executor.SearchAsync(distributedRequest, ct);  // FANOUT
}
```

**Isolation Mode Strategies**:
1. **Database per Tenant**: Complete database isolation
2. **Schema per Tenant**: Shared database, separate schemas
3. **Partition Key per Tenant**: Shared tables with partitioning

**Distributed Mode Components**:
- **Participation Modes**: OptIn, OptOut, AlwaysIncluded, NeverIncluded
- **Pre-Query Filters**: De-identification for research networks
- **Aggregation Levels**: Full, SummaryOnly, CountOnly, Custom
- **Composite Continuation Tokens**: Format: `{layerId}:{localToken}|{layerId}:{localToken}`

**Performance Comparison**:
| Scenario | Execution Path | Overhead | Latency (P95) |
|----------|---------------|----------|---------------|
| Isolation mode | Pass-through to single repository | None | < 100ms |
| Distributed mode (1 layer) | Pass-through to single layer | None | < 100ms |
| Distributed mode (2-3 layers, parallel) | Fanout + aggregation + deduplication | ~50ms | < 500ms |
| Distributed mode (10 layers, parallel) | Fanout + aggregation + deduplication | ~200ms | < 1s |

**Real-World Use Cases**:
1. **Healthcare Research Network**: 5 hospitals, each with separate data stores, OptIn participation
2. **Multi-Region Deployment**: Geographic data residency with global search
3. **SaaS with Premium Tier**: Basic tenants (partition key), Premium tenants (dedicated database)

**Security Considerations**:
- Tenant isolation enforcement in Isolation mode
- Distributed query authorization (verify access to each layer)
- De-identification filters for research networks
- Audit logging for cross-layer queries

**Incorporated Into**:
- **ADR-2500**: New architectural decisions and Phase 20 for multi-tenancy implementation
- **ADR-2509 (Phase 6)**: Multi-tenant foundations with Isolation mode basics
- **ADR-2520 (NEW Phase 20)**: Full multi-tenancy data partitioning modes (Weeks 95-106, 192 hours)

---

## Key Decisions Matrix

| Decision | Investigation Source | ADR Reference | Status |
|----------|---------------------|---------------|--------|
| Vertical slice architecture | fhir-server-v2-architecture.md | All ADRs | ✅ Adopted |
| Medino messaging | fhir-server-v2-architecture.md | All ADRs | ✅ Adopted |
| ASP.NET Core Minimal APIs | decoupled-resource-index-storage.md | ADR-2501 | ✅ Adopted |
| Memory-efficient patterns | memory-efficient-fhir-patterns.md | ADR-2501, 2502, 2504 | ✅ Adopted |
| Transaction table abstraction | transaction-table-core-abstraction.md | ADR-2504 | ✅ Adopted |
| File-based storage with metadata sidecar | phase1-file-based-storage-with-search.md | ADR-2501 | ✅ Adopted |
| InMemory search from microsoft/fhir-server | phase1-file-based-storage-with-search.md | ADR-2502 | ✅ Adopted |
| URN-based storage locations | decoupled-resource-index-storage.md | ADR-2511, 2512 | ✅ Adopted |
| Separate resource/index stores | decoupled-resource-index-storage.md | ADR-2511, 2512, 2513+ | ✅ Adopted |
| SQL 3-table split | storage-architecture-v2.md | ADR-2506 | ✅ Adopted |
| Cosmos compact search index | cosmos-10pb-storage-architecture.md | ADR-2507 | ✅ Adopted |
| Bundle processing with channels | bundle-processing-with-channels.md | ADR-2504 | ✅ Adopted |
| ASP.NET Core pipeline routing | bundle-processing-with-channels.md | ADR-2504 | ✅ Adopted |
| Segmented capability statement | dynamic-capability-statement-generation.md | ADR-2503, 2506, 2509, 2510 | ✅ Adopted |
| Capability statement enforcement | rbac-authorization-with-capability-enforcement.md | ADR-2503, 2506, 2513 | ✅ Adopted |
| Layered authorization pipeline | rbac-authorization-with-capability-enforcement.md | ADR-2503, 2506, 2513 | ✅ Adopted |
| Source-aware search parameter priority | custom-search-parameters-lifecycle.md | ADR-2503, 2514, 2515 | ✅ Adopted |
| Search parameter lifecycle states | custom-search-parameters-lifecycle.md | ADR-2515 | ✅ Adopted |
| R5+ constraint field support | custom-search-parameters-lifecycle.md | ADR-2514, 2515 | ✅ Adopted |
| Path-based version routing | fhir-version-conversion-and-multi-version-support.md | ADR-2508 | ✅ Adopted |
| Version-tagged storage (no conversion) | fhir-version-conversion-and-multi-version-support.md | ADR-2508 | ✅ Adopted |
| Version enforcement middleware | fhir-version-conversion-and-multi-version-support.md | ADR-2508 | ✅ Adopted |
| R6 Additional Resource support | custom-structures-and-r6-extensibility.md | ADR-2522 | ✅ Adopted |
| Extension indexing for search | custom-structures-and-r6-extensibility.md | ADR-2514, 2522 | ✅ Adopted |
| Dynamic IG hot-loading | custom-structures-and-r6-extensibility.md | ADR-2522 | ✅ Adopted |
| DurableTask for background jobs | background-jobs-with-durabletask.md | ADR-2510, 2515, 2516, 2517 | ✅ Adopted |
| Two-tier validation | two-tier-validation-architecture.md | ADR-2509 | ✅ Adopted |
| Bulk import/export with channels | bulk-import-export-with-channels.md | ADR-2508 | ✅ Adopted |
| Distributed caching abstraction | caching-abstraction-architecture.md | ADR-2505 | ✅ Adopted |
| Multi-provider identity | smart-identity-provider-abstraction.md | ADR-2510 | ✅ Adopted |
| Composite schema provider | multi-version-ig-loading-system.md | ADR-2511 | ✅ Adopted |
| Dual-mode data partitioning | multi-tenancy-data-partitioning-modes.md | ADR-2500, 2509, 2520 | ✅ Adopted |
| Pass-through optimization | multi-tenancy-data-partitioning-modes.md | ADR-2520 | ✅ Adopted |
| Data Layer Registry | multi-tenancy-data-partitioning-modes.md | ADR-2520 | ✅ Adopted |
| Distributed query execution | multi-tenancy-data-partitioning-modes.md | ADR-2520 | ✅ Adopted |

---

## Implementation Timeline

Based on all investigations, the phased implementation roadmap is:

**Prototype Phase** (Week 1):
- Setup project structure
- Implement PUT /Patient/{id} and GET /Patient/{id} vertically
- File-based storage with metadata sidecar files

**Phase 1.1** (Week 2): Bundle Processing with Channels

**Phase 1.2** (Week 3): Search Implementation (InMemory architecture)

**Phase 1.3** (Week 4): Search Parameter Types

**Phase 2-15**: See phase-specific ADRs for detailed implementation plans

---

## Testing Strategy

**E2E Test Success Criteria**: All **118 E2E test files** from src-old/test must pass

**Per-Phase Requirements**: 80% test coverage using xUnit + NSubstitute

**Performance Benchmarks**:
- CRUD operations: <10ms (in-memory), <20ms (file), <100ms (SQL/Cosmos)
- Search operations: <50ms (in-memory), <100ms (file), <200ms (SQL/Cosmos)

---

## Next Steps

1. **Create Phase-Specific ADRs**: Split ADR-2510 into focused ADRs per phase
2. **Begin Prototype Phase**: Setup project structure and implement PUT/GET
3. **Continuous Validation**: Measure against E2E tests from legacy codebase
4. **Iterative Refinement**: Adjust based on test results and performance data

---

## References

All investigation documents are located in `/docs/investigations/`:
- Architecture: 4 documents
- Storage: 4 documents
- Features: 4 documents
- Infrastructure: 3 documents
- Security: 3 documents
- Analysis: 2 documents

**Total**: 26 comprehensive investigation documents informing FHIR Server v2 design.
