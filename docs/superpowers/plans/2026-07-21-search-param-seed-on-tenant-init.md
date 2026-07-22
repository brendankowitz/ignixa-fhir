# Seed Search Parameters on Tenant Factory Construction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix a confirmed bug affecting every fresh tenant (production onboarding and E2E tests alike): `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` constructs both reference-data caches before anything seeds the search-parameter catalog, so both caches lock in "loaded" with zero rows and never recover.

**Architecture:** Insert one call to the existing, already-idempotent `SearchIndexReferenceDataCache.SyncSearchParametersToDatabase` between the two cache-construction blocks inside `CreateServiceFactory` — the single shared construction path every tenant goes through. Delete the E2E test fixture's now-redundant manual seeding, since `CreateServiceFactory` does it correctly for every tenant automatically.

**Tech Stack:** C#/.NET 10, EF Core, xUnit, real SQL Server integration/E2E tests.

## Global Constraints

- Full technical background and root cause: `docs/superpowers/specs/2026-07-21-search-param-seed-on-tenant-init-design.md`. Every task's requirements implicitly include that document.
- **Only two files touched, total**: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` (the fix) and `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` (delete now-redundant code). Do not touch `SqlServerSearchIndexReferenceDataCache.cs`, `SqlServerMergeRepository.cs`, or any other file from the earlier cache-race-fix plan — that work is correct, twice-reviewed, and unaffected by this bug.
- No new conditional/"only if empty" gating — `SyncSearchParametersToDatabase` is already idempotent per-URL; call it unconditionally.
- `parameterManager` (type `SearchParameterDefinitionManager`, which implements `ISearchParameterDefinitionManager`) is already available at the insertion point in `CreateServiceFactory` — confirmed by re-reading the current file. No new dependencies or constructor changes needed anywhere.
- This project has `<ImplicitUsings>enable</ImplicitUsings>` — `System.Linq` (needed for `.Where`/`.Select`/`.Distinct`) does not need an explicit `using` statement in `SqlEntityFrameworkRepositoryFactory.cs`.
- This is shared production tenant-construction code (not test-infrastructure-only) — same risk class as this session's earlier write-path fixes. Plan-level Fable review required before execution begins (unlike the now-superseded fixture-only plan). Task-scoped review per task, final whole-branch review before done.
- Executes directly on the current branch/worktree (`.claude/worktrees/ignixa-datalayer-sqlserver`, branch `worktree-ignixa-datalayer-sqlserver`) — no new worktree.
- Environment notes for this machine: `dotnet build`/`dotnet test` need `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT` unset first (`env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT ...`). E2E tests additionally need `TEST_SQL_CONNECTION_STRING` containing `Database=`/`Initial Catalog=` plus `SqlServer__AutomaticSchemaDeploymentEnabled=true`.
- Two baselines to beat on the targeted `SortTests` re-run: the true pre-any-fix baseline (20 failed / 2 passed / 22 total) and the failed first attempt's worse result (21 failed / 1 passed / 22 total). This fix must substantially beat both, not just one.

---

### Task 1: Seed search parameters in `CreateServiceFactory`

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:328-329`

**Interfaces:**
- Consumes: `SearchIndexReferenceDataCache.SyncSearchParametersToDatabase(IEnumerable<string> searchParameterUrls, ISearchParameterDefinitionManager searchParamManager)` — existing method, unchanged, `Ignixa.DataLayer.SqlEntityFramework.Indexing` namespace (already imported in this file via `using Ignixa.DataLayer.SqlEntityFramework.Indexing;`).
- Produces: no new public surface. After this change, any tenant constructed via `CreateServiceFactory` (production onboarding, E2E tests, everything routing through `GetOrCreateFactoryAsync`) has its search-parameter catalog seeded and both reference-data caches correctly warm before either is ever handed to a caller.

Current code (`SqlEntityFrameworkRepositoryFactory.cs`, lines 324-329):

```csharp
        // Get or create cached definition managers (reused across tenants with same FHIR version)
        var (compartmentManager, parameterManager) = GetOrCreateDefinitionManagers(fhirSpec, schemaProvider);

        // Get tenant-specific cache instance (reused across all requests for this tenant)
        var searchIndexCache = _multiTenantCache.GetOrCreateCacheForTenant(tenantId, dbContextOptions);

```

- [ ] **Step 1: Insert the seeding call**

Insert the following immediately after line 328 (`var searchIndexCache = _multiTenantCache.GetOrCreateCacheForTenant(tenantId, dbContextOptions);`) and before the blank line/comment that precedes the SqlServer cache construction block:

```csharp
        // Get or create cached definition managers (reused across tenants with same FHIR version)
        var (compartmentManager, parameterManager) = GetOrCreateDefinitionManagers(fhirSpec, schemaProvider);

        // Get tenant-specific cache instance (reused across all requests for this tenant)
        var searchIndexCache = _multiTenantCache.GetOrCreateCacheForTenant(tenantId, dbContextOptions);

        // Seed the search-parameter catalog for this tenant's FHIR version before either
        // reference-data cache is trusted for reads. SyncSearchParametersToDatabase is idempotent
        // per-URL (checks for an existing row before inserting) and updates searchIndexCache's own
        // in-memory dictionary as it goes, so this both seeds the database AND leaves the read-side
        // cache correctly warm -- one call covers both. Cheap on a restart of an already-seeded
        // tenant (one SELECT, no writes). Without this, a freshly-deployed database's caches (this
        // one and the SqlServer write-side cache constructed below) would warm to empty and never
        // recover -- see docs/superpowers/specs/2026-07-21-search-param-seed-on-tenant-init-design.md.
        var searchParamUrls = parameterManager.AllSearchParameters
            .Where(sp => sp.Url is not null)
            .Select(sp => sp.Url!.ToString())
            .Distinct()
            .ToList();
        var syncedSearchParamCount = searchIndexCache.SyncSearchParametersToDatabase(searchParamUrls, parameterManager).GetAwaiter().GetResult();
        logger.LogInformation(
            "Search parameter catalog synced for tenant {TenantId}: {SyncedCount} of {TotalCount} URLs",
            tenantId,
            syncedSearchParamCount,
            searchParamUrls.Count);

```

- [ ] **Step 2: Build**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet build src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj`

Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run the EF integration test suite**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT TEST_SQL_CONNECTION_STRING="Server=localhost;Trusted_Connection=True;TrustServerCertificate=True" dotnet test test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj`

This project exercises `SqlEntityFrameworkRepositoryFactory`'s construction path directly (it contains `CompiledSearchEndToEndTests.cs`, `StringSearchParamReadPathTests.cs`, `TestSchemaInitializer.cs`, among others). Record the real pass/fail/skip counts and compare against whatever the pre-existing baseline turns out to be when you run it — if it was already failing/skipping some tests before this change (unrelated, pre-existing), that's fine; what matters is no NEW failures caused by this change. If you can't tell whether a failure is pre-existing, check it out on `git stash` against the base commit and re-run before concluding it's this change's fault.

- [ ] **Step 4: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
git commit -m "fix(datalayer-sqlentityframework): seed search parameters during tenant factory construction"
```

---

### Task 2: Delete the E2E fixture's now-redundant seeding

**Files:**
- Modify: `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs:186-275`

**Interfaces:**
- Consumes: Task 1's fix (by the time `Client = CreateClient()` returns, tenant 1's search-parameter catalog is already seeded).
- Produces: nothing new for Task 3 — Task 3 just re-runs the full E2E suite against both tasks' combined changes.

Current code (`IgnixaApiFixture.cs`, lines 186-275):

```csharp
    public async Task InitializeAsync()
    {
        // Initialize SQL database if using SQL Server mode
        if (UseSqlServer)
        {
            await InitializeSqlDatabaseAsync();
        }

        // Create HTTP client and store for test access
        Client = CreateClient();

        // Sync base search parameters to database for SQL Server mode
        // This ensures search parameters like _tag, address-city, etc. are present
        // before tests run. Without this, searches will fail with "SearchParamId not found".
        if (UseSqlServer)
        {
            await SyncBaseSearchParametersAsync();
        }

        // Fetch /metadata once and cache it
        var metadataResponse = await Client.GetAsync("/metadata");
        metadataResponse.EnsureSuccessStatusCode();

        var metadataJson = await metadataResponse.Content.ReadAsStringAsync();
        var capability = JsonSourceNodeFactory.Parse<CapabilityStatementJsonNode>(metadataJson);

        // Parse FHIR version from capability statement
        FhirVersion = ParseFhirVersion(capability);

        // Create version-specific schema provider
        SchemaProvider = CreateSchemaProvider(FhirVersion);

        // Initialize SearchTestHarness with cached capability
        Harness = new SearchTestHarness(Client, SchemaProvider, capability);
    }

    private async Task InitializeSqlDatabaseAsync()
    {
        var dbName = ExtractDatabaseName(_sqlConnectionString);

        // Create database if not exists - replace "Database=" or "Initial Catalog=" with "Initial Catalog=master"
        var masterConnStr = Regex.Replace(
            _sqlConnectionString,
            @"(Database|Initial\s+Catalog)=[^;]+",
            "Initial Catalog=master",
            RegexOptions.IgnoreCase);
        await using var masterConn = new SqlConnection(masterConnStr);
        await masterConn.OpenAsync();

        await using var cmd = masterConn.CreateCommand();
        // CA2100 suppressed: dbName comes from test configuration (environment variable or generated GUID),
        // not user input. This is safe in test fixture context.
#pragma warning disable CA2100
        cmd.CommandText = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{dbName}') CREATE DATABASE [{dbName}]";
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Syncs base FHIR search parameters to the database.
    /// CRITICAL: SQL Server mode requires search parameters to be registered in the SearchParam table
    /// before searches can work. Without this, queries fail with "SearchParamId not found for parameter..."
    /// </summary>
    private async Task SyncBaseSearchParametersAsync()
    {
        // Get the search parameter definition manager from the application services
        // This contains all base FHIR search parameters from the pre-generated code
        var fhirVersionContext = Services.GetRequiredService<IFhirVersionContext>();
        var searchParamManager = fhirVersionContext.GetSearchParameterDefinitionManager(FhirVersion.R4);

        // Get all search parameter URLs from base spec
        var searchParamUrls = searchParamManager.AllSearchParameters
            .Where(sp => sp.Url is not null)
            .Select(sp => sp.Url!.ToString())
            .Distinct()
            .ToList();

        // Get the repository factory to access the reference data cache
        var repositoryFactory = Services.GetRequiredService<SqlEntityFrameworkRepositoryFactory>();

        // Get the reference data cache for tenant 1 (the E2E test tenant)
        var referenceDataCache = await repositoryFactory.GetSearchIndexReferenceCacheAsync(1, CancellationToken.None);

        // Sync search parameters to database
        var syncedCount = await referenceDataCache.SyncSearchParametersToDatabase(
            searchParamUrls,
            searchParamManager);

        Console.WriteLine($"Synced {syncedCount} base search parameters to database ({searchParamUrls.Count} total)");
    }
```

- [ ] **Step 1: Remove the now-redundant call site**

In `InitializeAsync`, replace:

```csharp
        // Create HTTP client and store for test access
        Client = CreateClient();

        // Sync base search parameters to database for SQL Server mode
        // This ensures search parameters like _tag, address-city, etc. are present
        // before tests run. Without this, searches will fail with "SearchParamId not found".
        if (UseSqlServer)
        {
            await SyncBaseSearchParametersAsync();
        }

        // Fetch /metadata once and cache it
```

with:

```csharp
        // Create HTTP client and store for test access. In SQL Server mode, tenant 1's search
        // parameter catalog is already seeded by the time this returns -- SqlEntityFrameworkRepositoryFactory.
        // CreateServiceFactory does it during construction now (see
        // docs/superpowers/specs/2026-07-21-search-param-seed-on-tenant-init-design.md), so this
        // fixture no longer needs its own separate seeding step.
        Client = CreateClient();

        // Fetch /metadata once and cache it
```

- [ ] **Step 2: Delete the now-unused `SyncBaseSearchParametersAsync` method entirely**

Remove the whole method (the `/// <summary>...` doc comment through the closing `}` shown in the "Current code" block above, the block starting at `/// <summary>` and ending right before `private static string ExtractDatabaseName`).

- [ ] **Step 3: Check for now-unused usings**

`SyncBaseSearchParametersAsync` was the only caller of `Services.GetRequiredService<IFhirVersionContext>()`/`Services.GetRequiredService<SqlEntityFrameworkRepositoryFactory>()` in this file — check whether removing it leaves any now-unused `using` directives (e.g. if `IFhirVersionContext` or `SqlEntityFrameworkRepositoryFactory`'s namespace was only imported for this method). Build in the next step will surface this as a warning if the project treats unused usings as a warning; if it doesn't, do a quick visual check of the usings block against what's still referenced elsewhere in the file before committing.

- [ ] **Step 4: Build**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet build test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj`

Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Targeted `SortTests` re-run against a fresh scratch database**

```bash
export TEST_SQL_CONNECTION_STRING="Server=localhost;Database=IgnixaE2ESeedFix2_$(date +%s);Trusted_Connection=True;TrustServerCertificate=True"
export SqlServer__AutomaticSchemaDeploymentEnabled=true
env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~SortTests"
```

Expected: a dramatic improvement over BOTH prior results — the true baseline (20 failed / 2 passed / 22 total) and the failed first attempt (21 failed / 1 passed / 22 total, worse than baseline). Record the exact real numbers. If results still look bad, do not proceed to Task 3 or commit — report BLOCKED with the real numbers and a sample of actual failure messages, so the diagnosis can be re-examined rather than assumed correct.

- [ ] **Step 6: Commit**

```bash
git add test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs
git commit -m "test(e2e): remove now-redundant search-parameter seeding from the fixture"
```

---

### Task 3: Full E2E suite re-run

**Files:**
- None modified — this task is verification only, reusing the same scratch database Task 2's Step 5 deployed and seeded (same `TEST_SQL_CONNECTION_STRING`/`SqlServer__AutomaticSchemaDeploymentEnabled` env vars, not a fresh one — avoids paying schema-deployment cost twice and better reflects steady-state behavior).

**Interfaces:**
- Consumes: both tasks' combined fix. No new interfaces.
- Produces: a final pass/fail/skip count for the whole suite — the real acid test for whether this diagnosis is correct.

- [ ] **Step 1: Full E2E suite re-run**

Reuse the exact `TEST_SQL_CONNECTION_STRING`/`SqlServer__AutomaticSchemaDeploymentEnabled` values from Task 2's Step 5 (same shell session, or re-export the identical values):

```bash
env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```

This is a real ~620-test SQL Server run — expect several minutes. Run it synchronously in the foreground and wait for it to actually finish; do not background it and report before it completes.

Expected: failure count drops substantially from the baseline (163 passed / 437 failed / 20 skipped / 620 total). Record the exact, unrounded pass/fail/skip/total numbers. If failures remain, list the failing test names by class and note whether their failure messages match the "empty search results" signature this fix targets or look like a genuinely different, unrelated issue — do not silently fold unrelated failures into "done," and do not attempt to fix them in this task.

- [ ] **Step 2: No commit needed**

This task makes no code changes — it is pure verification. If Step 1 reveals the fix doesn't work as expected (failure count doesn't meaningfully improve, or a THIRD attempt has now also failed), do not proceed to mark this task complete — report BLOCKED with the real numbers and the actual failure messages from a sample of still-failing tests. Per systematic-debugging's process, two fix attempts (the reverted fixture-only one, plus whatever this plan represents) will have been tried by this point; a third failure means the architecture, not just the fix, needs to be questioned with the user before attempting a fourth.

---

## Post-Plan: Final Review

After Task 3, dispatch a final review (matching this initiative's standing process) covering all 3 tasks' combined diff against the base commit. This touches shared production tenant-construction code used by every real tenant in the system (not test-infrastructure-only) — the review should explicitly confirm: the insertion point in `CreateServiceFactory` is genuinely correct (does it really run before both caches would otherwise see incomplete data — trace it, don't just trust the tests passing), the E2E fixture deletion doesn't silently lose any behavior other tests implicitly depended on, and there's no other real caller of `CreateServiceFactory`/`GetOrCreateFactoryAsync` this plan should have accounted for and didn't (e.g. confirm this code path is genuinely SqlServer/SqlEntityFramework-storage-specific and the FileSystem-storage tenant path is unaffected by construction, not by oversight).
