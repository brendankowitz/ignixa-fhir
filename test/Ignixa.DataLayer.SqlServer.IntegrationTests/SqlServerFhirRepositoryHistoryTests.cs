using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
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
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-1"}"""), new ResourceRequest("PUT", "Patient/history-1"));
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
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-2"}"""), new ResourceRequest("PUT", "Patient/history-2"));
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

    [Fact]
    public async Task GivenResourcesOfTwoTypes_WhenGetTypeHistoryAsyncCalled_ThenOnlyMatchingTypeIsReturned()
    {
        var patient = new ResourceWrapper("Patient", "history-type-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-type-1"}"""), new ResourceRequest("PUT", "Patient/history-type-1"));
        var observation = new ResourceWrapper("Observation", "history-type-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"history-type-2"}"""), new ResourceRequest("PUT", "Observation/history-type-2"));
        await _repository.CreateOrUpdateAsync(patient, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(observation, CancellationToken.None);

        var history = await _repository.GetTypeHistoryAsync(
            "Patient", TestTenantDatabase.TestTenantId, new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();

        history.ShouldContain(h => h.ResourceId == "history-type-1");
        history.ShouldNotContain(h => h.ResourceId == "history-type-2");
        history.ShouldAllBe(h => h.ResourceType == "Patient");
    }

    [Fact]
    public async Task GivenResourcesOfTwoTypes_WhenGetSystemHistoryAsyncCalled_ThenBothAreReturnedWithCorrectResourceType()
    {
        var patient = new ResourceWrapper("Patient", "history-sys-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-sys-1"}"""), new ResourceRequest("PUT", "Patient/history-sys-1"));
        var observation = new ResourceWrapper("Observation", "history-sys-2", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"history-sys-2"}"""), new ResourceRequest("PUT", "Observation/history-sys-2"));
        await _repository.CreateOrUpdateAsync(patient, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(observation, CancellationToken.None);

        var history = await _repository.GetSystemHistoryAsync(
            TestTenantDatabase.TestTenantId, new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();

        history.ShouldContain(h => h.ResourceId == "history-sys-1" && h.ResourceType == "Patient");
        history.ShouldContain(h => h.ResourceId == "history-sys-2" && h.ResourceType == "Observation");
    }

    [Fact]
    public async Task GivenADeletedResource_WhenGetResourceHistoryAsyncCalled_ThenTombstoneVersionHasIsDeletedTrue()
    {
        var resource = new ResourceWrapper("Patient", "history-deleted-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"history-deleted-1"}"""), new ResourceRequest("PUT", "Patient/history-deleted-1"));
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _repository.DeleteAsync(
            new ResourceKey("Patient", "history-deleted-1"),
            new ResourceRequest("DELETE", "Patient/history-deleted-1"),
            null,
            CancellationToken.None);

        var history = await _repository.GetResourceHistoryAsync(
            new ResourceKey("Patient", "history-deleted-1"), new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();

        // Not asserting sort position here: DeleteAsync is called with transactionId: null in real
        // production usage (DeleteResourceHandler.cs), same as the legacy EF source
        // (SqlEntityFrameworkRepository.cs:259) -- the tombstone row's TransactionId is therefore NULL,
        // it never joins to a dbo.Transactions row, and its t.CreateDate (the ORDER BY column) is NULL.
        // Where a NULL CreateDate sorts relative to a real one is a pre-existing trait shared by both
        // implementations, not something this task changes.
        history.Count.ShouldBe(2);
        var tombstone = history.Single(h => h.VersionId == "2");
        tombstone.IsDeleted.ShouldBeTrue();
        tombstone.Request!.Method.ShouldBe("DELETE");
        var original = history.Single(h => h.VersionId == "1");
        original.IsDeleted.ShouldBeFalse();
    }
}
