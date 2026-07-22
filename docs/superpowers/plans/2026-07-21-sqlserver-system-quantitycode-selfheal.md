# SqlServer System/QuantityCode Self-Healing Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix a confirmed, pre-existing bug live since Phase D's original write-path port: `SystemMappings`/`QuantityCodeMappings` are never populated by anything in the SqlServer write path, silently dropping every token/quantity search value that carries a `System` (including `_tag` and `gender`).

**Architecture:** Port EF's proven `LazyLoadingDictionary` pattern as a generic `OnDemandResolvingDictionary<TKey,TValue>` wrapper inside `SqlServerSearchIndexReferenceDataCache`, used only for `SystemMappings`/`QuantityCodeMappings`. On a cache miss, it synchronously resolves via the cache's own already-correct `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` methods, caches the result, and logs a warning on failure. Zero changes to any row generator.

**Tech Stack:** C#/.NET 10, ADO.NET, xUnit + Shouldly, real SQL Server integration tests.

## Global Constraints

- Full technical background and rationale: `docs/superpowers/specs/2026-07-21-sqlserver-system-quantitycode-selfheal-design.md`. Every task's requirements implicitly include that document.
- **Single production file**: `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`. Do not touch any row-generator file, `SqlServerMergeRepository.cs`, `SqlServerFhirRepository.cs`, or anything from either of today's two earlier fix plans.
- **Do not touch `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs`.** There is an existing, separate, uncommitted, already-task-reviewed-correct edit in that file (from a different, still-in-progress plan) — do not commit it, revert it, or modify it. This plan's own E2E test runs will run against that uncommitted state, which is expected and correct.
- Log level for a resolve failure: `LogWarning` (explicit user choice, matching this morning's Task 4 precedent for the `SearchParamId`-miss case), not EF's `LogError`.
- `GetOrCreateSystemIdAsync(string? systemUri, CancellationToken cancellationToken)` returns `Task<int>` and `GetOrCreateQuantityCodeIdAsync(string? code, CancellationToken cancellationToken)` returns `Task<int>` — both already exist, already correctly double-checked-locked against `_dbLock`, unchanged by this plan.
- Executes directly on the current branch/worktree (`.claude/worktrees/ignixa-datalayer-sqlserver`, branch `worktree-ignixa-datalayer-sqlserver`) — no new worktree.
- This is production write-path code, same risk class as today's other two fixes. Plan-level Fable review required before execution begins. Task-scoped review per task, final whole-branch review before done.
- Environment notes for this machine: `dotnet build`/`dotnet test` need `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT` unset first. E2E tests additionally need `TEST_SQL_CONNECTION_STRING` containing `Database=`/`Initial Catalog=` plus `SqlServer__AutomaticSchemaDeploymentEnabled=true`.

---

### Task 1: `OnDemandResolvingDictionary` wrapper + cache-level tests

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs`

**Interfaces:**
- Produces: `internal sealed class OnDemandResolvingDictionary<TKey, TValue>` (nested inside `SqlServerSearchIndexReferenceDataCache`) — `internal`, not `private`, specifically so this task's failure-path test can construct it directly with a fake, intentionally-throwing `resolveAsync` delegate (the real `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` essentially never throw under normal test conditions — there's no clean way to force a real DB failure deterministically, so testing the failure path requires constructing the wrapper directly against a fake resolver). `InternalsVisibleTo` for `Ignixa.DataLayer.SqlServer.IntegrationTests` already exists in the csproj — no project changes needed.
- Consumes: `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` (existing, unchanged), `_systemCache`/`_quantityCodeCache` (existing fields, unchanged), `_logger` (existing field, unchanged).

Current code (`SqlServerSearchIndexReferenceDataCache.cs`, lines 58-63):

```csharp
    // No sentinel concept for System/QuantityCode (only ever populated with real, on-demand-created
    // IDs), so the backing ConcurrentDictionary can be returned directly -- a genuinely live view,
    // not a copy, matching SqlServerMergeRepository's expectation that it stays live.
    public IReadOnlyDictionary<string, int> SystemMappings => _systemCache;

    public IReadOnlyDictionary<string, int> QuantityCodeMappings => _quantityCodeCache;
```

The `SentinelFilteringDictionary` private nested class at the bottom of the same file (lines 452-487) is the pattern this task's new wrapper follows for its `IReadOnlyDictionary<TKey,TValue>` member implementations:

```csharp
    private sealed class SentinelFilteringDictionary(ConcurrentDictionary<string, short> inner) : IReadOnlyDictionary<string, short>
    {
        public short this[string key] => TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

        public IEnumerable<string> Keys => this.Select(kvp => kvp.Key);

        public IEnumerable<short> Values => this.Select(kvp => kvp.Value);

        public int Count => inner.Count(kvp => kvp.Value != MissingSentinel);

        public bool ContainsKey(string key) => inner.TryGetValue(key, out var value) && value != MissingSentinel;

        public bool TryGetValue(string key, out short value)
        {
            if (inner.TryGetValue(key, out var found) && found != MissingSentinel)
            {
                value = found;
                return true;
            }

            value = default;
            return false;
        }

        public IEnumerator<KeyValuePair<string, short>> GetEnumerator()
            => inner.Where(kvp => kvp.Value != MissingSentinel).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
```

- [ ] **Step 1: Write the failing tests**

Add these three `[Fact]` methods to `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs`, inside the existing `SqlServerSearchIndexReferenceDataCacheTests` class (after the existing tests, before the closing brace):

```csharp
    [Fact]
    public async Task GivenAColdSystemMappingsCache_WhenTryGetValueMissesOnAnUnknownSystemUri_ThenItResolvesInsertsAndCachesTheNewSystem()
    {
        var systemUri = $"http://example.org/self-heal-system-{Guid.NewGuid()}";

        var found = _cache.SystemMappings.TryGetValue(systemUri, out var resolvedId);

        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE SystemId = {resolvedId}");
        rowCount.ShouldBe(1);

        // Second lookup on the SAME cache instance must hit the now-warm dictionary, not re-resolve.
        var secondLookupFound = _cache.SystemMappings.TryGetValue(systemUri, out var secondResolvedId);
        secondLookupFound.ShouldBeTrue();
        secondResolvedId.ShouldBe(resolvedId);
    }

    [Fact]
    public async Task GivenAColdQuantityCodeMappingsCache_WhenTryGetValueMissesOnAnUnknownCode_ThenItResolvesInsertsAndCachesTheNewCode()
    {
        var code = $"self-heal-code-{Guid.NewGuid():N}";

        var found = _cache.QuantityCodeMappings.TryGetValue(code, out var resolvedId);

        found.ShouldBeTrue();
        resolvedId.ShouldBeGreaterThan(0);

        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.QuantityCode WHERE QuantityCodeId = {resolvedId}");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public void GivenAResolverThatThrows_WhenTryGetValueMisses_ThenAWarningIsLoggedAndFalseIsReturned()
    {
        var backingCache = new ConcurrentDictionary<string, int>();
        var logger = new ListLogger<SqlServerSearchIndexReferenceDataCache>();
        var wrapper = new SqlServerSearchIndexReferenceDataCache.OnDemandResolvingDictionary<string, int>(
            backingCache,
            (_, _) => Task.FromException<int>(new InvalidOperationException("simulated resolve failure")),
            logger);

        var found = wrapper.TryGetValue("any-key", out var value);

        found.ShouldBeFalse();
        value.ShouldBe(0);
        logger.Warnings.ShouldContain(w => w.Contains("any-key"));
        backingCache.ContainsKey("any-key").ShouldBeFalse();
    }
```

This test file needs two new usings. `SqlServerMergeRepositoryTests.cs` (Task 4 of this morning's cache-race-fix plan) already defines `internal sealed class ListLogger<T> : ILogger<T>` in namespace `Ignixa.DataLayer.SqlServer.IntegrationTests` — reuse it directly rather than duplicating it; `internal` types are visible across files in the same assembly given a `using` for their namespace. Add `using System.Collections.Concurrent;` (for `ConcurrentDictionary` in the third test) and `using Ignixa.DataLayer.SqlServer.IntegrationTests;` (to reach the existing `ListLogger<T>`, since this file's own namespace is the `.Indexing` sub-namespace) to the top of the test file.

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheTests"`

Expected: compile error — `SystemMappings`/`QuantityCodeMappings` don't yet self-heal on miss (the first two tests would fail, not error, against current code — but `OnDemandResolvingDictionary` doesn't exist yet, so the third test's reference to `SqlServerSearchIndexReferenceDataCache.OnDemandResolvingDictionary<string, int>` is a compile error).

- [ ] **Step 3: Implement the wrapper class**

In `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`, add this new nested class immediately after the existing `SentinelFilteringDictionary` class (i.e., just before the final closing `}` of `SqlServerSearchIndexReferenceDataCache` itself):

```csharp
    /// <summary>
    /// Read-only dictionary wrapper that resolves a cache miss on demand via <paramref name="resolveAsync"/>
    /// (synchronously, via <c>GetAwaiter().GetResult()</c> -- used from within the row generators'
    /// synchronous <c>TryGetValue</c> calls, where async/await isn't available). Ports EF's
    /// <c>LazyLoadingDictionary</c> pattern (Ignixa.DataLayer.SqlEntityFramework.Indexing.
    /// SearchIndexReferenceDataCache) for <see cref="SystemMappings"/>/<see cref="QuantityCodeMappings"/>
    /// specifically -- see docs/superpowers/specs/2026-07-21-sqlserver-system-quantitycode-selfheal-design.md
    /// for why this was missing and why it's safe (bounded blocking cost, no deadlock against
    /// <see cref="_dbLock"/>, since row generation always runs after Ensure*PreloadedAsync has already
    /// released it). Internal, not private, so tests can construct it directly with a fake resolver to
    /// exercise the failure path deterministically -- <see cref="GetOrCreateSystemIdAsync"/>/
    /// <see cref="GetOrCreateQuantityCodeIdAsync"/> essentially never throw under normal test conditions.
    /// </summary>
    internal sealed class OnDemandResolvingDictionary<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        Func<TKey, CancellationToken, Task<TValue>> resolveAsync,
        ILogger logger) : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull
    {
        public TValue this[TKey key] => TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");

        public IEnumerable<TKey> Keys => cache.Keys;

        public IEnumerable<TValue> Values => cache.Values;

        public int Count => cache.Count;

        public bool ContainsKey(TKey key) => cache.ContainsKey(key);

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

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => cache.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
```

- [ ] **Step 4: Wire `SystemMappings`/`QuantityCodeMappings` to the new wrapper**

Replace the current property implementations shown above (lines 58-63) with:

```csharp
    public IReadOnlyDictionary<string, int> SystemMappings =>
        new OnDemandResolvingDictionary<string, int>(_systemCache, GetOrCreateSystemIdAsync, _logger);

    public IReadOnlyDictionary<string, int> QuantityCodeMappings =>
        new OnDemandResolvingDictionary<string, int>(_quantityCodeCache, GetOrCreateQuantityCodeIdAsync, _logger);
```

- [ ] **Step 5: Build**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`

Expected: 0 warnings, 0 errors. If a nullability warning appears on the `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync` method-group-to-delegate conversions (their real signatures declare `string?` parameters, while the delegate type declares non-nullable `string`), this is expected to be a safe, warning-free conversion (a method accepting a wider/nullable type is safely assignable to a delegate that only ever passes non-null values) — if the build disagrees, investigate rather than suppress.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT TEST_SQL_CONNECTION_STRING="Server=localhost;Trusted_Connection=True;TrustServerCertificate=True" dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheTests"`

Expected: all tests pass, including the 3 new ones (existing tests in this file must still pass unchanged).

- [ ] **Step 7: Run the full existing test file plus the full integration test project**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT TEST_SQL_CONNECTION_STRING="Server=localhost;Trusted_Connection=True;TrustServerCertificate=True" dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`

Expected: no regressions (previously 71/71 — confirm the count is still 71 plus this task's 3 new tests, i.e. 74/74).

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs
git commit -m "fix(sqlserver): self-heal SystemMappings/QuantityCodeMappings on cache miss"
```

---

### Task 2: Targeted `SortTests` + full E2E suite re-run

**Files:**
- None modified — this task is verification only.

**Interfaces:**
- Consumes: Task 1's fix. No new interfaces.
- Produces: the real acid-test result for whether this diagnosis is correct, and (indirectly) the trigger for the controller to go finish/commit the separate, still-pending seed-ordering plan's Task 2/3 once this confirms `_tag`-based tests actually pass now.

**IMPORTANT: do not touch `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs`.** There is an existing, separate, uncommitted, already-correct edit sitting in that file from a different plan — leave it exactly as you find it. Running the E2E suite against that uncommitted state is expected and correct; do not `git stash`, `git checkout --`, commit, or otherwise touch it.

- [ ] **Step 1: Targeted `SortTests` re-run against a fresh scratch database**

```bash
export TEST_SQL_CONNECTION_STRING="Server=localhost;Database=IgnixaE2ESelfHealFix_$(date +%s);Trusted_Connection=True;TrustServerCertificate=True"
export SqlServer__AutomaticSchemaDeploymentEnabled=true
env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~SortTests"
```

Expected: a dramatic improvement over the 20 failed / 2 passed / 22 total baseline (which held identically across both this session's prior fix attempts, since this System/QuantityCode bug was masking any improvement either of them made). Record the exact real numbers. If results still show no improvement, do not proceed to Step 2 — report BLOCKED with the real numbers and a sample of actual failure messages from still-failing tests, so the diagnosis can be re-examined rather than assumed correct.

- [ ] **Step 2: Full E2E suite re-run**

Reuse the exact `TEST_SQL_CONNECTION_STRING`/`SqlServer__AutomaticSchemaDeploymentEnabled` values from Step 1 (same shell session, or re-export the identical values):

```bash
env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```

This is a real ~620-test SQL Server run — expect several minutes. Run it synchronously in the foreground and wait for it to actually finish; do not background it and report before it completes.

Expected: failure count drops substantially from the baseline (163 passed / 437 failed / 20 skipped / 620 total). Record the exact, unrounded pass/fail/skip/total numbers. If failures remain, list the failing test names by class and note whether their failure messages look related to this specific bug (System/QuantityCode resolution) or are a genuinely different, unrelated issue — do not silently fold unrelated failures into "done," and do not attempt to fix them in this task.

- [ ] **Step 3: No commit needed**

This task makes no code changes — it is pure verification. If Step 1 or Step 2 reveals a problem, report BLOCKED with the real evidence rather than proceeding.

---

## Post-Plan: Final Review

After Task 2, dispatch a final review covering both tasks' combined diff against the base commit. Given this is the third fix in a row targeting E2E search failures today, the review should explicitly confirm: the `OnDemandResolvingDictionary`'s `IReadOnlyDictionary` implementation is complete and correct (no missing interface members), the concurrency-safety argument (no deadlock against `_dbLock`, bounded blocking cost) holds against the real current code, and this genuinely required zero row-generator changes as claimed. Once this final review is clean, report the full picture to the controller/user — including that this unblocks the separate, still-pending seed-ordering plan's Task 2 (commit the already-correct fixture edit) and Task 3 (its own full E2E re-run), which should now finally show real improvement.
