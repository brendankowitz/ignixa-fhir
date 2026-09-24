using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
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
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"expiry-1"}"""), new ResourceRequest("PUT", "Patient/expiry-1"))
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
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"expiry-2"}"""), new ResourceRequest("PUT", "Patient/expiry-2"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var expired = await _repository.GetExpiredResourcesAsync(batchSize: 100, CancellationToken.None);

        expired.ShouldNotContain(e => e.ResourceId == "expiry-2");
    }

    /// <summary>
    /// Covers only <c>dbo.Resource</c>, which is what the name now says: the current version AND its
    /// history both go. <c>dbo.ResourceTtl</c> is
    /// <see cref="GivenAResourceWithATtlRow_WhenHardDeleteResourceAsyncCalled_ThenTheTtlRowIsGone"/>'s job
    /// and the fifteen search-index tables are
    /// <see cref="GivenAResourceIndexedIntoEverySearchIndexTable_WhenHardDeleteResourceAsyncCalled_ThenEverySearchIndexTableIsSweptClean"/>'s.
    /// This test used to assert an empty ResourceTtl as well, which proved nothing: the resource below has
    /// no ExpiresAt, so no TTL row was ever written and the count was zero before the delete as well as
    /// after. An assertion that cannot fail reads as coverage and is worse than none.
    /// </summary>
    [Fact]
    public async Task GivenAResourceWithHistory_WhenHardDeleteResourceAsyncCalled_ThenAllVersionsAreGone()
    {
        var resource = new ResourceWrapper("Patient", "hard-delete-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"hard-delete-1"}"""), new ResourceRequest("PUT", "Patient/hard-delete-1"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var versionsBefore = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'hard-delete-1'");
        versionsBefore.ShouldBe(2, "the delete has to have both a current version and a history row to remove");

        var resourceTypeId = await _database.ExecuteScalarAsync<short>("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        await _repository.HardDeleteResourceAsync(resourceTypeId, "hard-delete-1", CancellationToken.None);

        var remainingRows = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'hard-delete-1'");
        remainingRows.ShouldBe(0);
    }

    /// <summary>
    /// Pins the hard-delete batch's TTL removal, which nothing else does in the direction that matters.
    /// The two tests that touch <c>dbo.ResourceTtl</c> in
    /// <c>SqlServerFhirRepositoryDeleteAtomicityTests</c> both assert the row SURVIVES, so the batch's
    /// final statement was only ever pinned in the direction of declining to delete. Mutating its
    /// <c>NOT EXISTS</c> guard to <c>AND 1 = 0</c> makes TTL removal permanently dead, and before this
    /// test existed the entire suite stayed green through that mutation; this test is now the one thing
    /// that fails.
    /// <para>
    /// The pre-delete assertion is the load-bearing half. Without it this test is the vacuous one it
    /// replaces: "no ResourceTtl row afterwards" is trivially true for any resource that never had one,
    /// which is every resource written without an <c>ExpiresAt</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAResourceWithATtlRow_WhenHardDeleteResourceAsyncCalled_ThenTheTtlRowIsGone()
    {
        const string ResourceId = "hard-delete-ttl-1";
        var resource = new ResourceWrapper("Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"))
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var ttlRowsBefore = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = '{ResourceId}'");
        ttlRowsBefore.ShouldBe(1, "without a TTL row to remove, the assertion after the delete cannot fail");

        var resourceTypeId = await _database.ExecuteScalarAsync<short>("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        await _repository.HardDeleteResourceAsync(resourceTypeId, ResourceId, CancellationToken.None);

        var ttlRowsAfter = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = '{ResourceId}'");
        ttlRowsAfter.ShouldBe(0, "no version of the resource is left, so nothing is holding the expiry");
    }

    /// <summary>
    /// The TTL cleanup job's real contract: hard delete must leave no orphaned search-index row
    /// behind in any of the 15 tables <c>SqlServerFhirRepository.SearchIndexTables</c> lists, not just
    /// in <c>dbo.Resource</c>/<c>dbo.ResourceTtl</c>, which are the two tests above.
    /// The resource is written with an index entry per table first and each table's row count is
    /// asserted non-zero before the delete -- see <see cref="SearchIndexTableSeeder"/> for why an
    /// "everything is empty afterwards" assertion on its own would prove nothing.
    /// </summary>
    [Fact]
    public async Task GivenAResourceIndexedIntoEverySearchIndexTable_WhenHardDeleteResourceAsyncCalled_ThenEverySearchIndexTableIsSweptClean()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);

        const string ReferenceTargetId = "hard-delete-sweep-target";
        await _repository.CreateOrUpdateAsync(
            new ResourceWrapper("Patient", ReferenceTargetId, "1", DateTimeOffset.UtcNow,
                ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ReferenceTargetId}}"}"""),
                new ResourceRequest("PUT", $"Patient/{ReferenceTargetId}")),
            CancellationToken.None);

        const string ResourceId = "hard-delete-sweep-1";
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

        var resourceTypeId = await _database.ExecuteScalarAsync<short>("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");
        await _repository.HardDeleteResourceAsync(resourceTypeId, ResourceId, CancellationToken.None);

        await SearchIndexTableSeeder.AssertEverySearchIndexTableIsEmptyAsync(_database, surrogateId, CancellationToken.None);
    }
}
