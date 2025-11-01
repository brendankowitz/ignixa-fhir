# Caching Architecture: What, Where, and How

**Date**: October 31, 2025
**Scope**: Comprehensive overview of all caching mechanisms in Ignixa FHIR Server
**Purpose**: Performance optimization and memory management reference

## Table of Contents

1. [High-Level Overview](#high-level-overview)
2. [Request-Scoped Caching](#request-scoped-caching)
3. [Application-Level Caching](#application-level-caching)
4. [Tenant-Scoped Caching](#tenant-scoped-caching)
5. [Static/Global Caching](#staticglobal-caching)
6. [Cache Invalidation](#cache-invalidation)
7. [Memory Impact](#memory-impact)

---

## High-Level Overview

Ignixa uses **four cache scopes** with different lifetimes:

| Scope | Lifetime | Thread-Safe | Purpose | Examples |
|-------|----------|-------------|---------|----------|
| **Request** | Single HTTP request | No (single-threaded) | Avoid redundant work within request | ResourceJsonNode views |
| **Application** | App lifetime | Yes (ConcurrentDictionary) | Hot-path performance | FHIRPath delegates, CapabilityStatement |
| **Tenant** | Tenant registration | Yes (per-tenant isolation) | Tenant-specific config | DbContextOptions, SearchIndexer |
| **Static** | Process lifetime | Yes (read-only after init) | Immutable metadata | Search parameter definitions |

**Design Principles**:
- ✅ **Lazy initialization**: Populate caches on first use, not at startup
- ✅ **Thread-safety**: All shared caches use `ConcurrentDictionary` or immutable collections
- ✅ **Memory-bounded**: Application-level caches have implicit bounds (limited expression variety)
- ✅ **No TTL/expiration**: Caches live for their scope lifetime (stateless server, restart to clear)

---

## Request-Scoped Caching

**Lifetime**: Single HTTP request (disposed after response sent)
**Thread-Safety**: NOT required (single-threaded request processing)

### 1. ResourceJsonNode - Derived View Caching

**File**: `src/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs:23-26`

```csharp
public class ResourceJsonNode : BaseJsonNode, IResourceNode
{
    private MetaJsonNode? _cachedMeta;
    private JsonNodeSourceNode? _cachedSourceNode;
    private ITypedElement? _cachedTypedElement;
    private IStructureDefinitionSummaryProvider? _cachedProvider;
}
```

**What**:
- `_cachedMeta`: Wrapper for `meta` property (avoids re-creating on each access)
- `_cachedSourceNode`: ISourceNode view (created once via `ToSourceNode()`)
- `_cachedTypedElement`: ITypedElement view (created once via `ToTypedElement()`)

**Why**:
- ResourceJsonNode is mutated in-place (PATCH operations)
- Multiple operations access same derived views (validation, indexing)
- Creating views has overhead (~10-20 ns for ITypedElement)

**Invalidation**: `InvalidateCachedViews()` called after PATCH mutations (line 151)

**Example Flow**:
```csharp
// Request: PATCH /Patient/123
var resource = await repository.GetByIdAsync("Patient", "123");  // Fresh ResourceJsonNode

// 1st access: Create and cache ITypedElement
var typedElement = resource.ToTypedElement(schemaProvider);  // Cached
var searchIndices = indexer.Extract(typedElement);           // Reuses cache

// Apply PATCH mutations
await ApplyPatchAsync(resource, patchDocument);
resource.InvalidateCachedViews();  // ← Clear cache

// 2nd access: Re-create ITypedElement with fresh state
var updatedTypedElement = resource.ToTypedElement(schemaProvider);  // New cache
```

**Memory**: ~200 bytes per ResourceJsonNode (negligible, request-scoped)

---

### 2. TypedElementOnSourceNode - Child Definition Caching

**File**: `src/Ignixa.Serialization/SourceNodes/TypedElementOnSourceNode.cs:27`

```csharp
internal class TypedElementOnSourceNode : ITypedElement
{
    private readonly Lazy<IStructureDefinitionSummary?> _structureDefinition;
    private Dictionary<string, IElementDefinitionSummary?>? _childDefinitionCache;
}
```

**What**:
- `_structureDefinition`: Lazy-loaded schema for current element type
- `_childDefinitionCache`: Caches child element definitions by name (e.g., "name.family")

**Why**:
- FHIRPath evaluation accesses same child elements repeatedly
- Schema lookups are O(n) without cache (Phase 2 optimization: 6.25x speedup)

**When Populated**:
```csharp
// First access: Cache miss
var child = typedElement.Children("name");  // Looks up "name" definition, caches it

// Second access: Cache hit
var child2 = typedElement.Children("name");  // Returns cached definition
```

**Invalidation**: Never (request-scoped, disposed after request)

**Memory**: ~50 bytes per cached child definition × ~10-20 child types = ~500-1000 bytes per TypedElement

---

## Application-Level Caching

**Lifetime**: Application process (cleared only on restart)
**Thread-Safety**: YES (ConcurrentDictionary)

### 3. FHIRPath Expression Caching (Phase 3)

**File**: `src/Ignixa.FhirPath/Evaluation/TypedElementExtensions.cs:23-28`

```csharp
public static class TypedElementExtensions
{
    // AST Cache: String → Expression AST
    private static readonly ConcurrentDictionary<string, Expression> _astCache = new();

    // Delegate Cache: String → Compiled Delegate (or null if unsupported)
    private static readonly ConcurrentDictionary<string, Func<...>?> _delegateCache = new();

    private static readonly FhirPathCompiler _astCompiler = new();
    private static readonly FhirPathDelegateCompiler _delegateCompiler = new(...);
}
```

**What**:
1. **AST Cache**: Parses FHIRPath expression string to Abstract Syntax Tree
2. **Delegate Cache**: Compiles AST to executable IL delegate (7x speedup)

**Cache Key**: Expression string (e.g., `"name.family"`, `"telecom.where(system='phone')"`)

**Population Flow**:
```csharp
// First evaluation of "name.family"
typedElement.Select("name.family")
  → Parse to AST (cache miss, ~2-5 μs)
  → Compile to delegate (cache miss, ~5-10 μs)
  → Execute delegate (~0.5 μs)
  → Total: ~8-16 μs

// Second evaluation of "name.family"
typedElement.Select("name.family")
  → AST cache hit (~0.1 μs)
  → Delegate cache hit (~0.1 μs)
  → Execute delegate (~0.5 μs)
  → Total: ~0.7 μs  ← 10-20x faster
```

**Cache Size**:
- **Typical**: 20-30 unique expressions per resource type × 10 common types = **200-300 entries**
- **Memory**: ~1-2 KB per entry (AST + delegate) = **200-600 KB total**
- **Unbounded**: No eviction policy (assumes limited expression variety in production)

**Why Unbounded is Safe**:
- Search parameters use fixed expressions (from FHIR spec)
- Custom search queries are user-driven (limited variety)
- Worst case: 1000 unique expressions = ~2 MB (acceptable)

**Performance Impact**:
- **Cold cache** (first request): 8-16 μs per expression evaluation
- **Warm cache** (subsequent): 0.7 μs per expression evaluation
- **Total search indexing** (100 parameters): 52.7 μs (already 44x optimized)

---

### 4. CapabilityStatement Caching

**File**: `src/Ignixa.Application/Infrastructure/Caching/MemoryCapabilityCache.cs`

```csharp
public class MemoryCapabilityCache : ICapabilityCache
{
    private readonly ConcurrentDictionary<int, CapabilityCacheEntry> _cache = new();
}
```

**What**:
- Caches generated CapabilityStatement per tenant
- CapabilityStatement is expensive to generate (5-20ms, includes reflection + schema analysis)

**Cache Key**: `tenantId` (int)

**Cache Value**:
```csharp
public class CapabilityCacheEntry
{
    public CapabilityStatementJsonNode CapabilityStatement { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ETag { get; set; }  // For HTTP caching (304 Not Modified)
}
```

**Invalidation**:
```csharp
// Triggered by:
// 1. Tenant configuration changes (rare)
// 2. New search parameters added (never in production)
// 3. Application restart

public class CapabilityCacheInvalidator : ICapabilityCacheInvalidator
{
    public void InvalidateForTenant(int tenantId)
    {
        _capabilityCache.Remove(tenantId);
    }
}
```

**Memory**: ~50-100 KB per CapabilityStatement × number of tenants

---

### 5. Validation Schema Caching

**File**: `src/Ignixa.Validation/Schema/CachedValidationSchemaResolver.cs`

```csharp
public class CachedValidationSchemaResolver : IValidationSchemaResolver
{
    private readonly ConcurrentDictionary<string, Lazy<ValidationSchema>> _cache = new();
}
```

**What**:
- Caches compiled JSON Schema validators for resource types
- Validation schemas are expensive to compile (10-50ms per resourceType)

**Cache Key**: `(resourceType, fhirVersion, profile)` composite key (e.g., `"Patient|R4|http://..."`)

**Why**:
- Tier 2/3 validation requires JSON Schema compilation
- Compilation involves regex parsing, constraint analysis
- Schemas are immutable (FHIR spec doesn't change at runtime)

**Memory**: ~100-500 KB per schema × ~20 common types = **2-10 MB**

---

## Tenant-Scoped Caching

**Lifetime**: Tenant registration (cleared on tenant reconfiguration or app restart)
**Thread-Safety**: YES (per-tenant isolation + ConcurrentDictionary)

### 6. FhirVersionContext - Per-Version Singletons

**File**: `src/Ignixa.Search/Infrastructure/FhirVersionContext.cs`

```csharp
public class FhirVersionContext : IFhirVersionContext, IDisposable
{
    // Lazy-initialized singletons per FHIR version
    private readonly ConcurrentDictionary<FhirSpecification, Lazy<IFhirSchemaProvider>> _schemaProviders = new();
    private readonly ConcurrentDictionary<FhirSpecification, Lazy<ISearchIndexer>> _searchIndexers = new();
    private readonly ConcurrentDictionary<FhirSpecification, Lazy<SearchParameterDefinitionManager>> _definitionManagers = new();
}
```

**What**:
- One `IFhirSchemaProvider` per FHIR version (R4, R4B, R5, etc.)
- One `ISearchIndexer` per FHIR version
- One `SearchParameterDefinitionManager` per FHIR version

**Why**:
- Multi-version support (same server handles R4 and R5 resources)
- Expensive to initialize (schema loading: 5-50ms)
- Immutable after creation (FHIR specs don't change)

**Memory**: ~10-20 MB per FHIR version × active versions (typically 1-2)

---

### 7. DbContextOptions Caching (SQL EF)

**File**: `src/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:36`

```csharp
public class SqlEntityFrameworkRepositoryFactory : IFhirRepositoryFactory
{
    // Thread-safe cache: tenantId → DbContextOptions
    private readonly ConcurrentDictionary<int, Lazy<DbContextOptions<FhirDbContext>>> _dbContextOptionsCache = new();
}
```

**What**:
- Caches EF Core `DbContextOptions` per tenant
- DbContextOptions is **expensive** to create (connection string parsing, model compilation)

**Why**:
- DbContextOptions is **thread-safe** and reusable
- DbContext is **NOT thread-safe** → create per request
- Avoids re-compiling EF model on every request

**Pattern**:
```csharp
// Cached (application lifetime)
var options = _dbContextOptionsCache.GetOrAdd(tenantId, CreateDbContextOptions);

// Per-request (scoped, disposed after request)
using var dbContext = new FhirDbContext(options);
await dbContext.Resources.Where(...).ToListAsync();
```

**Memory**: ~5-10 KB per tenant × number of tenants

---

## Static/Global Caching

**Lifetime**: Process lifetime (immutable, initialized once)
**Thread-Safety**: YES (read-only after initialization)

### 8. Search Parameter Definitions (Pre-Generated)

**File**: `src/Ignixa.Search/Generated/R4SearchParameterDefinitions.g.cs`

```csharp
public static class R4SearchParameterDefinitions
{
    // Static array: ~22,000 search parameters across all R4 resource types
    private static SearchParameterInfo[]? _baseParameters;

    public static SearchParameterInfo[] GetBaseSearchParameters()
    {
        if (_baseParameters == null)
        {
            _baseParameters = new SearchParameterInfo[]
            {
                // Generated at build time from FHIR spec
                new SearchParameterInfo
                {
                    Code = "active",
                    Type = SearchParamType.Token,
                    Expression = "Patient.active",
                    // ... ~50 properties
                },
                // ... 21,999 more
            };
        }

        return _baseParameters;
    }
}
```

**What**:
- **Pre-generated** array of all FHIR search parameters (per version)
- Loaded into `SearchParameterDefinitionManager` at construction (< 5ms)

**Why Pre-Generated** (not runtime parsing):
- **Startup time**: 5ms vs. 50-200ms (40-100x faster)
- **Memory efficiency**: Compact array vs. reflection overhead
- **Type safety**: Compile-time validation vs. runtime errors

**Memory**: ~5-10 MB per FHIR version (R4, R4B, R5, R6, STU3)

---

### 9. Structure Definition Providers (Code-Generated)

**File**: `src/Ignixa.Specification/Generated/R4StructureDefinitionSummaryProvider.g.cs`

```csharp
public class R4StructureDefinitionSummaryProvider : IFhirSchemaProvider
{
    // Dictionary: resourceType → StructureDefinitionSummary
    private readonly Dictionary<string, IStructureDefinitionSummary> _structureDefinitions;

    public R4StructureDefinitionSummaryProvider()
    {
        _structureDefinitions = new Dictionary<string, IStructureDefinitionSummary>
        {
            ["Patient"] = new StructureDefinitionSummary
            {
                TypeName = "Patient",
                IsResource = true,
                Elements = new[]
                {
                    new ElementDefinitionSummary { ElementName = "id", Type = new[] { ... } },
                    new ElementDefinitionSummary { ElementName = "meta", Type = new[] { ... } },
                    // ... 150+ elements
                }
            },
            // ... 150+ resource types
        };
    }
}
```

**What**:
- **Code-generated** schema metadata for all FHIR resource types
- Provides type information for property navigation

**Why Code-Generated**:
- **Fast initialization**: < 5ms (dictionary pre-populated)
- **No JSON parsing**: Avoids deserializing FHIR StructureDefinitions (50-100ms)
- **Compact memory**: Only stores needed metadata (not full FHIR StructureDefinition)

**Memory**: ~15-30 MB per FHIR version

---

## Cache Invalidation

### When Caches are Cleared

| Cache | Invalidated On | Mechanism |
|-------|---------------|-----------|
| **Request-scoped** | Request completion | Automatic (object disposal) |
| **FHIRPath AST/Delegate** | Never (unbounded) | Manual: `TypedElementExtensions.ClearCache()` |
| **CapabilityStatement** | Tenant config change | `CapabilityCacheInvalidator.InvalidateForTenant(id)` |
| **Validation Schemas** | Never (immutable spec) | Application restart |
| **DbContextOptions** | Tenant reconfiguration | Factory recreates cache entry |
| **FhirVersionContext** | Application restart | Singleton disposal |
| **Search Parameter Defs** | Never (immutable spec) | Application restart |

### Manual Cache Clearing (for Testing)

```csharp
// Clear FHIRPath caches (rare, only for memory pressure or testing)
TypedElementExtensions.ClearCache();

// Invalidate CapabilityStatement for tenant (after config change)
_capabilityCacheInvalidator.InvalidateForTenant(tenantId);

// No API for other caches (restart required)
```

---

## Memory Impact

### Estimated Memory Usage (Typical Production Server)

**Per-Request** (disposed after response):
- ResourceJsonNode + cached views: **~1 KB**
- TypedElementOnSourceNode child cache: **~0.5-1 KB**
- **Total per request**: ~1.5-2 KB

**Application-Level** (process lifetime):
- FHIRPath AST cache (200-300 expressions): **~200-600 KB**
- FHIRPath Delegate cache (200-300 delegates): **~200-600 KB**
- CapabilityStatement cache (10 tenants): **~500 KB - 1 MB**
- Validation schema cache (20 types): **~2-10 MB**
- **Total**: ~3-12 MB

**Tenant-Level** (per tenant):
- FhirVersionContext (1-2 FHIR versions): **~10-20 MB**
- DbContextOptions: **~5-10 KB**
- **Total per tenant**: ~10-20 MB

**Static/Global** (per FHIR version):
- Search parameter definitions: **~5-10 MB**
- Structure definition providers: **~15-30 MB**
- **Total per FHIR version**: ~20-40 MB

**Grand Total** (1 tenant, R4 only, 10 concurrent requests):
- Request-scoped: 10 × 2 KB = **20 KB**
- Application-level: **~10 MB**
- Tenant-level: **~15 MB**
- Static/global: **~30 MB**
- **Total**: ~**55 MB** (excluding DbContext working sets)

---

## Cache Performance Impact

### Measured Speedups (from benchmarks)

| Operation | Without Cache | With Cache | Speedup |
|-----------|---------------|------------|---------|
| **FHIRPath evaluation** | 5-10 μs | 0.7 μs | **7-14x** |
| **Property navigation** | 6.25x slower | Baseline | **6.25x** (Phase 2) |
| **Search indexing** (total) | ~2600 μs (baseline) | 52.7 μs | **44x** (Phase 2+3 combined) |
| **CapabilityStatement** | 5-20 ms | < 1 ms | **5-20x** |
| **DbContextOptions** | 10-50 ms | < 0.1 ms | **100-500x** |

---

## Best Practices

### ✅ DO:
- Use `ConcurrentDictionary` for all shared caches
- Use `Lazy<T>` for expensive initialization
- Cache immutable objects (ASTs, schemas, DbContextOptions)
- Document cache lifetime in code comments
- Use request-scoped caching for per-request views

### ❌ DON'T:
- Cache mutable objects across requests (race conditions)
- Use TTL/expiration without clear eviction strategy (memory leaks)
- Pre-populate caches at startup (lazy initialization is faster)
- Cache tenant-specific data in static/global caches (multi-tenancy violation)
- Share `DbContext` instances across requests (not thread-safe)

---

## Related Documents

- **Performance Analysis**: `docs/investigations/POST-PUT-PERFORMANCE-SUMMARY.md`
- **Phase 2 Optimizations**: Commit `63b959c` (property caching, 6.25x)
- **Phase 3 Optimizations**: Commit `b704390` (FHIRPath delegate compilation, 7x)
- **Connection Pooling**: `docs/investigations/sql-connection-pooling-analysis.md`

---

**Last Updated**: October 31, 2025
**Status**: Complete
**Maintainer**: Reference for all caching decisions
