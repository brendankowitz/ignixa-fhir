using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
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
        var pending = await _database.Repository.GetNextTransactionIdAsync(0);
        await _database.Repository.CreateOrUpdateAsync(Patient("behind-pending"));
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsVisible = 1"))
            .ShouldBe(0);

        await _database.Repository.CommitTransactionAsync(pending);

        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1 AND VisibleDate IS NOT NULL"))
            .ShouldBe(2);
    }

    // §5.2 step 3: a rejected delete completes its allocation, so it cannot hold the drain or visibility.
    [Fact]
    public async Task GivenAStandaloneDeleteRejectedByTheServer_WhenItRollsBack_ThenItsAllocationIsCompleted()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("delete-rejected"));
        await _database.ExecuteNonQueryAsync("""
            CREATE TRIGGER dbo.RejectTombstone ON dbo.Resource AFTER INSERT AS
            IF EXISTS (SELECT 1 FROM inserted WHERE RequestMethod = 'DELETE')
                THROW 50001, 'injected tombstone rejection', 1;
            """);

        var rejection = await Should.ThrowAsync<SqlException>(async () => await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "delete-rejected"),
            new ResourceRequest("DELETE", "Patient/delete-rejected"),
            definitionsEventId: 0));

        rejection.Number.ShouldBe(50001);
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 0"))
            .ShouldBe(0);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND FailureReason LIKE '%injected tombstone rejection%'"))
            .ShouldBe(1);
    }

    // §5.5 invariant: every SQL write shape allocates through BeginTransactionAsync.
    [Fact]
    public async Task GivenEachSqlWriteShape_WhenItStarts_ThenItAllocatesThroughTheVisibilityTransaction()
    {
        var transactionCount = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions");

        await _database.Repository.CreateOrUpdateAsync(Patient("allocation-create"));
        await AssertTransactionCountAsync(++transactionCount);

        await ((IAtomicFhirRepository)_database.Repository).WriteTransactionAsync(
        [
            Patient("allocation-atomic") with { ExpectedVersionId = "0" },
        ],
        CancellationToken.None);
        await AssertTransactionCountAsync(++transactionCount);

        var batchTransaction = await _database.Repository.GetNextTransactionIdAsync(0);
        await AssertTransactionCountAsync(++transactionCount);
        await _database.Repository.CommitTransactionAsync(batchTransaction);

        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "allocation-create"),
            new ResourceRequest("DELETE", "Patient/allocation-create"),
            definitionsEventId: 0);
        await AssertTransactionCountAsync(++transactionCount);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1"))
            .ShouldBe(transactionCount);
    }

    private async Task AssertTransactionCountAsync(int expected) =>
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions"))
        .ShouldBe(expected);

    private static ResourceWrapper Patient(string id) => new(
        "Patient", id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));
}
