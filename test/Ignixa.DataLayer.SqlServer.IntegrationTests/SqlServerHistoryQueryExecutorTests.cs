using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerHistoryQueryExecutorTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerHistoryQueryExecutor _executor = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _executor = new SqlServerHistoryQueryExecutor(
            _database.SqlExecutionService,
            _database.TenantId,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            NullLogger<SqlServerHistoryQueryExecutor>.Instance);
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenAResourceWithHistory_WhenQueriedDirectlyThroughTheExecutor_ThenReturnsTheExpectedEntries()
    {
        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        var resource = new ResourceWrapper("Patient", "executor-history-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"executor-history-1"}"""), new ResourceRequest("PUT", "Patient/executor-history-1"));
        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _database.Repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);
        await _database.Repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var history = await _executor.GetResourceHistoryAsync(
            resourceTypeId, "Patient", "executor-history-1", new HistoryQueryParameters { Count = 10 }, CancellationToken.None).ToListAsync();

        history.Count.ShouldBe(3);
        history.Select(h => h.VersionId).ShouldBe(["3", "2", "1"]);
    }

    [Fact]
    public async Task GivenACorruptProbeRow_WhenQueriedWithAPageSizeOfOne_ThenYieldsAPagingProbeSentinel()
    {
        // Arrange -- two versions, history sorted descending by default so version 2 is the real page
        // and version 1 is the lookahead row @CountPlusOne fetches to detect a further page.
        // Corrupting version 1's RawResource mirrors the exact defect code review found: a corrupt
        // probe row must not silently drop the caller's only proof that a further page exists.
        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        var resourceId = $"executor-probe-{Guid.NewGuid():N}";
        var resource = new ResourceWrapper("Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""), new ResourceRequest("PUT", $"Patient/{resourceId}"));
        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _database.Repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        await _database.ExecuteNonQueryAsync(
            $"UPDATE dbo.Resource SET RawResource = 0xDEADBEEF WHERE ResourceId = '{resourceId}' AND Version = 1");

        var history = await _executor.GetResourceHistoryAsync(
            resourceTypeId, "Patient", resourceId, new HistoryQueryParameters { Count = 1 }, CancellationToken.None).ToListAsync();

        // Assert -- the real page (version 2) plus a content-free sentinel proving a further page
        // exists despite the probe row (version 1) being unreadable.
        history.Count.ShouldBe(2);
        history.ShouldContain(h => h.VersionId == "2");
        history.ShouldContain(h => h.IsPagingProbe);
    }

    [Fact]
    public async Task GivenCountIsMaxValue_WhenQueriedAsHistoryCountHelperDoesForTotalAccurate_ThenDoesNotOverflowTheFetchRowcount()
    {
        // HistoryCountHelper deliberately sets Count = int.MaxValue ("no limit, count everything")
        // when answering _history?_total=accurate. AddSharedHistoryParameters used to bind
        // @CountPlusOne as Count + 1 unconditionally, which overflows int.MaxValue to
        // int.MinValue; SQL Server then rejects the negative FETCH NEXT rowcount on every call
        // (measured: "The number of rows provided for a FETCH clause must be greater then [sic] zero.").
        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        var resourceId = $"executor-maxcount-{Guid.NewGuid():N}";
        var resource = new ResourceWrapper("Patient", resourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""), new ResourceRequest("PUT", $"Patient/{resourceId}"));
        await _database.Repository.CreateOrUpdateAsync(resource, CancellationToken.None);
        await _database.Repository.CreateOrUpdateAsync(resource with { }, CancellationToken.None);

        var history = await _executor.GetResourceHistoryAsync(
            resourceTypeId, "Patient", resourceId, new HistoryQueryParameters { Count = int.MaxValue }, CancellationToken.None).ToListAsync();

        history.Count.ShouldBe(2);
    }
}
