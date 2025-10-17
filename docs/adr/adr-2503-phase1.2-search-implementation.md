# ADR 2503: Phase 1.2 - Search Implementation with InMemory Architecture

## Status

Proposed

## Context

Following Bundle processing in Phase 1.1, we add complete search functionality using the proven InMemory search architecture from microsoft/fhir-server. This phase also introduces the Capability Statement service and basic authorization.

**Related Investigations**:
- `phase1-file-based-storage-with-search.md` - InMemory search architecture from microsoft/fhir-server
- `search-query-parsing.md` - Simplified SearchOptionsBuilder (250 lines vs 800-line legacy factory)
- `bundle-streaming.md` - IAsyncEnumerable + FhirJsonWriter for streaming Bundle responses (95% memory reduction)
- `dynamic-capability-statement-generation.md` - Segmented capability statement with caching
- `rbac-authorization-with-capability-enforcement.md` - 5-layer authorization pipeline

### Key Innovation: Reuse microsoft/fhir-server InMemory Search

**Problem**: Search is complex - expression parsing, type-specific comparisons, indexing strategy.

**Solution**: Port the battle-tested InMemory search from microsoft/fhir-server `feature/subscription-engine` branch:

**Components**:
1. `SearchQueryInterpreter.cs` - Expression visitor pattern for search parameter parsing
2. `ComparisonValueVisitor.cs` - Type-specific value comparisons (string, token, date, number, reference)
3. `InMemoryIndex.cs` - Optimized index structure with grouped lookups

**Why Reuse?**
- ✅ Proven in production microsoft/fhir-server
- ✅ Handles edge cases (case-insensitive string, partial dates, ranges)
- ✅ Performance optimized (10x faster than re-extracting on every search)
- ✅ Supports all FHIR search parameter types

**Source**: `docs/investigations/phase1-file-based-storage-with-search.md`

### Additional Deliverables

**Capability Statement Service** (from `dynamic-capability-statement-generation.md`):
- Segmented architecture for efficient caching
- Version hash tracking for change detection
- Dynamic refresh when search parameters added

**Authorization Middleware** (from `rbac-authorization-with-capability-enforcement.md`):
- Basic authentication handler
- Foundation for 5-layer authorization pipeline (full implementation in Phase 6, 10)

## Decision

Implement search functionality using:
1. **microsoft/fhir-server InMemory search** architecture (ported to Ignixa)
2. **Segmented CapabilityStatement** service
3. **Basic authorization** middleware (authentication only)

### Architecture

#### 1. InMemory Search Index

**File Structure**:
```
Ignixa.Api/
  Features/
    Search/
      SearchEndpoints.cs           # GET {resourceType}?name=value
      SearchHandler.cs             # Medino handler
      InMemory/
        InMemorySearchService.cs   # Main search orchestrator
        InMemoryIndex.cs           # Index structure
        SearchQueryInterpreter.cs  # Expression visitor
        ComparisonValueVisitor.cs  # Type comparisons
      IndexLoading/
        IndexLoaderService.cs      # Load from .metadata.ndjson
        SearchIndexBuilder.cs      # Extract search values
```

**Core Abstractions**:
```csharp
// Ignixa.Api/Features/Search/Abstractions/ISearchService.cs
namespace Ignixa.Features.Search.Abstractions;

public interface ISearchService
{
    ValueTask<SearchResult> SearchAsync(
        string resourceType,
        IReadOnlyDictionary<string, string> searchParams,
        CancellationToken ct = default);
}

public record SearchResult(
    IReadOnlyList<ResourceWrapper> Resources,
    string? ContinuationToken,
    int Total);

// Ignixa.Api/Features/Search/InMemory/InMemorySearchService.cs
namespace Ignixa.Features.Search.InMemory;

public class InMemorySearchService : ISearchService
{
    private readonly InMemoryIndex _index;
    private readonly IFhirSchemaProvider _schemaProvider;

    public async ValueTask<SearchResult> SearchAsync(
        string resourceType,
        IReadOnlyDictionary<string, string> searchParams,
        CancellationToken ct)
    {
        // 1. Parse search parameters
        var parsedParams = ParseSearchParameters(resourceType, searchParams);

        // 2. Query index using SearchQueryInterpreter
        var matchingResourceIds = await _index.SearchAsync(
            resourceType,
            parsedParams,
            ct);

        // 3. Load resources
        var resources = await LoadResourcesAsync(matchingResourceIds, ct);

        // 4. Apply pagination
        return ApplyPagination(resources, searchParams);
    }
}
```

#### 2. SearchQueryInterpreter Pattern

**Ported from microsoft/fhir-server**:

```csharp
// Ignixa.Api/Features/Search/InMemory/SearchQueryInterpreter.cs
namespace Ignixa.Features.Search.InMemory;

/// <summary>
/// Expression visitor that converts FHIR search parameters to index queries
/// Ported from microsoft/fhir-server feature/subscription-engine branch
/// </summary>
public class SearchQueryInterpreter
{
    private readonly InMemoryIndex _index;

    public IEnumerable<string> Visit(SearchParameterExpression expression)
    {
        return expression switch
        {
            StringSearchExpression str => VisitString(str),
            TokenSearchExpression token => VisitToken(token),
            DateSearchExpression date => VisitDate(date),
            NumberSearchExpression num => VisitNumber(num),
            ReferenceSearchExpression @ref => VisitReference(@ref),
            _ => throw new NotSupportedException($"Search parameter type {expression.GetType().Name} not supported")
        };
    }

    private IEnumerable<string> VisitString(StringSearchExpression expression)
    {
        // Use InMemoryIndex for case-insensitive lookup
        return _index.SearchString(
            expression.ResourceType,
            expression.ParameterName,
            expression.Value,
            expression.Modifier);
    }

    private IEnumerable<string> VisitToken(TokenSearchExpression expression)
    {
        // Search by system|code
        return _index.SearchToken(
            expression.ResourceType,
            expression.ParameterName,
            expression.System,
            expression.Code);
    }

    // ... other visitors
}
```

#### 3. InMemoryIndex Structure

**Ported from microsoft/fhir-server**:

```csharp
// Ignixa.Api/Features/Search/InMemory/InMemoryIndex.cs
namespace Ignixa.Features.Search.InMemory;

/// <summary>
/// Optimized in-memory search index with grouped lookups
/// Ported from microsoft/fhir-server feature/subscription-engine branch
/// </summary>
public class InMemoryIndex
{
    // Key: {ResourceType}:{ParamName}:{NormalizedValue} -> List<ResourceId>
    private readonly ConcurrentDictionary<string, List<string>> _stringIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _tokenIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _numberIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _dateIndex = new();
    private readonly ConcurrentDictionary<string, List<string>> _referenceIndex = new();

    public IEnumerable<string> SearchString(
        string resourceType,
        string paramName,
        string value,
        StringModifier modifier)
    {
        // Normalize value (lowercase, trim)
        var normalizedValue = NormalizeString(value);

        // Build index key
        var keyPrefix = $"{resourceType}:{paramName}:";

        return modifier switch
        {
            StringModifier.Exact => SearchExact(keyPrefix, normalizedValue),
            StringModifier.Contains => SearchContains(keyPrefix, normalizedValue),
            _ => SearchStartsWith(keyPrefix, normalizedValue) // Default
        };
    }

    private IEnumerable<string> SearchStartsWith(string keyPrefix, string value)
    {
        // Find all keys that start with value
        return _stringIndex
            .Where(kvp => kvp.Key.StartsWith(keyPrefix) &&
                         kvp.Key.Substring(keyPrefix.Length).StartsWith(value))
            .SelectMany(kvp => kvp.Value)
            .Distinct();
    }

    public IEnumerable<string> SearchToken(
        string resourceType,
        string paramName,
        string? system,
        string code)
    {
        // Key format: ResourceType:ParamName:system|code
        var key = system != null
            ? $"{resourceType}:{paramName}:{system}|{code}"
            : $"{resourceType}:{paramName}:|{code}";  // No system

        return _tokenIndex.TryGetValue(key, out var resourceIds)
            ? resourceIds
            : Enumerable.Empty<string>();
    }

    // Add/Remove methods for index maintenance
    public void AddStringIndex(string resourceType, string resourceId, string paramName, string value)
    {
        var normalized = NormalizeString(value);
        var key = $"{resourceType}:{paramName}:{normalized}";

        _stringIndex.AddOrUpdate(
            key,
            _ => new List<string> { resourceId },
            (_, list) => { list.Add(resourceId); return list; });
    }

    private static string NormalizeString(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
```

#### 4. Index Loading from Metadata Sidecar

```csharp
// Ignixa.Api/Features/Search/IndexLoading/IndexLoaderService.cs
namespace Ignixa.Features.Search.IndexLoading;

public class IndexLoaderService
{
    private readonly InMemoryIndex _index;
    private readonly string _dataPath;

    public async ValueTask LoadAllIndicesAsync(CancellationToken ct)
    {
        // Scan for all .metadata.ndjson files
        var metadataFiles = Directory.GetFiles(
            _dataPath,
            "*.metadata.ndjson",
            SearchOption.AllDirectories);

        foreach (var file in metadataFiles)
        {
            await LoadIndicesFromFileAsync(file, ct);
        }
    }

    private async ValueTask LoadIndicesFromFileAsync(string metadataPath, CancellationToken ct)
    {
        // Read .metadata.ndjson
        var lines = await File.ReadAllLinesAsync(metadataPath, ct);
        var metadata = JsonSerializer.Deserialize<ResourceMetadata>(lines[0]);

        // Add to index
        foreach (var (paramName, values) in metadata.SearchIndices)
        {
            var paramInfo = _searchParameterProvider.GetSearchParameter(
                metadata.ResourceType,
                paramName);

            if (paramInfo == null)
                continue;

            foreach (var value in values)
            {
                AddToIndex(metadata.ResourceType, metadata.Id, paramInfo, value);
            }
        }
    }

    private void AddToIndex(
        string resourceType,
        string resourceId,
        SearchParameterInfo paramInfo,
        object value)
    {
        switch (paramInfo.Type)
        {
            case SearchParamType.String:
                _index.AddStringIndex(resourceType, resourceId, paramInfo.Code, value.ToString()!);
                break;

            case SearchParamType.Token:
                var (system, code) = ParseToken(value);
                _index.AddTokenIndex(resourceType, resourceId, paramInfo.Code, system, code);
                break;

            // ... other types
        }
    }
}
```

#### 5. Capability Statement Service

**From `dynamic-capability-statement-generation.md`**:

```csharp
// Ignixa.Api/Features/Metadata/CapabilityStatementService.cs
namespace Ignixa.Features.Metadata;

public interface ICapabilityStatementService
{
    ValueTask<CapabilityStatement> GetCapabilityStatementAsync(
        FhirRequestContext context,
        CancellationToken ct = default);
}

public class CapabilityStatementService : ICapabilityStatementService
{
    private readonly IEnumerable<ICapabilitySegment> _segments;
    private readonly IMemoryCache _cache;

    public async ValueTask<CapabilityStatement> GetCapabilityStatementAsync(
        FhirRequestContext context,
        CancellationToken ct)
    {
        var cacheKey = $"capability:{context.TenantId}:{context.Version}";

        if (_cache.TryGetValue<CapabilityStatement>(cacheKey, out var cached))
        {
            return cached;
        }

        // Build capability statement from segments
        var builder = new CapabilityStatementBuilder(context.Version);
        var capabilityContext = new CapabilityContext(
            context.Version,
            context.TenantId);

        foreach (var segment in _segments.OrderBy(s => s.Priority))
        {
            await segment.ApplyAsync(builder, capabilityContext, ct);
        }

        var capability = builder.Build();

        // Cache with version hash for invalidation
        var versionHash = await ComputeVersionHashAsync(capabilityContext, ct);
        _cache.Set(cacheKey, capability, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            Size = 1
        });

        return capability;
    }

    private async ValueTask<string> ComputeVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        var hashes = new List<string>();

        foreach (var segment in _segments)
        {
            var hash = await segment.GetVersionHashAsync(context, ct);
            hashes.Add(hash);
        }

        var combined = string.Join("|", hashes);
        return HashHelper.ComputeSHA256(combined);
    }
}

// Ignixa.Api/Features/Metadata/Segments/StaticCapabilitySegment.cs
public class StaticCapabilitySegment : ICapabilitySegment
{
    public string SegmentKey => "Static";
    public int Priority => 10;

    public ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        builder.SetSoftware("Ignixa FHIR Server", "0.1.0");
        builder.SetImplementation("Ignixa", "https://sparky.example.com");
        builder.SetFhirVersion(context.FhirVersion);

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Static content never changes
        return ValueTask.FromResult("static-v1");
    }
}

// Ignixa.Api/Features/Metadata/Segments/ResourceInteractionSegment.cs
public class ResourceInteractionCapabilitySegment : ICapabilitySegment
{
    public string SegmentKey => "ResourceInteractions";
    public int Priority => 20;

    public ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        // Patient resource (from Prototype)
        builder.AddResource("Patient", resource =>
        {
            resource.AddInteraction(TypeRestfulInteraction.Read);
            resource.AddInteraction(TypeRestfulInteraction.Update);
            resource.AddInteraction(TypeRestfulInteraction.SearchType);
        });

        // Add search parameters (Phase 1.2)
        builder.AddSearchParameter("Patient", new CapabilitySearchParam
        {
            Name = "name",
            Definition = "http://hl7.org/fhir/SearchParameter/Patient-name",
            Type = SearchParamType.String,
            Documentation = "A server defined search that may match any of the string fields in the HumanName"
        });

        return ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Quasi-static: changes when new resource types added
        var data = "Patient:read,update,search-type|name";
        return ValueTask.FromResult(HashHelper.ComputeSHA256(data));
    }
}
```

#### 6. Authorization Middleware

**From `rbac-authorization-with-capability-enforcement.md`**:

```csharp
// Ignixa.Api/Features/Authorization/FhirAuthorizationMiddleware.cs
namespace Ignixa.Features.Authorization;

public class FhirAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IEnumerable<IAuthorizationHandler> _handlers;

    public async Task InvokeAsync(HttpContext context, FhirRequestContext fhirContext)
    {
        // Build authorization context
        var authContext = new FhirAuthorizationContext
        {
            UserId = context.User.Identity?.Name,
            TenantId = fhirContext.TenantId,
            Roles = context.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToArray(),
            Interaction = GetInteraction(context.Request.Method, context.GetRouteValue("id")),
            ResourceType = context.GetRouteValue("resourceType")?.ToString(),
            HttpContext = context
        };

        // Execute authorization pipeline (ordered by Priority)
        foreach (var handler in _handlers.OrderBy(h => h.Priority))
        {
            var result = await handler.HandleAsync(authContext, context.RequestAborted);

            if (!result.Allowed)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new OperationOutcome
                {
                    Issue = new List<OperationOutcome.IssueComponent>
                    {
                        new()
                        {
                            Severity = OperationOutcome.IssueSeverity.Error,
                            Code = OperationOutcome.IssueType.Forbidden,
                            Diagnostics = result.Reason
                        }
                    }
                });
                return;
            }
        }

        await _next(context);
    }

    private static FhirInteraction GetInteraction(string method, object? id)
    {
        return (method, id) switch
        {
            ("GET", not null) => FhirInteraction.Read,
            ("GET", null) => FhirInteraction.Search,
            ("PUT", _) => FhirInteraction.Update,
            ("POST", _) => FhirInteraction.Create,
            ("DELETE", _) => FhirInteraction.Delete,
            _ => FhirInteraction.Unknown
        };
    }
}

// Ignixa.Api/Features/Authorization/Handlers/AuthenticationHandler.cs
public class AuthenticationHandler : IAuthorizationHandler
{
    public int Priority => 10;  // First handler

    public ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken ct)
    {
        // Phase 1.2: Basic authentication (token present?)
        // Full authentication in Phase 6 (Multi-Tenant) and Phase 10 (SMART)

        if (context.HttpContext.User.Identity?.IsAuthenticated == false)
        {
            return ValueTask.FromResult(AuthorizationResult.Deny(
                "Authentication required"));
        }

        return ValueTask.FromResult(AuthorizationResult.Allow());
    }
}
```

### Week 3 Implementation Plan (~16 Claude Code hours)

**NOTE**: Expression tree infrastructure already in place at `Ignixa.Search.Expressions/` (relocated October 2025).
See `docs/investigations/search-query-parsing.md` for query parsing implementation details.

#### 1. Port InMemory Search (8 hours)

**Tasks**:
- Copy SearchQueryInterpreter.cs from microsoft/fhir-server
- Copy ComparisonValueVisitor.cs
- Copy InMemoryIndex.cs
- Adapt to Ignixa namespaces and patterns
- **Use existing** `Ignixa.Search.Expressions.Parsers.ExpressionParser` for query parsing
- **Implement** `SearchOptionsBuilder` (simplified vs legacy SearchOptionsFactory - see `search-query-parsing.md`)
  - 250 lines vs 800-line legacy factory (70% reduction)
  - QueryParameterParser for structured parameter parsing
  - SearchOptionsBuilder for SearchOptions creation
- **Implement** streaming Bundle responses (see `bundle-streaming.md`)
  - IAsyncEnumerable + FhirJsonWriter for 95% memory reduction
  - BundleSerializer for streaming serialization
- Create SearchHandler (Medino)
- Create SearchEndpoints

**Testing**:
- Unit tests for SearchQueryInterpreter
- Unit tests for InMemoryIndex
- Unit tests for SearchOptionsBuilder
- E2E tests: BasicSearchTests.cs, StringSearchTests.cs

#### 2. Index Loading Service (3 hours)

**Tasks**:
- Create IndexLoaderService
- Load indices from .metadata.ndjson files on startup
- Add indices when resources created/updated

**Testing**:
- Unit tests for IndexLoaderService
- Integration test: startup loads all indices

#### 3. Capability Statement Service (3 hours)

**Tasks**:
- Create CapabilityStatementService with segment pattern
- Implement StaticCapabilitySegment
- Implement ResourceInteractionCapabilitySegment
- Add GET /metadata endpoint

**Testing**:
- Unit tests for segments
- E2E test: GET /metadata returns valid CapabilityStatement

#### 4. Authorization Middleware (2 hours)

**Tasks**:
- Create FhirAuthorizationMiddleware
- Implement AuthenticationHandler (basic)
- Register in ASP.NET Core pipeline

**Testing**:
- Unit tests for AuthenticationHandler
- E2E test: Unauthorized request returns 403

## Consequences

### Positive

1. **Proven Architecture**: Reusing microsoft/fhir-server InMemory search reduces risk
2. **Performance**: 10x faster than re-extracting search parameters on every search
3. **Completeness**: SearchQueryInterpreter handles all FHIR edge cases
4. **Extensibility**: Segment pattern makes CapabilityStatement easy to extend
5. **Security Foundation**: Authorization middleware ready for Phase 6, 10 enhancements

### Negative

1. **Code Porting**: Requires careful adaptation from microsoft/fhir-server to Ignixa patterns
2. **Memory Footprint**: InMemory index grows with resource count (mitigated by .metadata.ndjson pre-extraction)
3. **Limited Search Types**: Phase 1.2 only supports string search; other types in Phase 1.3

### Risks

1. **License Compliance**: Ensure microsoft/fhir-server MIT license properly attributed
2. **Index Consistency**: Must keep InMemoryIndex in sync with file storage

## References

- Investigation: `docs/investigations/phase1-file-based-storage-with-search.md` - InMemory search architecture
- Investigation: `docs/investigations/search-query-parsing.md` - **Simplified SearchOptionsBuilder (250 lines vs 800-line legacy)**
- Investigation: `docs/investigations/bundle-streaming.md` - **IAsyncEnumerable + FhirJsonWriter streaming (95% memory reduction)**
- Investigation: `docs/investigations/dynamic-capability-statement-generation.md` - Segmented capability statement
- Investigation: `docs/investigations/rbac-authorization-with-capability-enforcement.md` - 5-layer authorization pipeline
- microsoft/fhir-server InMemory search: https://github.com/microsoft/fhir-server/tree/feature/subscription-engine/src/Microsoft.Health.Fhir.Core/Features/Search/InMemory
- Expression tree classes: `src/Ignixa.Search/Expressions/` - **Relocated October 2025**
- ADR-2500: Master Implementation Roadmap
- ADR-2501: Prototype Phase
- ADR-2502: Phase 1.1 - Bundle Processing

## Next Steps

1. **Begin Phase 1.2 Implementation** (Week 3)
2. **Port InMemory search** from microsoft/fhir-server
3. **Implement Capability Statement** service
4. **Add Authorization** middleware
5. **Phase 1.3**: Extend to all search parameter types (Token, Date, Number, Reference)
