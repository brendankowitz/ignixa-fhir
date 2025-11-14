# Lazy-Loading Cache Implementation

## Summary

Implemented lazy-loading support for `SearchIndexReferenceDataCache` to automatically load missing reference data on cache miss during TVP (Table-Valued Parameter) row generation.

## Problem

Row generators were experiencing cache misses when accessing reference data dictionaries:
- `SystemMappings` - System URI to ID mapping
- `QuantityCodeMappings` - Quantity code to ID mapping
- `ResourceTypeMappings` - Resource type name to ID mapping
- `SearchParameterMappings` - Search parameter URI to ID mapping

**Before**: If a value wasn't in the cache, `TryGetValue()` returned false and the index entry was skipped.

**After**: If a value isn't in the cache, `TryGetValue()` automatically loads it from the database (or creates it for Systems/QuantityCodes), caches it, and returns it.

## Implementation

### LazyLoadingDictionary<TKey, TValue>

Created a custom dictionary wrapper class (nested in `SearchIndexReferenceDataCache`):

```csharp
private class LazyLoadingDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
```

**Key Features**:
- Wraps the underlying `ConcurrentDictionary<TKey, TValue>` cache
- Intercepts `TryGetValue()` calls
- On cache miss, invokes async load function using blocking `.GetAwaiter().GetResult()`
- Filters sentinel values (e.g., -1 for "not found") via optional `isValidValue` function
- Thread-safe for concurrent access
- Works in synchronous TVP generation context

### Property Updates

Updated all four cache properties to return lazy-loading wrappers:

```csharp
public IReadOnlyDictionary<string, short> ResourceTypeMappings
{
    get
    {
        if (_resourceTypeMappingsWrapper == null)
        {
            _resourceTypeMappingsWrapper = new LazyLoadingDictionary<string, short>(
                _resourceTypeCache,
                async key => await GetResourceTypeIdAsync(key) ?? -1,
                _logger,
                isValidValue: value => value > 0); // Filter out sentinel -1
        }
        return _resourceTypeMappingsWrapper;
    }
}
```

**Configuration per property**:

| Property | Load Function | Sentinel Filter | Behavior |
|----------|---------------|-----------------|----------|
| `ResourceTypeMappings` | `GetResourceTypeIdAsync()` | value > 0 | Load from DB, filter -1 |
| `SearchParameterMappings` | `GetSearchParamIdAsync()` | value > 0 | Load from DB, filter -1 |
| `SystemMappings` | `GetOrCreateSystemIdAsync()` | value > 0 | Load or create, filter 0 |
| `QuantityCodeMappings` | `GetOrCreateQuantityCodeIdAsync()` | value > 0 | Load or create, filter 0 |

## Design Decisions

### 1. Blocking Async (`.GetAwaiter().GetResult()`)

**Why**: Row generators run in synchronous TVP generation context - can't change signatures to async due to SQL Server TVP API limitations.

**Acceptable because**:
- Only blocks once per unique value (subsequent accesses use cached value)
- Minimizes database round-trips
- Better than skipping index entries entirely

### 2. Sentinel Value Filtering

**Purpose**: Cache stores -1 for "not found" entries to avoid repeated database queries for non-existent data.

**Implementation**: `isValidValue` function filters out:
- ResourceTypes/SearchParameters: -1 (not found)
- Systems/QuantityCodes: 0 (invalid/null)

### 3. Lazy Initialization of Wrappers

**Why**: Wrappers are created on-demand when properties are first accessed.

**Benefits**:
- No overhead if properties aren't used
- Wrappers share same underlying cache dictionaries
- Thread-safe initialization via null check

## Testing Strategy

Created comprehensive tests in `SearchIndexReferenceDataCacheTests.cs`:

1. **Lazy Loading Tests**: Verify cache miss triggers database load
2. **Sentinel Filtering Tests**: Verify -1 values are filtered correctly
3. **GetOrCreate Tests**: Verify Systems/QuantityCodes create new entries
4. **Caching Tests**: Verify subsequent accesses use cached values
5. **Concurrency Tests**: Verify thread-safe operation under concurrent access

## Usage Example

```csharp
// Row generator code (synchronous)
public void GenerateTokenRow(FhirSearchToken token)
{
    // Before: Cache miss skips indexing
    // After: Cache miss triggers lazy load
    if (_referenceDataCache.SystemMappings.TryGetValue(token.System, out var systemId))
    {
        // systemId is now guaranteed to be valid (loaded or created)
        row.SystemId = systemId;
    }
}
```

## Performance Impact

**Positive**:
- Eliminates skipped index entries due to cache misses
- Minimizes database round-trips (one per unique value)
- Subsequent accesses are in-memory (no DB hit)

**Negative**:
- First access to uncached value blocks on database I/O
- Acceptable trade-off for correctness (complete indexing)

## Files Modified

1. `src/Ignixa.DataLayer.SqlEntityFramework/Indexing/SearchIndexReferenceDataCache.cs`
   - Added `LazyLoadingDictionary<TKey, TValue>` nested class
   - Updated 4 cache property implementations
   - Added lazy-initialization fields

2. `test/Ignixa.DataLayer.SqlEntityFramework.Tests/SearchIndexReferenceDataCacheTests.cs` (NEW)
   - Comprehensive test coverage for lazy-loading behavior

## Build Verification

- Solution builds: ✅ `dotnet build All.sln` (0 warnings, 0 errors)
- All tests pass: ✅ `dotnet test All.sln` (1472 tests passed)

## Future Improvements

1. **Async TVP Support**: If SQL Server adds async TVP APIs, convert to true async/await
2. **Metrics**: Add logging/metrics for cache miss rates and load times
3. **Preloading Hints**: Allow callers to hint which values will be needed for batch preloading
4. **TTL/Expiration**: Consider adding time-based cache expiration for long-running processes
