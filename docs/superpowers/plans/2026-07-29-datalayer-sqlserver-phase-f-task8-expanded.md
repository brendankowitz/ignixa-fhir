# Phase F — Task 8 expanded: the composition root is not a relocation

## ADJUDICATED PREMISES (2026-07-29, after this plan was drafted)

Two investigations disagreed on three points. Each was settled from the code, and this plan is **wrong in two
places** as a result. Read this section before executing any task below.

**1. There are two reference-data caches per tenant, and they do not compete.** This plan says
`SqlServerSearchIndexReferenceDataCache` is "already the sole cache on write and search paths." That holds
only for the *request* path. `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` constructs both, per
tenant: the SqlServer one backs `SqlServerFhirRepository`/`SqlServerCompiledSearchService`, the EF one serves
package-load-time search-parameter catalog sync via `GetSearchIndexReferenceCacheAsync`. Disjoint duties, not
rivals. Task 8.2 still has to port the catalog-sync surface; there is no conflict to resolve.

**2. Revised-plan Task 5a was substantially executed — under a different filename.** This plan asserts no
`SqlServerServiceFactory` exists and concludes 5a is open. The class exists as
`Ignixa.DataLayer.SqlServer/SqlServerRepositoryFactory.cs`; its own doc calls itself the relocated
composition root, and `SqlEntityFrameworkRepositoryFactory` now calls into it rather than constructing types
inline — so what callers receive today is already `SqlServerFhirRepository` and
`SqlServerCompiledSearchService`. **Task 8.4 must be rescoped against what that class already does.** What
genuinely remains in the EF factory: the storage-type gate, the inline system-partition connection-string
inheritance, `ValidateManagedIdentityAuthentication`, and the deploy→upgrade→sync→preload ordering.

**3. Terminology never cut over, and that part stands.** `ValidationServicesRegistration.cs:110-123` still
builds the EF `SqlTerminologyService` and `HybridTerminologyService`; `ImportTerminologyResourceActivity`
still takes a `FhirDbContext`. `SqlServerTerminologyService` is referenced only by its own file, the csproj,
a plan doc, and the oracle fixture — so the 31 oracle facts pinning it, and the importer's 34, protect
nothing in production yet.

**The negative-lookup sentinel bug is live today, with a narrower trigger than stated below.** Confirmed:
`SqlServerSymbolResolver` → `TryGetSystemIdAsync` → `_systemCache[uri] = -1`, no TTL, no capacity bound, no
`ForgetMissingSystem` equivalent (EF has a 5-minute TTL *and* a cross-tenant broadcast).
`GetOrCreateSystemIdAsync` *is* sentinel-aware, so a later resource write for that system heals it. The
unhealable path: a system arriving via terminology import — which today runs the EF repository and broadcasts
only to the EF cache — that is never indexed by a resource write. Searches for it return nothing for the
process lifetime. Task 8.8 widens this from "terminology-only systems" to "any system created by import",
since partition-0 terminology and per-tenant search hold different cache instances. **8.3 before 8.8 is a
correctness constraint, not a preference.**

**`SqlReferenceDataPreloadHandler` has never run, settled by code.** Registered `AddSingleton<T>()` only;
Autofac's `AutofacRegistration` registers `descriptor.ServiceType` alone with no interface discovery; Medino
`PublishAsync` resolves `IEnumerable<INotificationHandler<T>>`, which is empty. Verified against the exact
restored binaries (Medino 2.0.7, Autofac.Extensions.DependencyInjection 10.0.0). Task 8.9's container test is
corroboration, not the load-bearing evidence.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Supersedes tasks 5a, 7 and 8 of** `docs/superpowers/plans/2026-07-28-datalayer-sqlserver-phase-f-revised.md`.
Tasks R, 5b and 6 of that document are done (see "What the branch actually executed" below) and are
unchanged. Tasks 9 and 10 of that document survive, with additions this plan hands them.

**Goal:** Leave `Ignixa.DataLayer.SqlEntityFramework` with zero production consumers, so that the revised
plan's Task 9 gate can pass and its Task 10 can delete the project. Every type, registration, hosted
service, event handler, DurableTask activity, configuration literal and `ProjectReference` that still
reaches into the EF assembly must be ported, rewritten or deliberately deleted first.

**Architecture:** The remaining EF surface is not a set of repositories behind Domain interfaces — those are
already ported. What is left is the **composition root and the things that reach through it**: a factory
that is simultaneously `IFhirRepositoryFactory`, `ISearchServiceFactory`, a `FhirDbContext` vendor and a
reference-data-cache vendor; two event handlers, two hosted-service-adjacent handlers and one DurableTask
activity that resolve that concrete factory by type; and a second, independent EF wiring path for tenant-1
package content. The shape of the fix is: give `Ignixa.DataLayer.SqlServer` a real service factory and a
per-tenant reference-data cache registry, extend `IPackageResourceRepository` with the three members the
terminology consumers currently reach past it to get, and repoint each consumer at an abstraction rather
than at a concrete factory.

**Tech Stack:** .NET 10, C# (file-scoped namespaces, primary constructors, collection expressions), raw
ADO.NET via `ISqlExecutionService`, `Microsoft.Data.SqlClient`, Autofac, Medino, DurableTask, xUnit +
Shouldly, live SQL Server for integration tests.

**Design:** `docs/superpowers/specs/2026-07-26-datalayer-sqlserver-phase-f-design.md` (Phase F as a whole).
This plan is the correction to that design's account of Task 8's size.

---

## Global Constraints

Everything in the revised plan's "Revised Global Constraints" stands, and is restated here because this
document will be executed on its own:

- **Classify before porting.** For every behaviour found:
  - *Working and reachable* → port exactly, including quirks. Test against EF first, prove green there,
    then flip a single seam without editing an assertion.
  - *Working but its premise is false* → reproduce, make the falsity explicit in code, pin it with a test.
  - *Not functional* → it cannot be ported. Write a correct implementation against the schema, take only
    the behavioural rules from the EF source, and say so in the class doc.
- **Fixing a defect during a port requires evidence the fix is safe**, not just that the defect is real.
  Establish the blast radius first. Task 8.4 contains one fix whose blast radius is production credentials;
  it is written as an explicit human decision, not an autonomous one.
- **Oracle-first, always.** Where coverage is thin — and for four of the components below it is *zero* —
  write the test against the EF implementation, prove it green there, then repoint one seam. A test written
  only against the new code proves nothing about equivalence.
- **Tests must live in a project that is in `All.sln`.** Verified list of test projects that build and run:
  `Ignixa.Api.Tests`, `Ignixa.Api.E2ETests`, `Ignixa.Application.Tests`,
  `Ignixa.Application.Experimental.Tests`, `Ignixa.DataLayer.SqlServer.Tests`,
  `Ignixa.DataLayer.SqlServer.IntegrationTests`, `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests`,
  `Ignixa.RepoGuards.Tests`, plus the Core/Search/Serialization suites.
  **`test/Ignixa.DataLayer.SqlEntityFramework.Tests/Ignixa.DataLayer.LegacySqlEF.Tests.csproj` is NOT in
  `All.sln`** — it has never built or run, and its `HybridTerminologyServiceTests.cs` cannot compile
  (`HybridTerminologyService`'s constructor takes the concrete `SqlTerminologyService`; the tests pass
  `Substitute.For<ITerminologyService>()`). Do not add tests to it. Do not "fix" it. Task 8.11 deletes it.
- **Do not touch `Ignixa.Search.Sql`.** Any compiler change means the task has gone out of scope.
- **Identifiers in hand-written SQL come from `SqlCatalog.Default.Table("X").Column("Y")`**, not string
  literals. Values and statement structure stay hand-written.
- Environment for every test run: `unset Platform __DOTNET_PREFERRED_BITNESS __DOTNET_ADD_32BIT`;
  `TEST_SQL_CONNECTION_STRING` must contain a `Database=`/`Initial Catalog=` segment;
  `SqlServer__AutomaticSchemaDeploymentEnabled=true`.
- No inline comments except where they explain a non-obvious invariant (CLAUDE.md). No `#region`. One type
  per file. `cancellationToken`, never `ct`.
- **No commits without user approval.**

---

## What the branch actually executed, and what the brief got wrong

Read this before starting. The brief that commissioned this plan was itself wrong in five places, and the
revised plan's own task numbering no longer describes the tree.

**Verified from `git log` on `worktree-ignixa-datalayer-sqlserver` (tip `1f2c731b`):** Task R merged
(`16a68d67`), Task 5b landed the 31-fact terminology oracle (`a239aaa1`, `e5c53e07`), Task 6 ported the
terminology service (`bc972486`) and the CodeSystem/ValueSet importers with their TVP procedures
(`e7b234c2`, `3da89395`, `1f2c731b`). Tasks 1–4 landed earlier.

1. **Revised plan Task 5a was never executed.** There is no `src/DataLayer/Ignixa.DataLayer.SqlServer/
   SqlServerServiceFactory.cs`. `DataLayerRegistration.RegisterRepositoryFactories` still registers
   `SqlEntityFrameworkRepositoryFactory` as the named `"SqlEf"` `IFhirRepositoryFactory` /
   `ISearchServiceFactory`, and `CompositeRepositoryFactory`/`CompositeSearchServiceFactory` still route to
   it. The brief describes Task 8 as "what 5a deliberately left". Nothing was left — 5a's whole scope is
   still open, and this plan owns it.
2. **Revised plan Task 7 was never executed either.** `HybridTerminologyService` is still at
   `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/HybridTerminologyService.cs`,
   and `ValidationServicesRegistration.RegisterTerminologyServices` (lines 110–123) still registers the EF
   `SqlTerminologyService` and hands it to the hybrid. **The `SqlServerTerminologyService` that Task 6
   ported and pinned with 31 oracle facts is dead code in production** — nothing resolves it outside
   `TerminologyOracleFixture`. This is the single largest gap the brief did not mention.
3. **The brief listed four unported components; there are seven.** It missed
   `src/Application/Ignixa.Api/Services/SqlReferenceDataPreloadService.cs`
   (`SqlReferenceDataPreloadHandler`, resolves `SqlEntityFrameworkRepositoryFactory` and calls
   `GetSearchIndexReferenceCacheAsync`), `src/Application/Ignixa.Api/Services/
   TerminologyImportBootstrapService.cs` (resolves the same factory and queries
   `context.PackageResources` by LINQ), and the whole `HybridTerminologyService` /
   `ValidationServicesRegistration` path from (2).
4. **The brief said three config files still emit `"SqlEntityFramework"`; there are six.** In addition to
   `src/Application/Ignixa.Web/appsettings.json` (2 sites), `appsettings.Development.json` (2 sites) and
   `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` (line 122), the literal also appears in
   `.vscode/launch.json` (2 sites), `deploy/azure/azuredeploy.json` (2 sites) and
   `src/Application/Ignixa.Web/Properties/launchSettings.json` (3 sites). The E2E fixture additionally
   carries a logging category `"Logging:LogLevel:Ignixa.DataLayer.SqlEntityFramework.Search"` (line 189)
   and a comment at line 222 naming `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` as the thing
   that seeds the catalog.
5. **The brief's characterisation of `SqlServerSearchIndexReferenceDataCache` is wrong.** It is *not* "a
   DIFFERENT object serving a different purpose". Its own class doc says what it is: an ADO.NET port of
   `SearchIndexReferenceDataCache`'s **write-path surface only**, with five members deliberately not
   ported — `SyncSearchParametersToDatabase`, `GetStatistics`, the `SearchParameterInfo` overload of
   `GetSearchParamIdAsync`, `GetValidResourceTypeMappings`, `GetValidSearchParameterMappings`. It already
   *is* the sole cache on both the write path (`SqlServerMergeRepository`) and the search path
   (`SqlServerSymbolResolver`). It is not a replacement only because of those five omissions and two
   behavioural divergences (see Task 8.2 and Task 8.3). Treating it as a different object would produce two
   competing caches per tenant; treating it as a partial port produces one.

The brief was right about everything else it asserted, and each of those is re-verified in the task that
consumes it.

---

## Classification of every unported component

Per the Global Constraints. Each row is a claim about code that was read in full, with the evidence.

| # | Component | Class | Evidence |
|---|---|---|---|
| 1 | `PackageLoadedSearchParameterSyncHandler` | **Working and reachable** → port exactly | Registered unconditionally (`ApplicationServicesRegistration.cs:436`). Its only EF coupling is `_repositoryFactory.GetSearchIndexReferenceCacheAsync`. Its effect (`dbo.SearchParam` rows + capability-cache invalidation) is observable. Its catch-all swallow at line 154 is deliberate and documented ("allow package load to succeed even if sync fails") — port it, do not silently improve it. |
| 2 | `PackageLoadedTerminologyImportHandler` | **Working and reachable, but with zero coverage by construction** → port exactly, build the oracle by hand | Registered when `Experimental:Features:Terminology:EnableAutoImport` is true, and `src/Application/Ignixa.Web/appsettings.json:222` sets it **true**. So it runs in the shipped configuration. But `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs:177` sets it **false**, so the only integration harness that could exercise it turns it off. Its query is also duplicated near-verbatim by `TerminologyImportBootstrapService` (same flag, tenant 1 only) — one query, two callers. |
| 3a | `SearchIndexReferenceDataCache.SyncSearchParametersToDatabase` + the `SearchParameterInfo` overload + `GetStatistics` | **Working and reachable, never ported** → port exactly (Task 8.2) | `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory:342` calls `SyncSearchParametersToDatabase` synchronously before any repository is handed out, and `PackageLoadedSearchParameterSyncHandler:125` calls it again on every package load. The `OverridesUrl` aliasing branch (lines 993–1009) is the reason an IG parameter that overrides a base parameter indexes against the base parameter's id. Nothing equivalent exists on the SqlServer cache. |
| 3b | `NegativeLookupCache` + `MultiTenantSearchIndexCache.ForgetMissingSystem` | **Not functional in the SqlServer counterpart** → rewrite (Task 8.3) | EF records read-only misses in a separate, TTL-bounded (5 min), capacity-bounded (10 000) cache, and `MultiTenantSearchIndexCache.ForgetMissingSystem` broadcasts an invalidation to **every** tenant cache when a writer creates a `dbo.System` row outside the cache. `SqlServerSearchIndexReferenceDataCache.TryGetSystemIdAsync` instead writes a **permanent** `-1` sentinel into the shared `_systemCache`, with no TTL and no cross-instance broadcast. Once Task 8.8 gives terminology import its own partition-0 cache instance, a system created by a CodeSystem import will never clear the tenant's search-path sentinel — `?code=http://newly-imported\|x` answers "no such terminology" for the process lifetime. |
| 3c | `MultiTenantSearchIndexCache` itself | **Working and reachable, no SqlServer counterpart** → port as a registry (Task 8.3) | Singleton, per-tenant `ConcurrentDictionary`, owns cache lifetime and disposal, exposes `InvalidateTenantCache`/`InvalidateAllCaches`/`CacheStatistics`. `SqlServerRepositoryFactory.CreateReferenceDataCacheAsync` is a static helper that returns a fresh cache and remembers nothing — there is no registry, so there is nowhere for a handler or an activity to obtain *the tenant's* cache. |
| 4 | `PackageRepositoryDbContextFactory` | **Not functional (dead)** → delete the registration, do not port | Registered at `DataLayerRegistration.cs:263` as `IDbContextFactory<FhirDbContext>` and `AsSelf()`. Its only two consumer types are `SqlEntityFramework/EventStore/SqlSourceEventStore.cs:17` and `SqlEntityFramework/Features/PackageManagement/SqlPackageResourceRepository.cs:34`, and **neither is registered any more** — Task 1 replaced the event store with `SqlServerSourceEventStore` (`ConformanceServicesRegistration.cs:56`) and Task 4 replaced the repository with `SqlServerPackageResourceRepository` (`DataLayerRegistration.cs:284`). Nothing in `src/` or `test/` resolves `IDbContextFactory<FhirDbContext>`. Note the dormant failure mode: the registration delegate throws `InvalidOperationException` if tenant 1 has no connection string, and has simply never been invoked. |
| 5 | `SqlEntityFrameworkRepositoryFactory` storage-type gate | **Working and reachable** → port exactly | Lines 161–164 accept `"SqlEntityFramework"` and `"SqlServer"` as synonyms and throw naming both. `CompositeRepositoryFactory`/`CompositeSearchServiceFactory` already do the same. Port the pair unchanged. |
| 6 | System-partition connection-string inheritance | **Working and reachable** → port exactly | Inline in `GetOrCreateFactoryAsync`, lines 166–189: a tenant with `IsSystemPartition` (or `tenantId == SystemConstants.SystemPartitionId`) and an empty connection string inherits from `Storage.InheritConnectionStringFromTenant`, with a specific two-case error message distinguishing "tenant not found" from "tenant has no ConnectionString". Reachable: partition 0 is how transaction ids and terminology are addressed. |
| 7 | `ValidateManagedIdentityAuthentication` | **Not functional** → rewrite, with the fix gated on a human decision | Line 221 reads `Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")` and ignores the `environment` constructor parameter that `DataLayerRegistration.cs:175` passes it (sourced from `RegisterIgnixaServices(configuration, environmentName)`). Constructed with `environment: "Production"` while the OS variable is unset, the production credential guard logs "validation skipped" and returns. |
| 8 | `CreateServiceFactory`'s ordering | **Working and reachable** → port exactly, preserving the sequence | `DeployIfEmptyAsync` → `UpgradeIfNeededAsync` → build definition managers → `SyncSearchParametersToDatabase` → `SqlServerRepositoryFactory.CreateReferenceDataCacheAsync` (which itself does `PreloadResourceTypesAsync` then `PreloadSearchParamsAsync(maxRows: null)`), all synchronous, all once per tenant, all outside the per-request closures. The `dbContextOptions` construction and the `TenantServiceFactory.DbContextOptions` field are the only genuinely dead parts. |
| 9 | `ImportTerminologyResourceActivity` | **Working, but two of its premises are false** → port with both made explicit (Task 8.8) | (a) It writes `TerminologyImportStatus` itself via `UpdateImportStatusAsync`, and so does the importer — in EF at `SqlCodeSystemImporter.cs:126/170/246/286`, and in the port via `dbo.ImportTermCodeSystem.sql:34/72` and `SqlServerCodeSystemImporter.RecordFailureAsync`. Two writers, one column. (b) The content-unchanged skip path leaves the row `Completed` (EF line 97–103, port line 92–102) and returns `CreateSkipped()`, whose `Status` is `TerminologyImportStatus.Skipped`; the activity then overwrites `Completed` with `Skipped`. The **next** import then fails the `== Completed` guard and re-imports in full. Re-loading a package oscillates skip → full re-import → skip. This is pre-existing in EF, not introduced by Task 6. |
| 10 | `SqlReferenceDataPreloadHandler` | **Not functional (never invoked)** → delete, do not port | `BackgroundServicesRegistration.cs:42` registers it as `services.AddSingleton<SqlReferenceDataPreloadHandler>()` — as its own concrete type only. Medino dispatches notifications through `AutofacMediatorServiceProvider.GetServices<T>()`, which does `_context.Resolve<IEnumerable<INotificationHandler<TenantPackagePreloadCompletedEvent>>>()`. Autofac's `Populate` maps `AddSingleton<T>()` to `RegisterType<T>().As<T>()`, so the handler is not in that enumeration. Its work is in any case already done by `CreateServiceFactory` (row 8). Task 8.9 proves the absence with a container test before deleting. |
| 11 | `TerminologyImportBootstrapService` | **Working and reachable** → port exactly | `BackgroundServicesRegistration.cs:47`, `AddHostedService`, gated on the same `EnableAutoImport` flag that `appsettings.json` sets true. Its EF coupling is `GetDbContextAsync` + a LINQ query over `context.PackageResources` that is the same query as row 2's. |

---

## Ordering, and what each task unblocks

```
8.0  baselines + census                     (no code; gates everything)
 |
 +-- 8.1  terminology service wiring        (independent: needs only ISqlExecutionService)
 |
 +-- 8.2  cache: catalog-sync surface  -----+--> 8.3 --+--> 8.4 --+--> 8.5
 |                                          |          |          +--> 8.7
 +-- 8.6  IPackageResourceRepository: ------+----------+          +--> 8.8
          three terminology members                               +--> 8.9
                                                                  +--> 8.10
                                                                        |
                                                            8.11 oracle retirement
                                                                        |
                                                            8.12 pre-deletion gate
                                                                        |
                                                    (revised plan Task 10: delete)
```

- **8.1** removes the `ValidationServicesRegistration` EF dependency and makes the already-ported
  terminology service live. Nothing depends on it; run it first because it is the highest
  value-per-risk item on the board.
- **8.2** unblocks 8.3 (the registry has nothing worth registering without the sync surface), 8.4
  (the factory's catalog-sync step), 8.5 and 8.9.
- **8.3** unblocks 8.4, 8.5, 8.8 — every consumer that needs *the tenant's* cache rather than *a* cache.
- **8.4** unblocks 8.5, 8.7, 8.8, 8.9, 8.10: each of those repoints a consumer away from
  `SqlEntityFrameworkRepositoryFactory`, which cannot be deleted until the factory that replaces it exists
  and is registered.
- **8.6** is independent of 8.4 and can run in parallel with 8.2–8.4. It unblocks 8.7 and 8.8.
- **8.11** requires all ports to be complete, because it decides the fate of every test that names an EF
  type.
- **8.12** is the gate. If it fails, the revised plan's Task 10 does not start.

---

## File structure

New and moved files, in the order the tasks create them:

```
src/DataLayer/Ignixa.DataLayer.SqlServer/
  Features/Terminology/HybridTerminologyService.cs                    (8.1, moved from EF)
  Features/Terminology/ITerminologyImportStatusProvider.cs            (8.1, new)
  Indexing/SqlServerSearchIndexReferenceDataCache.cs                  (8.2, 8.3 — extended)
  Indexing/SqlServerNegativeLookupCache.cs                            (8.3, new)
  Indexing/SqlServerSearchIndexCacheRegistry.cs                       (8.3, new)
  SqlServerServiceFactory.cs                                          (8.4, new)
  Events/PackageLoadedSearchParameterSyncHandler.cs                   (8.5, moved from EF)
  Events/PackageLoadedTerminologyImportHandler.cs                     (8.7, moved from EF)
  Features/PackageManagement/SqlServerPackageResourceRepository.cs     (8.6 — extended)

src/Application/Ignixa.Domain/Abstractions/
  IPackageResourceRepository.cs                                        (8.6 — extended)

src/Application/Ignixa.Api/
  Registrations/ValidationServicesRegistration.cs                      (8.1)
  Registrations/DataLayerRegistration.cs                               (8.3, 8.4)
  Registrations/ApplicationServicesRegistration.cs                     (8.5, 8.7)
  Registrations/BackgroundServicesRegistration.cs                      (8.7, 8.9)
  Services/TerminologyImportBootstrapService.cs                        (8.7)
  Services/SqlReferenceDataPreloadService.cs                           (8.9 — deleted)

src/Application/Ignixa.Application.BackgroundOperations/
  Terminology/Activities/ImportTerminologyResourceActivity.cs          (8.8)
```

**The porting model to follow:** `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs` and
`SqlServerPackageResourceRepository.cs` for ADO.NET conventions;
`test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/TerminologyOracleFixture.cs` for the oracle-then-
flip discipline — it is the reference example of a fixture that constructs both implementations and names
the single line that is the seam.

---

### Task 8.0: Baselines and the EF-consumer census — *verification only, no code*

The revised plan's Task R baselines were measured at commit `16a68d67`; six commits have landed since,
three of which change the database schema. The numbers are not in this document because they cannot be
determined by reading code, and a plan with invented numbers is worse than one that says so.

- [ ] Run and record, as the acceptance baseline for every task below:
      `dotnet build All.sln` (must be 0 warnings / 0 errors);
      `Ignixa.Search.Sql.Tests` (both TFMs);
      `Ignixa.Application.Tests`;
      `Ignixa.Api.Tests`;
      `Ignixa.DataLayer.SqlServer.Tests`;
      `Ignixa.DataLayer.SqlServer.IntegrationTests`;
      `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests`;
      `Ignixa.Api.E2ETests` **with the failing test names listed, not just the count**.
- [ ] Record the census as a table in the task report: for each of the 11 rows in the classification table
      above, the file, the line, the resolving mechanism (Autofac named / Autofac concrete /
      `services.AddX` / `new` by hand), and the task that removes it. This table is what Task 8.12 checks
      against — it is the definition of "nothing outside the EF project references it".
- [ ] Confirm no other EF reference exists that this plan missed:
      `grep -rn "SqlEntityFramework" --include=*.cs --include=*.csproj --include=*.json src test deploy .vscode`
      and reconcile every hit against the census. Any unreconciled hit is a finding and extends this plan.

**Ends in a testable state:** the baseline numbers are recorded and reproducible.

---

### Task 8.1: Wire the terminology service that Task 6 already ported

The ported `SqlServerTerminologyService` is pinned by 31 oracle facts and resolved by nothing.
`ValidationServicesRegistration.RegisterTerminologyServices` still builds the EF `SqlTerminologyService`,
which needs the whole `SqlEntityFrameworkRepositoryFactory`. This is revised-plan Task 7 plus the terminology
half of its Task 8, and it is independent of everything else here.

**Consumes:** `ISqlExecutionService`, `SystemConstants.SystemPartitionId`, `IMemoryCache`,
`InMemoryTerminologyService`.
**Produces:** `ITerminologyService` resolved from `HybridTerminologyService(SqlServerTerminologyService,
InMemoryTerminologyService)`; a new `ITerminologyImportStatusProvider`.

**Files:** move `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/
HybridTerminologyService.cs` → `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/
HybridTerminologyService.cs`; create `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/
ITerminologyImportStatusProvider.cs`; modify
`src/Application/Ignixa.Api/Registrations/ValidationServicesRegistration.cs` (lines 110–123).

**The reason this is a port and not a move.** `HybridTerminologyService`'s constructor takes the *concrete*
`SqlTerminologyService`, because it routes on `GetImportStatusAsync`, which is not a member of
`ITerminologyService`. That single coupling is why the never-built `HybridTerminologyServiceTests.cs`
cannot compile. `SqlServerTerminologyService` also exposes `GetImportStatusAsync` publicly and also does not
declare it on any interface.

- [ ] Introduce `ITerminologyImportStatusProvider` with the single member
      `Task<TerminologyImportStatus?> GetImportStatusAsync(string canonical, CancellationToken cancellationToken)`.
      Implement it on `SqlServerTerminologyService` (the method already exists with that exact signature —
      adding the interface is declaration-only).
- [ ] **Oracle-first.** Before moving anything, add
      `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/HybridTerminologyRoutingTests.cs`
      written against the **EF** `HybridTerminologyService` + EF `SqlTerminologyService`, using
      `TerminologyOracleFixture.CreateEfTerminologyService`'s construction path. Cover the routing decision
      for all seven operations that `HybridTerminologyService` forwards: for each, one case where the
      canonical *is* imported (`TerminologyImportStatus.Completed` → SQL path) and one where it is not
      (→ fallback path). Assert on the *result*, not on which service was called: seed the SQL tables with a
      code the fallback cannot know, and a fallback ValueSet the SQL tables do not have.
- [ ] Prove all of those green against EF. Any that cannot be made green is a finding — record it, it means
      that routing branch does not work today.
- [ ] Move the file. Retarget the constructor's first parameter to
      `(ITerminologyService sqlService, ITerminologyImportStatusProvider statusProvider, ITerminologyService fallbackService, ILogger<HybridTerminologyService> logger)`.
      Do not change any routing logic.
- [ ] Flip the seam in the test to `SqlServerTerminologyService`. **No assertion changes.**
- [ ] Rewrite `ValidationServicesRegistration.RegisterTerminologyServices` to register
      `SqlServerTerminologyService` as `AsSelf()`, `As<ITerminologyImportStatusProvider>()`, built from
      `ISqlExecutionService`, `SystemConstants.SystemPartitionId`, an `IMemoryCache` and a logger — matching
      `TerminologyOracleFixture.CreateTerminologyService` exactly, including the per-scope cache lifetime
      (`InstancePerLifetimeScope`, as today).
- [ ] Verify: full baseline, plus `Ignixa.Api.Tests` proving the container resolves `ITerminologyService`
      without touching `SqlEntityFrameworkRepositoryFactory`.

**Ends in a testable state:** `$lookup`, `$expand`, `$validate-code`, `$translate` and `$subsumes` run
against the ported service through the real container. `ValidationServicesRegistration` no longer names an
EF type.

---

### Task 8.2: Give `SqlServerSearchIndexReferenceDataCache` the catalog-sync surface

The three members that make the SqlServer cache a replacement rather than a partial port. Nothing else in
this plan can proceed without `SyncSearchParametersToDatabase`.

**Consumes:** `ISqlExecutionService`, `ISearchParameterDefinitionManager`, `SearchParameterInfo`.
**Produces, on `SqlServerSearchIndexReferenceDataCache`:**
- `Task<int> SyncSearchParametersToDatabaseAsync(IEnumerable<string> searchParameterUrls, ISearchParameterDefinitionManager searchParamManager, CancellationToken cancellationToken)`
- `Task<short?> GetSearchParamIdAsync(SearchParameterInfo searchParameter, CancellationToken cancellationToken)`
- `CacheStatistics GetStatistics()`

**Files:** modify `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`;
create `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SearchIndexCacheStatistics.cs` (the EF
`CacheStatistics` record lives in `MultiTenantSearchIndexCache.cs`, which violates one-type-per-file and is
being deleted; give the port its own file).
**Source of truth:** `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/
SearchIndexReferenceDataCache.cs` lines 165–186 (`OverridesUrl` overload), 943–1049 (sync), 1073–1082
(statistics). Read all three in full first.

**Behaviours that must survive, each of which is load-bearing and non-obvious:**

1. The sync writes `Uri`, `Status = "Enabled"`, `LastUpdated = DateTimeOffset.UtcNow`,
   `IsPartiallySupported = false` — four columns, and no others.
2. When a parameter has `OverridesUrl` and the overridden URL already has a row, the **new** row is still
   inserted, but the cache is populated with the **overridden** parameter's `SearchParamId`. That aliasing
   is why an IG parameter overriding a base parameter indexes against the base parameter's rows.
3. Cache writes use the **indexer** (`_searchParamCache[url] = id`), never `TryAdd` — deliberately, because
   an earlier lookup may have cached the `-1` miss sentinel and `TryAdd` would leave it there for the
   process lifetime, silently dropping every index row for that parameter. The port's `_searchParamCache`
   already uses indexer assignment elsewhere; keep it.
4. The return value is the count of **newly inserted** parameters, not the count of URLs processed.
   `CreateServiceFactory` logs it as "N of M".
5. `GetStatistics` filters `-1` sentinels out of the search-param and resource-type counts but not out of
   the system and quantity-code counts. Reproduce exactly; it is the shape `SqlReferenceDataPreloadHandler`
   logged.

**A defect to fix, with evidence it is safe.** EF's sync loads the entire `dbo.SearchParam` table into a
`List` and then calls `existingList.FirstOrDefault(sp => sp.Uri == url)` **inside** the loop — O(n²) over
~1 400 base parameters plus every IG parameter, on the startup path of every tenant. The port must use a
dictionary keyed ordinally. This is safe because the lookup is an exact-equality match on the same key in
both shapes; pin it with a test that syncs 2 000 URLs and asserts the resulting row set is identical to
EF's.

- [ ] **Oracle-first.** Add `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/
      SearchParameterCatalogSyncTests.cs`, written against the **EF** `SearchIndexReferenceDataCache`
      constructed over a real `FhirDbContext` from `TestTenantDatabase`. Cover:
      - empty input returns 0 and writes nothing;
      - a fresh URL inserts one row with exactly the four columns above and returns 1;
      - re-syncing the same URL returns 0 and does not duplicate;
      - a parameter whose `OverridesUrl` names an existing row: a new row **is** inserted, and
        `TryGetSearchParamIdFromCache(url)` returns the **overridden** row's id;
      - a parameter whose `OverridesUrl` names a URL with no row: the new row's own id is cached;
      - a URL previously cached as a miss (call `GetSearchParamIdAsync` first, assert null) is repaired by
        the sync — assert `TryGetSearchParamIdFromCache` returns the real id afterwards. **This is the
        sentinel-repair behaviour from (3) and it is the one most likely to be lost in a port.**
      - `GetStatistics()` after a sync plus one deliberate miss, asserting the sentinel is excluded.
- [ ] Prove every one of those green against EF.
- [ ] Implement the three members on `SqlServerSearchIndexReferenceDataCache`. Use `SqlCatalog.Default.
      Table("SearchParam")` for identifiers. Bind `Uri` as `SqlDbType.VarChar` — `dbo.SearchParam.Uri` is
      `VARCHAR`, not `NVARCHAR`, and the existing `GetSearchParamIdAsync` already documents why binding
      `NVarChar` causes an implicit-conversion scan against the clustered PK.
- [ ] Flip the seam. **No assertion changes.** Add the 2 000-URL performance pin as a new test.
- [ ] Verify: full baseline plus the new file green.

**Ends in a testable state:** the SqlServer cache can seed and repair the search-parameter catalog, proven
row-for-row against EF.

---

### Task 8.3: `SqlServerSearchIndexCacheRegistry`, and close the negative-entry gap

Two things that must land together, because the invalidation broadcast has nowhere to live without the
registry and the registry is unsafe to introduce without it.

**Consumes:** `ISqlExecutionService`, `ILoggerFactory`, Task 8.2's cache.
**Produces:**
- `SqlServerSearchIndexCacheRegistry` — singleton; `Task<SqlServerSearchIndexReferenceDataCache> GetOrCreateForTenantAsync(int tenantId, CancellationToken cancellationToken)`, `bool InvalidateTenant(int tenantId)`, `void InvalidateAll()`, `void ForgetMissingSystem(string? systemUri)`, `IReadOnlyDictionary<int, CacheStatistics> CacheStatistics`, `int CachedTenantCount`.
- `SqlServerNegativeLookupCache` — TTL- and capacity-bounded miss record.

**Files:** create `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexCacheRegistry.cs`
and `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerNegativeLookupCache.cs`; modify
`SqlServerSearchIndexReferenceDataCache.cs` and `DataLayerRegistration.cs`.
**Source of truth:** `SqlEntityFramework/Indexing/MultiTenantSearchIndexCache.cs` (registry semantics) and
`SqlEntityFramework/Indexing/NegativeLookupCache.cs` (5-minute `DefaultLifetime`, 10 000 `DefaultCapacity`,
`TimeProvider` injection).

**The defect being closed, stated precisely.** `SqlServerSearchIndexReferenceDataCache.TryGetSystemIdAsync`
and `TryGetQuantityCodeIdAsync` record a miss as `-1` **in the shared positive cache**, permanently. The
write path's `GetOrCreateSystemIdAsync` is sentinel-aware, so a write through *the same instance* heals it.
But from Task 8.8 onward, terminology import holds a partition-0 cache instance while the search path holds
a per-tenant one; a `dbo.System` row created by CodeSystem import will not clear the search instance's
sentinel, and there is no TTL to expire it. EF solved exactly this with a separate TTL-bounded miss cache
plus a registry-wide `ForgetMissingSystem` broadcast, and its `NegativeLookupCache` doc names
`SqlSystemRepository.GetOrCreateAsync` (CodeSystem import) as the second writer that must invalidate.
Without this task, wiring 8.8 introduces a live search bug.

- [ ] **Oracle-first, in two halves.**
      (a) `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/NegativeLookupInvalidationTests.cs`
      written against the **EF** `MultiTenantSearchIndexCache` + `SearchIndexReferenceDataCache`: probe a
      system through cache A (miss), create the `dbo.System` row through cache B's
      `GetOrCreateSystemIdAsync`, call `MultiTenantSearchIndexCache.ForgetMissingSystem`, assert cache A now
      resolves it. Also a TTL test with an injected `TimeProvider` proving the miss expires after five
      minutes with no invalidation at all.
      (b) A registry-semantics test against `MultiTenantSearchIndexCache`: same tenant returns the same
      instance; different tenants return different instances; `InvalidateTenantCache` disposes and a
      subsequent get returns a *new* instance; `InvalidateTenantCache` on an unknown tenant returns false.
- [ ] Prove both green against EF.
- [ ] Port `SqlServerNegativeLookupCache` (same TTL, same capacity, same `TimeProvider` injection, same
      `Forget`/`RecordMiss`/`IsKnownMissing` shape). Rewire `TryGetSystemIdAsync` and
      `TryGetQuantityCodeIdAsync` to record misses **there** rather than as `-1` in `_systemCache` /
      `_quantityCodeCache`, and to consult it before taking `_dbLock`. Add
      `ForgetMissingSystem(string?)` to the cache.
- [ ] **Do not change `GetResourceTypeIdAsync` or `GetSearchParamIdAsync` miss behaviour in this task.**
      They use a `-1` sentinel in the positive cache, which diverges from EF (`GetResourceTypeIdAsync`
      deliberately does *not* cache misses, with a documented reason: a poisoned resource-type cache makes
      the row generators drop the resource). That divergence shipped in Phase B/D, is out of this plan's
      scope, and goes to the follow-ups register.
- [ ] Implement `SqlServerSearchIndexCacheRegistry`. `GetOrCreateForTenantAsync` must perform the same eager
      preloads `SqlServerRepositoryFactory.CreateReferenceDataCacheAsync` performs today
      (`PreloadResourceTypesAsync`, then `PreloadSearchParamsAsync(maxRows: null)`), **exactly once per
      tenant** — use a per-tenant `Lazy<Task<...>>` or `SemaphoreSlim`, not `ConcurrentDictionary.GetOrAdd`
      with an async factory, which can run the factory more than once. `ForgetMissingSystem` broadcasts to
      every cached tenant, matching EF's rationale verbatim: a negative entry is a pure optimisation, so
      dropping a still-valid one costs one round trip and never a wrong answer.
- [ ] Make `SqlServerSystemRepository.GetOrCreateAsync` call `ForgetMissingSystem` after a successful
      create — the second-writer discipline EF's `NegativeLookupCache` doc requires. It currently holds a
      single cache instance; give it the registry instead, or an injected
      `Action<string>` invalidation callback if the registry would create a cycle. State which you chose
      and why in the class doc.
- [ ] Flip both oracle test files' seams to the registry + SqlServer cache. **No assertion changes.**
- [ ] Register the registry in `DataLayerRegistration.AddIgnixaDataLayerServices` as a singleton alongside
      (not yet replacing) `MultiTenantSearchIndexCache`. Retire `SqlServerRepositoryFactory.
      CreateReferenceDataCacheAsync` in favour of the registry — it has exactly one caller.
- [ ] Verify: full baseline plus both new files green.

**Ends in a testable state:** a system created through terminology import is visible to a search that
previously recorded it missing, proven by a test that fails without the broadcast.

---

### Task 8.4: `SqlServerServiceFactory`, and flip the composition root

The keystone. This is revised-plan Task 5a in full, plus the storage-type work its Task 8 named.

**Consumes:** `ITenantConfigurationStore`, `ILoggerFactory`, `RecyclableMemoryStreamManager`,
`ISchemaDeployer`, `ISqlExecutionService`, Task 8.3's registry, Task 8.2's sync, `string environment`.
**Produces:** `SqlServerServiceFactory : IFhirRepositoryFactory, ISearchServiceFactory` with
`Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken cancellationToken)`,
`Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken cancellationToken)`,
`void ClearCache()`, `int CachedServicesCount`. **No `GetDbContextAsync`. No
`GetSearchIndexReferenceCacheAsync`** — the registry serves that need directly, which is what breaks every
remaining consumer's dependency on a concrete factory.

**Files:** create `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerServiceFactory.cs`; modify
`src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`.
**Source of truth:** `SqlEntityFrameworkRepositoryFactory.cs` in full — specifically
`GetOrCreateFactoryAsync` (135–198), `ValidateManagedIdentityAuthentication` (216–250),
`GetOrCreateDefinitionManagers` (256–268) and `CreateServiceFactory` (270–398).

**What must survive, in this order** (from `CreateServiceFactory`, verified line by line):

1. `_schemaDeployer.DeployIfEmptyAsync(tenantId)` then `UpgradeIfNeededAsync(tenantId)`, both awaited
   before anything else, both wrapped so a failure logs and rethrows.
2. `FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion)` →
   `fhirSpec.GetSchemaProvider()` → definition managers, **cached by `FhirVersion`** so tenants sharing a
   version share the managers.
3. `SyncSearchParametersToDatabaseAsync` over the distinct non-null `AllSearchParameters` URLs, logging
   "N of M". This both seeds `dbo.SearchParam` and warms the cache — one call, two effects, per
   `docs/superpowers/specs/2026-07-21-search-param-seed-on-tenant-init-design.md`. Without it a freshly
   deployed database's caches warm to empty and never recover.
4. The reference-data cache obtained from the registry (which preloads resource types then search
   parameters), **once per tenant, outside any per-request path**.
5. Only then, the per-request repository and search-service construction, via
   `SqlServerRepositoryFactory.CreateRepository` / `CreateSearchService` unchanged.

Note that (3) and (4) are ordered: the sync must run before the preload is trusted, and the port must not
reorder them into "preload, then sync" even though that reads more naturally.

**The `ValidateManagedIdentityAuthentication` decision — for the human, not the agent.** The method is
broken as described in classification row 7. Fixing it is a one-line change with a production blast radius:

- *Who is affected.* Only a deployment whose `IHostEnvironment.EnvironmentName` is `Production` **and**
  whose tenant connection string contains `Password=` or `pwd=`. `deploy/azure/azuredeploy.json` provisions
  Managed Identity, so the reference deployment is unaffected. E2E runs under `UseEnvironment("Test")`;
  `TerminologyOracleFixture` passes `"Development"` explicitly. No test is affected either way.
- *What breaks if fixed.* Exactly the configuration the guard was written to reject — a production server
  using SQL authentication now fails at first tenant access instead of starting.
- *What breaks if not fixed.* Nothing, today. The guard simply continues not to run.

Options, in the order I'd rank them:
  **(a)** Port the method reading the injected `environment`, i.e. make the guard work. Recommended: the
  guard is a security control and a security control that silently no-ops is worse than no control.
  **(b)** Port the method reading the injected `environment`, but behind a `SqlServerOptions` flag
  defaulting to off, with a `LogError` (not throw) when it would have thrown. Reversible; gives one release
  of warning before enforcement.
  **(c)** Port the bug verbatim with a pinning test and a class-doc note. Defensible under "port as-is",
  and the wrong call here.

- [ ] **Ask the user which of (a)/(b)/(c) to implement before writing the method.** Do not choose.

**Steps:**

- [ ] **Oracle-first.** Add `test/Ignixa.DataLayer.SqlServer.IntegrationTests/
      ServiceFactoryTenantResolutionTests.cs` written against **`SqlEntityFrameworkRepositoryFactory`**,
      using `TerminologyOracleFixture`'s `SystemPartitionTenantStore` shape for the tenant store. Cover:
      - unknown tenant → `InvalidOperationException` naming "does not exist";
      - inactive tenant → `InvalidOperationException` naming "is not active";
      - storage type `"SqlEntityFramework"` → resolves;
      - storage type `"SqlServer"` → resolves;
      - storage type `"FileSystem"` → `InvalidOperationException` whose message names both accepted values;
      - non-system tenant with empty connection string → `InvalidOperationException` naming
        `Storage.ConnectionString`;
      - system partition with empty connection string and a valid
        `InheritConnectionStringFromTenant` → resolves against the inherited string;
      - system partition inheriting from a tenant that does not exist → message says "not found";
      - system partition inheriting from a tenant with no connection string → message says
        "has no ConnectionString";
      - two `GetRepositoryAsync` calls for the same tenant → `CachedServicesCount == 1`, and the schema
        deployer was invoked once (use a counting `ISchemaDeployer` double);
      - after a first `GetRepositoryAsync`, `dbo.SearchParam` is non-empty — the catalog-sync-on-init
        contract;
      - `ValidateManagedIdentityAuthentication`: constructed with `environment: "Production"`, OS variable
        unset, password-bearing connection string → **assert the current (broken) behaviour: it does not
        throw.** This test is the record of the defect; option (a) or (b) inverts it in the same commit that
        implements the fix, and option (c) keeps it.
- [ ] Prove every one green against EF. Anything that cannot be made green is a finding.
- [ ] Write `SqlServerServiceFactory`. No `FhirDbContext`, no `DbContextOptions`, no `TenantServiceFactory`
      record holding options. The per-tenant cached state is: the reference-data cache (from the registry),
      the two definition managers, and the resolved connection string.
- [ ] Flip the seam in the oracle test to `SqlServerServiceFactory`. **No assertion changes**, except the
      `ValidateManagedIdentityAuthentication` case if the user chose (a) or (b).
- [ ] In `DataLayerRegistration.RegisterRepositoryFactories`, replace the
      `SqlEntityFrameworkRepositoryFactory` registration with `SqlServerServiceFactory`, keeping the
      **named registration key `"SqlEf"`** so `RegisterCompositeFactories` is untouched in this task.
      Renaming the key is Task 8.10's job and must not be bundled here — one seam per task.
- [ ] **Delete the `PackageRepositoryDbContextFactory` registration** (`DataLayerRegistration.cs:263–279`)
      and the `IDbContextFactory<FhirDbContext>` / `Microsoft.EntityFrameworkCore` usings it required.
      Classification row 4: dead. Before deleting, prove it: add a container test in `Ignixa.Api.Tests`
      asserting `IDbContextFactory<FhirDbContext>` has no registered consumer, then delete both the
      registration and the test's subject.
- [ ] Delete `MultiTenantSearchIndexCache`'s registration from `AddIgnixaDataLayerServices` (line 40) — with
      the EF factory gone, nothing resolves it.
- [ ] Verify: full baseline, **plus a real application start** against a real tenant database. The E2E suite
      does not prove this; Phase B's ~10 missing tables were found by running the app.

**Ends in a testable state:** the application serves reads and writes with no EF type in the resolution
graph for repositories or search services.

---

### Task 8.5: Port `PackageLoadedSearchParameterSyncHandler`

**Consumes:** `IFhirVersionContext`, Task 8.3's registry, `ITenantConfigurationStore`,
`ICapabilityCacheInvalidator`, Task 8.2's `SyncSearchParametersToDatabaseAsync`.
**Produces:** `Ignixa.DataLayer.SqlServer.Events.PackageLoadedSearchParameterSyncHandler :
INotificationHandler<PackageLoadedEvent>`.

**Files:** move `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Events/
PackageLoadedSearchParameterSyncHandler.cs` → `src/DataLayer/Ignixa.DataLayer.SqlServer/Events/`; modify
`src/Application/Ignixa.Api/Registrations/ApplicationServicesRegistration.cs:436`.

Classification row 1: working and reachable. The only change is the second constructor parameter —
`SqlEntityFrameworkRepositoryFactory` becomes `SqlServerSearchIndexCacheRegistry`, and
`_repositoryFactory.GetSearchIndexReferenceCacheAsync(tenantId, ct)` becomes
`_registry.GetOrCreateForTenantAsync(tenantId, cancellationToken)`. Everything else — the tenant-not-found
early return, the FHIR-version conversion, the `Distinct()`, the stopwatch and params/sec log, the
capability-cache invalidation, and the catch-all that does not rethrow — is ported byte-for-byte.

- [ ] **Oracle-first.** Add `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Events/
      PackageLoadedSearchParameterSyncHandlerTests.cs` against the **EF** handler. Cover:
      - a tenant that does not exist → returns without throwing, writes no rows, does **not** invalidate the
        capability cache (use a recording `ICapabilityCacheInvalidator`);
      - a normal tenant → `dbo.SearchParam` contains a row per distinct manager URL, and the capability
        cache was invalidated exactly once for that tenant;
      - a second identical event → no new rows, capability cache invalidated again (the invalidation is not
        conditional on the synced count);
      - a registry/cache that throws → the handler swallows it, logs, and **does not** invalidate the
        capability cache, because the invalidation is inside the `try`. Pin this: it is the behaviour a
        reader would most likely "fix".
- [ ] Prove green against EF.
- [ ] Move the file, change the one parameter, flip the seam. **No assertion changes.**
- [ ] Update the Autofac registration to the new type. Keep `InstancePerDependency`.
- [ ] Verify: full baseline.

**Ends in a testable state:** loading a package registers its search parameters through the SqlServer cache,
proven equivalent to EF row-for-row.

---

### Task 8.6: `IPackageResourceRepository` gains the three terminology-import members

The three consumers in Tasks 8.7 and 8.8 currently reach *past* `IPackageResourceRepository` and query
`context.PackageResources` by LINQ, because the interface has no member for what they need. Verified: the
interface has 20 members and none of them fetch by `PackageResourceId`, list pending terminology resources,
or write import status.

**Consumes:** `ISqlExecutionService`.
**Produces, on `IPackageResourceRepository` and `SqlServerPackageResourceRepository`:**
- `Task<PackageResource?> GetByPackageResourceIdAsync(long packageResourceId, CancellationToken cancellationToken)`
- `Task<IReadOnlyList<PendingTerminologyResource>> ListPendingTerminologyResourcesAsync(string? packageId, string? packageVersion, CancellationToken cancellationToken)` — null `packageId`/`packageVersion` means "all packages", which is what the bootstrap service needs; both non-null is what the event handler needs.
- `Task UpdateTerminologyImportStatusAsync(long packageResourceId, TerminologyImportStatus status, DateTimeOffset? importStartDate, DateTimeOffset? importCompletedDate, string? errorMessage, int? importedConceptCount, CancellationToken cancellationToken)`

**Files:** modify `src/Application/Ignixa.Domain/Abstractions/IPackageResourceRepository.cs` and
`src/DataLayer/Ignixa.DataLayer.SqlServer/Features/PackageManagement/SqlServerPackageResourceRepository.cs`;
create `src/Application/Ignixa.Domain/Models/PendingTerminologyResource.cs`.

**Implementations that must be updated:** there are exactly two —
`SqlEntityFramework/Features/PackageManagement/SqlPackageResourceRepository.cs` (unregistered since Task 4;
give it `NotSupportedException` bodies rather than real implementations, since it is deleted in Task 10) and
`SqlServerPackageResourceRepository`. Verified: no third implementation exists in `src/` or `test/`.

**The selection predicate, verified identical in both existing consumers**
(`PackageLoadedTerminologyImportHandler.cs:48–57`, `TerminologyImportBootstrapService.cs:53–64`):
`IsActive` AND `ResourceType IN ('CodeSystem','ValueSet','ConceptMap')` AND
`TerminologyImportStatus IS NULL OR = 'Pending' OR = 'Failed'`, optionally scoped by
`PackageId` + `PackageVersion`. The bootstrap service additionally groups by `(PackageId, PackageVersion)`;
return the flat rows and let the caller group, so one query serves both.

**A claim to verify rather than assume.** `SqlServerPackageResourceRepository.ReadResource` maps 11 of the
17 `PackageResource` columns, leaving `TerminologyImportStatus`, `ContentHash` and the four `Import*` fields
null — a documented inherited limitation (follow-ups register). Task 8.8's consumer needs
`PackageResourceId`, `ResourceType`, `Canonical` and `ResourceJson`, all of which **are** in the mapped
eleven, and `SqlServerCodeSystemImporter` reads `ContentHash`/`TerminologyImportStatus` from the database
itself in `ReadPackageRowAsync` rather than from the model. So the 11-column mapper is sufficient here.

- [ ] Confirm the above by reading `SqlServerCodeSystemImporter.ImportAsync` and `ReadPackageRowAsync`
      before writing `GetByPackageResourceIdAsync`. If any consumer needs one of the six unmapped columns,
      extend `ReadResource` in this task and say so.
- [ ] **Oracle-first.** The oracle here is not another repository — it is the inline LINQ in the two
      consumers. Add `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/
      PendingTerminologyResourceQueryTests.cs` that seeds `dbo.PackageResource` rows spanning every
      dimension of the predicate (each of the three resource types; a fourth type that must be excluded;
      `IsActive` false; each of the four status values `NULL`/`Pending`/`Failed`/`Completed`; two packages)
      and asserts the row set returned by **the EF LINQ query executed against a real `FhirDbContext`** —
      copy the expression verbatim from `TerminologyImportBootstrapService.cs:53–64`. That expression is the
      specification.
- [ ] Prove green against EF.
- [ ] Implement the three members over `ISqlExecutionService`, identifiers from
      `SqlCatalog.Default.Table("PackageResource")`.
- [ ] Flip the seam to `ListPendingTerminologyResourcesAsync`. **No assertion changes.**
- [ ] Add tests for the other two members: `GetByPackageResourceIdAsync` returns null for an absent id and a
      correctly mapped model for a present one; `UpdateTerminologyImportStatusAsync` writes only the fields
      whose arguments are non-null, leaving the others untouched (this is the EF activity's partial-update
      semantics — see `ImportTerminologyResourceActivity.UpdateImportStatusAsync`, which builds its `SET`
      clause conditionally). Truncate `errorMessage` at 1 000 characters, matching
      `ImportTerminologyResourceActivity.cs:238` and the `NVARCHAR(1000)` column.
- [ ] Verify: full baseline.

**Ends in a testable state:** the terminology-import query and status write exist behind the Domain
interface, proven against the LINQ they replace.

---

### Task 8.7: Port `PackageLoadedTerminologyImportHandler` and `TerminologyImportBootstrapService`

Two consumers, one query. They are ported together because they share the predicate from Task 8.6 and
because splitting them would leave one of the two EF-coupled callers behind for no review benefit.

**Consumes:** Task 8.6's `ListPendingTerminologyResourcesAsync`, `IMediator`.
**Produces:** `Ignixa.DataLayer.SqlServer.Events.PackageLoadedTerminologyImportHandler`; a
`TerminologyImportBootstrapService` that resolves `IPackageResourceRepository` instead of
`SqlEntityFrameworkRepositoryFactory`.

**Files:** move `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Events/
PackageLoadedTerminologyImportHandler.cs` → `src/DataLayer/Ignixa.DataLayer.SqlServer/Events/`; modify
`src/Application/Ignixa.Api/Services/TerminologyImportBootstrapService.cs` and
`src/Application/Ignixa.Api/Registrations/ApplicationServicesRegistration.cs:443`.

**Coverage note that governs this task.** Both are gated on
`Experimental:Features:Terminology:EnableAutoImport`, which `appsettings.json:222` sets **true** and
`IgnixaApiFixture.cs:177` sets **false**. So both run in production and neither is touched by E2E. There is
no oracle to capture from an existing test because no existing test exercises them. Task 8.6's tests are the
oracle for the *query*; this task's tests are new coverage for the *dispatch*, and must be written as such
rather than pretending to be an equivalence proof.

- [ ] Port the handler: replace the `GetDbContextAsync` + LINQ block with
      `ListPendingTerminologyResourcesAsync(notification.PackageId, notification.PackageVersion, cancellationToken)`.
      Preserve: the zero-results early return with its exact log message, the
      `TerminologyImportTriggeredEvent` shape, and the catch-all that does not rethrow.
- [ ] Port the bootstrap service: replace its LINQ + `GroupBy` with
      `ListPendingTerminologyResourcesAsync(null, null, stoppingToken)` grouped in memory by
      `(PackageId, PackageVersion)`. Preserve the 5-second `Task.Delay`, the tenant-1 scoping, the
      per-package try/catch, and the `OperationCanceledException` branch.
      **Record as a finding:** the 5-second delay is a race against `TenantPackagePreloadService`, not a
      synchronisation. It is out of scope here; it goes to the follow-ups register.
- [ ] Add `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Events/
      TerminologyImportTriggerTests.cs` covering both callers against a recording `IMediator`:
      no pending resources → no event published; pending resources in one package → exactly one
      `TerminologyImportTriggeredEvent` carrying every pending id; pending resources across two packages
      via the bootstrap path → exactly two events, correctly partitioned; a repository that throws → no
      event, no rethrow.
- [ ] Update the Autofac registration and the `BackgroundServicesRegistration` hosted-service registration.
      Both stay gated on the same config key; do not change the gate.
- [ ] Verify: full baseline, plus a real application start with
      `Experimental:Features:Terminology:EnableAutoImport=true` and a package containing terminology
      resources — this is the only way either path is exercised end to end.

**Ends in a testable state:** package load and startup both trigger terminology import with no EF type in
the path.

---

### Task 8.8: Port `ImportTerminologyResourceActivity`

The last EF consumer outside the API registrations, and the one carrying two false premises.

**Consumes:** Task 8.6's three members, Task 8.3's registry, `SqlServerSystemRepository`,
`SqlServerCodeSystemImporter`, `IFhirRequestContextAccessor`.
**Produces:** an activity whose only data-layer dependencies are `IPackageResourceRepository` and
`ITerminologyImporter`.

**Files:** modify `src/Application/Ignixa.Application.BackgroundOperations/Terminology/Activities/
ImportTerminologyResourceActivity.cs` and
`src/Application/Ignixa.Application.BackgroundOperations/Ignixa.Application.BackgroundOperations.csproj`
(drop the EF `ProjectReference`).

**What the activity currently does, verified:** resolves `SqlEntityFrameworkRepositoryFactory` from a scope,
gets a `FhirDbContext`, hand-builds `SqlSystemRepository` + `SqlCodeSystemImporter`, queries
`context.PackageResources` by LINQ, maps 17 columns through a private `MapEntityToModel`, sets the ambient
`IFhirRequestContext`, routes on `ResourceType`, and writes status three times
(`InProgress` before, `Failed` on exception, `result.Status` after).

**The two false premises, and what to do about each.**

*Premise A — the activity is the writer of `TerminologyImportStatus`.* It is not the only one. The ported
stored procedures `dbo.ImportTermCodeSystem.sql` (lines 34, 72–74), `dbo.ImportTermValueSet.sql` (23,
48–50) and `dbo.ImportTermConceptMap.sql` (17, 42–44) set `InProgress` then `Completed` with
`ImportCompletedDate` and `ImportedConceptCount`, and `SqlServerCodeSystemImporter.RecordFailureAsync` sets
`Failed`. The EF importer did the same. Two writers, one column, and the activity's write lands *after* the
importer's.

*Premise B — writing `result.Status` is harmless.* It is not. `TerminologyImportResult.CreateSkipped()`
carries `Status = TerminologyImportStatus.Skipped`. The content-unchanged skip path leaves the row
`Completed` and returns `CreateSkipped()`, so the activity overwrites `Completed` with `Skipped`. The next
import then fails the importer's `existing.Status == "Completed"` guard and re-imports in full, writing
`Completed` again — so re-loading an unchanged package alternates between a cheap skip and a full re-import
forever. This is inherited from EF, not introduced by Task 6.

- [ ] **Oracle-first.** Add `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/
      TerminologyImportStatusLifecycleTests.cs` against the **EF** activity path (EF importer + the
      activity's `UpdateImportStatusAsync` logic, reproduced in the test if the activity cannot be
      constructed standalone — say so in the test doc if it cannot). Assert, in order, the *observed*
      column values after: a first import; an immediate identical re-import; and a third import.
      **Expect the oscillation and pin it.** A test that asserts the correct behaviour here would be red
      against EF, which is the finding, not a bug in the test.
- [ ] Prove the oracle green against EF, oscillation included.
- [ ] Port the activity: resolve `IPackageResourceRepository` and `ITerminologyImporter` from the scope
      (register `SqlServerCodeSystemImporter` as `ITerminologyImporter` in `DataLayerRegistration`, built
      from `ISqlExecutionService`, `SystemConstants.SystemPartitionId`, a `SqlServerSystemRepository` over
      the registry's partition-0 cache, and a logger). Delete `MapEntityToModel`,
      `ParseTerminologyImportStatus` and the private `FhirDbContext`-typed `UpdateImportStatusAsync`.
      Preserve the ambient-request-context set/restore around the whole body and the two catch-alls that
      return a failure output rather than throwing.
- [ ] Flip the seam. **No assertion changes** — the oscillation must still be observable, proving the port
      is faithful.
- [ ] **Then, as a separate commit**, fix premise B: stop the activity writing `Skipped` over `Completed`.
      The minimal correct change is to make the activity write status only when the importer did not — i.e.
      drop the activity's post-import `UpdateTerminologyImportStatusAsync` entirely, since all three
      terminal states are already written by the procedures or by `RecordFailureAsync`. Invert the oracle
      test in the same commit and say plainly in the commit message that this changes behaviour.
      *Blast radius:* the only consumer of `TerminologyImportStatus` is the importer's own skip guard and
      `SqlServerTerminologyService.GetImportStatusAsync`, which `HybridTerminologyService` uses to route.
      `Skipped` is not `Completed`, so today a skipped-then-unchanged ValueSet routes to the **fallback**
      service — meaning this defect is already visible at the `$expand` endpoint. Verify that claim with a
      test before making the change.
- [ ] Drop the EF `ProjectReference` from `Ignixa.Application.BackgroundOperations.csproj`. It must build
      with the EF project absent from its reference graph; that is the check.
- [ ] Verify: full baseline, plus a real terminology import against a real database.

**Ends in a testable state:** terminology import runs entirely on ported types, and the status lifecycle is
pinned rather than assumed.

---

### Task 8.9: Delete `SqlReferenceDataPreloadHandler`

Classification row 10: not functional. It is registered as `services.AddSingleton<SqlReferenceDataPreloadHandler>()`
— as its own type only — while Medino dispatches through
`AutofacMediatorServiceProvider.GetServices<T>()`, which resolves
`IEnumerable<INotificationHandler<TenantPackagePreloadCompletedEvent>>`. Autofac's `Populate` maps
`AddSingleton<T>()` to `RegisterType<T>().As<T>()`, so the handler is not in that enumeration and has never
run. Its work — `PreloadResourceTypesAsync` and `PreloadSearchParamsAsync` per tenant — is in any case
performed by the service factory during tenant initialisation (Task 8.4 step 4).

**Files:** delete `src/Application/Ignixa.Api/Services/SqlReferenceDataPreloadService.cs`; modify
`src/Application/Ignixa.Api/Registrations/BackgroundServicesRegistration.cs:42`.

- [ ] **Prove the absence before deleting.** Add a test to `Ignixa.Api.Tests` that builds the real Autofac
      container from the real registrations and asserts
      `container.Resolve<IEnumerable<INotificationHandler<TenantPackagePreloadCompletedEvent>>>()` does not
      contain a `SqlReferenceDataPreloadHandler`. If it *does* contain one, this classification is wrong:
      stop, and port the handler onto the registry from Task 8.3 instead of deleting it.
- [ ] If the absence is confirmed: delete the file and its registration, and record the finding in the task
      report with the container test as evidence. Keep a variant of the container test asserting the
      handler type no longer exists in the assembly, so a future reintroduction has to be deliberate.
- [ ] Verify: full baseline. Confirm by log inspection on a real application start that per-tenant reference
      data is still preloaded — the log line
      `"Preloaded {Count} search parameters into cache"` must still appear once per tenant, emitted by
      `SqlServerSearchIndexReferenceDataCache` via the registry.

**Ends in a testable state:** the dead handler is gone, and the preload it claimed to do is proven to happen
elsewhere.

---

### Task 8.10: Retire the `"SqlEntityFramework"` storage-type literal

**Files (six, not three):**
`src/Application/Ignixa.Web/appsettings.json` (2 sites, lines 157 and 177);
`src/Application/Ignixa.Web/appsettings.Development.json` (2 sites, 168 and 180);
`src/Application/Ignixa.Web/Properties/launchSettings.json` (3 sites, 36, 42, 48);
`.vscode/launch.json` (2 sites, 45 and 51);
`deploy/azure/azuredeploy.json` (2 sites, 396 and 420);
`test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` (line 122).
Plus `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs` for the named-registration key.

Both literals are already accepted as synonyms by `CompositeRepositoryFactory`,
`CompositeSearchServiceFactory` and (after Task 8.4) `SqlServerServiceFactory`. This task changes what the
codebase *emits*, not what it *accepts* — the synonym stays, deprecated, because a deployed
`appsettings.json` in the field will still say `SqlEntityFramework`.

- [ ] Change all 12 configuration sites to `"SqlServer"`.
- [ ] Rename the Autofac named-registration key from `"SqlEf"` to `"SqlServer"` in
      `RegisterRepositoryFactories` and both `ResolveNamed` calls in `RegisterCompositeFactories`. This is a
      container-internal key, unrelated to the config literal; renaming it in the same task keeps the two
      spellings from drifting.
- [ ] Update `IgnixaApiFixture.cs:189`'s logging category from
      `"Ignixa.DataLayer.SqlEntityFramework.Search"` to `"Ignixa.DataLayer.SqlServer.Search"`, and the
      comment at line 222 to name `SqlServerServiceFactory.CreateServiceFactory`.
- [ ] Add a `Ignixa.RepoGuards.Tests` guard asserting that no file under `src/`, `deploy/` or `.vscode/`
      contains the literal `"SqlEntityFramework"` as a storage-type value. `Ignixa.RepoGuards.Tests` is in
      `All.sln` and already contains this kind of guard (`GitIgnoreSourcePathsTests`,
      `PackageStabilityGuardTests`), so the pattern exists.
- [ ] Add a test proving the deprecated synonym still resolves: a tenant configured
      `Type = "SqlEntityFramework"` must still be served by `SqlServerServiceFactory`. This is the
      backward-compatibility contract and it must outlive this phase.
- [ ] Verify: full baseline, **including E2E**, since `IgnixaApiFixture` is in the blast radius.

**Ends in a testable state:** nothing the repository emits names the retired provider, and a field
configuration that still does keeps working.

---

### Task 8.11: Retire the disappearing oracles, deliberately

The revised plan's Task 10 says "delete the two EF test projects". That undercounts. The EF assembly is
referenced by **six** `.csproj` files, and two of the referencing test projects contain coverage that is
about the *SqlServer* implementation and must survive the deletion in some form. This task decides the fate
of each, one at a time, before anything is deleted.

**The six references, verified:**

| Project | Why it references EF | Disposition |
|---|---|---|
| `src/Application/Ignixa.Api` | All of the above tasks | Reference droppable once 8.1–8.10 land |
| `src/Application/Ignixa.Application.BackgroundOperations` | `ImportTerminologyResourceActivity` | Dropped in Task 8.8 |
| `test/Ignixa.Api.E2ETests` | Logging category + comment only, after 8.10 | Reference droppable |
| `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` | It *is* the EF suite | Deleted with the project |
| `test/Ignixa.DataLayer.SqlEntityFramework.Tests` | Not in `All.sln`, never built, does not compile | Deleted; no coverage lost because none ever ran |
| `test/Ignixa.DataLayer.SqlServer.IntegrationTests` | `Fixtures/TerminologyOracleFixture.cs` and the whole `Differential/` folder | **Needs work — see below** |

**`TerminologyOracleFixture`.** Three members bind it to EF: the `SqlEntityFrameworkRepositoryFactory`
field, `CreateEfTerminologyService()` and `CreateImporterAsync()`. Every assertion in the seven
`TerminologyOracle*Tests.cs` files was proven against EF and then repointed; the EF constructors remain only
so a disagreement could be attributed. Once EF is gone that attribution is no longer possible, which is the
point of the phase.

**`Differential/` (13 files).** `DifferentialTestHarness` runs the EF and SqlServer implementations against
the same database and compares row state. Every one of those tests is, by construction, deleted with EF.
That is roughly the "50 differential facts" the revised plan's Task 10 names — but they are not merely
deleted, they are the *only* proof that the two write paths agree, and
`docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md` assumes the legacy engine is available
for row-level comparison when closing the remaining search gaps.

- [ ] For each of the 13 `Differential/` files, decide and record one of: **(i)** the facts it asserts are
      already covered by a SqlServer-only test — name the test; **(ii)** the facts are worth keeping and are
      converted to SqlServer-only assertions against a recorded expected row state (`RowStateSnapshot`
      already exists and can hold a golden snapshot rather than a live EF comparison); **(iii)** the facts
      are genuinely EF-comparison-only and are lost — record exactly what is lost.
      **Produce this table before deleting anything.** It is the concrete answer to "how are the
      disappearing oracles handled", and it is the artefact a reviewer will ask for.
- [ ] Convert every file classified (ii). A golden `RowStateSnapshot` captured from the current EF run and
      committed as test data is a legitimate substitute for a live oracle, and it is the only substitute
      available after deletion. Capture the snapshots **while EF still runs.**
- [ ] Delete every file classified (i) or (iii), with the rationale in the commit message.
- [ ] Strip `CreateEfTerminologyService`, `CreateImporterAsync` and the `SqlEntityFrameworkRepositoryFactory`
      field from `TerminologyOracleFixture`, replacing the factory's role (tenant store + schema deploy +
      execution service) with direct construction — the fixture already builds all three itself for
      `CreateSqlServerImporter`, so this is a deletion, not a rewrite.
- [ ] Drop the EF `ProjectReference` from `Ignixa.DataLayer.SqlServer.IntegrationTests.csproj` and
      `Ignixa.Api.E2ETests.csproj`.
- [ ] Amend `docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md` and
      `docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md` to say that the legacy engine is
      no longer available for row-level comparison, and to point at whatever golden snapshots replaced it.
      (The revised plan's Task 10 already lists these two amendments; doing them here means Task 10 is a
      pure deletion.)
- [ ] Verify: full baseline. `Ignixa.DataLayer.SqlServer.IntegrationTests`'s count will drop by the number
      of deleted differential tests — **record the expected new number before running**, so a drop by a
      different amount is caught.

**Ends in a testable state:** no test project references EF, and every fact that mattered either still runs
or is recorded as deliberately lost.

---

### Task 8.12: Pre-deletion gate — *verification only, no code*

This replaces and extends the revised plan's Task 9. Deletion is the one irreversible step, and it removes
the rollback lever for a live, unflagged read **and** write cutover. If any check fails, **stop**. The
revised plan's Task 10 does not start.

**Build and reference checks:**

- [ ] `dotnet build All.sln` → 0 warnings, 0 errors.
- [ ] `grep -rn "SqlEntityFramework" --include=*.cs --include=*.csproj --include=*.json --include=*.sln src test deploy .vscode`
      returns hits **only** inside `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/`,
      `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/`,
      `test/Ignixa.DataLayer.SqlEntityFramework.Tests/`, `All.sln`, and the deprecated-synonym acceptance
      code in `CompositeRepositoryFactory` / `CompositeSearchServiceFactory` / `SqlServerServiceFactory`
      plus its test. Any other hit means an earlier task is incomplete — finish it rather than deleting
      around it.
- [ ] `grep -rn "FhirDbContext" --include=*.cs src test` returns hits only inside the EF project.
- [ ] Reconcile the result against Task 8.0's census table. Every census row must be marked closed, with the
      task that closed it.

**Test checks:**

- [ ] Every unit and integration suite at the Task 8.0 baseline, except
      `Ignixa.DataLayer.SqlServer.IntegrationTests`, which must match the number recorded in Task 8.11.
- [ ] **E2E at exactly the Task 8.0 baseline, matching on failing test names**, not just counts. A *lower*
      failure count is as suspect as a higher one — nothing in this plan should fix a search gap.
- [ ] `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` still green. It is about to be deleted, but a
      failure here means the EF implementation changed underneath the ports, which invalidates every oracle
      they were proven against.

**Runtime checks — the ones that matter most, because five of the eleven components have no E2E coverage:**

- [ ] **A real application start** against a real tenant database, with
      `Experimental:Features:Terminology:EnableAutoImport=true`. Confirm in the logs, in order: schema
      deploy/upgrade; `"Search parameter catalog synced for tenant N: X of Y URLs"`;
      `"Preloaded N resource types into cache"`; `"Preloaded N search parameters into cache"`.
- [ ] Load a package containing `SearchParameter` resources. Confirm `dbo.SearchParam` gains rows and
      `/metadata` reflects them (this exercises Task 8.5 end to end).
- [ ] Load a package containing `CodeSystem`, `ValueSet` and `ConceptMap` resources. Confirm
      `TerminologyImportTriggeredEvent` is published, the import runs, and
      `dbo.PackageResource.TerminologyImportStatus` reaches `Completed` (Tasks 8.6–8.8).
- [ ] Re-load the **same** package unchanged. Confirm the status stays `Completed` and no re-import occurs —
      this is the specific regression Task 8.8's second commit fixes, and it is invisible to every test that
      does not run the import twice.
- [ ] `$lookup`, `$expand` and `$subsumes` against an imported CodeSystem and ValueSet (Task 8.1 + 8.8).
- [ ] Search on a token parameter whose system was created **by the terminology import in the same process**
      — the negative-entry invalidation from Task 8.3. Do a search for that system *before* the import
      (expect no matches), import, then search again (expect matches). Without Task 8.3 this second search
      returns nothing.
- [ ] Restart the application against the already-populated database and repeat the first four runtime
      checks. Startup on a *seeded* database takes different branches from startup on an empty one, and only
      the empty path is covered by integration tests.

**Documentation checks:**

- [ ] Task 8.11's differential-facts disposition table exists and every row is decided.
- [ ] The two spec amendments from Task 8.11 are committed.
- [ ] The follow-ups register below is merged into the revised plan's register, so it does not evaporate.

---

## Follow-ups register — additions from this plan

To be merged into the revised plan's register at Task 8.12.

| Item | Where | Why deferred |
|---|---|---|
| `GetResourceTypeIdAsync` caches misses; EF deliberately does not | `SqlServerSearchIndexReferenceDataCache` | Shipped in Phase B/D. EF's doc says a cached resource-type miss makes the row generators drop the resource for the process lifetime. The SqlServer version relies on `CacheResourceTypeId` being called after every insert — a discipline, not a guarantee. Out of scope for a composition-root task. |
| `OnDemandResolvingDictionary` logs and returns false where `LazyLoadingDictionary` throws | `SqlServerSearchIndexReferenceDataCache` | EF turned a transient load failure into an exception specifically because reporting the key absent silently drops a search-index row. The port downgrades it to a warning. Divergence already shipped; changing it is a behaviour change with its own blast radius. |
| `SqlServerCodeSystemImporter` dropped EF's `content=not-present` and `content=supplement` skip branches | `SqlServerCodeSystemImporter` | EF skipped both with a recorded status (`SqlCodeSystemImporter.cs:120–157`); the port has no `not-present` or `supplement` handling at all, so a supplement now imports as an ordinary CodeSystem against the same `url` as the CodeSystem it supplements. Found while reading for Task 8.8; it is a Task 6 regression, not a Task 8 one. **Verify the collision consequence before deciding priority.** |
| `TerminologyImportBootstrapService`'s 5-second `Task.Delay` | `TerminologyImportBootstrapService` | It races `TenantPackagePreloadService` rather than synchronising with it. On a slow cold start the scan runs before packages are loaded and finds nothing. Pre-existing; porting it preserves it. |
| `PackageLoadedSearchParameterSyncHandler` swallows every exception | ported handler | Deliberate ("allow package load to succeed even if sync fails"), but the failure is invisible to the caller and the parameters are *not* in fact "loaded lazily on first search" — an unsynced parameter has no `dbo.SearchParam` row and indexes as a miss. The comment is wrong; the behaviour is intentional. |
| Two writers of `TerminologyImportStatus` | procedures + activity | Task 8.8 removes the activity as a writer. The procedures remain the sole writer, which is correct, but the column is still written from three procedures and one C# method with no single owner. |

---

## What would make this fail

- **Task 8.3 is treated as gold-plating and skipped.** It produces no user-visible feature, and the bug it
  closes only appears after Task 8.8 wires a second cache instance — i.e. it is a bug that this plan
  *introduces* if 8.3 is cut. The symptom is a search silently returning nothing for terminology that
  demonstrably exists, for the lifetime of the process.
- **Task 8.4 is written before the `ValidateManagedIdentityAuthentication` decision is taken.** The
  Transformer Mandate applies: generate the options, let the human choose. An agent that quietly "fixes" it
  has changed a production security posture without approval; one that quietly ports the bug has shipped a
  security control that does nothing. Both are worse than asking.
- **Task 8.11 is compressed into "delete the test projects".** The 13 differential files are the only proof
  the two write paths ever agreed, and their disposition table is the artefact that makes the deletion
  reviewable. Capturing golden snapshots is only possible while EF still runs.
- **The runtime checks in 8.12 are treated as optional.** Five of the eleven components in the
  classification table have zero automated coverage — two because E2E turns their feature flag off, one
  because it never ran at all. The application-start checks are not belt-and-braces; for those five they are
  the *only* verification.
- **8.0's baselines are skipped because "the numbers will not change".** Six commits have landed since the
  last measurement, three of them schema changes. Without a baseline, "at the baseline" is unfalsifiable.

---

## Open questions

Things this plan could not resolve from the code. Each names the specific file, and each must be answered
before the task that depends on it.

1. **Every acceptance number.** Task 8.0 exists because the test suites could not be run while writing this
   plan. No task below 8.0 can claim "at baseline" until 8.0 records one.
2. **Whether `SqlReferenceDataPreloadHandler` is genuinely never invoked.** The mechanism is verified
   (`src/Application/Ignixa.Api/Registrations/BackgroundServicesRegistration.cs:42` registers it
   `As<SqlReferenceDataPreloadHandler>()` only; `src/Application/Ignixa.Api/Infrastructure/
   AutofacMediatorServiceProvider.cs:33` resolves `IEnumerable<INotificationHandler<T>>`), but the
   conclusion depends on how Autofac's `Populate` maps `AddSingleton<T>()` when `T` is both service and
   implementation. Task 8.9 resolves it with a container test rather than an argument; if the test
   contradicts this classification, 8.9 becomes a port, not a deletion.
3. **Whether any of the six unmapped `PackageResource` columns is needed by the terminology import path.**
   `SqlServerPackageResourceRepository.ReadResource` (line 617) maps 11 of 17. I traced
   `SqlServerCodeSystemImporter.ImportAsync` and it reads `ContentHash` and `TerminologyImportStatus` from
   the database directly via `ReadPackageRowAsync` (line 617 of that file), not from the model — so the
   eleven look sufficient. I could not exhaustively verify the ValueSet and ConceptMap import paths,
   which are longer. Task 8.6's first step is to read them.
4. **What `TerminologyImportResult.Status` should be for a skipped import once Task 8.8's second commit
   lands.** With the activity no longer writing status, `CreateSkipped()`'s `Status` becomes unread. Either
   the field is removed from the type (a Domain change touching
   `src/Application/Ignixa.Domain/Terminology/TerminologyImportResult.cs`) or it stays as informational
   output. I have no basis in the code for choosing; it is a design call for the reviewer.
5. **Whether the `Differential/` golden-snapshot substitution is acceptable at all.**
   `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/RowStateSnapshot.cs` exists and appears
   capable of holding a serialised expected state, but I did not read it closely enough to be certain it
   can round-trip to a committed file. If it cannot, Task 8.11's option (ii) is unavailable and more facts
   fall into option (iii) than this plan assumes.
6. **Whether `deploy/azure/azuredeploy.json`'s two `"SqlEntityFramework"` values are consumed by a live
   deployment.** Changing them is safe under the deprecated-synonym rule either way, but if an existing
   deployed parameter file carries the old value, Task 8.10's `RepoGuards` assertion must exclude
   `deploy/` or the guard will fail against a legitimately unchanged artefact. I could not determine
   whether that file is a template or a checked-in live parameter set.
