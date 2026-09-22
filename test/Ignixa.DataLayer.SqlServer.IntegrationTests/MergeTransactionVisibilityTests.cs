using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class MergeTransactionVisibilityTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenACompletedWrite_WhenCommitReturns_ThenVisibleDateIsPublished()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("visible"));

        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1 AND VisibleDate IS NOT NULL"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnEarlierPendingAllocation_WhenItCompletes_ThenVisibilityAdvancesWithoutSkippingIt()
    {
        var pending = await _database.Repository.GetNextTransactionIdAsync();
        await _database.Repository.CreateOrUpdateAsync(Patient("behind-pending"));
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsVisible = 1"))
            .ShouldBe(0);

        await _database.Repository.CommitTransactionAsync(pending);

        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1 AND VisibleDate IS NOT NULL"))
            .ShouldBe(2);
    }

    private static ResourceWrapper Patient(string id) => new(
        "Patient", id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));
}
