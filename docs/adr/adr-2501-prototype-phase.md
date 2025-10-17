# ADR 2501: Prototype Phase - Vertical Slice with PUT and GET

## Status

Implemented (with architectural variation - see Project Organization below)

## Context

The Prototype Phase establishes the foundational architecture for FHIR Server v2 by implementing a complete vertical slice from HTTP API to persistent storage. This phase prioritizes the F5 developer experience principle: a developer should be able to press F5 and immediately have a working FHIR server with zero external dependencies.

### Goals

1. **Validate Architecture Decisions**: Prove that vertical slice architecture works end-to-end
2. **Establish Development Patterns**: Set standards for all future phases
3. **Enable Early Feedback**: Working software in Week 1 for stakeholder validation
4. **Minimize Scope**: Two operations only (PUT and GET) to focus on architecture quality

### Intentional Simplifications (Added in Later Phases)

**Prototype Phase** intentionally uses simplified approaches to focus on architecture validation:

1. **Storage Architecture**: Simple coupled approach where search indices and raw resource data are stored together in `FileBasedFhirRepository`.
   - Investigation `decoupled-resource-index-storage.md` documents the pattern for future decoupling
   - **Phase 8** (SQL Storage): Separate Resource/RawResource tables with URN-based storage locations
   - **Phase 9** (Cosmos Storage): Decoupled resource/index containers
   - **Phase 10+** (Hybrid Storage): Tiered storage strategies (hot SQL, warm/cold Blob)

2. **Capability Statement**: Static JSON file served from `/metadata` endpoint
   - Investigation `dynamic-capability-statement-generation.md` documents segmented capability pattern
   - **Phase 1.2** (Search): Add CapabilityStatementService with basic segments
   - **Phase 3** (Validation): Add ProfileCapabilitySegment with dynamic refresh
   - **Phase 6** (Multi-Tenant): Add tenant-aware capability caching

3. **Authorization**: No authorization in Prototype (open access)
   - Investigation `rbac-authorization-with-capability-enforcement.md` documents layered authorization
   - **Phase 1.2** (Search): Add FhirAuthorizationMiddleware with AuthenticationHandler
   - **Phase 3** (Validation): Add CapabilityEnforcementHandler
   - **Phase 6** (Multi-Tenant): Add TenantIsolationHandler
   - **Phase 10** (SMART): Add SmartScopeAuthorizationHandler with patient compartment filtering

### Key Design Decisions

Based on investigations in `phase1-file-based-storage-with-search.md` and `storage-architecture-v2.md`:

**0. Project Organization and Naming**

This is a **side project** called **"Ignixa"**, not "Microsoft.Health". Project naming follows this pattern:
- `Ignixa.*` namespace (e.g., `Ignixa.Api`, `Ignixa.Domain`, `Ignixa.Application`)
- Layered architecture with separate projects for each architectural layer

**Implemented Architecture: Layered Projects** (organized by architectural layer):

```
All.sln (9 projects)
├── 1. Ignixa.Domain              # Domain models and abstractions (no dependencies)
│   ├── Abstractions/
│   │   └── IFhirRepository.cs
│   └── Models/
│       ├── ResourceKey.cs
│       ├── ResourceWrapper.cs
│       ├── ResourceRequest.cs
│       └── TransactionId.cs
│
├── 2. Ignixa.Application         # Medino handlers and business logic (→ Domain)
│   └── Features/
│       └── Patient/
│           ├── CreateOrUpdatePatientCommand.cs
│           ├── CreateOrUpdatePatientHandler.cs
│           ├── GetPatientQuery.cs
│           └── GetPatientHandler.cs
│
├── 3. Ignixa.DataLayer.*         # Data storage implementations (→ Domain)
│   ├── Ignixa.DataLayer.FileSystem
│   │   └── FileSystem/
│   │       └── FileBasedFhirRepository.cs
│   └── Ignixa.DataLayer.InMemoryIndex
│       └── InMemoryIndex/
│           ├── IResourceLocationIndex.cs
│           └── InMemoryResourceLocationIndex.cs
│
├── 4. Ignixa.Api                 # ASP.NET Core API (→ all layers)
│   ├── Features/
│   │   ├── Patient/
│   │   │   └── Api/
│   │   │       └── PatientController.cs
│   │   └── Metadata/
│   │       └── Api/
│   │           └── MetadataController.cs
│   ├── Services/
│   │   └── IndexLoaderService.cs
│   ├── Middleware/
│   │   └── FhirExceptionMiddleware.cs
│   ├── Infrastructure/
│   │   └── AutofacMediatorServiceProvider.cs
│   └── Program.cs
│
└── Supporting Libraries
    ├── Ignixa.Extensions         # FHIR extensions and utilities
    ├── Ignixa.Search             # Search functionality
    └── Ignixa.SourceNodeSerialization # Serialization utilities
```

**Architecture Principles**:
1. **Domain** has no dependencies (pure models and abstractions)
2. **Application** depends only on Domain (business logic, CQRS handlers)
3. **DataLayer** depends only on Domain (storage implementations - file, SQL, Cosmos)
4. **API** depends on all layers (HTTP concerns, controllers, middleware)

**Rationale**:
- **Clean Architecture**: Clear separation between domain logic, application logic, and infrastructure
- **Multi-DataLayer Support**: Easy to add new storage implementations (Ignixa.DataLayer.SqlServer, Ignixa.DataLayer.CosmosDB)
- **Feature Folders Within Layers**: Each layer uses feature folders (e.g., Application/Features/Patient/)
- **Testability**: Each layer can be tested independently
- **Scalability**: Supports Isolation Mode (single data layer) and Distributed Mode (multiple data layers)

**Note**: This differs from the original ADR proposal (single project with feature folders), but provides better separation of concerns and aligns with Clean Architecture principles. The vertical slice philosophy is maintained within each layer via feature folders.

**1. File-Based Storage with Metadata Sidecar**

Instead of re-extracting search parameters on every startup, we write `.metadata.ndjson` files alongside resource `.ndjson` files:

```
/data/Patient/2025/01/15/
  tx-1234567890.ndjson           # Bundle + Resource data
  tx-1234567890.metadata.ndjson  # Pre-extracted search indices + request metadata
```

**Benefits**:
- 10x faster startup (no FHIRPath evaluation)
- Consistent indices (same values at write and read)
- Request metadata preserved for audit/replay

**2. Request Metadata Storage**

Based on legacy data layer design (`ResourceWrapper.Request`), metadata includes:
- `RequestMethod` (POST, PUT, DELETE, etc.)
- `RequestUrl` (Patient, Patient/123, etc.)
- `IfMatch`, `IfNoneExist`, `IfModifiedSince` (conditional request headers)

**Use cases**: Audit trails, transaction replay, debugging

**3. Memory-Efficient Patterns from Day One**

From `memory-efficient-fhir-patterns.md`:
- `RecyclableMemoryStream` for all I/O (70% GC pressure reduction)
- `Span<T>` and `Memory<T>` for zero-allocation parsing
- `ArrayPool<T>` for temporary collections

## Decision

Implement a complete vertical slice with **only** `PUT /Patient/{id}` and `GET /Patient/{id}` operations, integrating all architectural layers:

### Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                 Prototype Phase Architecture (Implemented)       │
├─────────────────────────────────────────────────────────────────┤
│  1. API Layer (ASP.NET Core Controllers)                        │
│     Ignixa.Api                                                   │
│     - PatientController: PUT /Patient/{id}, GET /Patient/{id}   │
│     - MetadataController: GET /metadata                         │
│     - IndexLoaderService: IHostedService for startup loading    │
│     - FhirExceptionMiddleware: FHIR error responses             │
│                                                                  │
│  2. Application Layer (Medino CQRS Handlers)                    │
│     Ignixa.Application                                           │
│     - CreateOrUpdatePatientCommand / Handler                    │
│     - GetPatientQuery / Handler                                 │
│                                                                  │
│  3. Domain Layer (Models & Abstractions)                        │
│     Ignixa.Domain                                                │
│     - IFhirRepository interface                                 │
│     - ResourceWrapper, ResourceKey, ResourceRequest             │
│     - TransactionId                                             │
│                                                                  │
│  4. Data Layer (Storage Implementations)                        │
│     Ignixa.DataLayer.FileSystem                                  │
│     - FileBasedFhirRepository: NDJSON storage with metadata     │
│     Ignixa.DataLayer.InMemoryIndex                               │
│     - InMemoryResourceLocationIndex: Resource location tracking │
│                                                                  │
│  5. Supporting Libraries                                        │
│     - Ignixa.Extensions: FHIR utilities                         │
│     - Ignixa.Search: Search parameters                          │
│     - Ignixa.SourceNodeSerialization: Custom serialization      │
│                                                                  │
│  6. Tests (xUnit + NSubstitute)                                 │
│     Ignixa.Api.Tests                                             │
│     - Unit tests for all layers                                 │
│     - Integration tests for PUT/GET                             │
└─────────────────────────────────────────────────────────────────┘
```

## Implementation Plan

### Week 1: Prototype Phase (~20 Claude Code hours)

#### 1. Project Structure Setup (2 hours)

**Layered Project Organization** (separation by architectural layer):

```
All.sln
src/
  Ignixa.Domain/                     # Core domain (no dependencies)
    Abstractions/
      IFhirRepository.cs
    Models/
      ResourceWrapper.cs
      ResourceKey.cs
      ResourceRequest.cs
      TransactionId.cs

  Ignixa.Application/                # Business logic (→ Domain)
    Features/
      Patient/
        CreateOrUpdatePatientCommand.cs
        CreateOrUpdatePatientHandler.cs
        GetPatientQuery.cs
        GetPatientHandler.cs

  Ignixa.DataLayer.FileSystem/       # File storage (→ Domain)
    FileSystem/
      FileBasedFhirRepository.cs

  Ignixa.DataLayer.InMemoryIndex/    # Index tracking (→ Domain)
    InMemoryIndex/
      IResourceLocationIndex.cs
      InMemoryResourceLocationIndex.cs

  Ignixa.Api/                        # HTTP API (→ all layers)
    Features/
      Patient/
        Api/
          PatientController.cs
      Metadata/
        Api/
          MetadataController.cs
    Services/
      IndexLoaderService.cs
    Middleware/
      FhirExceptionMiddleware.cs
    Infrastructure/
      AutofacMediatorServiceProvider.cs
    Program.cs

  Ignixa.Extensions/                 # FHIR utilities
  Ignixa.Search/                     # Search parameters
  Ignixa.SourceNodeSerialization/    # Custom serialization

test/
  Ignixa.Api.Tests/
    Features/
      Patient/
        PatientControllerTests.cs
        CreateOrUpdatePatientHandlerTests.cs
        GetPatientHandlerTests.cs
    DataLayer/
      FileBasedFhirRepositoryTests.cs
```

**Key Characteristics**:
- ✅ Separate projects for Domain, Application, DataLayer, API
- ✅ Clear dependency flow: API → Application → Domain ← DataLayer
- ✅ Feature folders within each layer (e.g., Application/Features/Patient/)
- ✅ Multiple DataLayer projects for different storage backends
- ✅ Easy to test each layer independently

#### 2. Core Abstractions (4 hours)

```csharp
// Ignixa.Domain/Abstractions/IFhirRepository.cs
namespace Ignixa.Domain.Abstractions;

public interface IFhirRepository
{
    Task<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken ct = default);
    Task<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, ResourceRequest request, CancellationToken ct = default);
    IEnumerable<string> GetAllMetadataFiles();
}

// Ignixa.Domain/Models/ResourceWrapper.cs
namespace Ignixa.Domain.Models;

public class ResourceWrapper
{
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public int VersionId { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public string RawJson { get; set; } = string.Empty;  // Stored for prototype simplicity
    public bool IsDeleted { get; set; }
}

public record ResourceKey(
    string ResourceType,
    string Id,
    int? VersionId = null);

public class ResourceRequest
{
    public string Method { get; set; } = string.Empty;        // PUT, POST, etc.
    public string Url { get; set; } = string.Empty;           // Patient/123
    public string? IfMatch { get; set; }
    public string? IfNoneExist { get; set; }
    public string? IfModifiedSince { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

public class TransactionId
{
    public string Value { get; }

    private TransactionId(string value) => Value = value;

    public static TransactionId Generate() =>
        new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

    public override string ToString() => Value;
}
```

#### 3. File-Based Repository (6 hours)

```csharp
public class FileBasedFhirRepository : IFhirRepository
{
    private readonly string _basePath;
    private readonly InMemoryIndex _searchIndex;
    private readonly ISearchIndexer _searchIndexer;

    public async ValueTask<ResourceWrapper?> GetAsync(
        ResourceKey key,
        CancellationToken ct = default)
    {
        // 1. Lookup in InMemoryIndex to find file location
        var location = _searchIndex.GetResourceLocation(key);
        if (location == null) return null;

        // 2. Read from NDJSON file
        using var fileStream = new FileStream(location.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var memory = RecyclableMemoryStreamManager.Shared.GetStream("ReadResource");

        await fileStream.CopyToAsync(memory, ct);

        // 3. Parse and return (skip line 1 Bundle, find resource on line 2+)
        return await ParseResourceFromNdjsonAsync(memory, key, ct);
    }

    public async ValueTask<ResourceKey> CreateOrUpdateAsync(
        ResourceWrapper resource,
        CancellationToken ct = default)
    {
        var transactionId = TransactionId.Generate();
        var timestamp = DateTimeOffset.UtcNow;

        // 1. Extract search parameters BEFORE writing
        var searchEntries = _searchIndexer.Extract(resource.Resource);

        // 2. Write resource data file (.ndjson)
        await WriteResourceFileAsync(transactionId, resource, timestamp, ct);

        // 3. Write metadata sidecar file (.metadata.ndjson)
        await WriteMetadataFileAsync(transactionId, resource, searchEntries, timestamp, ct);

        // 4. Update in-memory index
        var location = new FileLocation(GetResourceFilePath(resource, timestamp), transactionId, timestamp);
        _searchIndex.AddResourceLocation(
            new ResourceKey(resource.ResourceType, resource.ResourceId, resource.VersionId),
            location);

        return new ResourceKey(resource.ResourceType, resource.ResourceId, resource.VersionId);
    }

    private async Task WriteResourceFileAsync(
        TransactionId transactionId,
        ResourceWrapper resource,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var dirPath = GetDirectoryPath(resource.ResourceType, timestamp);
        Directory.CreateDirectory(dirPath);

        var filePath = Path.Combine(dirPath, $"tx-{transactionId}.ndjson");

        using var stream = RecyclableMemoryStreamManager.Shared.GetStream("WriteResource");
        using var writer = new Utf8JsonWriter(stream);

        // Line 1: Transaction Bundle metadata
        var bundle = new
        {
            resourceType = "Bundle",
            id = transactionId.ToString(),
            type = "transaction",
            timestamp = timestamp,
            entry = new[]
            {
                new
                {
                    request = new
                    {
                        method = resource.Request.Method,
                        url = resource.Request.Url
                    }
                }
            }
        };

        JsonSerializer.Serialize(writer, bundle);
        await writer.FlushAsync(ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);

        // Line 2: Resource
        JsonSerializer.Serialize(writer, resource.Resource);
        await writer.FlushAsync(ct);

        // Write to file
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Position = 0;
        await stream.CopyToAsync(fileStream, ct);
    }

    private async Task WriteMetadataFileAsync(
        TransactionId transactionId,
        ResourceWrapper resource,
        IReadOnlyCollection<SearchIndexEntry> searchEntries,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var dirPath = GetDirectoryPath(resource.ResourceType, timestamp);
        var metadataPath = Path.Combine(dirPath, $"tx-{transactionId}.metadata.ndjson");

        using var stream = RecyclableMemoryStreamManager.Shared.GetStream("WriteMetadata");
        using var writer = new Utf8JsonWriter(stream);

        var metadata = new
        {
            resourceKey = new
            {
                resourceType = resource.ResourceType,
                id = resource.ResourceId,
                versionId = resource.VersionId
            },
            transactionId = transactionId.ToString(),
            timestamp = timestamp,
            requestMethod = resource.Request.Method,
            requestUrl = resource.Request.Url,
            ifMatch = resource.Request.IfMatch,
            ifNoneExist = resource.Request.IfNoneExist,
            ifModifiedSince = resource.Request.IfModifiedSince,
            searchIndices = searchEntries.Select(e => new
            {
                searchParameterName = e.SearchParameter.Name,
                searchParameterType = e.SearchParameter.Type.ToString(),
                value = SerializeSearchValue(e.Value)
            })
        };

        JsonSerializer.Serialize(writer, metadata);
        await writer.FlushAsync(ct);

        // Write to file
        await using var fileStream = new FileStream(metadataPath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Position = 0;
        await stream.CopyToAsync(fileStream, ct);
    }

    private string GetDirectoryPath(string resourceType, DateTimeOffset timestamp) =>
        Path.Combine(
            _basePath,
            resourceType,
            timestamp.ToString("yyyy"),
            timestamp.ToString("MM"),
            timestamp.ToString("dd"));
}
```

#### 4. InMemoryIndex (Basic) (2 hours)

```csharp
public class InMemoryIndex
{
    // Resource location index: ResourceKey -> FileLocation
    private readonly ConcurrentDictionary<ResourceKey, FileLocation> _resourceLocations = new();

    public void AddResourceLocation(ResourceKey key, FileLocation location)
    {
        _resourceLocations[key] = location;
    }

    public FileLocation? GetResourceLocation(ResourceKey key)
    {
        return _resourceLocations.TryGetValue(key, out var location) ? location : null;
    }

    public void LoadFromMetadata(ResourceMetadata metadata, FileLocation location)
    {
        var resourceKey = new ResourceKey(
            metadata.ResourceKey.ResourceType,
            metadata.ResourceKey.Id,
            metadata.ResourceKey.VersionId);

        _resourceLocations[resourceKey] = location;
    }
}

public record FileLocation(
    string FilePath,
    TransactionId TransactionId,
    DateTimeOffset Timestamp);
```

#### 5. Medino Handlers (3 hours)

```csharp
// Application Layer
public record CreateOrUpdatePatientCommand(
    string PatientId,
    ISourceNode Resource) : ICommand<ResourceKey>;

public class CreateOrUpdatePatientCommandHandler : ICommandHandler<CreateOrUpdatePatientCommand, ResourceKey>
{
    private readonly IFhirRepository _repository;
    private readonly IFhirSchemaProvider _schemaProvider;

    public async ValueTask<ResourceKey> HandleAsync(
        CreateOrUpdatePatientCommand command,
        CancellationToken ct)
    {
        // 1. Validate resource
        var validator = new FastStructuralValidator(_schemaProvider);
        var validationResult = validator.ValidateStructure(command.Resource, "Patient", ct);

        if (!validationResult.IsValid)
        {
            throw new FhirValidationException("Patient resource failed validation", validationResult.Issues);
        }

        // 2. Create ResourceWrapper
        var resource = new ResourceWrapper(
            ResourceType: "Patient",
            ResourceId: command.PatientId,
            VersionId: "1", // TODO: Increment version for updates
            LastModified: DateTimeOffset.UtcNow,
            Resource: command.Resource,
            Request: new ResourceRequest("PUT", $"Patient/{command.PatientId}", null, null, null),
            IsDeleted: false);

        // 3. Persist
        return await _repository.CreateOrUpdateAsync(resource, ct);
    }
}

public record GetPatientQuery(string PatientId) : IQuery<ResourceWrapper?>;

public class GetPatientQueryHandler : IQueryHandler<GetPatientQuery, ResourceWrapper?>
{
    private readonly IFhirRepository _repository;

    public async ValueTask<ResourceWrapper?> HandleAsync(
        GetPatientQuery query,
        CancellationToken ct)
    {
        var key = new ResourceKey("Patient", query.PatientId);
        return await _repository.GetAsync(key, ct);
    }
}
```

#### 6. API Layer (2 hours)

**Implementation**: ASP.NET Core MVC Controllers

**Rationale**:
- Traditional MVC pattern familiar to most developers
- Easy to extend with additional resource types
- Good tooling support and documentation
- Works well with Autofac dependency injection

```csharp
// Ignixa.Api/Features/Patient/Api/PatientController.cs
namespace Ignixa.Api.Features.Patient.Api;

[ApiController]
[Route("[controller]")]
public class PatientController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<PatientController> _logger;
    private readonly RecyclableMemoryStreamManager _memoryStreamManager;

    public PatientController(
        IMediator mediator,
        ILogger<PatientController> logger,
        RecyclableMemoryStreamManager memoryStreamManager)
    {
        _mediator = mediator;
        _logger = logger;
        _memoryStreamManager = memoryStreamManager;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(
        string id,
        CancellationToken cancellationToken)
    {
        var query = new GetPatientQuery(id);
        var result = await _mediator.SendAsync(query, cancellationToken);

        if (result == null)
        {
            return NotFound();
        }

        // Add FHIR headers
        Response.Headers.ETag = $"W/\"{result.VersionId}\"";
        Response.Headers.LastModified = result.LastModified.ToString("R");

        return Content(result.RawJson, "application/fhir+json");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(
        string id,
        CancellationToken cancellationToken)
    {
        // 1. Read request body using RecyclableMemoryStream
        using var stream = _memoryStreamManager.GetStream("put-patient");
        await Request.Body.CopyToAsync(stream, cancellationToken);

        stream.Position = 0;
        string json;
        using (var reader = new StreamReader(stream, leaveOpen: true))
        {
            json = await reader.ReadToEndAsync(cancellationToken);
        }

        // 2. Parse FHIR resource
        stream.Position = 0;
        var sourceNode = await FhirJsonNode.ParseAsync(stream, cancellationToken);

        // 3. Create request metadata
        var request = new ResourceRequest
        {
            Method = "PUT",
            Url = $"Patient/{id}",
            Headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
        };

        // 4. Send command via Medino
        var command = new CreateOrUpdatePatientCommand(id, json, request);
        var resourceKey = await _mediator.SendAsync(command, cancellationToken);

        // 5. Return response with FHIR headers
        Response.Headers.ETag = $"W/\"{resourceKey.VersionId}\"";

        return StatusCode(resourceKey.VersionId == 1 ? 201 : 200);
    }
}
```

#### 7. Index Loader Service (1 hour)

```csharp
public class IndexLoaderService : IHostedService
{
    private readonly InMemoryIndex _searchIndex;
    private readonly string _basePath;
    private readonly ILogger<IndexLoaderService> _logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading search index from metadata files...");

        var sw = Stopwatch.StartNew();
        var fileCount = 0;

        // Walk directory structure
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
            _logger.LogInformation("Created data directory: {BasePath}", _basePath);
            return;
        }

        var resourceDirs = Directory.GetDirectories(_basePath);

        foreach (var resourceDir in resourceDirs)
        {
            var resourceType = Path.GetFileName(resourceDir);

            // Find all .metadata.ndjson files
            var metadataFiles = Directory.GetFiles(
                resourceDir,
                "*.metadata.ndjson",
                SearchOption.AllDirectories);

            foreach (var metadataFile in metadataFiles)
            {
                var resourceFile = metadataFile.Replace(".metadata.ndjson", ".ndjson");
                await LoadMetadataFileAsync(metadataFile, resourceFile, cancellationToken);
                fileCount++;
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Search index loaded: {FileCount} transaction files in {Duration}ms",
            fileCount,
            sw.ElapsedMilliseconds);
    }

    private async Task LoadMetadataFileAsync(
        string metadataPath,
        string resourcePath,
        CancellationToken ct)
    {
        using var reader = new StreamReader(metadataPath);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            var metadata = JsonSerializer.Deserialize<ResourceMetadata>(line);
            if (metadata == null) continue;

            var location = new FileLocation(
                resourcePath,
                TransactionId.Parse(metadata.TransactionId),
                metadata.Timestamp);

            _searchIndex.LoadFromMetadata(metadata, location);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

## Success Criteria

### Functional Requirements

✅ **PUT /Patient/{id}** creates or updates patient:
- Parse FHIR JSON from request body
- Validate Patient resource structure
- Write `.ndjson` file with Bundle + Patient
- Write `.metadata.ndjson` file with search indices + request metadata
- Update InMemoryIndex with file location
- Return 200 OK with ETag and Last-Modified headers

✅ **GET /Patient/{id}** retrieves patient:
- Lookup file location in InMemoryIndex
- Read and parse `.ndjson` file
- Return 200 OK with resource
- Return 404 Not Found if resource doesn't exist
- Include ETag and Last-Modified headers

✅ **Server restart persistence**:
- IndexLoaderService loads all `.metadata.ndjson` files on startup
- InMemoryIndex rebuilt from metadata
- All resources accessible after restart

### Performance Targets

| Operation | Target | Measurement |
|-----------|--------|-------------|
| PUT /Patient/{id} | <20ms | File write + index update |
| GET /Patient/{id} | <10ms | Index lookup + file read |
| Startup (1,000 resources) | <3s | Metadata file loading |

### Test Coverage

✅ **80% minimum test coverage** using xUnit + NSubstitute:
- Unit tests for FileBasedFhirRepository
- Unit tests for Medino handlers
- Integration tests for PUT and GET endpoints
- IndexLoaderService tests

### E2E Test Success Criteria

From `src-old/test`:
- ✅ `CreateTests.cs` - Basic patient creation scenarios
- ✅ `ReadTests.cs` - Patient retrieval scenarios
- ✅ `UpdateTests.cs` - Patient update scenarios (PUT with same ID)

## Consequences

### Positive

1. **Rapid Validation**: Working FHIR server in Week 1
2. **Architecture Proven**: Vertical slice pattern validated end-to-end
3. **Developer Experience**: F5 works with zero setup
4. **Performance Baseline**: Establishes benchmarks for future phases
5. **Foundation for Search**: Metadata sidecar files ready for Phase 1.2
6. **Audit-Ready**: Request metadata captured from day one

### Negative

1. **Limited Functionality**: Only 2 operations (by design)
2. **No Search**: Search deferred to Phase 1.2
3. **No Bundle Processing**: Transactions deferred to Phase 1.1
4. **No DELETE**: Soft delete deferred to Phase 2

### Risks & Mitigation

| Risk | Mitigation |
|------|------------|
| File I/O performance | Use RecyclableMemoryStream, measure benchmarks |
| Index memory usage | Monitor with 10K+ resources, optimize if needed |
| Concurrent writes | Add file locking or queue if issues arise |
| Schema validation | Use existing IFhirSchemaProvider, proven code |

## Next Steps

**Week 2**: Phase 1.1 - Bundle Processing with Channels (ADR-2502)

**Week 3**: Phase 1.2 - Search Implementation with SearchQueryInterpreter (ADR-2503)

**Week 4**: Phase 1.3 - Search Parameter Types (ADR-2504)

## References

- Investigation: `phase1-file-based-storage-with-search.md`
- Investigation: `storage-architecture-v2.md`
- Investigation: `memory-efficient-fhir-patterns.md`
- Master Roadmap: ADR-2500 (to be created)
