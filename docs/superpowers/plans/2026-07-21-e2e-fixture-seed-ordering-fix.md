# E2E Fixture Seed-Ordering Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix a confirmed E2E test regression where `IgnixaApiFixture` seeds the search-parameter catalog into the database *after* triggering the SqlServer write-side cache's eager warm-up, causing that cache to permanently lock in "loaded" with zero rows and silently drop every subsequent write's search-index rows.

**Architecture:** Reorder `IgnixaApiFixture.SyncBaseSearchParametersAsync` to seed the database via a standalone, throwaway `SearchIndexReferenceDataCache`/`FhirDbContext` pair — bypassing the tenant factory entirely — before ever calling `GetSearchIndexReferenceCacheAsync`, which is what constructs tenant 1's factory for the first time and triggers the eager warm-up. Entirely confined to the test fixture; no production code changes.

**Tech Stack:** C#/.NET 10, EF Core, xUnit, real SQL Server E2E tests via `WebApplicationFactory`.

## Global Constraints

- Full technical background and root cause: `docs/superpowers/specs/2026-07-21-e2e-fixture-seed-ordering-fix-design.md`. Every task's requirements implicitly include that document.
- **Single file only**: `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs`. Do not touch `SqlServerSearchIndexReferenceDataCache.cs`, `SqlServerMergeRepository.cs`, `SqlEntityFrameworkRepositoryFactory.cs`, or any other file from the earlier cache-race-fix plan — that work is correct, already reviewed twice, and unaffected by this bug.
- Do not add retry/resilience semantics to any cache — the explicit, user-chosen fix is reordering the fixture's own calls, not changing cache behavior.
- No audit of production tenant-onboarding code paths for the same ordering risk — explicitly out of scope, deferred to a future investigation if it ever manifests there.
- Executes directly on the current branch/worktree (`.claude/worktrees/ignixa-datalayer-sqlserver`, branch `worktree-ignixa-datalayer-sqlserver`) — no new worktree.
- This fix is small and entirely test-infrastructure — lower risk than the production write-path work earlier this session. No separate plan-level Fable review this time; still use subagent-driven-development with task-scoped review, and a final review before this is considered done.
- Test environment: E2E tests need `TEST_SQL_CONNECTION_STRING` containing `Database=`/`Initial Catalog=` (unlike the DataLayer integration tests' fixture, which builds its own name) plus `SqlServer__AutomaticSchemaDeploymentEnabled=true`. On this machine, `dotnet build`/`dotnet test` also need `Platform`/`__DOTNET_PREFERRED_BITNESS`/`__DOTNET_ADD_32BIT` unset first (stray shell env vars unrelated to this project).

---

### Task 1: Fix the fixture's seed ordering + targeted verification

**Files:**
- Modify: `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs`

**Interfaces:**
- Consumes: `SearchIndexReferenceDataCache(FhirDbContext context, ILogger<SearchIndexReferenceDataCache> logger)` (existing constructor, `Ignixa.DataLayer.SqlEntityFramework.Indexing` namespace), `FhirDbContext(DbContextOptions<FhirDbContext> options)` (existing constructor, `Ignixa.DataLayer.SqlEntityFramework` namespace, already imported in this file), `SearchIndexReferenceDataCache.SyncSearchParametersToDatabase(IReadOnlyList<string> urls, ISearchParameterDefinitionManager manager)` (existing method, unchanged).
- Produces: nothing new for other tasks — Task 2 just re-runs the full E2E suite against this change.

Current code (`test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs`, lines 6-22 for usings, lines 249-275 for the method):

```csharp
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Harness;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
```

```csharp
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

- [ ] **Step 1: Add the two new usings**

Add `using Ignixa.DataLayer.SqlEntityFramework.Indexing;`, `using Microsoft.EntityFrameworkCore;`, and `using Microsoft.Extensions.Logging;` to the usings block (alphabetical order, matching the existing list's convention):

```csharp
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Harness;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Specification.Generated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
```

`Microsoft.EntityFrameworkCore.SqlServer` (needed for the `.UseSqlServer(...)` extension method used in Step 2) does not need its own `using` — it's an extension method on `DbContextOptionsBuilder<T>`, which lives in the `Microsoft.EntityFrameworkCore` namespace already added above. The package itself is already referenced transitively through this project's reference to `Ignixa.DataLayer.SqlEntityFramework`.

- [ ] **Step 2: Replace `SyncBaseSearchParametersAsync`'s body**

Replace the method shown above with:

```csharp
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

        // Seed the database via a standalone cache instance BEFORE ever calling
        // GetSearchIndexReferenceCacheAsync -- that call constructs tenant 1's factory for the
        // first time, which eagerly warms the separate SqlServer write-side cache. If the
        // database is still empty when that happens, the write-side cache locks in "loaded"
        // with zero rows and never recovers (see
        // docs/superpowers/specs/2026-07-21-e2e-fixture-seed-ordering-fix-design.md).
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

- [ ] **Step 3: Build**

Run: `env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet build test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj`

Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Targeted re-run against a fresh scratch database**

```bash
export TEST_SQL_CONNECTION_STRING="Server=localhost;Database=IgnixaE2ESeedOrderFix_$(date +%s);Trusted_Connection=True;TrustServerCertificate=True"
export SqlServer__AutomaticSchemaDeploymentEnabled=true
env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~SortTests"
```

Expected: a dramatic improvement over the previous baseline (20 failed / 2 passed / 22 total, including the substring-matched `ChainingAndSortTests`) — most or all of these should now pass. Record the exact real numbers; if any still fail, read their actual failure messages before assuming they're unrelated to this fix (they might not be — this is the same class of symptom this whole investigation started from).

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs
git commit -m "fix(e2e): seed the search-parameter catalog before constructing tenant 1's factory"
```

---

### Task 2: Full E2E suite re-run

**Files:**
- None modified — this task is verification only, using the same connection string and database Task 1's Step 4 already deployed and populated (reuse it, don't create a new one, so the full suite benefits from the already-deployed schema and already-synced catalog).

**Interfaces:**
- Consumes: the fixture fix from Task 1. No new interfaces.
- Produces: a final pass/fail/skip count for the whole suite, the real acid test for whether the root-cause diagnosis in the design doc was correct.

- [ ] **Step 1: Full E2E suite re-run**

Reuse the exact `TEST_SQL_CONNECTION_STRING`/`SqlServer__AutomaticSchemaDeploymentEnabled` env vars from Task 1's Step 4 (same shell session, or re-export the identical values — do not generate a new timestamp/database name, reusing the already-deployed-and-seeded database avoids paying schema-deployment cost twice and is a more realistic steady-state test):

```bash
env -u Platform -u __DOTNET_PREFERRED_BITNESS -u __DOTNET_ADD_32BIT dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```

This is a real ~620-test SQL Server run — expect several minutes. Run it synchronously in the foreground and wait for it to actually finish; do not background it and report before it completes.

Expected: failure count drops substantially from the previous baseline (163 passed / 437 failed / 20 skipped / 620 total). Record the exact, unrounded pass/fail/skip/total numbers — do not round or approximate, per this initiative's standing testing discipline. If failures remain, list the failing test names by class (a table, like the previous investigation's report) and note whether their failure messages match the "empty search results" signature this fix targets or look like a genuinely different, unrelated issue — do not silently fold unrelated failures into "done," and do not attempt to fix them in this task.

- [ ] **Step 2: No commit needed**

This task makes no code changes — it is pure verification. If Step 1 reveals the fix doesn't work as expected (failure count doesn't meaningfully improve), do not proceed to mark this task complete — report BLOCKED with the real numbers and the actual failure messages from a sample of still-failing tests, so the root cause can be re-examined rather than assumed correct.

---

## Post-Plan: Final Review

After Task 2, dispatch a final review (matching this initiative's standing process) covering both tasks' combined diff (`IgnixaApiFixture.cs` only) against the base commit. Given this fix is test-infrastructure-only with no production code touched, the review can be lighter-weight than the production write-path reviews earlier this session, but should still confirm: the fix genuinely addresses the root cause (not just "tests pass now" — trace the actual mechanism), no production files were touched, and the E2E suite's real failure-count improvement is genuine and well-understood (not, for example, a side effect of running against a warmer/already-partially-seeded database from Task 1's run masking a different problem).
