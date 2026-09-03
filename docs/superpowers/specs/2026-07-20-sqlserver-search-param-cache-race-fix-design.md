# SqlServer Search-Parameter Cache Race Fix — Design

**Status:** Approved by user, 2026-07-20. Fixes a confirmed regression discovered after Phase D's write-path cutover.

## Background

Phase D (`worktree-ignixa-datalayer-sqlserver`) replaced `Ignixa.DataLayer.SqlEntityFramework`'s EF-based `IFhirRepository` write path with a new raw-ADO.NET implementation in `Ignixa.DataLayer.SqlServer`. All 11 implementation tasks were completed, task-reviewed, and a final whole-branch review returned "Ready to merge: Yes." That verdict has since been **retracted**.

The differential-test harness built for Phase D compares row-level state between the old (EF) and new (SqlServer) write paths against each other — it never exercises a live search. When the E2E suite (`test/Ignixa.Api.E2ETests`) was run for the first time this session (it had been silently excluded from every prior run via a `--filter` flag, due to an unrelated environment config gap), 437 of 620 tests failed: writes via the real HTTP API succeeded, but subsequent searches returned empty or wrong results.

## Root Cause

Confirmed via direct source inspection and a real, isolated E2E repro (fresh database, `SortTests` in isolation): `dbo.SearchParam` was fully seeded (1405 rows, including `_tag`=901), but after four Patient writes, `dbo.TokenSearchParam` contained rows for only one search parameter (`individual-address-use`, id 1105) — zero rows for `_tag`, `gender`, or anything else those resources should have indexed. No exception, no error, no log at default verbosity.

**Mechanism:**

1. `SqlServerMergeRepository.MergeResourcesAsync` (`SqlServerMergeRepository.cs:156-159`) gates search-parameter cache warm-up on `if (_referenceDataCache.SearchParameterMappings.Count == 0) { await PreloadSearchParamsAsync(...) }`. This check happens **outside** the cache's `_dbLock` semaphore.
2. `SqlServerSearchIndexReferenceDataCache.PreloadSearchParamsAsync` (`Indexing/SqlServerSearchIndexReferenceDataCache.cs:76-111`) populates `_searchParamCache` (a `ConcurrentDictionary`) one row at a time inside a `foreach` loop, with no `ORDER BY` on the underlying query — insertion order is arbitrary and the dictionary is visible to readers throughout the load, not just after it completes.
3. On a cold cache, if two writes land concurrently, the first can be mid-load (some rows in, most not) while the second observes `Count > 0`, skips its own preload entirely, and reads the still-filling dictionary directly.
4. Every row generator (`TokenSearchParameterRowGenerator.cs:73-74`, `:150`, and the same pattern via `SearchParameterIdLookupHelper.TryGetSearchParamId` at all 15 call sites across `RowGenerators/`) treats a cache miss as `continue` — the row is silently dropped, no log, no exception.
5. `SqlServerMergeRepository.cs:150`'s `ResourceTypeMappings` guard has the identical structural flaw (same `Count == 0` pattern, same missing lock), just a smaller/faster preload making the race window narrower and not yet observed failing in practice.

**Why the EF reference implementation doesn't have this problem, despite having the identical silent-continue-on-miss in its own row generators:** `MultiTenantSearchIndexCache.GetOrCreateCacheForTenant` (`Ignixa.DataLayer.SqlEntityFramework/Indexing/MultiTenantSearchIndexCache.cs:73-96`) calls `cache.InitializeAsync().GetAwaiter().GetResult()` **synchronously, inside cache construction**, before the cache is ever handed to any caller. `InitializeAsync()` loads the entire `dbo.SearchParams` table, uncapped. No EF caller can ever observe a partially-populated cache. EF's strategy is prevention (guarantee completeness before release), not detection-and-recovery — and this port's job is to restore that guarantee, not invent a new one.

The equivalent eager warm-up in the SqlServer port (`SqlEntityFrameworkRepositoryFactory.cs:340-345`) only calls `PreloadResourceTypesAsync` — the full search-parameter preload was never carried over, leaving the racy lazy guard as the only thing that ever populates `SearchParameterMappings`.

## Fix

Three changes, all confined to the write path already introduced by Phase D:

### 1. Eager, complete warm-up at cache construction

`SqlEntityFrameworkRepositoryFactory.cs:340-345` gains a synchronous call to fully preload search parameters, uncapped, alongside the existing `PreloadResourceTypesAsync` call — mirroring `MultiTenantSearchIndexCache.GetOrCreateCacheForTenant`'s `InitializeAsync()` call site exactly. After this change, no caller ever receives a `SqlServerSearchIndexReferenceDataCache` reference before it is fully populated.

The existing 10,000-row cap on `PreloadSearchParamsAsync` is removed for this eager call (pass `maxRows: null`), matching EF's `InitializeAsync`'s unbounded load. The real catalog is 1405 rows; the cap was a latent silent-truncation risk with no corresponding behavior in the reference implementation.

### 2. Race-free single-flight preload inside the cache

Move "ensure preloaded" out of `SqlServerMergeRepository` (which currently reaches into the cache and inspects `Count` itself) and into `SqlServerSearchIndexReferenceDataCache` as two new methods:

- `Task EnsureResourceTypesPreloadedAsync(CancellationToken cancellationToken)`
- `Task EnsureSearchParametersPreloadedAsync(CancellationToken cancellationToken)`

Each uses the cache's existing `_dbLock` semaphore with double-checked locking — check `Count == 0`, acquire `_dbLock`, re-check `Count == 0` under the lock, preload if still needed, release — the same pattern already used correctly by `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` in the same class. This makes the guard itself race-free: a concurrent caller arriving mid-load blocks on `_dbLock` until the in-flight preload finishes, rather than reading a partial dictionary.

**Implementation note (avoid a self-deadlock):** `PreloadResourceTypesAsync`/`PreloadSearchParamsAsync` already acquire `_dbLock` internally and neither currently re-checks whether the cache is already populated before querying — `SemaphoreSlim` is also not reentrant. `Ensure*PreloadedAsync` must **not** call those methods while already holding the lock. The plan should have `Ensure*PreloadedAsync` acquire `_dbLock` itself, re-check `Count == 0` under the lock, and — only if still empty — call the existing `Preload*Async` method's query-and-populate body directly (extracted so it no longer takes its own lock), rather than calling the public `Preload*Async` method re-entrantly. The public `Preload*Async` methods can stay as unconditional-reload entry points (used, e.g., by an explicit cache-refresh path) with their own locking exactly as today; `Ensure*PreloadedAsync` is a new, separate code path that shares the lock but not the outer method call.

`SqlServerMergeRepository.MergeResourcesAsync` calls `await _referenceDataCache.EnsureResourceTypesPreloadedAsync(cancellationToken)` and `await _referenceDataCache.EnsureSearchParametersPreloadedAsync(cancellationToken)` instead of inspecting `Count` and calling `Preload*Async` directly.

This is the defense-in-depth layer: after (1), the factory-constructed cache is always already warm, but integration tests construct `SqlServerSearchIndexReferenceDataCache` directly without going through the factory (`TestTenantDatabase.CreateSqlServerFhirRepositoryAsync()` and similar) — those paths still rely on the lazy guard, and it must be genuinely race-free on its own, not merely redundant.

### 3. Warning log on a genuine cache miss

Every one of the 15 call sites of `SearchParameterIdLookupHelper.TryGetSearchParamId` across `RowGenerators/` currently does:

```csharp
if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
    continue;
```

Each site adds a warning-level log call before `continue`, identifying the missing search-parameter URL and the resource being indexed. After (1) and (2), a miss for a legitimate, already-registered search parameter should be effectively impossible; if one still occurs (e.g., a new `SearchParameter` resource registered after warm-up, before the next process restart), it becomes an observable log line instead of a silent, unexplained missing-search-result bug — matching this project's own "no silent failures" standard, which EF's identical-but-unlogged version does not currently meet either. This diverges intentionally from strict EF parity; it does not change EF's own code.

## Explicitly Out of Scope

- Fixing EF's own equivalent silent-continue-on-miss (`Ignixa.DataLayer.SqlEntityFramework/RowGenerators/TokenSearchParameterRowGenerator.cs` and siblings) — untouched, out of Phase D's scope, and its risk profile differs since EF's cache can never actually be observed partial.
- The pre-existing, already-documented `HardDeleteResourceAsync` legacy bug (`SqlEntityFrameworkRepository.cs:989-1026`) — unrelated, separately tracked.
- Adding on-demand, self-healing single-URI DB lookups on a cache miss (the currently-dead `GetSearchParamIdAsync` method). The reference implementation does not do this either — its correctness comes entirely from guaranteeing completeness up front, not from detect-and-recover. Matching that architecture is the goal; inventing a new capability neither implementation has is not.
- The `System`/`QuantityCode` on-demand caches (`GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync`) — these are correctly guarded (check-then-check-under-lock already) and unrelated to this bug.
- The dual-cache transitional risk between the new write-side cache and the existing EF-based read-side cache (accepted, documented risk from the original Phase D design doc) — unaffected by this fix.

## Testing

1. **New concurrency regression test**: a differential/integration test that fires multiple concurrent first-ever writes against a cold `SqlServerSearchIndexReferenceDataCache` (via `TestTenantDatabase`, bypassing the factory's eager warm-up to exercise the lazy single-flight path directly) and asserts every expected search-parameter row lands in the TVP output — the class of bug the existing differential harness structurally cannot catch, since it never compares against a live search.
2. **Targeted E2E re-run**: the specific failing tests already identified (`SortTests.GivenPatients_WhenSearchedWithSortByLastUpdated...` and others) re-run against a fresh database to confirm the fix against the real symptom, not just the new unit-level regression test.
3. **Full E2E suite re-run**: after the targeted fix is confirmed, the full `test/Ignixa.Api.E2ETests` suite (previously 163 passed / 437 failed / 20 skipped) re-run to confirm the failure count drops to (ideally) zero new failures attributable to this bug. Any remaining failures get triaged separately — this fix addresses the search-parameter cache race specifically, not every possible E2E gap.
4. **Existing differential suite**: re-run to confirm no regression from the `Count`-check-to-`Ensure*PreloadedAsync` refactor (65/65 previously green).

## Files Touched

- `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs` — add `EnsureResourceTypesPreloadedAsync`/`EnsureSearchParametersPreloadedAsync`.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs` — replace the two `Count == 0` guards with calls to the new `Ensure*PreloadedAsync` methods.
- `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` — add the eager, uncapped search-parameter preload call at cache construction (~line 345).
- `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/*.cs` — 15 files, add a warning log call at each `TryGetSearchParamId` miss site.
- New test file(s) under `test/Ignixa.DataLayer.SqlServer.IntegrationTests/` for the concurrency regression test.
