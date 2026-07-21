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

    [Fact]
    public async Task GivenAResourceWithHistory_WhenHardDeleteResourceAsyncCalled_ThenAllVersionsAndSearchIndexRowsAreGone()
    {
        var resource = new ResourceWrapper("Patient", "hard-delete-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"hard-delete-1"}"""), new ResourceRequest("PUT", "Patient/hard-delete-1"));
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
