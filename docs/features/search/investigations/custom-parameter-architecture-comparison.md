# Investigation: Custom Search Parameter Architecture Comparison

**Feature**: search
**Status**: In Progress
**Created**: 2025-12-19

## Approach

This investigation evaluates three distinct architectural approaches for enabling customers to create custom FHIR search parameters (custom indexes) in Ignixa. Each proposal is grounded in production FHIR server implementations, analyzing their storage strategies, conflict resolution, reindexing mechanisms, and operational characteristics.

The goal is to identify the optimal approach for Ignixa that balances:
- **F5 Developer Experience** - Works with minimal setup
- **Multi-tenancy** - Per-tenant custom parameters without cross-contamination
- **FHIR Compliance** - Standards-based SearchParameter resources
- **Performance** - Efficient indexing and search execution
- **Operational Simplicity** - Clear lifecycle management without hidden state

## Evidence: Prior Art Analysis

### 1. Microsoft FHIR Server (src-old)

**Source**: `E:\data\src\fhir-server-contrib\src-old\`

**Architecture**:
- **Dual Storage**: In-memory `ConcurrentDictionary` + persistent data store (SQL/Cosmos/File)
- **Conflict Resolution**: Sophisticated semantic FhirPath expression comparison via `SearchParameterComparer`
- **Reindexing**: Hash-based change detection triggers background reindex jobs
- **Lifecycle**: Seven-state model (Enabled, Supported, PendingDelete, Deleted, Unsupported, PendingDisable, Disabled)
- **Concurrency**: Distributed locks per SearchParameter URL across instances
- **Cache Freshness**: Periodic background refresh checks DB max `LastUpdated` timestamp

**Key Files**:
- `Microsoft.Health.Fhir.Core/Features/Search/Registry/SearchParameterDefinitionManager.cs`
- `Microsoft.Health.Fhir.Shared.Core/Features/Search/Parameters/SearchParameterComparer.cs`
- `Microsoft.Health.Fhir.Core/Features/Operations/Reindex/CreateReindexRequestHandler.cs`

**Strengths**:
- Handles multiple parameter versions via `ConcurrentQueue<SearchParameterInfo>` per code
- Deep semantic validation prevents incompatible parameter changes
- Robust multi-instance coordination via distributed locks
- Clear separation: parameter updates blocked during reindex

**Weaknesses**:
- Complex seven-state lifecycle increases cognitive load
- Dual storage creates eventual consistency challenges
- Heavy dependency on distributed locking (coordination overhead)
- In-memory dictionaries require cache invalidation across instances

**Performance Characteristics**:
- Conflict detection: O(n) where n = parameters with same code (typically 1-3)
- Cache refresh: Periodic polling (configurable, default ~30s lag between instances)
- Reindex: Batch-based (100 resources/batch), single-threaded per resource type

### 2. HAPI FHIR JPA Server

**Sources**:
- [HAPI FHIR Search Documentation](https://hapifhir.io/hapi-fhir/docs/server_jpa/search.html)
- [Smile CDR Custom Search Parameters](https://smilecdr.com/docs/fhir_standard/fhir_search_custom_search_parameters.html)

**Architecture**:
- **Storage**: JPA entities for SearchParameter resources, indexed in relational tables
- **Conflict Resolution**: Not explicitly documented (likely overwrite or last-write-wins)
- **Reindexing**: `$reindex` operation with `searchparameter-enabled-for-searching` extension to disable during indexing
- **Lifecycle**: Two-state (enabled/disabled via extension)
- **Special Optimizations**:
  - **Uplifted Refchains**: Pre-computed indexes for common chained searches (trades write speed for read speed)
  - **Combo Search Parameters**: Multi-column indexes for parameter combinations (avoids joins)

**Key Mechanisms**:
- Auto-indexing on first startup (inserts default search parameters into DB)
- FhirPath expression evaluation extracts values for indexing
- New resources indexed immediately; existing require explicit `$reindex`
- Extension: `searchparameter-enabled-for-searching=false` prevents searches until ready

**Strengths**:
- Simple two-state lifecycle (enabled/disabled)
- Uplifted refchains solve common chained search performance problems
- JPA abstraction supports multiple SQL databases (PostgreSQL, Oracle, SQL Server, etc.)
- Combo parameters optimize frequent multi-parameter searches

**Weaknesses**:
- No documented conflict resolution strategy (risk of silent overwrites)
- Reindexing window where old data is invisible (must manually disable parameter)
- Tight coupling to JPA/Hibernate (limits storage flexibility)
- Stale data risk with uplifted refchains (denormalized indexes)

**Performance Characteristics**:
- Chained searches: 2x slower without uplifted refchains
- Combo parameters: Single table lookup vs multi-table join
- Reindex: Not explicitly documented (likely similar batch-based approach)

### 3. LinuxForHealth (IBM) FHIR Server

**Sources**:
- [LinuxForHealth FHIR Search Configuration](https://linuxforhealth.github.io/FHIR/guides/FHIRSearchConfiguration/)
- [GitHub - LinuxForHealth/FHIR](https://github.com/LinuxForHealth/FHIR)

**Architecture**:
- **Storage**: Tenant-specific configuration files (`extension-search-parameters.json` bundles)
- **Conflict Resolution**: File-based precedence (tenant config overrides defaults)
- **Reindexing**: Dual-mode operation
  - **Server-driven**: Background job processes all resources
  - **Client-driven**: Caller controls batch size and pacing
- **Lifecycle**: Implicit (parameters active once file loaded + reindex completes)
- **Multi-Tenancy**: Per-tenant config directory (`${server.config.dir}/config/<tenant-id>`)

**Key Mechanisms**:
- SearchParameter bundles in JSON files loaded at startup
- FhirPath expressions evaluated during resource create/update/reindex
- Tenant isolation via separate config directories
- No runtime API to add parameters (configuration-as-code model)

**Strengths**:
- **Configuration-as-code**: Search parameters version-controlled with infrastructure
- **Tenant isolation**: Physical file separation prevents cross-tenant leaks
- **F5 simplicity**: Drop JSON file in directory, restart server, done
- **Client-driven reindex**: Allows external orchestration and throttling

**Weaknesses**:
- No runtime API (requires restart to add parameters)
- File-based conflict resolution is implicit (last-loaded wins within tenant)
- No status tracking (parameter is "ready" when reindex finishes, but no API visibility)
- Limited observability (can't query active parameters via FHIR API)

**Performance Characteristics**:
- Load time: Linear in number of SearchParameter resources in bundle
- Reindex: Client controls batch size (flexible throttling)
- Search execution: Standard FhirPath evaluation (no special optimizations documented)

## Proposal 1: Hybrid File + DB with Explicit Lifecycle (Microsoft-Inspired)

**Tagline**: "Best of both worlds - file-based for base/IG parameters, DB-stored for custom, with explicit status tracking"

### Architecture

```
┌─────────────────────────────────────────────────────────┐
│  SearchParameter Sources                                 │
├─────────────────────────────────────────────────────────┤
│  1. Embedded JSON (Base R4 spec)                        │
│     - Loaded at startup                                  │
│     - Priority: 1 (highest)                              │
│                                                           │
│  2. IG Package Files (Implementation Guides)             │
│     - Extracted from NPM packages                        │
│     - Priority: 2                                        │
│                                                           │
│  3. Tenant Config Files (Custom static)                  │
│     - config/<tenant-id>/search-parameters.json          │
│     - Priority: 3                                        │
│                                                           │
│  4. Database (Runtime custom via POST)                   │
│     - Stored as FHIR SearchParameter resources           │
│     - Priority: 4 (lowest)                               │
└─────────────────────────────────────────────────────────┘
              ↓ All loaded into ↓
┌─────────────────────────────────────────────────────────┐
│  SearchParameterRegistry (In-Memory)                     │
├─────────────────────────────────────────────────────────┤
│  - Dictionary<string, SearchParameterInfo> by URL       │
│  - Dictionary<string, List<SearchParameterInfo>> by code│
│  - Conflict resolution via priority + semantic compare  │
│  - Status: Supported → Reindexing → Enabled             │
└─────────────────────────────────────────────────────────┘
```

### Conflict Resolution Strategy

```csharp
public enum ConflictResolution
{
    UseHigherPriority,    // Base beats IG beats tenant beats runtime
    SemanticCompatible,   // FhirPath expressions are subset/superset
    Reject                // Incompatible, return error
}

public class SearchParameterConflictResolver
{
    public ConflictResolution ResolveConflict(
        SearchParameterInfo existing,
        SearchParameterInfo newParam)
    {
        // Step 1: Priority check
        if (newParam.Priority > existing.Priority)
            return ConflictResolution.Reject; // Lower priority can't replace higher

        if (newParam.Priority < existing.Priority)
            return ConflictResolution.UseHigherPriority; // Higher wins

        // Step 2: Same priority - semantic comparison
        var comparison = _expressionComparer.Compare(
            existing.Expression,
            newParam.Expression);

        return comparison switch
        {
            ExpressionRelation.Equal => ConflictResolution.SemanticCompatible,
            ExpressionRelation.Subset => ConflictResolution.SemanticCompatible,
            ExpressionRelation.Superset => ConflictResolution.SemanticCompatible,
            _ => ConflictResolution.Reject
        };
    }
}
```

### Lifecycle Management

```
POST /SearchParameter
  ↓
1. Validate FhirPath expression compiles
2. Conflict check against existing parameters
3. Store in DB with Status = "Supported"
4. Add to in-memory registry (NOT searchable yet)
5. Return 201 Created with warning: "Parameter requires reindex before searchable"
  ↓
POST /$reindex?url=http://example.com/SearchParameter/custom-foo
  ↓
6. Update Status = "Reindexing"
7. Queue background job or return job handle for client-driven
8. For each resource of base types:
   - Evaluate FhirPath expression
   - Extract values
   - Store in search index tables
9. Update Status = "Enabled"
10. Parameter now searchable
```

### Database Schema (Minimal)

```sql
-- Stores runtime custom parameters only (file-based params NOT stored)
CREATE TABLE SearchParameterMetadata (
    Url VARCHAR(512) PRIMARY KEY,
    TenantId INT NOT NULL,
    Code VARCHAR(128) NOT NULL,
    ResourceTypes VARCHAR(512) NOT NULL, -- JSON array
    Expression VARCHAR(4000) NOT NULL,
    Type VARCHAR(32) NOT NULL,
    Source VARCHAR(32) NOT NULL, -- 'Base' | 'ImplementationGuide' | 'TenantConfig' | 'RuntimeCustom'
    Priority INT NOT NULL,
    Status VARCHAR(32) NOT NULL, -- 'Supported' | 'Reindexing' | 'Enabled' | 'Disabled'
    CreatedAt DATETIME NOT NULL,
    LastReindexedAt DATETIME NULL
);

CREATE INDEX IX_SearchParameterMetadata_TenantId_Code
    ON SearchParameterMetadata(TenantId, Code);
CREATE INDEX IX_SearchParameterMetadata_Status
    ON SearchParameterMetadata(Status);
```

**Note**: Base/IG/TenantConfig parameters are NOT stored in DB - loaded from files at startup. DB only stores runtime POSTed custom parameters.

### Tradeoffs

| Pros | Cons |
|------|------|
| **Clear priority hierarchy** prevents IG/custom from clobbering base parameters | **Complex**: Four parameter sources increases mental model overhead |
| **File-based base/IG params** enable GitOps workflows (version control) | **Dual storage**: File + DB creates potential cache invalidation issues |
| **Explicit status lifecycle** makes reindex state visible to operators | **Restart required** for tenant config file changes (unlike runtime POST) |
| **Semantic expression comparison** prevents incompatible parameter changes | **Performance**: FhirPath parsing during conflict check adds latency |
| **Tenant isolation** via config directories + TenantId column | **Migration complexity**: Moving custom params between file/DB requires manual export |

### Alignment

- [x] Follows layer rules (API -> Application -> Domain -> DataLayer)
- [x] F5 Developer Experience (drop config file OR POST runtime, both work)
- [x] FHIR spec compliance (SearchParameter resources)
- [x] Multi-tenancy (tenant config directories + TenantId DB column)
- [x] Consistent with existing patterns (mimics package-management feature)

### Implementation Estimate

- **Core registry + conflict resolver**: 3 days
- **File loading (base/IG/tenant)**: 2 days
- **DB storage for runtime custom**: 2 days
- **API endpoints (POST /SearchParameter, GET /SearchParameter)**: 2 days
- **Reindex orchestration**: 4 days
- **Tests (unit + integration)**: 3 days
- **Total**: ~16 days (3.2 weeks)

## Proposal 2: Pure Configuration-as-Code (LinuxForHealth-Inspired)

**Tagline**: "Simple, stateless, GitOps-friendly - everything in version-controlled files"

### Architecture

```
Project Root
├── config/
│   ├── default/
│   │   └── search-parameters.json  (Base R4 + common extensions)
│   └── tenants/
│       ├── tenant-1/
│       │   └── search-parameters.json  (Tenant-1 custom params)
│       └── tenant-2/
│           └── search-parameters.json  (Tenant-2 custom params)
└── packages/
    └── hl7.fhir.us.core@6.1.0/
        └── SearchParameter-*.json  (IG parameters)

↓ Startup ↓

SearchParameterRegistry (In-Memory Only)
- Loads all files in precedence order
- No database persistence
- No runtime API to add parameters
```

### Parameter Loading

```csharp
public class SearchParameterFileLoader
{
    public async Task<List<SearchParameterInfo>> LoadAllAsync(int? tenantId, CancellationToken ct)
    {
        var parameters = new List<SearchParameterInfo>();

        // 1. Load base parameters (embedded resources)
        parameters.AddRange(await LoadEmbeddedBaseParametersAsync(ct));

        // 2. Load IG package parameters
        foreach (var package in Directory.GetDirectories("packages"))
        {
            parameters.AddRange(await LoadPackageParametersAsync(package, ct));
        }

        // 3. Load default config
        if (File.Exists("config/default/search-parameters.json"))
        {
            parameters.AddRange(await LoadBundleAsync(
                "config/default/search-parameters.json", ct));
        }

        // 4. Load tenant-specific config
        if (tenantId.HasValue)
        {
            var tenantFile = $"config/tenants/tenant-{tenantId}/search-parameters.json";
            if (File.Exists(tenantFile))
            {
                parameters.AddRange(await LoadBundleAsync(tenantFile, ct));
            }
        }

        return parameters;
    }
}
```

### Conflict Resolution

**Rule**: Last-loaded wins within same tenant. Tenant parameters override default, default overrides IG, IG overrides base.

```csharp
// Simple dictionary merge - last write wins
public void MergeParameters(List<SearchParameterInfo> newParams)
{
    foreach (var param in newParams)
    {
        _parametersByUrl[param.Url] = param; // Overwrite

        if (!_parametersByCode.ContainsKey(param.Code))
            _parametersByCode[param.Code] = new List<SearchParameterInfo>();

        _parametersByCode[param.Code].RemoveAll(p => p.Url == param.Url);
        _parametersByCode[param.Code].Add(param);
    }
}
```

### Reindexing Strategy

**Two modes**:

**1. Server-Driven (Background Job)**:
```csharp
POST /$reindex
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "resourceType",
    "valueCode": "Patient"
  }]
}

Response: 202 Accepted
Location: /$reindex/job-abc123

// Poll job status
GET /$reindex/job-abc123
{
  "status": "in-progress",
  "resourcesProcessed": 5000,
  "resourcesTotal": 10000
}
```

**2. Client-Driven (Streaming)**:
```csharp
// Client controls batch size
GET /Patient?_reindex=true&_count=100

// Returns:
{
  "resourceType": "Bundle",
  "type": "searchset",
  "entry": [ /* 100 Patient resources */ ],
  "link": [{
    "relation": "next",
    "url": "/Patient?_reindex=true&_count=100&_offset=100"
  }]
}

// Client fetches next page when ready (throttling built-in)
```

### No Status Tracking

Parameters are either:
- **Active**: Loaded from file, available for search
- **Inactive**: Not in any config file, not available

No intermediate states. No DB status column. Reindex progress tracked by job ID only (ephemeral).

### Tradeoffs

| Pros | Cons |
|------|------|
| **GitOps-native**: All config in version control (audit trail, rollback, CI/CD) | **No runtime API**: Must restart server to add parameters |
| **Stateless**: No DB storage, no cache invalidation, no eventual consistency | **No visibility**: Can't query "which parameters are active" via FHIR API |
| **F5 simplicity**: Drop file in directory, restart, done | **Restart downtime**: Adding parameter requires brief service interruption |
| **Tenant isolation**: Physical file separation guarantees no cross-contamination | **No partial reindex tracking**: Can't mark single parameter as "reindexing" |
| **Easy backup/restore**: Copy config directory = full backup | **Large config files**: All tenant params in one JSON bundle (potential merge conflicts) |

### Alignment

- [x] Follows layer rules (API -> Application -> Domain -> DataLayer)
- [x] F5 Developer Experience (file drop + restart is simplest possible)
- [x] FHIR spec compliance (SearchParameter resources in bundles)
- [x] Multi-tenancy (tenant config directories)
- [?] Consistent with existing patterns (package-management uses DB storage, this is file-only)

### Implementation Estimate

- **File loader + bundle parser**: 2 days
- **In-memory registry (no DB)**: 1 day
- **Tenant-aware loading**: 1 day
- **Reindex endpoints (server-driven + client-driven)**: 3 days
- **Tests**: 2 days
- **Total**: ~9 days (1.8 weeks)

## Proposal 3: Event-Sourced Parameter Registry (Novel Approach)

**Tagline**: "Immutable event log + materialized views for auditability and time-travel debugging"

### Architecture

```
Event Store (Append-Only Log)
┌────────────────────────────────────────────┐
│ Event 1: SearchParameterCreated            │
│   - URL, Code, Expression, Source, TenantId│
│   - Timestamp: 2025-01-15T10:30:00Z        │
├────────────────────────────────────────────┤
│ Event 2: ReindexStarted                    │
│   - SearchParameterURL                     │
│   - Timestamp: 2025-01-15T10:31:00Z        │
├────────────────────────────────────────────┤
│ Event 3: ReindexCompleted                  │
│   - SearchParameterURL                     │
│   - ResourcesIndexed: 10000                │
│   - Timestamp: 2025-01-15T10:35:00Z        │
├────────────────────────────────────────────┤
│ Event 4: SearchParameterDisabled           │
│   - SearchParameterURL                     │
│   - Reason: "Performance issues"           │
│   - Timestamp: 2025-01-16T14:00:00Z        │
└────────────────────────────────────────────┘
         ↓ Projection ↓
┌────────────────────────────────────────────┐
│ Materialized View: Active Parameters       │
├────────────────────────────────────────────┤
│ URL                          | Status      │
│ http://example.com/foo       | Enabled     │
│ http://example.com/bar       | Disabled    │
└────────────────────────────────────────────┘
```

### Event Types

```csharp
// Base event
public abstract record SearchParameterEvent
{
    public required Guid EventId { get; init; }
    public required string SearchParameterUrl { get; init; }
    public required int TenantId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string UserId { get; init; } // Who triggered the event
}

// Specific events
public record SearchParameterCreated : SearchParameterEvent
{
    public required string Code { get; init; }
    public required string[] Base { get; init; }
    public required string Expression { get; init; }
    public required SearchParameterSource Source { get; init; }
}

public record ReindexStarted : SearchParameterEvent
{
    public required string JobId { get; init; }
}

public record ReindexProgressed : SearchParameterEvent
{
    public required string JobId { get; init; }
    public required int ResourcesProcessed { get; init; }
    public required int ResourcesTotal { get; init; }
}

public record ReindexCompleted : SearchParameterEvent
{
    public required string JobId { get; init; }
    public required int ResourcesIndexed { get; init; }
}

public record SearchParameterDisabled : SearchParameterEvent
{
    public required string Reason { get; init; }
}

public record SearchParameterEnabled : SearchParameterEvent;

public record SearchParameterDeleted : SearchParameterEvent;
```

### Projection: Current State

```csharp
public class SearchParameterProjection
{
    private readonly Dictionary<string, SearchParameterInfo> _currentState = new();

    public void Apply(SearchParameterEvent evt)
    {
        switch (evt)
        {
            case SearchParameterCreated created:
                _currentState[created.SearchParameterUrl] = new SearchParameterInfo
                {
                    Url = created.SearchParameterUrl,
                    Code = created.Code,
                    Base = created.Base,
                    Expression = created.Expression,
                    Source = created.Source,
                    Status = SearchParameterStatus.Supported,
                    CreatedAt = created.Timestamp
                };
                break;

            case ReindexStarted started:
                if (_currentState.TryGetValue(started.SearchParameterUrl, out var param))
                {
                    _currentState[started.SearchParameterUrl] = param with
                    {
                        Status = SearchParameterStatus.Reindexing
                    };
                }
                break;

            case ReindexCompleted completed:
                if (_currentState.TryGetValue(completed.SearchParameterUrl, out var param))
                {
                    _currentState[completed.SearchParameterUrl] = param with
                    {
                        Status = SearchParameterStatus.Enabled,
                        LastReindexedAt = completed.Timestamp
                    };
                }
                break;

            case SearchParameterDisabled disabled:
                if (_currentState.TryGetValue(disabled.SearchParameterUrl, out var param))
                {
                    _currentState[disabled.SearchParameterUrl] = param with
                    {
                        Status = SearchParameterStatus.Disabled
                    };
                }
                break;

            case SearchParameterEnabled enabled:
                if (_currentState.TryGetValue(enabled.SearchParameterUrl, out var param))
                {
                    _currentState[enabled.SearchParameterUrl] = param with
                    {
                        Status = SearchParameterStatus.Enabled
                    };
                }
                break;

            case SearchParameterDeleted deleted:
                _currentState.Remove(deleted.SearchParameterUrl);
                break;
        }
    }

    public IReadOnlyDictionary<string, SearchParameterInfo> GetCurrentState()
        => _currentState;
}
```

### Event Store Implementation

```csharp
public interface ISearchParameterEventStore
{
    // Append event (immutable, never update/delete)
    ValueTask AppendAsync(SearchParameterEvent evt, CancellationToken ct);

    // Read all events for a parameter (for debugging)
    IAsyncEnumerable<SearchParameterEvent> GetEventsAsync(
        string searchParameterUrl,
        CancellationToken ct);

    // Read all events since timestamp (for cache rebuild)
    IAsyncEnumerable<SearchParameterEvent> GetEventsSinceAsync(
        DateTimeOffset since,
        CancellationToken ct);

    // Replay all events to build projection
    IAsyncEnumerable<SearchParameterEvent> GetAllEventsAsync(CancellationToken ct);
}

// File-based implementation (one JSON file per event)
public class FileSearchParameterEventStore : ISearchParameterEventStore
{
    private readonly string _eventDirectory;

    public async ValueTask AppendAsync(SearchParameterEvent evt, CancellationToken ct)
    {
        var filename = $"{evt.Timestamp:yyyyMMddHHmmssfff}_{evt.EventId}.json";
        var path = Path.Combine(_eventDirectory, filename);

        var json = JsonSerializer.Serialize(evt, new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new PolymorphicTypeResolver() // Serialize derived types
        });

        await File.WriteAllTextAsync(path, json, ct);
    }

    public async IAsyncEnumerable<SearchParameterEvent> GetAllEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var files = Directory.GetFiles(_eventDirectory, "*.json")
            .OrderBy(f => f); // Chronological order by filename

        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var evt = JsonSerializer.Deserialize<SearchParameterEvent>(json);
            if (evt != null)
                yield return evt;
        }
    }
}
```

### Conflict Resolution

Events are facts - they don't have conflicts. The projection decides behavior:

```csharp
// Example: Two tenants POST same parameter URL
// Tenant 1 creates: SearchParameterCreated(URL=foo, TenantId=1, Timestamp=T1)
// Tenant 2 creates: SearchParameterCreated(URL=foo, TenantId=2, Timestamp=T2)

// Projection applies tenant isolation:
public void Apply(SearchParameterCreated created)
{
    var key = $"{created.TenantId}:{created.SearchParameterUrl}";
    _currentState[key] = new SearchParameterInfo { /* ... */ };
}

// Result: Both parameters exist, isolated by tenant
```

### Time-Travel Debugging

```csharp
// Rebuild state as of 2025-01-15T12:00:00Z
public async Task<Dictionary<string, SearchParameterInfo>> GetStateAtAsync(
    DateTimeOffset pointInTime,
    CancellationToken ct)
{
    var projection = new SearchParameterProjection();

    await foreach (var evt in _eventStore.GetAllEventsAsync(ct))
    {
        if (evt.Timestamp > pointInTime)
            break;

        projection.Apply(evt);
    }

    return projection.GetCurrentState();
}

// Use case: "Why was parameter X disabled on Jan 15?"
var events = await _eventStore.GetEventsAsync("http://example.com/param-x", ct);
var disabledEvent = events.OfType<SearchParameterDisabled>().FirstOrDefault();
Console.WriteLine($"Disabled because: {disabledEvent?.Reason}");
Console.WriteLine($"Disabled by: {disabledEvent?.UserId}");
```

### Tradeoffs

| Pros | Cons |
|------|------|
| **Full audit trail**: Every parameter change logged with user + reason | **Complexity**: Event sourcing adds architectural overhead |
| **Time-travel debugging**: Replay events to see state at any point in time | **Performance**: Projection rebuild on startup (mitigated with snapshots) |
| **Immutable**: Events never deleted/modified (regulatory compliance, SOC2) | **Storage growth**: Event log grows unbounded (mitigated with archival) |
| **No lost data**: Even if projection corrupted, rebuild from event log | **Learning curve**: Team must understand event sourcing patterns |
| **Natural multi-tenancy**: Events include TenantId, projection isolates | **Overkill?**: May be too sophisticated for simple parameter management |

### Alignment

- [x] Follows layer rules (API -> Application -> Domain -> DataLayer)
- [?] F5 Developer Experience (event sourcing is NOT simple for local dev)
- [x] FHIR spec compliance (SearchParameter resources)
- [x] Multi-tenancy (TenantId in events)
- [?] Consistent with existing patterns (no other event-sourced features in Ignixa)

### Implementation Estimate

- **Event types + store interface**: 2 days
- **File-based event store**: 2 days
- **Projection builder + replay logic**: 3 days
- **API endpoints (emit events)**: 2 days
- **Reindex orchestration (emit events)**: 3 days
- **Time-travel debugging tools**: 2 days
- **Tests**: 3 days
- **Total**: ~17 days (3.4 weeks)

## Proposal 4: Versioned Search Indexes with Event History (Zero-Downtime Transitions)

**Tagline**: "Blue-green deployment for search parameters - build new index while serving from old, atomic switch when ready"

### Core Problem Solved

**Current workflow (all proposals 1-3)**:
```
1. Create SearchParameter v1
2. Status: Supported (NOT SEARCHABLE - returns incomplete results!)
3. Reindex (minutes to hours)
4. Status: Enabled (NOW SEARCHABLE)

5. Modify SearchParameter → v2
6. Status: Reindexing (STALE DATA - searches return v1 results, but v2 definition active!)
7. Reindex completes
8. Status: Enabled (NOW CORRECT)
```

**Problem**: Gap between SP change and usability. Users either:
- Get wrong results (searching before reindex)
- Get no results (parameter disabled during reindex)
- Must manually track "is reindex done yet?"

**Proposal 4 Solution**: Version the search index data itself, not just the parameter.

```
1. Create SearchParameter v1
2. Build Index v1 in background (CONCURRENT - searches fail gracefully with "not ready")
3. Index v1 complete → ATOMIC SWITCH → searches now use v1

4. Modify SearchParameter → v2
5. Build Index v2 in background (CONCURRENT - searches still use v1)
6. Index v2 complete → ATOMIC SWITCH → searches now use v2
7. Keep v1 for 24h (rollback window), then purge
```

**Key Innovation**: Search queries always use a **complete, consistent index**. Never serve partial/stale data.

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Event Store (Append-Only)                                   │
├─────────────────────────────────────────────────────────────┤
│  Event 1: SearchParameterCreated (v1)                       │
│    - URL, Expression: "Patient.name", TenantId, Timestamp   │
│  Event 2: SearchIndexBuildStarted (v1)                      │
│  Event 3: SearchIndexBuildCompleted (v1)                    │
│  Event 4: SearchIndexActivated (v1) ← Atomic switch         │
│  Event 5: SearchParameterUpdated (v2)                       │
│    - Expression: "Patient.name | Patient.alias"             │
│  Event 6: SearchIndexBuildStarted (v2)                      │
│  Event 7: SearchIndexBuildCompleted (v2)                    │
│  Event 8: SearchIndexActivated (v2) ← Atomic switch         │
│  Event 9: SearchIndexRetired (v1) ← Purge old index         │
└─────────────────────────────────────────────────────────────┘
         ↓ Projection ↓
┌─────────────────────────────────────────────────────────────┐
│  Active Search Index Registry                                │
├─────────────────────────────────────────────────────────────┤
│  URL: http://ex.com/sp/name                                 │
│    - Active Version: v2 (Enabled, searches use this)        │
│    - Building Version: v3 (Reindexing, not queryable)       │
│    - Retired Versions: v1 (kept 24h for rollback)           │
└─────────────────────────────────────────────────────────────┘
         ↓ Queries ↓
┌─────────────────────────────────────────────────────────────┐
│  Search Index Storage (Versioned)                            │
├─────────────────────────────────────────────────────────────┤
│  Index v1: [Patient/123 → "John"], [Patient/456 → "Jane"]  │
│  Index v2: [Patient/123 → "John","Johnny"], ...  (active)  │
│  Index v3: [partial data...] (building, not queryable yet) │
└─────────────────────────────────────────────────────────────┘
```

### Versioning Strategy

```csharp
public record SearchParameterVersion
{
    public required string Url { get; init; }
    public required int Version { get; init; } // Auto-increment on each change
    public required string Expression { get; init; }
    public required SearchIndexStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; } // When became queryable
    public DateTimeOffset? RetiredAt { get; init; } // When switched away from
}

public enum SearchIndexStatus
{
    Building,      // Reindex in progress, not queryable
    Active,        // Complete and serving queries
    Retired,       // Replaced by newer version, kept for rollback
    Purged         // Deleted after retention period
}
```

### Event Types (Extended from Proposal 3)

```csharp
// Parameter lifecycle
public record SearchParameterCreated : SearchParameterEvent
{
    public required string Expression { get; init; }
    public required int Version { get; init; } = 1; // Always starts at v1
}

public record SearchParameterUpdated : SearchParameterEvent
{
    public required string NewExpression { get; init; }
    public required int NewVersion { get; init; } // v2, v3, etc.
    public required string ChangeReason { get; init; }
}

// Index lifecycle (NEW)
public record SearchIndexBuildStarted : SearchParameterEvent
{
    public required int Version { get; init; }
    public required int EstimatedResourceCount { get; init; }
}

public record SearchIndexBuildProgressed : SearchParameterEvent
{
    public required int Version { get; init; }
    public required int ResourcesIndexed { get; init; }
    public required int TotalResources { get; init; }
}

public record SearchIndexBuildCompleted : SearchParameterEvent
{
    public required int Version { get; init; }
    public required int ResourcesIndexed { get; init; }
    public required TimeSpan Duration { get; init; }
}

public record SearchIndexActivated : SearchParameterEvent
{
    public required int Version { get; init; }
    public int? ReplacedVersion { get; init; } // What version is now retired
}

public record SearchIndexRetired : SearchParameterEvent
{
    public required int Version { get; init; }
    public required string Reason { get; init; } // "Replaced by v2" | "Rollback"
}

public record SearchIndexPurged : SearchParameterEvent
{
    public required int Version { get; init; }
}

// Rollback support (NEW)
public record SearchIndexRolledBack : SearchParameterEvent
{
    public required int FromVersion { get; init; }
    public required int ToVersion { get; init; }
    public required string Reason { get; init; }
}
```

### Search Index Storage Schema (Extends Existing Tables)

**Current Schema** (already exists in `97.sql`):
```sql
-- Existing: Search parameter definitions
CREATE TABLE dbo.SearchParam (
    SearchParamId        SMALLINT           IDENTITY (1, 1) NOT NULL,
    Uri                  VARCHAR (128)      COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status               VARCHAR (20)       NOT NULL,
    LastUpdated          DATETIMEOFFSET (7) NOT NULL,
    IsPartiallySupported BIT                NOT NULL,
    CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED (Uri)
);

-- Existing: String search index (one per value)
CREATE TABLE dbo.StringSearchParam (
    ResourceTypeId      SMALLINT       NOT NULL,
    ResourceSurrogateId BIGINT         NOT NULL,
    SearchParamId       SMALLINT       NOT NULL, -- FK to SearchParam
    Text                NVARCHAR (256) NOT NULL,
    TextOverflow        NVARCHAR (MAX) NULL,
    IsMin               BIT            DEFAULT 0 NOT NULL,
    IsMax               BIT            DEFAULT 0 NOT NULL
);

-- Similarly: DateTimeSearchParam, TokenSearchParam, NumberSearchParam, etc.
-- All follow same pattern: (ResourceTypeId, ResourceSurrogateId, SearchParamId, <value columns>)

-- Existing: Reindex job tracking
CREATE TABLE dbo.ReindexJob (
    Id                VARCHAR (64)  NOT NULL,
    Status            VARCHAR (10)  NOT NULL,
    HeartbeatDateTime DATETIME2 (7) NULL,
    RawJobRecord      VARCHAR (MAX) NOT NULL, -- JSON payload
    JobVersion        ROWVERSION    NOT NULL,
    CONSTRAINT PKC_ReindexJob PRIMARY KEY CLUSTERED (Id)
);
```

**Proposal 4 Extensions** (backward-compatible additions):

```sql
-- EXTEND SearchParam with version tracking columns
ALTER TABLE dbo.SearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1,
    Source VARCHAR(32) NOT NULL DEFAULT 'BaseSpecification', -- 'BaseSpecification' | 'ImplementationGuide' | 'Custom' | 'CustomPackage'
    SourcePackage VARCHAR(256) NULL, -- e.g., 'hl7.fhir.us.core#6.1.0'
    Expression VARCHAR(4000) NULL, -- FhirPath expression
    CreatedAt DATETIMEOFFSET(7) NOT NULL DEFAULT GETUTCDATE(),
    ActivatedAt DATETIMEOFFSET(7) NULL, -- When version became active
    RetiredAt DATETIMEOFFSET(7) NULL, -- When version was replaced
    PurgedAt DATETIMEOFFSET(7) NULL, -- When index data was deleted
    ResourcesIndexed INT NULL; -- Count of resources indexed

-- Change primary key to allow multiple versions per URI
ALTER TABLE dbo.SearchParam DROP CONSTRAINT PKC_SearchParam;
ALTER TABLE dbo.SearchParam ADD CONSTRAINT PKC_SearchParam
    PRIMARY KEY CLUSTERED (Uri, ParameterVersion);

-- NEW: Track active version per search parameter
CREATE TABLE dbo.SearchParameterActiveVersion (
    Uri VARCHAR(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ActiveVersion INT NOT NULL, -- Version currently serving queries
    BuildingVersion INT NULL, -- Version currently being indexed
    ActiveSince DATETIMEOFFSET(7) NOT NULL,
    LastModified DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT PKC_SearchParameterActiveVersion PRIMARY KEY CLUSTERED (Uri)
);

-- EXTEND existing search index tables with ParameterVersion column
-- This allows multiple versions of same parameter to coexist
ALTER TABLE dbo.StringSearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

ALTER TABLE dbo.TokenSearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

ALTER TABLE dbo.DateTimeSearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

ALTER TABLE dbo.NumberSearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

ALTER TABLE dbo.QuantitySearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

ALTER TABLE dbo.ReferenceSearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

ALTER TABLE dbo.UriSearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1;

-- Update clustered indexes to include ParameterVersion
-- Example for StringSearchParam (repeat for all search param tables)
DROP INDEX IXC_StringSearchParam ON dbo.StringSearchParam;
CREATE CLUSTERED INDEX IXC_StringSearchParam
    ON dbo.StringSearchParam(ResourceTypeId, ResourceSurrogateId, SearchParamId, ParameterVersion)
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

-- Add filtered index for active version queries only
-- This keeps query performance identical to current (unversioned) behavior
CREATE INDEX IX_StringSearchParam_ActiveVersion
    ON dbo.StringSearchParam(SearchParamId, Text, ParameterVersion)
    INCLUDE(TextOverflow, IsMin, IsMax)
    WHERE ParameterVersion IN (
        SELECT ActiveVersion
        FROM dbo.SearchParameterActiveVersion spav
        INNER JOIN dbo.SearchParam sp ON sp.Uri = spav.Uri AND sp.ParameterVersion = spav.ActiveVersion
        WHERE sp.SearchParamId = SearchParamId
    )
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);
```

**Migration Strategy** (for existing deployments):

```sql
-- Step 1: Add new columns with defaults (allows existing code to continue working)
ALTER TABLE dbo.SearchParam ADD
    ParameterVersion INT NOT NULL DEFAULT 1,
    Source VARCHAR(32) NOT NULL DEFAULT 'BaseSpecification',
    Expression VARCHAR(4000) NULL,
    CreatedAt DATETIMEOFFSET(7) NOT NULL DEFAULT GETUTCDATE();

-- Step 2: Populate Expression from existing metadata (if stored elsewhere)
-- UPDATE dbo.SearchParam SET Expression = <from existing source>;

-- Step 3: Add ParameterVersion to all search index tables
ALTER TABLE dbo.StringSearchParam ADD ParameterVersion INT NOT NULL DEFAULT 1;
-- (repeat for all search param tables)

-- Step 4: Create active version tracking table
CREATE TABLE dbo.SearchParameterActiveVersion (
    Uri VARCHAR(128) NOT NULL PRIMARY KEY,
    ActiveVersion INT NOT NULL DEFAULT 1, -- All existing params are v1
    BuildingVersion INT NULL,
    ActiveSince DATETIMEOFFSET(7) NOT NULL DEFAULT GETUTCDATE(),
    LastModified DATETIMEOFFSET(7) NOT NULL DEFAULT GETUTCDATE()
);

-- Step 5: Populate active version table with all existing parameters
INSERT INTO dbo.SearchParameterActiveVersion (Uri, ActiveVersion, ActiveSince, LastModified)
SELECT Uri, 1, CreatedAt, LastUpdated
FROM dbo.SearchParam;

-- Step 6: Rebuild indexes to include ParameterVersion (can be done online)
-- (Rebuild clustered indexes as shown above)
```

**Backward Compatibility**:

```csharp
// Existing queries continue to work (default ParameterVersion = 1)
var results = await dbContext.StringSearchParams
    .Where(sp => sp.SearchParamId == paramId && sp.Text.Contains(searchText))
    .ToListAsync();

// Versioned queries explicitly filter by active version
var activeVersion = await dbContext.SearchParameterActiveVersions
    .Where(v => v.Uri == paramUri)
    .Select(v => v.ActiveVersion)
    .FirstOrDefaultAsync();

var results = await dbContext.StringSearchParams
    .Where(sp =>
        sp.SearchParamId == paramId &&
        sp.ParameterVersion == activeVersion && // NEW: Version filter
        sp.Text.Contains(searchText))
    .ToListAsync();
```

**Benefits of Reusing Existing Tables**:

| Benefit | Impact |
|---------|--------|
| **Zero migration risk** | Existing indexes continue working with default ParameterVersion=1 |
| **No data movement** | ALTER TABLE adds columns in-place (metadata-only for defaults) |
| **Partition scheme preserved** | PartitionScheme_ResourceTypeId remains unchanged |
| **Query plan stability** | Existing queries use same indexes (filtered indexes for versioned queries) |
| **Storage efficiency** | No duplicate data during migration (only new versions add rows) |
| **Tooling compatibility** | Existing scripts, ORMs, and monitoring tools continue working |

### Atomic Activation Logic

```csharp
public class SearchIndexActivationService
{
    private readonly ISearchParameterEventStore _eventStore;
    private readonly ISearchIndexRepository _indexRepo;

    /// <summary>
    /// Atomically switch from old version to new version
    /// </summary>
    public async ValueTask ActivateVersionAsync(
        string searchParameterUrl,
        int newVersion,
        CancellationToken ct)
    {
        // Step 1: Get current active version
        var current = await _indexRepo.GetActiveVersionAsync(searchParameterUrl, ct);

        // Step 2: Verify new version is fully built
        var newVersionMeta = await _indexRepo.GetVersionMetadataAsync(
            searchParameterUrl, newVersion, ct);

        if (newVersionMeta?.Status != SearchIndexStatus.Building)
        {
            throw new InvalidOperationException(
                $"Version {newVersion} is not in Building state (status: {newVersionMeta?.Status})");
        }

        // Step 3: ATOMIC SWITCH (transaction or optimistic concurrency)
        using var txn = await _indexRepo.BeginTransactionAsync(ct);
        try
        {
            // Update active version pointer
            await _indexRepo.UpdateActiveVersionAsync(
                searchParameterUrl,
                newVersion,
                ct);

            // Mark new version as Active
            await _indexRepo.UpdateVersionStatusAsync(
                searchParameterUrl,
                newVersion,
                SearchIndexStatus.Active,
                ct);

            // Mark old version as Retired (if exists)
            if (current.HasValue)
            {
                await _indexRepo.UpdateVersionStatusAsync(
                    searchParameterUrl,
                    current.Value,
                    SearchIndexStatus.Retired,
                    ct);
            }

            await txn.CommitAsync(ct);

            // Step 4: Emit events (after successful commit)
            await _eventStore.AppendAsync(new SearchIndexActivated
            {
                SearchParameterUrl = searchParameterUrl,
                TenantId = GetTenantId(),
                Version = newVersion,
                ReplacedVersion = current,
                Timestamp = DateTimeOffset.UtcNow
            }, ct);

            if (current.HasValue)
            {
                await _eventStore.AppendAsync(new SearchIndexRetired
                {
                    SearchParameterUrl = searchParameterUrl,
                    TenantId = GetTenantId(),
                    Version = current.Value,
                    Reason = $"Replaced by v{newVersion}",
                    Timestamp = DateTimeOffset.UtcNow
                }, ct);
            }
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }
}
```

### Query Execution (Version-Aware)

```csharp
public class VersionedSearchService
{
    public async ValueTask<SearchResult> SearchAsync(
        string resourceType,
        Dictionary<string, string> parameters,
        CancellationToken ct)
    {
        var results = new List<Resource>();

        foreach (var (code, value) in parameters)
        {
            // Resolve parameter code to URL
            var paramUrl = await _parameterRegistry.ResolveUrlAsync(resourceType, code, ct);
            if (paramUrl == null)
                continue; // Unknown parameter, skip

            // Get ACTIVE version for this parameter
            var activeVersion = await _indexRepo.GetActiveVersionAsync(paramUrl, ct);
            if (!activeVersion.HasValue)
            {
                // No active version = parameter exists but not indexed yet
                throw new SearchParameterNotReadyException(
                    $"Search parameter '{code}' exists but is not yet indexed. " +
                    $"Check /$reindex status for {paramUrl}");
            }

            // Query using ACTIVE version only
            var matchingResources = await _indexRepo.SearchIndexAsync(
                paramUrl,
                activeVersion.Value, // Specific version
                resourceType,
                value,
                ct);

            results.AddRange(matchingResources);
        }

        return new SearchResult { Resources = results };
    }
}
```

### Rollback Support

```csharp
/// <summary>
/// Rollback to previous version if new version has issues
/// </summary>
public async ValueTask RollbackVersionAsync(
    string searchParameterUrl,
    string reason,
    CancellationToken ct)
{
    // Get current active version
    var current = await _indexRepo.GetActiveVersionAsync(searchParameterUrl, ct);
    if (!current.HasValue)
        throw new InvalidOperationException("No active version to rollback from");

    // Get most recent retired version
    var retired = await _indexRepo.GetRetiredVersionsAsync(searchParameterUrl, ct);
    var rollbackTarget = retired.OrderByDescending(v => v.Version).FirstOrDefault();

    if (rollbackTarget == null)
        throw new InvalidOperationException("No retired version available for rollback");

    // Check if retired version still exists (not purged)
    if (rollbackTarget.Status == SearchIndexStatus.Purged)
        throw new InvalidOperationException(
            $"Version {rollbackTarget.Version} has been purged, cannot rollback");

    // Atomic switch back to old version
    using var txn = await _indexRepo.BeginTransactionAsync(ct);
    try
    {
        await _indexRepo.UpdateActiveVersionAsync(
            searchParameterUrl,
            rollbackTarget.Version,
            ct);

        await _indexRepo.UpdateVersionStatusAsync(
            searchParameterUrl,
            rollbackTarget.Version,
            SearchIndexStatus.Active,
            ct);

        await _indexRepo.UpdateVersionStatusAsync(
            searchParameterUrl,
            current.Value,
            SearchIndexStatus.Retired,
            ct);

        await txn.CommitAsync(ct);

        // Emit rollback event
        await _eventStore.AppendAsync(new SearchIndexRolledBack
        {
            SearchParameterUrl = searchParameterUrl,
            TenantId = GetTenantId(),
            FromVersion = current.Value,
            ToVersion = rollbackTarget.Version,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);
    }
    catch
    {
        await txn.RollbackAsync(ct);
        throw;
    }
}
```

### Background Purge Job

```csharp
/// <summary>
/// Purge retired versions after retention period (default 24h)
/// </summary>
public class SearchIndexPurgeJob : IBackgroundJob
{
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(24);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - _retentionPeriod;

        // Find retired versions older than retention period
        var toPurge = await _indexRepo.GetRetiredVersionsOlderThanAsync(cutoff, ct);

        foreach (var version in toPurge)
        {
            // Delete index data for this version
            await _indexRepo.DeleteIndexEntriesAsync(
                version.SearchParameterUrl,
                version.Version,
                ct);

            // Mark version as Purged
            await _indexRepo.UpdateVersionStatusAsync(
                version.SearchParameterUrl,
                version.Version,
                SearchIndexStatus.Purged,
                ct);

            // Emit event
            await _eventStore.AppendAsync(new SearchIndexPurged
            {
                SearchParameterUrl = version.SearchParameterUrl,
                TenantId = version.TenantId,
                Version = version.Version,
                Timestamp = DateTimeOffset.UtcNow
            }, ct);

            _logger.LogInformation(
                "Purged search index for {Url} v{Version} (retired {RetiredAt})",
                version.SearchParameterUrl, version.Version, version.RetiredAt);
        }
    }
}
```

### API Operations

**1. Create SearchParameter (Auto-starts v1 build)**:
```http
POST /SearchParameter
{
  "resourceType": "SearchParameter",
  "url": "http://example.com/sp/foo",
  "code": "foo",
  "expression": "Patient.extension('http://ex.com/foo').value"
}

Response: 201 Created
Location: /SearchParameter/foo
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "code": "informational",
    "diagnostics": "SearchParameter created. Index build started for version 1. " +
                   "Parameter will be searchable after build completes. " +
                   "Monitor progress: GET /$reindex?url=http://example.com/sp/foo"
  }]
}
```

**2. Monitor Build Progress**:
```http
GET /$reindex?url=http://example.com/sp/foo

Response: 200 OK
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "url",
    "valueUrl": "http://example.com/sp/foo"
  }, {
    "name": "version",
    "valueInteger": 1
  }, {
    "name": "status",
    "valueCode": "building"
  }, {
    "name": "progress",
    "part": [{
      "name": "resourcesProcessed",
      "valueInteger": 5000
    }, {
      "name": "totalResources",
      "valueInteger": 10000
    }, {
      "name": "percentComplete",
      "valueDecimal": 50.0
    }]
  }]
}
```

**3. Update SearchParameter (Triggers v2 build)**:
```http
PUT /SearchParameter/foo
{
  "resourceType": "SearchParameter",
  "url": "http://example.com/sp/foo",
  "expression": "Patient.extension('http://ex.com/foo').value | Patient.name" // Changed!
}

Response: 200 OK
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "diagnostics": "SearchParameter updated to version 2. Index build started. " +
                   "Searches will continue using version 1 until version 2 is ready."
  }]
}
```

**4. Rollback to Previous Version**:
```http
POST /$rollback
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "url",
    "valueUrl": "http://example.com/sp/foo"
  }, {
    "name": "reason",
    "valueString": "Version 2 returns incorrect results"
  }]
}

Response: 200 OK
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "diagnostics": "Rolled back to version 1. Version 2 is now retired."
  }]
}
```

**5. View Version History**:
```http
GET /SearchParameter/foo/$history

Response: 200 OK
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "version",
    "part": [{
      "name": "number",
      "valueInteger": 2
    }, {
      "name": "status",
      "valueCode": "retired"
    }, {
      "name": "expression",
      "valueString": "Patient.extension('http://ex.com/foo').value | Patient.name"
    }, {
      "name": "createdAt",
      "valueDateTime": "2025-12-19T10:30:00Z"
    }, {
      "name": "retiredAt",
      "valueDateTime": "2025-12-19T11:00:00Z"
    }]
  }, {
    "name": "version",
    "part": [{
      "name": "number",
      "valueInteger": 1
    }, {
      "name": "status",
      "valueCode": "active"
    }, {
      "name": "expression",
      "valueString": "Patient.extension('http://ex.com/foo').value"
    }, {
      "name": "activatedAt",
      "valueDateTime": "2025-12-19T10:00:00Z"
    }]
  }]
}
```

### Storage Size Analysis

**Concern**: Versioned indexes = multiple copies of data?

**Mitigation**:
```
Scenario: 100K Patient resources, SearchParameter with 3 versions

Unversioned (Proposals 1-3):
  SearchIndex: 100K entries × 100 bytes = 10 MB

Versioned (Proposal 4):
  Active version:   100K entries × 100 bytes = 10 MB
  Retired v2 (24h): 100K entries × 100 bytes = 10 MB
  Building v4:      50K entries × 100 bytes = 5 MB (partial)
  Total: 25 MB

Overhead: 2.5x during transition periods, 1x steady state (old versions purged)
```

**Optimization**: Incremental indexing (only index changed resources for new version)
```csharp
// When building v2, only re-index resources modified since v1 was created
var changedResources = await _resourceRepo.SearchAsync(
    resourceType: "Patient",
    parameters: new() {
        ["_lastUpdated"] = $"gt{version1.CreatedAt:o}"
    },
    ct);

// Copy unchanged entries from v1 to v2
await _indexRepo.CopyUnchangedEntriesAsync(
    fromVersion: 1,
    toVersion: 2,
    excludeResourceIds: changedResources.Select(r => r.Id),
    ct);
```

### Tradeoffs

| Pros | Cons |
|------|------|
| **Zero-downtime updates**: Searches always use complete index | **Storage overhead**: 2-2.5x during transition periods (24h window) |
| **No stale results**: Queries never see partial/inconsistent data | **Complexity**: Versioned schema + activation logic increases mental model |
| **Rollback safety**: Keep old version for 24h, instant rollback | **Migration cost**: Existing single-version indexes must be versioned |
| **Auditability**: Full history of parameter changes + activations | **Performance**: Purge job adds background processing overhead |
| **Clear semantics**: "Building" vs "Active" eliminates confusion | **Transaction requirements**: Atomic activation needs DB transaction support |

### Alignment

- [x] Follows layer rules (API -> Application -> Domain -> DataLayer)
- [x] F5 Developer Experience (no manual tracking, queries fail cleanly if not ready)
- [x] FHIR spec compliance (SearchParameter resources)
- [x] Multi-tenancy (TenantId in events and active version tracking)
- [x] Consistent with existing patterns (versioning similar to package versioning)
- [x] **Solves core UX pain point**: No more "parameter exists but can't use it yet"

### Implementation Estimate

- **Event types + versioning models**: 2 days
- **Versioned index storage schema**: 3 days
- **Atomic activation service**: 3 days
- **Version-aware query service**: 2 days
- **Rollback mechanism**: 2 days
- **Background purge job**: 1 day
- **API endpoints (history, rollback)**: 2 days
- **Incremental indexing optimization**: 3 days
- **Tests (unit + integration)**: 4 days
- **Total**: ~22 days (4.4 weeks)

### Comparison to Proposals 1-3

| Aspect | Proposal 1-3 | Proposal 4 (Versioned) |
|--------|-------------|------------------------|
| **Searchable immediately?** | No (must wait for reindex) | No (but clear "building" status) |
| **Wrong results during reindex?** | Yes (or parameter disabled) | **No** (old version still active) |
| **Rollback support** | Manual (restore from backup) | **Instant** (atomic switch to retired version) |
| **Storage overhead** | 1x | 2-2.5x during transitions, 1x steady state |
| **Operational complexity** | Medium | High (version management) |
| **Audit trail** | Logs only (Proposals 1-2) / Events (Proposal 3) | **Full event history** |

## Alternative Approaches (Not Detailed)

Beyond the three proposals above, other viable approaches include:

1. **Pure In-Memory (Ephemeral)**: Parameters defined in `appsettings.json`, loaded at startup, no persistence. Fast but parameters lost on restart. Good for development, poor for production.

2. **Hybrid DB + Cache with CDC**: Database as source of truth, in-memory cache invalidated via Change Data Capture (CDC) streams (SQL Server Change Tracking, Postgres LISTEN/NOTIFY). Complex infrastructure dependency.

3. **FHIR-Native (SearchParameter as Regular Resource)**: Treat SearchParameter like any other FHIR resource - no special handling. Simple but no lifecycle management, no reindex coordination, parameters searchable before indexed (incorrect results).

## Recommendation

**Proposal 4 (Versioned Search Indexes with Event History)** is the only viable approach for production deployments.

**Rationale**:
- **Solves core UX pain**: No stale/incomplete results during reindex
- **Operational safety**: Instant rollback if new parameter version has issues
- **Audit compliance**: Full event history of parameter changes
- **Clear semantics**: "Building" vs "Active" eliminates confusion
- **Distribution reality**: Static files (Proposal 2) don't work for SaaS/container/multi-tenant deployments

### Why Proposal 2 (Config-as-Code) Doesn't Work for Customers

**File-based distribution fails because**:
- **SaaS deployments**: Customers don't have file system access
- **Container/Docker**: Ephemeral file systems, can't persist config files
- **Multi-tenant**: Can't give each tenant file system access for security
- **Updates**: No way to push updated search parameters without redeployment
- **GitOps**: Only works for Ignixa developers, not end customers

**Reality**: Only Ignixa.Search itself should ship with embedded static files (base R4 parameters). Everything else needs runtime loading.

### Customer Distribution Channels (Proposal 4)

Customers have **three ways** to load search parameters into Ignixa:

#### 1. Package Installation (IG SearchParameters)

**Use Case**: Load SearchParameters from FHIR Implementation Guides (e.g., US Core, UK Core, AU Base)

**Workflow**:
```http
POST /$install-package
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "packageId",
    "valueString": "hl7.fhir.us.core"
  }, {
    "name": "version",
    "valueString": "6.1.0"
  }]
}

Response: 202 Accepted
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "diagnostics": "Package installed. Found 47 SearchParameter resources. " +
                   "Index builds started. Parameters will be searchable after builds complete. " +
                   "Monitor: GET /$package-status?id=hl7.fhir.us.core"
  }]
}
```

**Backend Process**:
```csharp
public class PackageInstallationHandler
{
    public async ValueTask InstallPackageAsync(
        string packageId,
        string version,
        CancellationToken ct)
    {
        // Step 1: Download from packages.fhir.org
        var packageBytes = await _npmClient.DownloadPackageAsync(packageId, version, ct);
        var extractPath = await _packageExtractor.ExtractAsync(packageBytes, ct);

        // Step 2: Find all SearchParameter resources
        var searchParams = Directory.GetFiles(extractPath, "SearchParameter-*.json")
            .Select(f => _fhirParser.Parse<SearchParameter>(File.ReadAllText(f)))
            .ToList();

        // Step 3: Create versioned parameters (v1 for each)
        foreach (var sp in searchParams)
        {
            var paramInfo = new SearchParameterVersion
            {
                Url = sp.Url,
                Version = 1,
                Code = sp.Code,
                Expression = sp.Expression,
                Base = sp.Base.Select(b => b.Value.ToString()).ToArray(),
                Source = SearchParameterSource.ImplementationGuide,
                SourcePackage = $"{packageId}#{version}",
                Status = SearchIndexStatus.Building
            };

            // Emit event
            await _eventStore.AppendAsync(new SearchParameterCreated
            {
                SearchParameterUrl = paramInfo.Url,
                TenantId = GetTenantId(),
                Expression = paramInfo.Expression,
                Version = 1,
                Source = SearchParameterSource.ImplementationGuide,
                SourceMetadata = new { packageId, version },
                Timestamp = DateTimeOffset.UtcNow
            }, ct);

            // Queue index build
            await _indexBuilder.QueueBuildAsync(paramInfo, ct);
        }
    }
}
```

**Package Status Tracking**:
```http
GET /$package-status?id=hl7.fhir.us.core

Response:
{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "packageId",
    "valueString": "hl7.fhir.us.core#6.1.0"
  }, {
    "name": "searchParametersTotal",
    "valueInteger": 47
  }, {
    "name": "searchParametersActive",
    "valueInteger": 35
  }, {
    "name": "searchParametersBuilding",
    "valueInteger": 12
  }, {
    "name": "overallStatus",
    "valueCode": "in-progress"
  }]
}
```

#### 2. POST /SearchParameter (Custom Parameters)

**Use Case**: Customer-specific search parameters for extensions or custom workflows

**Workflow**:
```http
POST /SearchParameter
{
  "resourceType": "SearchParameter",
  "url": "http://acme.org/fhir/SearchParameter/patient-loyalty-tier",
  "name": "PatientLoyaltyTier",
  "code": "loyalty-tier",
  "base": ["Patient"],
  "type": "token",
  "expression": "Patient.extension.where(url='http://acme.org/fhir/StructureDefinition/loyalty-tier').value.code",
  "status": "active"
}

Response: 201 Created
Location: /SearchParameter/patient-loyalty-tier
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "diagnostics": "SearchParameter created (version 1). Index build started. " +
                   "Parameter will be searchable after build completes. " +
                   "Monitor: GET /$reindex?url=http://acme.org/fhir/SearchParameter/patient-loyalty-tier"
  }]
}
```

**Backend Process**:
```csharp
public class CreateSearchParameterHandler : IRequestHandler<CreateResourceRequest<SearchParameter>>
{
    public async ValueTask<CreateResourceResponse> HandleAsync(
        CreateResourceRequest<SearchParameter> request,
        CancellationToken ct)
    {
        var sp = request.Resource;

        // Step 1: Validate FhirPath expression compiles
        await _fhirPathValidator.ValidateAsync(sp.Expression, ct);

        // Step 2: Check conflicts
        var conflict = await _conflictResolver.CheckAsync(sp.Url, sp.Code, sp.Base, ct);
        if (conflict.HasConflict)
        {
            throw new InvalidOperationException(conflict.Message);
        }

        // Step 3: Store SearchParameter as regular FHIR resource
        var stored = await _resourceRepository.CreateAsync(sp, ct);

        // Step 4: Emit event + queue index build
        await _eventStore.AppendAsync(new SearchParameterCreated
        {
            SearchParameterUrl = sp.Url,
            TenantId = GetTenantId(),
            Expression = sp.Expression,
            Version = 1,
            Source = SearchParameterSource.Custom,
            UserId = request.UserId,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        var paramInfo = new SearchParameterVersion
        {
            Url = sp.Url,
            Version = 1,
            Code = sp.Code,
            Expression = sp.Expression,
            Base = sp.Base.Select(b => b.Value.ToString()).ToArray(),
            Source = SearchParameterSource.Custom,
            Status = SearchIndexStatus.Building
        };

        await _indexBuilder.QueueBuildAsync(paramInfo, ct);

        return new CreateResourceResponse
        {
            Resource = stored,
            OperationOutcome = new OperationOutcome
            {
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new()
                    {
                        Severity = OperationOutcome.IssueSeverity.Information,
                        Code = OperationOutcome.IssueType.Informational,
                        Diagnostics = $"SearchParameter created (version 1). Index build started for {sp.Base.Length} resource types."
                    }
                }
            }
        };
    }
}
```

#### 3. POST Custom Package Bundle (Local Packages)

**Use Case**: Customer has a private/local FHIR package not published to packages.fhir.org

**Workflow**:
```http
POST /$upload-package
Content-Type: application/gzip

<binary package.tgz bytes>

Response: 202 Accepted
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "information",
    "diagnostics": "Custom package uploaded. Found 12 SearchParameter resources. " +
                   "Index builds started. Monitor: GET /$package-status?id=custom-{uploadId}"
  }]
}
```

**Alternative: Bundle Upload**:
```http
POST /SearchParameter/$batch-create
{
  "resourceType": "Bundle",
  "type": "transaction",
  "entry": [
    {
      "request": { "method": "POST", "url": "SearchParameter" },
      "resource": {
        "resourceType": "SearchParameter",
        "url": "http://acme.org/sp/foo",
        "code": "foo",
        ...
      }
    },
    {
      "request": { "method": "POST", "url": "SearchParameter" },
      "resource": {
        "resourceType": "SearchParameter",
        "url": "http://acme.org/sp/bar",
        "code": "bar",
        ...
      }
    }
  ]
}

Response: 200 OK
{
  "resourceType": "Bundle",
  "type": "transaction-response",
  "entry": [
    {
      "response": { "status": "201 Created", "location": "SearchParameter/foo" },
      "outcome": { /* OperationOutcome: index building */ }
    },
    {
      "response": { "status": "201 Created", "location": "SearchParameter/bar" },
      "outcome": { /* OperationOutcome: index building */ }
    }
  ]
}
```

**Backend Process** (handles both package upload and bundle):
```csharp
public class CustomPackageUploadHandler
{
    public async ValueTask HandlePackageUploadAsync(
        Stream packageStream,
        CancellationToken ct)
    {
        // Extract package to temp directory
        var extractPath = await _packageExtractor.ExtractTgzAsync(packageStream, ct);

        // Load package.json to get metadata
        var packageJson = await File.ReadAllTextAsync(
            Path.Combine(extractPath, "package.json"), ct);
        var packageMeta = JsonSerializer.Deserialize<PackageMetadata>(packageJson);

        // Find SearchParameter resources
        var searchParams = Directory.GetFiles(extractPath, "SearchParameter-*.json")
            .Select(f => _fhirParser.Parse<SearchParameter>(File.ReadAllText(f)))
            .ToList();

        // Process each (same as POST /SearchParameter)
        foreach (var sp in searchParams)
        {
            await CreateSearchParameterAsync(sp, SearchParameterSource.CustomPackage, ct);
        }

        // Store package metadata for uninstall
        await _packageRegistry.RegisterAsync(new PackageRegistration
        {
            PackageId = packageMeta.Name,
            Version = packageMeta.Version,
            IsCustom = true,
            UploadedAt = DateTimeOffset.UtcNow,
            SearchParameterUrls = searchParams.Select(sp => sp.Url).ToList()
        }, ct);
    }
}
```

### Source Priority and Conflict Resolution

When parameters from different sources have the same code:

```
Priority Order (highest to lowest):
1. Base Specification (embedded in Ignixa.Search)
2. Implementation Guide packages
3. Custom posted parameters
4. Custom package uploads
```

**Conflict Behavior**:
```csharp
// Example: US Core defines Patient.race, conflicts with base R4 Patient.race
var baseParam = await _repo.GetByCodeAsync("Patient", "race", ct);
// baseParam.Source = SearchParameterSource.BaseSpecification

var usCoreParam = /* from hl7.fhir.us.core package */;
// usCoreParam.Source = SearchParameterSource.ImplementationGuide

// Conflict check
var result = _conflictResolver.Resolve(baseParam, usCoreParam);
// Result: REJECT (base has higher priority)

// Response to package install
{
  "issue": [{
    "severity": "warning",
    "diagnostics": "Skipped SearchParameter http://hl7.org/fhir/us/core/.../patient-race: " +
                   "conflicts with base specification parameter. " +
                   "Base parameter will remain active."
  }]
}
```

**DerivedFrom Override** (R5+ pattern):
```json
// US Core can override base by using derivedFrom
{
  "resourceType": "SearchParameter",
  "url": "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-race",
  "derivedFrom": "http://hl7.org/fhir/SearchParameter/Patient-race",
  "code": "race",
  "expression": "Patient.extension.where(...).value.code", // More specific
  "constraint": "Patient.meta.profile.where($this = 'http://.../us-core-patient').exists()"
}

// Conflict resolution: DerivedFrom creates parent-child relationship
// Both parameters coexist, constraint determines which applies per resource
```

### Storage Lifecycle Strategy (Addresses Indefinite Growth)

**Retention Window Configuration**:
```csharp
public class SearchIndexRetentionSettings
{
    // How long to keep retired versions for rollback (default 24h)
    public TimeSpan RetiredVersionRetention { get; set; } = TimeSpan.FromHours(24);

    // Max number of retired versions to keep per parameter (default 2)
    // Oldest beyond this limit are purged immediately
    public int MaxRetiredVersions { get; set; } = 2;

    // Purge job frequency (default every 6 hours)
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(6);
}
```

**Elegant Drop Workflow**:
```
1. Version v3 activated (v2 becomes Retired)
   - v2 marked with RetiredAt = Now
   - v1 still Retired (from previous transition)

2. Purge job runs every 6 hours:
   - Check: v1.RetiredAt + 24h < Now? → YES → Purge v1
   - Check: v2.RetiredAt + 24h < Now? → NO → Keep v2

3. Next activation (v4):
   - v3 becomes Retired
   - v2 still within 24h window

4. Later purge job:
   - Check: v2.RetiredAt + 24h < Now? → YES → Purge v2
   - Check MaxRetiredVersions: v3 is 1 of max 2 → Keep v3
```

**Storage Footprint Over Time** (100K resources, 100 bytes/entry):
```
Day 0:  v1 Active (10 MB)
Day 1:  v2 Active + v1 Retired (20 MB) ← Peak during rollback window
Day 2:  v2 Active (10 MB) ← v1 purged, back to baseline
Day 3:  v3 Active + v2 Retired (20 MB)
Day 4:  v3 Active (10 MB) ← v2 purged

Steady state: 1x storage (active version only)
Peak: 2x storage (during 24h rollback window)
Max: 3x storage (if new version built while old still in retention)
```

### Maintaining Search Performance with Versioned Indexes

**Index Partitioning Strategy**:
```sql
-- Index optimized for version-specific queries
CREATE INDEX IX_SearchIndex_VersionSpecific
    ON SearchIndexEntries(SearchParameterUrl, ParameterVersion, ResourceType, StringValue)
    WHERE ParameterVersion IN (
        SELECT ActiveVersion FROM SearchParameterActiveVersions
    );

-- PostgreSQL partial index: Only indexes active versions
-- Reduces index size by excluding retired/building versions
```

**Query Performance Guarantees**:
| Operation | Target | Strategy |
|-----------|--------|----------|
| **Search using active param** | <50ms (same as unversioned) | Partial index on active version only |
| **Version activation** | <100ms | Single UPDATE to ActiveVersion pointer |
| **Rollback** | <100ms | Single UPDATE to ActiveVersion pointer |
| **Purge retired version** | <5s per 100K entries | Background job, batched deletes |

**Monitoring & Alerts**:
```csharp
public interface ISearchIndexMetrics
{
    // Alert if total index size exceeds threshold
    void RecordTotalIndexSize(long bytes);

    // Alert if retired versions accumulate (purge job failing?)
    void RecordRetiredVersionCount(int count);

    // Alert if building version takes too long
    void RecordBuildDuration(TimeSpan duration);

    // Track storage efficiency
    void RecordStorageEfficiency(
        long activeVersionSize,
        long retiredVersionSize,
        int retiredVersionCount);
}

// Example alert thresholds
// - Total index size > 50 GB → investigate purge job
// - Retired version count > MaxRetiredVersions * 2 → purge job stuck
// - Build duration > 1 hour → consider parallel indexing
```

**Incremental Indexing (Minimizes v2 Build Time)**:
```csharp
// Only re-index resources modified since v1 was created
// Copy unchanged resources from v1 to v2
public async ValueTask BuildIncrementalVersionAsync(
    string paramUrl,
    int fromVersion,
    int toVersion,
    CancellationToken ct)
{
    var v1Meta = await _indexRepo.GetVersionMetadataAsync(paramUrl, fromVersion, ct);

    // Step 1: Copy unchanged entries (fast, no FhirPath evaluation)
    await _indexRepo.ExecuteSqlAsync($@"
        INSERT INTO SearchIndexEntries (
            SearchParameterUrl, ParameterVersion, ResourceType, ResourceId, ValueType,
            StringValue, TokenSystem, TokenCode, NumberValue, DateValue,
            ReferenceResourceType, ReferenceResourceId, CreatedAt
        )
        SELECT
            SearchParameterUrl, {toVersion}, ResourceType, ResourceId, ValueType,
            StringValue, TokenSystem, TokenCode, NumberValue, DateValue,
            ReferenceResourceType, ReferenceResourceId, GETUTCDATE()
        FROM SearchIndexEntries
        WHERE SearchParameterUrl = @paramUrl
          AND ParameterVersion = {fromVersion}
          AND ResourceId NOT IN (
              -- Exclude resources modified since v1 was created
              SELECT Id FROM Resources
              WHERE ResourceType IN @resourceTypes
                AND LastModified > @v1CreatedAt
          )
    ", new { paramUrl, resourceTypes = v1Meta.Base, v1CreatedAt = v1Meta.CreatedAt });

    // Step 2: Index only changed resources (slow, requires FhirPath)
    var changedResources = await _resourceRepo.SearchAsync(
        resourceTypes: v1Meta.Base,
        parameters: new() { ["_lastUpdated"] = $"gt{v1Meta.CreatedAt:o}" },
        ct);

    foreach (var resource in changedResources)
    {
        var values = await _indexExtractor.ExtractValuesAsync(resource, v2Expression, ct);
        await _indexRepo.InsertEntriesAsync(paramUrl, toVersion, resource.Id, values, ct);
    }
}

// Example: 100K resources, 5K changed since v1
// - v1 full build: 100K × 10ms FhirPath = 1000s (16 min)
// - v2 incremental: 95K copy (10s) + 5K index (50s) = 60s (1 min)
// Speedup: 16x faster
```

## Next Steps

1. **Spike**: Prototype Proposal 4 core mechanisms (3-4 days)
   - Versioned index storage schema (SQL/file/Cosmos adapters)
   - Atomic activation service (version pointer updates)
   - Event store implementation (file-based for simplicity)
   - Package installation handler (from packages.fhir.org)

2. **ADR**: Document Proposal 4 decision with:
   - **Customer distribution channels**: Package install, POST /SearchParameter, upload custom package
   - **Storage lifecycle**: Retention windows (24h default), purge job, max versions (2)
   - **Performance targets**: <50ms search, <100ms activation/rollback, 16x faster incremental builds
   - **Migration from existing custom-parameters.md approach**: How to version existing unversioned indexes

3. **Implementation**: Full build with tests (~22 days / 4.4 weeks)
   - **Week 1**: Versioned storage schema + event store
   - **Week 2**: Package installation + POST /SearchParameter + conflict resolution
   - **Week 3**: Index builder + activation service + rollback mechanism
   - **Week 4**: Incremental indexing + purge job + monitoring
   - **Throughout**: Tests (unit + integration + E2E)

4. **Monitoring & Alerts**: Metrics for operational health
   - Index size tracking (alert if >50 GB total)
   - Retired version accumulation (alert if >MaxRetiredVersions * 2)
   - Build duration (alert if >1 hour for 100K resources)
   - Purge job health (alert if failing or not running)

5. **Documentation**: Customer-facing guides
   - **Installing IG packages**: `POST /$install-package` workflow
   - **Creating custom parameters**: `POST /SearchParameter` + monitoring build progress
   - **Uploading private packages**: `POST /$upload-package` for local packages
   - **Rollback procedures**: When to rollback, how to monitor version history
   - **Storage planning**: Sizing guidelines for index storage (2-2.5x during transitions)

6. **Future Enhancements** (post-MVP):
   - Parallel index building (across resource types)
   - Smart incremental builds (detect expression changes, only re-index affected paths)
   - Cross-version queries (search across multiple parameter versions for debugging)
   - Auto-rollback on error threshold (e.g., if v2 causes >10% search failures)

## Sources

- [HAPI FHIR Search Documentation](https://hapifhir.io/hapi-fhir/docs/server_jpa/search.html)
- [Smile CDR Custom Search Parameters](https://smilecdr.com/docs/fhir_standard/fhir_search_custom_search_parameters.html)
- [LinuxForHealth FHIR Search Configuration](https://linuxforhealth.github.io/FHIR/guides/FHIRSearchConfiguration/)
- [LinuxForHealth FHIR GitHub](https://github.com/LinuxForHealth/FHIR)
- Microsoft FHIR Server (src-old codebase analysis)
