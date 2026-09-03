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
    /// What this pins, exactly: that <c>DeleteAsync</c> never COMMITS a state in which the resource has no
    /// current row. Committed non-atomically, the interval between the history flip's commit and the
    /// tombstone insert's commit is a whole round trip during which exactly that state is committed and
    /// readable. Committed atomically, it never exists for anyone to read.
    /// <para>
    /// The reader runs under READ_COMMITTED_SNAPSHOT so that it observes committed STATES and nothing else.
    /// That is what makes a <c>null</c> here mean "a committed state with no current row existed" rather
    /// than "a locking read lost the row" -- and it is also the whole of the test's reach.
    /// </para>
    /// <para>
    /// What this therefore does NOT pin: the 404 window a real client falls into. No deployed tenant runs
    /// under RCSI -- it is set nowhere in the product (not in the dacpac, not in the deployer, not in the
    /// connection-string builders; <c>SqlExecutionService</c> takes the server default), so every tenant
    /// reads under locking READ COMMITTED, where a read racing a delete can return <c>null</c> for reasons
    /// that have nothing to do with what was committed. Turning RCSI on here is what removes that from
    /// view. The locking-READ-COMMITTED window is pinned separately, and only, by
    /// <see cref="GivenTheReadIsForcedToStartMidDelete_WhenTheDeleteCommits_ThenTheReadSeesTheTombstone"/>.
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
    /// The window a real client falls into, under the isolation every deployed tenant actually runs:
    /// locking READ COMMITTED, no RCSI. A GET issued while a DELETE is in flight must return the resource
    /// (200) or the tombstone (410 Gone). It must never return nothing, which the API reports as 404
    /// "never existed".
    /// <para>
    /// Nothing here is left to timing. The reader is launched from inside the delete's transaction, between
    /// the history flip and the tombstone insert, and given three quarters of a second to reach the lock it
    /// cannot get past -- so it is certain to have begun mid-delete and to return only after the commit,
    /// which is precisely the shape every observed failure had. Polling and hoping for the race reproduced
    /// the anomaly on 8 of 30 rounds; forcing it reproduces it every time.
    /// </para>
    /// <para>
    /// Without the <c>INDEX(IX_Resource_ResourceTypeId_ResourceId)</c> hint on
    /// <c>SqlServerFhirRepository.GetAsync</c>'s current-resource read this returns <c>null</c>: the
    /// optimizer seeks the version index backward, positions on the live version, blocks on its clustered
    /// row, and after the commit re-examines that row, finds <c>IsHistory = 1</c>, and scans down past
    /// versions that do not exist -- never back up to the tombstone. Forced onto the filtered index, whose
    /// entry the delete removes and re-adds under the identical key, the same blocked read finds the
    /// tombstone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenTheReadIsForcedToStartMidDelete_WhenTheDeleteCommits_ThenTheReadSeesTheTombstone()
    {
        // The premise of the whole test: this database is on locking READ COMMITTED, like every deployed
        // tenant. If that ever stops being true the anomaly disappears and the test stops meaning anything.
        (await CountAsync("SELECT CAST(is_read_committed_snapshot_on AS INT) FROM sys.databases WHERE database_id = DB_ID()"))
            .ShouldBe(0, "this test is only meaningful under locking READ COMMITTED");

        const string ResourceId = "delete-read-window-1";
        var key = new ResourceKey("Patient", ResourceId);
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ResourceId), CancellationToken.None);

        // Warm the reader's resource-type cache so the racing GET is nothing but the one read under test.
        (await _repository.GetAsync(key, CancellationToken.None)).ShouldNotBeNull();

        var (interceptor, repository) = await CreateInterceptedRepositoryAsync();
        interceptor.ResetOrdinals();

        Task<SearchEntryResult?>? read = null;
        interceptor.BeforeNonQueryAsync = async ordinal =>
        {
            // Ordinal 2 is the tombstone insert: the flip has run and holds its locks, the tombstone is not
            // in yet, and the transaction is still open.
            if (ordinal != 2)
            {
                return;
            }

            interceptor.Disarm();
            read = Task.Run(async () => await _repository.GetAsync(key, CancellationToken.None));
            await Task.Delay(TimeSpan.FromMilliseconds(750));
        };

        await repository.DeleteAsync(
            key, new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None);

        var racingRead = read ?? throw new InvalidOperationException(
            "the delete never reached the tombstone insert, so the racing read was never started");
        var seen = await racingRead.WaitAsync(TimeSpan.FromSeconds(30));

        seen.ShouldNotBeNull("a read racing the delete returned nothing, which the API reports as 404 'never existed'");
        seen!.IsDeleted.ShouldBeTrue();
        seen.VersionId.ShouldBe("2");
    }

    /// <summary>
    /// The hint in <c>GetAsync</c> names an index by string. Renaming or dropping
    /// <c>IX_Resource_ResourceTypeId_ResourceId</c> breaks that read outright, and changing its shape --
    /// dropping the filter, or adding a key column -- would break it more quietly, because the fix depends
    /// on the delete's two writes landing on one identical index key. This says so in the schema's own
    /// terms so the next person to touch that index finds out here.
    /// </summary>
    [Fact]
    public async Task GivenTheCurrentResourceRead_WhenTheSchemaIsDeployed_ThenTheIndexItIsHintedOntoExistsAsAFilteredUniqueIndex()
    {
        (await CountAsync(
            """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.Resource')
              AND name = 'IX_Resource_ResourceTypeId_ResourceId'
              AND is_unique = 1 AND has_filter = 1
            """)).ShouldBe(1, "GetAsync hints onto this index by name; renaming or dropping it breaks that read");

        // Stripped of the punctuation SQL Server chooses to store it with -- ([IsHistory]=(0)) here.
        var filter = await _database.ExecuteScalarAsync<string>(
            """
            SELECT TRANSLATE(filter_definition, ' []()', '     ')
            FROM sys.indexes
            WHERE object_id = OBJECT_ID('dbo.Resource') AND name = 'IX_Resource_ResourceTypeId_ResourceId'
            """);
        filter.Replace(" ", string.Empty, StringComparison.Ordinal).ShouldBe("IsHistory=0");

        var keyColumns = await _database.ExecuteScalarAsync<string>(
            """
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID('dbo.Resource')
              AND i.name = 'IX_Resource_ResourceTypeId_ResourceId'
              AND ic.is_included_column = 0
            """);

        // Exactly these two, in this order: the delete's history flip and its tombstone insert have to
        // resolve to the same index key for a blocked reader to land on the tombstone.
        keyColumns.ShouldBe("ResourceTypeId,ResourceId");
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

        /// <summary>Restarts the ordinal count, for hooks assigned directly rather than through <see cref="FailBeforeNonQuery"/>.</summary>
        public void ResetOrdinals() => _nonQueryCount = 0;

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
