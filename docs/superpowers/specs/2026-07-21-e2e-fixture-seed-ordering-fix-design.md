# E2E Fixture Seed-Ordering Fix — Design

**Status:** Approved by user, 2026-07-21. Fixes a confirmed regression discovered while re-verifying the search-parameter cache race fix (`docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md`).

## Background

After the cache-race-fix plan landed (commit `7c495484`, final review "Ready to merge: Yes"), a follow-up investigation into why `Ignixa.Api.E2ETests` still showed 437/620 failures found the true root cause: not a read-side bug, but a startup-ordering race between the E2E test fixture and this morning's own fix.

## Root Cause

`IgnixaApiFixture.SyncBaseSearchParametersAsync()` (`test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs:249-275`) does this, in order:

```csharp
var repositoryFactory = Services.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();
var referenceDataCache = await repositoryFactory.GetSearchIndexReferenceCacheAsync(1, CancellationToken.None);
var syncedCount = await referenceDataCache.SyncSearchParametersToDatabase(searchParamUrls, searchParamManager);
```

`GetSearchIndexReferenceCacheAsync` is the *first* call that touches tenant 1's `TenantServiceFactory`. On that first call, `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` constructs it — and, per this morning's Task 3, eagerly and synchronously warms the separate `SqlServerSearchIndexReferenceDataCache` (the write-side cache) by calling `PreloadResourceTypesAsync`/`PreloadSearchParamsAsync(maxRows: null)` against the database. At that exact moment, `dbo.SearchParam`/`dbo.ResourceType` are still empty — schema was just deployed, nothing has been seeded yet. The eager warm-up loads zero rows.

This morning's *other* fix (commit `7c495484`) replaced the old `Count == 0` guard with a `volatile bool` completion flag, set `true` once a load completes — including a load that legitimately found nothing. The old guard's accidental behavior was to keep retrying on every subsequent call as long as the map stayed empty; that's what used to paper over this exact race. The new, correct-for-its-own-bug-class guard doesn't retry once the flag is set.

The very next line, `SyncSearchParametersToDatabase`, inserts 1405 real search-parameter rows into the database and into the EF-based *read-side* cache — but the SqlServer *write-side* cache, already marked "loaded" with zero entries, never sees them. Every resource written afterward through `SqlServerFhirRepository`/`SqlServerMergeRepository` gets zero search-parameter/resource-type mappings, so every row generator's cache-miss path fires (the `"-- row skipped"` warning this morning's Task 4 added) and all search-index rows are silently dropped for every write, for the rest of the test process's life. This explains all 437 currently-failing E2E search tests — confirmed end-to-end (generated SQL, direct DB queries, log traces) for `SortTests.GivenPatients_WhenSearchedWithSortByLastUpdated`.

`GetSearchIndexReferenceCacheAsync` can't simply be called *after* seeding, because `SyncSearchParametersToDatabase` is a method on the object it returns — the fixture needs *a* cache instance to seed with in the first place. This is a genuine chicken-and-egg ordering problem in the fixture, not a one-line reorder.

## Decision Record

- **Fix approach**: fix the ordering bug at its source (not add cache-level resilience/retry semantics). The user explicitly chose this over making the write-side cache retry on an empty load, accepting that it only covers the confirmed call site rather than being robust to any future ordering issue.
- **Scope**: the E2E test fixture only. No audit of real production tenant-onboarding paths for the same risk — explicitly deferred as a separate, future investigation if it ever manifests there.
- **No production code changes.** This entire fix lives in `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs`.

## Fix

Seed the database *before* the first call to `GetSearchIndexReferenceCacheAsync` (the call that triggers the tenant factory's construction and the SqlServer cache's eager warm-up), using a throwaway `SearchIndexReferenceDataCache` instance built directly — bypassing the factory and its singleton caching entirely, so it has no interaction with `SqlEntityFrameworkRepositoryFactory`'s tenant-factory lifecycle.

`SyncBaseSearchParametersAsync` changes from:

```csharp
var repositoryFactory = Services.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();
var referenceDataCache = await repositoryFactory.GetSearchIndexReferenceCacheAsync(1, CancellationToken.None);
var syncedCount = await referenceDataCache.SyncSearchParametersToDatabase(searchParamUrls, searchParamManager);
```

to: construct a standalone `FhirDbContext` directly from the fixture's own `_sqlConnectionString` (already used elsewhere in this file, e.g. `InitializeSqlDatabaseAsync`), wrap it in a throwaway `SearchIndexReferenceDataCache`, call `SyncSearchParametersToDatabase` on *that* instance to seed the database, dispose it, and only *then* call `repositoryFactory.GetSearchIndexReferenceCacheAsync(1, CancellationToken.None)` — which now constructs tenant 1's factory for the first time against an already-seeded database, so the SqlServer eager warm-up sees real data.

```csharp
private async Task SyncBaseSearchParametersAsync()
{
    var fhirVersionContext = Services.GetRequiredService<IFhirVersionContext>();
    var searchParamManager = fhirVersionContext.GetSearchParameterDefinitionManager(FhirVersion.R4);

    var searchParamUrls = searchParamManager.AllSearchParameters
        .Where(sp => sp.Url is not null)
        .Select(sp => sp.Url!.ToString())
        .Distinct()
        .ToList();

    // Seed the database via a standalone cache instance BEFORE ever calling
    // GetSearchIndexReferenceCacheAsync -- that call constructs tenant 1's factory for the
    // first time, which eagerly warms the separate SqlServer write-side cache. If the database
    // is still empty when that happens, the write-side cache locks in "loaded" with zero rows
    // and never recovers (see docs/superpowers/specs/2026-07-21-e2e-fixture-seed-ordering-fix-design.md).
    var dbContextOptions = new DbContextOptionsBuilder<FhirDbContext>()
        .UseSqlServer(_sqlConnectionString)
        .Options;
    int syncedCount;
    await using (var seedContext = new FhirDbContext(dbContextOptions))
    {
        var seedCache = new SearchIndexReferenceDataCache(
            seedContext,
            Services.GetRequiredService<ILoggerFactory>().CreateLogger<SearchIndexReferenceDataCache>());
        syncedCount = await seedCache.SyncSearchParametersToDatabase(searchParamUrls, searchParamManager);
    }

    // Now safe to construct tenant 1's factory -- the database is already seeded, so the
    // SqlServer write-side cache's eager warm-up (SqlEntityFrameworkRepositoryFactory.
    // CreateServiceFactory) sees the real catalog.
    var repositoryFactory = Services.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();
    await repositoryFactory.GetSearchIndexReferenceCacheAsync(1, CancellationToken.None);

    Console.WriteLine($"Synced {syncedCount} base search parameters to database ({searchParamUrls.Count} total)");
}
```

New usings needed in `IgnixaApiFixture.cs`: `Ignixa.DataLayer.SqlEntityFramework.Indexing` (for `SearchIndexReferenceDataCache`), `Microsoft.EntityFrameworkCore` (for `DbContextOptionsBuilder`).

## Explicitly Out of Scope

- Any change to `SqlServerSearchIndexReferenceDataCache`, `SqlServerMergeRepository`, or any other file touched by this morning's cache-race-fix plan — that fix's own correctness (verified by its own final review + re-review) is unaffected by this bug and unaffected by this fix.
- Auditing real production tenant-onboarding code paths for the same ordering risk — explicitly deferred.
- Adding retry/resilience semantics to the write-side cache's completion flags — explicitly rejected in favor of fixing the one confirmed ordering bug directly.

## Testing

1. Targeted re-run of `SortTests` (previously 20 failed / 2 passed out of 22, including the substring-matched `ChainingAndSortTests`) against a fresh scratch database — expect this to flip to mostly/all passing.
2. Full E2E suite re-run (previously 163 passed / 437 failed / 20 skipped / 620 total) — expect the failure count to drop substantially. This is the real acid test for whether this was the actual root cause; record exact numbers, don't round.
3. Confirm the E2E fixture's existing behavior (search parameters actually land in the database, `/metadata` etc. still work) is otherwise unchanged — this is a reordering, not a logic change, so no new test coverage is needed beyond the E2E suite itself re-passing.

## Files Touched

- `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` — the only file this plan touches.
