using System.Globalization;
using System.Xml.Linq;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>Spec §5 (barrier-fenced cutoff), §8.3-§8.5 and §8.7: the SQL <see cref="IReindexStore"/> contract.</summary>
public sealed class ReindexStoreSqlContractTests : IAsyncLifetime
{
    private const long BarrierEventId = 70;
    private static readonly XNamespace ShowPlan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    private TestTenantDatabase _database = null!;
    private IReindexStore _store = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _store = _database.ReindexStore;
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    // §8.7: the barrier is raised monotonically, then B and S are read.
    [Fact]
    public async Task GivenExistingTransactions_WhenBarrierIsRaisedMonotonically_ThenItReturnsThePostBarrierCutoff()
    {
        await PutAsync(Patient("before-barrier"));

        var first = await _store.RaiseBarrierAsync(42, CancellationToken.None);
        var second = await _store.RaiseBarrierAsync(17, CancellationToken.None);

        first.TransactionId.ShouldBeGreaterThan(0);
        first.SurrogateId.ShouldBeGreaterThanOrEqualTo(first.TransactionId);
        second.ShouldBe(first);
        (await _database.ExecuteScalarAsync<long>(
            "SELECT Bigint FROM dbo.Parameters WHERE Id = 'Conformance.MinAcceptedDefinitionsEventId'")).ShouldBe(42);
    }

    // §5.1 S_t, §5.5 pre-feature rows: S is the greater of the newest current resource and the newest transaction.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GivenTransactionsAndLegacyResources_WhenBarrierIsRaised_ThenTheSurrogateCutoffIsTheGreaterValue(
        bool transactional, bool legacy)
    {
        var transactionCutoff = -1L;
        if (transactional)
        {
            await PutAsync(Patient("transactional"));
            transactionCutoff = await _database.ExecuteScalarAsync<long>("SELECT MAX(SurrogateIdRangeLastValue) FROM dbo.Transactions");
        }

        var legacySurrogateId = transactionCutoff + 999_999;
        if (legacy)
        {
            await _database.ExecuteNonQueryAsync(
                $"""
                 INSERT dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted,
                     RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId)
                 VALUES (1, 'legacy', 1, 0, {legacySurrogateId}, 0, 'PUT', 0x01, 0, NULL, NULL, NULL);
                 """);
        }

        var cutoff = await _store.RaiseBarrierAsync(42, CancellationToken.None);

        cutoff.SurrogateId.ShouldBe(legacy ? legacySurrogateId : -1);
        if (transactional)
        {
            cutoff.TransactionId.ShouldBeGreaterThan(0);
        }
        else
        {
            cutoff.TransactionId.ShouldBe(-1);
        }
    }

    // §5.2 step 3 / D1: the drain reports incomplete transactions only, never trusting IsVisible.
    [Fact]
    public async Task GivenVisibilityDisagreesWithCompletion_WhenDraining_ThenOnlyIncompleteTransactionsAreReported()
    {
        // 200 completes and visibility advances past it; then 100 is allocated late, below the watermark (D1).
        // 300: completed but not yet visible. 400: still incomplete although marked visible.
        await InsertTransactionsAsync("(200, 200, 1, 0); EXEC dbo.MergeResourcesAdvanceTransactionVisibility;");
        (await _store.GetVisibleWatermarkAsync(CancellationToken.None)).ShouldBe(200);
        await InsertTransactionsAsync("(100, 100, 0, 0), (300, 300, 1, 0), (400, 400, 0, 1)");

        foreach (var expected in new long?[] { 100, 400, null })
        {
            ((await _store.GetOldestIncompleteTransactionAsync(500, CancellationToken.None))?.TransactionId).ShouldBe(expected);
            await _database.ExecuteNonQueryAsync($"UPDATE dbo.Transactions SET IsCompleted = 1 WHERE SurrogateIdRangeFirstValue = {expected ?? 0}");
        }
    }

    // §5.2 step 3: the drain seeks the filtered incomplete index, although dbo.Transactions is never pruned.
    [Fact]
    public async Task GivenManyCompletedTransactions_WhenTheDrainLooksForAnIncompleteOne_ThenItSeeksTheFilteredIndexInsteadOfScanning()
    {
        await InsertTransactionsAsync(
            """
            SELECT TOP (20000) n, n, 1, 1
            FROM (SELECT ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) FROM sys.all_objects a CROSS JOIN sys.all_objects b) AS numbers(n);
            UPDATE STATISTICS dbo.Transactions WITH FULLSCAN;
            """,
            values: false);
        SqlCommand? drainQuery = null;
        var (store, cache) = await CreateStoreAsync(new InterceptingSqlExecutionService(_database.SqlExecutionService, command =>
        {
            drainQuery = command.Parameters.Contains("@CutoffTransactionId") ? InterceptingSqlExecutionService.Clone(command) : drainQuery;
            return Task.CompletedTask;
        }));
        using (cache)
        {
            (await store.GetOldestIncompleteTransactionAsync(20_000, CancellationToken.None)).ShouldBeNull();
        }

        var plan = await CaptureShowPlanXmlAsync(drainQuery!);
        var operators = XDocument.Parse(plan).Descendants(ShowPlan + "RelOp")
            .Select(operation => (
                PhysicalOp: operation.Attribute("PhysicalOp")!.Value,
                Index: operation.Elements().Where(element => element.Name != ShowPlan + "RelOp")
                    .Descendants(ShowPlan + "Object")
                    .FirstOrDefault(@object => @object.Attribute("Table")?.Value == "[Transactions]")?.Attribute("Index")?.Value,
                RowsRead: operation.Elements(ShowPlan + "RunTimeInformation").Descendants(ShowPlan + "RunTimeCountersPerThread")
                    .Sum(counters => long.Parse(counters.Attribute("ActualRowsRead")?.Value ?? "0", CultureInfo.InvariantCulture))))
            .Where(operation => operation.Index is not null)
            .ToArray();

        operators.ShouldContain(op => op.PhysicalOp == "Index Seek" && op.Index == "[IX_Transactions_SurrogateIdRangeFirstValue_Incomplete]", plan);
        operators.ShouldNotContain(op => op.Index == "[PKC_Transactions_SurrogateIdRangeFirstValue]" && op.PhysicalOp != "Key Lookup", plan);
        operators.Sum(op => op.RowsRead).ShouldBe(0, plan);
    }

    // §8.5: completion checks the tenant catalog by storage URI, never by conformance id.
    [Fact]
    public async Task GivenACatalogProvisionedByUri_WhenAParameterIsChecked_ThenItIsFoundByItsStorageUriNotItsConformanceId()
    {
        const string provisioned = "http://example.org/SearchParameter/provisioned";
        const string neverProvisioned = "http://example.org/SearchParameter/never-provisioned";
        using var catalog = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await catalog.SyncSearchParametersToDatabaseAsync([provisioned], null, CancellationToken.None);
        var physicalId = (await catalog.GetSearchParamIdAsync(provisioned, CancellationToken.None))!.Value;

        // Conformance allocates its own logical ids; one can equal another URI's IDENTITY value.
        var unprovisioned = Target(neverProvisioned, conformanceId: physicalId);
        var overriding = Target("http://example.org/SearchParameter/override", conformanceId: 40_000) with { OverridesCanonical = provisioned };

        (await _store.HasSearchParameterAsync(unprovisioned.StorageCanonical, CancellationToken.None)).ShouldBeFalse();
        (await _store.HasSearchParameterAsync(Target(provisioned, short.MaxValue + 1).StorageCanonical, CancellationToken.None)).ShouldBeTrue();
        (await _store.HasSearchParameterAsync(overriding.StorageCanonical, CancellationToken.None)).ShouldBeTrue();
    }

    // §5.2 step 4, §5.5: only current, non-deleted rows at or below the cutoff are reindexed.
    [Fact]
    public async Task GivenCurrentDeletedAndHistoryRows_WhenRangesAndPagesAreRead_ThenOnlyCurrentNonDeletedRowsAtTheCutoffAreReturned()
    {
        await PutAsync(Patient("first"), Patient("updated"), Patient("updated") with { VersionId = "2" }, Patient("deleted"));
        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "deleted"), new ResourceRequest("DELETE", "Patient/deleted"), definitionsEventId: 0);
        var (_, cutoff) = await _store.RaiseBarrierAsync(50, CancellationToken.None);

        var resources = await ReadAllAsync(_store, "Patient", cutoff);

        resources.Select(resource => resource.Resource.ResourceId).ShouldBe(["first", "updated"], ignoreOrder: true);
        resources.Select(resource => resource.Resource.VersionId).ShouldContain("2");
        resources.ShouldAllBe(resource => resource.ResourceSurrogateId <= cutoff);
    }

    // §8.3: ranges over sparse ids are contiguous and exhaustive to S, and only an unfinished page continues.
    [Theory]
    [InlineData(0, new[] { 0 })]
    [InlineData(2, new[] { 2 })]
    [InlineData(3, new[] { 2, 1 })]
    public async Task GivenSparseCurrentResourceIds_WhenRangesArePlannedInPagesOfTwo_ThenTheyAreContiguousAndExhaustiveToTheUpperBound(
        int resourceCount, int[] expectedPageSizes)
    {
        var ids = new List<long>();
        for (var index = 0; index < resourceCount; index++)
        {
            await PutAsync(Patient($"sparse-{index}"));
        }

        for (var index = 0; index < resourceCount; index++)
        {
            await _database.ExecuteNonQueryAsync(
                $"UPDATE dbo.Resource SET ResourceSurrogateId += {index * 1_000} WHERE ResourceId = 'sparse-{index}' AND IsHistory = 0");
            ids.Add(await _database.ExecuteScalarAsync<long>($"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = 'sparse-{index}'"));
        }

        var upperBound = ids.LastOrDefault() + 2_050;
        var pages = new List<SurrogateIdRangePage>();
        for (long? startAfter = -1; startAfter is { } start; startAfter = pages[^1].NextStartAfter)
        {
            pages.Add(await _store.GetSurrogateIdRangesAsync("Patient", start, upperBound, 1, 2, CancellationToken.None));
        }

        var ranges = pages.SelectMany(page => page.Ranges).ToArray();
        pages.Select(page => page.Ranges.Count).ShouldBe(expectedPageSizes);
        pages.SkipLast(1).Select(page => page.NextStartAfter == page.Ranges[^1].End).ShouldAllBe(continuesAtItsEnd => continuesAtItsEnd);
        ranges.Sum(range => range.ResourceCount).ShouldBe(resourceCount);
        ranges.Zip(ranges.Skip(1)).ShouldAllBe(pair => pair.Second.Start == pair.First.End + 1);
        if (resourceCount > 0)
        {
            ranges[0].Start.ShouldBe(ids[0]);
            ranges[^1].End.ShouldBe(upperBound);
        }
    }

    // §8.7: dbo.ResourceType grows as types are first written, by any instance; a store whose cache was loaded
    // before that must still find the type, or the reindex skips its rows and enables the parameter anyway.
    [Fact]
    public async Task GivenATypeFirstWrittenAfterTheStoreLoadedItsTypes_WhenRangesArePlannedAndRead_ThenItsResourcesAreIncluded()
    {
        var (store, cache) = await CreateStoreAsync(_database.SqlExecutionService);
        using (cache)
        {
            (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ResourceType WHERE Name = 'Observation'")).ShouldBe(0);
            await PutAsync(Resource("Observation", "late-type"));
            var (_, cutoff) = await store.RaiseBarrierAsync(50, CancellationToken.None);

            (await ReadAllAsync(store, "Observation", cutoff)).ShouldHaveSingleItem().Resource.ResourceId.ShouldBe("late-type");
        }
    }

    // §5.5: an import reserves 1000 ids; S is the maximum last value, so a resource past the first value is included.
    [Fact]
    public async Task GivenAnImportReservationWhoseResourceFallsPastItsFirstValue_WhenBarrierIsRaised_ThenTheCutoffRangeIncludesTheResource()
    {
        var (transactionId, _) = await _database.MergeRepository.BeginTransactionAsync(1000, definitionsEventId: 0, CancellationToken.None);
        await _database.MergeRepository.MergeResourcesAsync(transactionId, singleTransaction: true, [Patient("import-tail")], [999], CancellationToken.None);
        await _database.MergeRepository.CommitTransactionAsync(transactionId, null, CancellationToken.None);

        var (cutoffTransactionId, cutoffSurrogateId) = await _store.RaiseBarrierAsync(55, CancellationToken.None);

        var resourceSurrogateId = await _database.ExecuteScalarAsync<long>(
            "SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = 'import-tail' AND IsHistory = 0");
        transactionId.ShouldBeLessThanOrEqualTo(cutoffTransactionId);
        resourceSurrogateId.ShouldBeGreaterThan(cutoffTransactionId);
        cutoffSurrogateId.ShouldBeGreaterThanOrEqualTo(resourceSurrogateId);
        (await ReadAllAsync(_store, "Patient", cutoffSurrogateId)).Select(resource => resource.Resource.ResourceId).ShouldContain("import-tail");
    }

    // F10, §5.5: a version superseded after the read is a conflict, and the index-only write creates no history.
    [Fact]
    public async Task GivenAResourceSupersededAfterRead_WhenItsIndicesAreUpdated_ThenItIsReportedAsAConflictWithoutCreatingHistory()
    {
        var resource = await PutAndReadAsync(Patient("conflict"));
        await PutAsync(Patient("conflict") with { VersionId = "2", DefinitionsEventId = BarrierEventId });
        const string history = "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'conflict' AND IsHistory = 1";
        var historyBefore = await _database.ExecuteScalarAsync<int>(history);

        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe(new SearchIndexUpdateResult(0, 1));

        (await _database.ExecuteScalarAsync<int>(history)).ShouldBe(historyBefore);
    }

    // §8.7 UpdateResourceSearchParams: an index-only rebuild keeps the resource's write claims.
    [Fact]
    public async Task GivenExistingResourceWriteClaims_WhenSearchIndicesAreRebuilt_ThenTheClaimsArePreservedExactly()
    {
        var resource = await PutAndReadAsync(Patient("claims"));
        var surrogateId = resource.ResourceSurrogateId;
        await _database.ExecuteNonQueryAsync(
            $"INSERT dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue) VALUES ({surrogateId}, 1, N'alpha'), ({surrogateId}, 2, N'beta');");

        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe(new SearchIndexUpdateResult(1, 0));

        (await _database.ExecuteScalarAsync<string>(
            $"SELECT STRING_AGG(CONCAT(ClaimTypeId, '=', ClaimValue), ',') WITHIN GROUP (ORDER BY ClaimTypeId) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {surrogateId}"))
            .ShouldBe("1=alpha,2=beta");
    }

    // §5.3 ordering proof: every racing allocation is inside the cutoff or rejected, with and without RCSI.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenConcurrentBarrierRaisesAndStaleAllocations_WhenRacing_ThenEveryAllocationIsCoveredOrRejectedAndVisibilityAdvances(
        bool readCommittedSnapshot)
    {
        await ConfigureReadCommittedSnapshotAsync(readCommittedSnapshot);

        for (var iteration = 1; iteration <= 30; iteration++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allocation = Task.Run(async () =>
            {
                await start.Task;
                await Task.Delay(iteration % 3 == 0 ? 1 : 0);
                return await _database.MergeRepository.BeginTransactionAsync(1, definitionsEventId: iteration - 1, CancellationToken.None);
            });
            var raise = Task.Run(async () =>
            {
                await start.Task;
                await Task.Delay(iteration % 3 == 1 ? 1 : 0);
                return await _store.RaiseBarrierAsync(iteration, CancellationToken.None);
            });

            start.SetResult();
            var rejected = await Record.ExceptionAsync(() => allocation);
            var cutoff = await raise;

            if (rejected is null)
            {
                var (transactionId, _) = await allocation;
                transactionId.ShouldBeLessThanOrEqualTo(cutoff.TransactionId);
                await _database.MergeRepository.CommitTransactionAsync(transactionId, "Barrier race cleanup.", CancellationToken.None);
                continue;
            }

            var transaction = rejected.ShouldBeOfType<StaleConformanceDefinitionsException>().TransactionId;
            (await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = {transaction} AND IsCompleted = 1 AND IsSuccess = 0"))
                .ShouldBe(1);
            await WaitForVisibleWatermarkAsync(transaction);
        }
    }

    // F10, §8.4 step 4: conflict rows get no extension update; updated rows have their extension columns restored.
    [Fact]
    public async Task GivenOneConflictingAndOneUpdatedResource_WhenReindexUpdatesExtensions_ThenOnlyTheUpdatedResourceIsProcessed()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);
        var searchIndices = BuildSearchIndicesWithExtensions("reindex-target");
        await PutAsync(Patient("conflicted-input") with { SearchIndices = searchIndices }, Patient("updated-input") with { SearchIndices = searchIndices });
        var (_, cutoff) = await _store.RaiseBarrierAsync(65, CancellationToken.None);
        var stale = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None))
            .Select(resource => resource with { Resource = resource.Resource with { SearchIndices = searchIndices } })
            .ToArray();
        var conflicted = stale.Single(resource => resource.Resource.ResourceId == "conflicted-input").ResourceSurrogateId;
        var updated = stale.Single(resource => resource.Resource.ResourceId == "updated-input").ResourceSurrogateId;
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, conflicted, CancellationToken.None);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, conflicted, CancellationToken.None);
        await _database.ExecuteNonQueryAsync(
            $"""
             UPDATE dbo.TokenSearchParam SET IdentifierTypeCode = NULL WHERE ResourceSurrogateId = {updated};
             UPDATE dbo.UriSearchParam SET Version = NULL, Fragment = NULL WHERE ResourceSurrogateId = {updated};
             """);
        var logger = new RecordingLogger<SqlServerPostMergeExtensionUpdater>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (store, cache) = await CreateStoreAsync(
            new InterceptingSqlExecutionService(_database.SqlExecutionService, async command =>
            {
                if (command.CommandText.Contains("EXEC dbo.UpdateResourceSearchParams", StringComparison.Ordinal))
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            }),
            logger);
        using (cache)
        {
            var update = store.UpdateSearchIndicesAsync(stale, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await PutAsync(Patient("conflicted-input") with { VersionId = "2", DefinitionsEventId = 65 });
            release.SetResult();

            (await update).ShouldBe(new SearchIndexUpdateResult(1, 1));
            await SearchIndexTableSeeder.AssertEverySearchIndexTableIsEmptyAsync(_database, conflicted, CancellationToken.None);
        }

        logger.Errors.ShouldBeEmpty();
        (await _database.ExecuteScalarAsync<string>(
            $"SELECT CONCAT((SELECT TOP (1) IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = {updated}), '|', (SELECT TOP (1) Version FROM dbo.UriSearchParam WHERE ResourceSurrogateId = {updated}))"))
            .ShouldBe("MR|1.0");
    }

    // F9, N3: a repeated index-only write changes no version, raw resource, transaction or history.
    [Fact]
    public async Task GivenACurrentResource_WhenIndexOnlyUpdateIsRepeated_ThenVersionRawResourceTransactionAndHistoryStayUnchanged()
    {
        var resource = await PutAndReadAsync(Patient("idempotent"));
        const string snapshot = """
            SELECT CONCAT(Version, '|', TransactionId, '|', CONVERT(varchar(max), RawResource, 1), '|', IsHistory, '|',
                (SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 1))
            FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0
            """;
        var before = await _database.ExecuteScalarAsync<string>(snapshot);

        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe(new SearchIndexUpdateResult(1, 0));
        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe(new SearchIndexUpdateResult(1, 0));

        (await _database.ExecuteScalarAsync<string>(snapshot)).ShouldBe(before);
    }

    // §8.7 UpdateResourceSearchParams: the index-only write covers every typed table and the extension columns.
    [Fact]
    public async Task GivenDriftInEveryTypedSearchIndexTable_WhenIndexOnlyUpdateRuns_ThenAllRowsAndExtensionColumnsAreRestored()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);
        await PutAsync(Patient("reindex-target"));
        var searchIndices = BuildSearchIndicesWithExtensions("reindex-target");
        var read = await PutAndReadAsync(Patient("reindex-all-types") with { SearchIndices = searchIndices });
        var indexed = read with { Resource = read.Resource with { SearchIndices = searchIndices } };
        var surrogateId = read.ResourceSurrogateId;
        var expectedRowCounts = new Dictionary<string, int>();
        foreach (var table in SearchIndexTableSeeder.SearchIndexTables.Where(table => table != "ResourceWriteClaim"))
        {
            expectedRowCounts[table] = await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {surrogateId}");
            expectedRowCounts[table].ShouldBeGreaterThan(0, $"dbo.{table} must have a row before drift is introduced.");
            await _database.ExecuteNonQueryAsync($"DELETE FROM dbo.{table} WHERE ResourceSurrogateId = {surrogateId}");
        }

        (await _store.UpdateSearchIndicesAsync([indexed], CancellationToken.None)).ShouldBe(new SearchIndexUpdateResult(1, 0));

        foreach (var (table, expectedCount) in expectedRowCounts)
        {
            (await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {surrogateId}"))
                .ShouldBe(expectedCount, $"dbo.{table} must be restored to its pre-drift row count.");
        }

        (await _database.ExecuteScalarAsync<string>(
            $"""
             SELECT CONCAT(system.Value COLLATE DATABASE_DEFAULT, '|', token.IdentifierTypeCode COLLATE DATABASE_DEFAULT, '|',
                 uri.Version COLLATE DATABASE_DEFAULT, '|', uri.Fragment COLLATE DATABASE_DEFAULT)
             FROM (SELECT TOP (1) IdentifierTypeSystemId, IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = {surrogateId}) AS token
             JOIN dbo.System AS system ON system.SystemId = token.IdentifierTypeSystemId
             CROSS JOIN (SELECT TOP (1) Version, Fragment FROM dbo.UriSearchParam WHERE ResourceSurrogateId = {surrogateId}) AS uri
             """))
            .ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203|MR|1.0|fragment");
    }

    private static IReadOnlyList<object> BuildSearchIndicesWithExtensions(string referenceTargetId) =>
        SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(referenceTargetId)
            .Cast<SearchIndexEntry>()
            .Select(entry => entry.SearchParameter.Type switch
            {
                SearchParamType.Token => new SearchIndexEntry(entry.SearchParameter, new TokenSearchValue(
                    system: null, code: "sweep-code", text: "sweep text",
                    identifierTypeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203", identifierTypeCode: "MR")),
                SearchParamType.Uri => new SearchIndexEntry(entry.SearchParameter,
                    new UriSearchValue("http://example.org/sweep-uri|1.0#fragment", separateCanonicalComponents: true)),
                _ => entry,
            })
            .Cast<object>()
            .ToArray();

    private static ReindexParameterDefinition Target(string canonical, int conformanceId) =>
        new(canonical, "code", "Patient", conformanceId, ActivationEventId: 1, ["Patient"]);

    private async Task PutAsync(params ResourceWrapper[] resources)
    {
        foreach (var resource in resources)
        {
            await _database.Repository.CreateOrUpdateAsync(resource);
        }
    }

    /// <summary>Writes <paramref name="resource"/>, raises the barrier and reads it back as a reindex worker would.</summary>
    private async Task<ReindexResource> PutAndReadAsync(ResourceWrapper resource)
    {
        await PutAsync(resource);
        var (_, cutoff) = await _store.RaiseBarrierAsync(BarrierEventId, CancellationToken.None);
        return (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None))
            .Single(read => read.Resource.ResourceId == resource.ResourceId);
    }

    /// <summary>Plans every range of <paramref name="resourceType"/> up to <paramref name="cutoff"/> and reads each in pages of one.</summary>
    private static async Task<List<ReindexResource>> ReadAllAsync(IReindexStore store, string resourceType, long cutoff)
    {
        var resources = new List<ReindexResource>();
        var (ranges, _) = await store.GetSurrogateIdRangesAsync(resourceType, -1, cutoff, 1, 10, CancellationToken.None);
        foreach (var (start, end, _) in ranges)
        {
            for (var page = await store.ReadRangeAsync(resourceType, start, end, 1, null, CancellationToken.None);
                 page.Count > 0;
                 page = await store.ReadRangeAsync(resourceType, start, end, 1, page[^1].ResourceSurrogateId, CancellationToken.None))
            {
                resources.AddRange(page);
            }
        }

        return resources;
    }

    private Task InsertTransactionsAsync(string rows, bool values = true) =>
        _database.ExecuteNonQueryAsync(
            $"INSERT dbo.Transactions (SurrogateIdRangeFirstValue, SurrogateIdRangeLastValue, IsCompleted, IsVisible) {(values ? "VALUES " : string.Empty)}{rows}");

    private async Task ConfigureReadCommittedSnapshotAsync(bool enabled)
    {
        var builder = new SqlConnectionStringBuilder(_database.ConnectionString);
        var databaseName = builder.InitialCatalog;
        databaseName.ShouldStartWith("IgnixaDataLayerSqlServerTest_", Case.Sensitive);
        using var pooled = new SqlConnection(_database.ConnectionString);
        SqlConnection.ClearPool(pooled);
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText = $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT {(enabled ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
        SqlConnection.ClearPool(pooled);

        (await _database.ExecuteScalarAsync<int>(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()")).ShouldBe(enabled ? 1 : 0);
    }

    private async Task WaitForVisibleWatermarkAsync(long transactionId)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (await _store.GetVisibleWatermarkAsync(CancellationToken.None) < transactionId)
        {
            if (DateTimeOffset.UtcNow > timeout)
            {
                throw new TimeoutException($"Visible watermark did not advance past failed transaction {transactionId}.");
            }

            await Task.Delay(25);
        }
    }

    /// <summary>A store whose commands run through <paramref name="sql"/>; dispose the returned cache.</summary>
    private async Task<(IReindexStore Store, SqlServerSearchIndexReferenceDataCache Cache)> CreateStoreAsync(
        ISqlExecutionService sql,
        ILogger<SqlServerPostMergeExtensionUpdater>? extensionLogger = null)
    {
        var cache = new SqlServerSearchIndexReferenceDataCache(sql, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.PreloadResourceTypesAsync(CancellationToken.None);
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            sql, _database.TenantId, extensionLogger ?? NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        var store = new SqlServerReindexStore(
            sql, _database.TenantId, new GzipResourceCompressor(new RecyclableMemoryStreamManager()), cache, extensionUpdater, NullLogger.Instance);
        return (store, cache);
    }

    /// <summary>Runs <paramref name="command"/> with <c>STATISTICS XML</c> on and returns its single actual plan.</summary>
    private async Task<string> CaptureShowPlanXmlAsync(SqlCommand command)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using (var enable = new SqlCommand("SET STATISTICS XML ON;", connection))
        {
            await enable.ExecuteNonQueryAsync();
        }

        command.Connection = connection;
        var plans = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.FieldCount == 1)
                {
                    plans.Add(reader.GetString(0));
                }
            }
        }
        while (await reader.NextResultAsync());

        return plans.ShouldHaveSingleItem();
    }

    private static ResourceWrapper Patient(string id) => Resource("Patient", id);

    private static ResourceWrapper Resource(string type, string id) => new(
        type, id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"{{type}}","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"{type}/{id}"));
}
