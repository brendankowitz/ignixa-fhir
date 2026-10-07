using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public sealed class ReindexStoreSqlContractTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private IReindexStore _store = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _store = _database.ReindexStore;
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenExistingTransactions_WhenBarrierIsRaisedMonotonically_ThenItReturnsThePostBarrierCutoff()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("before-barrier"));

        var first = await _store.RaiseBarrierAsync(42, CancellationToken.None);
        var second = await _store.RaiseBarrierAsync(17, CancellationToken.None);

        first.TransactionId.ShouldBeGreaterThan(0);
        first.SurrogateId.ShouldBeGreaterThanOrEqualTo(first.TransactionId);
        second.ShouldBe(first);
        (await _database.ExecuteScalarAsync<long>(
            "SELECT Bigint FROM dbo.Parameters WHERE Id = 'Conformance.MinAcceptedDefinitionsEventId'"))
            .ShouldBe(42);
    }

    [Fact]
    public async Task GivenCurrentDeletedAndHistoryRows_WhenRangesAndPagesAreRead_ThenOnlyCurrentNonDeletedRowsAtTheCutoffAreReturned()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("first"));
        await _database.Repository.CreateOrUpdateAsync(Patient("updated"));
        await _database.Repository.CreateOrUpdateAsync(Patient("updated") with { VersionId = "2" });
        await _database.Repository.CreateOrUpdateAsync(Patient("deleted"));
        await _database.Repository.DeleteAsync(
            new ResourceKey("Patient", "deleted"),
            new ResourceRequest("DELETE", "Patient/deleted"));

        var (_, cutoff) = await _store.RaiseBarrierAsync(50, CancellationToken.None);
        var ranges = await _store.GetSurrogateIdRangesAsync("Patient", cutoff, 1, CancellationToken.None);
        var resources = new List<ReindexResource>();

        foreach (var (start, end) in ranges)
        {
            var after = (long?)null;
            while (true)
            {
                var page = await _store.ReadRangeAsync("Patient", start, end, 1, after, CancellationToken.None);
                if (page.Count == 0)
                {
                    break;
                }

                resources.AddRange(page);
                after = page[^1].ResourceSurrogateId;
            }
        }

        resources.Select(resource => resource.Resource.ResourceId).ShouldBe(["first", "updated"], ignoreOrder: true);
        resources.Select(resource => resource.Resource.VersionId).ShouldContain("2");
        resources.ShouldAllBe(resource => resource.ResourceSurrogateId <= cutoff);
    }

    [Fact]
    public async Task GivenAResourceSupersededAfterRead_WhenItsIndicesAreUpdated_ThenItIsReportedAsAConflictWithoutCreatingHistory()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("conflict"));
        var (_, cutoff) = await _store.RaiseBarrierAsync(60, CancellationToken.None);
        var resource = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None)).Single();
        var beforeHistory = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'conflict' AND IsHistory = 1");

        await _database.Repository.CreateOrUpdateAsync(
            Patient("conflict") with { VersionId = "2", DefinitionsEventId = 60 });

        var result = await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None);

        result.Updated.ShouldBe(0);
        result.Conflicts.ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'conflict' AND IsHistory = 1"))
            .ShouldBe(beforeHistory + 1);
    }

    [Fact]
    public async Task GivenACurrentResource_WhenIndexOnlyUpdateIsRepeated_ThenVersionRawResourceTransactionAndHistoryStayUnchanged()
    {
        await _database.Repository.CreateOrUpdateAsync(Patient("idempotent"));
        var (_, cutoff) = await _store.RaiseBarrierAsync(70, CancellationToken.None);
        var resource = (await _store.ReadRangeAsync("Patient", 0, cutoff, 10, null, CancellationToken.None)).Single();
        var version = await _database.ExecuteScalarAsync<int>(
            "SELECT Version FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var transactionId = await _database.ExecuteScalarAsync<long>(
            "SELECT TransactionId FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var rawResource = await _database.ExecuteScalarBytesAsync(
            "SELECT RawResource FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0");
        var historyCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 1");

        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe((1, 0));
        (await _store.UpdateSearchIndicesAsync([resource], CancellationToken.None)).ShouldBe((1, 0));

        (await _database.ExecuteScalarAsync<int>(
            "SELECT Version FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(version);
        (await _database.ExecuteScalarAsync<long>(
            "SELECT TransactionId FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(transactionId);
        (await _database.ExecuteScalarBytesAsync(
            "SELECT RawResource FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 0"))
            .ShouldBe(rawResource);
        (await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = 'idempotent' AND IsHistory = 1"))
            .ShouldBe(historyCount);
    }

    private static ResourceWrapper Patient(string id) => new(
        "Patient",
        id,
        "1",
        DateTimeOffset.UtcNow,
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
        new ResourceRequest("PUT", $"Patient/{id}"));
}
