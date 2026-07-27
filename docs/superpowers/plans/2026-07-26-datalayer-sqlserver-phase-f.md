# Phase F — retire `Ignixa.DataLayer.SqlEntityFramework` — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework` entirely, leaving `Ignixa.DataLayer.SqlServer` as the only data layer.

**Architecture:** Six implementations still on EF sit behind interfaces owned by higher layers, so each port is interface-preserving: reimplement the same contract in `Ignixa.DataLayer.SqlServer` over `ISqlExecutionService`, swap one registration, delete the EF type. No consumer signature changes except `ImportTerminologyResourceActivity`, which takes a `FhirDbContext` directly. Deletion is the last task, because the EF implementation is the only reference the ports can be verified against.

**Tech Stack:** .NET 10, C# (file-scoped namespaces, primary constructors, collection expressions), raw ADO.NET via `ISqlExecutionService`, `Microsoft.Data.SqlClient`, xUnit + Shouldly, live SQL Server for integration tests.

**Design:** `docs/superpowers/specs/2026-07-26-datalayer-sqlserver-phase-f-design.md`

## Global Constraints

- **Phase F changes no behaviour.** Every acceptance number is equality with the pre-phase baseline, not improvement.
- **Baselines that must hold at the end of every task:** `dotnet build All.sln` 0 warnings / 0 errors; `Ignixa.Search.Sql.Tests` **848/848** both TFMs; `Ignixa.Application.Tests` **1125 passed / 0 failed / 1 skipped**; `Ignixa.DataLayer.SqlServer.IntegrationTests` **135/135** for Tasks 1–8 and **85/85** after Task 9.
- **E2E must land on exactly 620 total / 569 passed / 31 failed / 20 skipped, with the same failing test names** as recorded in `docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md`. A *lower* failure count is as suspect as a higher one — nothing in this phase should fix a search gap.
- **Verify before deleting.** Tasks 1–8 run while the EF implementation is still present and registered. Any port merged after Task 9 has no reference to be checked against.
- **Where existing coverage is thin, write the test against the EF implementation FIRST**, prove it passes there, then repoint it at the new implementation. A test written only against the new code proves nothing about equivalence.
- **Do not touch `Ignixa.Search.Sql`.** Phase F is a data-layer phase. Any compiler change means the task has gone out of scope.
- **Do not fix bugs found in the EF implementations.** Port behaviour as-is, including quirks; record anything suspicious in the task report. A behavioural "improvement" during a port is indistinguishable from a port defect at review time.
- Environment for every test run: `unset Platform __DOTNET_PREFERRED_BITNESS __DOTNET_ADD_32BIT`; `TEST_SQL_CONNECTION_STRING` must contain a `Database=`/`Initial Catalog=` segment; `SqlServer__AutomaticSchemaDeploymentEnabled=true`.
- **Table and column identifiers in hand-written SQL come from `SqlCatalog.Default.Table("X").Column("Y")`, not string literals.** `Ignixa.DataLayer.SqlServer` already references `Ignixa.Search.Sql`, and Task 0 extended the catalog to cover the tables these ports write against. A renamed column then fails the build instead of throwing SQL error 207 at runtime. Values and statement structure stay hand-written -- the catalog covers identifiers only.
- No inline comments except where they explain a non-obvious invariant (CLAUDE.md). No `#region`. One type per file. `cancellationToken`, never `ct`.

---

## File Structure

New files land in `src/DataLayer/Ignixa.DataLayer.SqlServer/`, mirroring the EF project's layout so the correspondence stays obvious during review:

```
Ignixa.DataLayer.SqlServer/
  EventStore/SqlServerSourceEventStore.cs                      (Task 1)
  Features/BackgroundJobs/SqlServerBackgroundJobRepository.cs  (Task 2)
  Features/Terminology/SqlServerSystemRepository.cs            (Task 3)
  Features/PackageManagement/SqlServerPackageResourceRepository.cs (Task 4)
  Features/Terminology/SqlServerTerminologyService.cs          (Task 5)
  Features/Terminology/SqlServerCodeSystemImporter.cs          (Tasks 6a-6c)
  Features/Terminology/HybridTerminologyService.cs             (Task 7, moved)
  SqlServerServiceFactory.cs                                   (Task 8)
```

**The porting model to follow:** `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs` and `SqlServerHistoryQueryExecutor.cs`. Both are existing, reviewed, raw-ADO.NET implementations over `ISqlExecutionService` — parameter binding, reader mapping, and batching conventions all come from there rather than being invented per task.

**The specification for each port is the EF source file itself.** These are not greenfield implementations; the EF file defines required behaviour down to its quirks. Every porting task below names its source file and requires reading it in full before writing anything.

---

### Task 0: Extend `SqlCatalog` to the data-layer tables — **COMPLETE**

Done ahead of the plan's execution, recorded here so the sequence is auditable and the constraint above has
a provenance.

The generator already parsed every `Tables/*.sql` file but filtered the result to search-index tables only,
so the tables these ports write against were excluded rather than absent. Widening that filter surfaced
three DDL constructs the parser never had to handle, because the search-index tables do not use them:

1. **Implicit nullability** — 65 columns across the schema omit `NULL`/`NOT NULL`. The regex required it.
2. **`IDENTITY` after the nullability clause** — `PackageResource` declares `BIGINT NOT NULL IDENTITY (1, 1)`
   where `System` declares `INT IDENTITY (1, 1) NOT NULL`. Both orders occur; the regex allowed only one.
3. **Computed columns** — `EventLog`'s `PartitionId AS isnull(...) PERSISTED`. **Not fixed.**

Because of (3) the catalog is a **named set, not the whole schema**: search tables plus `Term*`,
`SourceEvents`, `BackgroundJobs`, `PackageResource`, and `System`. Teaching the parser computed columns is
deferred until something needs them — widening speculatively means fixing parser gaps for tables no one
reads. `Table()` still throws `KeyNotFoundException` on a miss, so an omission stays loud.

**Files:** `src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs` (filter),
`src/Core/Ignixa.Search.Sql.Generators/DdlTableParser.cs` (regex),
`test/Ignixa.Search.Sql.Tests/Catalog/SqlCatalogDataLayerTablesTests.cs` (new).

- [x] Widen the generator's table filter to the named set.
- [x] Make the nullability clause optional; allow `IDENTITY` on either side of it.
- [x] Pin every construct with a test — both IDENTITY orders, `NVARCHAR (MAX)`, an inline `DEFAULT`, and a
      negative case asserting `EventLog` still throws so the set stays deliberate.
- [x] Verified: `dotnet build All.sln` 0/0; `Ignixa.Search.Sql.Generators.Tests` 11/11 both TFMs;
      `Ignixa.Search.Sql.Tests` **848/848** both TFMs (was 839 — the 9 new facts above).

---

### Task 1: Port `SqlSourceEventStore` — **COMPLETE**

Smallest port (157 lines, 4 LINQ constructs). Establishes the raw-ADO.NET pattern for every later task.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/EventStore/SqlServerSourceEventStore.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/EventStore/SqlSourceEventStore.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/EventStore/SourceEventEntity.cs` (column mapping)
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs` (registration swap)
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/EventStore/SqlServerSourceEventStoreTests.cs`

**Interfaces:**
- Produces: `SqlServerSourceEventStore : ISourceEventStore` (`Ignixa.Conformance.Events.Abstractions`)
```csharp
Task<IReadOnlyList<SourceEvent>> AppendAsync(IEnumerable<NewSourceEvent> events, CancellationToken cancellationToken);
IAsyncEnumerable<SourceEvent> ReadAllAsync(CancellationToken cancellationToken);
IAsyncEnumerable<SourceEvent> ReadFromAsync(long afterEventId, CancellationToken cancellationToken);
IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken);
```
- Consumes: `ISqlExecutionService`, `ILogger<SqlServerSourceEventStore>`

- [x] **Step 1: Read the EF implementation and its entity in full.** Record the `dbo.SourceEvents` column list, the ordering guarantee of each read method, and what `AppendAsync` returns (assigned event ids — confirm whether they come from an OUTPUT clause or a post-insert read).

- [x] **Step 2: Write the failing test against the EF implementation.** Round-trip: append three events across two stream ids, then assert `ReadAllAsync` returns all three in ascending event-id order, `ReadFromAsync` skips correctly, and `ReadStreamAsync` filters by stream. Register the **EF** store in the fixture for this step.

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerSourceEventStoreTests"`
Expected: PASS against EF — this proves the test encodes real current behaviour, not an assumption.

- [x] **Step 3: Implement `SqlServerSourceEventStore`** over `ISqlExecutionService`, following `SqlServerHistoryQueryExecutor`'s reader-mapping style. `AppendAsync` uses a single multi-row INSERT with `OUTPUT INSERTED.EventId` rather than a round-trip per event.

- [x] **Step 4: Repoint the test's fixture at the new implementation and run.**
Expected: PASS with identical assertions and no edits to them. Any assertion that needs changing is a behavioural difference — stop and report it rather than adjusting the test.

- [x] **Step 5: Swap the registration** in `DataLayerRegistration.cs`; leave the EF type in place (Task 9 deletes it).

- [x] **Step 6: Full verification.**
Run: `dotnet build All.sln` → 0/0; `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → 135/135 + the new facts.

- [x] **Step 7: Commit** — `feat(sqlserver): port the source event store off EF`.

---

### Task 2: Port `SqlBackgroundJobRepository<T>`

250 lines, 6 LINQ constructs, generic over `T : IJobDefinition`.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/BackgroundJobs/SqlServerBackgroundJobRepository.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/BackgroundJobs/SqlBackgroundJobRepository.cs`, `Entities/BackgroundJobEntity.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/SqlServerBackgroundJobRepositoryTests.cs`

**Interfaces:**
- Produces: `SqlServerBackgroundJobRepository<T> : IBackgroundJobRepository<T> where T : class, IJobDefinition`
```csharp
Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken);
Task<BackgroundJob<T>?> GetAsync(string jobId, int tenantId, CancellationToken cancellationToken);
Task UpdateAsync(BackgroundJob<T> job, int tenantId, CancellationToken cancellationToken);
Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int? jobType = null, CancellationToken cancellationToken = default);
Task DeleteAsync(string jobId, int tenantId, CancellationToken cancellationToken);
```

- [ ] **Step 1: Read the EF implementation.** Note specifically how `T` is serialised into the job-definition column (JSON?) and which serializer options — a mismatch here silently breaks deserialisation of jobs written before the port.

- [ ] **Step 2: Write the round-trip test against EF.** Create a job with a real `T`, `GetAsync` it back, assert the definition deserialises equal. Cover `ListAsync` with and without the `jobType` filter, `UpdateAsync` status transition, and `DeleteAsync`. Include a **tenant-isolation** case: a job created under tenant A must not be visible to `GetAsync(jobId, tenantB)`.
Expected: PASS against EF.

- [ ] **Step 3: Implement over `ISqlExecutionService`,** reusing the EF implementation's exact serializer configuration.

- [ ] **Step 4: Repoint the fixture, re-run, assertions unchanged.**

- [ ] **Step 5: Swap the registration.**

- [ ] **Step 6: Full verification** (build 0/0, integration 135/135 + new facts).

- [ ] **Step 7: Commit** — `feat(sqlserver): port the background job repository off EF`.

---

### Task 3: Port `SqlSystemRepository`

126 lines. Small, but a leaf dependency of both terminology tasks, so it must land before them.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerSystemRepository.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/SqlSystemRepository.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/SqlServerSystemRepositoryTests.cs`

**Interfaces:**
- Produces: `SqlServerSystemRepository : ISystemRepository`
```csharp
Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken);
Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken);
```

- [ ] **Step 1: Read the EF implementation.** `GetOrCreateAsync` is a get-or-insert race candidate. Record exactly how EF handles a concurrent duplicate insert — unique-constraint catch-and-reread, or nothing at all. **The port must reproduce whichever it is**, not improve on it (Global Constraints).

- [ ] **Step 2: Write the test against EF:** `GetOrCreateAsync` twice with the same URI returns the same id; `GetSystemIdAsync` returns null for an unknown URI. Add a **concurrency** case — ten parallel `GetOrCreateAsync` calls for one new URI must yield one distinct id.
Expected: PASS against EF. If the concurrency case fails against EF, that is a pre-existing defect: record it in the report, mark the case `Skip` with a reference, and do **not** fix it here.

- [ ] **Step 3: Implement,** preferring a single `MERGE`/`INSERT ... WHERE NOT EXISTS` + read round-trip. Note the repo's documented `sp_reset_connection` behaviour: `ISqlExecutionService` opens a fresh connection per call, so no temp-table or session state can be carried between the insert and the read.

- [ ] **Step 4: Repoint, re-run, assertions unchanged.**

- [ ] **Step 5: Swap the registration.**

- [ ] **Step 6: Full verification.**

- [ ] **Step 7: Commit** — `feat(sqlserver): port the terminology system repository off EF`.

---

### Task 4: Port `SqlPackageResourceRepository`

943 lines, 58 LINQ constructs — the first genuine rewrite.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/PackageManagement/SqlServerPackageResourceRepository.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/PackageManagement/SqlPackageResourceRepository.cs`, `Entities/PackageResourceEntity.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/SqlServerPackageResourceRepositoryTests.cs`

**Interfaces:**
- Produces: `SqlServerPackageResourceRepository : IPackageResourceRepository` — `UpsertAsync`, `BatchUpsertAsync`, `GetByCanonicalAsync(canonical, version?)`, `GetFromPackageAsync(packageId, packageVersion, canonical)`, `GetLatestByCanonicalAsync(canonical, resourceType?)`, `ListPackageResourcesAsync(packageId, packageVersion, …)` — read the interface file for the complete list and exact optional-parameter defaults.
- Consumes: `ISqlExecutionService`.

- [ ] **Step 1: Read the EF implementation in full and enumerate every public method**, writing down for each: its SQL shape, its ordering guarantee, and its null/empty return convention. This enumeration goes in the task report and is the checklist for Step 2.

- [ ] **Step 2: Write one test per public method against EF.** `GetLatestByCanonicalAsync` needs explicit version-ordering coverage — seed three versions out of order and assert which one wins, since "latest" is a semantic the port could plausibly get wrong while still compiling. `BatchUpsertAsync` needs an update-existing-plus-insert-new mixed batch.
Expected: all PASS against EF.

- [ ] **Step 3: Implement.** `BatchUpsertAsync` must batch — follow `SqlServerMergeRepository`'s TVP/batching convention rather than issuing one statement per item.

- [ ] **Step 4: Repoint, re-run, assertions unchanged.**

- [ ] **Step 5: Swap the registration.**

- [ ] **Step 6: Full verification.**

- [ ] **Step 7: Commit** — `feat(sqlserver): port the package resource repository off EF`.

---

### Task 5: Port `SqlTerminologyService`

771 lines, 46 LINQ constructs, seven independently testable public operations.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerTerminologyService.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/SqlTerminologyService.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/SqlServerTerminologyServiceTests.cs`

**Interfaces:**
- Produces: `SqlServerTerminologyService : ITerminologyService` (`Ignixa.Validation.Abstractions`)
```csharp
Task<LookupResult> LookupCodeAsync(...);
Task<ExpandResult?> ExpandValueSetAsync(...);
Task<TerminologyValidationResult> ValidateCodeAsync(...);
Task<BindingValidationResult> ValidateBindingAsync(...);
Task<TranslateResult> TranslateCodeAsync(...);
Task<SubsumesResult> SubsumesAsync(...);
Task<TerminologyImportStatus?> GetImportStatusAsync(...);
```
Read the source for exact parameter lists.
- Consumes: `ISqlExecutionService`, `ISystemRepository` (Task 3).

- [ ] **Step 1: Read the EF implementation in full.** `SubsumesAsync` and `ExpandValueSetAsync` are the two with real query complexity (hierarchy traversal and set expansion) — record whether either uses a recursive CTE or client-side recursion, because that determines whether the port is a translation or a redesign.

- [ ] **Step 2: Write one test per operation against EF,** seeding a small CodeSystem with a two-level hierarchy so `SubsumesAsync` has something real to traverse. Cover the negative case for each (unknown code, unknown system) — these return typed results rather than throwing, and the exact shape is easy to get wrong.
Expected: all PASS against EF.

- [ ] **Step 3: Implement,** one operation at a time, running that operation's test after each.

- [ ] **Step 4: Repoint, re-run, assertions unchanged.**

- [ ] **Step 5: Swap the registration.**

- [ ] **Step 6: Full verification.**

- [ ] **Step 7: Commit** — `feat(sqlserver): port the terminology service off EF`.

---

### Task 6: Port `SqlCodeSystemImporter`

1,874 lines, 86 LINQ constructs — the largest single item in the phase and the most likely place for a silent behavioural difference. **Split into three commits**, each independently reviewable.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerCodeSystemImporter.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/SqlCodeSystemImporter.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/SqlServerCodeSystemImporterTests.cs`

**Interfaces:**
- Produces: `SqlServerCodeSystemImporter : ITerminologyImporter`
```csharp
Task<TerminologyImportResult> ImportCodeSystemAsync(int tenantId, PackageResource packageResource, CancellationToken cancellationToken);
Task<TerminologyImportResult> ImportValueSetAsync(int tenantId, PackageResource packageResource, CancellationToken cancellationToken);
Task<TerminologyImportResult> ImportConceptMapAsync(int tenantId, PackageResource packageResource, CancellationToken cancellationToken);
```
- Consumes: `ISqlExecutionService`, `ISystemRepository` (Task 3).

- [ ] **Step 1: Read the EF implementation in full and map every `SaveChangesAsync` call site** (there are at least nine). Each is a transaction/visibility boundary that EF's change tracker currently defines implicitly. Write the list into the task report: what is pending at each save, and whether a failure between two of them leaves a partially-imported CodeSystem. **This map is the deliverable of Step 1** — the port cannot be correct without it.

- [ ] **Step 2: Record the dual insert strategy.** `ImportCodeSystemAsync` branches on `BulkInsertThreshold = 1000`: above it, a hand-written `BulkInsertConceptsAsync`; at or below it, `_context.TermConcepts.AddRange` + `SaveChangesAsync`. The port must make the small-set path's batching explicit rather than inheriting change-tracker behaviour. Both paths must remain, with the same threshold, so import behaviour does not change at the boundary.

- [ ] **Step 3: Write tests against EF — Task 6a.** Import a small CodeSystem (well under 1,000 concepts) and assert row counts in `dbo.TermCodeSystem`/`dbo.TermConcept`, the returned `TerminologyImportResult`, and idempotency (importing the same resource twice does not duplicate). Add a case **straddling the threshold**: 1,000 and 1,001 concepts must produce identical row state via different code paths.
Expected: PASS against EF.

- [ ] **Step 4: Implement `ImportCodeSystemAsync` only, both insert paths. Commit as 6a.**
Run the Step 3 tests repointed at the new implementation; assertions unchanged.
Commit — `feat(sqlserver): port CodeSystem import off EF`.

- [ ] **Step 5: Tests then implementation for `ImportValueSetAsync` — commit as 6b.** Same discipline: test against EF first, then port, then repoint. Include a ValueSet that references a CodeSystem imported in the same test, since the cross-reference resolution is where the EF version does most of its work.
Commit — `feat(sqlserver): port ValueSet import off EF`.

- [ ] **Step 6: Tests then implementation for `ImportConceptMapAsync` — commit as 6c.**
Commit — `feat(sqlserver): port ConceptMap import off EF`.

- [ ] **Step 7: Swap the registration and run full verification** (build 0/0, integration 135/135 + new facts, `Ignixa.Application.Tests` 1125/0/1).

---

### Task 7: Move `HybridTerminologyService`

195 lines. A composition over `ITerminologyService`, not a data-access type — a namespace move, not a port.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/HybridTerminologyService.cs`
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Features/Terminology/HybridTerminologyService.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs`

- [ ] **Step 1: Confirm it has no EF dependency** — `grep -n "FhirDbContext\|_context\|Microsoft.EntityFrameworkCore" ` over the file. If any hit appears, stop: it is a port, not a move, and needs its own test-against-EF cycle like Tasks 1–6.

- [ ] **Step 2: Move the file, change the namespace, update the registration and any usings.**

- [ ] **Step 3: Verify** — build 0/0; `Ignixa.DataLayer.SqlServer.IntegrationTests` 135/135; `Ignixa.Application.Tests` 1125/0/1.

- [ ] **Step 4: Commit** — `refactor(sqlserver): move HybridTerminologyService out of the EF project`.

---

### Task 8: Relocate the composition root and rename the storage type

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerServiceFactory.cs`
- Read as spec: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` (497 lines)
- Modify: `src/Application/Ignixa.Api/Registrations/DataLayerRegistration.cs` (drop `IDbContextFactory<FhirDbContext>`)
- Modify: `src/Application/Ignixa.Api/Services/SqlReferenceDataPreloadService.cs`
- Modify: `src/Application/Ignixa.Api/Services/TerminologyImportBootstrapService.cs`
- Modify: `src/Application/Ignixa.Application.BackgroundOperations/Terminology/Activities/ImportTerminologyResourceActivity.cs` (**takes `FhirDbContext` directly — the one real signature change in the phase**)
- Modify: wherever the storage-type string is parsed (`SqlExecutionService` and tenant-config binding — locate with `grep -rn '"SqlEntityFramework"' --include=*.cs src/`)
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/StorageTypeNameTests.cs`

- [ ] **Step 1: Read the EF factory's `CreateServiceFactory` in full.** It still does work Phase F must preserve: schema deployment, search-parameter catalog sync, and once-per-tenant reference-cache preloading. Only the `FhirDbContext`/`dbContextOptions` construction is genuinely dead. List what must survive in the task report before writing the replacement.

- [ ] **Step 2: Write the storage-type test first.** Assert `"SqlServer"` resolves, and `"SqlEntityFramework"` **still** resolves to the same backend as a deprecated synonym. Deployed tenant configs use the old string, so this is a compatibility guarantee, not cosmetics.

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter "FullyQualifiedName~StorageTypeNameTests"`
Expected: FAIL — `"SqlServer"` is not yet a recognised value.

- [ ] **Step 3: Implement `SqlServerServiceFactory`,** carrying over everything from Step 1's list, with no `FhirDbContext`. The `CreateRepository`/`CreateSearchService` delegates lose their unused `FhirDbContext` parameter.

- [ ] **Step 4: Add the storage-type synonym mapping.** Re-run Step 2's test → PASS.

- [ ] **Step 5: Repoint the three services** off `SqlEntityFrameworkRepositoryFactory`. `ImportTerminologyResourceActivity` needs its `FhirDbContext` parameter replaced with the ported terminology dependencies from Tasks 5–6.

- [ ] **Step 6: Full verification, including a real application start.** Build 0/0; integration 135/135; `Ignixa.Application.Tests` 1125/0/1; `Ignixa.Api.Tests` 135/135. Then start the API against a real tenant database and exercise a terminology import and a package load. Phase B's history is the argument: its ~10 missing tables were found by running the app, not by any test.

- [ ] **Step 7: Commit** — `refactor(sqlserver): relocate the composition root out of the EF project`.

---

### Task 9: Delete the EF project

Last task. Everything before this is reversible; this is not.

**Files:**
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/` (entire project, ~10,600 lines)
- Delete: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/` (13 files, **50 facts/theories**)
- Delete: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/`
- Delete: `test/Ignixa.DataLayer.SqlEntityFramework.Tests/` (`Ignixa.DataLayer.LegacySqlEF.Tests.csproj` — already fails to compile, already absent from `All.sln`)
- Modify: `test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj` (drop the EF `ProjectReference`; the 3 source hits are config strings and a comment, not type usage)
- Modify: `All.sln`
- Modify: `docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md`

- [ ] **Step 1: Confirm nothing live still references the project.**
Run: `grep -rn "Ignixa.DataLayer.SqlEntityFramework\|FhirDbContext" --include=*.cs --include=*.csproj src/ test/ | grep -v "/Ignixa.DataLayer.SqlEntityFramework"`
Expected: only the E2E csproj reference and the three E2E string/comment hits. **Any other hit means an earlier task is incomplete — stop and finish it rather than deleting.**

- [ ] **Step 2: Delete the projects and the differential tests,** and remove them from `All.sln`.

- [ ] **Step 3: Amend the gap-closure design document.** It assumes the legacy engine is available for row-level comparison when closing the remaining 31 search gaps. Record that the harness is gone, what that costs the five remaining groups, and that `docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md`'s differential evidence is now historical rather than reproducible.

- [ ] **Step 4: Full verification against every Global Constraint.**
Run: `dotnet build All.sln` → 0/0.
Run: `dotnet test test/Ignixa.Search.Sql.Tests` → **848/848** both TFMs.
Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → **85/85**, and confirm 135 − 85 = 50 is accounted for **entirely** by the deleted differential facts. Any other missing test is a mistake in Step 2.
Run: `dotnet test test/Ignixa.Application.Tests` → 1125/0/1.
Run: the E2E suite → **620 / 569 / 31 / 20**, and diff the failing test names against the gap-closure document's recorded set. Same names, or the phase has regressed something.

- [ ] **Step 5: Commit** — `refactor: delete Ignixa.DataLayer.SqlEntityFramework`.

---

## Acceptance for the phase

- `src/DataLayer/` contains exactly one SQL data layer.
- No source file outside the deleted projects mentions `FhirDbContext` or `Ignixa.DataLayer.SqlEntityFramework`.
- Every number in Global Constraints holds, with E2E matching on **failing test names**, not just counts.
- A real application start completes with terminology import and package load exercised (Task 8, Step 6).
