using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerFhirRepositoryBatchTests : IAsyncLifetime
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
    public async Task GivenThreeNewResourcesInOneBatch_WhenBatchWriteAsyncCalled_ThenAllThreeAreCreated()
    {
        var transactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var operations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "batch-1", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-1"}"""), [], "PUT", 0),
            ("Patient", "batch-2", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-2"}"""), [], "PUT", 1),
            ("Observation", "batch-3", ResourceJsonNode.Parse("""{"resourceType":"Observation","id":"batch-3"}"""), [], "PUT", 2),
        };

        var keys = await _repository.BatchWriteAsync(transactionId, operations, CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, CancellationToken.None);

        keys.Count.ShouldBe(3);
        (await _repository.GetAsync(new ResourceKey("Patient", "batch-1"), CancellationToken.None)).ShouldNotBeNull();
        (await _repository.GetAsync(new ResourceKey("Observation", "batch-3"), CancellationToken.None)).ShouldNotBeNull();
    }

    // NOTE: this test was originally written (per the task brief) to exercise the InvalidOperationException
    // "version constraint violation" pre-flight check via a stale-caller-retries-a-batch scenario.
    // That specific check is provably unreachable via BatchWriteAsync's public surface: the port
    // (faithfully matching SqlEntityFrameworkRepository.cs:477/517/528) computes
    // newVersion = existing.MaxVersion + 1 from the SAME batch-local snapshot the check later
    // compares it against -- so newVersion > existing.MaxVersion holds by construction on every call,
    // for every operation, with no code path (short of two operations targeting the same key within
    // one batch, which the original's own comment at :474-475 already flags as "a validation error
    // caught elsewhere") able to make the check trigger. Confirmed empirically: running the brief's
    // literal scenario completes successfully (writes a legitimate version 3), it does not throw.
    //
    // The surrogate-ID pre-flight check (ResourceVersionConflictException) has no such self-referential
    // problem: newSurrogateId is derived from the CALLER-supplied transactionId, independent of the
    // batch's own version snapshot, so it genuinely protects against a stale caller retrying a batch
    // with an old transaction ID. This test exercises that real, reachable path instead.
    [Fact]
    public async Task GivenABatchReusesAStaleTransactionIdForAResourceWrittenLater_WhenBatchWriteAsyncCalled_ThenThrowsResourceVersionConflictException()
    {
        var staleTransactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);

        var existing = new ResourceWrapper("Patient", "batch-conflict-1", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-conflict-1"}"""), new ResourceRequest("PUT", "Patient/batch-conflict-1"));
        await _repository.CreateOrUpdateAsync(existing, CancellationToken.None);
        await _repository.CreateOrUpdateAsync(existing with { }, CancellationToken.None); // now at version 2, with a surrogate ID newer than staleTransactionId

        var operations = new (string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)[]
        {
            ("Patient", "batch-conflict-1", ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"batch-conflict-1"}"""), [], "PUT", 0),
        };

        await Should.ThrowAsync<ResourceVersionConflictException>(() =>
            _repository.BatchWriteAsync(staleTransactionId, operations, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNoStalledTransactions_WhenGetStalledTransactionsAsyncCalledWithAOneHourThreshold_ThenReturnsEmpty()
    {
        var stalled = await _repository.GetStalledTransactionsAsync(TimeSpan.FromHours(1), CancellationToken.None);
        stalled.ShouldBeEmpty();
    }
}
