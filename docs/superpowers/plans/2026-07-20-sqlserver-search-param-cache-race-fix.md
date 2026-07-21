# SqlServer Search-Parameter Cache Race Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix a confirmed production write-path regression where a racy, unlocked "populate once" cache guard in `Ignixa.DataLayer.SqlServer`'s search-parameter reference-data cache silently drops search-index rows for concurrently-written resources, causing search-after-write to return empty or wrong results.

**Architecture:** Restore the completeness guarantee the EF reference implementation already has (cache is always fully populated before any caller can observe it) by (1) eagerly, synchronously, and completely warming the cache at construction time in the factory, (2) making the lazy fallback guard genuinely race-free via proper double-checked locking encapsulated inside the cache class itself, and (3) turning a genuine remaining cache miss from a silent drop into an observable warning log. No behavior changes to the EF reference implementation.

**Tech Stack:** C#/.NET 10, ADO.NET (`Microsoft.Data.SqlClient`), xUnit + Shouldly, real SQL Server integration tests via `TestTenantDatabase`.

## Global Constraints

- Full technical background and rationale: `docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md`. Every task's requirements implicitly include that document.
- `SqlServerSearchIndexReferenceDataCache`'s `_dbLock` (a `SemaphoreSlim(1, 1)`) is **not reentrant**. `PreloadResourceTypesAsync`/`PreloadSearchParamsAsync` already acquire it internally. The new `EnsureResourceTypesPreloadedAsync`/`EnsureSearchParametersPreloadedAsync` methods must acquire `_dbLock` themselves and call an **extracted, non-locking** loader body directly — never call the public `Preload*Async` methods while already holding the lock, and never call them from inside `Ensure*PreloadedAsync` at all.
- The public `Preload*Async` methods keep their exact current signatures and behavior (unconditional reload, their own locking) — they are still used directly by the eager factory warm-up (Task 3) and by every existing test that calls them.
- Do not touch `Ignixa.DataLayer.SqlEntityFramework`'s `RowGenerators/*.cs`, `Indexing/SearchIndexReferenceDataCache.cs`, or `Indexing/MultiTenantSearchIndexCache.cs`. The EF reference implementation's behavior — including its own unlogged silent-continue-on-miss — is intentionally left unchanged.
- Do not touch `HardDeleteResourceAsync` or any code related to the already-documented legacy `HardDeleteResourceAsync` bug — unrelated.
- Do not add on-demand, self-healing single-URI DB lookups on a cache miss (`GetSearchParamIdAsync` stays dead code, exactly as today). The fix is prevention (guaranteed completeness up front), matching the reference implementation's actual strategy — not detection-and-recovery.
- Do not touch `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` — already correctly double-checked-locked, unrelated to this bug.
- The real call-site count for `SearchParameterIdLookupHelper.TryGetSearchParamId` is **16 call sites across 14 files** under `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/` (re-confirmed by grep during this plan's writing — the design doc's "15" was an approximation). `SearchParameterIdLookupHelper.cs` itself is not one of the 14 — it defines the method, it doesn't call it.
- This is production write-path code (the same class of risk as Phase D itself, and this literally is Phase D's own code being corrected). Every task gets a task-scoped review; the whole branch gets a final review on the most capable available model; the plan itself gets a Fable-model review before execution begins, per the user's standing instruction for this initiative's high-risk write-path work.
- Executes directly on the current branch/worktree (`.claude/worktrees/ignixa-datalayer-sqlserver`, branch `worktree-ignixa-datalayer-sqlserver`) — no new worktree.
- Test environment: `TEST_SQL_CONNECTION_STRING` must be set (see `docker-compose.test.yml`); `TestTenantDatabase.CreateEmptyAsync()`/`CreateSqlServerFhirRepositoryAsync()` provision real, uniquely-named scratch databases per test.

---

### Task 1: Race-free `Ensure*PreloadedAsync` on `SqlServerSearchIndexReferenceDataCache`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs`

**Interfaces:**
- Produces: `Task EnsureResourceTypesPreloadedAsync(CancellationToken cancellationToken)` and `Task EnsureSearchParametersPreloadedAsync(CancellationToken cancellationToken)` on `SqlServerSearchIndexReferenceDataCache` — race-free, safe to call from multiple concurrent callers on a cold cache, idempotent (a no-op if already populated).
- Consumes: nothing new; the class's existing `_resourceTypeCache`, `_searchParamCache`, `_dbLock`, `_sqlExecutionService`, `tenantId`, `_logger` fields.

The current file (`Indexing/SqlServerSearchIndexReferenceDataCache.cs`) has this at lines 51-111:

```csharp
    public async Task PreloadResourceTypesAsync(CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var command = new SqlCommand("SELECT ResourceTypeId, Name FROM dbo.ResourceType");
            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId,
                command,
                reader => (Id: reader.GetInt16(0), Name: reader.GetString(1)),
                cancellationToken);

            foreach (var row in rows)
            {
                _resourceTypeCache[row.Name] = row.Id;
            }

            _logger.LogInformation("Preloaded {Count} resource types into cache", rows.Count);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task PreloadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            var commandText = maxRows.HasValue
                ? "SELECT TOP (@MaxRows) SearchParamId, Uri FROM dbo.SearchParam"
                : "SELECT SearchParamId, Uri FROM dbo.SearchParam";

            using var command = new SqlCommand(commandText);
            if (maxRows.HasValue)
            {
                command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = maxRows.Value;
            }

            var rows = await _sqlExecutionService.ExecuteReaderAsync(
                tenantId,
                command,
                reader => (Id: reader.GetInt16(0), Uri: reader.GetString(1)),
                cancellationToken);

            foreach (var row in rows)
            {
                _searchParamCache[row.Uri] = row.Id;
            }

            _logger.LogInformation(
                "Preloaded {Count} search parameters into cache{MaxRowsInfo}",
                rows.Count,
                maxRows.HasValue ? $" (limited to {maxRows.Value} rows)" : string.Empty);
        }
        finally
        {
            _dbLock.Release();
        }
    }
```

- [ ] **Step 1: Write the failing concurrency test**

Open `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs`. Add this `[Fact]` inside the existing `SqlServerSearchIndexReferenceDataCacheTests` class (after the existing tests, before the closing brace):

```csharp
    [Fact]
    public async Task GivenAColdCache_WhenEnsureSearchParametersPreloadedAsyncCalledConcurrently_ThenEveryParameterIsLoadedForEveryCaller()
    {
        // A small seeded catalog wouldn't reliably expose the race (the population loop finishes
        // before a second caller's check can land in the gap) -- the real production bug only
        // manifested against the real ~1400-row catalog. 200 rows widens the population loop's
        // duration enough to make a still-broken guard fail this test reliably, not flakily.
        const int SearchParamCount = 200;
        var values = string.Join(",", Enumerable.Range(0, SearchParamCount)
            .Select(i => $"('http://example.org/ensure-test-param-{i}', 'active', SYSDATETIMEOFFSET(), 0)"));
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES {values}");

        var callers = Enumerable.Range(0, 20)
            .Select(_ => _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None));
        await Task.WhenAll(callers);

        _cache.SearchParameterMappings.Count.ShouldBe(SearchParamCount);
        for (var i = 0; i < SearchParamCount; i++)
        {
            _cache.SearchParameterMappings.ContainsKey($"http://example.org/ensure-test-param-{i}")
                .ShouldBeTrue($"parameter index {i} must be present -- a race would drop some entries");
        }
    }

    [Fact]
    public async Task GivenAColdCache_WhenEnsureResourceTypesPreloadedAsyncCalledConcurrently_ThenEveryResourceTypeIsLoadedForEveryCaller()
    {
        var callers = Enumerable.Range(0, 20)
            .Select(_ => _cache.EnsureResourceTypesPreloadedAsync(CancellationToken.None));
        await Task.WhenAll(callers);

        _cache.ResourceTypeMappings.ContainsKey("Patient").ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAWarmCache_WhenEnsureSearchParametersPreloadedAsyncCalledAgain_ThenItIsANoOp()
    {
        // Seed one row BEFORE the first call -- otherwise the cache starts and stays genuinely
        // empty (0 rows in dbo.SearchParam), which is indistinguishable from "still cold" and the
        // guard would legitimately reload on the second call too, making this test assert nothing
        // about the no-op behavior it's meant to prove.
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            "VALUES ('http://example.org/warm-before-first-call', 'active', SYSDATETIMEOFFSET(), 0)");

        await _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);
        var countAfterFirstCall = _cache.SearchParameterMappings.Count;

        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            "VALUES ('http://example.org/added-after-warm', 'active', SYSDATETIMEOFFSET(), 0)");

        await _cache.EnsureSearchParametersPreloadedAsync(CancellationToken.None);

        // Count is unchanged -- Ensure* is a "populate if empty" guard, not a refresh. The newly
        // inserted row is invisible to this cache instance until something explicitly reloads it;
        // that is existing, intentional cache behavior, not something this fix changes.
        _cache.SearchParameterMappings.Count.ShouldBe(countAfterFirstCall);
    }
```

Add `using System.Linq;` at the top of the file if not already present (needed for `Enumerable.Range`).

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheTests"`

Expected: compile error — `EnsureSearchParametersPreloadedAsync`/`EnsureResourceTypesPreloadedAsync` do not exist on `SqlServerSearchIndexReferenceDataCache`.

- [ ] **Step 3: Extract non-locking loader bodies and add the `Ensure*PreloadedAsync` methods**

In `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`, replace the two methods shown above (lines 51-111) with:

```csharp
    public async Task PreloadResourceTypesAsync(CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await LoadResourceTypesAsync(cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Ensures resource-type mappings are loaded, race-free under concurrent callers on a cold
    /// cache. Unlike a bare <c>ResourceTypeMappings.Count == 0</c> check (the bug this method
    /// fixes -- see docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md),
    /// the emptiness check and the load happen under the same lock: a concurrent caller arriving
    /// mid-load blocks on <see cref="_dbLock"/> until the in-flight load finishes, instead of
    /// reading a partially-populated dictionary. A no-op if the cache is already populated.
    /// </summary>
    public async Task EnsureResourceTypesPreloadedAsync(CancellationToken cancellationToken)
    {
        if (_resourceTypeCache.Count > 0)
        {
            return;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_resourceTypeCache.Count > 0)
            {
                return;
            }

            await LoadResourceTypesAsync(cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Query-and-populate body shared by <see cref="PreloadResourceTypesAsync"/> and
    /// <see cref="EnsureResourceTypesPreloadedAsync"/>. Does NOT acquire <see cref="_dbLock"/> --
    /// both callers already hold it. Never call this directly from outside those two methods.
    /// </summary>
    private async Task LoadResourceTypesAsync(CancellationToken cancellationToken)
    {
        using var command = new SqlCommand("SELECT ResourceTypeId, Name FROM dbo.ResourceType");
        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            reader => (Id: reader.GetInt16(0), Name: reader.GetString(1)),
            cancellationToken);

        foreach (var row in rows)
        {
            _resourceTypeCache[row.Name] = row.Id;
        }

        _logger.LogInformation("Preloaded {Count} resource types into cache", rows.Count);
    }

    public async Task PreloadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)
    {
        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            await LoadSearchParamsAsync(maxRows, cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Ensures search-parameter mappings are loaded, race-free under concurrent callers on a cold
    /// cache -- see <see cref="EnsureResourceTypesPreloadedAsync"/>'s remarks; same shape, same bug
    /// fixed. Always loads the full catalog uncapped (<c>maxRows: null</c>) -- unlike the capped
    /// call <see cref="PreloadSearchParamsAsync"/> makes elsewhere, this mirrors the reference EF
    /// implementation's uncapped <c>SearchIndexReferenceDataCache.InitializeAsync</c>. A no-op if
    /// the cache is already populated.
    /// </summary>
    public async Task EnsureSearchParametersPreloadedAsync(CancellationToken cancellationToken)
    {
        if (_searchParamCache.Count > 0)
        {
            return;
        }

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            if (_searchParamCache.Count > 0)
            {
                return;
            }

            await LoadSearchParamsAsync(maxRows: null, cancellationToken);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Query-and-populate body shared by <see cref="PreloadSearchParamsAsync"/> and
    /// <see cref="EnsureSearchParametersPreloadedAsync"/>. Does NOT acquire <see cref="_dbLock"/> --
    /// both callers already hold it. Never call this directly from outside those two methods.
    /// </summary>
    private async Task LoadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)
    {
        var commandText = maxRows.HasValue
            ? "SELECT TOP (@MaxRows) SearchParamId, Uri FROM dbo.SearchParam"
            : "SELECT SearchParamId, Uri FROM dbo.SearchParam";

        using var command = new SqlCommand(commandText);
        if (maxRows.HasValue)
        {
            command.Parameters.Add("@MaxRows", SqlDbType.Int).Value = maxRows.Value;
        }

        var rows = await _sqlExecutionService.ExecuteReaderAsync(
            tenantId,
            command,
            reader => (Id: reader.GetInt16(0), Uri: reader.GetString(1)),
            cancellationToken);

        foreach (var row in rows)
        {
            _searchParamCache[row.Uri] = row.Id;
        }

        _logger.LogInformation(
            "Preloaded {Count} search parameters into cache{MaxRowsInfo}",
            rows.Count,
            maxRows.HasValue ? $" (limited to {maxRows.Value} rows)" : string.Empty);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheTests"`

Expected: all tests pass, including the 3 new ones (existing tests in this file must still pass unchanged — `PreloadResourceTypesAsync`/`PreloadSearchParamsAsync`'s public signatures and behavior are unchanged).

- [ ] **Step 5: Run the full existing test file plus the full integration test project once**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`

Expected: no regressions (previously 65/65 passing — confirm the count is still 65 plus this task's 3 new tests, i.e. 68/68).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs
git commit -m "fix(sqlserver): add race-free Ensure*PreloadedAsync to the reference-data cache"
```

---

### Task 2: `SqlServerMergeRepository` — replace the racy guards

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs:149-160`

**Interfaces:**
- Consumes: `EnsureResourceTypesPreloadedAsync`/`EnsureSearchParametersPreloadedAsync` from Task 1.
- Produces: no new public surface; `MergeResourcesAsync`'s external behavior for correct, non-racing callers is unchanged (it already fully preloads before use — the fix only closes the race window that undermined that guarantee under concurrency).

Current code in `SqlServerMergeRepository.cs` (lines 149-160):

```csharp
        // Ensure cache is preloaded for small reference data (if not already done)
        if (_referenceDataCache.ResourceTypeMappings.Count == 0)
        {
            _logger.LogInformation("Preloading resource type mappings");
            await _referenceDataCache.PreloadResourceTypesAsync(cancellationToken);
        }

        if (_referenceDataCache.SearchParameterMappings.Count == 0)
        {
            _logger.LogInformation("Preloading search parameter mappings (limited to 10,000 rows)");
            await _referenceDataCache.PreloadSearchParamsAsync(maxRows: 10000, cancellationToken);
        }
```

- [ ] **Step 1: Replace the guards**

Replace the block above with:

```csharp
        // Ensure cache is fully and safely preloaded before reading from it below. A bare
        // Count == 0 check here (the previous code) raced against concurrent callers on a cold
        // cache and could silently read a partially-populated dictionary -- see
        // docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md.
        // EnsureResourceTypesPreloadedAsync/EnsureSearchParametersPreloadedAsync are race-free.
        await _referenceDataCache.EnsureResourceTypesPreloadedAsync(cancellationToken);
        await _referenceDataCache.EnsureSearchParametersPreloadedAsync(cancellationToken);
```

- [ ] **Step 2: Build**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the existing `SqlServerMergeRepositoryTests` and `SqlServerFhirRepositoryCrudTests` to confirm no regression**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerMergeRepositoryTests|FullyQualifiedName~SqlServerFhirRepositoryCrudTests"`

Expected: all pass, same as before this change (single-threaded call patterns are behaviorally identical — the guard now goes through `Ensure*PreloadedAsync` instead of inline `Count == 0` + `Preload*Async`, but does the same work for a non-racing caller).

- [ ] **Step 4: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs
git commit -m "fix(sqlserver): use race-free Ensure*PreloadedAsync in MergeResourcesAsync"
```

---

### Task 3: Eager, uncapped warm-up at cache construction

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:339-345`

**Interfaces:**
- Consumes: `PreloadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken)` (existing, unchanged signature, from `SqlServerSearchIndexReferenceDataCache`).
- Produces: no new public surface. After this change, every `SqlServerSearchIndexReferenceDataCache` this factory constructs is fully warm (both resource types and search parameters) before it is handed to any `createRepository`/`createSearchService` closure.

Current code (`SqlEntityFrameworkRepositoryFactory.cs`, lines 339-345):

```csharp
#pragma warning disable CA2000 // Dispose objects before losing scope
        var sqlServerSearchIndexCache = new SqlServerSearchIndexReferenceDataCache(
            _sqlExecutionService,
            tenantId,
            _loggerFactory.CreateLogger<SqlServerSearchIndexReferenceDataCache>());
#pragma warning restore CA2000
        sqlServerSearchIndexCache.PreloadResourceTypesAsync(CancellationToken.None).GetAwaiter().GetResult();
```

- [ ] **Step 1: Add the eager search-parameter warm-up call**

Replace the block above with:

```csharp
#pragma warning disable CA2000 // Dispose objects before losing scope
        var sqlServerSearchIndexCache = new SqlServerSearchIndexReferenceDataCache(
            _sqlExecutionService,
            tenantId,
            _loggerFactory.CreateLogger<SqlServerSearchIndexReferenceDataCache>());
#pragma warning restore CA2000
        sqlServerSearchIndexCache.PreloadResourceTypesAsync(CancellationToken.None).GetAwaiter().GetResult();
        // Eager, uncapped warm-up -- mirrors MultiTenantSearchIndexCache.GetOrCreateCacheForTenant's
        // InitializeAsync() call for the EF-based searchIndexCache above: guarantees no caller of
        // this factory's createRepository delegate ever observes a partially-populated cache. See
        // docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md.
        sqlServerSearchIndexCache.PreloadSearchParamsAsync(maxRows: null, CancellationToken.None).GetAwaiter().GetResult();
```

- [ ] **Step 2: Build**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj`

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the existing factory-level tests to confirm no regression**

Run: `dotnet test src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.Tests.csproj --filter "FullyQualifiedName~SqlEntityFrameworkRepositoryFactory"`

If no such filtered tests exist, run the project's full suite instead: `dotnet test src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.Tests.csproj` and confirm no new failures relative to the pre-change baseline.

Expected: no regressions. This step is timing-sensitive to watch, not just pass/fail — note the wall-clock duration this test run takes, for Step 4.

- [ ] **Step 4: Note the eager warm-up's cost, don't just assume it's negligible**

`dbo.SearchParam` is a small table (1405 rows in the real catalog) and this call happens once per tenant, at first factory construction for that tenant (`TenantServiceFactory` is itself cached in `_factoryCache`, so this cost is not paid per-request). Confirm this by re-reading the surrounding `GetOrCreateFactoryAsync`/`_factoryCache` logic (`SqlEntityFrameworkRepositoryFactory.cs`, search for `_factoryCache.GetOrAdd`) before treating it as harmless — if this eager block is ever reached on a hot path instead of once-per-tenant-ever, that changes the risk calculus and should be flagged in this task's report rather than assumed away.

- [ ] **Step 5: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
git commit -m "fix(sqlserver): eagerly warm the full search-parameter catalog at cache construction"
```

---

### Task 4: Loud logging on a genuine search-parameter cache miss

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ISearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ReferenceSearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/StringSearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/NumberSearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/QuantitySearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/DateTimeSearchParameterRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenTextRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/RefTokenCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenTokenCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenDateTimeCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenQuantityCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenStringCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenNumberNumberCompositeRowGenerator.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/TokenSearchParameterRowGenerator.cs` (2 call sites: `GenerateSqlDataRecords` and `ExtractExtensionData`)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/UriSearchParameterRowGenerator.cs` (2 call sites: `GenerateSqlDataRecords` and `ExtractExtensionData`)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs` (16 call sites passing the new `logger` argument)

**Interfaces:**
- Consumes: nothing new from earlier tasks.
- Produces: `ISearchParameterRowGenerator.GenerateSqlDataRecords` gains a 5th parameter, `ILogger logger`, as its last parameter. All 12 implementing classes' `GenerateSqlDataRecords`, plus `TokenSearchParameterRowGenerator`/`UriSearchParameterRowGenerator`'s `GenerateSqlDataRecords` and `ExtractExtensionData` (neither of which is part of the interface), gain the same `ILogger logger` last parameter. `SearchParameterIdLookupHelper.TryGetSearchParamId`'s own signature is unchanged — the log call is added at each of the 16 call sites, not inside the helper.

This is a single mechanical pattern applied at all 16 call sites (confirmed by grep — see Global Constraints). Two fully worked examples below (the simplest single-call-site case, and the two-call-site case), then the complete list of the other 12 files with their exact current line numbers — apply the identical pattern to each.

- [ ] **Step 1: Update the interface**

In `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ISearchParameterRowGenerator.cs`, add `using Microsoft.Extensions.Logging;` to the usings, and change:

```csharp
    IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap);
```

to:

```csharp
    IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap,
        ILogger logger);
```

Also add a `<param name="logger">` doc-comment line matching the existing doc-comment style for the other four parameters.

- [ ] **Step 2: Worked example — single-call-site file (`ReferenceSearchParameterRowGenerator.cs`)**

Add `using Microsoft.Extensions.Logging;` to the usings. Change the method signature (line 20-24) from:

```csharp
    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap)
```

to:

```csharp
    public IEnumerable<SqlDataRecord> GenerateSqlDataRecords(
        IReadOnlyList<ResourceWrapper> resources,
        IReadOnlyDictionary<string, short> resourceTypeIdMap,
        IReadOnlyDictionary<string, short> searchParameterIdMap,
        IReadOnlyDictionary<ResourceWrapper, long> resourceSurrogateIdMap,
        ILogger logger)
```

Change the miss site (line 57-58) from:

```csharp
                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                    continue;
```

to:

```csharp
                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                {
                    logger.LogWarning(
                        "SearchParamId not found in cache for {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} -- row skipped",
                        searchIndex.SearchParameter.Url, resource.ResourceType, resource.ResourceId);
                    continue;
                }
```

Use this exact log message text — word for word, including "-- row skipped" — at every one of the 16 call sites in this task, with no per-file customization. A uniform message is deliberate: it makes every occurrence a literal copy-paste of the same four lines, which is easy to verify by grep (`grep -rn "row skipped" RowGenerators/` should return exactly 16 hits when this task is done) and removes any judgment call about how to phrase each site.

Apply this exact two-part pattern (signature + brace-and-log-wrapped miss site, verbatim as shown above) to the remaining 11 single-call-site files below. Each has exactly one `TryGetSearchParamId` call, at the given line:

- `StringSearchParameterRowGenerator.cs:75`
- `NumberSearchParameterRowGenerator.cs:64`
- `QuantitySearchParameterRowGenerator.cs:83`
- `DateTimeSearchParameterRowGenerator.cs:56`
- `TokenTextRowGenerator.cs:54`
- `RefTokenCompositeRowGenerator.cs:68`
- `TokenTokenCompositeRowGenerator.cs:66`
- `TokenDateTimeCompositeRowGenerator.cs:66`
- `TokenQuantityCompositeRowGenerator.cs:73`
- `TokenStringCompositeRowGenerator.cs:66`
- `TokenNumberNumberCompositeRowGenerator.cs:71`

Each of these 11 classes implements `ISearchParameterRowGenerator` and has exactly one `GenerateSqlDataRecords` method with the same 4-parameter signature shown in Step 1's "before" — add the `ILogger logger` 5th parameter, `using Microsoft.Extensions.Logging;`, and the same brace-and-log wrapping at its one `TryGetSearchParamId` site.

- [ ] **Step 3: Worked example — two-call-site file (`UriSearchParameterRowGenerator.cs`)**

Add `using Microsoft.Extensions.Logging;`. This file has two methods, `GenerateSqlDataRecords` (line 22-26) and `ExtractExtensionData` (line 74-78) — `ExtractExtensionData` is not part of `ISearchParameterRowGenerator`, so its signature change is not compiler-enforced; do it explicitly. Add `ILogger logger` as the last parameter to both signatures, exactly as Step 2. Wrap both miss sites (line 54-55 in `GenerateSqlDataRecords`, line 100-101 in `ExtractExtensionData`) the same way, e.g. for the `ExtractExtensionData` site:

```csharp
                if (!SearchParameterIdLookupHelper.TryGetSearchParamId(searchIndex.SearchParameter, searchParameterIdMap, out var searchParamId))
                {
                    logger.LogWarning(
                        "SearchParamId not found in cache for {SearchParameterUrl} while indexing {ResourceType}/{ResourceId} -- row skipped",
                        searchIndex.SearchParameter.Url, resource.ResourceType, resource.ResourceId);
                    continue;
                }
```

Use the same exact "-- row skipped" message text here too (not a different phrase for extension-data methods) — uniform across all 16 sites, per Step 2's note.

- [ ] **Step 4: `TokenSearchParameterRowGenerator.cs` — the other two-call-site file**

Add `using Microsoft.Extensions.Logging;`. This class does not implement `ISearchParameterRowGenerator` either (it's constructed and called directly by type in `SqlServerMergeRepository.cs`). It has `GenerateSqlDataRecords` (line 34-38, miss site at line 73-74) and `ExtractExtensionData` (line 120-124, miss site at line 150-151). Apply the identical signature-and-miss-site pattern to both, using the same uniform "-- row skipped" message text from Step 2 in both places.

- [ ] **Step 5: Update all 16 call sites in `SqlServerMergeRepository.cs`**

`SqlServerMergeRepository` already has `_logger` (an `ILogger<SqlServerMergeRepository>`, assignable to the `ILogger` parameter these methods now take) in scope. Update the 14 `GenerateSqlDataRecords` calls (lines 185, 187, 189, 190, 191, 192, 193, 194, 198, 199, 200, 201, 202, 203) and 2 `ExtractExtensionData` calls (lines 311-312, 313-314) to pass `_logger` as the final argument. For example, line 185:

```csharp
        var referenceSearchParams = MaterializeIfNotEmpty(_referenceRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap));
```

becomes:

```csharp
        var referenceSearchParams = MaterializeIfNotEmpty(_referenceRowGenerator.GenerateSqlDataRecords(resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger));
```

Apply the same `, _logger` insertion (before the closing paren of the `GenerateSqlDataRecords(...)`/`ExtractExtensionData(...)` call, not the outer `MaterializeIfNotEmpty(...)`/`.ToList()` wrapper) at every one of the other 15 call sites listed above. For the two `ExtractExtensionData` calls (lines 311-314), which span two lines each:

```csharp
        var tokenExtensions = _tokenRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap).ToList();
        var uriExtensions = _uriRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap).ToList();
```

becomes:

```csharp
        var tokenExtensions = _tokenRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToList();
        var uriExtensions = _uriRowGenerator.ExtractExtensionData(
            resources, resourceTypeIdMap, searchParameterIdMap, resourceSurrogateIdMap, _logger).ToList();
```

- [ ] **Step 6: Build**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`

Expected: 0 warnings, 0 errors. A missed call site (in either a row-generator file or `SqlServerMergeRepository.cs`) shows up here as a compile error (wrong argument count) — treat any such error as a sign a site was missed, not something to silently work around.

- [ ] **Step 7: Write a test proving the log fires on a genuine miss**

Add to `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryTests.cs`, inside the existing `SqlServerMergeRepositoryTests` class:

```csharp
    [Fact]
    public async Task GivenATokenSearchIndexWithAnUnregisteredSearchParameterUrl_WhenMerged_ThenAWarningIsLoggedAndTheRowIsSkipped()
    {
        var logger = new ListLogger<SqlServerMergeRepository>();
        var repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            _cache, new SqlServerPostMergeExtensionUpdater(
                _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            logger);

        var (transactionId, _) = await repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        var resourceJson = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"test-patient-unregistered-param"}""");
        var searchParameter = new SearchParameterInfo(
            "not-registered", "not-registered", SearchParamType.Token,
            new Uri("http://example.org/not-a-real-search-parameter"));
        var tokenValue = new TokenSearchValue(
            system: null, code: "some-code", text: null, identifierTypeSystem: null, identifierTypeCode: null);
        var wrapper = new ResourceWrapper(
            "Patient", "test-patient-unregistered-param", "1", DateTimeOffset.UtcNow, resourceJson,
            new ResourceRequest("PUT", "Patient/test-patient-unregistered-param"))
        {
            SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)]
        };

        await repository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        logger.Warnings.ShouldContain(w => w.Contains("http://example.org/not-a-real-search-parameter"));
        var rowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= " + transactionId);
        rowCount.ShouldBe(0);
    }
```

This test needs a minimal in-memory `ILogger<T>` test double that records warning messages, since `Microsoft.Extensions.Logging.Abstractions.NullLogger` discards everything. Add this small class to the bottom of `SqlServerMergeRepositoryTests.cs`, outside the test class:

```csharp
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}
```

Add `using Microsoft.Extensions.Logging;` to the top of the test file if not already present (it likely already has `Microsoft.Extensions.Logging.Abstractions` for `NullLogger` — `ILogger<T>`/`LogLevel`/`EventId` live in the non-`.Abstractions` `Microsoft.Extensions.Logging` namespace, so this is a genuinely new using).

- [ ] **Step 8: Run the new test and the full existing test file**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerMergeRepositoryTests"`

Expected: all pass, including the new one.

- [ ] **Step 9: Run the full differential and integration suite**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`

Expected: no regressions (baseline from Task 1's Step 5, plus this task's 1 new test).

- [ ] **Step 10: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryTests.cs
git commit -m "fix(sqlserver): log a warning instead of silently dropping a row on a search-param cache miss"
```

---

### Task 5: End-to-end concurrency regression test + full verification pass

**Files:**
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryConcurrentColdCacheTests.cs`

**Interfaces:**
- Consumes: `TestTenantDatabase.CreateSqlServerFhirRepositoryAsync()` (existing), `SqlServerMergeRepository.BeginTransactionAsync`/`MergeResourcesAsync`/`CommitTransactionAsync` (existing, from Task 2's fixed guards).
- Produces: nothing new for later tasks — this is the last task in the plan.

This is the test that most directly reproduces the originally-observed production bug: concurrent first-writes against a cold cache (the fixture from `TestTenantDatabase.CreateSqlServerFhirRepositoryAsync()` deliberately only eagerly preloads resource types, not search parameters — see `Fixtures/TestTenantDatabase.cs:131`, unchanged by this plan — so it still exercises the lazy `Ensure*PreloadedAsync` path from Task 1/2, not Task 3's eager factory warm-up).

- [ ] **Step 1: Write the new test file**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryConcurrentColdCacheTests.cs`:

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// Reproduces the originally-observed production regression: concurrent first-writes against a
/// cold SqlServerSearchIndexReferenceDataCache silently dropped search-parameter rows for
/// whichever parameters hadn't finished loading yet (see
/// docs/superpowers/specs/2026-07-20-sqlserver-search-param-cache-race-fix-design.md). This test
/// exercises SqlServerMergeRepository.MergeResourcesAsync directly (not just the cache in
/// isolation, unlike SqlServerSearchIndexReferenceDataCacheTests' concurrency tests), through the
/// same TestTenantDatabase fixture used by every other SqlServerMergeRepository test -- which
/// deliberately does not eagerly preload search parameters, so the lazy Ensure*PreloadedAsync path
/// is what's actually under test here.
/// </summary>
public sealed class SqlServerMergeRepositoryConcurrentColdCacheTests : IAsyncLifetime
{
    // 200 rows widens PreloadSearchParamsAsync's population loop enough that a still-broken guard
    // would reliably observe a partial dictionary mid-load -- the real production bug only
    // manifested against the real ~1400-row catalog, not a handful of seeded rows.
    private const int SearchParamCount = 200;
    private const int ConcurrentWriteCount = 40;

    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        var values = string.Join(",", Enumerable.Range(0, SearchParamCount)
            .Select(i => $"('{ConcurrencyTestSearchParamUrl(i)}', 'active', SYSDATETIMEOFFSET(), 0)"));
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES {values}");
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static string ConcurrencyTestSearchParamUrl(int index) =>
        $"http://example.org/concurrency-test-param-{index}";

    [Fact]
    public async Task GivenAColdCache_WhenManyResourcesAreMergedConcurrently_ThenEveryTokenSearchParamRowLands()
    {
        var writes = Enumerable.Range(0, ConcurrentWriteCount).Select(async i =>
        {
            var paramIndex = i % SearchParamCount;
            var searchParameter = new SearchParameterInfo(
                $"concurrency-test-param-{paramIndex}",
                $"concurrency-test-param-{paramIndex}",
                SearchParamType.Token,
                new Uri(ConcurrencyTestSearchParamUrl(paramIndex)));
            var tokenValue = new TokenSearchValue(
                system: null, code: $"concurrency-code-{i}", text: null,
                identifierTypeSystem: null, identifierTypeCode: null);

            var resourceId = $"concurrency-test-patient-{i}";
            var resourceJson = ResourceJsonNode.Parse(
                $$"""{"resourceType":"Patient","id":"{{resourceId}}"}""");
            var wrapper = new ResourceWrapper(
                "Patient", resourceId, "1", DateTimeOffset.UtcNow, resourceJson,
                new ResourceRequest("PUT", $"Patient/{resourceId}"))
            {
                SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)]
            };

            var (transactionId, _) = await _database.MergeRepository.BeginTransactionAsync(
                resourceCount: 1, CancellationToken.None);
            await _database.MergeRepository.MergeResourcesAsync(
                transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
            await _database.MergeRepository.CommitTransactionAsync(
                transactionId, cancellationToken: CancellationToken.None);
        });

        await Task.WhenAll(writes);

        var tokenRowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE Code LIKE 'concurrency-code-%'");
        tokenRowCount.ShouldBe(ConcurrentWriteCount,
            "every concurrently-merged resource's token search parameter must land -- a lower " +
            "count means the cache raced and silently dropped rows for a search parameter that " +
            "hadn't finished loading yet");
    }
}
```

- [ ] **Step 2: Run the new test**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerMergeRepositoryConcurrentColdCacheTests"`

Expected: PASS. If this fails, that means the race is not actually fixed — re-check Task 1/2's implementation before proceeding; do not adjust this test's assertions to accommodate a failure.

- [ ] **Step 3: Run the full differential and integration suite one more time**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`

Expected: no regressions (baseline from Task 4's Step 9, plus this task's 1 new test).

- [ ] **Step 4: Targeted E2E re-run against the specific tests that failed before this fix**

Ensure the E2E environment is configured (see this session's established workaround: `TEST_SQL_CONNECTION_STRING` pointing at a fresh scratch database, `SqlServer__AutomaticSchemaDeploymentEnabled=true`).

Run: `dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~SortTests"`

Expected: all pass (previously failing, e.g. `GivenPatients_WhenSearchedWithSortByLastUpdated...` returning `results.Length == 0`, `GivenPatientsWithIncludes_...` returning an empty bundle).

- [ ] **Step 5: Full E2E suite re-run**

Run: `dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj`

Expected: failure count drops from the pre-fix baseline (163 passed / 437 failed / 20 skipped, out of 620) to ideally 0 new failures attributable to this bug. Record the actual pass/fail/skip counts precisely in this task's report — do not round or approximate. Any remaining failures not attributable to the search-parameter cache race are out of this task's scope; list them by name for the user to triage separately, do not silently fold them into "done" or attempt to fix them here.

- [ ] **Step 6: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryConcurrentColdCacheTests.cs
git commit -m "test(sqlserver): reproduce the search-param cache race via concurrent cold-cache writes"
```

---

## Post-Plan: Final Whole-Branch Review and Merge/Push

After Task 5, dispatch a final whole-branch review (most capable available model) covering all 5 tasks' combined diff against the merge-base, per this initiative's standing process. Given this fix directly follows and corrects a "Ready to merge: Yes" verdict that was retracted, the final review should explicitly re-examine whether the fix actually closes the race (not just whether the new tests pass) and whether any of the 16 row-generator files were missed in Task 4's sweep. Only after that review is clean (or its findings are fixed and re-reviewed) should the user be asked the two standing questions: merge into `feature/fhir-to-sql-compiler`, and push to `origin/worktree-ignixa-datalayer-sqlserver`.
