using Ignixa.Abstractions;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

/// <summary>
/// The atomicity and concurrency contract of <see cref="SqlServerFhirRepository.DeleteAsync"/>.
/// <para>
/// Every other test on this path is a sequential happy-path assertion, which is exactly why the port lost
/// the EF version's single-<c>SaveChangesAsync</c> atomicity without anything going red: four separately
/// committed statements produce the same end state as one transaction whenever nothing interrupts them and
/// nobody else is writing. These tests interrupt, and they write concurrently.
/// </para>
/// </summary>
public class SqlServerFhirRepositoryDeleteAtomicityTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerFhirRepository _repository = null!;
    private SqlServerSearchIndexReferenceDataCache? _interceptedCache;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        _repository = _database.Repository;
    }

    public async Task DisposeAsync()
    {
        _interceptedCache?.Dispose();
        await _database.DisposeAsync();
    }

    /// <summary>
    /// The regression itself. Failing between the history flip and the tombstone insert must leave the
    /// resource exactly as it was -- not readable-but-versioned, not gone.
    /// <para>
    /// The fault is injected by ordinal over non-query commands, counted the same way whether they run
    /// inside a transaction or standalone, so the injection point is identical for the atomic and the
    /// non-atomic shape of this method. Ordinal 2 is the tombstone insert; ordinal 1, the history flip, has
    /// already run.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAFailureBetweenTheHistoryFlipAndTheTombstone_WhenDeleteAsyncRuns_ThenTheResourceIsLeftUnchangedAndStillReadable()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);

        const string ReferenceTargetId = "delete-atomicity-target";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ReferenceTargetId), CancellationToken.None);

        const string ResourceId = "delete-atomicity-1";
        var resource = new ResourceWrapper("Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"))
        {
            SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(ReferenceTargetId),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var surrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, surrogateId, CancellationToken.None);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, surrogateId, CancellationToken.None);

        var (interceptor, repository) = await CreateInterceptedRepositoryAsync();
        interceptor.FailBeforeNonQuery(2, new InvalidOperationException("injected failure before the tombstone insert"));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await repository.DeleteAsync(
            new ResourceKey("Patient", ResourceId), new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None));
        thrown.Message.ShouldContain("injected failure");

        interceptor.Disarm();

        // Nothing half-applied: one row, still current, still version 1, still not deleted.
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}'")).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 1")).ShouldBe(0);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsDeleted = 1")).ShouldBe(0);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = '{ResourceId}'")).ShouldBe(1);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, surrogateId, CancellationToken.None);

        var fetched = await _repository.GetAsync(new ResourceKey("Patient", ResourceId), CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.VersionId.ShouldBe("1");
        fetched.IsDeleted.ShouldBeFalse();
    }

    /// <summary>
    /// The window a real client falls into, driven for real: a reader polling as fast as it can while the
    /// delete runs. Committed non-atomically, the interval between the history flip's commit and the
    /// tombstone insert's commit is a whole round trip during which the resource has no current row at all,
    /// and the reader sees <c>null</c> -- which the API layer reports as 404 "never existed" for a resource
    /// that certainly did. Committed atomically, no such state is ever committed for anyone to read.
    /// <para>
    /// The reader is put on READ_COMMITTED_SNAPSHOT deliberately, and the assertion means nothing without
    /// it. Under this server's default locking READ COMMITTED a scan concurrent with an update that moves a
    /// row's key -- which setting <c>IsHistory = 1</c> does, since <c>IX_Resource_ResourceTypeId_ResourceId</c>
    /// is filtered on <c>IsHistory = 0</c> -- can miss the row whether or not the write was atomic. This test
    /// measured that happening on roughly half of twenty rounds with the atomic delete in place: a
    /// pre-existing read-isolation property of the storage engine, not a property of this method, and not
    /// something this change claims to fix. Snapshot reads remove that confound: every read returns a
    /// consistent committed state, so a <c>null</c> here means a committed state with no current row really
    /// existed -- which is exactly, and only, the defect under test.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenASnapshotReaderPollingThroughout_WhenDeleteAsyncRuns_ThenNoCommittedStateEverLacksACurrentRow()
    {
        await EnableReadCommittedSnapshotAsync();

        const int Rounds = 20;
        var roundsWhereTheResourceVanished = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            var resourceId = $"delete-window-{round}";
            var key = new ResourceKey("Patient", resourceId);
            await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(resourceId), CancellationToken.None);

            using var stopReading = new CancellationTokenSource();
            var readerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var deleteFinished = 0;

            var reader = Task.Run(async () =>
            {
                string? whereItVanished = null;
                var reads = 0;
                while (!stopReading.IsCancellationRequested)
                {
                    var seen = await _repository.GetAsync(key, CancellationToken.None);
                    reads++;
                    readerStarted.TrySetResult();

                    if (seen is null)
                    {
                        whereItVanished =
                            $"round {round}, read #{reads}, delete still in flight: {Volatile.Read(ref deleteFinished) == 0}";
                        break;
                    }
                }

                readerStarted.TrySetResult();
                return whereItVanished;
            });

            // The reader has to already be in its loop when the delete starts, or a fast delete can finish
            // before the poll ever runs and the round proves nothing.
            await readerStarted.Task;

            await _repository.DeleteAsync(
                key, new ResourceRequest("DELETE", $"Patient/{resourceId}"), null, CancellationToken.None);
            Volatile.Write(ref deleteFinished, 1);

            await stopReading.CancelAsync();
            var observation = await reader;
            if (observation is not null)
            {
                roundsWhereTheResourceVanished.Add(observation);
            }
        }

        roundsWhereTheResourceVanished.ShouldBeEmpty();
    }

    /// <summary>
    /// The stale-surrogate-ID case. <c>DeleteAsync</c> reads the current version on its own connection and
    /// commits that read before it writes anything; a concurrent writer can version the row in that gap.
    /// The history flip must then match nothing and the delete must fail as the conflict it is -- rather
    /// than re-stamping a row that is already history and inserting a tombstone at a version that is no
    /// longer current, which is how two rows end up with <c>IsHistory = 0</c> for one resource.
    /// </summary>
    [Fact]
    public async Task GivenAConcurrentWriterVersionsTheRowAfterTheDeleteReadIt_WhenTheHistoryFlipRuns_ThenItDoesNotReStampTheHistoryRowAndTheDeleteFails()
    {
        const string ResourceId = "delete-stale-surrogate-1";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ResourceId), CancellationToken.None);

        var (interceptor, repository) = await CreateInterceptedRepositoryAsync();

        // Fires immediately before the history flip -- after DeleteAsync's read of the current version has
        // been committed and its surrogate ID captured, which is exactly the gap being simulated.
        interceptor.BeforeNonQueryAsync = async ordinal =>
        {
            if (ordinal != 1)
            {
                return;
            }

            interceptor.Disarm();
            await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ResourceId), CancellationToken.None);
        };

        await Should.ThrowAsync<ResourceVersionConflictException>(async () => await repository.DeleteAsync(
            new ResourceKey("Patient", ResourceId), new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None));

        // Exactly the concurrent writer's state, and nothing of the delete's.
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}'")).ShouldBe(2);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0")).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsDeleted = 1")).ShouldBe(0);

        var fetched = await _repository.GetAsync(new ResourceKey("Patient", ResourceId), CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.VersionId.ShouldBe("2");
        fetched.IsDeleted.ShouldBeFalse();
    }

    /// <summary>
    /// The one command in this repository that must never be retried: on-demand resource-type creation is an
    /// unguarded <c>INSERT ... OUTPUT INSERTED</c>, and it goes through <c>ExecuteReaderAsync</c> only
    /// because it needs the generated ID back.
    /// <para>
    /// This pins the call site, not the mechanism -- that a <c>NonIdempotent</c> command really does bypass
    /// the retry pipeline is already covered, for both overloads, by
    /// <c>SqlExecutionServiceConnectionTests</c>. Nothing else would notice this argument being dropped, and
    /// the sweep over the rest of the calls is here so an over-broad "fix" that marks the reads
    /// non-idempotent too -- which would surface transient faults callers should never have seen -- fails
    /// just as loudly.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAResourceTypeMissingFromTheCatalog_WhenItIsCreatedOnDemand_ThenOnlyThatInsertDeclaresItselfNonIdempotent()
    {
        var (interceptor, repository) = await CreateInterceptedRepositoryAsync();

        // The fixture seeds only Patient, so any call that has to resolve another type reaches the insert.
        // A GET is the shortest route to it, and the miss it then reports is beside the point.
        await repository.GetAsync(new ResourceKey("Observation", "never-created"), CancellationToken.None);

        var byKind = interceptor.ReaderCalls
            .ToLookup(call => call.CommandText.Contains("INSERT INTO dbo.ResourceType", StringComparison.Ordinal));
        var inserts = byKind[true].ToList();
        var everythingElse = byKind[false].ToList();

        inserts.Count.ShouldBe(1);
        inserts[0].Idempotency.ShouldBe(SqlCommandIdempotency.NonIdempotent);

        everythingElse.ShouldNotBeEmpty();
        everythingElse.ShouldAllBe(call => call.Idempotency == SqlCommandIdempotency.Idempotent);
    }

    private Task<int> CountAsync(string sql) => _database.ExecuteScalarAsync<int>(sql);

    /// <summary>
    /// Switches this test class's own scratch database to snapshot reads. Run from <c>master</c> because a
    /// database cannot be altered from a connection inside it, and <c>WITH ROLLBACK IMMEDIATE</c> because
    /// the fixture's deploy leaves pooled connections behind that would otherwise block the change; the
    /// pools are cleared on both sides so no killed connection is handed back out.
    /// </summary>
    private async Task EnableReadCommittedSnapshotAsync()
    {
        var builder = new SqlConnectionStringBuilder(_database.ConnectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        SqlConnection.ClearAllPools();

        await using (var connection = new SqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();

            // CA2100 suppressed: databaseName is the GUID-suffixed name this fixture generated for itself,
            // and ALTER DATABASE does not accept a parameter for it -- same rationale as the fixture's own
            // CREATE/DROP DATABASE helpers.
#pragma warning disable CA2100
            command.CommandText = $"ALTER DATABASE [{databaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE";
#pragma warning restore CA2100
            await command.ExecuteNonQueryAsync();
        }

        SqlConnection.ClearAllPools();
    }

    private async Task<(NonQueryInterceptor Interceptor, SqlServerFhirRepository Repository)> CreateInterceptedRepositoryAsync()
    {
        // A second repository over the same database, wired exactly as TestTenantDatabase wires the first
        // but through the interceptor. It has to be a second one rather than a reconfigured first, because
        // the tests below drive the un-intercepted repository concurrently with this one.
        var interceptor = new NonQueryInterceptor(_database.SqlExecutionService);
        var cache = new SqlServerSearchIndexReferenceDataCache(
            interceptor, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        _interceptedCache = cache;
        await cache.PreloadResourceTypesAsync(CancellationToken.None);

        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var extensionUpdater = new SqlServerPostMergeExtensionUpdater(
            interceptor, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        var mergeRepository = new SqlServerMergeRepository(
            interceptor, _database.TenantId, compressor, cache, extensionUpdater, NullLogger<SqlServerMergeRepository>.Instance);

        var repository = new SqlServerFhirRepository(
            interceptor, _database.TenantId, compressor, cache, mergeRepository,
            NullLogger<SqlServerFhirRepository>.Instance);

        return (interceptor, repository);
    }

    private static ResourceWrapper BuildTestPatientWrapper(string id) =>
        new("Patient", id, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}"}"""),
            new ResourceRequest("PUT", $"Patient/{id}"));

    /// <summary>
    /// Passes every call through to a real <see cref="ISqlExecutionService"/> while numbering the non-query
    /// commands and giving a test somewhere to stand between two of them.
    /// <para>
    /// Commands issued inside a transaction are counted on the same sequence as standalone ones on purpose:
    /// it is what makes "fail before the second write of this delete" mean the same thing whether the four
    /// writes share a transaction or not, so the same injection point exercises both shapes.
    /// </para>
    /// </summary>
    private sealed class NonQueryInterceptor(ISqlExecutionService inner) : ISqlExecutionService
    {
        private int _nonQueryCount;

        /// <summary>Runs before each non-query, given its 1-based ordinal since the last <see cref="Disarm"/>.</summary>
        public Func<int, Task>? BeforeNonQueryAsync { get; set; }

        public void FailBeforeNonQuery(int ordinal, Exception failure)
        {
            _nonQueryCount = 0;
            BeforeNonQueryAsync = actual => actual == ordinal ? Task.FromException(failure) : Task.CompletedTask;
        }

        public void Disarm() => BeforeNonQueryAsync = null;

        private readonly List<(string CommandText, SqlCommandIdempotency Idempotency)> _readerCalls = [];

        /// <summary>Every command sent through <see cref="ExecuteReaderAsync"/>, with what it declared itself to be.</summary>
        public IReadOnlyList<(string CommandText, SqlCommandIdempotency Idempotency)> ReaderCalls
        {
            get
            {
                lock (_readerCalls)
                {
                    return [.. _readerCalls];
                }
            }
        }

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            ArgumentNullException.ThrowIfNull(command);
            lock (_readerCalls)
            {
                _readerCalls.Add((command.CommandText, idempotency));
            }

            return inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
        }

        public async Task<int> ExecuteNonQueryAsync(
            int tenantId,
            SqlCommand command,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            await OnNonQueryAsync();
            return await inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(
                tenantId, (context, ct) => work(new InterceptingTransactionContext(this, context), ct), cancellationToken);

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(
                tenantId, (context, ct) => work(new InterceptingTransactionContext(this, context), ct), cancellationToken);

        private async Task OnNonQueryAsync()
        {
            var ordinal = Interlocked.Increment(ref _nonQueryCount);
            var hook = BeforeNonQueryAsync;
            if (hook is not null)
            {
                await hook(ordinal);
            }
        }

        private sealed class InterceptingTransactionContext(NonQueryInterceptor owner, ISqlTransactionContext inner)
            : ISqlTransactionContext
        {
            public async Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken)
            {
                await owner.OnNonQueryAsync();
                return await inner.ExecuteNonQueryAsync(command, cancellationToken);
            }

            public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
                SqlCommand command,
                Func<SqlDataReader, TResult> readRow,
                CancellationToken cancellationToken)
                => inner.ExecuteReaderAsync(command, readRow, cancellationToken);
        }
    }
}
