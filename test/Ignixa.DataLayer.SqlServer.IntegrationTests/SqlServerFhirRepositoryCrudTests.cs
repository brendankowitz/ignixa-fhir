using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.ValueSets.Normative;
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
        // Regression proof for the plan-review correction: the insert path used to route through
        // the cache's read-only lookup (which caches a "confirmed missing" sentinel on a miss) and
        // never updated the cache after inserting, so the second caller for the same never-before-
        // seen type name would see the stale sentinel and attempt a duplicate INSERT.
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

    private static ResourceWrapper BuildTestPatientWrapper(string id) =>
        new("Patient", id, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
            new ResourceRequest("PUT", $"Patient/{id}"));
}
