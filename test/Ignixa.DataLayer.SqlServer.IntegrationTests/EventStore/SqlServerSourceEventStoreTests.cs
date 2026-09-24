using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.SqlServer.EventStore;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.EventStore;

/// <summary>
/// Behavioural contract for <see cref="ISourceEventStore"/>, written against the EF implementation first so
/// it encodes what the store actually does today rather than what the port is assumed to do. Phase F Task 1
/// repoints <see cref="CreateStore"/> at the raw-ADO.NET implementation; every assertion below must hold
/// unchanged. An assertion that needs editing is a behavioural difference, not a test that needs adjusting.
/// </summary>
public class SqlServerSourceEventStoreTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    // The single seam Task 1 flipped. Every assertion below was written and run green against the EF
    // implementation first; none were edited when this changed.
    private ISourceEventStore CreateStore()
        => new SqlServerSourceEventStore(
            _database.SqlExecutionService,
            _database.TenantId,
            NullLogger<SqlServerSourceEventStore>.Instance);

    private static NewSourceEvent Deactivation(string streamId, string packageId, string reason) =>
        new(streamId, nameof(PackageDeactivated), new PackageDeactivated(packageId, "1.0.0", reason));

    [Fact]
    public async Task GivenEventsAcrossTwoStreams_WhenAppended_ThenReadAllReturnsThemInEventIdOrder()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var appended = await store.AppendAsync(
        [
            Deactivation("stream-a", "pkg.one", "first"),
            Deactivation("stream-b", "pkg.two", "second"),
            Deactivation("stream-a", "pkg.three", "third"),
        ], CancellationToken.None);

        // Assert
        appended.Count.ShouldBe(3);
        appended.Select(e => e.EventId).ShouldBe(appended.Select(e => e.EventId).OrderBy(id => id));

        var all = new List<SourceEvent>();
        await foreach (var evt in store.ReadAllAsync(CancellationToken.None))
        {
            all.Add(evt);
        }

        all.Count.ShouldBe(3);
        all.Select(e => e.EventId).ShouldBe(all.Select(e => e.EventId).OrderBy(id => id));
        all.Select(e => e.StreamId).ShouldBe(["stream-a", "stream-b", "stream-a"]);
        all.Select(e => ((PackageDeactivated)e.Data).Reason).ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public async Task GivenAppendedEvents_WhenReadFromAnEventId_ThenOnlyLaterEventsAreReturned()
    {
        var store = CreateStore();
        var appended = await store.AppendAsync(
        [
            Deactivation("stream-a", "pkg.one", "first"),
            Deactivation("stream-a", "pkg.two", "second"),
            Deactivation("stream-a", "pkg.three", "third"),
        ], CancellationToken.None);

        var cutoff = appended[0].EventId;

        var later = new List<SourceEvent>();
        await foreach (var evt in store.ReadFromAsync(cutoff, CancellationToken.None))
        {
            later.Add(evt);
        }

        // Strictly greater than, so the cutoff event itself is excluded.
        later.Count.ShouldBe(2);
        later.ShouldAllBe(e => e.EventId > cutoff);
        later.Select(e => ((PackageDeactivated)e.Data).Reason).ShouldBe(["second", "third"]);
    }

    [Fact]
    public async Task GivenEventsAcrossTwoStreams_WhenReadingOneStream_ThenOnlyThatStreamsEventsAreReturned()
    {
        var store = CreateStore();
        await store.AppendAsync(
        [
            Deactivation("stream-a", "pkg.one", "first"),
            Deactivation("stream-b", "pkg.two", "second"),
            Deactivation("stream-a", "pkg.three", "third"),
        ], CancellationToken.None);

        var streamA = new List<SourceEvent>();
        await foreach (var evt in store.ReadStreamAsync("stream-a", CancellationToken.None))
        {
            streamA.Add(evt);
        }

        streamA.Count.ShouldBe(2);
        streamA.ShouldAllBe(e => e.StreamId == "stream-a");
        streamA.Select(e => ((PackageDeactivated)e.Data).Reason).ShouldBe(["first", "third"]);
    }

    [Fact]
    public async Task GivenABatch_WhenAppended_ThenEveryEventSharesOneTimestamp()
    {
        // AppendAsync captures DateTimeOffset.UtcNow once per call and stamps the whole batch with it,
        // rather than timestamping each event as it is constructed.
        var store = CreateStore();

        var appended = await store.AppendAsync(
        [
            Deactivation("stream-a", "pkg.one", "first"),
            Deactivation("stream-a", "pkg.two", "second"),
        ], CancellationToken.None);

        appended.Select(e => e.Timestamp).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task GivenAnAppendedEvent_WhenReadBack_ThenTheStoredTransactionIdAppearsOnlyOnTheReadPath()
    {
        // A deliberate asymmetry in the current implementation, pinned so the port cannot quietly "fix" it:
        // AppendAsync builds its return value with the 5-argument SourceEvent constructor, so TransactionId
        // takes its default of 0 even though the row it just wrote carries the real cutoff. The read path
        // uses the 6-argument constructor and surfaces the stored value. Changing either is a behaviour
        // change, not a port detail.
        var store = CreateStore();

        var appended = await store.AppendAsync(
            [Deactivation("stream-a", "pkg.one", "first")], CancellationToken.None);

        appended[0].TransactionId.ShouldBe(0);

        var stored = await _database.ExecuteScalarAsync<long>(
            "SELECT TOP 1 TransactionId FROM dbo.SourceEvents ORDER BY EventId", CancellationToken.None);

        var readBack = new List<SourceEvent>();
        await foreach (var evt in store.ReadAllAsync(CancellationToken.None))
        {
            readBack.Add(evt);
        }

        readBack[0].TransactionId.ShouldBe(stored);
    }

    [Fact]
    public async Task GivenAnEmptyStore_WhenReadAll_ThenNothingIsReturned()
    {
        var store = CreateStore();

        var all = new List<SourceEvent>();
        await foreach (var evt in store.ReadAllAsync(CancellationToken.None))
        {
            all.Add(evt);
        }

        all.ShouldBeEmpty();
    }
}
