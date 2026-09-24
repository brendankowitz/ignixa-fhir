# SqlServer System/QuantityCode Self-Healing Cache — Design

**Status:** Approved by user, 2026-07-21. Fixes a confirmed, pre-existing bug surfaced while verifying `docs/superpowers/specs/2026-07-21-search-param-seed-on-tenant-init-design.md`.

## Background

While verifying the seed-on-tenant-init fix's Task 2 against a real database, `SortTests` showed zero improvement (20 failed/2 passed/22 total, identical to the true pre-fix baseline). A 2-round systematic-debugging investigation found the true cause: not a repeat of either of today's two earlier fixes, but a third, separate, pre-existing bug live since Phase D's original write-path port.

## Root Cause, Confirmed

`SqlServerSearchIndexReferenceDataCache.SystemMappings`/`QuantityCodeMappings` (`src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs:61,63`) return the raw `_systemCache`/`_quantityCodeCache` `ConcurrentDictionary` fields directly. Nothing in the SqlServer write path — not `SqlServerMergeRepository`, not `SqlServerFhirRepository`, not `SqlEntityFrameworkRepositoryFactory`'s eager cache warm-up (which only calls `PreloadResourceTypesAsync`/`PreloadSearchParamsAsync`) — ever calls the cache's own `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` methods proactively. Both dictionaries start empty on a fresh tenant and stay empty forever.

Every row generator that consumes `SystemMappings`/`QuantityCodeMappings` does a bare `TryGetValue`; on a miss, the calling `if`/`else if` chain simply doesn't produce a row for that value (e.g. `TokenSearchParameterRowGenerator.cs:94-103`) — silently, with no warning log (unlike the adjacent `SearchParamId`-miss branch two lines below, which does log, per this morning's Task 4). Confirmed via live E2E log capture: every `SqlServerMergeRepository` write batch logs `0 systems` for the entire test run. Any token or quantity search value carrying a `System` — nearly all of them, including `_tag` and `gender` — silently drops.

The EF reference implementation already solves this: its own `SystemMappings`/`QuantityCodeMappings` properties (`SearchIndexReferenceDataCache.cs:543-581`) return a generic `LazyLoadingDictionary<TKey,TValue>` wrapper (`:748-855`, reused for all four mapping types) whose `TryGetValue` synchronously resolves a miss via `GetOrCreateSystemIdAsync(...).GetAwaiter().GetResult()`, caching the result and logging on failure. This pattern was never ported to the SqlServer write-side cache. `SqlServerSearchIndexReferenceDataCache` already has correctly-implemented `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` methods (proper double-checked locking against `_dbLock`, reviewed this morning) — they're simply never called by anything in the write path.

`ResourceTypeMappings`/`SearchParameterMappings` are unaffected and stay on this morning's eager-preload strategy — a deliberately different, already-correct approach for those two (small, fully-preloadable catalogs), not something this fix touches or needs to unify with System/QuantityCode's fundamentally different "large, unbounded, on-demand-created" shape.

## Fix

Port a generic, private `OnDemandResolvingDictionary<TKey,TValue>` wrapper class into `SqlServerSearchIndexReferenceDataCache.cs`, matching EF's `LazyLoadingDictionary` shape:

```csharp
private sealed class OnDemandResolvingDictionary<TKey, TValue>(
    ConcurrentDictionary<TKey, TValue> cache,
    Func<TKey, CancellationToken, Task<TValue>> resolveAsync,
    ILogger logger) : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (cache.TryGetValue(key, out value!))
        {
            return true;
        }

        try
        {
            value = resolveAsync(key, CancellationToken.None).GetAwaiter().GetResult();
            cache[key] = value;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve {Key} on demand -- row skipped", key);
            value = default!;
            return false;
        }
    }

    // IReadOnlyDictionary implementation delegates to the backing cache (Keys, Values, Count,
    // ContainsKey, indexer, GetEnumerator) -- matches SentinelFilteringDictionary's shape in this
    // same file for the read-only surface members that don't need on-demand resolution.
}
```

`SystemMappings`/`QuantityCodeMappings` change from:

```csharp
public IReadOnlyDictionary<string, int> SystemMappings => _systemCache;

public IReadOnlyDictionary<string, int> QuantityCodeMappings => _quantityCodeCache;
```

to:

```csharp
public IReadOnlyDictionary<string, int> SystemMappings =>
    new OnDemandResolvingDictionary<string, int>(_systemCache, GetOrCreateSystemIdAsync, _logger);

public IReadOnlyDictionary<string, int> QuantityCodeMappings =>
    new OnDemandResolvingDictionary<string, int>(_quantityCodeCache, GetOrCreateQuantityCodeIdAsync, _logger);
```

`GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync`'s existing signatures (`Task<int> GetOrCreateSystemIdAsync(string? systemUri, CancellationToken cancellationToken)`) already match the wrapper's expected `Func<TKey, CancellationToken, Task<TValue>>` delegate shape — no changes needed to either method.

**Zero changes to any row generator.** All 14 call sites across 8 files (`QuantitySearchParameterRowGenerator.cs`, `RefTokenCompositeRowGenerator.cs`, `TokenDateTimeCompositeRowGenerator.cs`, `TokenNumberNumberCompositeRowGenerator.cs`, `TokenQuantityCompositeRowGenerator.cs`, `TokenSearchParameterRowGenerator.cs`, `TokenStringCompositeRowGenerator.cs`, `TokenTokenCompositeRowGenerator.cs`) keep their exact current `_systemMappings.TryGetValue(...)`/`_quantityCodeMappings.TryGetValue(...)` calls unchanged — they transparently start hitting the self-healing wrapper instead of the permanently-empty raw dictionary.

**Log level**: `LogWarning` (matching this morning's Task 4 precedent for the `SearchParamId`-miss case), not EF's `LogError` — deliberate, explicit user choice.

**Concurrency safety, verified not just assumed**: `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` acquire `_dbLock` internally. Row generation happens after `EnsureResourceTypesPreloadedAsync`/`EnsureSearchParametersPreloadedAsync` have already been awaited to completion (this morning's Task 2) and fully released `_dbLock` — so a synchronous-over-async block inside `TryGetValue` during row generation cannot deadlock against an already-held lock on the same call stack. Different concurrent `MergeResourcesAsync` calls contending for `_dbLock` simply serialize, which is the intended behavior for a shared resource guard. The blocking cost itself is bounded, not per-request: System/QuantityCode values are heavily reused across resources (a small, bounded real-world set — common code systems, units) and get cached permanently in `_systemCache`/`_quantityCodeCache` after first resolution, for the life of the tenant's cache instance — so the blocking path only fires on genuinely new-value discovery, not routine writes, matching how the EF reference has already operated in production.

## Explicitly Out of Scope

- `ResourceTypeMappings`/`SearchParameterMappings` — unaffected, stay on this morning's eager-preload strategy.
- Any change to `SqlServerMergeRepository.cs`, `SqlServerFhirRepository.cs`, or any of the 8 row-generator files — the fix is entirely contained in the cache class.
- Auditing whether this bug also affects the EF read path's own reference-data cache — it doesn't; that cache already has this self-healing pattern (it's what this fix ports from).
- Re-examining whether this bug affects real production tenants the same way it affects E2E tests — it does, by the same reasoning as the seed-ordering bug (this is the shared write path), but that's not new scope this fix needs to additionally investigate.

## Testing

1. Cache-level unit tests: a miss on `SystemMappings`/`QuantityCodeMappings` resolves via the real `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` methods, inserts into `dbo.System`/`dbo.QuantityCode`, and is cached for subsequent lookups (matching the existing tests' style in `SqlServerSearchIndexReferenceDataCacheTests.cs`).
2. A test proving a genuine resolve failure logs a warning and `TryGetValue` returns `false` (mirroring how Task 4 tested the `SearchParamId`-miss warning this morning).
3. Targeted `SortTests` E2E re-run against a fresh scratch database — this is now the real acid test blocking Task 2 of the seed-ordering plan; expect a dramatic improvement over the 20 failed/2 passed/22 total baseline.
4. Full E2E suite re-run (baseline 163 passed/437 failed/20 skipped/620 total).
5. Full `Ignixa.DataLayer.SqlServer.IntegrationTests` re-run (currently 71/71) to confirm no regression.

## Process

Production write-path code, same risk class as today's other two fixes. Plan-level Fable review before execution, task-scoped review per task, final whole-branch review before done.

## Files Touched

- `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs` — the only file this fix touches.
