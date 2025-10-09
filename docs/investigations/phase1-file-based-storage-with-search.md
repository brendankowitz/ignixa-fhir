# Phase 1: File-Based Storage with In-Memory Search

## Overview

Phase 1 implementation focuses on establishing a working FHIR server with:
1. **File-based storage** (NDJSON with transaction bundles + metadata sidecar files)
2. **In-memory search indices** (loaded from metadata files on startup)
3. **Complete InMemory search architecture** (from microsoft/fhir-server subscription-engine branch)
4. **Fast CRUD operations** with search capability

This provides the F5 developer experience while establishing patterns for production storage providers.

**Note**: This implementation uses the complete InMemory search infrastructure from microsoft/fhir-server:
- `SearchQueryInterpreter.cs` - Expression visitor for query evaluation
- `ComparisonValueVisitor.cs` - Search value comparison logic
- `InMemoryIndex.cs` - Index structure and management

---

## Key Design Decisions

### 1. Metadata Sidecar Files

**Problem**: Re-extracting search parameters on startup is expensive (FHIRPath evaluation, schema lookups).

**Solution**: Write `.metadata.ndjson` sidecar files alongside `.ndjson` resource files:
```
/data/Patient/2025/01/15/
  tx-1234567890.ndjson           # Resource data (Bundle + Resources)
  tx-1234567890.metadata.ndjson  # Pre-extracted search indices
```

**Benefits**:
- **10x faster startup**: No search parameter re-extraction
- **Consistent indices**: Same values used at write and read time
- **Includes request metadata**: Stores HTTP method, URL, conditional headers (from legacy data layers)
- **Idempotent reloading**: Can rebuild index without changing behavior

### 2. Prototype Phase First

**Goal**: Complete vertical slice with PUT and GET before adding search.

**Rationale**:
1. Establishes project structure and all architectural layers
2. Proves file-based storage works end-to-end
3. Creates foundation for Bundle processing
4. Enables early testing and validation

**Layers**:
- API Layer (ASP.NET Core controllers)
- Application Layer (Medino handlers)
- Domain Layer (Resource models)
- Infrastructure Layer (FileBasedFhirRepository + InMemoryIndex)
- Tests (xUnit + NSubstitute, 80% coverage)

### 3. Request Metadata Storage

**Based on**: Legacy data layer design (ResourceWrapper.Request)

**Stored in metadata**:
- `RequestMethod` - POST, PUT, DELETE, GET, etc.
- `RequestUrl` - Patient, Patient/123, etc.
- `IfMatch` - ETag for conditional requests
- `IfNoneExist` - Conditional create
- `IfModifiedSince` - Conditional read

**Use cases**:
- Audit trails and history reconstruction
- Replay transactions from files
- Debugging and troubleshooting
- Bundle entry processing

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Phase 1 Architecture                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  1. File System Storage (Persistent)                            │
│     /data/{ResourceType}/{year}/{month}/{day}/                 │
│       - tx-{txId}.ndjson          (Resource data)              │
│       - tx-{txId}.metadata.ndjson (Search indices)             │
│                                                                  │
│  2. InMemoryIndex (from microsoft/fhir-server)                  │
│     ConcurrentDictionary<string, List<(ResourceKey, Indices)>>  │
│     Loaded from .metadata.ndjson files on startup               │
│     No re-extraction of search parameters needed                │
│                                                                  │
│  3. SearchQueryInterpreter (Search Engine)                      │
│     Visitor pattern for expression evaluation                    │
│     LINQ-based filtering using InMemoryIndex                    │
│                                                                  │
│  4. ComparisonValueVisitor (Value Comparison)                   │
│     ISearchValueVisitor for type-specific comparisons           │
│     Supports all FHIR search value types                        │
│                                                                  │
│  5. Transaction Support                                          │
│     Bundle metadata in first line of .ndjson                    │
│     Search indices in .metadata.ndjson sidecar                  │
│     Atomic file writes for consistency                          │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### 1. File Storage Layer with Metadata Sidecar

**Based on**: `storage-architecture-v2.md` File System design + metadata sidecar optimization

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
        var resource = await ReadFromNdjsonAsync(location, key, ct);
        return resource;
    }

    public async ValueTask<ResourceKey> CreateAsync(
        ResourceWrapper resource,
        CancellationToken ct = default)
    {
        var transactionId = TransactionId.Generate();
        var timestamp = DateTimeOffset.UtcNow;

        // 1. Extract search parameters BEFORE writing
        var searchEntries = _searchIndexer.Extract(resource.Resource);

        // 2. Write resource data file
        await WriteResourceFileAsync(transactionId, resource, timestamp, ct);

        // 3. Write metadata sidecar file
        await WriteMetadataFileAsync(transactionId, resource, searchEntries, timestamp, ct);

        // 4. Update in-memory index
        _searchIndex.IndexResources(resource.ToResourceElement());

        // 5. Return resource key
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
        using var writer = new StreamWriter(filePath, append: false);

        // Line 1: Transaction Bundle metadata
        var bundle = new Bundle
        {
            Id = transactionId.ToString(),
            Type = Bundle.BundleType.Transaction,
            Timestamp = timestamp,
            Entry = new List<Bundle.EntryComponent>
            {
                new Bundle.EntryComponent
                {
                    Request = new Bundle.RequestComponent
                    {
                        Method = Bundle.HTTPVerb.POST,
                        Url = $"{resource.ResourceType}"
                    }
                }
            }
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(bundle));

        // Line 2+: Resources
        await writer.WriteLineAsync(SerializeResource(resource));
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

        using var writer = new StreamWriter(metadataPath, append: false);

        // Write metadata for each resource
        var metadata = new ResourceMetadata
        {
            ResourceKey = new ResourceKey(resource.ResourceType, resource.ResourceId, resource.VersionId),
            TransactionId = transactionId,
            Timestamp = timestamp,

            // Request information (from legacy data layers)
            RequestMethod = resource.Request.Method,
            RequestUrl = resource.Request.Url,
            IfMatch = resource.Request.IfMatch,
            IfNoneExist = resource.Request.IfNoneExist,
            IfModifiedSince = resource.Request.IfModifiedSince,

            SearchIndices = searchEntries.Select(e => new SearchIndexMetadata
            {
                SearchParameterName = e.SearchParameter.Name,
                SearchParameterType = e.SearchParameter.Type,
                Value = SerializeSearchValue(e.Value)
            }).ToList()
        };

        await writer.WriteLineAsync(JsonSerializer.Serialize(metadata));
    }

    private async Task<ResourceWrapper?> ReadFromNdjsonAsync(
        FileLocation location,
        ResourceKey key,
        CancellationToken ct)
    {
        using var reader = new StreamReader(location.FilePath);

        // Skip line 1 (Bundle metadata)
        await reader.ReadLineAsync();

        // Read resources until we find the one we want
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var resource = DeserializeResource(line);
            if (resource.ResourceId == key.Id)
            {
                return resource;
            }
        }

        return null;
    }

    private string GetDirectoryPath(string resourceType, DateTimeOffset timestamp) =>
        Path.Combine(
            _basePath,
            resourceType,
            timestamp.ToString("yyyy"),
            timestamp.ToString("MM"),
            timestamp.ToString("dd"));
}

public record FileLocation(
    string FilePath,
    TransactionId TransactionId,
    DateTimeOffset Timestamp);

public class ResourceMetadata
{
    public required ResourceKey ResourceKey { get; init; }
    public required TransactionId TransactionId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    // Request information (from legacy data layers)
    public required string RequestMethod { get; init; }     // POST, PUT, DELETE, etc.
    public required string RequestUrl { get; init; }        // Patient, Patient/123, etc.
    public string? IfMatch { get; init; }                   // ETag for conditional requests
    public string? IfNoneExist { get; init; }               // Conditional create
    public string? IfModifiedSince { get; init; }           // Conditional read

    public required List<SearchIndexMetadata> SearchIndices { get; init; }
}

public class SearchIndexMetadata
{
    public required string SearchParameterName { get; init; }
    public required SearchParamType SearchParameterType { get; init; }
    public required object Value { get; init; } // Serialized ISearchValue
}
```

---

### 2. InMemoryIndex (from microsoft/fhir-server)

**Source**: `InMemoryIndex.cs` from subscription-engine branch

This is the exact implementation from microsoft/fhir-server, extended with file location tracking.

```csharp
public class InMemoryIndex
{
    private readonly ISearchIndexer _searchIndexer;

    // Core index structure (from microsoft/fhir-server)
    internal ConcurrentDictionary<string, List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)>> Index { get; }

    // Extension: Track file locations for retrieval
    private readonly ConcurrentDictionary<ResourceKey, FileLocation> _resourceLocations = new();

    public InMemoryIndex(ISearchIndexer searchIndexer)
    {
        _searchIndexer = EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
        Index = new ConcurrentDictionary<string, List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>>();
    }

    public void IndexResources(params ResourceElement[] resources)
    {
        EnsureArg.IsNotNull(resources, nameof(resources));

        foreach (var resource in resources)
        {
            var indexEntries = _searchIndexer.Extract(resource);

            Index.AddOrUpdate(
                resource.InstanceType,
                key => new List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>
                {
                    (ToResourceKey(resource), indexEntries)
                },
                (key, list) =>
                {
                    list.Add((ToResourceKey(resource), indexEntries));
                    return list;
                });
        }
    }

    // Extension: Load from metadata sidecar files
    public void LoadFromMetadata(ResourceMetadata metadata, FileLocation location)
    {
        var resourceKey = metadata.ResourceKey;

        // Track file location
        _resourceLocations[resourceKey] = location;

        // Deserialize search indices
        var searchEntries = metadata.SearchIndices.Select(m => new SearchIndexEntry
        {
            SearchParameter = new SearchParameterInfo(m.SearchParameterName, m.SearchParameterType),
            Value = DeserializeSearchValue(m.Value, m.SearchParameterType)
        }).ToList();

        // Add to index
        Index.AddOrUpdate(
            resourceKey.ResourceType,
            key => new List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>
            {
                (resourceKey, searchEntries)
            },
            (key, list) =>
            {
                list.Add((resourceKey, searchEntries));
                return list;
            });
    }

    public FileLocation? GetResourceLocation(ResourceKey key)
    {
        return _resourceLocations.TryGetValue(key, out var location) ? location : null;
    }

    public IEnumerable<(ResourceKey Key, IReadOnlyCollection<SearchIndexEntry> Index)> GetResourcesWithIndices(
        string resourceType)
    {
        return Index.TryGetValue(resourceType, out var list)
            ? list
            : Enumerable.Empty<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>();
    }

    private static ResourceKey ToResourceKey(ResourceElement resource)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));
        return new ResourceKey(resource.InstanceType, resource.Id, resource.VersionId);
    }
}
```

---

### 3. ComparisonValueVisitor (from microsoft/fhir-server)

**Source**: `ComparisonValueVisitor.cs` from subscription-engine branch

This visitor handles type-specific comparisons for all FHIR search value types.

```csharp
internal class ComparisonValueVisitor : ISearchValueVisitor
{
    private readonly BinaryOperator _expressionBinaryOperator;
    private readonly IComparable _second;
    private readonly List<Func<bool>> _comparisonValues = [];

    public ComparisonValueVisitor(BinaryOperator expressionBinaryOperator, IComparable second)
    {
        _expressionBinaryOperator = expressionBinaryOperator;
        _second = EnsureArg.IsNotNull(second, nameof(second));
    }

    public void Visit(CompositeSearchValue composite)
    {
        foreach (IReadOnlyList<ISearchValue> c in composite.Components)
        {
            foreach (ISearchValue inner in c)
            {
                inner.AcceptVisitor(this);
            }
        }
    }

    public void Visit(DateTimeSearchValue dateTime)
    {
        EnsureArg.IsNotNull(dateTime, nameof(dateTime));
        AddComparison(_expressionBinaryOperator, dateTime.Start);
    }

    public void Visit(NumberSearchValue number)
    {
        EnsureArg.IsNotNull(number, nameof(number));
        AddComparison(_expressionBinaryOperator, number.High);
    }

    public void Visit(QuantitySearchValue quantity)
    {
        EnsureArg.IsNotNull(quantity, nameof(quantity));
        AddComparison(_expressionBinaryOperator, quantity.High);
    }

    public void Visit(ReferenceSearchValue reference)
    {
        EnsureArg.IsNotNull(reference, nameof(reference));
        AddComparison(_expressionBinaryOperator, reference.ResourceId);
    }

    public void Visit(StringSearchValue s)
    {
        EnsureArg.IsNotNull(s, nameof(s));
        AddComparison(_expressionBinaryOperator, s.String);
    }

    public void Visit(TokenSearchValue token)
    {
        EnsureArg.IsNotNull(token, nameof(token));
        AddComparison(_expressionBinaryOperator, token.Text, token.System, token.Code);
    }

    public void Visit(UriSearchValue uri)
    {
        EnsureArg.IsNotNull(uri, nameof(uri));
        AddComparison(_expressionBinaryOperator, uri.Uri);
    }

    private void AddComparison(BinaryOperator binaryOperator, params IComparable[] first)
    {
        EnsureArg.IsNotNull(first, nameof(first));

        switch (binaryOperator)
        {
            case BinaryOperator.Equal:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) == 0));
                break;
            case BinaryOperator.GreaterThan:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) > 0));
                break;
            case BinaryOperator.LessThan:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) < 0));
                break;
            case BinaryOperator.NotEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) != 0));
                break;
            case BinaryOperator.GreaterThanOrEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) >= 0));
                break;
            case BinaryOperator.LessThanOrEqual:
                _comparisonValues.Add(() => first.Any(x => x.CompareTo(_second) <= 0));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(binaryOperator));
        }
    }

    public bool Compare()
    {
        return _comparisonValues.All(x => x.Invoke());
    }
}
```

---

### 4. SearchQueryInterpreter Implementation

**Source**: `SearchQueryInterpreter.cs` from subscription-engine branch

```csharp
public delegate IEnumerable<ResourceKey> SearchPredicate(
    IEnumerable<(ResourceKey Key, IReadOnlyCollection<SearchIndexEntry> Index)> input);

public class SearchQueryInterpreter :
    IExpressionVisitorWithInitialContext<SearchQueryInterpreter.Context, SearchPredicate>
{
    Context IExpressionVisitorWithInitialContext<Context, SearchPredicate>.InitialContext =>
        default;

    public SearchPredicate VisitSearchParameter(
        SearchParameterExpression expression,
        Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        var newContext = context.WithParameterName(expression.Parameter.Name);
        return expression.Expression.AcceptVisitor(this, newContext);
    }

    public SearchPredicate VisitBinary(BinaryExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        return input => input
            .Where(x => x.Index.Any(idx =>
                idx.SearchParameter.Name == context.ParameterName &&
                CompareBinary(expression.BinaryOperator, idx.Value, expression.Value)))
            .Select(x => x.Key);
    }

    private static bool CompareBinary(BinaryOperator op, ISearchValue first, object second)
    {
        if (first == null || second == null) return false;

        var visitor = new ComparisonValueVisitor(op, (IComparable)second);
        first.AcceptVisitor(visitor);
        return visitor.Compare();
    }

    public SearchPredicate VisitMultiary(MultiaryExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        var predicates = expression.Expressions
            .Select(expr => expr.AcceptVisitor(this, context))
            .ToList();

        return input =>
        {
            IEnumerable<ResourceKey>? result = null;

            foreach (var predicate in predicates)
            {
                var current = predicate(input).ToList();

                result = expression.MultiaryOperation switch
                {
                    MultiaryOperator.And => result == null ? current : result.Intersect(current),
                    MultiaryOperator.Or => result == null ? current : result.Union(current),
                    _ => throw new NotSupportedException($"Operator {expression.MultiaryOperation} not supported")
                };
            }

            return result ?? Enumerable.Empty<ResourceKey>();
        };
    }

    public SearchPredicate VisitString(StringExpression expression, Context context)
    {
        EnsureArg.IsNotNull(expression, nameof(expression));

        var comparison = expression.IgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        // Special handling for _type parameter
        if (context.ParameterName == "_type")
        {
            return input => input
                .Where(x => x.Key.ResourceType.Equals(expression.Value, comparison))
                .Select(x => x.Key);
        }

        return expression.StringOperator switch
        {
            StringOperator.Equals => input => input
                .Where(x => x.Index.Any(idx =>
                    idx.SearchParameter.Name == context.ParameterName &&
                    CompareString(idx, expression.Value, string.Equals, comparison)))
                .Select(x => x.Key),

            StringOperator.StartsWith => input => input
                .Where(x => x.Index.Any(idx =>
                    idx.SearchParameter.Name == context.ParameterName &&
                    CompareString(idx, expression.Value,
                        (a, b, c) => a.StartsWith(b, c), comparison)))
                .Select(x => x.Key),

            StringOperator.Contains => input => input
                .Where(x => x.Index.Any(idx =>
                    idx.SearchParameter.Name == context.ParameterName &&
                    CompareString(idx, expression.Value,
                        (a, b, c) => a.Contains(b, c), comparison)))
                .Select(x => x.Key),

            _ => throw new NotSupportedException($"String operator {expression.StringOperator} not supported")
        };
    }

    private static bool CompareString(
        SearchIndexEntry entry,
        string value,
        Func<string, string, StringComparison, bool> compareFunc,
        StringComparison comparison)
    {
        return entry.SearchParameter.Type switch
        {
            SearchParamType.String when entry.Value is StringSearchValue str =>
                compareFunc(str.String, value, comparison),

            SearchParamType.Token when entry.Value is TokenSearchValue token =>
                compareFunc(token.Code, value, comparison) ||
                (token.System != null && compareFunc(token.System, value, comparison)),

            _ => false
        };
    }

    public SearchPredicate VisitChained(ChainedExpression expression, Context context)
    {
        // Chained search deferred to Phase 3
        throw new SearchOperationNotSupportedException("Chained search not supported in Phase 1");
    }

    public SearchPredicate VisitMissingField(MissingFieldExpression expression, Context context)
    {
        // :missing modifier deferred to Phase 2
        throw new SearchOperationNotSupportedException("Missing field search not supported in Phase 1");
    }

    // ... other visit methods similar to original implementation

    public struct Context
    {
        public string ParameterName { get; set; }

        public Context WithParameterName(string paramName) =>
            new Context { ParameterName = paramName };
    }
}
```

---

### 5. Search Service Integration

```csharp
public class FileBasedSearchService : ISearchService
{
    private readonly InMemorySearchIndex _searchIndex;
    private readonly ISearchOptionsFactory _searchOptionsFactory;
    private readonly SearchQueryInterpreter _interpreter;
    private readonly IFhirRepository _repository;

    public async ValueTask<SearchResult> SearchAsync(
        string resourceType,
        IReadOnlyDictionary<string, StringValues> queryParameters,
        int? count = null,
        string? continuationToken = null,
        CancellationToken ct = default)
    {
        // 1. Parse search parameters into expression tree
        var searchOptions = _searchOptionsFactory.Create(resourceType, queryParameters);
        var expression = searchOptions.Expression;

        // 2. Get all resources with their search indices
        var resourcesWithIndices = _searchIndex.GetResourcesWithIndices(resourceType);

        // 3. Apply SearchQueryInterpreter to filter
        var predicate = expression.AcceptVisitor(_interpreter, default);
        var matchingKeys = predicate(resourcesWithIndices).ToList();

        // 4. Apply pagination
        var skip = ParseContinuationToken(continuationToken);
        var take = count ?? 10;
        var page = matchingKeys.Skip(skip).Take(take).ToList();

        // 5. Load full resources from files
        var resources = new List<ResourceWrapper>();
        foreach (var key in page)
        {
            var resource = await _repository.GetAsync(key, ct);
            if (resource != null)
            {
                resources.Add(resource);
            }
        }

        // 6. Generate continuation token
        var nextToken = matchingKeys.Count > skip + take
            ? GenerateContinuationToken(skip + take)
            : null;

        return new SearchResult(
            resources,
            matchingKeys.Count,
            nextToken);
    }

    private static int ParseContinuationToken(string? token) =>
        int.TryParse(token, out var skip) ? skip : 0;

    private static string GenerateContinuationToken(int skip) =>
        skip.ToString();
}
```

---

### 6. Startup Index Loading from Metadata Files

**Key Optimization**: Load search indices from `.metadata.ndjson` sidecar files instead of re-extracting from resources.

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
        var resourceCount = 0;

        // Walk directory structure
        var resourceDirs = Directory.GetDirectories(_basePath);

        foreach (var resourceDir in resourceDirs)
        {
            var resourceType = Path.GetFileName(resourceDir);

            // Find all .metadata.ndjson files (NOT .ndjson resource files)
            var metadataFiles = Directory.GetFiles(
                resourceDir,
                "*.metadata.ndjson",
                SearchOption.AllDirectories);

            foreach (var metadataFile in metadataFiles)
            {
                // Get corresponding resource file path
                var resourceFile = metadataFile.Replace(".metadata.ndjson", ".ndjson");

                await LoadMetadataFileAsync(metadataFile, resourceFile, resourceType, cancellationToken);
                resourceCount++;
            }
        }

        sw.Stop();
        _logger.LogInformation(
            "Search index loaded: {ResourceCount} transaction files in {Duration}ms",
            resourceCount,
            sw.ElapsedMilliseconds);
    }

    private async Task LoadMetadataFileAsync(
        string metadataPath,
        string resourcePath,
        string resourceType,
        CancellationToken ct)
    {
        using var reader = new StreamReader(metadataPath);

        // Read metadata lines (one per resource in transaction)
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var metadata = JsonSerializer.Deserialize<ResourceMetadata>(line);
            if (metadata == null) continue;

            // Extract file location from path
            var location = new FileLocation(
                resourcePath,
                metadata.TransactionId,
                metadata.Timestamp);

            // Load into InMemoryIndex (no re-extraction needed!)
            _searchIndex.LoadFromMetadata(metadata, location);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

**Performance Benefits**:
- **10x faster startup**: No FHIRPath evaluation or search parameter extraction
- **Lower CPU**: Simple JSON deserialization only
- **Consistent indices**: Same search values used at write time
- **Idempotent**: Can reload index without changing behavior

---

## Implementation Plan

### Prototype Phase: Vertical Slice (Week 1)

**Goal**: Complete vertical slice from HTTP to storage with PUT and GET operations.

```csharp
// Deliverables:
✅ Project structure with all layers
✅ FileBasedFhirRepository with NDJSON + metadata sidecar files
✅ InMemoryIndex (basic implementation)
✅ PUT /Patient/{id} - Create/Update operation vertically integrated
✅ GET /Patient/{id} - Read operation vertically integrated
✅ 80% test coverage for implemented operations
```

**Architecture Layers**:
1. **API Layer**: ASP.NET Core controllers with FHIR endpoints
2. **Application Layer**: Command/query handlers (Medino)
3. **Domain Layer**: Resource models and business logic
4. **Infrastructure Layer**: FileBasedFhirRepository + InMemoryIndex
5. **Tests**: xUnit + NSubstitute with 80% coverage

**File System**:
```
/data/Patient/2025/01/15/
  tx-1234567890.ndjson           # Resource data (Bundle + Patient)
  tx-1234567890.metadata.ndjson  # Search indices (pre-extracted)
```

**Example Metadata File** (`tx-1234567890.metadata.ndjson`):
```json
{"resourceKey":{"resourceType":"Patient","id":"example","versionId":"1"},"transactionId":"1234567890","timestamp":"2025-01-15T10:30:00Z","requestMethod":"PUT","requestUrl":"Patient/example","ifMatch":null,"ifNoneExist":null,"ifModifiedSince":null,"searchIndices":[{"searchParameterName":"family","searchParameterType":"String","value":{"string":"Smith"}},{"searchParameterName":"given","searchParameterType":"String","value":{"string":"John"}},{"searchParameterName":"birthdate","searchParameterType":"Date","value":{"start":"1980-01-01T00:00:00Z","end":"1980-01-01T23:59:59Z"}},{"searchParameterName":"identifier","searchParameterType":"Token","value":{"system":"http://example.org/mrn","code":"12345","text":"12345"}}]}
```

---

### Phase 1.1: Bundle Processing (Week 2)

**Goal**: Add Bundle transaction support with multiple operations.

```csharp
// Deliverables:
✅ Bundle transaction processing (POST, PUT, GET, DELETE)
✅ Reference resolution for Bundle entries
✅ Atomic transaction commits
✅ System.Threading.Channels for parallel execution
✅ 80% test coverage
```

---

### Phase 1.2: Search Implementation (Week 3)

**Goal**: Complete InMemory search architecture from microsoft/fhir-server.

```csharp
// Deliverables:
✅ Complete SearchQueryInterpreter from subscription-engine branch
✅ ComparisonValueVisitor for search value comparisons
✅ InMemoryIndex.LoadFromMetadata() for fast startup
✅ FileBasedSearchService with pagination
✅ IndexLoaderService loading from .metadata.ndjson files
✅ 80% test coverage
```

---

### Phase 1.3: Search Parameter Types (Week 4)

**Goal**: Support all basic search parameter types.

```csharp
// Deliverables:
✅ String search (name, family, given)
✅ Token search (identifier, code)
✅ Date search (birthdate, date)
✅ Number search (quantity values)
✅ Reference search (subject, patient)
✅ _type, _id, _lastUpdated parameters
✅ 80% test coverage
```

---

## E2E Test Success Criteria

**From src-old/test**: Phase 1 must pass these tests:

✅ **CreateTests.cs**:
- Create Patient with server-assigned ID
- Create Patient with client-assigned ID
- POST returns 201 Created with Location header
- Response includes ETag, Last-Modified

✅ **ReadTests.cs**:
- GET /Patient/{id} returns 200 OK
- GET /Patient/{id} with valid resource
- GET /Patient/{nonexistent} returns 404

✅ **UpdateTests.cs**:
- PUT /Patient/{id} updates resource
- Version increments correctly
- ETag updated

✅ **DeleteTests.cs**:
- DELETE /Patient/{id} soft deletes
- Deleted resource returns 410 Gone

✅ **Search/BasicSearchTests.cs**:
- Search by name
- Search by multiple parameters (AND)
- Search returns Bundle with correct type

✅ **Search/StringSearchTests.cs**:
- String search with :exact modifier
- String search with :contains modifier
- Case-insensitive search (default)

✅ **Search/TokenSearchTests.cs**:
- Token search by identifier

✅ **Search/DateSearchTests.cs**:
- Date search with birthdate

---

## Performance Targets

| Operation | Target | Measurement |
|-----------|--------|-------------|
| Create | <10ms | File write + index update |
| Read (by ID) | <5ms | Direct file read via index |
| Update | <12ms | File write + index update |
| Delete | <8ms | Mark deleted in index |
| Search (10 results) | <20ms | In-memory index scan + file reads |
| Search (1000 results) | <100ms | In-memory scan, paginated reads |
| Startup index load (10K resources) | <5s | Full directory scan + indexing |

---

## Storage Characteristics

**For 10,000 Patient resources**:
```
Directory structure:
/data/Patient/
  2025/
    01/
      15/ (500 files)
      16/ (500 files)
      ...

File size: ~4KB avg (Bundle + Patient NDJSON)
Total storage: 40MB uncompressed, ~12MB gzipped
Index memory: ~5MB in RAM (search indices)
Startup time: ~3s (load + index)
```

---

## Migration to Production Storage

File-based implementation establishes patterns for:

1. **SQL Server (Phase 10)**:
   - Same SearchQueryInterpreter
   - Same search expression tree
   - SQL-based search indices instead of in-memory

2. **Cosmos DB (Phase 11)**:
   - Same SearchQueryInterpreter
   - Same search expression tree
   - Cosmos query translation layer

3. **In-Memory (always available)**:
   - Testing and development
   - Same patterns as file-based

**Key Insight**: SearchQueryInterpreter pattern works across ALL storage providers by separating query interpretation from storage implementation.

---

## Benefits of This Approach

1. ✅ **F5 Experience**: Zero setup, file-based storage
2. ✅ **Real Search**: Full SearchQueryInterpreter from microsoft/fhir-server
3. ✅ **Production Patterns**: Establishes abstractions for all providers
4. ✅ **Fast Development**: In-memory index = fast searches
5. ✅ **Easy Testing**: File-based = easy to inspect and debug
6. ✅ **Proven Design**: Based on working microsoft/fhir-server code
7. ✅ **Reusable**: Same patterns migrate to SQL/Cosmos

---

## Next Steps

### Immediate: Prototype Phase (Week 1)

1. **Setup project structure**: All layers (API, Application, Domain, Infrastructure, Tests)
2. **Implement PUT /Patient/{id}** vertically through all layers:
   - API controller
   - Medino command handler
   - FileBasedFhirRepository.CreateAsync()
   - Write `.ndjson` + `.metadata.ndjson` files
   - Update InMemoryIndex
3. **Implement GET /Patient/{id}** vertically:
   - API controller
   - Medino query handler
   - FileBasedFhirRepository.GetAsync()
   - Lookup in InMemoryIndex
   - Read from `.ndjson` file
4. **80% test coverage**: xUnit + NSubstitute

### Week 2: Bundle Processing

1. Implement Bundle transaction support
2. Add System.Threading.Channels for parallel execution
3. Reference resolution and atomic commits

### Week 3: Search Implementation

1. Complete SearchQueryInterpreter from microsoft/fhir-server
2. Add ComparisonValueVisitor
3. Implement IndexLoaderService with metadata loading

### Week 4: Search Parameter Types

1. Support String, Token, Date, Number, Reference search
2. Pass all Phase 1 E2E tests

**Estimated**: 25 Claude Code hours for complete Phase 1 implementation
