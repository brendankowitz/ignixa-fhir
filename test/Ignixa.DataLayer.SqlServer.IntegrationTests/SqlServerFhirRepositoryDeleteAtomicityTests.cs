using System.Diagnostics;
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
    /// <summary>
    /// Rounds run by
    /// <see cref="GivenRealWritesRacingRealHardDeletes_WhenNeitherIsStalled_ThenNoSearchIndexRowOutlivesItsResource"/>.
    /// MEASURED, not chosen. Against the un-fixed final DELETE -- matching on
    /// <c>(ResourceTypeId, ResourceId)</c> rather than on <c>@SurrogateIds</c> -- four runs of 20 rounds
    /// orphaned rows on 2, 2, 1 and 2 rounds: 7 of 80, a per-round hit rate of about 9%. At that rate 40
    /// rounds detect the regression about 97% of the time (20 would be 84%), and cost about 50 seconds.
    /// All 80 measured rounds ran both sides to completion -- no deadlock, no exception of any kind -- so
    /// the round count is bounded by detection power, not by how often the race is even reachable.
    /// <para>
    /// Raise this if the regression it watches for ever ships again; do not lower it below about 30 without
    /// re-measuring, because detection falls off geometrically.
    /// </para>
    /// </summary>
    private const int LiveRaceRounds = 40;

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
    /// <para>
    /// The 409 also has to name the right row. The surrogate ID the delete read is the one ID that is
    /// definitely NOT current by the time the flip matches nothing, so reporting it as the conflicting
    /// "existing" ID sends whoever debugs the 409 to the row that is not the problem.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAConcurrentWriterVersionsTheRowAfterTheDeleteReadIt_WhenTheHistoryFlipRuns_ThenItDoesNotReStampTheHistoryRowAndTheDeleteFails()
    {
        const string ResourceId = "delete-stale-surrogate-1";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ResourceId), CancellationToken.None);

        var surrogateIdTheDeleteWillRead = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");

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

        var conflict = await Should.ThrowAsync<ResourceVersionConflictException>(async () => await repository.DeleteAsync(
            new ResourceKey("Patient", ResourceId), new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None));

        var surrogateIdThatIsActuallyCurrent = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        surrogateIdThatIsActuallyCurrent.ShouldNotBe(surrogateIdTheDeleteWillRead);
        conflict.ExistingSurrogateId.ShouldBe(
            surrogateIdThatIsActuallyCurrent,
            "the 409 names the surrogate ID the delete read, which is the one row that is certainly not the conflict");

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

    /// <summary>
    /// The two statements the rest of this class never observes. Every other test here injects at ordinal
    /// 2, the tombstone insert, so ordinal 3 (the TTL removal) and ordinal 4 (the fifteen-table index wipe)
    /// run after the injection point and are never watched. A regression that put the TTL removal back on
    /// its own auto-committed connection -- passing <c>transaction: null</c> to
    /// <c>UpsertResourceTtlAsync</c> -- would leave all of them green: the TTL row's disappearance is
    /// invisible until something fails AFTER it, which is exactly what this injects.
    /// <para>
    /// The ordinal-to-statement map is asserted rather than assumed, because the whole test rests on
    /// ordinal 3 being the TTL removal and ordinal 4 the wipe.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAFailureAtTheSearchIndexWipe_WhenDeleteAsyncRuns_ThenTheTombstoneIsGoneAndTheTtlRowIsStillThere()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);

        const string ReferenceTargetId = "delete-wipe-rollback-target";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ReferenceTargetId), CancellationToken.None);

        const string ResourceId = "delete-wipe-rollback-1";
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
        (await CountAsync($"SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = '{ResourceId}'")).ShouldBe(1);

        var (interceptor, repository) = await CreateInterceptedRepositoryAsync();
        interceptor.FailBeforeNonQuery(4, new InvalidOperationException("injected failure before the search-index wipe"));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(async () => await repository.DeleteAsync(
            new ResourceKey("Patient", ResourceId), new ResourceRequest("DELETE", $"Patient/{ResourceId}"), null, CancellationToken.None));
        thrown.Message.ShouldContain("injected failure");

        interceptor.Disarm();

        // The map this test depends on: 1 history flip, 2 tombstone insert, 3 TTL removal, 4 index wipe.
        var statements = interceptor.NonQueryCommands;
        statements.Count.ShouldBe(4);
        statements[0].ShouldContain("UPDATE dbo.Resource SET IsHistory = 1");
        statements[1].ShouldContain("INSERT INTO dbo.Resource");
        statements[2].ShouldContain("DELETE FROM dbo.ResourceTtl");
        statements[3].ShouldContain("DELETE FROM dbo.ReferenceSearchParam");

        // The tombstone did not survive its own transaction's rollback.
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}'")).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsDeleted = 1")).ShouldBe(0);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 1")).ShouldBe(0);

        // And neither did the TTL removal, which is the assertion a `transaction: null` regression fails.
        (await CountAsync($"SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = '{ResourceId}'"))
            .ShouldBe(1, "the TTL removal committed on its own rather than with the rest of the delete");

        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, surrogateId, CancellationToken.None);

        var fetched = await _repository.GetAsync(new ResourceKey("Patient", ResourceId), CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.VersionId.ShouldBe("1");
        fetched.IsDeleted.ShouldBeFalse();
    }

    /// <summary>
    /// <c>HardDeleteResourceAsync</c> collects the resource's surrogate IDs into <c>@SurrogateIds</c> with a
    /// plain SELECT -- no <c>UPDLOCK</c>, no <c>HOLDLOCK</c> -- and READ COMMITTED drops that statement's
    /// shared locks at statement end no matter what transaction surrounds it. A writer can therefore commit
    /// a whole new version, with a new surrogate ID, after the snapshot and before the batch ends.
    /// <para>
    /// What that used to cost: the fifteen index deletes are scoped to <c>@SurrogateIds</c>, but the final
    /// resource delete matched on <c>(ResourceTypeId, ResourceId)</c> -- so it removed the new version's
    /// resource row while leaving that version's search-index rows behind. Those rows are orphaned
    /// PERMANENTLY, because the next hard delete for this resource ID finds no <c>dbo.Resource</c> row to
    /// collect a surrogate ID from and so can never sweep them, and they still satisfy search queries
    /// joining on <c>ResourceSurrogateId</c>: a hard-deleted resource that keeps coming back in search
    /// results. Scoping the delete to <c>@SurrogateIds</c> leaves the new version alive with no history
    /// instead, which is incomplete but coherent, and a later hard delete finishes it.
    /// </para>
    /// <para>
    /// The interleaving is forced, not raced for: the batch is a single command, so there is nowhere for the
    /// interceptor to stand inside it. A second connection takes an X lock on the resource's one
    /// <c>dbo.ReferenceSearchParam</c> row instead -- the first of the fifteen tables, so the batch stalls
    /// with its snapshot already taken, its shared read locks already released, and no other lock held --
    /// and the racing version is committed while it is stalled there.
    /// </para>
    /// <para>
    /// That racing version is written as the rows a concurrent PUT commits rather than by calling
    /// <c>CreateOrUpdateAsync</c>, and it has to be: <c>MergeResources</c> deletes the previous version's
    /// rows from all fifteen search-index tables, so a real PUT would want the very row this test is holding
    /// locked and would block rather than race -- an artifact of forcing the interleaving, not anything the
    /// production paths do to each other. Nothing about the hazard depends on which writer produced the
    /// rows; it depends only on a version being committed whose surrogate ID the snapshot never saw.
    /// </para>
    /// <para>
    /// So this proves a property of the final DELETE's predicate under an interleaving that was staged. It
    /// does NOT prove that the two real code paths compose safely when they contend for locks, because the
    /// lock and the target table here were chosen precisely so that they would not contend.
    /// <see cref="GivenRealWritesRacingRealHardDeletes_WhenNeitherIsStalled_ThenNoSearchIndexRowOutlivesItsResource"/>
    /// covers that second claim, at the cost of only catching a regression probabilistically. Neither test
    /// replaces the other; delete one and the pair stops meaning what it means.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenAConcurrentWriterVersionsTheResourceMidHardDelete_WhenTheHardDeleteCommits_ThenNoSearchIndexRowOutlivesItsResource()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);

        const string ReferenceTargetId = "hard-delete-race-target";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ReferenceTargetId), CancellationToken.None);

        const string ResourceId = "hard-delete-race-1";
        var resource = new ResourceWrapper("Patient", ResourceId, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{ResourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{ResourceId}"))
        {
            SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(ReferenceTargetId),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

        var sweptSurrogateId = await _database.ExecuteScalarAsync<long>(
            $"SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceId = '{ResourceId}' AND IsHistory = 0");
        await SearchIndexTableSeeder.InsertResourceWriteClaimAsync(_database, sweptSurrogateId, CancellationToken.None);
        await SearchIndexTableSeeder.AssertEverySearchIndexTableHasRowsAsync(_database, sweptSurrogateId, CancellationToken.None);

        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        long survivingSurrogateId;
        await using (var blocker = new SqlConnection(_database.ConnectionString))
        {
            await blocker.OpenAsync();
            await using var blockerTransaction = (SqlTransaction)await blocker.BeginTransactionAsync();

            // XLOCK without HOLDLOCK on purpose: exclusive row locks are held to the end of the transaction
            // anyway, and a range lock here could block the concurrent writer's own inserts, which is the
            // one thing that must stay free to run.
            await using (var takeLock = blocker.CreateCommand())
            {
                takeLock.Transaction = blockerTransaction;

                // CA2100 suppressed: the only interpolated value is a surrogate ID this test just read back
                // out of dbo.Resource -- a long, never caller or user input -- matching how every other SQL
                // helper in this suite is written.
#pragma warning disable CA2100
                takeLock.CommandText =
                    $"SELECT COUNT(*) FROM dbo.ReferenceSearchParam WITH (XLOCK, ROWLOCK) WHERE ResourceSurrogateId = {sweptSurrogateId}";
#pragma warning restore CA2100
                ((int)(await takeLock.ExecuteScalarAsync())!).ShouldBeGreaterThan(0);
            }

            var hardDelete = Task.Run(async () =>
                await _repository.HardDeleteResourceAsync(resourceTypeId, ResourceId, CancellationToken.None));

            await WaitUntilSomethingIsBlockedAsync(hardDelete);

            // The racing writer: a whole new version, with its own surrogate ID and its own search-index
            // row, committed after the hard delete's snapshot was taken. Its index row goes in
            // dbo.ResourceWriteClaim, the LAST of the fifteen tables, which the stalled batch has not
            // reached and so holds no lock in.
            survivingSurrogateId = sweptSurrogateId + 1;
            await _database.ExecuteNonQueryAsync(
                $"""
                UPDATE dbo.Resource SET IsHistory = 1
                WHERE ResourceTypeId = {resourceTypeId} AND ResourceSurrogateId = {sweptSurrogateId};

                INSERT INTO dbo.Resource
                    (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash, TransactionId)
                SELECT ResourceTypeId, ResourceId, Version + 1, 0, {survivingSurrogateId}, 0, 'PUT', RawResource, IsRawResourceMetaSet, SearchParamHash, NULL
                FROM dbo.Resource
                WHERE ResourceTypeId = {resourceTypeId} AND ResourceSurrogateId = {sweptSurrogateId};

                INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
                VALUES ({survivingSurrogateId}, 1, 'racing-writer');
                """,
                CancellationToken.None);

            (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceSurrogateId = {survivingSurrogateId} AND IsHistory = 0"))
                .ShouldBe(1, "the racing version was not committed, so nothing raced");

            await blockerTransaction.CommitAsync();
            await hardDelete.WaitAsync(TimeSpan.FromSeconds(60));
        }

        // The point of the whole test: nothing anywhere in the fifteen tables is left pointing at a resource
        // row that no longer exists.
        await AssertNoSearchIndexRowOutlivesItsResourceAsync();

        // The sweep still did its job for the versions it snapshotted...
        await SearchIndexTableSeeder.AssertEverySearchIndexTableIsEmptyAsync(_database, sweptSurrogateId, CancellationToken.None);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceSurrogateId = {sweptSurrogateId}")).ShouldBe(0);

        // ...and the version it never saw is still whole: its row, its index row, and its expiry.
        (await CountAsync($"SELECT COUNT(*) FROM dbo.Resource WHERE ResourceSurrogateId = {survivingSurrogateId} AND IsHistory = 0"))
            .ShouldBe(1, "the racing version's resource row was deleted even though its search-index rows were not swept");
        (await CountAsync($"SELECT COUNT(*) FROM dbo.ResourceWriteClaim WHERE ResourceSurrogateId = {survivingSurrogateId}")).ShouldBe(1);
        (await CountAsync($"SELECT COUNT(*) FROM dbo.ResourceTtl WHERE ResourceId = '{ResourceId}'"))
            .ShouldBe(1, "the surviving version is still live, so it must keep its expiry");

        var fetched = await _repository.GetAsync(new ResourceKey("Patient", ResourceId), CancellationToken.None);
        fetched.ShouldNotBeNull();
        fetched!.IsDeleted.ShouldBeFalse();
    }

    /// <summary>
    /// The same invariant as
    /// <see cref="GivenAConcurrentWriterVersionsTheResourceMidHardDelete_WhenTheHardDeleteCommits_ThenNoSearchIndexRowOutlivesItsResource"/>,
    /// but with both sides real and neither one stalled: a real <c>CreateOrUpdateAsync</c> and a real
    /// <c>HardDeleteResourceAsync</c> started together, over and over, with the interleaving left to the
    /// scheduler. That test stages the interleaving and therefore proves the final DELETE's predicate; this
    /// one cannot stage anything, and therefore is the only one of the pair that exercises the real merge
    /// path, the real lock ordering, and whatever the two do to each other when they contend. Keep both.
    /// <para>
    /// THIS IS NOT A FLAKY TEST, and the distinction matters enough to spell out, because "probabilistic"
    /// gets read as "flaky" and tests like this get deleted for a fault they do not have. With the code
    /// correct, the invariant holds on EVERY round whatever the interleaving, so the verdict is
    /// deterministic: it does not intermittently redden CI. What is probabilistic is its DETECTION POWER --
    /// with the code broken it only fails on rounds where the racing write happens to land inside the
    /// batch's window. Reliable when the code is right, probabilistic at catching a regression, is a
    /// perfectly good test. The unacceptable inverse -- reliable at catching regressions but intermittently
    /// red when correct -- is not what this is.
    /// </para>
    /// <para>
    /// Both operations are allowed to fail, and the outcomes are counted rather than asserted, because the
    /// two take their locks in opposite orders -- this batch does the fifteen index tables and then
    /// <c>dbo.Resource</c>, while <c>MergeResources</c> flips <c>dbo.Resource</c> first and then deletes the
    /// previous version's rows from the same fifteen -- which is the classic shape of a deadlock cycle. A
    /// deadlock victim is a rolled-back transaction and cannot orphan anything, so it is not a failure of
    /// this invariant. What is worth recording is that across 80 measured rounds NOT ONE deadlocked or threw
    /// at all: the two paths do interleave in practice rather than serialising, which is what makes the race
    /// reachable often enough to be worth watching. What is asserted alongside the invariant is that the two
    /// sides' measured intervals actually OVERLAPPED on at least one round, so that a run in which the two
    /// never once ran at the same time cannot pass silently -- see <see cref="RacedOperation"/> for why
    /// "both sides completed" is not the same claim and does not do that job.
    /// </para>
    /// <para>
    /// Round count was picked by measurement against the un-fixed code, not by taste -- see the constant.
    /// </para>
    /// </summary>
    [Fact]
    public async Task GivenRealWritesRacingRealHardDeletes_WhenNeitherIsStalled_ThenNoSearchIndexRowOutlivesItsResource()
    {
        await SearchIndexTableSeeder.SeedSearchParameterCatalogAsync(_database, CancellationToken.None);

        const string ReferenceTargetId = "hard-delete-live-race-target";
        await _repository.CreateOrUpdateAsync(BuildTestPatientWrapper(ReferenceTargetId), CancellationToken.None);

        var resourceTypeId = await _database.ExecuteScalarAsync<short>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        var roundsThatOrphanedRows = new List<string>();
        var roundsWhereBothSidesOverlapped = 0;

        for (var round = 0; round < LiveRaceRounds; round++)
        {
            var resourceId = $"hard-delete-live-race-{round}";
            var resource = new ResourceWrapper("Patient", resourceId, "1", DateTimeOffset.UtcNow,
                ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
                new ResourceRequest("PUT", $"Patient/{resourceId}"))
            {
                SearchIndices = SearchIndexTableSeeder.BuildSearchIndicesCoveringEverySearchIndexTable(ReferenceTargetId),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
            };
            await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);

            var orphansBefore = await CountSearchIndexRowsOutlivingTheirResourceAsync();

            var hardDeleteTask = StartRacedOperationAsync(() =>
                _repository.HardDeleteResourceAsync(resourceTypeId, resourceId, CancellationToken.None));
            var writeTask = StartRacedOperationAsync(async () =>
                await _repository.CreateOrUpdateAsync(resource, CancellationToken.None));

            var hardDelete = await hardDeleteTask;
            var write = await writeTask;
            if (hardDelete.OverlappedWith(write))
            {
                roundsWhereBothSidesOverlapped++;
            }

            var orphansAfter = await CountSearchIndexRowsOutlivingTheirResourceAsync();
            if (orphansAfter > orphansBefore)
            {
                var where = await FindSearchIndexRowsOutlivingTheirResourceAsync();
                roundsThatOrphanedRows.Add(
                    $"round {round} ({resourceId}) orphaned {orphansAfter - orphansBefore} row(s); " +
                    $"hard delete: {hardDelete.Outcome ?? "completed"}; write: {write.Outcome ?? "completed"}; " +
                    $"orphans now: {string.Join(" | ", where)}");
            }
        }

        // Reported before the invariant so that a failure carries the hit rate with it: how many rounds
        // orphaned rows, out of how many were actually in flight at the same time.
        roundsThatOrphanedRows.ShouldBeEmpty(
            $"{roundsThatOrphanedRows.Count} of {LiveRaceRounds} rounds left search-index rows with no resource behind them " +
            $"({roundsWhereBothSidesOverlapped} rounds had the two sides genuinely overlap)");

        // This failing does NOT mean the invariant broke. It means the two paths stopped interleaving, so
        // the assertion above had nothing to detect with and this test can no longer see what it claims to
        // watch. Something changed the concurrency model: a lock hint added to the hard-delete batch
        // (UPDLOCK/HOLDLOCK on its snapshot is the specific candidate, and note the deterministic test
        // would stay green through exactly that change), a new lock in the merge path, or the two
        // serialising for some other reason. Go and look at that, not at this test.
        //
        // What is counted is temporal OVERLAP -- the two operations' measured intervals intersecting --
        // and not, as an earlier version of this guard did, "neither side threw". Two operations can both
        // complete with no overlap whatever, so a completion count would sit at 40 out of 40 while the
        // race window was never once entered, which is a permanently green guard watching nothing. 80 of
        // 80 measured rounds overlapped, so a threshold of "at least one" has a great deal of headroom and
        // should not fire on a healthy tree.
        roundsWhereBothSidesOverlapped.ShouldBeGreaterThan(
            0,
            "no round had the write and the hard delete in flight at the same time, so the two paths are no longer interleaving and this test can no longer detect the orphaning it exists to watch for -- the concurrency model changed, look there rather than at this assertion");
    }

    /// <summary>
    /// One side of a race: how it ended (<c>null</c> for success, otherwise a short description) and the
    /// interval it actually occupied. Both sides are allowed to fail -- a deadlock victim rolls back and
    /// cannot orphan anything -- so the outcome is recorded rather than asserted. The interval is what
    /// lets the test tell interleaving from mere completion: two operations can both succeed with no
    /// temporal overlap at all, and a round like that exercises nothing.
    /// </summary>
    private readonly record struct RacedOperation(string? Outcome, long StartTimestamp, long EndTimestamp)
    {
        public bool OverlappedWith(RacedOperation other)
            => StartTimestamp < other.EndTimestamp && other.StartTimestamp < EndTimestamp;
    }

    /// <summary>
    /// Runs <paramref name="operation"/> on the thread pool, stamping a timestamp immediately either side
    /// of it. <see cref="Stopwatch"/> rather than <c>DateTime.UtcNow</c> because the system clock's tick is
    /// coarser than the operations being measured, which would collapse genuinely overlapping intervals
    /// onto a single instant and make the overlap test read false.
    /// </summary>
    private static Task<RacedOperation> StartRacedOperationAsync(Func<Task> operation)
        => Task.Run(async () =>
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                await operation();
                return new RacedOperation(null, start, Stopwatch.GetTimestamp());
            }
            catch (Exception ex)
            {
                var outcome = ex is SqlException sql ? $"{ex.GetType().Name} {sql.Number}" : ex.GetType().Name;
                return new RacedOperation(outcome, start, Stopwatch.GetTimestamp());
            }
        });

    /// <summary>
    /// Waits until the hard delete is really parked on the blocker's lock. A fixed delay would make the
    /// test's whole premise a guess; if nothing ever blocks, say so rather than racing on regardless.
    /// </summary>
    private async Task WaitUntilSomethingIsBlockedAsync(Task hardDelete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (hardDelete.IsCompleted)
            {
                await hardDelete; // surface its exception if it has one
                throw new InvalidOperationException(
                    "the hard delete finished without ever blocking, so the concurrent write cannot have landed mid-batch");
            }

            var blocked = await CountAsync(
                "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0 AND database_id = DB_ID()");
            if (blocked > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new InvalidOperationException("the hard delete never blocked on the lock this test took out");
    }

    /// <summary>
    /// A search-index row whose resource row is gone is unreachable: no hard delete will ever collect its
    /// surrogate ID again, because collecting one requires a <c>dbo.Resource</c> row to read it from.
    /// </summary>
    private async Task AssertNoSearchIndexRowOutlivesItsResourceAsync()
    {
        var orphans = await FindSearchIndexRowsOutlivingTheirResourceAsync();
        orphans.ShouldBeEmpty(
            "search-index rows are left with no resource row behind them -- nothing can ever sweep them, because collecting a surrogate ID requires a dbo.Resource row to read it from, and they still satisfy search queries joining on ResourceSurrogateId");
    }

    private async Task<int> CountSearchIndexRowsOutlivingTheirResourceAsync()
    {
        var total = 0;
        foreach (var table in SearchIndexTableSeeder.SearchIndexTables)
        {
            total += await CountAsync(
                $"""
                SELECT COUNT(*) FROM dbo.{table} AS s
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Resource AS r WHERE r.ResourceSurrogateId = s.ResourceSurrogateId)
                """);
        }

        return total;
    }

    /// <summary>
    /// Names every table holding orphaned rows and the surrogate IDs in them, so a failure says where to
    /// look instead of leaving the next person to re-derive it from a bare count.
    /// </summary>
    private async Task<IReadOnlyList<string>> FindSearchIndexRowsOutlivingTheirResourceAsync()
    {
        var found = new List<string>();
        foreach (var table in SearchIndexTableSeeder.SearchIndexTables)
        {
            var surrogateIds = await _database.ExecuteScalarAsync<string>(
                $"""
                SELECT ISNULL(STRING_AGG(CAST(s.ResourceSurrogateId AS VARCHAR(32)), ','), '')
                FROM (SELECT DISTINCT ResourceSurrogateId FROM dbo.{table}) AS s
                WHERE NOT EXISTS (SELECT 1 FROM dbo.Resource AS r WHERE r.ResourceSurrogateId = s.ResourceSurrogateId)
                """);

            if (surrogateIds.Length > 0)
            {
                found.Add($"dbo.{table} surrogate IDs {surrogateIds}");
            }
        }

        return found;
    }

    private Task<int> CountAsync(string sql) => _database.ExecuteScalarAsync<int>(sql);

    /// <summary>
    /// Switches this test class's own scratch database to snapshot reads. Run from <c>master</c> because a
    /// database cannot be altered from a connection inside it, and <c>WITH ROLLBACK IMMEDIATE</c> because
    /// the fixture's deploy leaves pooled connections behind that would otherwise block the change; the
    /// pool for this database is cleared on both sides so no killed connection is handed back out.
    /// <para>
    /// <see cref="SqlConnection.ClearAllPools"/> would do the same job and much more besides: it is
    /// process-wide, so it would also tear down the pools of every other integration test class running in
    /// parallel with this one. <see cref="SqlConnection.ClearPool"/> is scoped to one connection string,
    /// and this scratch database's connection string is the only one whose connections are in the way.
    /// </para>
    /// </summary>
    private async Task EnableReadCommittedSnapshotAsync()
    {
        var builder = new SqlConnectionStringBuilder(_database.ConnectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        ClearThisDatabasesConnectionPool();

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

        ClearThisDatabasesConnectionPool();
    }

    private void ClearThisDatabasesConnectionPool()
    {
        using var pooled = new SqlConnection(_database.ConnectionString);
        SqlConnection.ClearPool(pooled);
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
            ResetOrdinals();
            BeforeNonQueryAsync = actual => actual == ordinal ? Task.FromException(failure) : Task.CompletedTask;
        }

        public void Disarm() => BeforeNonQueryAsync = null;

        /// <summary>Restarts the ordinal count, for hooks assigned directly rather than through <see cref="FailBeforeNonQuery"/>.</summary>
        public void ResetOrdinals()
        {
            _nonQueryCount = 0;
            lock (_nonQueryCommands)
            {
                _nonQueryCommands.Clear();
            }
        }

        private readonly List<string> _nonQueryCommands = [];

        /// <summary>
        /// The command text of every non-query since the last <see cref="ResetOrdinals"/>, indexed by
        /// ordinal minus one -- including the one an injected failure stopped from running. Lets a test say
        /// which statement an ordinal actually is instead of asserting it in a comment.
        /// </summary>
        public IReadOnlyList<string> NonQueryCommands
        {
            get
            {
                lock (_nonQueryCommands)
                {
                    return [.. _nonQueryCommands];
                }
            }
        }

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
            ArgumentNullException.ThrowIfNull(command);
            await OnNonQueryAsync(command);
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

        private async Task OnNonQueryAsync(SqlCommand command)
        {
            var ordinal = Interlocked.Increment(ref _nonQueryCount);
            lock (_nonQueryCommands)
            {
                _nonQueryCommands.Add(command.CommandText);
            }

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
                ArgumentNullException.ThrowIfNull(command);
                await owner.OnNonQueryAsync(command);
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
