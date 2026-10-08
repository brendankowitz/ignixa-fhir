using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
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
        var pending = await _database.Repository.GetNextTransactionIdAsync(0);
        await _database.Repository.CreateOrUpdateAsync(Patient("behind-pending"));
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions WHERE IsVisible = 1"))
            .ShouldBe(0);

        await _database.Repository.CommitTransactionAsync(pending);

        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1 AND VisibleDate IS NOT NULL"))
            .ShouldBe(2);
    }

    [Fact]
    public async Task GivenAStandaloneDelete_WhenTheTombstoneIsWritten_ThenItUsesACompletedVisibleAllocation()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("delete-allocation"));
        var before = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions");

        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "delete-allocation"),
            new ResourceRequest("DELETE", "Patient/delete-allocation"),
            definitionsEventId: 0);

        var after = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions");
        after.ShouldBe(before + 1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 1"))
            .ShouldBe(after);
    }

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
    }

    private async Task AssertTransactionCountAsync(int expected) =>
        (await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Transactions"))
        .ShouldBe(expected);

    private static ResourceWrapper Patient(string id) => new(
        "Patient", id, "1", DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));
}
