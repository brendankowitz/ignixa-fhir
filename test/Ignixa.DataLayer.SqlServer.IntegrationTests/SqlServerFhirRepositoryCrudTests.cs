using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerFhirRepositoryCrudTests : IAsyncLifetime
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
            new ResourceKey("Patient", "patient-crud-3"), new ResourceRequest("DELETE", "Patient/patient-crud-3"), null, CancellationToken.None);

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
        var firstDelete = await _repository.DeleteAsync(key, new ResourceRequest("DELETE", "Patient/patient-crud-4"), null, CancellationToken.None);
        var secondDelete = await _repository.DeleteAsync(key, new ResourceRequest("DELETE", "Patient/patient-crud-4"), null, CancellationToken.None);

        secondDelete!.VersionId.ShouldBe(firstDelete!.VersionId);
    }

    /// <summary>
    /// Pins this port's own intended semantics for the transactionId != null path of DeleteAsync --
    /// see the explanatory comment on SqlServerFhirRepository.DeleteAsync for the full rationale.
    /// Legacy SqlEntityFrameworkRepository.DeleteAsync has a latent bug on this exact parameter path
    /// (its EF-tracked history-flip and tombstone insert are only persisted as an incidental side
    /// effect of UpsertResourceTtlAsync's own conditional SaveChangesAsync call, while the raw-SQL
    /// search-index wipe always executes immediately), so this is deliberately NOT a differential
    /// test against legacy -- it proves the new port's own correct, consistent behavior directly: all
    /// three effects (tombstone, history flip, search-index wipe) are immediately and durably
    /// persisted, with no CommitTransactionAsync call on the passed-in transactionId anywhere in this
    /// test.
    /// </summary>
    [Fact]
    public async Task GivenAnExistingResourceWithASearchIndexEntry_WhenDeleteAsyncCalledWithANonNullTransactionId_ThenTombstoneHistoryFlipAndSearchIndexWipeAreAllImmediatelyPersisted()
    {
        const string SearchParamUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier-crud-delete-tx";
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES ('{SearchParamUrl}', 'active', SYSDATETIMEOFFSET(), 0)");

        var searchParameter = new SearchParameterInfo(
            "identifier", "identifier", SearchParamType.Token, new Uri(SearchParamUrl));
        // system is deliberately null here -- a non-null system requires a pre-seeded System cache
        // entry to resolve a SystemId (TokenSearchParameterRowGenerator silently skips the record
        // otherwise, see RowGenerators/TokenSearchParameterRowGenerator.cs), which this test doesn't
        // need: a code-only token is sufficient to prove the TokenSearchParam wipe (same reasoning
        // SqlServerMergeRepositoryTests already established for its identifier-extension test).
        var tokenValue = new TokenSearchValue(system: null, code: "delete-tx-12345", text: null);

        var resourceId = "patient-crud-delete-tx-1";
        var resource = new ResourceWrapper("Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"))
        {
            SearchIndices = [new SearchIndexEntry(searchParameter, tokenValue)]
        };

        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var originalSurrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 0");
        var tokenRowCountBeforeDelete = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = {originalSurrogateId}");
        tokenRowCountBeforeDelete.ShouldBeGreaterThan(0, "sanity check: the create must have actually indexed a TokenSearchParam row.");

        // A real, non-null TransactionId -- deliberately never committed via CommitTransactionAsync
        // anywhere in this test, to prove DeleteAsync's own writes don't depend on that commit.
        var transactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);

        var deletedKey = await _repository.DeleteAsync(
            new ResourceKey("Patient", resourceId), new ResourceRequest("DELETE", $"Patient/{resourceId}"), transactionId, CancellationToken.None);

        deletedKey.ShouldNotBeNull();
        deletedKey!.VersionId.ShouldBe("2");

        // Tombstone: immediately visible via GetAsync, with the transactionId stamped on it.
        var fetched = await _repository.GetAsync(new ResourceKey("Patient", resourceId), CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.IsDeleted.ShouldBeTrue();
        fetched.VersionId.ShouldBe("2");

        var tombstoneTransactionId = await _database.ExecuteScalarAsync<long>(
            $"SELECT TransactionId FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 0");
        tombstoneTransactionId.ShouldBe(transactionId.Value);

        // History flip: the original version-1 row is now IsHistory = 1, stamped with this
        // transactionId as its HistoryTransactionId.
        var historyRowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{resourceId}' AND IsHistory = 1 AND Version = 1 AND HistoryTransactionId = {transactionId.Value}");
        historyRowCount.ShouldBe(1);

        // Search-index wipe: TokenSearchParam rows for the original (now-superseded) surrogate ID are
        // gone.
        var tokenRowCountAfterDelete = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = {originalSurrogateId}");
        tokenRowCountAfterDelete.ShouldBe(0);
    }

    [Fact]
    public async Task GivenAResourceThatNeverExisted_WhenDeleteAsyncCalled_ThenReturnsNull()
    {
        var result = await _repository.DeleteAsync(
            new ResourceKey("Patient", "never-existed"), new ResourceRequest("DELETE", "Patient/never-existed"), null, CancellationToken.None);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenTwoCallsToGetNextTransactionIdAsync_WhenBothReturn_ThenTheyAreDifferentValues()
    {
        var first = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var second = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        first.ShouldNotBe(second);
    }

    [Fact]
    public async Task GivenANeverBeforeSeenResourceType_WhenGetOrCreateResourceTypeIdAsyncCalledTwice_ThenOnlyOneRowIsInsertedIntoDboResourceType()
    {
        // Regression proof for the plan-review correction: the insert path never updated the cache after
        // inserting, so the second caller for the same never-before-seen type name would not see the new
        // row and would attempt a duplicate INSERT. CacheResourceTypeId records the freshly-inserted id,
        // which is what makes the second call a cache hit.
        var resource = new ResourceWrapper(
            "Observation", "obs-crud-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"obs-crud-1"}"""),
            new ResourceRequest("PUT", "Observation/obs-crud-1"));

        await _repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var secondResource = new ResourceWrapper(
            "Observation", "obs-crud-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"obs-crud-2"}"""),
            new ResourceRequest("PUT", "Observation/obs-crud-2"));

        await _repository.CreateOrUpdateAsync(secondResource, CancellationToken.None);

        var rowCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.ResourceType WHERE Name = 'Observation'");
        rowCount.ShouldBe(1);
    }

    /// <summary>
    /// Pins what actually lands in <c>dbo.Resource.RawResource</c>: the caller's own JSON, gzip
    /// compressed, with exactly two server-managed additions -- <c>meta.versionId</c> and
    /// <c>meta.lastUpdated</c>. Asserted against concrete expected values (including the complete
    /// top-level property set, so a dropped or invented field fails) rather than against a recorded
    /// snapshot: the write path bakes both meta fields in before compressing, and nothing else in the
    /// stored bytes is allowed to drift.
    /// </summary>
    [Fact]
    public async Task GivenAResourceWithContent_WhenCreateOrUpdateAsyncCalled_ThenDboResourceRawResourceHoldsThatContentPlusServerManagedMeta()
    {
        const string ResourceId = "patient-rawresource-1";
        var resource = new ResourceWrapper("Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}","active":true,"name":[{"family":"Rawlings","given":["Ada"]}]}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"));

        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var rawResource = await _database.ExecuteScalarBytesAsync(
            $"SELECT RawResource FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        rawResource.ShouldNotBeNull();

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var json = compressor.DecompressBytes(rawResource);
        var reader = new Utf8JsonReader(json.Span);
        var stored = JsonNode.Parse(ref reader)!.AsObject();

        stored.Select(property => property.Key).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["active", "id", "meta", "name", "resourceType"]);
        stored["resourceType"]!.GetValue<string>().ShouldBe("Patient");
        stored["id"]!.GetValue<string>().ShouldBe(ResourceId);
        stored["active"]!.GetValue<bool>().ShouldBeTrue();
        stored["name"]!.AsArray().Count.ShouldBe(1);
        stored["name"]![0]!["family"]!.GetValue<string>().ShouldBe("Rawlings");
        stored["name"]![0]!["given"]!.AsArray().Select(given => given!.GetValue<string>()).ShouldBe(["Ada"]);
        stored["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
        DateTimeOffset.TryParse(
            stored["meta"]!["lastUpdated"]!.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out _).ShouldBeTrue();
    }

    /// <summary>
    /// The delete-path counterpart of
    /// <c>SqlServerFhirRepositoryExpiryTests.GivenAResourceIndexedIntoEverySearchIndexTable_WhenHardDeleteResourceAsyncCalled_ThenEverySearchIndexTableIsSweptClean</c>.
    /// <c>DeleteAsync</c> wipes the index rows of the superseded version's surrogate id across the
    /// same fixed 15-table list; every table's rows are asserted present first, so the sweep is
    /// genuinely exercised rather than asserted against empty tables.
    /// </summary>
    [Fact]
    public async Task GivenAResourceIndexedIntoEverySearchIndexTable_WhenDeleteAsyncCalled_ThenEverySearchIndexTableIsSweptClean()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);

        const string ReferenceTargetId = "delete-sweep-target";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ReferenceTargetId), CancellationToken.None);

        const string ResourceId = "delete-sweep-1";
        var resource = new ResourceWrapper("Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"))
        {
            SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(ReferenceTargetId)
        };
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var surrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, surrogateId, CancellationToken.None);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, surrogateId, CancellationToken.None);

        await _repository.DeleteAsync(
            new ResourceKey("Patient", ResourceId), new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None);

        await SearchIndexTableSeeder.AssertEverySearchIndexTableIsEmptyAsync(_database, surrogateId, CancellationToken.None);
    }

    private static ResourceWrapper BuildTestPatientWrapper(string id) =>
        new("Patient", id, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
            new ResourceRequest("PUT", $"Patient/{id}"));
}
