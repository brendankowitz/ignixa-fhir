# Investigation: Dynamic Capability Statement Generation with Smart Caching

## Problem Statement

The legacy FHIR server uses `IProvideCapability` pattern to collect capabilities from various components. This works well for **static capabilities** (CRUD operations, search parameters), but has limitations for **dynamic capabilities** that can change at runtime:

1. **Profile Loading**: When a new ImplementationGuide is loaded (e.g., US Core), its profiles should appear in `CapabilityStatement.rest.resource[].supportedProfile`
2. **Custom Search Parameters**: When custom search parameters are registered dynamically
3. **Feature Toggles**: When features are enabled/disabled via configuration updates
4. **Multi-Tenant**: Different tenants may have different capabilities

**Legacy Challenge**: CapabilityStatement is expensive to build (JSON serialization, FHIRPath evaluation, search parameter resolution). Rebuilding on every request causes performance issues, but caching forever means dynamic changes don't appear.

---

## Legacy Pattern Analysis

### 1. IProvideCapability Pattern

```csharp
// Legacy: Component registers capabilities
public interface IProvideCapability
{
    void Build(ICapabilityStatementBuilder builder);
}

// Example: Search component adds search parameters
public class SearchCapabilityProvider : IProvideCapability
{
    public void Build(ICapabilityStatementBuilder builder)
    {
        builder.SyncSearchParametersAsync();
        builder.AddGlobalSearchParameters();
    }
}
```

**Strengths**:
- ✅ Extensible: New components can contribute capabilities
- ✅ Decoupled: Each component owns its capability definition
- ✅ Discoverable: DI container collects all `IProvideCapability` implementations

**Weaknesses**:
- ❌ Static only: Runs once at startup
- ❌ No change detection: Can't detect when capabilities should be rebuilt
- ❌ All-or-nothing: Can't refresh just one section

### 2. Profile Syncing with ISupportedProfilesStore

```csharp
public interface ISupportedProfilesStore
{
    IEnumerable<string> GetSupportedProfiles(string resourceType, bool disableCacheRefresh = false);
    void Refresh(); // ← Can trigger refresh!
}

// In CapabilityStatementBuilder
public ICapabilityStatementBuilder SyncProfiles(bool disableCacheRefresh = false)
{
    if (!disableCacheRefresh)
    {
        _supportedProfiles.Refresh(); // ← Refresh before syncing
    }

    foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
    {
        SyncProfile(resource, disableCacheRefresh);
    }

    return this;
}
```

**Key Insight**: Legacy DOES support dynamic profile refresh via `Refresh()` method, but:
- Only profiles are refreshable
- Cache invalidation is manual (`disableCacheRefresh` parameter)
- No automatic change detection

### 3. Legacy Caching Strategy

```csharp
public abstract class ConformanceProviderBase : IConformanceProvider
{
    private readonly ConcurrentDictionary<string, bool> _evaluatedQueries =
        new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    public abstract Task<ResourceElement> GetCapabilityStatementOnStartup(CancellationToken ct);

    internal void ClearCache()
    {
        _evaluatedQueries.Clear(); // ← Manual cache clear
    }
}
```

**Problems**:
1. `GetCapabilityStatementOnStartup` implies it's built once
2. Cache clearing is internal/manual (no automatic invalidation)
3. No versioning or change detection mechanism

---

## V2 Solution: Segmented Capability Statement with Change Tracking

### Key Innovation: Capability Segments

Instead of treating CapabilityStatement as monolithic, break it into **segments** with different refresh rates:

| Segment | Examples | Refresh Rate | Change Detection |
|---------|----------|--------------|------------------|
| **Static** | FHIR version, software info, base interactions | Never (startup only) | N/A |
| **Quasi-Static** | Resource types, default search params | Rarely (IG load) | Version hash |
| **Dynamic** | Loaded profiles, custom search params | Frequently (on-demand) | Change notification |
| **Tenant-Specific** | Tenant capabilities | Per request | Tenant context |

### Architecture Pattern

```csharp
// 1. Capability Segment Abstraction
public interface ICapabilitySegment
{
    string SegmentKey { get; } // "static", "profiles", "search-params", etc.

    ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct);

    ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct);
}

// 2. Capability Context (per-request context)
public record CapabilityContext(
    FhirVersion FhirVersion,
    string? TenantId = null,
    bool IncludeExperimental = false);

// 3. Versioned Capability Cache Entry
public record CapabilityCacheEntry(
    ITypedElement Statement,
    string VersionHash,
    DateTimeOffset CachedAt);

// 4. Capability Statement Service
public class CapabilityStatementService
{
    private readonly IEnumerable<ICapabilitySegment> _segments;
    private readonly ICache _cache;

    public async ValueTask<ITypedElement> GetCapabilityStatementAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // 1. Build cache key from context
        var cacheKey = BuildCacheKey(context);

        // 2. Check if cached version is still valid
        var cached = await _cache.GetAsync<CapabilityCacheEntry>(cacheKey, ct);

        if (cached != null)
        {
            // 3. Verify each segment's version hash
            var currentHash = await ComputeVersionHashAsync(context, ct);

            if (cached.VersionHash == currentHash)
            {
                return cached.Statement; // ← Cache hit, still valid
            }
        }

        // 4. Build new capability statement
        var builder = CreateBuilder(context);

        foreach (var segment in _segments)
        {
            await segment.ApplyAsync(builder, context, ct);
        }

        var statement = builder.Build();
        var versionHash = await ComputeVersionHashAsync(context, ct);

        // 5. Cache with version hash
        await _cache.SetAsync(
            cacheKey,
            new CapabilityCacheEntry(statement, versionHash, DateTimeOffset.UtcNow),
            expiration: TimeSpan.FromHours(1), // Safety timeout
            ct);

        return statement;
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

        // Combine all segment hashes
        return string.Join("|", hashes);
    }
}
```

---

## Capability Segment Implementations

### 1. Static Segment (Never Changes)

```csharp
public class StaticCapabilitySegment : ICapabilitySegment
{
    private readonly CoreFeatureConfiguration _config;

    public string SegmentKey => "static";

    public async ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        builder.Apply(statement =>
        {
            statement.Name = _config.SoftwareName;
            statement.Status = "active";
            statement.Date = DateTimeOffset.UtcNow.ToString("O");
            statement.Publisher = "Microsoft";
            statement.Kind = "instance";
            statement.FhirVersion = context.FhirVersion.VersionString;

            statement.Software = new SoftwareComponent
            {
                Name = _config.SoftwareName,
                Version = ProductVersionInfo.Version.ToString()
            };
        });

        await ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Static content: hash the software version
        var version = ProductVersionInfo.Version.ToString();
        return ValueTask.FromResult(version);
    }
}
```

### 2. Profile Segment (Dynamic)

```csharp
public class ProfileCapabilitySegment : ICapabilitySegment
{
    private readonly ISupportedProfilesStore _profilesStore;
    private readonly IModelInfoProvider _modelInfo;

    public string SegmentKey => "profiles";

    public async ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        foreach (var resourceType in _modelInfo.GetResourceTypeNames())
        {
            if (resourceType == "Parameters") continue;

            var profiles = _profilesStore.GetSupportedProfiles(
                resourceType,
                disableCacheRefresh: true); // Already refreshed in GetVersionHashAsync

            if (profiles?.Any() == true)
            {
                builder.ApplyToResource(resourceType, component =>
                {
                    component.SupportedProfile.Clear();
                    foreach (var profile in profiles)
                    {
                        component.SupportedProfile.Add(profile);
                    }
                });
            }
        }

        await ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Trigger refresh to check for new profiles
        _profilesStore.Refresh();

        // Compute hash of all loaded profiles
        var allProfiles = new List<string>();

        foreach (var resourceType in _modelInfo.GetResourceTypeNames())
        {
            var profiles = _profilesStore.GetSupportedProfiles(
                resourceType,
                disableCacheRefresh: true);

            if (profiles != null)
            {
                allProfiles.AddRange(profiles);
            }
        }

        // Hash the sorted list of profiles
        allProfiles.Sort();
        var hash = ComputeHash(string.Join("|", allProfiles));

        return ValueTask.FromResult(hash);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }
}
```

### 3. Search Parameter Segment (Dynamic)

```csharp
public class SearchParameterCapabilitySegment : ICapabilitySegment
{
    private readonly ISearchParameterDefinitionManager _searchParamManager;
    private readonly IModelInfoProvider _modelInfo;

    public string SegmentKey => "search-params";

    public async ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        // Add global search parameters
        builder.AddGlobalSearchParameters();

        // Sync search parameters for each resource
        foreach (var resourceType in _modelInfo.GetResourceTypeNames())
        {
            if (resourceType == "Parameters") continue;

            var searchParams = _searchParamManager.GetSearchParameters(resourceType);

            if (searchParams.Any())
            {
                builder.ApplyToResource(resourceType, component =>
                {
                    component.SearchParam.Clear();

                    foreach (var sp in searchParams)
                    {
                        if (sp.Name == "_type") continue; // Exclude _type under resource

                        component.SearchParam.Add(new SearchParamComponent
                        {
                            Name = sp.Name,
                            Type = sp.Type,
                            Definition = sp.Url,
                            Documentation = sp.Description
                        });
                    }

                    // Add _include for reference parameters
                    component.SearchInclude.Clear();
                    foreach (var refParam in searchParams.Where(x => x.Type == SearchParamType.Reference))
                    {
                        component.SearchInclude.Add($"{resourceType}:{refParam.Code}");
                    }
                    if (component.SearchInclude.Any())
                    {
                        component.SearchInclude.Add("*");
                    }
                });

                // Add search-type interaction
                builder.ApplyToResource(resourceType, c =>
                {
                    if (!c.Interaction.Any(x => x.Code == "search-type"))
                    {
                        c.Interaction.Add(new ResourceInteractionComponent
                        {
                            Code = "search-type"
                        });
                    }
                });
            }
        }

        await ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Hash search parameter URLs + versions
        var paramIdentifiers = new List<string>();

        foreach (var resourceType in _modelInfo.GetResourceTypeNames())
        {
            var searchParams = _searchParamManager.GetSearchParameters(resourceType);

            foreach (var sp in searchParams)
            {
                paramIdentifiers.Add($"{sp.Url}|{sp.LastUpdated?.Ticks ?? 0}");
            }
        }

        paramIdentifiers.Sort();
        var hash = ComputeHash(string.Join("|", paramIdentifiers));

        return ValueTask.FromResult(hash);
    }
}
```

### 4. Resource Interactions Segment (Static)

```csharp
public class ResourceInteractionCapabilitySegment : ICapabilitySegment
{
    private readonly IModelInfoProvider _modelInfo;
    private readonly CoreFeatureConfiguration _config;

    public string SegmentKey => "interactions";

    public async ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        foreach (var resourceType in _modelInfo.GetResourceTypeNames())
        {
            if (resourceType == "Parameters") continue;

            // Standard CRUD interactions
            builder.ApplyToResource(resourceType, component =>
            {
                component.Interaction.Add(new ResourceInteractionComponent { Code = "create" });
                component.Interaction.Add(new ResourceInteractionComponent { Code = "read" });
                component.Interaction.Add(new ResourceInteractionComponent { Code = "vread" });
                component.Interaction.Add(new ResourceInteractionComponent { Code = "history-instance" });
                component.Interaction.Add(new ResourceInteractionComponent { Code = "history-type" });

                // AuditEvent special case: no update/delete
                if (resourceType != "AuditEvent")
                {
                    component.Interaction.Add(new ResourceInteractionComponent { Code = "update" });
                    component.Interaction.Add(new ResourceInteractionComponent { Code = "patch" });
                    component.Interaction.Add(new ResourceInteractionComponent { Code = "delete" });
                }

                // Versioning
                component.Versioning.Add("no-version");
                component.Versioning.Add("versioned");
                component.Versioning.Add("versioned-update");

                // Conditional operations
                component.ConditionalCreate = true;
                component.ReadHistory = true;
                component.UpdateCreate = true;

                if (resourceType != "AuditEvent")
                {
                    component.ConditionalUpdate = true;
                    component.ConditionalDelete.Add("not-supported");
                    component.ConditionalDelete.Add("single");
                    component.ConditionalDelete.Add("multiple");
                }
            });
        }

        // System-level interactions
        builder.AddGlobalInteraction("history-system");
        builder.AddGlobalInteraction("transaction");
        builder.AddGlobalInteraction("batch");

        await ValueTask.CompletedTask;
    }

    public ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Resource interactions are static based on FHIR version
        return ValueTask.FromResult(context.FhirVersion.VersionString);
    }
}
```

---

## Change Notification Pattern

For truly dynamic updates (e.g., when a new profile is loaded), use event-based cache invalidation:

```csharp
// 1. Define capability change event
public record CapabilityChangedEvent(string SegmentKey, FhirVersion? FhirVersion = null) : IEvent;

// 2. Segment notifies when it changes
public class ProfileCapabilitySegment : ICapabilitySegment
{
    private readonly IBus _bus;

    public async Task OnProfileLoadedAsync(string profileUrl, CancellationToken ct)
    {
        // Notify that profiles segment has changed
        await _bus.PublishAsync(new CapabilityChangedEvent("profiles"), ct);
    }
}

// 3. Service invalidates cache on event
public class CapabilityStatementService
{
    private readonly ICache _cache;

    // Subscribe to capability change events
    public void OnCapabilityChanged(CapabilityChangedEvent evt)
    {
        // Invalidate all cache entries for this segment
        // Pattern: "capability:{fhirVersion}:{tenantId?}:{segmentKey}"

        var pattern = evt.FhirVersion != null
            ? $"capability:{evt.FhirVersion}:*"
            : "capability:*";

        _cache.RemoveByPatternAsync(pattern); // ← Cache provider must support pattern removal
    }
}
```

---

## Multi-Tenant Support

Different tenants may have different capabilities (e.g., Tenant A has US Core, Tenant B doesn't):

```csharp
public class CapabilityStatementService
{
    public async ValueTask<ITypedElement> GetCapabilityStatementAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        // Cache key includes tenant ID
        var cacheKey = context.TenantId != null
            ? $"capability:{context.FhirVersion}:{context.TenantId}"
            : $"capability:{context.FhirVersion}:default";

        // Each tenant gets separate cache entry
        // ...
    }
}

// Tenant-specific segment
public class TenantProfileCapabilitySegment : ICapabilitySegment
{
    private readonly ITenantProfileStore _tenantProfiles;

    public async ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        if (context.TenantId == null) return;

        // Get profiles loaded for this specific tenant
        var tenantProfiles = await _tenantProfiles.GetProfilesAsync(
            context.TenantId,
            context.FhirVersion,
            ct);

        foreach (var (resourceType, profiles) in tenantProfiles)
        {
            builder.ApplyToResource(resourceType, component =>
            {
                foreach (var profile in profiles)
                {
                    component.SupportedProfile.Add(profile);
                }
            });
        }
    }

    public async ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        if (context.TenantId == null)
            return "no-tenant";

        var tenantProfiles = await _tenantProfiles.GetProfilesAsync(
            context.TenantId,
            context.FhirVersion,
            ct);

        var profileList = tenantProfiles.SelectMany(x => x.Value).ToList();
        profileList.Sort();

        return ComputeHash(string.Join("|", profileList));
    }
}
```

---

## Performance Optimizations

### 1. Parallel Segment Application

```csharp
public async ValueTask<ITypedElement> GetCapabilityStatementAsync(
    CapabilityContext context,
    CancellationToken ct)
{
    // ...cache check...

    var builder = CreateBuilder(context);

    // Apply segments in parallel where possible
    var staticSegments = _segments.Where(s => s.SegmentKey is "static" or "interactions");
    var dynamicSegments = _segments.Where(s => s.SegmentKey is "profiles" or "search-params");

    // Static segments first (parallel)
    await Task.WhenAll(staticSegments.Select(s => s.ApplyAsync(builder, context, ct).AsTask()));

    // Dynamic segments (parallel)
    await Task.WhenAll(dynamicSegments.Select(s => s.ApplyAsync(builder, context, ct).AsTask()));

    // ...build and cache...
}
```

### 2. Incremental Hash Computation

Instead of rehashing everything, maintain segment-level hashes:

```csharp
public class CapabilityVersionTracker
{
    private readonly ConcurrentDictionary<string, string> _segmentHashes = new();

    public async ValueTask<string> GetCompositeHashAsync(
        IEnumerable<ICapabilitySegment> segments,
        CapabilityContext context,
        CancellationToken ct)
    {
        var hashes = new List<string>();

        foreach (var segment in segments)
        {
            var key = $"{context.FhirVersion}:{context.TenantId}:{segment.SegmentKey}";

            // Check if segment hash is cached
            if (!_segmentHashes.TryGetValue(key, out var hash))
            {
                hash = await segment.GetVersionHashAsync(context, ct);
                _segmentHashes[key] = hash;
            }

            hashes.Add(hash);
        }

        return string.Join("|", hashes);
    }

    public void InvalidateSegment(string segmentKey, FhirVersion? version = null, string? tenantId = null)
    {
        var keysToRemove = _segmentHashes.Keys
            .Where(k => k.Contains(segmentKey))
            .Where(k => version == null || k.StartsWith(version.ToString()))
            .Where(k => tenantId == null || k.Contains(tenantId))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _segmentHashes.TryRemove(key, out _);
        }
    }
}
```

### 3. Lazy Segment Loading

Don't load all segments if only checking cache validity:

```csharp
public async ValueTask<ITypedElement> GetCapabilityStatementAsync(
    CapabilityContext context,
    CancellationToken ct)
{
    var cacheKey = BuildCacheKey(context);
    var cached = await _cache.GetAsync<CapabilityCacheEntry>(cacheKey, ct);

    if (cached != null)
    {
        // OPTIMIZATION: Only compute hash if cache exists
        var currentHash = await _versionTracker.GetCompositeHashAsync(_segments, context, ct);

        if (cached.VersionHash == currentHash)
        {
            return cached.Statement; // ← Fast path: cache hit, no segment loading
        }
    }

    // Slow path: build new statement
    // ...
}
```

---

## Implementation Phases

### Prototype Phase (ADR-2501)
**Status**: Not needed yet

Capability statement can be static JSON file served from `/metadata`.

### Phase 1.2 - Search Implementation (ADR-2503)
**Status**: Introduce basic pattern

Implement:
- `CapabilityStatementService` with basic caching
- `StaticCapabilitySegment` (software info, FHIR version)
- `ResourceInteractionCapabilitySegment` (CRUD operations)

Cache strategy: **Simple in-memory cache, rebuild on startup**

### Phase 3 - Validation (ADR-2506)
**Status**: Add profile segment

Implement:
- `ProfileCapabilitySegment` with change detection
- Cache invalidation when profiles loaded

### Phase 4 - Advanced Search (ADR-2507)
**Status**: Add search parameter segment

Implement:
- `SearchParameterCapabilitySegment`
- Event-based invalidation when custom search params registered

### Phase 6 - Multi-Tenant (ADR-2509)
**Status**: Add tenant-aware caching

Implement:
- `CapabilityContext` with `TenantId`
- Tenant-specific cache keys
- `TenantProfileCapabilitySegment`

### Phase 7 - Distributed Infrastructure (ADR-2510)
**Status**: Redis cache with pattern invalidation

Implement:
- Redis-based cache provider with pattern removal
- Distributed event bus for cache invalidation across nodes

---

## Cache Invalidation Strategies

| Strategy | When to Use | Implementation |
|----------|-------------|----------------|
| **Time-based expiration** | Safety net, prevent stale cache forever | 1-hour TTL |
| **Version hash comparison** | Detect changes in segment content | SHA256 of segment data |
| **Event-based invalidation** | Immediate update when change occurs | Pub/sub with `CapabilityChangedEvent` |
| **Pattern-based removal** | Invalidate multiple tenants/versions | Redis pattern matching |
| **Manual refresh** | Admin-triggered rebuild | `/admin/capability/refresh` endpoint |

---

## API Endpoints

### GET /metadata
Returns capability statement for current context:

```csharp
app.MapGet("/metadata", async (
    HttpContext httpContext,
    CapabilityStatementService capabilityService,
    ITenantContextResolver tenantResolver,
    IFhirVersionResolver versionResolver,
    CancellationToken ct) =>
{
    var context = new CapabilityContext(
        FhirVersion: versionResolver.Resolve(httpContext),
        TenantId: await tenantResolver.ResolveAsync(httpContext, ct)
    );

    var statement = await capabilityService.GetCapabilityStatementAsync(context, ct);

    return Results.Ok(statement);
});
```

### POST /admin/capability/refresh (Optional)

Admin endpoint to force rebuild:

```csharp
app.MapPost("/admin/capability/refresh", async (
    CapabilityStatementService capabilityService,
    ICache cache,
    CancellationToken ct) =>
{
    // Clear all capability cache entries
    await cache.RemoveByPatternAsync("capability:*", ct);

    return Results.Ok(new { message = "Capability cache cleared" });
})
.RequireAuthorization("Admin"); // ← Admin only
```

---

## Testing Strategy

### Unit Tests

```csharp
public class ProfileCapabilitySegmentTests
{
    [Fact]
    public async Task GetVersionHash_ChangesWhenProfileLoaded()
    {
        // Arrange
        var profileStore = new Mock<ISupportedProfilesStore>();
        profileStore.Setup(x => x.GetSupportedProfiles("Patient", true))
            .Returns(new[] { "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient" });

        var segment = new ProfileCapabilitySegment(profileStore.Object, ...);
        var context = new CapabilityContext(FhirVersion.R4);

        // Act
        var hash1 = await segment.GetVersionHashAsync(context, CancellationToken.None);

        // Load new profile
        profileStore.Setup(x => x.GetSupportedProfiles("Patient", true))
            .Returns(new[] {
                "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
                "http://custom.org/fhir/StructureDefinition/custom-patient"
            });

        var hash2 = await segment.GetVersionHashAsync(context, CancellationToken.None);

        // Assert
        Assert.NotEqual(hash1, hash2); // ← Hash changed!
    }
}
```

### Integration Tests

```csharp
public class CapabilityStatementDynamicTests : IAsyncLifetime
{
    [Fact]
    public async Task Metadata_UpdatesWhenProfileLoaded()
    {
        // 1. Get initial capability statement
        var response1 = await _client.GetAsync("/metadata");
        var capability1 = await response1.Content.ReadAsAsync<CapabilityStatement>();

        var patientResource1 = capability1.Rest[0].Resource.First(r => r.Type == "Patient");
        Assert.Empty(patientResource1.SupportedProfile); // ← No profiles yet

        // 2. Load US Core profile package
        await _profileLoader.LoadPackageAsync("hl7.fhir.us.core", "6.1.0");

        // 3. Get capability statement again
        var response2 = await _client.GetAsync("/metadata");
        var capability2 = await response2.Content.ReadAsAsync<CapabilityStatement>();

        var patientResource2 = capability2.Rest[0].Resource.First(r => r.Type == "Patient");
        Assert.Contains(
            "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
            patientResource2.SupportedProfile); // ← Profile now appears!
    }
}
```

---

## Benefits

### ✅ Compared to Legacy

| Aspect | Legacy | V2 |
|--------|--------|-----|
| **Dynamic updates** | Manual `Refresh()` call, profiles only | Automatic change detection, all segments |
| **Cache invalidation** | Manual `ClearCache()` | Event-based + version hash |
| **Granularity** | All-or-nothing rebuild | Segment-level refresh |
| **Multi-tenant** | Single global capability | Tenant-specific cache entries |
| **Performance** | Rebuild = expensive | Incremental hash checks, parallel segments |
| **Distributed** | In-process only | Redis cache + event bus |

### ✅ Key Improvements

1. **Automatic Change Detection**: Version hashes detect when rebuild needed
2. **Granular Caching**: Only rebuild changed segments
3. **Event-Driven Invalidation**: Immediate updates when profiles/params loaded
4. **Multi-Tenant Aware**: Separate cache per tenant
5. **Distributed-Ready**: Redis cache with pattern invalidation
6. **Performance**: Parallel segment application, lazy loading

---

## Consequences

### Positive

1. **Dynamic Capabilities**: Profiles/search params appear immediately after load
2. **Efficient Caching**: Only rebuild when content actually changes
3. **Scalable**: Works in distributed web farm with Redis
4. **Tenant Isolation**: Each tenant sees only their capabilities
5. **Backward Compatible**: Can still use `IProvideCapability` pattern for static segments

### Negative

1. **Complexity**: More code than simple static generation
2. **Hash Computation Overhead**: Must compute hashes to check validity
3. **Cache Storage**: Multiple tenant versions consume more memory/Redis space

### Mitigation

1. **Incremental Implementation**: Start simple (Phase 1), add complexity as needed
2. **Hash Caching**: Cache segment hashes to avoid recomputation
3. **TTL Safety Net**: 1-hour expiration prevents runaway cache growth

---

## References

- Legacy: `IProvideCapability.cs`, `CapabilityStatementBuilder.cs`
- Legacy: `ISupportedProfilesStore.cs` (Refresh pattern)
- Investigation: `caching-abstraction-architecture.md` (ICache abstraction)
- Investigation: `distributed-messaging-architecture.md` (Event bus)
- ADR-2503: Phase 1.2 - Search Implementation (first capability statement)
- ADR-2506: Phase 3 - Validation (profile loading)
- ADR-2509: Phase 6 - Multi-Tenant (tenant-aware capabilities)
- ADR-2510: Phase 7 - Distributed Infrastructure (Redis cache)

---

## Next Steps

1. **Phase 1.2**: Implement basic `CapabilityStatementService` with static segments
2. **Phase 3**: Add `ProfileCapabilitySegment` with change detection
3. **Phase 6**: Add tenant-aware caching
4. **Phase 7**: Implement Redis cache with distributed invalidation
