# Investigation: Decoupled Resource and Index Storage Architecture

## Executive Summary

This investigation explores a fundamental architectural pattern for FHIR Server v2: **separating resource storage from search index storage**. The legacy server tightly couples these concerns, preventing flexible storage strategies like storing search indices in SQL while keeping resource data in blob storage.

**Key Innovation**: URN-based storage locations that enable:
- Multiple storage backends (SQL, Blob, File, Cosmos) for raw resources
- Independent storage for search indices (always in SQL for performance)
- Hybrid strategies (hot data in SQL, warm/cold data in blob tiers)
- Tiered storage by age, size, or resource type

**Implementation**: This pattern will be implemented in Phase 8 (SQL Storage) and Phase 9 (Cosmos Storage).

---

## Problem Statement

### Legacy Coupling Issues

The legacy FHIR server has three critical coupling problems:

#### 1. `ResourceWrapper` Couples Everything

```csharp
// Legacy design (from src-old/Microsoft.Health.Fhir.Core/Features/Persistence/ResourceWrapper.cs)
public class ResourceWrapper
{
    public RawResource RawResource { get; set; }                       // Raw resource data
    public IReadOnlyCollection<SearchIndexEntry> SearchIndices { get; set; }  // Search indices
    public CompartmentIndices CompartmentIndices { get; set; }         // Compartment indices
    public ResourceRequest Request { get; set; }                        // Request metadata
    // ... all bundled together
}
```

**Problem**: Cannot update search indices without having the full resource data.

#### 2. `IFhirDataStore` Enforces Coupling

```csharp
// Legacy interface (from src-old/Microsoft.Health.Fhir.Core/Features/Persistence/IFhirDataStore.cs)
public interface IFhirDataStore
{
    Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resourceWrapper, ...);
    Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, ...);
}
```

**Problem**: Must load full `ResourceWrapper` (including raw resource) just to update search indices.

#### 3. SQL Schema Couples Resource and Search

```sql
-- Legacy schema (from src-old/.../Schema/Sql/Tables/Resource.sql)
CREATE TABLE dbo.Resource
(
    ResourceSurrogateId bigint NOT NULL PRIMARY KEY,
    RawResource varbinary(max) NOT NULL,  -- ← Raw data in same table
    SearchParamHash varchar(64) NULL,
    ...
);

CREATE TABLE dbo.StringSearchParam
(
    ResourceSurrogateId bigint NOT NULL,  -- ← FK to Resource table
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) NOT NULL
);
```

**Problem**:
- `ResourceSurrogateId` FK couples search param tables to Resource table
- Cannot move `RawResource` to blob storage without breaking search indices
- Cannot have "hot" indices in SQL and "cold" resources in blob storage

### Consequences

1. **No Flexible Storage**: Cannot store indices in SQL while resources are in blob
2. **No Tiering**: Cannot move old resources to cheap blob tiers while keeping indices hot
3. **Expensive Reindex**: Must load full resources to reindex (cannot work from cached search values)
4. **No Hybrid Strategies**: Cannot mix storage types by age, size, or resource type
5. **Scaling Challenges**: Cannot scale index storage independently from resource storage

---

## URN-Based Storage Location Pattern

### Core Concept

Use a **URN (Uniform Resource Name)** to identify where a resource's raw data is stored, while keeping search indices separate in SQL.

```sql
CREATE TABLE dbo.Resource
(
    ResourceSurrogateId bigint NOT NULL PRIMARY KEY,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) NOT NULL,
    Version int NOT NULL,
    StorageLocation varchar(500) NOT NULL,  -- ← URN format
    IsDeleted bit NOT NULL,
    LastModified datetimeoffset NOT NULL,
    SearchParameterHash varchar(64),
    TransactionId bigint
);
```

### URN Format Specifications

#### SQL Storage

```
Format: sql://[TableName]/[ResourceSurrogateId]

Examples:
sql://RawResource/12345
sql://RawResource/67890
```

**Use case**: Hot data (< 30 days old), frequently accessed resources

#### Blob Storage

```
Format: blob://[ContainerName]/[ResourceType]/[Year]/[Month]/[Day]/[FileName]

Examples:
blob://fhirdata/Patient/2025/01/15/tx-1234567890.ndjson
blob://fhirdata-hot/Observation/2025/01/15/tx-9876543210.ndjson
blob://fhirdata-cool/Patient/2023/06/20/tx-5555555555.ndjson
blob://fhirdata-archive/DocumentReference/2020/01/10/tx-1111111111.ndjson
```

**Use cases**:
- Warm data (30-365 days) → Blob hot tier
- Cold data (> 365 days) → Blob cool/archive tier
- Large resources (> 1MB) → Blob regardless of age

**Follows filesystem layout**: Same `/ResourceType/year/month/day/` pattern as file storage

#### File Storage

```
Format: file://[BasePath]/[ResourceType]/[Year]/[Month]/[Day]/[FileName]

Examples:
file:///data/Patient/2025/01/15/tx-1234567890.ndjson
file://C:/data/Patient/2025/01/15/tx-1234567890.ndjson  (Windows)
```

**Use case**: F5 developer experience, local development, testing

**Follows filesystem layout**: Same pattern as blob storage for consistency

#### Cosmos Storage

```
Format: cosmos://[DatabaseName]/[PartitionKey]/[DocumentId]

Examples:
cosmos://fhirdb/Patient|2025-01|tenant1/patient-123-v1
cosmos://fhirdb/Observation|2024-12|tenant2/obs-abc-v2
```

**Use case**: Planet-scale deployments, globally distributed data

---

## Architecture Pattern

### Core Abstractions

```csharp
// Polymorphic storage location (C# discriminated union)
public abstract record ResourceStorage
{
    public abstract string ToUrn();

    public static ResourceStorage Parse(string urn)
    {
        var uri = new Uri(urn);

        return uri.Scheme switch
        {
            "sql" => SqlResourceStorage.Parse(uri),
            "blob" => BlobResourceStorage.Parse(uri),
            "file" => FileResourceStorage.Parse(uri),
            "cosmos" => CosmosResourceStorage.Parse(uri),
            _ => throw new NotSupportedException($"Unknown storage scheme: {uri.Scheme}")
        };
    }
}

// Resource pointer: identity + storage location
public record ResourcePointer(
    string ResourceType,
    string ResourceId,
    string VersionId,
    long ResourceSurrogateId,
    ResourceStorage Storage);  // ← Polymorphic!

// Raw resource data (storage-agnostic)
public record RawResourceData(
    ReadOnlyMemory<byte> Data,
    bool IsCompressed,
    string ContentHash);
```

### Storage Abstraction Interfaces

```csharp
// Raw resource storage (can be SQL, Blob, File, Cosmos)
public interface IResourceStore
{
    ValueTask<RawResourceData?> GetAsync(ResourcePointer pointer, CancellationToken ct);

    ValueTask<ResourcePointer> StoreAsync(
        string resourceType,
        string resourceId,
        string versionId,
        RawResourceData data,
        DateTimeOffset timestamp,
        CancellationToken ct);

    ValueTask DeleteAsync(ResourcePointer pointer, CancellationToken ct);
}

// Search index storage (always SQL for performance)
public interface ISearchIndexStore
{
    ValueTask IndexAsync(
        ResourcePointer pointer,
        IReadOnlyCollection<SearchIndexEntry> indices,
        CancellationToken ct);

    ValueTask<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken ct);

    ValueTask DeleteIndicesAsync(ResourcePointer pointer, CancellationToken ct);
}

// Orchestrates operations across stores
public class ResourceCoordinator : IFhirRepository
{
    private readonly IResourceStore _resourceStore;
    private readonly ISearchIndexStore _searchIndexStore;
    private readonly ISearchIndexer _indexer;

    public async ValueTask<ResourceKey> CreateOrUpdateAsync(
        ResourceWrapper resource,
        CancellationToken ct)
    {
        // 1. Extract search indices
        var searchIndices = _indexer.Extract(resource.Resource);

        // 2. Store raw resource (returns pointer with URN)
        var rawData = new RawResourceData(
            SerializeResource(resource),
            IsCompressed: true,
            ContentHash: ComputeHash(resource));

        var pointer = await _resourceStore.StoreAsync(
            resource.ResourceType,
            resource.ResourceId,
            resource.VersionId,
            rawData,
            DateTimeOffset.UtcNow,
            ct);

        // 3. Store search indices with pointer reference
        await _searchIndexStore.IndexAsync(pointer, searchIndices, ct);

        return new ResourceKey(pointer.ResourceType, pointer.ResourceId, pointer.VersionId);
    }

    public async ValueTask<ResourceWrapper?> GetAsync(
        ResourceKey key,
        CancellationToken ct)
    {
        // 1. Search for resource pointer
        var searchResult = await _searchIndexStore.SearchAsync(
            new SearchRequest { ResourceType = key.ResourceType, Id = key.Id },
            ct);

        if (!searchResult.Pointers.Any()) return null;

        var pointer = searchResult.Pointers.First();

        // 2. Fetch raw resource from storage location (URN tells us where)
        var rawData = await _resourceStore.GetAsync(pointer, ct);
        if (rawData == null) return null;

        // 3. Deserialize and return
        return DeserializeResource(rawData, pointer);
    }
}
```

---

## Polymorphic ResourceStorage Implementations

### SqlResourceStorage

```csharp
public record SqlResourceStorage(
    long ResourceSurrogateId,
    string TableName = "RawResource") : ResourceStorage
{
    // sql://RawResource/12345
    public override string ToUrn() => $"sql://{TableName}/{ResourceSurrogateId}";

    public static SqlResourceStorage Parse(Uri uri)
    {
        // uri.Host = "RawResource"
        // uri.AbsolutePath = "/12345"
        var tableName = uri.Host;
        var surrogateId = long.Parse(uri.AbsolutePath.TrimStart('/'));
        return new SqlResourceStorage(surrogateId, tableName);
    }
}
```

### BlobResourceStorage

```csharp
public record BlobResourceStorage(
    string ContainerName,
    string ResourceType,
    int Year,
    int Month,
    int Day,
    string FileName) : ResourceStorage
{
    // blob://fhirdata/Patient/2025/01/15/tx-1234567890.ndjson
    public override string ToUrn() =>
        $"blob://{ContainerName}/{ResourceType}/{Year:D4}/{Month:D2}/{Day:D2}/{FileName}";

    // Matches filesystem layout
    public string GetBlobPath() =>
        $"{ResourceType}/{Year:D4}/{Month:D2}/{Day:D2}/{FileName}";

    public static BlobResourceStorage Parse(Uri uri)
    {
        // uri.Host = "fhirdata"
        // uri.AbsolutePath = "/Patient/2025/01/15/tx-1234567890.ndjson"
        var containerName = uri.Host;
        var parts = uri.AbsolutePath.TrimStart('/').Split('/');

        return new BlobResourceStorage(
            ContainerName: containerName,
            ResourceType: parts[0],      // "Patient"
            Year: int.Parse(parts[1]),    // 2025
            Month: int.Parse(parts[2]),   // 01
            Day: int.Parse(parts[3]),     // 15
            FileName: parts[4]);          // "tx-1234567890.ndjson"
    }
}
```

### FileResourceStorage

```csharp
public record FileResourceStorage(
    string ResourceType,
    int Year,
    int Month,
    int Day,
    string FileName) : ResourceStorage
{
    // file:///data/Patient/2025/01/15/tx-1234567890.ndjson
    public override string ToUrn() =>
        $"file:///data/{ResourceType}/{Year:D4}/{Month:D2}/{Day:D2}/{FileName}";

    // Matches blob storage layout
    public string GetFilePath(string basePath) =>
        Path.Combine(basePath, ResourceType, $"{Year:D4}", $"{Month:D2}", $"{Day:D2}", FileName);

    public static FileResourceStorage Parse(Uri uri)
    {
        // uri.AbsolutePath = "/data/Patient/2025/01/15/tx-1234567890.ndjson"
        var parts = uri.AbsolutePath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToArray();

        // Last 5 parts are: ResourceType/Year/Month/Day/FileName
        var offset = parts.Length - 5;

        return new FileResourceStorage(
            ResourceType: parts[offset],
            Year: int.Parse(parts[offset + 1]),
            Month: int.Parse(parts[offset + 2]),
            Day: int.Parse(parts[offset + 3]),
            FileName: parts[offset + 4]);
    }
}
```

### CosmosResourceStorage

```csharp
public record CosmosResourceStorage(
    string DatabaseName,
    string PartitionKey,
    string DocumentId) : ResourceStorage
{
    // cosmos://fhirdb/Patient|2025-01|tenant1/patient-123-v1
    public override string ToUrn() =>
        $"cosmos://{DatabaseName}/{PartitionKey}/{DocumentId}";

    public static CosmosResourceStorage Parse(Uri uri)
    {
        // uri.Host = "fhirdb"
        // uri.AbsolutePath = "/Patient|2025-01|tenant1/patient-123-v1"
        var databaseName = uri.Host;
        var parts = uri.AbsolutePath.TrimStart('/').Split('/');

        return new CosmosResourceStorage(
            DatabaseName: databaseName,
            PartitionKey: parts[0],  // "Patient|2025-01|tenant1"
            DocumentId: parts[1]);   // "patient-123-v1"
    }
}
```

---

## Storage Provider Implementations

### CompositeResourceStore (Router)

```csharp
public class CompositeResourceStore : IResourceStore
{
    private readonly SqlResourceStore _sqlStore;
    private readonly BlobResourceStore _blobStore;
    private readonly FileResourceStore _fileStore;
    private readonly CosmosResourceStore _cosmosStore;

    public async ValueTask<RawResourceData?> GetAsync(
        ResourcePointer pointer,
        CancellationToken ct)
    {
        // Route based on parsed storage type
        return pointer.Storage switch
        {
            SqlResourceStorage sql => await _sqlStore.GetAsync(sql, ct),
            BlobResourceStorage blob => await _blobStore.GetAsync(blob, ct),
            FileResourceStorage file => await _fileStore.GetAsync(file, ct),
            CosmosResourceStorage cosmos => await _cosmosStore.GetAsync(cosmos, ct),
            _ => throw new NotSupportedException($"Unknown storage: {pointer.Storage.GetType()}")
        };
    }

    public async ValueTask<ResourcePointer> StoreAsync(
        string resourceType,
        string resourceId,
        string versionId,
        RawResourceData data,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        // Select store based on strategy
        var store = _storageStrategy.SelectStore(resourceType, data.Data.Length, timestamp);

        return store switch
        {
            SqlResourceStore sql => await sql.StoreAsync(resourceType, resourceId, versionId, data, timestamp, ct),
            BlobResourceStore blob => await blob.StoreAsync(resourceType, resourceId, versionId, data, timestamp, ct),
            FileResourceStore file => await file.StoreAsync(resourceType, resourceId, versionId, data, timestamp, ct),
            CosmosResourceStore cosmos => await cosmos.StoreAsync(resourceType, resourceId, versionId, data, timestamp, ct),
            _ => throw new NotSupportedException()
        };
    }
}
```

### BlobResourceStore

```csharp
public class BlobResourceStore
{
    private readonly BlobContainerClient _container;

    public async ValueTask<ResourcePointer> StoreAsync(
        string resourceType,
        string resourceId,
        string versionId,
        RawResourceData data,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var surrogateId = GenerateSurrogateId();
        var txId = GenerateTransactionId();

        // Use filesystem layout: /ResourceType/year/month/day/tx-{txId}.ndjson
        var blobStorage = new BlobResourceStorage(
            ContainerName: _container.Name,
            ResourceType: resourceType,
            Year: timestamp.Year,
            Month: timestamp.Month,
            Day: timestamp.Day,
            FileName: $"tx-{txId}.ndjson");

        var blobPath = blobStorage.GetBlobPath();
        var blobClient = _container.GetBlobClient(blobPath);

        // Write NDJSON with Bundle + Resource (same format as file storage)
        using var stream = RecyclableMemoryStreamManager.Shared.GetStream();

        // Line 1: Bundle metadata
        await WriteTransactionBundleAsync(stream, txId, resourceType, resourceId, timestamp, ct);
        await stream.WriteAsync("\n"u8.ToArray(), ct);

        // Line 2: Resource data
        await stream.WriteAsync(data.Data, ct);

        stream.Position = 0;
        await blobClient.UploadAsync(stream, overwrite: false, ct);

        // Also write .metadata.ndjson sidecar (same as file storage)
        await WriteMetadataSidecarAsync(blobStorage, resourceType, resourceId, versionId, timestamp, ct);

        return new ResourcePointer(
            ResourceType: resourceType,
            ResourceId: resourceId,
            VersionId: versionId,
            ResourceSurrogateId: surrogateId,
            Storage: blobStorage);
    }

    public async ValueTask<RawResourceData?> GetAsync(
        BlobResourceStorage storage,
        CancellationToken ct)
    {
        var blobPath = storage.GetBlobPath();
        var blobClient = _container.GetBlobClient(blobPath);

        if (!await blobClient.ExistsAsync(ct))
            return null;

        var download = await blobClient.DownloadContentAsync(ct);

        // Parse NDJSON (skip line 1 Bundle, extract line 2+ resources)
        var resourceData = await ExtractResourceFromNdjsonAsync(download.Value.Content, ct);

        return new RawResourceData(
            Data: resourceData,
            IsCompressed: false,  // Blob storage handles compression
            ContentHash: ComputeHash(resourceData));
    }
}
```

### SqlResourceStore

```csharp
public class SqlResourceStore
{
    public async ValueTask<ResourcePointer> StoreAsync(
        string resourceType,
        string resourceId,
        string versionId,
        RawResourceData data,
        DateTimeOffset timestamp,
        CancellationToken ct)
    {
        var surrogateId = await GetNextSurrogateIdAsync(ct);

        var sqlStorage = new SqlResourceStorage(
            ResourceSurrogateId: surrogateId,
            TableName: "RawResource");

        // Store in separate RawResource table
        await ExecuteSqlAsync(@"
            INSERT INTO dbo.RawResource (
                ResourceSurrogateId,
                ResourceTypeId,
                Data,
                IsCompressed,
                ContentHash,
                SizeBytes
            ) VALUES (
                @SurrogateId,
                @ResourceTypeId,
                @Data,
                @IsCompressed,
                @ContentHash,
                @SizeBytes
            )",
            new {
                SurrogateId = surrogateId,
                ResourceTypeId = GetResourceTypeId(resourceType),
                Data = data.Data.ToArray(),
                data.IsCompressed,
                data.ContentHash,
                SizeBytes = data.Data.Length
            },
            ct);

        return new ResourcePointer(
            ResourceType: resourceType,
            ResourceId: resourceId,
            VersionId: versionId,
            ResourceSurrogateId: surrogateId,
            Storage: sqlStorage);
    }

    public async ValueTask<RawResourceData?> GetAsync(
        SqlResourceStorage storage,
        CancellationToken ct)
    {
        var result = await QuerySqlAsync(@"
            SELECT Data, IsCompressed, ContentHash
            FROM dbo.RawResource
            WHERE ResourceSurrogateId = @SurrogateId",
            new { SurrogateId = storage.ResourceSurrogateId },
            ct);

        if (result == null) return null;

        return new RawResourceData(
            Data: result.Data,
            IsCompressed: result.IsCompressed,
            ContentHash: result.ContentHash);
    }
}
```

---

## SQL Schema Pattern

### Resource Table (Metadata + URN Pointer)

```sql
CREATE TABLE dbo.Resource
(
    ResourceSurrogateId bigint NOT NULL PRIMARY KEY,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version int NOT NULL,
    IsDeleted bit NOT NULL DEFAULT 0,
    LastModified datetimeoffset NOT NULL DEFAULT SYSDATETIMEOFFSET(),

    -- URN-based storage location
    StorageLocation varchar(500) NOT NULL,

    SearchParameterHash varchar(64),
    TransactionId bigint,

    CONSTRAINT PK_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId)
        WITH (DATA_COMPRESSION = PAGE)
);

-- Indexes for storage location queries
CREATE INDEX IX_Resource_StorageLocation
    ON dbo.Resource(StorageLocation);

CREATE INDEX IX_Resource_StorageLocation_Sql
    ON dbo.Resource(StorageLocation)
    WHERE StorageLocation LIKE 'sql://%';

CREATE INDEX IX_Resource_StorageLocation_Blob
    ON dbo.Resource(StorageLocation)
    WHERE StorageLocation LIKE 'blob://%';
```

### RawResource Table (Binary Data for SQL Storage Only)

```sql
CREATE TABLE dbo.RawResource
(
    ResourceSurrogateId bigint NOT NULL PRIMARY KEY,
    ResourceTypeId smallint NOT NULL,
    Data varbinary(max) NOT NULL,
    IsCompressed bit NOT NULL DEFAULT 1,
    ContentHash binary(32) NOT NULL,
    SizeBytes int NOT NULL,

    CONSTRAINT PK_RawResource PRIMARY KEY CLUSTERED (ResourceSurrogateId, ResourceTypeId)
        WITH (DATA_COMPRESSION = PAGE),

    CONSTRAINT CH_RawResource_Data_Length CHECK (Data > 0x0)
);

-- No FK to Resource table - referenced via URN
```

### Search Index Tables (FK to Resource, Not RawResource)

```sql
CREATE TABLE dbo.StringSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin bit DEFAULT 0 NOT NULL,
    IsMax bit DEFAULT 0 NOT NULL,

    -- FK to Resource (metadata table), not RawResource
    CONSTRAINT FK_StringSearchParam_Resource
        FOREIGN KEY (ResourceSurrogateId)
        REFERENCES dbo.Resource(ResourceSurrogateId)
);

CREATE CLUSTERED INDEX IXC_StringSearchParam
    ON dbo.StringSearchParam (ResourceSurrogateId, SearchParamId)
    WITH (DATA_COMPRESSION = PAGE);
```

### SqlSearchIndexStore Implementation

```csharp
public class SqlSearchIndexStore : ISearchIndexStore
{
    public async ValueTask IndexAsync(
        ResourcePointer pointer,
        IReadOnlyCollection<SearchIndexEntry> indices,
        CancellationToken ct)
    {
        // Store resource metadata with URN
        var storageLocation = pointer.Storage.ToUrn();

        await ExecuteSqlAsync(@"
            INSERT INTO dbo.Resource (
                ResourceSurrogateId,
                ResourceTypeId,
                ResourceId,
                Version,
                StorageLocation,
                IsDeleted,
                LastModified,
                SearchParameterHash
            ) VALUES (
                @SurrogateId,
                @TypeId,
                @ResourceId,
                @Version,
                @StorageLocation,
                0,
                @LastModified,
                @SearchParamHash
            )",
            new {
                SurrogateId = pointer.ResourceSurrogateId,
                TypeId = GetResourceTypeId(pointer.ResourceType),
                ResourceId = pointer.ResourceId,
                Version = int.Parse(pointer.VersionId),
                StorageLocation = storageLocation,  // URN string
                LastModified = DateTimeOffset.UtcNow,
                SearchParamHash = ComputeHash(indices)
            },
            ct);

        // Store search indices
        foreach (var index in indices)
        {
            await InsertSearchIndexAsync(pointer.ResourceSurrogateId, index, ct);
        }
    }

    public async ValueTask<SearchResult> SearchAsync(
        SearchRequest request,
        CancellationToken ct)
    {
        // Query search param tables + join Resource for URN
        var sql = @"
            SELECT
                r.ResourceSurrogateId,
                r.ResourceTypeId,
                r.ResourceId,
                r.Version,
                r.StorageLocation
            FROM dbo.Resource r
            INNER JOIN dbo.StringSearchParam s ON r.ResourceSurrogateId = s.ResourceSurrogateId
            WHERE r.ResourceTypeId = @TypeId
              AND s.SearchParamId = @SearchParamId
              AND s.Text = @SearchValue";

        var rows = await QuerySqlAsync(sql, new { ... }, ct);

        // Parse URNs back to typed storage
        var pointers = rows.Select(row => new ResourcePointer(
            ResourceType: GetResourceTypeName(row.ResourceTypeId),
            ResourceId: row.ResourceId,
            VersionId: row.Version.ToString(),
            ResourceSurrogateId: row.ResourceSurrogateId,
            Storage: ResourceStorage.Parse(row.StorageLocation)  // Parse URN!
        )).ToList();

        return new SearchResult(pointers);
    }
}
```

---

## Hybrid Storage Strategies

### Strategy 1: Tiered by Age

```csharp
public class TieredStorageStrategy : IResourceStorageStrategy
{
    private readonly FileResourceStore _fileStore;     // Development only
    private readonly SqlResourceStore _sqlStore;       // Hot data (< 30 days)
    private readonly BlobResourceStore _blobHotStore;  // Warm data (30-365 days, blob hot tier)
    private readonly BlobResourceStore _blobCoolStore; // Cold data (> 365 days, blob cool tier)
    private readonly IHostEnvironment _environment;

    public IResourceStore SelectStore(
        string resourceType,
        int dataSize,
        DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp;

        // Development: always use file storage
        if (_environment.IsDevelopment())
            return _fileStore;

        // Production: tier by age
        if (age.TotalDays < 30)
            return _sqlStore;           // Hot: SQL for fast access
        else if (age.TotalDays < 365)
            return _blobHotStore;       // Warm: Blob hot tier
        else
            return _blobCoolStore;      // Cold: Blob cool/archive tier
    }
}
```

### Strategy 2: Tiered by Size

```csharp
public class SizeBasedStorageStrategy : IResourceStorageStrategy
{
    public IResourceStore SelectStore(
        string resourceType,
        int dataSize,
        DateTimeOffset timestamp)
    {
        // Large resources (> 1MB) always go to blob
        if (dataSize > 1_000_000)
            return _blobStore;

        // Small resources stay in SQL
        return _sqlStore;
    }
}
```

### Strategy 3: Tiered by Resource Type

```csharp
public class ResourceTypeStorageStrategy : IResourceStorageStrategy
{
    private static readonly HashSet<string> BlobResourceTypes = new()
    {
        "DocumentReference",  // Often has large attachments
        "Binary",             // Always large
        "Media",              // Images, audio, video
        "DiagnosticReport"    // Can have embedded documents
    };

    public IResourceStore SelectStore(
        string resourceType,
        int dataSize,
        DateTimeOffset timestamp)
    {
        if (BlobResourceTypes.Contains(resourceType))
            return _blobStore;

        return _sqlStore;
    }
}
```

### Migration Between Tiers (Background Job)

```csharp
public class StorageTieringService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await TierOldResourcesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task TierOldResourcesAsync(CancellationToken ct)
    {
        // Find resources in SQL older than 30 days
        var oldResources = await ExecuteSqlAsync(@"
            SELECT ResourceSurrogateId, StorageLocation
            FROM dbo.Resource
            WHERE StorageLocation LIKE 'sql://%'
              AND LastModified < DATEADD(day, -30, GETUTCDATE())
            ORDER BY LastModified
            OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY",
            ct);

        foreach (var resource in oldResources)
        {
            await MoveToWarmTierAsync(resource, ct);
        }
    }

    private async Task MoveToWarmTierAsync(ResourceRow resource, CancellationToken ct)
    {
        // 1. Parse current SQL storage location
        var sqlStorage = ResourceStorage.Parse(resource.StorageLocation) as SqlResourceStorage;

        // 2. Read from SQL
        var rawData = await _sqlStore.GetAsync(sqlStorage, ct);

        // 3. Write to blob (warm tier)
        var blobPointer = await _blobStore.StoreAsync(
            resource.ResourceType,
            resource.ResourceId,
            resource.VersionId,
            rawData,
            resource.LastModified,
            ct);

        // 4. Update Resource table with new blob URN
        await ExecuteSqlAsync(@"
            UPDATE dbo.Resource
            SET StorageLocation = @NewStorageLocation
            WHERE ResourceSurrogateId = @SurrogateId",
            new {
                NewStorageLocation = blobPointer.Storage.ToUrn(),  // blob://fhirdata-warm/...
                SurrogateId = resource.ResourceSurrogateId
            },
            ct);

        // 5. Delete from SQL RawResource table
        await ExecuteSqlAsync(@"
            DELETE FROM dbo.RawResource
            WHERE ResourceSurrogateId = @SurrogateId",
            new { SurrogateId = resource.ResourceSurrogateId },
            ct);

        _logger.LogInformation("Moved resource {ResourceType}/{ResourceId} from SQL to blob warm tier",
            resource.ResourceType, resource.ResourceId);
    }
}
```

---

## Benefits

### 1. Flexibility

**Mix storage types** based on deployment needs:
- Development: File storage (F5 experience)
- Small deployments: SQL only
- Medium deployments: SQL + Blob (tiered)
- Large deployments: SQL + Blob + Cosmos

### 2. Cost Optimization

**Move old resources to cheap storage**:
- Hot data (< 30 days): SQL (~$0.12/GB/month)
- Warm data (30-365 days): Blob hot tier (~$0.02/GB/month)
- Cold data (> 365 days): Blob cool tier (~$0.01/GB/month)
- Archive data (> 3 years): Blob archive (~$0.002/GB/month)

**Example savings** for 10TB of FHIR data (5 years old):
- All in SQL: $1,200/month
- Tiered (10% hot, 20% warm, 70% cold): $260/month
- **Savings**: $940/month (78% reduction)

### 3. Performance

**Keep hot indices in SQL, cold resources in blob**:
- Search indices: Always in SQL (fast queries)
- Recent resources: SQL (fast access)
- Old resources: Blob (acceptable latency for rare access)

**Query pattern optimization**:
```sql
-- Search is always fast (indices in SQL)
SELECT r.ResourceSurrogateId, r.StorageLocation
FROM dbo.Resource r
INNER JOIN dbo.StringSearchParam s ON r.ResourceSurrogateId = s.ResourceSurrogateId
WHERE s.Text = 'Smith';

-- Recent resources: fast (in SQL)
-- Old resources: slower (fetch from blob, but rare)
```

### 4. Scalability

**Scale independently**:
- Search index storage (SQL): Scale for query performance
- Resource storage (Blob): Scale for capacity
- No coupling between the two

### 5. Reindex Efficiency

**Future optimization**: Store searchable source separately

```csharp
public interface ISearchIndexStore
{
    // Store minimal parsed representation for reindexing
    ValueTask StoreSearchableSourceAsync(
        ResourcePointer pointer,
        ISourceNode searchableSource,  // Just what's needed for search extraction
        CancellationToken ct);

    // Reindex without loading full resource
    ValueTask ReindexAsync(
        ResourcePointer pointer,
        ISearchParameterDefinition[] newParams,
        CancellationToken ct)
    {
        // Load searchable source (small, < 10KB vs 100KB+ full resource)
        var source = await GetSearchableSourceAsync(pointer, ct);

        // Extract new indices
        var newIndices = _indexer.Extract(source, newParams);

        // Update indices (no need to fetch from blob!)
        await UpdateIndicesAsync(pointer, newIndices, ct);
    }
}
```

---

## Implementation Phases

### Prototype Phase (Week 1) - ADR-2501

**Simple coupled approach** (no decoupling yet):
- Single `IFhirRepository` interface
- File-based storage with `.metadata.ndjson` sidecar files
- Search indices loaded into `InMemoryIndex` on startup
- Focus: F5 developer experience and vertical slice validation

**Why not decoupled?**
- Prototype Phase focuses on proving the vertical slice pattern
- Decoupling adds complexity that's not needed for Week 1
- File storage doesn't benefit from decoupling (everything is local)

### Phase 8: SQL Server Storage (Weeks 30-35) - ADR-2511

**First implementation of decoupled pattern**:
- Implement `IResourceStore` and `ISearchIndexStore`
- Separate `Resource` and `RawResource` tables
- URN-based `StorageLocation` column
- `SqlResourceStorage` for SQL-based resources
- All resources initially in SQL (no tiering yet)

**Schema**:
```sql
CREATE TABLE dbo.Resource (
    ResourceSurrogateId bigint PRIMARY KEY,
    StorageLocation varchar(500) NOT NULL,  -- "sql://RawResource/12345"
    ...
);

CREATE TABLE dbo.RawResource (
    ResourceSurrogateId bigint PRIMARY KEY,
    Data varbinary(max) NOT NULL,
    ...
);
```

### Phase 9: Cosmos DB Storage (Weeks 36-43) - ADR-2512

**Add Cosmos storage provider**:
- Implement `CosmosResourceStore`
- `CosmosResourceStorage` for Cosmos-based resources
- URN format: `cosmos://fhirdb/Patient|2025-01|tenant1/patient-123-v1`
- Search indices still in SQL (hybrid approach)

### Phase 10+: Hybrid Storage Strategies

**Add blob storage and tiering**:
- Implement `BlobResourceStore`
- `BlobResourceStorage` with filesystem layout
- Tiered storage strategies (by age, size, type)
- Background job to migrate between tiers
- Cost optimization for large deployments

---

## Comparison: Legacy vs V2

| Aspect | Legacy | V2 (Decoupled) |
|--------|--------|----------------|
| **Storage Coupling** | Resource + Indices in same object | Separate `IResourceStore` + `ISearchIndexStore` |
| **Schema** | `RawResource` in Resource table | Separate `RawResource` table |
| **Storage Location** | Implicit (always SQL) | Explicit URN (`StorageLocation` column) |
| **Flexibility** | SQL only | SQL, Blob, File, Cosmos |
| **Tiering** | Not possible | Age/size/type-based tiering |
| **Reindex** | Must load full resource | Can use cached searchable source |
| **Cost** | High (all in SQL) | Optimized (tiered storage) |
| **Scalability** | Coupled | Independent |

---

## References

- **Legacy Code Analysis**: `src-old/Microsoft.Health.Fhir.Core/Features/Persistence/`
- **Storage Architecture v2**: `docs/investigations/storage-architecture-v2.md`
- **Transaction Table**: `docs/investigations/transaction-table-core-abstraction.md`
- **Phase 1 File Storage**: `docs/investigations/phase1-file-based-storage-with-search.md`

## Incorporated Into

- **ADR-2501**: Prototype Phase (mentions future decoupling)
- **ADR-2511**: Phase 8 - SQL Server Storage (first implementation)
- **ADR-2512**: Phase 9 - Cosmos DB Storage (Cosmos URN format)
- **Future**: Phase 10+ - Hybrid storage strategies
