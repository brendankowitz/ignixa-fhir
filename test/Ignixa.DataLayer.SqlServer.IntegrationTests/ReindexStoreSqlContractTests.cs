using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class ReindexStoreSqlContractTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private IReindexStore _store = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _store = _database.ReindexStore;
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public void GivenTheSqlRepository_WhenInspectingWriteOverloads_ThenNoZeroStampCompatibilityOverloadExists()
    {
        var methods = typeof(SqlServerFhirRepository).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        methods.Count(method =>
                method.Name == nameof(IFhirRepository.GetNextTransactionIdAsync) &&
                method.GetParameters().All(parameter => parameter.ParameterType != typeof(long)))
            .ShouldBe(0);
        methods.Count(method =>
                method.Name == nameof(IFhirRepository.DeleteAsync) &&
                method.GetParameters().All(parameter => parameter.ParameterType != typeof(long)))
            .ShouldBe(0);
    }

    [Fact]
    public async Task GivenExistingTransactions_WhenBarrierIsRaisedMonotonically_ThenItReturnsThePostBarrierCutoff()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("before-barrier"));

        var first = await _store.RaiseBarrierAsync(42, CancellationToken.None);
        var second = await _store.RaiseBarrierAsync(17, CancellationToken.None);

        first.TransactionId.ShouldBeGreaterThan(0);
        first.SurrogateId.ShouldBeGreaterThanOrEqualTo(first.TransactionId);
        second.ShouldBe(first);
        (await _database.ExecuteScalarAsync<long>(
            "SELECT Bigint FROM dbo.Parameters WHERE Id = 'Conformance.MinAcceptedDefinitionsEventId'"))
            .ShouldBe(42);
    }

    [Fact]
    public async Task GivenNoTransactionsOrResources_WhenBarrierIsRaised_ThenBothCutoffsUseTheEmptySentinel()
    {
        var cutoff = await _store.RaiseBarrierAsync(42, CancellationToken.None);

        cutoff.ShouldBe((-1, -1));
    }

    [Fact]
    public async Task GivenSearchParameterCatalog_WhenPhysicalIdIsChecked_ThenPresenceIsReported()
    {
        const string provisionedCanonical = "http://example.org/SearchParameter/provisioned";
        await _database.ExecuteNonQueryAsync(
            $"""
             INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported)
             VALUES ('{provisionedCanonical}', 'Supported', SYSUTCDATETIME(), 0);
             """);

        var searchParamId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{provisionedCanonical}'");
        (await _store.HasSearchParameterAsync(
            searchParamId,
            CancellationToken.None)).ShouldBeTrue();
        (await _store.HasSearchParameterAsync(
            searchParamId + 1,
            CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenLegacyCurrentResourcesWithoutTransactions_WhenBarrierIsRaised_ThenTheSurrogateCutoffIncludesThem()
    {
        await InsertLegacyCurrentResourceAsync("legacy-only", surrogateId: 999_999);

        var cutoff = await _store.RaiseBarrierAsync(42, CancellationToken.None);

        cutoff.ShouldBe((-1, 999_999));
    }

    [Fact]
    public async Task GivenTransactionsAndLegacyCurrentResources_WhenBarrierIsRaised_ThenTheSurrogateCutoffUsesTheGreaterValue()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("transactional"));
        var transactionCutoff = await _database.ExecuteScalarAsync<long>(
            "SELECT MAX(SurrogateIdRangeLastValue) FROM dbo.Transactions");
        await InsertLegacyCurrentResourceAsync("legacy-mixed", transactionCutoff + 1_000);

        var cutoff = await _store.RaiseBarrierAsync(42, CancellationToken.None);

        cutoff.TransactionId.ShouldBeGreaterThan(0);
        cutoff.SurrogateId.ShouldBe(transactionCutoff + 1_000);
    }

    [Fact]
    public async Task GivenCurrentDeletedAndHistoryRows_WhenRangesAndPagesAreRead_ThenOnlyCurrentNonDeletedRowsAtTheCutoffAreReturned()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("first"));
        await _database.Repository.CreateOrUpdateAsync(Patient("updated"));
        await _database.Repository.CreateOrUpdateAsync(Patient("updated") with { VersionId = "2" });
        await _database.Repository.CreateOrUpdateAsync(Patient("deleted"));
        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "deleted"),
            new ResourceRequest("DELETE", "Patient/deleted"),
            definitionsEventId: 0);

        var (_, cutoff) = await _store.RaiseBarrierAsync(50, CancellationToken.None);
        var (ranges, _) = await _store.GetSurrogateIdRangesAsync(
            "Patient", -1, cutoff, 1, 10, CancellationToken.None);
        var resources = new List<ReindexResource>();

        ranges.Sum(range => range.ResourceCount).ShouldBe(2);
        foreach (var (start, end, _) in ranges)
        {
            var after = (long?)null;
            while (true)
            {
                var page = await _store.ReadRangeAsync("Patient", start, end, 1, after, CancellationToken.None);
                if (page.Count == 0)
                {
                    break;
                }

                resources.AddRange(page);
                after = page[^1].ResourceSurrogateId;
            }
        }

        resources.Select(resource => resource.Resource.ResourceId).ShouldBe(["first", "updated"], ignoreOrder: true);
        resources.Select(resource => resource.Resource.VersionId).ShouldContain("2");
        resources.ShouldAllBe(resource => resource.ResourceSurrogateId <= cutoff);
    }

    [Fact]
    public async Task GivenSparseCurrentResourceIds_WhenRangesArePlanned_ThenTheyAreContiguousAndExhaustiveToTheUpperBound()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("sparse-first"));
        await _database.Repository.CreateOrUpdateAsync(Patient("sparse-middle"));
        await _database.Repository.CreateOrUpdateAsync(Patient("sparse-last"));
        var firstId = await GetCurrentResourceSurrogateIdAsync("sparse-first");
        var middleId = await GetCurrentResourceSurrogateIdAsync("sparse-middle");
        var lastId = await GetCurrentResourceSurrogateIdAsync("sparse-last");
        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.Resource SET ResourceSurrogateId = {middleId + 1_000} WHERE ResourceId = 'sparse-middle' AND IsHistory = 0");
        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.Resource SET ResourceSurrogateId = {lastId + 2_000} WHERE ResourceId = 'sparse-last' AND IsHistory = 0");
        var upperBound = lastId + 2_050;

        var (ranges, _) = await _store.GetSurrogateIdRangesAsync(
            "Patient", -1, upperBound, 1, 10, CancellationToken.None);

        ranges.Count.ShouldBe(3);
        ranges[0].Start.ShouldBe(firstId);
        ranges[^1].End.ShouldBe(upperBound);
        for (var index = 1; index < ranges.Count; index++)
        {
            ranges[index].Start.ShouldBe(ranges[index - 1].End + 1);
        }
    }

    [Fact]
    public async Task GivenNoCurrentResources_WhenRangesArePlanned_ThenThePageIsEmptyAndHasNoContinuation()
    {
        var page = await _store.GetSurrogateIdRangesAsync(
            "Patient", -1, long.MaxValue, 100, 10, CancellationToken.None);

        page.Ranges.ShouldBeEmpty();
        page.NextStartAfter.ShouldBeNull();
    }

    [Fact]
    public async Task GivenMoreRangesThanFitInOnePage_WhenRangesArePlanned_ThenPagesRemainContiguous()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("paged-first"));
        await _database.Repository.CreateOrUpdateAsync(Patient("paged-second"));
        await _database.Repository.CreateOrUpdateAsync(Patient("paged-third"));
        var upperBound = await GetCurrentResourceSurrogateIdAsync("paged-third");

        var firstPage = await _store.GetSurrogateIdRangesAsync(
            "Patient", -1, upperBound, 1, 2, CancellationToken.None);
        var secondPage = await _store.GetSurrogateIdRangesAsync(
            "Patient", firstPage.NextStartAfter!.Value, upperBound, 1, 2, CancellationToken.None);

        firstPage.Ranges.Count.ShouldBe(2);
        firstPage.NextStartAfter.ShouldBe(firstPage.Ranges[^1].End);
        secondPage.Ranges.ShouldHaveSingleItem();
        secondPage.Ranges[0].Start.ShouldBe(firstPage.Ranges[^1].End + 1);
        secondPage.Ranges[^1].End.ShouldBe(upperBound);
        secondPage.NextStartAfter.ShouldBeNull();
    }

    [Fact]
    public async Task GivenAnImportReservationWhoseResourceFallsPastItsFirstValue_WhenBarrierIsRaised_ThenTheCutoffRangeIncludesTheResource()
    {
        var (transactionId, _) = await _database.MergeRepository.BeginTransactionAsync(
            resourceCount: 1000,
            definitionsEventId: 0,
            CancellationToken.None);
        var resource = Patient("import-tail");

        await _database.MergeRepository.MergeResourcesAsync(
            transactionId,
            singleTransaction: true,
            [resource],
            [999],
            CancellationToken.None);
        await _database.MergeRepository.CommitTransactionAsync(transactionId, null, CancellationToken.None);

        var (cutoffTransactionId, cutoffSurrogateId) = await _store.RaiseBarrierAsync(55, CancellationToken.None);
        var resourceSurrogateId = await _database.ExecuteScalarAsync<long>(
            "SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = 'import-tail' AND IsHistory = 0");

        transactionId.ShouldBeLessThanOrEqualTo(cutoffTransactionId);
        resourceSurrogateId.ShouldBeGreaterThan(cutoffTransactionId);
        cutoffSurrogateId.ShouldBeGreaterThanOrEqualTo(resourceSurrogateId);

        var (ranges, _) = await _store.GetSurrogateIdRangesAsync(
            "Patient", -1, cutoffSurrogateId, 1000, 10, CancellationToken.None);
        var range = ranges.Single();
        var resources = await _store.ReadRangeAsync(
            "Patient", range.Start, range.End, 1000, afterSurrogateId: null, CancellationToken.None);

        resources.Select(reindexResource => reindexResource.Resource.ResourceId).ShouldContain("import-tail");
    }

    [Fact]
    public async Task GivenAResourceSupersededAfterRead_WhenItsIndicesAreUpdated_ThenItIsReportedAsAConflictWithoutCreatingHistory()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("conflict"));
        var (_, cutoff) = await _store.RaiseBarrierAsync(60, CancellationToken.None);
        var resource = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None)).Single();
        var beforeHistory = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'conflict' AND IsHistory = 1");

        await _database.Repository.CreateOrUpdateAsync(
            Patient("conflict") with { VersionId = "2", DefinitionsEventId = 60 });

        var result = await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None);

        result.Updated.ShouldBe(0);
        result.Conflicts.ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'conflict' AND IsHistory = 1"))
            .ShouldBe(beforeHistory + 1);
    }

    [Fact]
    public async Task GivenExistingResourceWriteClaims_WhenSearchIndicesAreRebuilt_ThenTheClaimsArePreservedExactly()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("claims"));
        var (_, cutoff) = await _store.RaiseBarrierAsync(70, CancellationToken.None);
        var resource = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None)).Single();
        await _database.ExecuteNonQueryAsync(
            $"""
             INSERT dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
             VALUES ({resource.ResourceSurrogateId}, 1, N'alpha'),
                    ({resource.ResourceSurrogateId}, 2, N'beta');
             """);

        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe((1, 0));

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {resource.ResourceSurrogateId}"))
            .ShouldBe(2);
        (await _database.ExecuteScalarAsync<string>(
            $"SELECT ClaimValue FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {resource.ResourceSurrogateId} AND ClaimTypeId = 1"))
            .ShouldBe("alpha");
        (await _database.ExecuteScalarAsync<string>(
            $"SELECT ClaimValue FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {resource.ResourceSurrogateId} AND ClaimTypeId = 2"))
            .ShouldBe("beta");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenConcurrentBarrierRaisesAndStaleAllocations_WhenRacing_ThenEveryAllocationIsCoveredOrRejectedAndVisibilityAdvances(
        bool readCommittedSnapshot)
    {
        const int iterations = 30;
        await ConfigureReadCommittedSnapshotAsync(readCommittedSnapshot);

        for (var iteration = 1; iteration <= iterations; iteration++)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allocationTask = Task.Run(async () =>
            {
                await start.Task;
                if (iteration % 3 == 0)
                {
                    await Task.Delay(1);
                }

                try
                {
                    return (
                        Allocation: ((long TransactionId, int SequenceStart)?)await _database.MergeRepository.BeginTransactionAsync(
                        resourceCount: 1,
                        definitionsEventId: iteration - 1,
                        CancellationToken.None),
                        RejectedTransactionId: (long?)null);
                }
                catch (StaleConformanceDefinitionsException exception)
                {
                    return (
                        Allocation: ((long TransactionId, int SequenceStart)?)null,
                        RejectedTransactionId: exception.TransactionId);
                }
            });
            var raiseTask = Task.Run(async () =>
            {
                await start.Task;
                if (iteration % 3 == 1)
                {
                    await Task.Delay(1);
                }

                return await _store.RaiseBarrierAsync(iteration, CancellationToken.None);
            });

            start.SetResult();
            var allocation = await allocationTask;
            var cutoff = await raiseTask;

            if (allocation.Allocation.HasValue)
            {
                allocation.Allocation.Value.TransactionId.ShouldBeLessThanOrEqualTo(cutoff.TransactionId);
                await _database.MergeRepository.CommitTransactionAsync(
                    allocation.Allocation.Value.TransactionId,
                    failureReason: "Reindex barrier race test cleanup.",
                    CancellationToken.None);
            }
            else
            {
                var transaction = allocation.RejectedTransactionId!.Value;
                (await _database.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue = {transaction} AND IsCompleted = 1 AND IsSuccess = 0"))
                    .ShouldBe(1);
                await WaitForVisibleWatermarkAsync(transaction);
            }
        }
    }

    [Fact]
    public async Task GivenAResourceSupersededAfterClaimsAndIndexesAreRead_WhenTheReindexProcedureRuns_ThenNoStaleRowsAreRecreated()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);
        var indexed = Patient("conflicted-input") with
        {
            SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable("conflicted-input"),
        };
        await _database.Repository.CreateOrUpdateAsync(indexed);

        var (_, cutoff) = await _store.RaiseBarrierAsync(65, CancellationToken.None);
        var read = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None))
            .Single(resource => resource.Resource.ResourceId == indexed.ResourceId);
        var stale = read with { Resource = read.Resource with { SearchIndices = indexed.SearchIndices } };
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(
            _database, stale.ResourceSurrogateId, CancellationToken.None);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(
            _database, stale.ResourceSurrogateId, CancellationToken.None);

        var (gate, store, cache) = await CreateGatedReindexStoreAsync();
        using (cache)
        {
            var update = store.UpdateSearchIndicesAsync([stale], CancellationToken.None);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await _database.Repository.CreateOrUpdateAsync(
                Patient(indexed.ResourceId) with { VersionId = "2", DefinitionsEventId = 65 });
            gate.Release.SetResult();

            (await update).ShouldBe((0, 1));
            await SearchIndexTableSeeder.AssertEverySearchIndexTableIsEmptyAsync(
                _database, stale.ResourceSurrogateId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenACurrentResource_WhenIndexOnlyUpdateIsRepeated_ThenVersionRawResourceTransactionAndHistoryStayUnchanged()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("idempotent"));
        var (_, cutoff) = await _store.RaiseBarrierAsync(70, CancellationToken.None);
        var resource = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None)).Single();
        var version = await _database.ExecuteScalarAsync<int>(
            "SELECT Version FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var transactionId = await _database.ExecuteScalarAsync<long>(
            "SELECT TransactionId FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var rawResource = await _database.ExecuteScalarBytesAsync(
            "SELECT RawResource FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var isHistory = await _database.ExecuteScalarAsync<bool>(
            "SELECT IsHistory FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var historyCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 1");

        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe((1, 0));
        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe((1, 0));

        (await _database.ExecuteScalarAsync<int>(
            "SELECT Version FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(version);
        (await _database.ExecuteScalarAsync<long>(
            "SELECT TransactionId FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(transactionId);
        (await _database.ExecuteScalarBytesAsync(
            "SELECT RawResource FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(rawResource);
        (await _database.ExecuteScalarAsync<bool>(
            "SELECT IsHistory FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(isHistory);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 1"))
            .ShouldBe(historyCount);
    }

    [Fact]
    public async Task GivenDriftInEveryTypedSearchIndexTable_WhenIndexOnlyUpdateRuns_ThenAllRowsAndExtensionColumnsAreRestored()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);
        await _database.Repository.CreateOrUpdateAsync(Patient("reindex-target"));

        var resource = Patient("reindex-all-types") with
        {
            SearchIndices = BuildSearchIndicesWithExtensions("reindex-target"),
        };
        await _database.Repository.CreateOrUpdateAsync(resource);

        var (_, cutoff) = await _store.RaiseBarrierAsync(80, CancellationToken.None);
        var reindexResource = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None))
            .Single(reindexResource => reindexResource.Resource.ResourceId == resource.ResourceId);
        var indexedResource = reindexResource with
        {
            Resource = reindexResource.Resource with { SearchIndices = resource.SearchIndices },
        };
        var resourceSurrogateId = reindexResource.ResourceSurrogateId;
        var expectedRowCounts = new Dictionary<string, int>();

        foreach (var table in SearchIndexTableSeeder.SearchIndexTables.Where(table => table != "ResourceWriteClaim"))
        {
            var expectedCount = await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {resourceSurrogateId}");
            expectedCount.ShouldBeGreaterThan(0, $"dbo.{table} must have a row before drift is introduced.");
            expectedRowCounts.Add(table, expectedCount);
            await _database.ExecuteNonQueryAsync(
                $"DELETE FROM dbo.{table} WHERE ResourceSurrogateId = {resourceSurrogateId}");
        }

        (await _store.UpdateSearchIndicesAsync([indexedResource], CancellationToken.None)).ShouldBe((1, 0));

        foreach (var (table, expectedCount) in expectedRowCounts)
        {
            (await _database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.{table} WHERE ResourceSurrogateId = {resourceSurrogateId}"))
                .ShouldBe(expectedCount, $"dbo.{table} must be restored to its pre-drift row count.");
        }

        var identifierTypeSystemId = await _database.ExecuteScalarAsync<int>(
            $"SELECT TOP (1) IdentifierTypeSystemId FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = {resourceSurrogateId} AND IdentifierTypeCode = 'MR'");
        var expectedIdentifierTypeSystemId = await _database.ExecuteScalarAsync<int>(
            "SELECT SystemId FROM dbo.System WHERE Value = 'http://terminology.hl7.org/CodeSystem/v2-0203'");
        identifierTypeSystemId.ShouldBe(expectedIdentifierTypeSystemId);
        (await _database.ExecuteScalarAsync<string>(
            $"SELECT TOP (1) IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = {resourceSurrogateId}"))
            .ShouldBe("MR");
        (await _database.ExecuteScalarAsync<string>(
            $"SELECT TOP (1) Version FROM dbo.UriSearchParam WHERE ResourceSurrogateId = {resourceSurrogateId}"))
            .ShouldBe("1.0");
        (await _database.ExecuteScalarAsync<string>(
            $"SELECT TOP (1) Fragment FROM dbo.UriSearchParam WHERE ResourceSurrogateId = {resourceSurrogateId}"))
            .ShouldBe("fragment");
    }

    private static IReadOnlyList<object> BuildSearchIndicesWithExtensions(string referenceTargetId) =>
        SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(referenceTargetId)
            .Cast<SearchIndexEntry>()
            .Select(entry => entry.SearchParameter.Type switch
            {
                SearchParamType.Token => new SearchIndexEntry(
                    entry.SearchParameter,
                    new TokenSearchValue(
                        system: null,
                        code: "sweep-code",
                        text: "sweep text",
                        identifierTypeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                        identifierTypeCode: "MR")),
                SearchParamType.Uri => new SearchIndexEntry(
                    entry.SearchParameter,
                    new UriSearchValue("http://example.org/sweep-uri|1.0#fragment", separateCanonicalComponents: true)),
                _ => entry,
            })
            .Cast<object>()
            .ToArray();

    private async Task<long> GetCurrentResourceSurrogateIdAsync(string resourceId) =>
        await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 0");

    private async Task InsertLegacyCurrentResourceAsync(string resourceId, long surrogateId)
    {
        await _database.ExecuteNonQueryAsync(
            $"""
             INSERT dbo.Resource (
                 ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted,
                 RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId, HistoryTransactionId)
             VALUES (1, '{resourceId}', 1, 0, {surrogateId}, 0, 'PUT', 0x01, 0, NULL, NULL, NULL);
             """);
    }

    private async Task ConfigureReadCommittedSnapshotAsync(bool enabled)
    {
        var builder = new SqlConnectionStringBuilder(_database.ConnectionString);
        var databaseName = builder.InitialCatalog;
        databaseName.ShouldStartWith("IgnixaDataLayerSqlServerTest_", Case.Sensitive);
        using var pooledConnection = new SqlConnection(builder.ConnectionString);
        SqlConnection.ClearPool(pooledConnection);
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100
        command.CommandText =
            $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT {(enabled ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE";
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
        SqlConnection.ClearPool(pooledConnection);

        (await _database.ExecuteScalarAsync<int>(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE database_id = DB_ID()"))
            .ShouldBe(enabled ? 1 : 0);
    }

    private async Task WaitForVisibleWatermarkAsync(long transactionId)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (await _store.GetVisibleWatermarkAsync(CancellationToken.None) >= transactionId)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Visible watermark did not advance past failed transaction {transactionId}.");
    }

    private async Task<(UpdateResourceSearchParamsGate Gate, IReindexStore Store, SqlServerSearchIndexReferenceDataCache Cache)> CreateGatedReindexStoreAsync()
    {
        var gate = new UpdateResourceSearchParamsGate(_database.SqlExecutionService);
        var cache = new SqlServerSearchIndexReferenceDataCache(
            gate, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.PreloadResourceTypesAsync(CancellationToken.None);
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            gate, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        return (gate, new SqlServerReindexStore(
            gate,
            _database.TenantId,
            compressor,
            cache,
            extensionUpdater,
            NullLogger.Instance), cache);
    }

    private sealed class UpdateResourceSearchParamsGate(ISqlExecutionService inner) : ISqlExecutionService
    {
        private readonly ISqlExecutionService _inner = inner;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            if (command.CommandText.Contains("EXEC dbo.UpdateResourceSearchParams", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return await _inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
        }

        public Task<IReadOnlyList<T>> ExecuteReaderAsync<T>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, T> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            _inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);

        public Task<T> ExecuteInTransactionAsync<T>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken) =>
            _inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken) =>
            _inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }

    private static ResourceWrapper Patient(string id) => new(
        "Patient",
        id,
        "1",
        DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));
}
