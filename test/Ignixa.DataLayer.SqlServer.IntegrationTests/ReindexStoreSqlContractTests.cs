using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
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
    public async Task GivenCurrentDeletedAndHistoryRows_WhenRangesAndPagesAreRead_ThenOnlyCurrentNonDeletedRowsAtTheCutoffAreReturned()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("first"));
        await _database.Repository.CreateOrUpdateAsync(Patient("updated"));
        await _database.Repository.CreateOrUpdateAsync(Patient("updated") with { VersionId = "2" });
        await _database.Repository.CreateOrUpdateAsync(Patient("deleted"));
        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "deleted"),
            new ResourceRequest("DELETE", "Patient/deleted"));

        var (_, cutoff) = await _store.RaiseBarrierAsync(50, CancellationToken.None);
        var ranges = await _store.GetSurrogateIdRangesAsync("Patient", cutoff, 1, CancellationToken.None);
        var resources = new List<ReindexResource>();

        foreach (var (start, end) in ranges)
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

        var ranges = await _store.GetSurrogateIdRangesAsync("Patient", cutoffSurrogateId, 1000, CancellationToken.None);
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

    private static ResourceWrapper Patient(string id) => new(
        "Patient",
        id,
        "1",
        DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));
}
