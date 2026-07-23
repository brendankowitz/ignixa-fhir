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
}
