using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
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
