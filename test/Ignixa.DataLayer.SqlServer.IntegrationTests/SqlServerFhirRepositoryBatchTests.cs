using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.IO;
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

    /// <summary>
    /// Batch writes must store the same <c>RawResource</c> content a single-resource write does --
    /// <c>BatchWriteAsync</c> has its own wrapper-building path (<c>BuildResourceWrappersAsync</c>),
    /// separate from <c>CreateOrUpdateAsync</c>'s, that stamps <c>meta.versionId</c>/
    /// <c>meta.lastUpdated</c> itself. Every one of the five rows is decompressed and asserted against
    /// its own concrete expected content, so a row-to-operation mix-up (all five sharing one entry's
    /// JSON, or an off-by-one in the entry-index-to-surrogate-id mapping) fails.
    /// </summary>
    [Fact]
    public async Task GivenAFiveResourceBatch_WhenBatchWriteAsyncCalled_ThenEachDboResourceRowHoldsItsOwnResourceContent()
    {
        var transactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);
        var operations = Enumerable.Range(0, 5)
            .Select(i => ("Patient", $"batch-content-{i}",
                ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"batch-content-{{i}}","name":[{"family":"Family{{i}}"}]}"""),
                (IReadOnlyList<object>)[], "PUT", i))
            .ToArray();

        await _repository.BatchWriteAsync(transactionId, operations, CancellationToken.None);
        await _repository.CommitTransactionAsync(transactionId, CancellationToken.None);

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        for (var i = 0; i < 5; i++)
        {
            var rawResource = await _database.ExecuteScalarBytesAsync(
                $"SELECT RawResource FROM dbo.Resource WHERE ResourceId = 'batch-content-{i}' AND IsHistory = 0");
            rawResource.ShouldNotBeNull($"batch-content-{i} was not written.");

            var json = compressor.DecompressBytes(rawResource);
            var reader = new Utf8JsonReader(json.Span);
            var stored = JsonNode.Parse(ref reader)!.AsObject();

            stored.Select(property => property.Key).OrderBy(name => name, StringComparer.Ordinal)
                .ShouldBe(["id", "meta", "name", "resourceType"]);
            stored["resourceType"]!.GetValue<string>().ShouldBe("Patient");
            stored["id"]!.GetValue<string>().ShouldBe($"batch-content-{i}");
            stored["name"]![0]!["family"]!.GetValue<string>().ShouldBe($"Family{i}");
            stored["meta"]!["versionId"]!.GetValue<string>().ShouldBe("1");
            stored["meta"]!["lastUpdated"].ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task GivenNoStalledTransactions_WhenGetStalledTransactionsAsyncCalledWithAOneHourThreshold_ThenReturnsEmpty()
    {
        var stalled = await _repository.GetStalledTransactionsAsync(TimeSpan.FromHours(1), CancellationToken.None);
        stalled.ShouldBeEmpty();
    }

    /// <summary>
    /// Positive-path proof for GetStalledTransactionsAsync -- the only prior coverage
    /// (the test above) exercises the empty-result case, which would pass unchanged even if the
    /// method's predicate were silently broken (e.g. a dropped "IsCompleted = 0" clause, wrong
    /// column, or inverted comparison). This begins a real transaction via GetNextTransactionIdAsync
    /// (dbo.Transactions.IsCompleted defaults to 0, HeartbeatDate defaults to getUTCdate() -- see
    /// Ignixa.DataLayer.SqlServer.Database/Tables/Transactions.sql and StoredProcedures/
    /// MergeResourcesBeginTransaction.sql), deliberately never commits it, and asserts it comes back.
    /// A TimeSpan.Zero threshold is deterministic here (not flaky): the transaction's HeartbeatDate is
    /// stamped at BeginTransaction time, strictly before this test's later
    /// GetStalledTransactionsAsync call computes "now" -- any nonzero elapsed time between those two
    /// points (guaranteed by the intervening round trip) satisfies HeartbeatDate &lt; now.
    /// </summary>
    [Fact]
    public async Task GivenAnUncommittedTransaction_WhenGetStalledTransactionsAsyncCalledWithAZeroThreshold_ThenTheUncommittedTransactionIsReturned()
    {
        var transactionId = await _repository.GetNextTransactionIdAsync(CancellationToken.None);

        var stalled = await _repository.GetStalledTransactionsAsync(TimeSpan.Zero, CancellationToken.None);

        stalled.ShouldContain(transactionId);
    }
}
