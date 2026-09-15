# Materialized Observation `$lastn` Code Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `$lastn`'s rejected query-time coding graph with an exact, transactionally write-maintained SQL Server materialization and prove the indexed read path meets a warm P95 below 100 ms.

**Architecture:** Add scope-partitioned identity, membership, reference-counted edge, current-group, generation, and dirty-observation tables to the SSDT project. New wrapper procedures atomically run the unchanged merge, reindex, and hard-delete procedures together with graph maintenance; a raw ADO.NET writer is the only public mutation boundary, while `LastNEmitter` reads only ready materialized groups and a direct executor maps non-ready scopes to a loud unavailable failure.

**Tech Stack:** C# / .NET 9 and .NET 10, `Ignixa.Search` and `Ignixa.Search.Sql`, raw `Microsoft.Data.SqlClient`, `Microsoft.Build.Sql` SSDT/DacFx deployment, SQL Server application locks, xUnit, Shouldly, ScriptDom grammar validation, Docusaurus 3.10.

**Spec:** `docs/features/search/investigations/materialized-observation-code-groups.md`

## Global Constraints

- Preserve exact transitive equivalence: two codings on one current Observation are translations, and translation is transitive across Observations.
- Only current, non-deleted Observations contribute to the graph and current group table.
- Identity equality is scope plus null-safe `SystemId`, case-sensitive `Code`, and case-sensitive `CodeOverflow`; `SHA2_256` is only a seek aid and a forced collision must not merge unequal values.
- `ComponentCodeIdentityId` is the minimum identity id in the connected component and is never exposed as a durable external identifier.
- Text-only values use `TokenText` only when no coding exists and compare under `Latin1_General_100_CS_AS`.
- Preserve `RANK()`, not `ROW_NUMBER()` or `DENSE_RANK()`, including all effective-time ties at the requested boundary and the existing deterministic missing-effective fallback.
- A scope is queryable only when `LastNCodeGroupGeneration.State = 'Ready'`; `Pending`, `Building`, `Failed`, or a missing row produces an explicit unavailable failure and never invokes the rejected query-time graph.
- Preserve the existing `MergeResources`, `UpdateResourceSearchParams`, and `HardDeleteResource` procedure bodies and every existing TVP definition. Add wrappers and new supporting objects only.
- Group maintenance is correctness-critical and executes in the same SQL transaction as its base write. Do not combine it with the nullable, best-effort `PostMergeExtensionUpdater`.
- Acquire transaction-owned exclusive application locks named `LastNCodeGroup:{ResourceTypeId}:{SearchParamId}` in lexicographic scope order. Lock failure, base-write failure, invariant failure, or repair failure rolls back the entire wrapper.
- The direct writer takes the existing TVP-shaped typed rows, exposes no independent graph mutation method, and uses raw ADO.NET only; `Ignixa.DataLayer.SqlServer` must not reference EF Core or an ORM.
- Do not add `$lastn` behavior to `SqlEntityFrameworkSearchService`, route compiled `$lastn` through it, or add an EF fallback.
- Keep ordinary candidate filtering, authorization, tenant visibility, emitted parameter order, deterministic outer ordering, and ScriptDom-valid SQL unchanged except for replacing graph construction with readiness and materialized-group access.
- Do not add ordinary paging, `_sort`, `_include`, or `_revinclude` semantics to `$lastn`.
- Backfill is resumable by committed surrogate-id ranges. Writes during `Building` upsert the affected current Observation into the active generation's dirty table; final replay, full-scope repair, invariant validation, and `Ready` transition occur under the scope lock.
- Failure or cancellation records `Failed` and a bounded failure reason. A subsequent attempt increments `Generation` and starts a fresh build.
- The acceptance workload is exactly 10,000 current Observation candidates, 400 independent groups, one to three codings per Observation, an `a -> b -> c -> d` bridge in every group, five warm-ups, and 30 measured runs.
- Acceptance requires warm P95 below 100 ms and no actual-plan `SpillToTempDb` or positive `SpillLevel` marker. Record SQL Server version, compatibility level, CPU, memory, command timeout, P50, P95, maximum, and graph cardinalities.
- Bump `SchemaVersionConstants.CurrentVersion` from `1` to `2`, retain `MinSupportedReadVersion = 1`, and append `Version 2 (expand)` to the immutable changelog.
- Follow repository conventions: file-scoped namespaces, one type per C# file, explicit types unless obvious, all asynchronous APIs accept `CancellationToken cancellationToken`, and tests use xUnit, Shouldly, Arrange/Act/Assert, and `GivenContext_WhenAction_ThenResult`.
- Every commit includes `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.
- Focused validation must pass before full validation: SQL database project build, relevant unit tests, relevant live SQL Server integration tests, `dotnet build All.sln` with zero warnings/errors, and `npm run build` from `docs/site`.

## File Map

### SQL schema and procedures

- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastNCodeIdentity.sql` — scoped code identities and component labels.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastNObservationCodeMembership.sql` — current Observation-to-identity membership.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastNCodeEdge.sql` — unordered edges with positive support counts.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastNObservationCodeGroup.sql` — one coded or text-only current group per Observation and scope.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastNCodeGroupGeneration.sql` — generation lifecycle and snapshot high-water mark.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastNCodeGroupDirtyObservation.sql` — deduplicated writes observed during a build.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Types/LastNResourceScopeList.sql` — `(ResourceTypeId, SearchParamId, ResourceSurrogateId)` batch input for graph maintenance.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/MaintainLastNCodeGroups.sql` — idempotent remove/add and affected-component repair primitive.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/MergeResourcesAndMaintainLastNGroups.sql` — transaction-owning merge wrapper with the unchanged merge signature.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/UpdateResourceSearchParamsAndMaintainLastNGroups.sql` — transaction-owning reindex wrapper with the unchanged update signature.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/HardDeleteResourceAndMaintainLastNGroups.sql` — transaction-owning hard-delete wrapper.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/EnableLastNCodeGroupScope.sql` — idempotently register an enabled scope in `Pending`.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/StartLastNCodeGroupGeneration.sql` — increment generation, set `Building`, and capture high water.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/BackfillLastNCodeGroupBatch.sql` — idempotently materialize a committed surrogate range.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/CompleteLastNCodeGroupGeneration.sql` — replay dirty ids, validate, and atomically mark ready.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/FailLastNCodeGroupGeneration.sql` — record a bounded failure for the active generation.

### Direct SQL Server boundary

- `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlResourceIndexBatch.cs` — typed holders for the existing TVP row streams.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlResourceMergeRequest.cs` — merge flags plus a typed index batch.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlResourceReindexRequest.cs` — reindex batch.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/ISqlResourceIndexWriter.cs` — merge, reindex, and hard-delete contract.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlResourceIndexWriter.cs` — constructs wrapper commands and executes them tenant-scoped.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/LastNCodeGroupScope.cs` — typed resource/search-parameter scope.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/LastNCodeGroupGenerationStatus.cs` — generation/state result.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/ILastNCodeGroupBackfillService.cs` — resumable build contract.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/LastNCodeGroupBackfillService.cs` — batch orchestration and failure recording.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/ILastNSearchExecutor.cs` — direct compiled-search execution contract.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/LastNUnavailableException.cs` — explicit non-ready failure.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/LastNSearchExecutor.cs` — raw ADO.NET execution and SQL error mapping.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs` — registrations.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj` — reference `Ignixa.Search.Sql`.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionConstants.cs` — schema version 2.

### Compiler, tests, and documentation

- `src/Core/Ignixa.Search.Sql/Builders/LastNEmitter.cs` — readiness guard and indexed materialized ranking.
- `test/Ignixa.Search.Sql.Tests/Compilation/LastNCompilationTests.cs` — deterministic SQL, parameter-order, grammar, and rejected-path guards.
- `test/Ignixa.DataLayer.SqlServer.Tests/SqlResourceIndexWriterTests.cs` — exact command/TVP contract and registrations.
- `test/Ignixa.DataLayer.SqlServer.Tests/LastNSearchExecutorTests.cs` — parameter typing and unavailable mapping.
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNTestDatabase.cs` — shared isolated-database deployment, catalog queries, seeding, and wrapper execution helpers for the materialization suites.
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNSchemaDeploymentTests.cs` — deployed object, key, constraint, index, and upgrade catalog checks.
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs` — identity, graph, merge/split, delete, rollback, and concurrency behavior.
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupBackfillTests.cs` — resume, dirty replay, generation transitions, and readiness.
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNMaterializedSqlSemanticsTests.cs` — direct indexed query semantics.
- `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNMaterializedSqlBenchmarkTests.cs` — 10,000-row acceptance workload.
- `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/LastNSqlSemanticsTests.cs` — remove after equivalent materialized coverage exists.
- `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/LastNSqlBenchmarkTests.cs` — remove rejected production-path benchmark after retaining its results in the investigation.
- `src/Core/Ignixa.Search.Sql/README.md` — materialized schema dependency and direct-execution status.
- `src/DataLayer/Ignixa.DataLayer.SqlServer/README.md` — writer, executor, generation, and operational behavior.
- `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/README.md` — Ignixa-owned materialization objects and provenance boundary.
- `docs/features/search/investigations/lastn-direct-search.md` — preserve rejected benchmark evidence and identify isolated test-only history.
- `docs/features/search/investigations/materialized-observation-code-groups.md` — record implementation evidence and verdict.
- `docs/features/search/readme.md` — final investigation statuses.
- `docs/site/docs/core-sdk/search.md` — document compiler/materialization contract without claiming an API route exists.

---

### Task 1: Add the materialized schema and prove its deployed catalog

**Files:**
- Create the six `Tables/LastN*.sql` files and `Types/LastNResourceScopeList.sql` listed in the file map.
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionConstants.cs`
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNTestDatabase.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNSchemaDeploymentTests.cs`

**Interfaces:**
- Consumes: existing `Resource`, `TokenSearchParam`, `TokenText`, and schema deployment conventions.
- Produces: the six tables and `dbo.LastNResourceScopeList(ResourceTypeId smallint, SearchParamId smallint, ResourceSurrogateId bigint)`; schema version `2`.

- [ ] **Step 1: Write the failing schema deployment test**

Add a live-database test that deploys a fresh uniquely named database, queries `sys.tables`, `sys.columns`, `sys.indexes`, `sys.foreign_keys`, and `sys.check_constraints`, and makes exact assertions:

```csharp
[SkippableFact]
public async Task GivenTheCurrentDacpac_WhenDeployed_ThenLastNMaterializationCatalogMatchesTheDesign()
{
    await using LastNTestDatabase database = await LastNTestDatabase.CreateAndDeployAsync();

    IReadOnlyList<string> tables = await database.ReadStringsAsync(
        "SELECT name FROM sys.tables WHERE name LIKE 'LastN%' ORDER BY name;");
    tables.ShouldBe([
        "LastNCodeEdge",
        "LastNCodeGroupDirtyObservation",
        "LastNCodeGroupGeneration",
        "LastNCodeIdentity",
        "LastNObservationCodeGroup",
        "LastNObservationCodeMembership",
    ]);

    (await database.ReadPrimaryKeyColumnsAsync("LastNCodeEdge"))
        .ShouldBe(["ResourceTypeId", "SearchParamId", "LeftCodeIdentityId", "RightCodeIdentityId"]);
    (await database.ReadIndexNamesAsync("LastNObservationCodeGroup"))
        .ShouldContain("IX_LastNObservationCodeGroup_Rank");
    (await database.ReadCheckDefinitionsAsync("LastNCodeEdge"))
        .Single().ShouldContain("[LeftCodeIdentityId]<[RightCodeIdentityId]");
    (await database.ReadCheckDefinitionsAsync("LastNCodeGroupGeneration"))
        .Single().ShouldContain("'Pending','Building','Ready','Failed'");
}
```

Also assert `Code` and `CodeOverflow` use `Latin1_General_100_CS_AS`, both scoped foreign keys exist, the group representation check allows exactly coded XOR text-only, and `SchemaVersionConstants.CurrentVersion == 2`.

Implement `LastNTestDatabase` as an internal `IAsyncDisposable` in its own file. It must use the existing `TEST_SQL_CONNECTION_STRING`/`SchemaDeployer` pattern, create a unique database, expose its open `SqlConnection`, provide the exact catalog readers used above, and drop the database in `DisposeAsync`. Add typed helper methods used by later tasks for seeding `Resource`, `TokenSearchParam`, `TokenText`, and `DateTimeSearchParam` rows and for executing each new stored procedure; every helper accepts `CancellationToken cancellationToken` and uses explicit `SqlDbType` values.

- [ ] **Step 2: Run the focused test and verify red**

Run:

```powershell
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastNSchemaDeploymentTests" --framework net10.0 --no-restore
```

Expected: FAIL because no `LastN*` tables or `LastNResourceScopeList` type exist and the compiled schema version is still `1`.

- [ ] **Step 3: Add the tables, keys, checks, and indexes**

Implement the DDL exactly from the spec. The identity hash default is not computed by the table; maintenance supplies it after serializing null markers and byte lengths. Representative complete invariants:

```sql
CREATE TABLE dbo.LastNCodeEdge (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    LeftCodeIdentityId BIGINT NOT NULL,
    RightCodeIdentityId BIGINT NOT NULL,
    SupportCount INT NOT NULL,
    CONSTRAINT PK_LastNCodeEdge PRIMARY KEY CLUSTERED
        (ResourceTypeId, SearchParamId, LeftCodeIdentityId, RightCodeIdentityId),
    CONSTRAINT CH_LastNCodeEdge_Order CHECK (LeftCodeIdentityId < RightCodeIdentityId),
    CONSTRAINT CH_LastNCodeEdge_Support CHECK (SupportCount > 0),
    CONSTRAINT FK_LastNCodeEdge_Left FOREIGN KEY
        (LeftCodeIdentityId, ResourceTypeId, SearchParamId)
        REFERENCES dbo.LastNCodeIdentity
        (CodeIdentityId, ResourceTypeId, SearchParamId),
    CONSTRAINT FK_LastNCodeEdge_Right FOREIGN KEY
        (RightCodeIdentityId, ResourceTypeId, SearchParamId)
        REFERENCES dbo.LastNCodeIdentity
        (CodeIdentityId, ResourceTypeId, SearchParamId)
);

CREATE INDEX IX_LastNCodeEdge_Right
    ON dbo.LastNCodeEdge
       (ResourceTypeId, SearchParamId, RightCodeIdentityId, LeftCodeIdentityId);
```

Use the exact table columns, keys, included columns, and checks in the spec. Add:

```sql
CREATE TYPE dbo.LastNResourceScopeList AS TABLE (
    ResourceTypeId SMALLINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    ResourceSurrogateId BIGINT NOT NULL,
    PRIMARY KEY (ResourceTypeId, SearchParamId, ResourceSurrogateId));
```

- [ ] **Step 4: Bump and classify the schema version**

Set `CurrentVersion = 2`, leave `MinSupportedReadVersion = 1`, and append:

```csharp
// Version 2 (expand) -- adds the materialized Observation code-group tables,
// indexes, constraints, supporting TVP, and transaction-owning wrapper procedures.
```

- [ ] **Step 5: Build and run the catalog test green**

Run:

```powershell
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastNSchemaDeploymentTests" --framework net10.0 --no-restore
```

Expected: database project build succeeds; every exact catalog assertion passes.

- [ ] **Step 6: Commit**

```powershell
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/LastN*.sql src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Types/LastNResourceScopeList.sql src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionConstants.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNTestDatabase.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNSchemaDeploymentTests.cs
git commit -m "Add materialized lastn schema" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 2: Implement exact graph maintenance, reference counts, and component repair

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/MaintainLastNCodeGroups.sql`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs`

**Interfaces:**
- Consumes: `dbo.LastNResourceScopeList`; current `Resource`, `TokenSearchParam`, and `TokenText` rows.
- Produces: `dbo.MaintainLastNCodeGroups @Mode varchar(8), @Resources dbo.LastNResourceScopeList READONLY`, where mode is exactly `Remove` or `Add`.

- [ ] **Step 1: Write failing graph semantics tests**

Create tests with public stored-procedure behavior:

```csharp
[SkippableFact]
public async Task GivenTransitiveBridges_WhenContributionsAreAdded_ThenAllIdentitiesUseTheMinimumComponent()
{
    await SeedObservationAsync(1, ["a", "b"]);
    await SeedObservationAsync(2, ["b", "c"]);
    await MaintainAsync("Add", 1, 2);

    IReadOnlyList<long> labels = await ReadComponentLabelsAsync("a", "b", "c");
    labels.Distinct().Count().ShouldBe(1);
    labels[0].ShouldBe(await ReadMinimumIdentityIdAsync("a", "b", "c"));
}

[SkippableFact]
public async Task GivenTwoObservationsSupportingOneEdge_WhenOneIsRemoved_ThenSupportRemainsOne()
{
    await SeedObservationAsync(1, ["a", "b"]);
    await SeedObservationAsync(2, ["a", "b"]);
    await MaintainAsync("Add", 1, 2);

    await MaintainAsync("Remove", 1);

    (await ReadSupportCountAsync("a", "b")).ShouldBe(1);
}

[SkippableFact]
public async Task GivenTheLastBridgeIsRemoved_WhenRepairRuns_ThenTheComponentSplits()
{
    await SeedObservationAsync(1, ["a", "b"]);
    await SeedObservationAsync(2, ["b", "c"]);
    await MaintainAsync("Add", 1, 2);

    await MaintainAsync("Remove", 2);

    (await ReadComponentLabelAsync("a")).ShouldBe(await ReadComponentLabelAsync("b"));
    (await ReadComponentLabelAsync("c")).ShouldNotBe(await ReadComponentLabelAsync("a"));
}
```

Add exact cases for a single coding, duplicate coding rows, text-only case sensitivity, no coding/text row, null versus non-null system, and long-code prefix/overflow. For collision coverage, seed two `LastNCodeIdentity` rows with unequal full values but the same explicit `CodeHash`, add matching Observations, and prove maintenance reuses the correct row by full equality rather than the first hash match; do not add a production test-only parameter.

- [ ] **Step 2: Run tests and verify red**

Run:

```powershell
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastNCodeGroupMaintenanceTests" --framework net10.0 --no-restore
```

Expected: FAIL with `Could not find stored procedure 'dbo.MaintainLastNCodeGroups'`.

- [ ] **Step 3: Implement idempotent removal and edge decrement**

Validate `@Mode`, copy distinct input rows to a work table, capture old labels, delete group and membership rows, derive each unordered membership pair once, decrement support once, and delete zero-support edges:

```sql
IF @Mode NOT IN ('Remove', 'Add')
    THROW 50400, 'MaintainLastNCodeGroups mode must be Remove or Add.', 1;

IF @Mode = 'Remove'
BEGIN
    SELECT DISTINCT m.ResourceTypeId, m.SearchParamId, m.ResourceSurrogateId,
           m.CodeIdentityId, i.ComponentCodeIdentityId
    INTO #oldMembership
    FROM @Resources r
    JOIN dbo.LastNObservationCodeMembership m
      ON m.ResourceTypeId = r.ResourceTypeId
     AND m.SearchParamId = r.SearchParamId
     AND m.ResourceSurrogateId = r.ResourceSurrogateId
    JOIN dbo.LastNCodeIdentity i
      ON i.CodeIdentityId = m.CodeIdentityId
     AND i.ResourceTypeId = m.ResourceTypeId
     AND i.SearchParamId = m.SearchParamId;

    SELECT DISTINCT leftMember.ResourceTypeId, leftMember.SearchParamId,
           leftMember.CodeIdentityId AS LeftCodeIdentityId,
           rightMember.CodeIdentityId AS RightCodeIdentityId
    INTO #removedPairs
    FROM #oldMembership leftMember
    JOIN #oldMembership rightMember
      ON rightMember.ResourceTypeId = leftMember.ResourceTypeId
     AND rightMember.SearchParamId = leftMember.SearchParamId
     AND rightMember.ResourceSurrogateId = leftMember.ResourceSurrogateId
     AND leftMember.CodeIdentityId < rightMember.CodeIdentityId;

    UPDATE edge
       SET SupportCount = SupportCount - 1
    FROM dbo.LastNCodeEdge edge
    JOIN #removedPairs pair
      ON pair.ResourceTypeId = edge.ResourceTypeId
     AND pair.SearchParamId = edge.SearchParamId
     AND pair.LeftCodeIdentityId = edge.LeftCodeIdentityId
     AND pair.RightCodeIdentityId = edge.RightCodeIdentityId;

    DELETE FROM dbo.LastNCodeEdge WHERE SupportCount = 0;
END;
```

Reject a missing edge or a decrement below zero with `THROW 50401`; do not silently repair corrupted counts.

- [ ] **Step 4: Implement exact identity lookup, membership, edge add, and text-only groups**

Serialize the identity into `varbinary(max)` with explicit null markers and `DATALENGTH` prefixes, compute `HASHBYTES('SHA2_256', identityBytes)`, seek by scope/hash, then apply full null-safe equality under the case-sensitive column collations. Use `UPDLOCK, HOLDLOCK` for lookup/insert and retry lookup after a duplicate-key race. Insert distinct membership rows; update existing edges first, then insert absent pairs under locks. Do not use SQL `MERGE`.

For no coding membership, select one distinct `TokenText.Text COLLATE Latin1_General_100_CS_AS`; throw `50402` if multiple distinct text values exist rather than choosing arbitrarily.

- [ ] **Step 5: Implement localized component repair**

Seed affected identities from old labels, removed endpoints, new identities, and their current labels. Expand to every identity with one of those labels, initialize `#labels(CodeIdentityId, ComponentCodeIdentityId)` to self, then propagate the minimum in both edge directions until `@@ROWCOUNT = 0`:

```sql
WHILE 1 = 1
BEGIN
    UPDATE target
       SET ComponentCodeIdentityId = source.MinimumId
    FROM #labels target
    JOIN (
        SELECT endpoint.CodeIdentityId, MIN(neighbor.ComponentCodeIdentityId) AS MinimumId
        FROM #edgeEndpoints endpoint
        JOIN #labels neighbor ON neighbor.CodeIdentityId = endpoint.NeighborCodeIdentityId
        GROUP BY endpoint.CodeIdentityId
    ) source ON source.CodeIdentityId = target.CodeIdentityId
    WHERE source.MinimumId < target.ComponentCodeIdentityId;

    IF @@ROWCOUNT = 0 BREAK;
END;
```

Update identity labels, find all current coded Observations with a repaired identity, and replace each coded group with `MIN(ComponentCodeIdentityId)`. Keep orphan identities.

- [ ] **Step 6: Run graph tests green and mutation-check support counting**

Run the focused suite. Expected: all graph tests pass.

Temporarily remove `DISTINCT` from `#removedPairs`, rerun `GivenTwoObservationsSupportingOneEdge_WhenOneIsRemoved_ThenSupportRemainsOne`, and expect failure because the edge is over-decremented. Restore the procedure and rerun green.

- [ ] **Step 7: Commit**

```powershell
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/MaintainLastNCodeGroups.sql test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs
git commit -m "Maintain exact lastn code components" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 3: Add transaction-owning merge, reindex, and hard-delete wrappers

**Files:**
- Create the three `*AndMaintainLastNGroups.sql` wrapper files listed in the file map.
- Modify tests: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs`

**Interfaces:**
- Consumes: unchanged base procedure signatures and `dbo.MaintainLastNCodeGroups`.
- Produces: wrappers with the exact base parameters and outputs; no base procedure or existing TVP changes.

- [ ] **Step 1: Write failing atomicity and current-state tests**

Add tests that execute wrappers, not the primitive:

```csharp
[SkippableFact]
public async Task GivenAReplacementMerge_WhenWrapperSucceeds_ThenOldContributionIsRemovedAndNewContributionIsCurrent()
{
    await ExecuteMergeWrapperAsync(Resource("obs", 1, 1001), Tokens(1001, "a", "b"));
    await ExecuteMergeWrapperAsync(Resource("obs", 2, 1002), Tokens(1002, "c"));

    (await ReadMembershipCodesAsync(1001)).ShouldBeEmpty();
    (await ReadMembershipCodesAsync(1002)).ShouldBe(["c"]);
    (await ReadGroupCountAsync(1002)).ShouldBe(1);
}

[SkippableFact]
public async Task GivenGraphMaintenanceFails_WhenMergeWrapperRuns_ThenBaseRowsAndGroupsRollBack()
{
    await ExecuteMergeWrapperAsync(Resource("obs", 1, 1001), Tokens(1001, "a"));
    await InstallFailingMaintenanceTriggerAsync();

    Func<Task> act = () => ExecuteMergeWrapperAsync(
        Resource("obs", 2, 1002), Tokens(1002, "b"));

    await act.ShouldThrowAsync<SqlException>();
    (await ReadCurrentVersionAsync("obs")).ShouldBe(1);
    (await ReadMembershipCodesAsync(1001)).ShouldBe(["a"]);
}
```

Add reindex code change, retry idempotency, full hard delete, history-only delete, base-procedure failure, lock timeout, lexicographic multi-scope acquisition, and no externally visible partial state.

- [ ] **Step 2: Run wrapper tests and verify red**

Expected: FAIL because the wrapper procedures do not exist.

- [ ] **Step 3: Implement the merge wrapper**

Copy the exact `MergeResources` parameter list. Set `XACT_ABORT ON`, begin one transaction, derive configured scopes by joining incoming resource types to generation rows, collect previous current surrogate ids by `(ResourceTypeId, ResourceId)`, and populate a local `@AffectedLastNResources dbo.LastNResourceScopeList` table variable from those previous ids plus the incoming `@Resources` rows. Acquire ordered application locks, remove old contributions, execute unchanged `dbo.MergeResources` with every parameter, add newly current contributions, upsert both previous and new surrogate ids into dirty rows for active `Building` generations, and commit:

```sql
DECLARE @AffectedLastNResources dbo.LastNResourceScopeList;
-- Populate from previous current rows and incoming @Resources, joined to enabled generation scopes.

DECLARE scope_cursor CURSOR LOCAL FAST_FORWARD FOR
SELECT DISTINCT ResourceTypeId, SearchParamId
FROM @AffectedLastNResources
ORDER BY ResourceTypeId, SearchParamId;

EXEC @LockResult = sys.sp_getapplock
    @Resource = CONCAT('LastNCodeGroup:', @ResourceTypeId, ':', @SearchParamId),
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;
IF @LockResult < 0 THROW 50410, 'Unable to acquire LastN code-group scope lock.', 1;
```

The catch block rolls back only when `XACT_STATE() <> 0`, then rethrows. It must not call `PostMergeExtensionUpdater`.

- [ ] **Step 4: Implement reindex and hard-delete wrappers**

The reindex wrapper has the exact `UpdateResourceSearchParams` signature and performs remove/base-update/add within its outer transaction, then upserts the affected surrogate ids into the active generation's dirty rows.

The hard-delete wrapper has:

```sql
@ResourceTypeId SMALLINT,
@ResourceId VARCHAR(64),
@KeepCurrentVersion BIT,
@IsResourceChangeCaptureEnabled BIT
```

When `@KeepCurrentVersion = 0`, capture and remove the current contribution before calling `HardDeleteResource` and upsert the removed current surrogate id into the active generation's dirty rows; when it is `1`, call the base procedure without changing materialization.

- [ ] **Step 5: Prove base objects and TVPs are byte-unchanged**

Run:

```powershell
git diff --exit-code ff039f9c -- src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/MergeResources.sql src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/UpdateResourceSearchParams.sql src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/HardDeleteResource.sql
$unexpectedTypes = git diff --name-only ff039f9c -- src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Types | Where-Object { $_ -ne 'src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Types/LastNResourceScopeList.sql' }
if ($unexpectedTypes) { $unexpectedTypes; throw 'Existing TVP definitions changed.' }
```

Expected: no differences except the newly added `LastNResourceScopeList.sql`.

- [ ] **Step 6: Run wrapper tests green**

Run the focused maintenance suite. Expected: merge, retry, reindex, delete, lock, rollback, and split assertions all pass.

- [ ] **Step 7: Commit**

```powershell
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/*AndMaintainLastNGroups.sql test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs
git commit -m "Wrap resource writes with lastn maintenance" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 4: Add and register the direct SQL Server writer boundary

**Files:**
- Create: `SqlResourceIndexBatch.cs`, `SqlResourceMergeRequest.cs`, `SqlResourceReindexRequest.cs`, `ISqlResourceIndexWriter.cs`, `SqlResourceIndexWriter.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/SqlResourceIndexWriterTests.cs`

**Interfaces:**
- Consumes: existing `SqlDataRecord` TVP rows and `ISqlExecutionService.ExecuteNonQueryAsync`.
- Produces:

```csharp
public interface ISqlResourceIndexWriter
{
    Task<int> MergeAsync(int tenantId, SqlResourceMergeRequest request, CancellationToken cancellationToken);
    Task<int> ReindexAsync(int tenantId, SqlResourceReindexRequest request, CancellationToken cancellationToken);
    Task HardDeleteAsync(
        int tenantId,
        short resourceTypeId,
        string resourceId,
        bool keepCurrentVersion,
        bool isResourceChangeCaptureEnabled,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing writer command-contract tests**

Use a recording `ISqlExecutionService` and one `SqlDataRecord` per TVP. Assert `CommandType.StoredProcedure`, exact wrapper names, output parameter direction, all parameter names/types/type names, `disableRetries: true`, and cancellation propagation:

```csharp
[Fact]
public async Task GivenAMergeBatch_WhenWriterExecutes_ThenItUsesTheAtomicWrapperAndExistingTvpNames()
{
    var execution = new RecordingSqlExecutionService(outputValue: 7);
    var writer = new SqlResourceIndexWriter(execution);

    int affected = await writer.MergeAsync(
        42,
        new SqlResourceMergeRequest(
            RaiseExceptionOnConflict: true,
            IsResourceChangeCaptureEnabled: false,
            TransactionId: 9001,
            SingleTransaction: true,
            Batch: TestBatches.Complete()),
        CancellationToken.None);

    affected.ShouldBe(7);
    execution.Command.CommandText.ShouldBe("dbo.MergeResourcesAndMaintainLastNGroups");
    execution.Command.CommandType.ShouldBe(CommandType.StoredProcedure);
    execution.StructuredTypeNames.ShouldBe([
        "dbo.ResourceList",
        "dbo.ResourceWriteClaimList",
        "dbo.ReferenceSearchParamList",
        "dbo.TokenSearchParamList",
        "dbo.TokenTextList",
        "dbo.StringSearchParamList",
        "dbo.UriSearchParamList",
        "dbo.NumberSearchParamList",
        "dbo.QuantitySearchParamList",
        "dbo.DateTimeSearchParamList",
        "dbo.ReferenceTokenCompositeSearchParamList",
        "dbo.TokenTokenCompositeSearchParamList",
        "dbo.TokenDateTimeCompositeSearchParamList",
        "dbo.TokenQuantityCompositeSearchParamList",
        "dbo.TokenStringCompositeSearchParamList",
        "dbo.TokenNumberNumberCompositeSearchParamList",
    ]);
    execution.DisableRetries.ShouldBeTrue();
}
```

- [ ] **Step 2: Run tests and verify red**

Expected: compile failure because writer types do not exist.

- [ ] **Step 3: Add exact typed request records**

`SqlResourceIndexBatch` has one nullable `IReadOnlyList<SqlDataRecord>` property for each existing TVP in the order asserted above. Merge and reindex requests expose only their base-procedure flags plus the batch. Do not expose graph-table rows.

- [ ] **Step 4: Implement stored-procedure command construction**

Build structured parameters with exact `SqlDbType.Structured` and `TypeName`. Use `DBNull.Value` for an absent TVP row stream, preserve `@DateTimeSearchParms` spelling for merge and `@DateTimeSearchParams` for reindex, and read the output after execution.

Call `ExecuteNonQueryAsync(tenantId, command, cancellationToken, disableRetries: true)` because the wrapper owns a multi-step transaction and the writer cannot prove the outcome of a client timeout. Deadlock retry belongs above this boundary with a freshly generated command/request.

- [ ] **Step 5: Register direct services**

Extend `AddIgnixaSqlServerSchemaDeployment` without renaming the existing public method:

```csharp
services.AddSingleton<ISqlExecutionService, SqlExecutionService>();
services.AddSingleton<ISqlResourceIndexWriter, SqlResourceIndexWriter>();
```

Add a service-descriptor assertion that each contract resolves to exactly one singleton implementation.

- [ ] **Step 6: Run writer tests green and verify no ORM reference**

Run:

```powershell
dotnet test test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj --filter "FullyQualifiedName~SqlResourceIndexWriterTests" --framework net10.0 --no-restore
Select-String -Path src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj -Pattern 'EntityFramework|Dapper|NHibernate'
```

Expected: tests pass; `Select-String` returns no matches.

- [ ] **Step 7: Commit**

```powershell
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlResource*.cs src/DataLayer/Ignixa.DataLayer.SqlServer/ISqlResourceIndexWriter.cs src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs test/Ignixa.DataLayer.SqlServer.Tests/SqlResourceIndexWriterTests.cs
git commit -m "Add direct lastn-aware resource writer" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 5: Add resumable generation, dirty replay, and readiness transitions

**Files:**
- Create the four generation procedures and five C# generation files listed in the file map.
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupBackfillTests.cs`

**Interfaces:**
- Consumes: `dbo.MaintainLastNCodeGroups`, generation/dirty tables, `ISqlExecutionService`.
- Produces:

```csharp
public sealed record LastNCodeGroupScope(short ResourceTypeId, short SearchParamId);
public sealed record LastNCodeGroupGenerationStatus(
    long Generation,
    string State,
    long? SnapshotHighWaterSurrogateId);

public interface ILastNCodeGroupBackfillService
{
    Task EnableScopeAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        CancellationToken cancellationToken);

    Task BuildAsync(
        int tenantId,
        LastNCodeGroupScope scope,
        int batchSize,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing generation tests**

Cover idempotent enablement in `Pending`, empty scope, restart after a committed first batch, write during `Building`, stale-generation dirty rows, completion, forced failure, cancellation, and a new attempt incrementing generation:

```csharp
[SkippableFact]
public async Task GivenAWriteDuringBuilding_WhenGenerationCompletes_ThenDirtyCurrentVersionWins()
{
    LastNCodeGroupGenerationStatus generation = await StartAsync(Scope);
    await BackfillBatchAsync(generation, startId: 1, endId: 100);
    await ReplaceObservationThroughWrapperAsync(oldSid: 50, newSid: 150, code: "new");

    await CompleteAsync(generation);

    (await ReadGenerationStateAsync(Scope)).ShouldBe("Ready");
    (await ReadMembershipCodesAsync(50)).ShouldBeEmpty();
    (await ReadMembershipCodesAsync(150)).ShouldBe(["new"]);
    (await ReadDirtyCountAsync(generation.Generation)).ShouldBe(0);
}
```

- [ ] **Step 2: Run focused tests and verify red**

Expected: FAIL because generation procedures and service do not exist.

- [ ] **Step 3: Implement start and batch procedures**

`EnableLastNCodeGroupScope` uses an update-first/insert-if-absent sequence under `UPDLOCK, HOLDLOCK` to create the scope at generation `0` in `Pending`; a repeated enable preserves the current generation and state.

`StartLastNCodeGroupGeneration` requires an enabled row, acquires the scope lock, increments from the current generation, sets `Building`, captures nullable `MAX(ResourceSurrogateId)` for current non-deleted resources of the scope type, clears timestamps/failure, and returns all three status fields.

`BackfillLastNCodeGroupBatch` verifies the requested generation is still `Building`, starts a transaction, acquires the scope application lock, selects current non-deleted ids in the inclusive range and no higher than the snapshot, then invokes remove and add so replay is idempotent. Each batch commits its own transaction.

- [ ] **Step 4: Implement completion and failure procedures**

Completion acquires the scope lock, repeatedly snapshots and deletes active-generation dirty ids, runs remove/add for each snapshot until no rows remain, then performs a full-scope component repair and validates:

```sql
IF EXISTS (
    SELECT 1
    FROM dbo.LastNObservationCodeMembership m
    LEFT JOIN dbo.LastNCodeIdentity i
      ON i.CodeIdentityId = m.CodeIdentityId
     AND i.ResourceTypeId = m.ResourceTypeId
     AND i.SearchParamId = m.SearchParamId
    WHERE m.ResourceTypeId = @ResourceTypeId
      AND m.SearchParamId = @SearchParamId
      AND i.CodeIdentityId IS NULL)
    THROW 50420, 'LastN membership invariant failed.', 1;

IF EXISTS (
    SELECT membership.ResourceSurrogateId
    FROM dbo.LastNObservationCodeMembership membership
    WHERE membership.ResourceTypeId = @ResourceTypeId
      AND membership.SearchParamId = @SearchParamId
    GROUP BY membership.ResourceSurrogateId
    HAVING COUNT(DISTINCT CodeIdentityId) > 0
       AND NOT EXISTS (
           SELECT 1 FROM dbo.LastNObservationCodeGroup g
           WHERE g.ResourceTypeId = @ResourceTypeId
             AND g.SearchParamId = @SearchParamId
             AND g.ResourceSurrogateId = membership.ResourceSurrogateId))
    THROW 50421, 'LastN coded group invariant failed.', 1;
```

Only after validation, set `State = 'Ready'`, `CompletedDateTime = SYSUTCDATETIME()`, and clear failure. Failure updates only the matching active generation to `Failed` and truncates the supplied reason to 1000 characters.

- [ ] **Step 5: Implement the C# batch orchestrator**

Reject `batchSize <= 0`. `EnableScopeAsync` executes the enable procedure. `BuildAsync` requires the scope to be enabled, starts the generation, skips range execution when the nullable high-water value is absent, otherwise loops from the minimum current surrogate id through the captured high water in checked `long` ranges of `batchSize`, executes one batch at a time, then completes. On `Exception`, call the failure procedure with `CancellationToken.None` so a cancelled caller still records state, preserve the original exception, and use `"Generation cancelled."` when the exception is `OperationCanceledException`.

- [ ] **Step 6: Register and run generation tests green**

Register `ILastNCodeGroupBackfillService` as singleton. Run focused tests and expect every resume, dirty replay, and state assertion to pass.

- [ ] **Step 7: Commit**

```powershell
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/EnableLastNCodeGroupScope.sql src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/*LastNCodeGroupGeneration.sql src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/BackfillLastNCodeGroupBatch.sql src/DataLayer/Ignixa.DataLayer.SqlServer/LastNCodeGroup*.cs src/DataLayer/Ignixa.DataLayer.SqlServer/ILastNCodeGroupBackfillService.cs src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupBackfillTests.cs
git commit -m "Add resumable lastn group backfill" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 6: Migrate `LastNEmitter` and add direct unavailable behavior

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Builders/LastNEmitter.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`
- Create: `ILastNSearchExecutor.cs`, `LastNUnavailableException.cs`, `LastNSearchExecutor.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Compilation/LastNCompilationTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/LastNSearchExecutorTests.cs`

**Interfaces:**
- Consumes: `CompiledSearch`, `ResultShape.LastN`, `ISqlExecutionService`.
- Produces:

```csharp
public interface ILastNSearchExecutor
{
    Task<IReadOnlyList<TResult>> ExecuteAsync<TResult>(
        int tenantId,
        CompiledSearch compiledSearch,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Rewrite compiler expectations first**

Change tests to require:

```csharp
compiled.Sql.ShouldContain("THROW 50403, '$lastn materialization is not ready for this scope.', 1");
compiled.Sql.ShouldContain("INNER JOIN dbo.LastNObservationCodeGroup groupRow");
compiled.Sql.ShouldContain("RANK() OVER");
compiled.Sql.ShouldNotContain("#code_nodes");
compiled.Sql.ShouldNotContain("#code_edges");
compiled.Sql.ShouldNotContain("#coded_membership");
compiled.Sql.ShouldNotContain("WHILE");
compiled.Parameters.Select(parameter => parameter.Value).ShouldBe(["final", 3]);
Ast.SqlGrammar.AssertValid(compiled.Sql);
```

Replace the golden SHA only after inspecting the exact emitted SQL. Add executor tests proving `SqlException.Number == 50403` becomes `LastNUnavailableException` and other SQL exceptions retain type/stack.

- [ ] **Step 2: Run tests and verify red**

Expected: compiler assertions fail on temporary graph SQL; executor tests do not compile.

- [ ] **Step 3: Replace graph construction with readiness and indexed groups**

Retain `WriteCteHeader`, the candidate CTE, maximum parameter emission, effective-date join, rank, duplicate-id grouping, and deterministic outer ordering. Emit this readiness and group shape:

```sql
IF NOT EXISTS (
    SELECT 1
    FROM dbo.LastNCodeGroupGeneration
    WHERE ResourceTypeId = 104
      AND SearchParamId = 210
      AND State = 'Ready')
    THROW 50403, '$lastn materialization is not ready for this scope.', 1;

;WITH lastn_candidates AS (
    -- MatchPageEmitter output is inserted here unchanged.
),
groups AS (
    SELECT candidate.T1, candidate.Sid1,
           groupRow.GroupKind, groupRow.CodeGroupId, groupRow.TextCode
    FROM lastn_candidates candidate
    INNER JOIN dbo.LastNObservationCodeGroup groupRow
      ON groupRow.ResourceTypeId = candidate.T1
     AND groupRow.SearchParamId = 210
     AND groupRow.ResourceSurrogateId = candidate.Sid1
)
```

Then use the existing `effective_rows`, `ranked`, `RANK()`, final grouping, and ordering. Resource/search parameter ids are resolved catalog ids and remain deterministic SQL literals; user `maximum` remains parameterized in its current ordinal.

- [ ] **Step 4: Implement direct executor and unavailable exception**

Add the `Ignixa.Search.Sql` project reference. Validate that `compiledSearch.Query.EffectiveShape` is `ResultShape.LastN`; otherwise throw `ArgumentException`. Add each emitted parameter with an explicit type mapping (`short -> SmallInt`, `int -> Int`, `long -> BigInt`, `string -> NVarChar` with exact length, `DateTime/DateTimeOffset -> DateTime2`) rather than `AddWithValue`. Execute through `ISqlExecutionService.ExecuteReaderAsync`.

Catch only SQL error `50403`:

```csharp
catch (SqlException exception) when (exception.Number == 50403)
{
    throw new LastNUnavailableException(
        "$lastn is unavailable while Observation code groups are not ready.",
        exception);
}
```

- [ ] **Step 5: Register and run tests green**

Register `ILastNSearchExecutor` as singleton. Run compiler and direct SQL Server unit suites. Expected: deterministic SQL, grammar, parameter order, type mapping, and failure mapping all pass.

- [ ] **Step 6: Commit**

```powershell
git add src/Core/Ignixa.Search.Sql/Builders/LastNEmitter.cs src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj src/DataLayer/Ignixa.DataLayer.SqlServer/ILastNSearchExecutor.cs src/DataLayer/Ignixa.DataLayer.SqlServer/LastNUnavailableException.cs src/DataLayer/Ignixa.DataLayer.SqlServer/LastNSearchExecutor.cs src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs test/Ignixa.Search.Sql.Tests/Compilation/LastNCompilationTests.cs test/Ignixa.DataLayer.SqlServer.Tests/LastNSearchExecutorTests.cs
git commit -m "Read lastn from materialized groups" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 7: Prove concurrency, rollback, overflow, split, semantics, and performance

**Files:**
- Expand: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs`
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNMaterializedSqlSemanticsTests.cs`
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNMaterializedSqlBenchmarkTests.cs`

**Interfaces:**
- Consumes: wrapper writer behavior, generation readiness, compiled indexed query, and direct executor.
- Produces: executable acceptance evidence for all correctness and performance gates.

- [ ] **Step 1: Add the complete executable correctness matrix**

Use two independently opened `SqlConnection`s and synchronization gates for concurrency. Add named tests for competing same-scope writes (serialized), independent scopes (not mutually blocked), sorted multi-scope locks (no deadlock), forced lock timeout (whole wrapper rollback), a deadlock victim retried by a fresh writer call, and a reader never observing base rows without groups.

Add direct semantics tests for ordinary filters before grouping, authorization in the candidate CTE, coded/text groups, boundary ties, missing effective values, empty results, deterministic group ordering, current resources only, case-sensitive overflow identity, absent/non-ready generation error `50403`, and ready generation success.

- [ ] **Step 2: Run the new correctness tests before filling missing behavior**

Run:

```powershell
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastNCodeGroupMaintenanceTests|FullyQualifiedName~LastNMaterializedSqlSemanticsTests" --framework net10.0 --no-restore --logger "console;verbosity=detailed"
```

Expected: any uncovered race or invariant fails with its exact test name; no test may be skipped when `TEST_SQL_CONNECTION_STRING` is set.

- [ ] **Step 3: Fix only behavior exposed by the matrix and rerun green**

Changes stay inside the new materialization procedures, direct writer/executor, or emitter. Do not edit the three base procedures, existing TVPs, or `SqlEntityFrameworkSearchService`.

Expected: all correctness tests pass repeatedly for five consecutive runs.

- [ ] **Step 4: Port the benchmark to materialized groups**

Keep constants exactly:

```csharp
private const int ObservationCount = 10_000;
private const int CodeGroupCount = 400;
private const int WarmupCount = 5;
private const int MeasuredRunCount = 30;
private const double P95TargetMilliseconds = 100;
```

Seed the same one-to-three-coding distribution and `a -> b -> c -> d` bridges, build and mark the generation ready before timing, time only the compiled read query, capture actual plans, and assert:

```csharp
resultCount.ShouldBe(CodeGroupCount);
p95.ShouldBeLessThan(P95TargetMilliseconds);
executionPlan.Contains("SpillToTempDb", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
Regex.IsMatch(executionPlan, """SpillLevel="[1-9]""").ShouldBeFalse();
materializedMemberships.ShouldBe(19_999);
identityCount.ShouldBe(1_600);
componentCount.ShouldBe(400);
```

Print server version, compatibility level, logical CPU count, visible memory, timeout, warm-up/sample counts, P50/P95/max, and all cardinalities through `ITestOutputHelper`.

- [ ] **Step 5: Run the opt-in acceptance benchmark**

Run:

```powershell
$env:RUN_LASTN_BENCHMARK = '1'
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastNMaterializedSqlBenchmarkTests" --framework net10.0 --no-restore --logger "console;verbosity=detailed"
```

Expected: 10,000 candidates; 400 results/components; 1,600 identities; 19,999 memberships; warm P95 below 100 ms; no spill marker.

- [ ] **Step 6: Commit**

```powershell
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNCodeGroupMaintenanceTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNMaterializedSqlSemanticsTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/LastNMaterializedSqlBenchmarkTests.cs src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures src/DataLayer/Ignixa.DataLayer.SqlServer src/Core/Ignixa.Search.Sql/Builders/LastNEmitter.cs
git commit -m "Prove materialized lastn acceptance gates" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```

---

### Task 8: Remove the rejected production path and finalize documentation

**Files:**
- Delete the two legacy query-time integration test files listed in the file map.
- Modify the three READMEs, two investigations, feature readme, and Docusaurus search page listed in the file map.

**Interfaces:**
- Consumes: passing correctness matrix and recorded benchmark output from Task 7.
- Produces: no query-time graph code in production, traceable evidence, accurate package documentation, and a building documentation site.

- [ ] **Step 1: Add a rejected-path tripwire before deleting old fixtures**

Keep this compiler test:

```csharp
[Fact]
public void GivenALastNPlan_WhenCompiled_ThenNoQueryTimeGraphObjectsAreEmitted()
{
    string sql = CreateLastNPlan().Compile().Sql;

    sql.ShouldNotContain("#code_nodes");
    sql.ShouldNotContain("#code_edges");
    sql.ShouldNotContain("#coded_membership");
    sql.ShouldNotContain("#code_reach");
    sql.ShouldNotContain("WHILE");
    sql.ShouldContain("dbo.LastNObservationCodeGroup");
}
```

Run it first and expect PASS.

- [ ] **Step 2: Delete or isolate every rejected query-time fixture**

Delete `LastNSqlSemanticsTests.cs` and `LastNSqlBenchmarkTests.cs` from the EF integration project because their equivalent semantics and benchmark now execute against the materialized direct SQL Server path. Confirm the rejected latency evidence remains verbatim in `lastn-direct-search.md`; do not delete historical measurements.

Search:

```powershell
Get-ChildItem src,test -Recurse -File | Select-String -Pattern '#code_nodes|#code_edges|#coded_membership|#code_reach|LastNSqlBenchmarkTests'
```

Expected: no production match and no executable benchmark match; only intentionally quoted historical documentation may mention the removed names.

- [ ] **Step 3: Update implementation and operational documentation**

Document exact schema ownership, writer wrappers, lock name/order, generation states, restart procedure, dirty replay, unavailable behavior, and benchmark command/results. In `Ignixa.Search.Sql/README.md`, replace the query-time graph description with the ready materialized join and state that execution is provided only by `Ignixa.DataLayer.SqlServer`, never `SqlEntityFrameworkSearchService`.

In `docs/site/docs/core-sdk/search.md`, retain frontmatter and add a `$lastn` section that distinguishes compiler support from HTTP route availability:

```markdown
## Observation `$lastn`

`Ignixa.Search` models `$lastn` separately from ordinary paging, and
`Ignixa.Search.Sql` compiles it as a terminal, tie-inclusive `RANK()` shape.
SQL Server execution requires a `Ready` materialized Observation code-group
generation. A missing, pending, building, or failed generation returns an
explicit unavailable error; the compiler never falls back to query-time graph
construction.

The initial shape rejects `_sort`, `_count`, continuation tokens, `_include`,
and `_revinclude`. The server does not advertise an HTTP `$lastn` route until
the Application handler, endpoint, and capability statement are added in a
separate reviewed change.
```

- [ ] **Step 4: Finalize investigation evidence and verdict**

Change `materialized-observation-code-groups.md` to `Implemented` only if every Task 7 gate passed. Record the actual environment and P50/P95/max with no estimated values. Change its verdict to accepted for the direct SQL materialization while explicitly leaving HTTP production wiring out of scope. Keep `lastn-direct-search.md` `Rejected` and update the feature table accordingly.

- [ ] **Step 5: Run all focused, full, and documentation validation**

Run:

```powershell
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj
dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj --filter "FullyQualifiedName~LastN" --framework net10.0 --no-restore
dotnet test test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj --filter "FullyQualifiedName~LastN|FullyQualifiedName~SqlResourceIndexWriter" --framework net10.0 --no-restore
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj --filter "FullyQualifiedName~LastN" --framework net10.0 --no-restore
dotnet build All.sln
Push-Location docs/site
npm ci
npm run build
Pop-Location
```

Expected: every focused test passes, the solution builds with zero warnings/errors, and Docusaurus reports a successful production build.

- [ ] **Step 6: Inspect the final diff for prohibited coupling**

Run:

```powershell
git diff --check
git status --short
Select-String -Path src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SqlEntityFrameworkSearchService.cs -Pattern 'LastN|lastn'
Select-String -Path src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj -Pattern 'EntityFramework|Dapper|NHibernate'
```

Expected: no whitespace errors; only intended files are changed; both `rg` checks return no matches.

- [ ] **Step 7: Commit**

```powershell
git add src/Core/Ignixa.Search.Sql/README.md src/DataLayer/Ignixa.DataLayer.SqlServer/README.md src/DataLayer/Ignixa.DataLayer.SqlServer.Database/README.md docs/features/search docs/site/docs/core-sdk/search.md test/Ignixa.Search.Sql.Tests/Compilation/LastNCompilationTests.cs test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/LastNSqlSemanticsTests.cs test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/LastNSqlBenchmarkTests.cs
git commit -m "Document materialized lastn groups" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>"
```
