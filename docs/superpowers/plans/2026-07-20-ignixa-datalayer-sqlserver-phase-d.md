# Ignixa.DataLayer.SqlServer Phase D: Write-Path Migration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Before dispatching Task 1, this plan requires a Fable-model review of the plan document itself** (not code — no code exists yet), per the user's explicit instruction given this phase's production write-path blast radius. See "Process note" at the end of this document.

**Goal:** Replace `Ignixa.DataLayer.SqlEntityFramework`'s EF-Core-based `IFhirRepository` implementation with a new raw-ADO.NET implementation in `Ignixa.DataLayer.SqlServer`, built on `ISqlExecutionService`, and cut writes over to it in production.

**Architecture:** Nothing moves out of `Ignixa.DataLayer.SqlEntityFramework` — every existing class stays exactly where it is, still load-bearing for reads until Phase E. `Ignixa.DataLayer.SqlServer` gains a complete parallel implementation: copies of pure-logic components, genuine ports of EF-dependent components, and 12 hand-written `IFhirRepository` methods replacing LINQ with parameterized SQL. Cutover is a single delegate swap in `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory`, gated on a from-scratch differential-test suite proving row-level equivalence against the existing implementation.

**Tech Stack:** C# / .NET 10, `Microsoft.Data.SqlClient`, `ISqlExecutionService` (Phase A), the SSDT-deployed schema (Phase B), `SchemaDeployer` (Phase B/C), xUnit + Shouldly, real SQL Server (no mocks for the differential suite).

## Global Constraints

Exact facts every task in this plan depends on. Copied from the design doc and from direct reads of the real current code — do not re-derive, and do not trust paraphrase over what's quoted here.

**Design doc:** `docs/superpowers/specs/2026-07-20-ignixa-datalayer-sqlserver-phase-d-design.md` — read this first for the full architectural reasoning; this plan implements it.

**`ISqlExecutionService`** (`src/DataLayer/Ignixa.DataLayer.SqlServer/ISqlExecutionService.cs`, already exists, do not modify):
```csharp
Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken);
Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken);
```
Both accept a fully-built `SqlCommand` (parameters, `CommandType`, output parameters, `SqlDbType.Structured` TVP parameters all set by the caller) and assign `command.Connection` internally before executing. After `ExecuteNonQueryAsync` returns, output-parameter values are readable from the same `SqlCommand` object the caller passed in (it's the same reference).

**`IFhirRepository`** (`src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs`, do not modify — the port implements this interface unchanged):
```csharp
ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken ct = default);
ValueTask<UpdateResult> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default);
ValueTask<TransactionId> GetNextTransactionIdAsync(CancellationToken ct = default);
Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(TransactionId transactionId, IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations, CancellationToken ct = default);
ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default);
ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(TimeSpan stallThreshold, CancellationToken ct = default);
IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(ResourceKey key, HistoryQueryParameters parameters, CancellationToken ct = default);
IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(string resourceType, int tenantId, HistoryQueryParameters parameters, CancellationToken ct = default);
IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(int tenantId, HistoryQueryParameters parameters, CancellationToken ct = default);
ValueTask<ResourceKey?> DeleteAsync(ResourceKey key, ResourceRequest request, TransactionId? transactionId = null, CancellationToken ct = default);
Task<IReadOnlyList<ExpiredResourceInfo>> GetExpiredResourcesAsync(int batchSize, CancellationToken ct = default);
Task HardDeleteResourceAsync(short resourceTypeId, string resourceId, CancellationToken ct = default);
```
`DeleteAsync` doc comment cites FHIR R4 §3.1.0.7.1: logical/soft delete, new version with `IsDeleted=true`, idempotent (returns the existing deleted version, no new version, if already deleted), null return only if the resource never existed. `HardDeleteResourceAsync` doc comment: "PHYSICAL deletion, not FHIR logical deletion" (TTL expiration, GDPR). `GetStalledTransactionsAsync` doc comment: "SQL: Queries TransactionEntity table where IsCompleted = false AND HeartbeatDate is old." The 3 history methods' doc comments: "Does NOT calculate total count," "Streams results incrementally for optimal memory usage."

**Domain types** (`src/Application/Ignixa.Domain/Models/`, `src/Core/Ignixa.Abstractions/ResourceKey.cs` — all plain records, zero EF dependency, do not modify):
```csharp
public record SearchEntryResult(string ResourceType, string ResourceId, string VersionId, DateTimeOffset LastModified, ReadOnlyMemory<byte> ResourceBytes)
{
    public bool IsDeleted { get; init; }
    public int? TenantId { get; init; }
    public ResourceRequest? Request { get; init; }
    public SearchEntryMode SearchMode { get; init; } = SearchEntryMode.Match;
}
public record ResourceWrapper(string ResourceType, string ResourceId, string VersionId, DateTimeOffset LastModified, ResourceJsonNode Resource, ResourceRequest Request, bool IsDeleted = false)
{
    public string FhirVersion { get; init; } = "4.0";
    public int? TenantId { get; init; }
    public IReadOnlyList<object>? SearchIndices { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
public record UpdateResult(ResourceKey Key, ReadOnlyMemory<byte> ResourceBytes, DateTimeOffset LastModified)
{
    public ResourceRequest? Request { get; init; }
}
public readonly record struct TransactionId(long Value);
public record ExpiredResourceInfo(short ResourceTypeId, string ResourceId, DateTimeOffset ExpiresAt, string ResourceType);
public record ResourceKey(string ResourceType, string Id, string? VersionId = null, int? TenantId = null);
```

**Surrogate ID / transaction ID generation — the real, load-bearing formula** (confirmed against `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/MergeResourcesBeginTransaction.sql:32`): `transactionId = datediff_big(millisecond, '0001-01-01', sysUTCdatetime()) * 80000 + sequenceRangeFirstValue`, where `sequenceRangeFirstValue` comes from `sys.sp_sequence_get_range` against `dbo.ResourceSurrogateIdUniquifierSequence` (capped at `MAXVALUE 79999`, confirmed against `Storage/ResourceSurrogateIdUniquifierSequence.sql`). Every real resource's surrogate ID is `transactionId + entryIndex` (confirmed against `SqlMergeRepository.BuildResourceSurrogateIdMap`).

**`IdHelper.ToDate()`/`IdHelper.ToId()` (`src/Application/Ignixa.Domain/Abstractions/IdHelper.cs`) ARE the correct, deliberate mechanism for this scheme — CORRECTED from an earlier draft of this plan, which wrongly claimed these were incompatible/buggy.** Verified by direct arithmetic: `IdHelper.ShiftFactor = 3`, so `ToId` computes `ticks << 3` = `ticks × 8`; since 1 millisecond = 10,000 ticks, that is `ms × 80,000` — the *exact same* multiplier the stored procedure uses, over the same epoch (`'0001-01-01'`). `ToDate`'s `value >> 3` correctly recovers `ms × 10,000 + ⌊u/8⌋` ticks, which truncates to exactly `ms` milliseconds since the uniquifier `u` is capped at 79,999 (well under 80,000, so it never carries into the millisecond digit). **`transactionId.Value.ToDate()` is the real mechanism `SqlEntityFrameworkRepository` uses to set `resource.Resource.Meta.LastUpdated` before serialization** in both `CreateOrUpdateAsync` (`:160`) and `BatchWriteAsync` (`:482`) — baked into the compressed `RawResource` bytes themselves, not just an in-memory field. **The port must replicate this exactly** — set `Meta.LastUpdated = transactionId.Value.ToDate()` (via `IdHelper`, a shared `Ignixa.Domain` extension both the EF and the new port can call directly — it needs no porting, just a `using Ignixa.Domain.Abstractions;`) before compressing/writing a resource, in both Task 6 (`CreateOrUpdateAsync`) and Task 7 (`BatchWriteAsync`). Task 8's history methods likewise source `LastModified` via `entity.ResourceSurrogateId.ToDate()` directly (not a `Transactions.CreateDate` join — the surrogate ID is never null, unlike a tombstone row's `TransactionId`, which can be), matching legacy exactly with **no behavioral divergence and no differential-test exemption needed for this field**.

**`dbo.Resource` table shape** (EF entity `ResourceEntity`, real columns the port's raw SQL reads/writes): `ResourceTypeId SMALLINT`, `ResourceId VARCHAR(64)`, `Version INT`, `IsHistory BIT`, `ResourceSurrogateId BIGINT` (PK), `IsDeleted BIT`, `RequestMethod VARCHAR(10) NULL`, `RawResource VARBINARY(MAX)`, `IsRawResourceMetaSet BIT`, `SearchParamHash VARCHAR(64) NULL`, `TransactionId BIGINT NULL` (FK → `dbo.Transactions.SurrogateIdRangeFirstValue`), `HistoryTransactionId BIGINT NULL` (same FK).

**`dbo.Transactions` table shape**: PK `SurrogateIdRangeFirstValue BIGINT`, `SurrogateIdRangeLastValue BIGINT`, `Definition VARCHAR(2000) NULL`, `IsCompleted BIT`, `IsSuccess BIT`, `IsVisible BIT`, `IsHistoryMoved BIT`, `CreateDate DATETIMEOFFSET`, `EndDate/VisibleDate/HistoryMovedDate DATETIMEOFFSET NULL`, `HeartbeatDate DATETIMEOFFSET`, `FailureReason VARCHAR(MAX) NULL`, `IsControlledByClient BIT DEFAULT 1`, `InvisibleHistoryRemovedDate DATETIMEOFFSET NULL`.

**`dbo.ResourceTtl` table shape**: PK `(ResourceTypeId SMALLINT, ResourceId VARCHAR(64))`, `ExpiresAt DATETIMEOFFSET`, `TransactionId BIGINT NULL` (no FK constraint).

**`dbo.ResourceType` table shape**: PK `ResourceTypeId SMALLINT IDENTITY`, `Name VARCHAR(50)`.

**The 15 search-index tables** (exact names, from `SqlEntityFrameworkRepository.cs`'s `SearchIndexTables` static array — every one keyed by `ResourceSurrogateId`, deleted wholesale on tombstone/hard-delete): `ReferenceSearchParam`, `TokenSearchParam`, `TokenText`, `StringSearchParam`, `UriSearchParam`, `NumberSearchParam`, `QuantitySearchParam`, `DateTimeSearchParam`, `ReferenceTokenCompositeSearchParam`, `TokenTokenCompositeSearchParam`, `TokenDateTimeCompositeSearchParam`, `TokenQuantityCompositeSearchParam`, `TokenStringCompositeSearchParam`, `TokenNumberNumberCompositeSearchParam`, `ResourceWriteClaim`.

**`SqlMergeRepository`'s real error mapping** (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlMergeRepository.cs:344-348`, must be replicated exactly in the ported merge mechanism):
```csharp
catch (SqlException ex) when (ex.Number == 50409)
{
    throw new PreconditionFailedException("Resource was recently updated. Please refresh and retry.");
}
```

**`SchemaDeployer`** (`src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs`, already exists, Phase B/C): `DeployIfEmptyAsync(int tenantId, CancellationToken)` bootstraps an empty tenant database from the embedded dacpac. The differential-test harness (Task 5) uses this directly against two throwaway databases — do not hand-write DDL or reference the old `DatabaseInitializer`/EF-migrations path, which no longer exists (deleted in Phase B).

**`ITenantConfigurationStore`/tenant config**: differential and integration tests in this plan follow the exact pattern established in `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs` and `test/Ignixa.SchemaUpgrade.Cli.Tests/RunAsyncDataLossTests.cs` (Phase C) for spinning up a real throwaway tenant database against `TEST_SQL_CONNECTION_STRING` — read those files for the exact fake `ITenantConfigurationStore`/cleanup pattern before writing Task 5.

**Environment**: local SQL Server 2025 at `Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` (Docker unavailable this session). `MSYS_NO_PATHCONV=1` prefix only needed if shelling out to `sqlpackage` directly — most of this plan's code calls `DacServices`/`ISqlExecutionService` in-process and won't need it.

**Namespacing convention for this plan's new code**: everything new lives under `Ignixa.DataLayer.SqlServer` (the existing project from Phases A-C), in new subfolders mirroring the EF project's structure where it aids readability (`RowGenerators/`, `Indexing/`, etc.) — exact folder layout is each task's own decision, guided by "one clear responsibility per file" (writing-plans convention), not dictated wholesale here.

---

### Task 1: Copy pure-logic components — `GzipResourceCompressor` and the `RowGenerators` folder

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Compression/GzipResourceCompressor.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ISearchParameterRowGenerator.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/{DateTimeSearchParameterRowGenerator,NumberSearchParameterRowGenerator,QuantityCodeRowGenerator,QuantitySearchParameterRowGenerator,ReferenceSearchParameterRowGenerator,RefTokenCompositeRowGenerator,StringSearchParameterRowGenerator,TokenDateTimeCompositeRowGenerator,TokenNumberNumberCompositeRowGenerator,TokenQuantityCompositeRowGenerator,TokenSearchParameterRowGenerator,TokenStringCompositeRowGenerator,TokenTextRowGenerator,TokenTokenCompositeRowGenerator,UriSearchParameterRowGenerator}.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ResourceRowGenerator.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/ResourceWriteClaimRowGenerator.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/SearchParameterIdLookupHelper.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/RowGenerators/RowGeneratorsCompilationTests.cs`

**Interfaces:**
- Consumes: nothing from this plan (first task).
- Produces: `Ignixa.DataLayer.SqlServer.Compression.GzipResourceCompressor`, `Ignixa.DataLayer.SqlServer.RowGenerators.ISearchParameterRowGenerator` and all 19 concrete classes — Task 4 (merge mechanism port) and Task 6+ (repository) construct and call these directly, with the exact same public API as their EF-project originals (confirmed identical by this task's own tests, §below).

This task is a **verbatim copy with a namespace change only** — do not alter any logic. Both `GzipResourceCompressor` and every file under `RowGenerators/` are confirmed zero-EF-dependency (grepped for `FhirDbContext|_context\.|DbContext|EntityFrameworkCore` across the whole `RowGenerators/` folder: zero matches; `GzipResourceCompressor.cs` read in full: only `System.IO.Compression`/`Ignixa.Serialization`/`Microsoft.IO` references).

- [ ] **Step 1: Copy `GzipResourceCompressor.cs`**

Read `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Compression/GzipResourceCompressor.cs` in full. Copy it verbatim to `src/DataLayer/Ignixa.DataLayer.SqlServer/Compression/GzipResourceCompressor.cs`, changing only the `namespace` line from `Ignixa.DataLayer.SqlEntityFramework.Compression` to `Ignixa.DataLayer.SqlServer.Compression`. The class is a primary-constructor `GzipResourceCompressor(RecyclableMemoryStreamManager memoryStreamManager)` with two methods: `byte[] SerializeAndCompress(ResourceJsonNode node)` and `ReadOnlyMemory<byte> DecompressBytes(ReadOnlyMemory<byte> compressedData)`. No other changes.

- [ ] **Step 2: Copy every file under `RowGenerators/`**

Read each of the 19 files listed above under `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/RowGenerators/` in full. Copy each verbatim to the corresponding path under `src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators/`, changing only the `namespace` line from `Ignixa.DataLayer.SqlEntityFramework.RowGenerators` to `Ignixa.DataLayer.SqlServer.RowGenerators` and updating any `using Ignixa.DataLayer.SqlEntityFramework.RowGenerators;` references between these files to `using Ignixa.DataLayer.SqlServer.RowGenerators;`. Do not change any method bodies, signatures, or logic.

Note the two structural outliers, confirmed by direct read (not just grep) — copy them exactly as-is, do not force them into `ISearchParameterRowGenerator`'s shape:
- `ResourceRowGenerator` — constructor `(GzipResourceCompressor compressor, ILogger<ResourceRowGenerator>? logger = null)`, its own `GenerateSqlDataRecords` overload (different signature from the interface).
- `ResourceWriteClaimRowGenerator` — no constructor parameters, its own `GenerateSqlDataRecords` signature.
- `SearchParameterIdLookupHelper` — a `public static class` helper, not a generator.
- `TokenSearchParameterRowGenerator` and `UriSearchParameterRowGenerator` additionally define `ExtractExtensionData(...)` methods returning `TokenSearchParamExtensionData`/`UriSearchParamExtensionData` records (defined in those same files) — these feed Task 3's `PostMergeExtensionUpdater` port directly. Confirm both extension-data record types come along with the copy (they're defined in the same source files, not separate).

- [ ] **Step 3: Write a compilation/shape-parity test**

```csharp
using System.Reflection;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests.RowGenerators;

public class RowGeneratorsCompilationTests
{
    [Fact]
    public void GivenTheCopiedRowGeneratorsAssembly_WhenListingISearchParameterRowGeneratorImplementers_ThenAllFourteenArePresent()
    {
        var expectedTypeNames = new[]
        {
            "DateTimeSearchParameterRowGenerator", "NumberSearchParameterRowGenerator", "QuantityCodeRowGenerator",
            "QuantitySearchParameterRowGenerator", "ReferenceSearchParameterRowGenerator", "RefTokenCompositeRowGenerator",
            "StringSearchParameterRowGenerator", "TokenDateTimeCompositeRowGenerator", "TokenNumberNumberCompositeRowGenerator",
            "TokenQuantityCompositeRowGenerator", "TokenSearchParameterRowGenerator", "TokenStringCompositeRowGenerator",
            "TokenTextRowGenerator", "TokenTokenCompositeRowGenerator", "UriSearchParameterRowGenerator"
        };

        var assembly = typeof(ISearchParameterRowGenerator).Assembly;
        var implementers = assembly.GetTypes()
            .Where(t => typeof(ISearchParameterRowGenerator).IsAssignableFrom(t) && !t.IsInterface)
            .Select(t => t.Name)
            .ToList();

        foreach (var expected in expectedTypeNames)
        {
            implementers.ShouldContain(expected);
        }
        implementers.Count.ShouldBe(15); // 15 files implement the interface (QuantityCodeRowGenerator + the 14 named above)
    }

    [Fact]
    public void GivenResourceRowGenerator_WhenConstructedWithACompressor_ThenSucceeds()
    {
        var compressor = new Compression.GzipResourceCompressor(new Microsoft.IO.RecyclableMemoryStreamManager());
        var generator = new ResourceRowGenerator(compressor);
        generator.ShouldNotBeNull();
    }

    [Fact]
    public void GivenResourceWriteClaimRowGenerator_WhenConstructedWithNoArguments_ThenSucceeds()
    {
        var generator = new ResourceWriteClaimRowGenerator();
        generator.ShouldNotBeNull();
    }
}
```

- [ ] **Step 4: Run the test**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~RowGeneratorsCompilationTests`
Expected: 3/3 PASS.

- [ ] **Step 5: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 Warning(s), 0 Error(s).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Compression src/DataLayer/Ignixa.DataLayer.SqlServer/RowGenerators test/Ignixa.DataLayer.SqlServer.Tests/RowGenerators
git commit -m "feat(datalayer-sqlserver): copy GzipResourceCompressor and RowGenerators for the write-path port"
```

---

### Task 2: Port `SearchIndexReferenceDataCache` to ADO.NET

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing/SqlServerSearchIndexReferenceDataCache.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing/SqlServerSearchIndexReferenceDataCacheTests.cs`

**Interfaces:**
- Consumes: `ISqlExecutionService` (Global Constraints), `SchemaDeployer.DeployIfEmptyAsync` for test setup.
- Produces: `Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexReferenceDataCache`, with this exact public surface (Task 4's merge port and Task 6+'s repository both depend on these exact names/types):
```csharp
public class SqlServerSearchIndexReferenceDataCache(ISqlExecutionService sqlExecutionService, int tenantId, ILogger<SqlServerSearchIndexReferenceDataCache> logger)
{
    public IReadOnlyDictionary<string, short> ResourceTypeMappings { get; }
    public IReadOnlyDictionary<string, short> SearchParameterMappings { get; }
    public IReadOnlyDictionary<string, int> SystemMappings { get; }
    public IReadOnlyDictionary<string, int> QuantityCodeMappings { get; }
    public Task PreloadResourceTypesAsync(CancellationToken cancellationToken);
    public Task PreloadSearchParamsAsync(int? maxRows, CancellationToken cancellationToken);
    public Task<short?> GetResourceTypeIdAsync(string? resourceTypeName, CancellationToken cancellationToken);
    public Task<short?> GetSearchParamIdAsync(string uri, CancellationToken cancellationToken);
    public Task<int> GetOrCreateSystemIdAsync(string? systemUri, CancellationToken cancellationToken);
    public Task<int> GetOrCreateQuantityCodeIdAsync(string? code, CancellationToken cancellationToken);
    public short? TryGetResourceTypeIdFromCache(string? resourceTypeName);
    public short? TryGetSearchParamIdFromCache(string? uri);
    public void CacheResourceTypeId(string resourceTypeName, short resourceTypeId);
}
```

This is the largest genuine port in the mechanical/foundation group of this plan. The real `SearchIndexReferenceDataCache` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Indexing/SearchIndexReferenceDataCache.cs`, 888 lines) has 17 public members total; **this task ports only the 8 members the write path actually calls** (confirmed by tracing `SqlMergeRepository`'s and `RowGenerators`' real usage — they read the 4 dictionary properties and call `GetOrCreateSystemIdAsync`/`GetOrCreateQuantityCodeIdAsync`/`GetResourceTypeIdAsync`/`GetSearchParamIdAsync`/the two preload methods; nothing in the write path calls `SyncSearchParametersToDatabase`, `GetStatistics`, the `SearchParameterInfo`-overload of `GetSearchParamIdAsync`, or `GetValidResourceTypeMappings`/`GetValidSearchParameterMappings` — those are read-path-only members Phase E's own cache port will need, not this one). If a later task in this plan discovers a write-path call site needing one of the un-ported members, add it then with a real test — do not port unused surface speculatively (YAGNI).

**Read-only lookups (no on-demand creation), the exact SQL to write:**
```sql
-- ResourceType, by name
SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = @Name;
-- ResourceType, preload all
SELECT ResourceTypeId, Name FROM dbo.ResourceType;
-- SearchParam, by URI
SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = @Uri;
-- SearchParam, preload (maxRows nullable -> conditionally append TOP)
SELECT [TOP (@MaxRows)] SearchParamId, Uri FROM dbo.SearchParam;
```
Cache miss on `GetResourceTypeIdAsync`/`GetSearchParamIdAsync` returns `null` (not an exception) and caches a sentinel so repeated lookups for a genuinely-missing name don't re-query — mirror the EF version's `ConcurrentDictionary<string, short>` cache with a `-1` sentinel value, translating "cached as -1" back to `null` at the public API boundary (the public methods above return `short?`, not the raw `-1` sentinel).

**On-demand-creation lookups, exact SQL and ID-retrieval mechanism:**
```sql
-- System: check-then-insert (no unique-constraint retry in the original either -- confirmed, see Global Constraints note below)
SELECT SystemId FROM dbo.System WHERE Value = @Value;
-- if not found:
INSERT INTO dbo.System (Value) OUTPUT INSERTED.SystemId VALUES (@Value);

-- QuantityCode: identical pattern against dbo.QuantityCode -- note the column is Value, NOT Code
-- (confirmed against Tables/QuantityCode.sql; using "Code" here would fail at runtime)
SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = @Value;
INSERT INTO dbo.QuantityCode (Value) OUTPUT INSERTED.QuantityCodeId VALUES (@Value);
```
Both `dbo.System.Value` and `dbo.QuantityCode.Value` are `NVARCHAR` — bind the parameter as `SqlDbType.NVarChar`, not `VarChar`.

Use `OUTPUT INSERTED.<IdColumn>` and `ExecuteReaderAsync` (not `ExecuteNonQueryAsync`) for the insert, reading the generated ID back from the single result row — this replaces EF's "read the ID off the tracked entity post-`SaveChangesAsync`" mechanism with the ADO.NET-idiomatic equivalent.

**The 4 dictionary properties must be live views over the backing `ConcurrentDictionary`s, not snapshots** (e.g. return the `ConcurrentDictionary<string, int>` itself typed as `IReadOnlyDictionary<string, int>`, do not copy it into a new `Dictionary` at property-get time) — `SqlServerMergeRepository` (Task 4) captures these references once at its own construction and must see every subsequent on-demand insert immediately, matching the original `SearchIndexReferenceDataCache`'s same live-reference behavior (confirmed: `SqlMergeRepository`'s constructor captures `referenceDataCache.SystemMappings`/`.QuantityCodeMappings` once and relies on them staying live). Sentinel values (the internal "confirmed missing" marker) must never leak through these public dictionary properties — filter them out, matching the original's behavior.

**A second public method is needed for Task 6's resource-type-creation helper to avoid a double-insert bug**: `void CacheResourceTypeId(string resourceTypeName, short resourceTypeId)` — a direct, unconditional `ConcurrentDictionary` set with no DB access and no sentinel interaction. This exists because `GetResourceTypeIdAsync`'s cache-miss path caches a `-1` "confirmed missing" sentinel; if a caller (Task 6's `GetOrCreateResourceTypeIdAsync`) used `GetResourceTypeIdAsync` to check-then-insert, the sentinel would still be cached after a successful insert, causing every subsequent lookup for that same name to return "not found" and re-insert (duplicate row / unique-constraint violation). `CacheResourceTypeId` lets a caller that already knows it just inserted a real row record that fact directly, bypassing the sentinel entirely. **No unique-constraint catch/retry** — confirmed the original `SearchIndexReferenceDataCache` has none either (relies on its own in-process `SemaphoreSlim` lock for single-process safety; a true concurrent-insert race across processes is an existing, unaddressed gap this port does not need to fix). Match that: no catch/retry logic here either.

**Concurrency**: use a single `SemaphoreSlim(1, 1)` field gating every DB-touching operation (mirrors the original's `_dbLock` — "DbContext is not thread-safe" doesn't literally apply to `ISqlExecutionService` since it opens a fresh connection per call, but the double-check-locking pattern around the 4 `ConcurrentDictionary` caches is still the right shape to avoid duplicate concurrent inserts within one process). Check cache → if miss, acquire the semaphore → double-check cache under the lock → query/insert → release.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

public class SqlServerSearchIndexReferenceDataCacheTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAKnownResourceType_WhenGetResourceTypeIdAsyncCalled_ThenReturnsItsRealId()
    {
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);
        var id = await _cache.GetResourceTypeIdAsync("Patient", CancellationToken.None);
        id.ShouldNotBeNull();
        _cache.ResourceTypeMappings["Patient"].ShouldBe(id!.Value);
    }

    [Fact]
    public async Task GivenAnUnknownResourceTypeName_WhenGetResourceTypeIdAsyncCalledTwice_ThenReturnsNullBothTimesAndOnlyQueriesOnce()
    {
        var first = await _cache.GetResourceTypeIdAsync("NotARealResourceType", CancellationToken.None);
        var second = await _cache.GetResourceTypeIdAsync("NotARealResourceType", CancellationToken.None);
        first.ShouldBeNull();
        second.ShouldBeNull();
        _cache.TryGetResourceTypeIdFromCache("NotARealResourceType").ShouldBeNull();
    }

    [Fact]
    public async Task GivenANewSystemUri_WhenGetOrCreateSystemIdAsyncCalled_ThenInsertsAndReturnsAGeneratedId()
    {
        var systemUri = $"http://example.org/test-system-{Guid.NewGuid()}";
        var id = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);
        id.ShouldBeGreaterThan(0);
        _cache.SystemMappings[systemUri].ShouldBe(id);

        var rowCount = await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.System WHERE SystemId = {id}");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnExistingSystemUri_WhenGetOrCreateSystemIdAsyncCalledTwice_ThenReturnsTheSameIdBothTimesAndInsertsOnce()
    {
        var systemUri = $"http://example.org/test-system-{Guid.NewGuid()}";
        var firstId = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);
        var secondId = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);
        secondId.ShouldBe(firstId);

        var rowCount = await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.System WHERE Value = '{systemUri}'");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenANewQuantityCode_WhenGetOrCreateQuantityCodeIdAsyncCalled_ThenInsertsAndReturnsAGeneratedId()
    {
        var code = $"test-code-{Guid.NewGuid():N}";
        var id = await _cache.GetOrCreateQuantityCodeIdAsync(code, CancellationToken.None);
        id.ShouldBeGreaterThan(0);
        _cache.QuantityCodeMappings[code].ShouldBe(id);
    }

    [Fact]
    public void GivenCacheResourceTypeIdCalledDirectly_WhenTryGetResourceTypeIdFromCacheCalled_ThenReturnsTheCachedValueWithoutAnyDbAccess()
    {
        _cache.CacheResourceTypeId("SyntheticType", 999);
        _cache.TryGetResourceTypeIdFromCache("SyntheticType").ShouldBe((short)999);
    }

    [Fact]
    public async Task GivenASystemIdInsertedThroughOneCacheInstance_WhenAnotherCacheInstanceReadsSystemMappings_ThenTheLiveDictionaryReflectsItAfterAFreshLookup()
    {
        // Proves SystemMappings is a live view, not a point-in-time snapshot -- a second logical
        // "reader" (here, a second GetOrCreateSystemIdAsync call reusing the same cache instance,
        // which is how SqlServerMergeRepository's captured dictionary reference actually behaves)
        // sees the insert without needing to re-fetch the property.
        var systemUri = $"http://example.org/live-view-{Guid.NewGuid()}";
        var mappingsReference = _cache.SystemMappings;
        mappingsReference.ContainsKey(systemUri).ShouldBeFalse();

        await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);

        mappingsReference.ContainsKey(systemUri).ShouldBeTrue();
    }
}
```

`TestTenantDatabase` (a new small test-fixture helper) does not exist yet — create it as part of this step at `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/TestTenantDatabase.cs`, following the exact real fake-`ITenantConfigurationStore`/cleanup pattern in `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs` (read that file first): a static `CreateEmptyAsync()` factory that creates a uniquely-named scratch database, deploys it via `SchemaDeployer.DeployIfEmptyAsync`, and exposes `TenantId`, `SqlExecutionService` (a real `SqlExecutionService` wired to a single-tenant fake store pointing at the scratch database), and `ExecuteScalarAsync<T>(string sql)` (a thin raw-ADO.NET helper for test assertions) plus `IAsyncLifetime`-compatible `DisposeAsync()` that drops the database. This fixture is reused by every later integration test in this plan (Tasks 3, 4, 6-10) — build it once, correctly, here.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheTests` (with `TEST_SQL_CONNECTION_STRING` set per Global Constraints)
Expected: FAIL with "SqlServerSearchIndexReferenceDataCache does not exist" / "TestTenantDatabase does not exist" (compile error).

- [ ] **Step 3: Implement `TestTenantDatabase` and `SqlServerSearchIndexReferenceDataCache`**

Implement per the SQL and concurrency design above. Every public method builds a `SqlCommand`, calls `ISqlExecutionService.ExecuteReaderAsync`/`ExecuteNonQueryAsync` with the real `tenantId` field, and updates the appropriate `ConcurrentDictionary` cache under the semaphore.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SqlServerSearchIndexReferenceDataCacheTests`
Expected: 7/7 PASS.

- [ ] **Step 5: Full solution build + unit suite**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests` → all passing (no regressions from Task 1).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Indexing test/Ignixa.DataLayer.SqlServer.IntegrationTests/Indexing test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures
git commit -m "feat(datalayer-sqlserver): port SearchIndexReferenceDataCache's write-path surface to ADO.NET"
```

---

### Task 3: Port `PostMergeExtensionUpdater`

**Note on task order (corrected during plan review):** this task now runs BEFORE the TVP merge mechanism port (Task 4), reversing the original draft's order — Task 4's `MergeResourcesAsync` port genuinely depends on this task's output (§below, and see Task 4's own note), not the other way around.

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerPostMergeExtensionUpdater.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerPostMergeExtensionUpdaterTests.cs`

**Interfaces:**
- Consumes: `ISqlExecutionService`, `TokenSearchParamExtensionData`/`UriSearchParamExtensionData` (from Task 1's copied `TokenSearchParameterRowGenerator.cs`/`UriSearchParameterRowGenerator.cs`), `TestTenantDatabase` (Task 2).
- Produces: `Ignixa.DataLayer.SqlServer.SqlServerPostMergeExtensionUpdater`, consumed directly by Task 4's `SqlServerMergeRepository`:
```csharp
public class SqlServerPostMergeExtensionUpdater(ISqlExecutionService sqlExecutionService, int tenantId, ILogger<SqlServerPostMergeExtensionUpdater> logger)
{
    public Task UpdateTokenSearchParamExtensionsAsync(IEnumerable<TokenSearchParamExtensionData> extensions, CancellationToken cancellationToken);
    public Task UpdateUriSearchParamExtensionsAsync(IEnumerable<UriSearchParamExtensionData> extensions, CancellationToken cancellationToken);
    public Task UpdateAllExtensionsAsync(IEnumerable<TokenSearchParamExtensionData> tokenExtensions, IEnumerable<UriSearchParamExtensionData> uriExtensions, CancellationToken cancellationToken);
}
```

Mechanical retarget, confirmed already raw SQL. Read `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/PostMergeExtensionUpdater.cs` in full and port its `BatchSize = 100` chunking logic verbatim. The exact SQL text (confirmed real, quote verbatim per row in the batch with numbered parameter suffixes):
```sql
UPDATE dbo.TokenSearchParam
SET IdentifierTypeSystemId = @IdentifierTypeSystemId{i},
    IdentifierTypeCode = @IdentifierTypeCode{i}
WHERE ResourceTypeId = @ResourceTypeId{i}
  AND ResourceSurrogateId = @ResourceSurrogateId{i}
  AND SearchParamId = @SearchParamId{i}
  AND ((@SystemId{i} IS NULL AND SystemId IS NULL) OR SystemId = @SystemId{i})
  AND Code = @Code{i};
```
```sql
UPDATE dbo.UriSearchParam
SET Version = @Version{i},
    Fragment = @Fragment{i}
WHERE ResourceTypeId = @ResourceTypeId{i}
  AND ResourceSurrogateId = @ResourceSurrogateId{i}
  AND SearchParamId = @SearchParamId{i}
  AND Uri = @Uri{i};
```
Build one `StringBuilder` of N such statements + a matching `List<SqlParameter>` per `Chunk(BatchSize)` batch, then one `SqlCommand`/`ExecuteNonQueryAsync` call per batch (replacing the EF version's per-batch `ExecuteSqlRawAsync` call).

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerPostMergeExtensionUpdaterTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerPostMergeExtensionUpdater _updater = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _updater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAnEmptyExtensionList_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenNoOpsWithoutError()
    {
        await Should.NotThrowAsync(() =>
            _updater.UpdateTokenSearchParamExtensionsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task GivenAPreExistingTokenSearchParamRow_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenTheExtensionColumnsAreSet()
    {
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, 1000, 1, NULL, 'test-code')");

        var extension = new TokenSearchParamExtensionData(
            ResourceTypeId: 1, ResourceSurrogateId: 1000, SearchParamId: 1, SystemId: null, Code: "test-code",
            IdentifierTypeSystemId: 42, IdentifierTypeCode: "MR");

        await _updater.UpdateTokenSearchParamExtensionsAsync([extension], CancellationToken.None);

        var identifierTypeCode = await _database.ExecuteScalarAsync<string>(
            "SELECT IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = 1000");
        identifierTypeCode.ShouldBe("MR");
    }
}
```

Confirm `TokenSearchParamExtensionData`'s exact real property names/order by reading its definition in the copied `TokenSearchParameterRowGenerator.cs` (Task 1) before finalizing this test's constructor call — adjust the test above to match whatever the real record shape is if it differs from this sketch.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SqlServerPostMergeExtensionUpdaterTests`
Expected: FAIL (compile error).

- [ ] **Step 3: Implement `SqlServerPostMergeExtensionUpdater`** per the SQL/batching design above.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SqlServerPostMergeExtensionUpdaterTests`
Expected: 2/2 PASS.

- [ ] **Step 5: Full solution build + regression**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerPostMergeExtensionUpdater.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerPostMergeExtensionUpdaterTests.cs
git commit -m "feat(datalayer-sqlserver): port PostMergeExtensionUpdater's extension-column updates to ISqlExecutionService"
```

---

### Task 4: Port the TVP merge/transaction mechanism

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryTests.cs`

**Interfaces:**
- Consumes: `Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexReferenceDataCache` (Task 2), `Ignixa.DataLayer.SqlServer.Compression.GzipResourceCompressor` (Task 1), all 19 `RowGenerators` (Task 1), **`Ignixa.DataLayer.SqlServer.SqlServerPostMergeExtensionUpdater` (Task 3 — corrected during plan review; the real `SqlMergeRepository.MergeResourcesAsync` calls the extension updater internally, so this port must too, not defer it to a later caller)**, `ISqlExecutionService` (Global Constraints), `TestTenantDatabase` (Task 2).
- Produces: `Ignixa.DataLayer.SqlServer.SqlServerMergeRepository` with this exact public surface (Task 6+'s repository calls these directly):
```csharp
public class SqlServerMergeRepository(
    ISqlExecutionService sqlExecutionService, int tenantId, GzipResourceCompressor compressor,
    SqlServerSearchIndexReferenceDataCache referenceDataCache, SqlServerPostMergeExtensionUpdater extensionUpdater,
    ILogger<SqlServerMergeRepository> logger)
{
    public Task<(long TransactionId, int SequenceStart)> BeginTransactionAsync(int resourceCount, CancellationToken cancellationToken = default);
    public Task<int> MergeResourcesAsync(long transactionId, bool singleTransaction, IReadOnlyList<ResourceWrapper> resources, IReadOnlyList<int> entryIndices, CancellationToken cancellationToken = default);
    public Task CommitTransactionAsync(long transactionId, string? failureReason = null, CancellationToken cancellationToken = default);
    public Task PutTransactionHeartbeatAsync(long transactionId, CancellationToken cancellationToken = default);
}
```

This is the roadmap's original narrow scope, genuinely mechanical: read `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlMergeRepository.cs` in full (already re-confirmed once this session — every write is `_context.Database.ExecuteSqlRawAsync("EXEC dbo.XXX ...", parameters, cancellationToken)`). Port each of the 4 public methods by replacing that call with: build a `SqlCommand` (`CommandText = "EXEC dbo.XXX @Param1, @Param2 OUTPUT, ..."`, `CommandType = CommandType.Text`, matching the exact EXEC text and parameter list already in the EF version verbatim — same stored procedure names, same parameter names/types/TVP type names), then `await sqlExecutionService.ExecuteNonQueryAsync(tenantId, command, cancellationToken)`, then read output-parameter values off the same `command.Parameters` collection. Replicate `MergeResourcesAsync`'s `catch (SqlException ex) when (ex.Number == 50409) => throw new PreconditionFailedException(...)` exactly (Global Constraints).

Replace every `_referenceDataCache.ResourceTypeMappings`/`.SearchParameterMappings`/`.SystemMappings`/`.QuantityCodeMappings` reference with the Task 2 cache's identically-named properties — the `RowGenerators` calls are otherwise unchanged (same method signatures, same dictionaries passed in). `BuildResourceSurrogateIdMap` and `MaterializeIfNotEmpty` (private static helpers in the original) port verbatim, no logic change — quote them directly from the source file.

**`MergeResourcesAsync` must also port the real method's tail end, not just the SP call** (confirmed against `SqlMergeRepository.cs:324-328,358-371` — this was missed in an earlier draft of this plan): before the SP call, extract extension data via `_referenceRowGenerators`' `ExtractExtensionData(...)` calls (`TokenSearchParameterRowGenerator`/`UriSearchParameterRowGenerator`, both copied in Task 1) into `List<TokenSearchParamExtensionData>`/`List<UriSearchParamExtensionData>`; after the SP call succeeds (and after reading `@AffectedRows`), if either list is non-empty, call `extensionUpdater.UpdateAllExtensionsAsync(tokenExtensions, uriExtensions, cancellationToken)`. Without this, the ported merge path silently never writes `IdentifierTypeSystemId`/`IdentifierTypeCode`/`Version`/`Fragment` — a real production regression for `:of-type`/`:above`/`:below` search modifiers (CLAUDE.md's own documented `PostMergeExtensionUpdater` pattern).

**Also port the original's guard clauses** (all present in the real file, not optional): `MergeResourcesAsync` returns `0` immediately if `resources` is null or empty; validates `entryIndices.Count == resources.Count`, throwing the same `ArgumentException` (quote its exact message from the source); auto-preloads the reference-data cache (`PreloadResourceTypesAsync`/`PreloadSearchParamsAsync(maxRows: 10000)`) if the corresponding mapping dictionary is empty, matching the original's lazy-preload-on-first-use behavior exactly.

**Retry safety note** (no action needed, stated for the implementer's awareness): `ISqlExecutionService`'s Polly retry pipeline (Global Constraints) re-executes the whole `ExecuteNonQueryAsync` call on a transient error (deadlock 1205, timeout -2). `MergeResources` is safe to retry by design (idempotent within one transaction ID) — this is pre-existing Phase A behavior the merge path inherits unchanged; do not add any additional idempotency guard here.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerMergeRepositoryTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerMergeRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        var cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.PreloadResourceTypesAsync(CancellationToken.None);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        _repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, compressor, cache, extensionUpdater, NullLogger<SqlServerMergeRepository>.Instance);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenASingleResource_WhenMergedThroughBeginMergeCommit_ThenARowExistsInDboResource()
    {
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);

        var resourceJson = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"test-patient-1"}""");
        var wrapper = new ResourceWrapper("Patient", "test-patient-1", "1", DateTimeOffset.UtcNow, resourceJson, new ResourceRequest("PUT"));

        var affectedRows = await _repository.MergeResourcesAsync(
            transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        affectedRows.ShouldBeGreaterThan(0);
        var rowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'test-patient-1'");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAHeartbeatCall_WhenPutTransactionHeartbeatAsyncCalled_ThenTheTransactionsHeartbeatDateAdvances()
    {
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        var before = await _database.ExecuteScalarAsync<DateTimeOffset>(
            $"SELECT HeartbeatDate FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = {transactionId}");

        await Task.Delay(50);
        await _repository.PutTransactionHeartbeatAsync(transactionId, CancellationToken.None);

        var after = await _database.ExecuteScalarAsync<DateTimeOffset>(
            $"SELECT HeartbeatDate FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = {transactionId}");
        after.ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task GivenAResourceWithATokenIdentifierSearchIndex_WhenMergedAndTheSearchIndexIncludesAnIdentifierType_ThenTheExtensionColumnsAreWritten()
    {
        // Proves the extension-updater wiring (this task's own correction, per the plan review) is
        // real, not just present in the constructor -- IdentifierTypeSystemId/IdentifierTypeCode
        // are NOT writable through the TVP itself (CLAUDE.md's PostMergeExtensionUpdater pattern),
        // only via this post-merge call.
        var (transactionId, _) = await _repository.BeginTransactionAsync(resourceCount: 1, CancellationToken.None);
        var resourceJson = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"test-patient-identifier","identifier":[{"system":"http://example.org/mrn","value":"12345","type":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v2-0203","code":"MR"}]}}]}""");
        var wrapper = new ResourceWrapper("Patient", "test-patient-identifier", "1", DateTimeOffset.UtcNow, resourceJson, new ResourceRequest("PUT"))
        {
            // Read RowGenerators/TokenSearchParameterRowGenerator.cs (Task 1) for the exact real
            // shape a caller must populate here to produce a token search index with an
            // IdentifierType -- this sketch may need adjusting to that real input contract.
            SearchIndices = [] // TODO for the implementer: populate the real token search index object(s) this generator expects
        };

        await _repository.MergeResourcesAsync(transactionId, singleTransaction: true, [wrapper], [0], CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);

        var identifierTypeCode = await _database.ExecuteScalarAsync<string?>(
            "SELECT TOP (1) IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId >= @TransactionId".Replace("@TransactionId", transactionId.ToString()));
        identifierTypeCode.ShouldBe("MR");
    }
}
```

This third test has one deliberate, flagged gap: it needs the real shape of a token search-index entry that carries an `IdentifierType` (the exact object `TokenSearchParameterRowGenerator.GenerateSqlDataRecords`/`ExtractExtensionData` expect in `resource.SearchIndices`) — read `RowGenerators/TokenSearchParameterRowGenerator.cs` (Task 1) directly during implementation and replace the `SearchIndices = []` placeholder with a real populated value; do not skip this test or leave it passing vacuously against an empty index list.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SqlServerMergeRepositoryTests`
Expected: FAIL (compile error, `SqlServerMergeRepository` does not exist).

- [ ] **Step 3: Implement `SqlServerMergeRepository`** per the porting instructions above, including the extension-updater call and the guard clauses.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SqlServerMergeRepositoryTests`
Expected: 3/3 PASS.

- [ ] **Step 5: Full solution build + regression**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerMergeRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerMergeRepositoryTests.cs
git commit -m "feat(datalayer-sqlserver): port the TVP merge/transaction mechanism, including its post-merge extension-column update call"
```

---

### Task 5: Build the differential-test harness

**Files:**
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/DifferentialTestHarness.cs`
- Create: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/RowStateSnapshot.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/DifferentialTestHarnessTests.cs`

**Interfaces:**
- Consumes: `SchemaDeployer`, `TestTenantDatabase` (Task 2), `IFhirRepository` (Global Constraints, both implementations satisfy this — the EF-based `SqlEntityFrameworkRepository` and, once it exists, the new port).
- Produces: `Ignixa.DataLayer.SqlServer.IntegrationTests.Differential.DifferentialTestHarness`, used by Tasks 6-9's differential test cases and Task 10's comprehensive pass. Exact API:
```csharp
public class DifferentialTestHarness : IAsyncDisposable
{
    public static Task<DifferentialTestHarness> CreateAsync(CancellationToken cancellationToken);
    public IFhirRepository LegacyRepository { get; }   // real Ignixa.DataLayer.SqlEntityFramework.SqlEntityFrameworkRepository, wired to database A
    public IFhirRepository NewRepository { get; }        // the new port, wired to database B -- set once Task 6 exists; null/throws before then
    public SqlMergeRepository LegacyMergeRepository { get; }   // the same instance LegacyRepository was constructed with -- for tests (Task 10) that need to force a SQL-level condition (e.g. error 50409) deterministically, bypassing IFhirRepository's own client-side version lookup
    public SqlServerMergeRepository NewMergeRepository { get; } // same, for the new port -- set once Task 6 exists
    public Task<RowStateSnapshot> SnapshotLegacyAsync(string tableName, string whereClause, CancellationToken cancellationToken); // dumps a table's rows from database A ONLY
    public Task<RowStateSnapshot> SnapshotNewAsync(string tableName, string whereClause, CancellationToken cancellationToken);    // dumps a table's rows from database B ONLY
    public void AssertEquivalent(RowStateSnapshot legacy, RowStateSnapshot @new, params string[] ignoredColumns); // column-by-column row comparison, ignoredColumns for genuinely nondeterministic identifier/timestamp columns only (never for a documented behavioral divergence -- Task 8 has none, per Global Constraints)
}
```

**Corrected during plan review — this is a genuine, load-bearing fix, not a naming preference.** An earlier draft of this task specified a single `SnapshotAsync(tableName, whereClause, ct)` method whose doc claimed it "dumps a table's rows from BOTH databases," returning one `RowStateSnapshot`. That is not implementable as a stateless single-database-selector-free call — every consuming test would have called it twice with IDENTICAL arguments and had no way to actually target database A vs database B, meaning `AssertEquivalent` would silently compare a dataset to itself and **pass on every real divergence, including a genuinely broken port**. The two-method split above (`SnapshotLegacyAsync`/`SnapshotNewAsync`, explicit about which database each call targets) is the fix. Every test snippet in Tasks 6-10 below has been updated to call the two methods explicitly (`SnapshotLegacyAsync` for `LegacyRepository`'s database, `SnapshotNewAsync` for `NewRepository`'s database) — do not reintroduce the ambiguous single-method shape.

This is real test infrastructure, not a throwaway helper — it is used by every remaining task in this plan. Build it BEFORE any repository-method task needs it (this ordering is deliberate and load-bearing for the Fable plan review, per the process note at the end of this document).

`SnapshotLegacyAsync`/`SnapshotNewAsync` each execute `SELECT * FROM {tableName} WHERE {whereClause}` against their own single, specific database via raw ADO.NET (`SqlDataReader`, generic column-name/value dictionary per row — do not hardcode a column list, since it must work across all 15 search-index tables plus `dbo.Resource`/`dbo.Transactions`/`dbo.ResourceTtl`), returning a `RowStateSnapshot` (`record` wrapping `IReadOnlyList<IReadOnlyDictionary<string, object?>>` rows). **Normalize values before sorting/comparing** — `object.Equals`/default ordering do not work correctly for every SQL column type read back via `SqlDataReader.GetValue`: convert `byte[]` (e.g. `RawResource`) to a hex string for both sorting and equality (raw `byte[]` reference-equality via `object.Equals` is always false even for identical content), normalize `DBNull.Value` to CLR `null` uniformly, and use a stable, deterministic sort key built from the normalized column values (e.g. concatenate every normalized column value as a string, in a fixed column order) — SQL doesn't guarantee row order without `ORDER BY`, and TVP-based bulk inserts don't guarantee insertion order either, so an unsorted comparison would spuriously fail on correct data just because two result sets came back in different orders.

`AssertEquivalent` compares row COUNT first (fail fast with a clear message showing counts on each side), then compares each row's non-ignored columns for exact equality (using the same normalization rules above), producing a Shouldly-style failure message naming the specific table/column/row-index/legacy-value/new-value on any mismatch — a bare `snapshot1.ShouldBe(snapshot2)` is not acceptable here, since a raw dictionary-equality failure gives no diagnostic value for a differential test whose entire purpose is pinpointing behavioral drift. **`ignoredColumns` is a narrow escape hatch for genuinely nondeterministic identifier/timestamp columns that legitimately differ between two independently-provisioned databases (e.g. `ResourceSurrogateId`, `TransactionId`, `SurrogateIdRangeFirstValue`) — it is not a general-purpose "make the test pass" lever.** Every call site across this plan that passes `ignoredColumns` must have a code comment justifying why that specific column is expected to differ; do not add a column to an ignore list to silence a real, unexplained mismatch.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class DifferentialTestHarnessTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTwoFreshlyDeployedDatabases_WhenSnapshottingDboResourceType_ThenBothAreIdentical()
    {
        // dbo.ResourceType is seeded identically by the dacpac's post-deployment script on both
        // sides -- a genuine zero-diff baseline proving the harness itself works before any real
        // resource data is written by either repository.
        var legacy = await _harness.SnapshotLegacyAsync("dbo.ResourceType", "1=1", CancellationToken.None);
        var @new = await _harness.SnapshotNewAsync("dbo.ResourceType", "1=1", CancellationToken.None);

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new));
    }

    [Fact]
    public async Task GivenARealDivergenceInsertedDirectlyIntoOnlyOneDatabase_WhenSnapshottingBothAndAsserting_ThenAssertEquivalentThrows()
    {
        // The canary this harness was missing before this fix: proves AssertEquivalent actually
        // DETECTS a real difference, not just that it doesn't false-positive on identical data.
        // Without this test, a broken SnapshotLegacyAsync/SnapshotNewAsync split (e.g. both
        // accidentally reading the same physical database) would still pass every other test here.
        await _harness.InsertIntoNewDatabaseOnlyForTestingAsync(
            "INSERT INTO dbo.ResourceType (Name) VALUES ('CanaryOnlyOnNewSide')", CancellationToken.None);

        var legacy = await _harness.SnapshotLegacyAsync("dbo.ResourceType", "Name = 'CanaryOnlyOnNewSide'", CancellationToken.None);
        var @new = await _harness.SnapshotNewAsync("dbo.ResourceType", "Name = 'CanaryOnlyOnNewSide'", CancellationToken.None);

        Should.Throw<ShouldAssertException>(() => _harness.AssertEquivalent(legacy, @new));
    }

    [Fact]
    public void GivenTwoSnapshotsWithDifferingRowCounts_WhenAssertEquivalentCalled_ThenThrowsWithACountMismatchMessage()
    {
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1 }]);
        var @new = new RowStateSnapshot([]);

        var exception = Should.Throw<ShouldAssertException>(() => _harness.AssertEquivalent(legacy, @new));
        exception.Message.ShouldContain("row count");
    }

    [Fact]
    public void GivenTwoSnapshotsDifferingOnlyInAnIgnoredColumn_WhenAssertEquivalentCalledWithThatColumnIgnored_ThenDoesNotThrow()
    {
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["ResourceSurrogateId"] = 1000L } ]);
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["ResourceSurrogateId"] = 2000L } ]);

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new, "ResourceSurrogateId"));
    }

    [Fact]
    public void GivenTwoSnapshotsWithIdenticalByteArrayColumns_WhenAssertEquivalentCalled_ThenDoesNotThrow()
    {
        // Proves the byte[] normalization (hex-string comparison, not reference equality) works --
        // without it, two snapshots holding equal-content-but-different-instance byte[] values
        // (the real shape RawResource takes) would spuriously fail every comparison.
        var legacy = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["RawResource"] = new byte[] { 1, 2, 3 } } ]);
        var @new = new RowStateSnapshot([new Dictionary<string, object?> { ["Id"] = 1, ["RawResource"] = new byte[] { 1, 2, 3 } } ]);

        Should.NotThrow(() => _harness.AssertEquivalent(legacy, @new));
    }
}
```

`InsertIntoNewDatabaseOnlyForTestingAsync(string sql, CancellationToken)` is a small test-only escape hatch on `DifferentialTestHarness` for the canary test above — executes raw SQL directly against database B only, with no equivalent on database A. Add it to the class's public API alongside the members already listed.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~DifferentialTestHarnessTests`
Expected: FAIL (compile error, `DifferentialTestHarness`/`RowStateSnapshot` do not exist).

- [ ] **Step 3: Implement `RowStateSnapshot` and `DifferentialTestHarness`**

`CreateAsync` provisions two throwaway tenant databases via `TestTenantDatabase.CreateEmptyAsync()` (Task 2), constructs a real `Ignixa.DataLayer.SqlEntityFramework.SqlEntityFrameworkRepository` against database A for `LegacyRepository` (read that class's real constructor signature from `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepository.cs` — it needs a `FhirDbContext` pointed at database A's connection string, `GzipResourceCompressor`, a `SqlMergeRepository`, `SearchIndexReferenceDataCache`, and a logger; wire these directly, matching how `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory`'s `createRepository` delegate does it) — **keep a reference to the `SqlMergeRepository` instance you just constructed and expose it as `LegacyMergeRepository`**, not just wrapped inside `LegacyRepository`. `NewRepository`/`NewMergeRepository` are `null`/throw `NotImplementedException` with a clear message until Task 6 lands (Task 6's brief already covers wiring both when it constructs `NewRepository`) — this task only needs `LegacyRepository`/`LegacyMergeRepository` and the snapshot/compare mechanism to exist and be provably correct (including genuinely detecting a real divergence, per the canary test above) before any repository-method task depends on it.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~DifferentialTestHarnessTests`
Expected: 5/5 PASS.

- [ ] **Step 5: Full solution build + regression**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 6: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential
git commit -m "test(datalayer-sqlserver): build the row-level differential-test harness, with explicit per-database snapshot methods and a divergence-detection canary"
```

---

### Task 6: The new repository — single-resource CRUD (`GetAsync`, `CreateOrUpdateAsync`, `DeleteAsync`, `GetNextTransactionIdAsync`, `CommitTransactionAsync`)

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs`
- Modify: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/DifferentialTestHarness.cs` (wire up `NewRepository`)
- Modify: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/TestTenantDatabase.cs` (add `CreateSqlServerFhirRepositoryAsync()` + `Repository` property, §below — Tasks 7-9 reuse this same addition, built once here)
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryCrudTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/SingleResourceCrudDifferentialTests.cs`

**Interfaces:**
- Consumes: `SqlServerMergeRepository` (Task 4), `SqlServerSearchIndexReferenceDataCache` (Task 2), `GzipResourceCompressor` (Task 1), `ISqlExecutionService`, `DifferentialTestHarness` (Task 5).
- Produces: `Ignixa.DataLayer.SqlServer.SqlServerFhirRepository`, implementing `IFhirRepository`. This task implements 5 of its 12 methods plus 2 shared private helpers; Tasks 7-9 add the rest to the same class (partial-class or straight incremental addition — the plan does not mandate `partial`, use your judgment on file size, matching writing-plans' "if a file grows unwieldy, split it" guidance, but keep it one class/one interface for the DI wiring in Task 11 to swap cleanly).

**`GetOrCreateResourceTypeIdAsync` (private helper, replaces the EF version's ad-hoc pattern with a direct call into Task 2's cache):**

**Corrected during plan review** — an earlier draft routed the insert path through `_cache.GetResourceTypeIdAsync` (Task 2's read-only lookup, which caches a `-1` "confirmed missing" sentinel on a miss) and never updated the cache after inserting, so every subsequent call for the same new type name would see the stale sentinel, conclude "still missing," and attempt to insert AGAIN — a duplicate row / unique-constraint violation on the second caller. Use `CacheResourceTypeId` (Task 2, added for exactly this) to record the freshly-inserted ID directly, bypassing the sentinel path entirely:
```csharp
private async Task<short> GetOrCreateResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
{
    var cached = _cache.TryGetResourceTypeIdFromCache(resourceType);
    if (cached.HasValue) return cached.Value;

    var id = await _cache.GetResourceTypeIdAsync(resourceType, cancellationToken);
    if (id.HasValue) return id.Value;

    var command = new SqlCommand(
        "INSERT INTO dbo.ResourceType (Name) OUTPUT INSERTED.ResourceTypeId VALUES (@Name)");
    command.Parameters.AddWithValue("@Name", resourceType);
    var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt16(0), cancellationToken);
    var newId = results[0];
    _cache.CacheResourceTypeId(resourceType, newId);
    return newId;
}
```
Add a test proving the second call for the same never-before-seen type name returns the same ID without a second INSERT (query `dbo.ResourceType`'s row count for that name before/after both calls, assert it stays 1) — this is the regression proof for the bug this correction fixes.

**`GetNextSurrogateIdAsync` (private helper, exact formula from Global Constraints, must match bit-for-bit):**
```csharp
private async Task<long> GetNextSurrogateIdAsync(CancellationToken cancellationToken)
{
    var command = new SqlCommand("SELECT NEXT VALUE FOR dbo.ResourceSurrogateIdUniquifierSequence");
    var results = await _sqlExecutionService.ExecuteReaderAsync(_tenantId, command, reader => reader.GetInt32(0), cancellationToken);
    var sequenceValue = results[0];
    return (long)(DateTimeOffset.UtcNow - DateTimeOffset.MinValue).TotalMilliseconds * 80000 + sequenceValue;
}
```

**`GetAsync`** — port `SqlEntityFrameworkRepository.cs:67-127`'s two query shapes as raw SQL joined to `dbo.Transactions` for `LastModified`:
```sql
-- with a specific VersionId
SELECT r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId AND r.Version = @Version;

-- current version (no VersionId given)
SELECT TOP (1) r.ResourceId, r.Version, r.RawResource, r.IsDeleted, r.RequestMethod, t.CreateDate
FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId AND r.IsHistory = 0
ORDER BY r.Version DESC;
```
`LastModified = t.CreateDate ?? DateTimeOffset.UtcNow` (null-coalesce exactly like the original, for the edge case where `TransactionId` is null). Return `null` if no row found. Do NOT filter `IsDeleted` — return deleted resources with `IsDeleted=true` set on the result (exact comment from the original, preserved: the API layer maps this to 410 Gone vs 404).

**`CreateOrUpdateAsync`** — port `:130-202`: allocate a transaction via `GetNextTransactionIdAsync()`, look up current version (`SELECT TOP(1) Version FROM dbo.Resource WHERE ResourceTypeId=@t AND ResourceId=@id AND IsHistory=0 ORDER BY ResourceSurrogateId DESC`), `newVersion = current?.Version + 1 ?? 1`. Set `resource.Resource.Meta.VersionId = newVersion.ToString()` and, **using `Ignixa.Domain.Abstractions.IdHelper`'s real `ToDate()` extension (confirmed correct — Global Constraints — not banned)**, `resource.Resource.Meta.LastUpdated = transactionId.Value.ToDate()` — this must happen BEFORE the resource is handed to `_mergeRepository.MergeResourcesAsync`, since the merge path compresses `resource.Resource` into `RawResource` and the timestamp needs to be baked into those bytes, exactly matching legacy's `SqlEntityFrameworkRepository.cs:160`. Then delegate to `_mergeRepository.MergeResourcesAsync`/`CommitTransactionAsync` (single-element array, `singleTransaction: true`), then call the private `UpsertResourceTtlAsync` helper (Task 6 implements this helper now since `CreateOrUpdateAsync` needs it; `DeleteAsync` below reuses it).

**`UpsertResourceTtlAsync` (private helper):**
```sql
-- expiresAt has a value: find-or-update-or-insert
MERGE dbo.ResourceTtl AS target
USING (SELECT @ResourceTypeId AS ResourceTypeId, @ResourceId AS ResourceId) AS source
ON target.ResourceTypeId = source.ResourceTypeId AND target.ResourceId = source.ResourceId
WHEN MATCHED THEN UPDATE SET ExpiresAt = @ExpiresAt, TransactionId = @TransactionId
WHEN NOT MATCHED THEN INSERT (ResourceTypeId, ResourceId, ExpiresAt, TransactionId) VALUES (@ResourceTypeId, @ResourceId, @ExpiresAt, @TransactionId);

-- expiresAt is null: delete if present
DELETE FROM dbo.ResourceTtl WHERE ResourceTypeId = @ResourceTypeId AND ResourceId = @ResourceId;
```

**`DeleteAsync`** — **rewritten during plan review to match the real code's actual semantics; an earlier draft of this plan described this method incorrectly (it does NOT allocate a transaction via `GetNextTransactionIdAsync`, and there is NO `CommitTransactionAsync` call anywhere in it).** Read `SqlEntityFrameworkRepository.cs:205-316` directly during implementation to confirm every detail below — this is the plan's own re-derivation, not a substitute for reading the real source. Look up current non-history version; `null` if not found; if already `IsDeleted`, return the existing `ResourceKey` unchanged (no new version written); otherwise:
1. `newVersion = current.Version + 1`.
2. Mark the current row `IsHistory=1`, `HistoryTransactionId=@HistoryTransactionId` — `UPDATE dbo.Resource SET IsHistory=1, HistoryTransactionId=@HistoryTransactionId WHERE ResourceSurrogateId=@ResourceSurrogateId`, where `@HistoryTransactionId = transactionId?.Value` (the method's own optional `transactionId` PARAMETER, nullable — NOT a value obtained by calling `GetNextTransactionIdAsync()`; if the caller didn't pass one in, this is a real SQL `NULL`).
3. Build a minimal tombstone JSON (`{"resourceType":..., "id":..., "meta":{"versionId":..., "lastUpdated":...}}`), compress it via `GzipResourceCompressor`.
4. Get a new surrogate ID via `GetNextSurrogateIdAsync()` (this helper — a raw `NEXT VALUE FOR` sequence call plus the `*80000` formula, Global Constraints — is unrelated to transaction allocation and is still needed here).
5. Insert the new deleted row, with the full real column list (an earlier draft of this plan omitted two columns legacy sets — `IsRawResourceMetaSet=1` and `SearchParamHash=NULL`, confirmed at `SqlEntityFrameworkRepository.cs:287-288`):
   ```sql
   INSERT INTO dbo.Resource
       (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId)
   VALUES
       (@ResourceTypeId, @ResourceId, @NewVersion, 0, @NewSurrogateId, 1, 'DELETE', @TombstoneBytes, 1, NULL, @TransactionId);
   ```
   where `@TransactionId` is the SAME `transactionId?.Value` (nullable) as step 2 — a tombstone written by a standalone `DeleteAsync` call (no caller-supplied `transactionId`) has a NULL `TransactionId` on its row, matching legacy exactly.
6. `UpsertResourceTtlAsync(resourceTypeId, resourceId, expiresAt: null, ...)` — clears any TTL.
7. `DeleteSearchIndexEntriesAsync(currentEntity.ResourceSurrogateId, ...)` — wipes all 15 search-index tables (Global Constraints) for the OLD surrogate ID: `DELETE FROM {table} WHERE ResourceSurrogateId = @ResourceSurrogateId`, one statement per table, can be one `SqlCommand` with a multi-statement `CommandText` or 15 sequential calls — implementer's choice, but must delete from all 15, not a subset.

**No explicit SQL transaction wraps steps 2-7, and there is no step 8 "commit" call of any kind.** This matches CLAUDE.md's own documented architecture for this codebase ("Transaction Model & MergeResources Pattern": application-level transaction visibility via `dbo.Transactions`, not ACID SQL-transaction boundaries — the same philosophy `PostMergeExtensionUpdater`'s already-existing, already-accepted non-transactional multi-statement batches follow). If a partial failure between steps 2 and 7 is a real operational concern, that is a pre-existing property of the legacy implementation being faithfully replicated here, not a new risk this port introduces — out of scope to fix in this task.

**`GetNextTransactionIdAsync`** — pure delegation: `var (id, _) = await _mergeRepository.BeginTransactionAsync(1000, cancellationToken); return new TransactionId(id);` (the `1000` matches the original's hardcoded `resourceCount` argument exactly — quote-verify against `:319-334` before implementing).

**`CommitTransactionAsync`** — pure delegation: `await _mergeRepository.CommitTransactionAsync(transactionId.Value, failureReason: null, cancellationToken);`.

- [ ] **Step 1: Write the failing integration tests** (golden-shape, not yet differential)

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerFhirRepositoryCrudTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync(); // helper added in this task, wires all the Task 1-4 pieces together
        _repository = _database.Repository;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenANewResource_WhenCreateOrUpdateAsyncCalled_ThenGetAsyncReturnsItWithVersion1()
    {
        var resource = BuildTestPatientWrapper("patient-crud-1");
        var result = await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        result.Key.VersionId.ShouldBe("1");

        var fetched = await _repository.GetAsync(new ResourceKey("Patient", "patient-crud-1"), CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.VersionId.ShouldBe("1");
        fetched.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnExistingResource_WhenCreateOrUpdateAsyncCalledAgain_ThenVersionIncrementsToTwo()
    {
        var resource = BuildTestPatientWrapper("patient-crud-2");
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        var second = await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        second.Key.VersionId.ShouldBe("2");
    }

    [Fact]
    public async Task GivenAnExistingResource_WhenDeleteAsyncCalled_ThenGetAsyncReturnsATombstoneWithIsDeletedTrue()
    {
        var resource = BuildTestPatientWrapper("patient-crud-3");
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var deletedKey = await _repository.DeleteAsync(
            new ResourceKey("Patient", "patient-crud-3"), new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);

        deletedKey.ShouldNotBeNull();
        var fetched = await _repository.GetAsync(new ResourceKey("Patient", "patient-crud-3"), CancellationToken.None);
        fetched!.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAnAlreadyDeletedResource_WhenDeleteAsyncCalledAgain_ThenReturnsTheSameKeyWithoutANewVersion()
    {
        var resource = BuildTestPatientWrapper("patient-crud-4");
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        var key = new ResourceKey("Patient", "patient-crud-4");
        var firstDelete = await _repository.DeleteAsync(key, new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);
        var secondDelete = await _repository.DeleteAsync(key, new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);

        secondDelete!.VersionId.ShouldBe(firstDelete!.VersionId);
    }

    [Fact]
    public async Task GivenAResourceThatNeverExisted_WhenDeleteAsyncCalled_ThenReturnsNull()
    {
        var result = await _repository.DeleteAsync(
            new ResourceKey("Patient", "never-existed"), new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenTwoCallsToGetNextTransactionIdAsync_WhenBothReturn_ThenTheyAreDifferentValues()
    {
        var first = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var second = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        first.ShouldNotBe(second);
    }

    private static ResourceWrapper BuildTestPatientWrapper(string id) =>
        new("Patient", id, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
            new ResourceRequest("PUT"));
}
```

- [ ] **Step 2: Write the failing differential tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class SingleResourceCrudDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTheSameResourceCreatedThroughBothRepositories_WhenSnapshottingDboResource_ThenRowsAreEquivalent()
    {
        var resource = new ResourceWrapper("Patient", "diff-crud-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-crud-1"}"""), new ResourceRequest("PUT"));

        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var legacySnapshot = await _harness.SnapshotLegacyAsync(
            "dbo.Resource", "ResourceId = 'diff-crud-1'", CancellationToken.None);
        var newSnapshot = await _harness.SnapshotNewAsync(
            "dbo.Resource", "ResourceId = 'diff-crud-1'", CancellationToken.None);

        // ResourceSurrogateId and TransactionId legitimately differ between the two databases
        // (independently allocated sequences/clocks) -- everything else must match exactly.
        _harness.AssertEquivalent(legacySnapshot, newSnapshot, "ResourceSurrogateId", "TransactionId", "HistoryTransactionId");
    }

    [Fact]
    public async Task GivenTheSameResourceDeletedThroughBothRepositories_WhenSnapshottingAllFifteenSearchIndexTables_ThenAllAreEmptyOnBothSides()
    {
        var resource = new ResourceWrapper("Patient", "diff-crud-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-crud-2"}"""), new ResourceRequest("PUT"));
        var key = new ResourceKey("Patient", "diff-crud-2");

        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.LegacyRepository.DeleteAsync(key, new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);
        await _harness.NewRepository.DeleteAsync(key, new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);

        string[] searchIndexTables =
        [
            "ReferenceSearchParam", "TokenSearchParam", "TokenText", "StringSearchParam", "UriSearchParam",
            "NumberSearchParam", "QuantitySearchParam", "DateTimeSearchParam", "ReferenceTokenCompositeSearchParam",
            "TokenTokenCompositeSearchParam", "TokenDateTimeCompositeSearchParam", "TokenQuantityCompositeSearchParam",
            "TokenStringCompositeSearchParam", "TokenNumberNumberCompositeSearchParam", "ResourceWriteClaim"
        ];

        foreach (var table in searchIndexTables)
        {
            var legacySnapshot = await _harness.SnapshotLegacyAsync($"dbo.{table}", "1=1", CancellationToken.None);
            var newSnapshot = await _harness.SnapshotNewAsync($"dbo.{table}", "1=1", CancellationToken.None);
            _harness.AssertEquivalent(legacySnapshot, newSnapshot);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryCrudTests|FullyQualifiedName~SingleResourceCrudDifferentialTests"`
Expected: FAIL (compile error, `SqlServerFhirRepository` does not exist, `DifferentialTestHarness.NewRepository` throws `NotImplementedException`).

- [ ] **Step 4: Extend `TestTenantDatabase` with a `SqlServerFhirRepository`-wiring factory**

Add to `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/TestTenantDatabase.cs`. **Note the construction order: `SqlServerPostMergeExtensionUpdater` is built BEFORE `SqlServerMergeRepository` and passed into it (Task 4's constructor requires it — this snippet corrects an earlier draft that built the merge repository with the old, pre-Task-3/4-swap 5-argument constructor, which no longer compiles against Task 4's real signature).**
```csharp
public static async Task<TestTenantDatabase> CreateSqlServerFhirRepositoryAsync()
{
    var database = await CreateEmptyAsync();
    var cache = new SqlServerSearchIndexReferenceDataCache(
        database.SqlExecutionService, database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
    await cache.PreloadResourceTypesAsync(CancellationToken.None);
    var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
    var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
        database.SqlExecutionService, database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
    var mergeRepository = new SqlServerMergeRepository(
        database.SqlExecutionService, database.TenantId, compressor, cache, extensionUpdater, NullLogger<SqlServerMergeRepository>.Instance);
    database.Repository = new SqlServerFhirRepository(
        database.SqlExecutionService, database.TenantId, compressor, cache, mergeRepository,
        NullLogger<SqlServerFhirRepository>.Instance);
    return database;
}

public SqlServerFhirRepository Repository { get; private set; } = null!;
```
This is the single place all of Tasks 6-9's tests get a fully-wired `SqlServerFhirRepository` from — built once here, reused by every later task's test `InitializeAsync()` via the same `TestTenantDatabase.CreateSqlServerFhirRepositoryAsync()` call. Adjust `SqlServerFhirRepository`'s real constructor parameter list to match whatever you actually implement in Step 4 below — the shape above is illustrative of what it needs (all of Tasks 1-4's components plus `ISqlExecutionService`/`tenantId`), not a mandated exact signature.

- [ ] **Step 5: Implement `SqlServerFhirRepository`'s CRUD methods** per the design above, and wire `DifferentialTestHarness.NewRepository`/`NewMergeRepository` (Task 5's stubs) to a real, fully-constructed pair against database B (reuse the same `TestTenantDatabase.CreateSqlServerFhirRepositoryAsync()` wiring from Step 4, not a separate hand-rolled construction — expose the `SqlServerMergeRepository` instance that wiring already builds internally as `NewMergeRepository`, the same way Task 5 exposes `LegacyMergeRepository`).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryCrudTests|FullyQualifiedName~SingleResourceCrudDifferentialTests"`
Expected: 8/8 PASS.

- [ ] **Step 7: Full solution build + regression**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryCrudTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/TestTenantDatabase.cs
git commit -m "feat(datalayer-sqlserver): port GetAsync/CreateOrUpdateAsync/DeleteAsync/GetNextTransactionIdAsync/CommitTransactionAsync to raw ADO.NET"
```

---

### Task 7: The new repository — batch/transaction lifecycle (`BatchWriteAsync`, `GetStalledTransactionsAsync`)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryBatchTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/BatchWriteDifferentialTests.cs`

**Interfaces:**
- Consumes: everything Task 6 produced, same class.
- Produces: `BatchWriteAsync`/`GetStalledTransactionsAsync` added to `SqlServerFhirRepository`.

**`BatchWriteAsync`** — the most logic-dense method in this plan, port `SqlEntityFrameworkRepository.cs:337-631` faithfully, including its two real pre-flight validation checks (Global Constraints doesn't repeat these — quoting the exact rule here since it's this task's core requirement):
1. **Resource-type resolution**: for each distinct `resourceType` in the batch, check `SqlServerSearchIndexReferenceDataCache.TryGetResourceTypeIdFromCache` first; for any cache miss, batch-query `SELECT ResourceTypeId, Name FROM dbo.ResourceType WHERE Name IN (...)` for all misses in ONE query (not one query per miss); for any still-missing after that, insert via the `GetOrCreateResourceTypeIdAsync` helper (Task 6).
2. **Existing-resource lookup, chunked at exactly 100 items per batch** (the original's comment explains this chunk size works around EF's expression-tree compiler stack-overflowing on large `IN` lists — that specific constraint doesn't apply to hand-written SQL, but **keep the 100-item chunking anyway**, since SQL Server's own parameter-count limits and query-plan size make very large `IN`/parameterized-list clauses a real, separate concern — do not remove the chunking, just note the original reason no longer applies verbatim): `SELECT ResourceTypeId, ResourceId, Version, ResourceSurrogateId FROM dbo.Resource WHERE IsHistory = 0 AND (ResourceTypeId, ResourceId) IN (...)` (SQL Server doesn't support tuple `IN` directly — use a `VALUES` table-value constructor joined against, or an OR-chain per 100-item chunk; implementer's choice, but must be a single round-trip per 100-item chunk, not N round-trips), grouped client-side into `(TypeId, ResourceId) -> (MaxVersion, MaxSurrogateId)`.
3. **The exact validation logic to replicate** (quoted from the original, comment: "replicates the stored procedure's validation check to catch issues early with better error messages"): for each operation, `newSurrogateId = transactionId.Value + entryIndex`; if an existing row was found for that `(resourceType, resourceId)`:
   - if `newVersion <= existing.MaxVersion` → `throw new InvalidOperationException("Version constraint violation")` (quote the exact original message text once you read it directly from `:337-631` — reproduce verbatim, do not paraphrase).
   - if `newSurrogateId <= existing.MaxSurrogateId` → `throw new ResourceVersionConflictException(resourceType, resourceId, newSurrogateId, existing.MaxSurrogateId)` (confirm this exception type's real constructor signature in `src/Application/Ignixa.Domain/Exceptions/` before using it).
4. **Before delegating to the merge repository, construct each operation's `ResourceWrapper`** — this enumeration is illustrative, not exhaustive; `SqlEntityFrameworkRepository.cs:337-631` is the authoritative source, read it directly during implementation. At minimum: `newVersion = (existing?.MaxVersion ?? 0) + 1`, set `Meta.VersionId = newVersion.ToString()` and `Meta.LastUpdated = transactionId.Value.ToDate()` (same `IdHelper` mechanism as `CreateOrUpdateAsync`, Global Constraints — this is row-state-visible in `RawResource`, an earlier draft of this task omitted it), build `Request = new ResourceRequest(operation.httpMethod, $"{operation.resourceType}/{operation.resourceId}")`. Delegate the final write to `_mergeRepository.MergeResourcesAsync` — same mechanism as `CreateOrUpdateAsync`. Do NOT commit internally (matches the original: commit happens later via a separate `CommitTransactionAsync` call the caller makes). Return the ordered list of `ResourceKey`s (one per input operation, same order) that `BatchWriteAsync`'s signature promises.

**`GetStalledTransactionsAsync`** — simple, port `:651-685` directly:
```sql
SELECT SurrogateIdRangeFirstValue FROM dbo.Transactions
WHERE IsCompleted = 0 AND HeartbeatDate < @StalledBefore;
```
where `@StalledBefore = DateTimeOffset.UtcNow - stallThreshold`, mapping each result row to `new TransactionId(value)`.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerFhirRepositoryBatchTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _repository = _database.Repository;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenThreeNewResourcesInOneBatch_WhenBatchWriteAsyncCalled_ThenAllThreeAreCreated()
    {
        var transactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var operations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "batch-1", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-1"}"""), [], "PUT", 0),
            ("Patient", "batch-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-2"}"""), [], "PUT", 1),
            ("Observation", "batch-3", ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"batch-3"}"""), [], "PUT", 2),
        };

        var keys = await _repository.BatchWriteAsync(transactionId, operations, CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, CancellationToken.None);

        keys.Count.ShouldBe(3);
        (await _repository.GetAsync(new ResourceKey("Patient", "batch-1"), CancellationToken.None)).ShouldNotBeNull();
        (await _repository.GetAsync(new ResourceKey("Observation", "batch-3"), CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenAResourceAlreadyAtAHigherVersionThanTheBatchWouldWrite_WhenBatchWriteAsyncCalled_ThenThrowsInvalidOperationException()
    {
        var existing = new ResourceWrapper("Patient", "batch-conflict-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-conflict-1"}"""), new ResourceRequest("PUT"));
        await _repository.CreateOrUpdateAsync(existing, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(existing with { }, CancellationToken.None); // now at version 2

        // A stale caller retries a batch operation still believing this resource doesn't exist yet
        // -- the entryIndex-derived surrogate ID here is fine, but the version-conflict pre-check
        // (Version 1 <= existing MaxVersion 2) must fire before any SQL write is even attempted.
        var transactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var operations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "batch-conflict-1", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-conflict-1"}"""), [], "PUT", 0),
        };

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _repository.BatchWriteAsync(transactionId, operations, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNoStalledTransactions_WhenGetStalledTransactionsAsyncCalledWithAOneHourThreshold_ThenReturnsEmpty()
    {
        var stalled = await _repository.GetStalledTransactionsAsync(TimeSpan.FromHours(1), CancellationToken.None);
        stalled.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Write the failing differential test**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Models;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class BatchWriteDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenAFiveResourceBatchWrittenThroughBothRepositories_WhenSnapshottingDboResource_ThenAllFiveRowsAreEquivalent()
    {
        var operations = Enumerable.Range(0, 5)
            .Select(i => ("Patient", $"diff-batch-{i}",
                ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"diff-batch-{{i}}"}"""),
                (IReadOnlyList<object>)[], "PUT", i))
            .ToArray();

        var legacyTx = await _harness.LegacyRepository.GetNextTransactionIdAsync(CancellationToken.None);
        await _harness.LegacyRepository.BatchWriteAsync(legacyTx, operations, CancellationToken.None);
        await _harness.LegacyRepository.CommitTransactionAsync(legacyTx, CancellationToken.None);

        var newTx = await _harness.NewRepository.GetNextTransactionIdAsync(CancellationToken.None);
        await _harness.NewRepository.BatchWriteAsync(newTx, operations, CancellationToken.None);
        await _harness.NewRepository.CommitTransactionAsync(newTx, CancellationToken.None);

        var legacySnapshot = await _harness.SnapshotLegacyAsync("dbo.Resource", "ResourceId LIKE 'diff-batch-%'", CancellationToken.None);
        var newSnapshot = await _harness.SnapshotNewAsync("dbo.Resource", "ResourceId LIKE 'diff-batch-%'", CancellationToken.None);

        _harness.AssertEquivalent(legacySnapshot, newSnapshot, "ResourceSurrogateId", "TransactionId", "HistoryTransactionId");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryBatchTests|FullyQualifiedName~BatchWriteDifferentialTests"`
Expected: FAIL (compile error, `BatchWriteAsync`/`GetStalledTransactionsAsync` not implemented on `SqlServerFhirRepository`).

- [ ] **Step 4: Implement `BatchWriteAsync` and `GetStalledTransactionsAsync`** per the design above — read `SqlEntityFrameworkRepository.cs:337-631` directly one more time while implementing to confirm the exact `InvalidOperationException` message text and `ResourceVersionConflictException` constructor arguments before finalizing.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryBatchTests|FullyQualifiedName~BatchWriteDifferentialTests"`
Expected: 4/4 PASS.

- [ ] **Step 6: Full solution build + regression**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 7: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryBatchTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/BatchWriteDifferentialTests.cs
git commit -m "feat(datalayer-sqlserver): port BatchWriteAsync/GetStalledTransactionsAsync, including client-side version/surrogate conflict pre-checks"
```

---

### Task 8: The new repository — history streaming (faithful port, no behavioral divergence)

**Corrected during plan review — this task no longer contains a "bug fix."** An earlier draft of this plan (and of Global Constraints) claimed `IdHelper.ToDate()` was an incompatible/buggy decode of `ResourceSurrogateId` and had the user approve fixing it by sourcing `LastModified` from `dbo.Transactions.CreateDate` instead. That claim was arithmetically wrong (re-verified: `IdHelper`'s ticks-based formula IS the same `ms × 80,000` scheme the real surrogate-ID generator uses — see Global Constraints). With the correct facts, the user chose to match legacy exactly: `LastModified = entity.ResourceSurrogateId.ToDate()` (via `Ignixa.Domain.Abstractions.IdHelper`, no porting needed, a shared extension method), not a `Transactions.CreateDate` join for the output field. **This is now a faithful, non-divergent port — no `ignoredColumns` treatment is needed for `LastModified` in this task's differential tests for behavioral reasons** (a differential test comparing history across TWO INDEPENDENTLY-WRITTEN databases will still see different real `LastModified` values, since each write happens at a different wall-clock moment on each side — that's a timing artifact of the test setup, not a design divergence, and is called out explicitly below where it applies).

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryHistoryTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/HistoryDifferentialTests.cs`

**Interfaces:**
- Consumes: everything Task 6/7 produced.
- Produces: `GetResourceHistoryAsync`/`GetTypeHistoryAsync`/`GetSystemHistoryAsync` added to `SqlServerFhirRepository`.

Port `SqlEntityFrameworkRepository.cs:756-931`'s shared `ExecuteHistoryQueryAsync` logic as one private helper all 3 public methods call, differing only in their base `WHERE` clause. The `LEFT JOIN` to `dbo.Transactions` is still needed — for `Since`/`Until` filtering and for sort ordering (both confirmed real, separate behaviors, unrelated to the `LastModified` field itself) — even though `LastModified`'s VALUE now comes from `r.ResourceSurrogateId` in C#, not from a selected `t.CreateDate` column:
```sql
-- GetResourceHistoryAsync (by key)
... FROM dbo.Resource r LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
    WHERE r.ResourceTypeId = @ResourceTypeId AND r.ResourceId = @ResourceId

-- GetTypeHistoryAsync (by resource type)
... WHERE r.ResourceTypeId = @ResourceTypeId

-- GetSystemHistoryAsync (all types) -- needs a JOIN to dbo.ResourceType for the type name, per the original's Include(ResourceType)
... FROM dbo.Resource r
    LEFT JOIN dbo.Transactions t ON r.TransactionId = t.SurrogateIdRangeFirstValue
    JOIN dbo.ResourceType rt ON r.ResourceTypeId = rt.ResourceTypeId
    WHERE 1=1
```
Shared filters/pagination, appended to whichever base `WHERE` above applies: optional `AND t.CreateDate >= @Since` / `AND t.CreateDate <= @Until` (both UTC-normalized — confirm `HistoryQueryParameters`'s real property types by reading its definition before implementing, and match the original's `.UtcDateTime` comparison exactly), `ORDER BY t.CreateDate {ASC|DESC}, r.ResourceSurrogateId {ASC|DESC}` (direction from `parameters.Sort`), `OFFSET @Offset ROWS FETCH NEXT @CountPlusOne ROWS ONLY` where `@CountPlusOne = parameters.Count + 1` (the original's `+1` — confirmed a has-more-page sentinel the caller strips; port this pagination shape exactly even though this task doesn't need to fully understand the caller's use of the extra row, per the original's own documented behavior). **Call `parameters = parameters.Validate()` before building the query** (legacy calls this in all 3 methods — confirmed at `HistoryQueryParameters.cs:100`; it clamps `Count` to `[1, 1000]` and `Offset` to `≥ 0` — skipping it changes pagination behavior for out-of-range caller inputs).

Per-row: decompress `RawResource` via `GzipResourceCompressor`, compute `LastModified = r.ResourceSurrogateId.ToDate()` (via `IdHelper`), resolve the resource type name (available directly from the query for `GetSystemHistoryAsync`'s join; for the by-key/by-type variants, the caller already knows the type — use the parameter, no extra lookup needed, unlike the original's more roundabout cache-then-DB-fallback since this port's query shape makes the type name available up front in all 3 cases), and set `Request = new ResourceRequest(entity.RequestMethod ?? "PUT", ...)` and `IsDeleted = entity.IsDeleted` on the constructed `SearchEntryResult` (both confirmed set by the original at `:906-915`, omitted from an earlier draft of this task). **Wrap the per-row deserialization/mapping in try/catch, log a warning, and skip the row (do not throw) on failure** — replicate the original's per-row swallow-and-log behavior exactly (a design choice already made, not something this port second-guesses), stream via `yield return` for successfully-mapped rows only.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerFhirRepositoryHistoryTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _repository = _database.Repository;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAResourceUpdatedThreeTimes_WhenGetResourceHistoryAsyncCalled_ThenReturnsAllThreeVersionsNewestFirstByDefault()
    {
        var resource = new ResourceWrapper("Patient", "history-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-1"}"""), new ResourceRequest("PUT"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var history = await _repository.GetResourceHistoryAsync(
            new ResourceKey("Patient", "history-1"), new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();

        history.Count.ShouldBe(3);
        history.Select(h => h.VersionId).ShouldBe(["3", "2", "1"]);
    }

    [Fact]
    public async Task GivenAHistoryRow_WhenStreamed_ThenLastModifiedMatchesTheOwningTransactionsCreateDate()
    {
        var resource = new ResourceWrapper("Patient", "history-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-2"}"""), new ResourceRequest("PUT"));
        var beforeWrite = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        var afterWrite = DateTimeOffset.UtcNow.AddSeconds(1);

        var history = await _repository.GetResourceHistoryAsync(
            new ResourceKey("Patient", "history-2"), new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();

        // LastModified is decoded from ResourceSurrogateId via IdHelper.ToDate() -- confirmed a
        // correct, real, recent timestamp (Global Constraints has the arithmetic proof), not the
        // garbage value an earlier, incorrect draft of this plan believed it would be.
        history.Single().LastModified.ShouldBeInRange(beforeWrite, afterWrite);
    }
}
```

`HistoryQueryParameters`'s real shape (`Count`, `Since`, `Until`, `Offset`, `Sort` — confirm exact property names before finalizing this test) needs to be read from its real source (likely `src/Application/Ignixa.Domain/Models/` or nearby) during implementation.

- [ ] **Step 2: Write the failing differential test**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class HistoryDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTheSameThreeVersionHistoryWrittenThroughBothRepositories_WhenGetResourceHistoryAsyncCalledOnBoth_ThenVersionIdsAndResourceBytesMatchButLastModifiedIsExplicitlyExempt()
    {
        var resource = new ResourceWrapper("Patient", "diff-history-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-history-1"}"""), new ResourceRequest("PUT"));

        for (var i = 0; i < 3; i++)
        {
            await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
            await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        }

        var key = new ResourceKey("Patient", "diff-history-1");
        var parameters = new HistoryQueryParameters { Count = 10 };
        var legacyHistory = await _harness.LegacyRepository.GetResourceHistoryAsync(key, parameters, CancellationToken.None).ToListAsync();
        var newHistory = await _harness.NewRepository.GetResourceHistoryAsync(key, parameters, CancellationToken.None).ToListAsync();

        legacyHistory.Select(h => h.VersionId).ShouldBe(newHistory.Select(h => h.VersionId));
        // LastModified is not compared here, but for a mundane reason, not a design divergence:
        // this test writes to two INDEPENDENT databases in two separate CreateOrUpdateAsync loops
        // above, so each side's real ResourceSurrogateId (and therefore its correctly-decoded
        // LastModified) reflects a genuinely different wall-clock write time. The decoding
        // MECHANISM is identical and correct on both sides (Global Constraints) -- only the values
        // differ, because the two writes happened at different real moments.
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryHistoryTests|FullyQualifiedName~HistoryDifferentialTests"`
Expected: FAIL (compile error, the 3 history methods not implemented on `SqlServerFhirRepository`).

- [ ] **Step 4: Implement `GetResourceHistoryAsync`/`GetTypeHistoryAsync`/`GetSystemHistoryAsync`** per the design above.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryHistoryTests|FullyQualifiedName~HistoryDifferentialTests"`
Expected: 3/3 PASS.

- [ ] **Step 6: Full solution build + regression**

Run: `dotnet build All.sln` → 0/0. Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 7: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryHistoryTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/HistoryDifferentialTests.cs
git commit -m "feat(datalayer-sqlserver): port GetResourceHistoryAsync/GetTypeHistoryAsync/GetSystemHistoryAsync"
```

---

### Task 9: The new repository — expiry, TTL, and hard-delete (`GetExpiredResourcesAsync`, `HardDeleteResourceAsync`)

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryExpiryTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ExpiryAndHardDeleteDifferentialTests.cs`

**Interfaces:**
- Consumes: everything Task 6-8 produced.
- Produces: `GetExpiredResourcesAsync`/`HardDeleteResourceAsync` added to `SqlServerFhirRepository` — this completes all 12 `IFhirRepository` methods.

**`GetExpiredResourcesAsync`** — port `:934-967`'s 3-way join as raw SQL:
```sql
SELECT TOP (@BatchSize) t.ResourceTypeId, t.ResourceId, t.ExpiresAt, rt.Name
FROM dbo.ResourceTtl t
JOIN dbo.Resource r ON r.ResourceTypeId = t.ResourceTypeId AND r.ResourceId = t.ResourceId AND r.IsHistory = 0 AND r.IsDeleted = 0
JOIN dbo.ResourceType rt ON rt.ResourceTypeId = t.ResourceTypeId
WHERE t.ExpiresAt < @Now;
```
Map each row to `new ExpiredResourceInfo(ResourceTypeId, ResourceId, ExpiresAt, ResourceTypeName)`.

**`HardDeleteResourceAsync`** — already raw SQL in the original (confirmed, Global Constraints); port the real 4-step statement nearly verbatim, retargeting only the execution wrapper. Read `SqlEntityFrameworkRepository.cs:989-1026` directly and reproduce its exact `ExecuteSqlInterpolatedAsync` statement text as a parameterized `SqlCommand` (the `DECLARE @SurrogateIds TABLE` + delete-from-all-15-search-index-tables + delete-from-`dbo.Resource` + delete-from-`dbo.ResourceTtl` sequence) — this is the one method in the whole port where "read the real file and transcribe the SQL text with minimal changes" is the correct and sufficient approach, since there's no LINQ to translate.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerFhirRepositoryExpiryTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _repository = _database.Repository;
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAResourceWithAnExpiresAtInThePast_WhenGetExpiredResourcesAsyncCalled_ThenItIsReturned()
    {
        var resource = new ResourceWrapper("Patient", "expiry-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"expiry-1"}"""), new ResourceRequest("PUT"))
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var expired = await _repository.GetExpiredResourcesAsync(batchSize: 100, CancellationToken.None);

        expired.ShouldContain(e => e.ResourceId == "expiry-1" && e.ResourceType == "Patient");
    }

    [Fact]
    public async Task GivenAResourceWithNoExpiresAt_WhenGetExpiredResourcesAsyncCalled_ThenItIsNotReturned()
    {
        var resource = new ResourceWrapper("Patient", "expiry-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"expiry-2"}"""), new ResourceRequest("PUT"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var expired = await _repository.GetExpiredResourcesAsync(batchSize: 100, CancellationToken.None);

        expired.ShouldNotContain(e => e.ResourceId == "expiry-2");
    }

    [Fact]
    public async Task GivenAResourceWithHistory_WhenHardDeleteResourceAsyncCalled_ThenAllVersionsAndSearchIndexRowsAreGone()
    {
        var resource = new ResourceWrapper("Patient", "hard-delete-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"hard-delete-1"}"""), new ResourceRequest("PUT"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var resourceTypeId = await _database.ExecuteScalarAsync<short>("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        await _repository.HardDeleteResourceAsync(resourceTypeId, "hard-delete-1", CancellationToken.None);

        var remainingRows = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'hard-delete-1'");
        remainingRows.ShouldBe(0);
        var remainingTtl = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = 'hard-delete-1'");
        remainingTtl.ShouldBe(0);
    }
}
```

- [ ] **Step 2: Write the failing differential test**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class ExpiryAndHardDeleteDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTheSameHardDeleteThroughBothRepositories_WhenSnapshottingDboResourceAndAllSearchIndexTables_ThenBothAreEmptyOnBothSides()
    {
        var resource = new ResourceWrapper("Patient", "diff-hard-delete-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-hard-delete-1"}"""), new ResourceRequest("PUT"));

        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        // ResourceTypeId is allocated independently per database -- resolve it on each side rather
        // than assuming it's the same numeric value.
        var legacyTypeId = (await _harness.SnapshotLegacyAsync("dbo.ResourceType", "Name = 'Patient'", CancellationToken.None))
            .Rows.Single()["ResourceTypeId"];
        var newTypeId = (await _harness.SnapshotNewAsync("dbo.ResourceType", "Name = 'Patient'", CancellationToken.None))
            .Rows.Single()["ResourceTypeId"];

        await _harness.LegacyRepository.HardDeleteResourceAsync((short)(int)legacyTypeId!, "diff-hard-delete-1", CancellationToken.None);
        await _harness.NewRepository.HardDeleteResourceAsync((short)(int)newTypeId!, "diff-hard-delete-1", CancellationToken.None);

        var legacySnapshot = await _harness.SnapshotLegacyAsync("dbo.Resource", "ResourceId = 'diff-hard-delete-1'", CancellationToken.None);
        var newSnapshot = await _harness.SnapshotNewAsync("dbo.Resource", "ResourceId = 'diff-hard-delete-1'", CancellationToken.None);
        _harness.AssertEquivalent(legacySnapshot, newSnapshot);
    }
}
```

Note: `RowStateSnapshot.Rows` needs to be a public property (Task 5's implementation) exposing `IReadOnlyList<IReadOnlyDictionary<string, object?>>` for this kind of ad-hoc single-value extraction — confirm this is already the shape from Task 5, adjust if Task 5's implementer chose a different accessor name.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryExpiryTests|FullyQualifiedName~ExpiryAndHardDeleteDifferentialTests"`
Expected: FAIL (compile error).

- [ ] **Step 4: Implement `GetExpiredResourcesAsync` and `HardDeleteResourceAsync`** per the design above.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlServerFhirRepositoryExpiryTests|FullyQualifiedName~ExpiryAndHardDeleteDifferentialTests"`
Expected: 4/4 PASS.

- [ ] **Step 6: Confirm `SqlServerFhirRepository` now implements all 12 `IFhirRepository` methods**

Run: `dotnet build All.sln` — 0 warnings, 0 errors, including no "does not implement interface member" errors, confirms the class is a complete `IFhirRepository`.

- [ ] **Step 7: Full regression**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` → all passing.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerFhirRepository.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlServerFhirRepositoryExpiryTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ExpiryAndHardDeleteDifferentialTests.cs
git commit -m "feat(datalayer-sqlserver): port GetExpiredResourcesAsync/HardDeleteResourceAsync -- SqlServerFhirRepository now implements all 12 IFhirRepository methods"
```

---

### Task 10: Comprehensive differential pass + error-handling parity

**Files:**
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ErrorHandlingParityDifferentialTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/ComprehensiveWorkflowDifferentialTests.cs`

**Interfaces:**
- Consumes: the complete `SqlServerFhirRepository` (Tasks 6-9), `DifferentialTestHarness` (Task 5).
- Produces: nothing new consumed by later tasks — this is the plan's correctness gate before cutover (Task 11).

This task does not add new repository code. It proves, with tests, that everything built in Tasks 1-9 composes correctly under realistic multi-operation workflows, and that error-handling parity (design doc §4, a first-class requirement) genuinely holds — not just per-method in isolation.

- [ ] **Step 1: Write the error-handling parity differential tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class ErrorHandlingParityDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenTheSameVersionMergedTwiceDirectlyThroughTheMergeRepository_WhenBothRepositoriesAreExercised_ThenBothThrowPreconditionFailedException()
    {
        // Corrected during plan review: an earlier draft raced two concurrent CreateOrUpdateAsync
        // calls hoping one would lose and hit SQL error 50409 -- nondeterministic, since if the two
        // calls happen to serialize (very possible against a fast local SQL Server instance) both
        // succeed and the test fails spuriously. This version forces the SQL-level condition
        // directly and deterministically: call MergeResourcesAsync twice with the SAME explicit
        // target version, bypassing IFhirRepository's own client-side "read current version first"
        // logic entirely, so the conflict is guaranteed to occur inside the stored procedure itself
        // -- proving SqlMergeRepository's/SqlServerMergeRepository's
        // catch (SqlException ex) when (ex.Number == 50409) mapping is present and identical on
        // both implementations.
        var legacyTx = await _harness.LegacyRepository.GetNextTransactionIdAsync(CancellationToken.None);
        var legacyResource = new ResourceWrapper("Patient", "diff-conflict-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-1"}"""), new ResourceRequest("PUT"));
        await _harness.LegacyMergeRepository.MergeResourcesAsync(
            legacyTx.Value, singleTransaction: true, [legacyResource], [0], CancellationToken.None);

        var newTx = await _harness.NewRepository.GetNextTransactionIdAsync(CancellationToken.None);
        var newResource = new ResourceWrapper("Patient", "diff-conflict-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-1"}"""), new ResourceRequest("PUT"));
        await _harness.NewMergeRepository.MergeResourcesAsync(
            newTx.Value, singleTransaction: true, [newResource], [0], CancellationToken.None);

        // Second merge, SAME explicit version ("1") for the SAME (ResourceTypeId, ResourceId) --
        // MergeResources itself (not any client-side pre-check) must detect and reject this.
        var legacyException = await Should.ThrowAsync<Exception>(() =>
            _harness.LegacyMergeRepository.MergeResourcesAsync(
                legacyTx.Value, singleTransaction: true, [legacyResource with { }], [1], CancellationToken.None));
        var newException = await Should.ThrowAsync<Exception>(() =>
            _harness.NewMergeRepository.MergeResourcesAsync(
                newTx.Value, singleTransaction: true, [newResource with { }], [1], CancellationToken.None));

        legacyException.ShouldBeOfType<PreconditionFailedException>();
        newException.ShouldBeOfType<PreconditionFailedException>();
    }

    [Fact]
    public async Task GivenABatchWriteWithAStaleVersion_WhenCalledOnBothRepositories_ThenBothThrowInvalidOperationExceptionWithTheSameMessage()
    {
        var resource = new ResourceWrapper("Patient", "diff-conflict-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), new ResourceRequest("PUT"));
        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.LegacyRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None); // version 2
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _harness.NewRepository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var staleOperations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "diff-conflict-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"diff-conflict-2"}"""), [], "PUT", 0),
        };

        var legacyTx = await _harness.LegacyRepository.GetNextTransactionIdAsync(CancellationToken.None);
        var newTx = await _harness.NewRepository.GetNextTransactionIdAsync(CancellationToken.None);

        var legacyException = await Should.ThrowAsync<InvalidOperationException>(() =>
            _harness.LegacyRepository.BatchWriteAsync(legacyTx, staleOperations, CancellationToken.None));
        var newException = await Should.ThrowAsync<InvalidOperationException>(() =>
            _harness.NewRepository.BatchWriteAsync(newTx, staleOperations, CancellationToken.None));

        legacyException.Message.ShouldBe(newException.Message);
    }
}
```

- [ ] **Step 2: Write the comprehensive multi-operation workflow differential test**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Differential;

public class ComprehensiveWorkflowDifferentialTests : IAsyncLifetime
{
    private DifferentialTestHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await DifferentialTestHarness.CreateAsync(CancellationToken.None);
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task GivenARealisticCreateUpdateBatchDeleteHardDeleteWorkflowRunOnBothRepositories_WhenSnapshottingEveryAffectedTable_ThenAllRowsAreEquivalent()
    {
        // Exercises CreateOrUpdateAsync, GetNextTransactionIdAsync, BatchWriteAsync,
        // CommitTransactionAsync, DeleteAsync, and HardDeleteResourceAsync in one realistic
        // sequence per repository -- proving composition, not exhaustive method coverage.
        // GetAsync/the 3 history methods/GetStalledTransactionsAsync/GetExpiredResourcesAsync
        // each already have dedicated coverage in Tasks 6, 8, and 9 and are deliberately NOT
        // re-exercised here -- this test's value is the multi-method sequence, not a full-surface
        // checklist.
        foreach (var isLegacy in new[] { true, false })
        {
            var repository = isLegacy ? _harness.LegacyRepository : _harness.NewRepository;

            var patient = new ResourceWrapper("Patient", "workflow-1", "1", DateTimeOffset.UtcNow,
                ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"workflow-1"}"""), new ResourceRequest("PUT"));
            await repository.CreateOrUpdateAsync(patient, CancellationToken.None);
            await repository.CreateOrUpdateAsync(patient with { }, CancellationToken.None);

            var batchTx = await repository.GetNextTransactionIdAsync(CancellationToken.None);
            var batchOps = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
            {
                ("Observation", "workflow-obs-1", ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"workflow-obs-1"}"""), [], "PUT", 0),
            };
            await repository.BatchWriteAsync(batchTx, batchOps, CancellationToken.None);
            await repository.CommitTransactionAsync(batchTx, CancellationToken.None);

            await repository.DeleteAsync(new ResourceKey("Observation", "workflow-obs-1"), new ResourceRequest("DELETE"), cancellationToken: CancellationToken.None);

            var resourceTypeId = await GetResourceTypeIdAsync(repository, isLegacy, "Patient");
            await repository.HardDeleteResourceAsync(resourceTypeId, "never-created-but-hard-delete-must-no-op-safely", CancellationToken.None);
        }

        string[] tablesToCompare = ["dbo.Resource", "dbo.Transactions", "dbo.ResourceType"];
        foreach (var table in tablesToCompare)
        {
            var legacySnapshot = await _harness.SnapshotLegacyAsync(table, "1=1", CancellationToken.None);
            var newSnapshot = await _harness.SnapshotNewAsync(table, "1=1", CancellationToken.None);
            // Every ignored column here is a genuinely independently-allocated identifier/timestamp
            // (each database allocates its own sequence values/clock reads) -- none represent a
            // real behavioral divergence. ResourceTypeId specifically: ResourceType rows are
            // IDENTITY-keyed per database, so the same logical type ("Patient") gets a different
            // numeric ID on each side even though the Name column (compared, not ignored) matches.
            _harness.AssertEquivalent(legacySnapshot, newSnapshot,
                "ResourceSurrogateId", "TransactionId", "HistoryTransactionId",
                "SurrogateIdRangeFirstValue", "SurrogateIdRangeLastValue", "CreateDate", "HeartbeatDate",
                "ResourceTypeId");
        }
    }

    private async Task<short> GetResourceTypeIdAsync(IFhirRepository repository, bool isLegacy, string resourceTypeName)
    {
        // Resolves a real ResourceTypeId via a lightweight probe write+read rather than reaching
        // into either repository's private cache -- keeps this test honestly black-box against
        // the IFhirRepository interface alone.
        var probe = new ResourceWrapper(resourceTypeName, $"probe-{Guid.NewGuid():N}", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"{{resourceTypeName}}","id":"probe"}"""), new ResourceRequest("PUT"));
        await repository.CreateOrUpdateAsync(probe, CancellationToken.None);
        var snapshot = isLegacy
            ? await _harness.SnapshotLegacyAsync("dbo.ResourceType", $"Name = '{resourceTypeName}'", CancellationToken.None)
            : await _harness.SnapshotNewAsync("dbo.ResourceType", $"Name = '{resourceTypeName}'", CancellationToken.None);
        return (short)(int)snapshot.Rows.Single()["ResourceTypeId"]!;
    }
}
```

- [ ] **Step 3: Run all differential tests together**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~Differential`
Expected: every differential test in the whole `Differential/` namespace (Tasks 5-10 combined) PASSES.

- [ ] **Step 4: Full solution build + complete regression**

Run: `dotnet build All.sln` → 0 Warning(s), 0 Error(s).
Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` (with `TEST_SQL_CONNECTION_STRING` set) → all passing except the 2 known pre-existing unrelated `Ignixa.SqlOnFhir.Tests` submodule failures every task in this initiative has hit.

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential
git commit -m "test(datalayer-sqlserver): comprehensive differential pass proving error-handling parity and full-workflow equivalence"
```

---

### Task 11: Cutover — swap the write path in production

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:327` (the `createRepository` delegate)
- Test: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/` (existing tests exercising `SqlEntityFrameworkRepositoryFactory.GetRepositoryAsync` — confirm they still pass with the new implementation wired in; do not write new tests here, this task's job is verifying existing coverage still holds after the swap)

**Interfaces:**
- Consumes: `SqlServerFhirRepository` (Tasks 6-9, complete), `ISqlExecutionService` (already DI-registered per Phase A).
- Produces: nothing new — this is the cutover itself.

**The swap** (per design doc §5 and Global Constraints): change `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory`'s `createRepository` delegate (currently `SqlEntityFrameworkRepositoryFactory.cs:327-344`, constructing `SqlEntityFrameworkRepository`) to construct `SqlServerFhirRepository` instead, wired to `ISqlExecutionService`/`tenantId`/a new `SqlServerSearchIndexReferenceDataCache` instance/`SqlServerMergeRepository`/`SqlServerPostMergeExtensionUpdater` — the exact same set of dependencies Task 5's `DifferentialTestHarness.NewRepository` already wires up for tests, now wired into the real per-tenant factory instead of a test fixture. The `createSearchService` delegate (`:347`) is **not touched** — it continues receiving whatever `IFhirRepository` it's handed purely through the interface (confirmed no downcast exists, design doc §1).

`ISqlExecutionService` needs to be available to `SqlEntityFrameworkRepositoryFactory` — check its constructor/DI registration (it currently only takes EF-related dependencies) and add whatever DI wiring is needed to inject `ISqlExecutionService` (already registered as a singleton service somewhere in Phase A's `ServiceCollectionExtensions.cs` — reuse that registration, do not create a second one).

No feature flag, no toggle (design doc §5, user's explicit decision) — this is a straight, unconditional swap.

- [ ] **Step 1: Confirm the swap's dependency wiring compiles**

Read `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs` in full (the real current constructor, DI registrations, and `CreateServiceFactory`'s surrounding context) before editing — confirm exactly what constructor parameter(s) need to be added to get `ISqlExecutionService` into this class, and check `src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs` (Phase A) for the existing DI registration to reuse.

- [ ] **Step 2: Make the swap**

Change the `createRepository` delegate body to construct `SqlServerFhirRepository` (and its dependencies) instead of `SqlEntityFrameworkRepository`/`SqlMergeRepository`. Leave `createSearchService` untouched.

- [ ] **Step 3: Run the existing EF-integration-test suite to confirm the read path (still EF-based) is unaffected**

Run: `dotnet test test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` (with `TEST_SQL_CONNECTION_STRING` set)
Expected: same pass/skip counts as before this task's change (Phase C's ledger records this suite's baseline: 2 passed, 4 skipped — confirm this task doesn't regress it).

- [ ] **Step 4: Run every integration/differential test from Tasks 2-10 once more, against the now-live wiring**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests` (with `TEST_SQL_CONNECTION_STRING` set)
Expected: all passing.

- [ ] **Step 5: Full solution build + complete regression**

Run: `dotnet build All.sln` → 0 Warning(s), 0 Error(s).
Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` (with `TEST_SQL_CONNECTION_STRING` set) → all passing except the 2 known pre-existing unrelated `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
git commit -m "feat(datalayer-sqlserver): cut writes over to SqlServerFhirRepository -- straight swap, no feature flag"
```

---

## Final steps

After all 11 tasks are complete: run the full solution build+test one more time, generate the final whole-branch review package (merge-base with `feature/fhir-to-sql-compiler`), dispatch the final reviewer on the most capable available model, report to the user, and ask separately about merging into `feature/fhir-to-sql-compiler` and pushing to origin — this branch has stayed standalone through Phases A, B, and C by explicit user choice each time; ask again for Phase D, do not assume the same answer.

## Process note

Per the user's explicit instruction this session, given this phase's production write-path blast radius: **before dispatching Task 1's implementer, this plan document itself gets a Fable-model review** — in addition to this initiative's standing per-task and final-whole-branch code reviews, which still happen exactly as in every prior phase. The plan-level reviewer should assess:
- Does the task decomposition actually cover every real behavior in the 12 `IFhirRepository` methods (not just the ones highlighted in the design doc)?
- Does the differential-test harness design (Task 5) actually prove row-level equivalence rigorously, and is it built before the tasks that depend on it (Tasks 6-10), not after?
- Are there FHIR-semantic subtleties in the real current code (versioning, history, tombstones, optimistic concurrency) this plan's task briefs fail to account for?
- Is the task sequencing sound overall?

Only after that review is clean (or its findings are resolved) should the Subagent-Driven vs Inline execution choice be offered.
