using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.SqlServer.EventStore;
using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlEventAndJobPersistenceContractTests(SqlPersistenceContractFixture fixture) : IClassFixture<SqlPersistenceContractFixture>
{
    private TestTenantDatabase Database => fixture.Database;

    [Theory]
    [InlineData(699)]
    [InlineData(700)]
    [InlineData(2500)]
    public async Task GivenALargeEventBatch_WhenAppended_ThenOrderingTimestampAndCutoffArePreserved(int count)
    {
        var store = CreateEventStore();
        var stream = Guid.NewGuid().ToString();
        var events = Enumerable.Range(0, count).Select(i => Event(stream, $"package-{i}")).ToArray();

        var appended = await store.AppendAsync(events, CancellationToken.None);
        var persisted = await ReadStreamAsync(store, stream);

        appended.Count.ShouldBe(count);
        persisted.Count.ShouldBe(count);
        persisted.Select(e => ((PackageDeactivated)e.Data).PackageId)
            .ShouldBe(Enumerable.Range(0, count).Select(i => $"package-{i}"));
        persisted.Select(e => e.EventId).ShouldBe(appended.Select(e => e.EventId));
        persisted.Select(e => e.Timestamp).Distinct().ShouldBe([appended[0].Timestamp]);
        persisted.ShouldAllBe(e => e.TransactionId == 100);
        appended.ShouldAllBe(e => e.TransactionId == 0);
    }

    [Fact]
    public async Task GivenALaterInvisibleAllocation_WhenAppending_ThenOnlyTheNonzeroVisibleCutoffIsPersisted()
    {
        var store = CreateEventStore();
        var stream = Guid.NewGuid().ToString();

        var appended = await store.AppendAsync([Event(stream, "visible-cutoff")], CancellationToken.None);
        var persisted = await ReadStreamAsync(store, stream);

        appended.Single().TransactionId.ShouldBe(0);
        persisted.Single().TransactionId.ShouldBe(100);
    }

    [Fact]
    public async Task GivenAnInvalidEventInALaterChunk_WhenAppending_ThenEveryChunkIsRolledBack()
    {
        var store = CreateEventStore();
        var stream = Guid.NewGuid().ToString();
        var existing = await store.AppendAsync([Event(stream, "existing")], CancellationToken.None);
        var events = Enumerable.Range(0, 2000).Select(i => Event(stream, $"package-{i}")).ToArray();
        events[1500] = new NewSourceEvent(stream, new string('x', 101), new PackageDeactivated("invalid", "1", "test"));

        var error = await Should.ThrowAsync<SqlException>(() => store.AppendAsync(events, CancellationToken.None));

        // A truncation in the later chunk, not the old RPC parameter-limit error before any insert.
        (error.Number is 2628 or 8152).ShouldBeTrue($"Expected truncation, got SQL {error.Number}: {error.Message}");
        (await ReadStreamAsync(store, stream)).Select(e => e.EventId).ShouldBe(existing.Select(e => e.EventId));
    }

    [Theory]
    [InlineData(TenantMode.Isolated, 1)]
    [InlineData(TenantMode.Distributed, 2)]
    public async Task GivenAnExistingOwner_WhenReplacementChangesOwner_ThenUpdateIsRejectedWithoutWriting(
        TenantMode mode, int requester)
    {
        var repository = CreateJobs(mode);
        var job = Job();
        await repository.CreateAsync(job);
        job.Definition = Job(tenantId: 2).Definition;
        job.Status = "Running";

        await Should.ThrowAsync<InvalidOperationException>(() => repository.UpdateAsync(job, requester));

        var persisted = await CreateJobs(TenantMode.Isolated).GetAsync(job.JobId, 1);
        persisted.ShouldNotBeNull();
        persisted.Status.ShouldBe("Queued");
        persisted.Definition.TenantId.ShouldBe(1);
        (await CreateJobs(TenantMode.Isolated).GetAsync(job.JobId, 2)).ShouldBeNull();
    }

    [Theory]
    [InlineData(TenantMode.Isolated)]
    [InlineData(TenantMode.Distributed)]
    public async Task GivenRowAndJsonOwnersDisagree_WhenMaterialized_ThenReadsAndMutationsRejectTheRow(TenantMode mode)
    {
        var repository = CreateJobs(mode);
        var job = Job();
        await repository.CreateAsync(job);
        using var corrupt = new SqlCommand("""
            UPDATE dbo.BackgroundJobs SET Definition = JSON_MODIFY(Definition, '$.TenantId', 2)
            WHERE TenantId = 1 AND JobId = @id
            """);
        corrupt.Parameters.AddWithValue("@id", job.JobId);
        await Database.SqlExecutionService.ExecuteNonQueryAsync(Database.TenantId, corrupt, CancellationToken.None);

        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() => repository.GetAsync(job.JobId, 1));
            await Should.ThrowAsync<InvalidOperationException>(() => repository.GetAsync(job.JobId, 2));
            await Should.ThrowAsync<InvalidOperationException>(() => repository.ListAsync());
            await Should.ThrowAsync<InvalidOperationException>(() => repository.UpdateAsync(job, 1));
            await Should.ThrowAsync<InvalidOperationException>(() => repository.DeleteAsync(job.JobId, 1));
        }
        finally
        {
            using var cleanup = new SqlCommand("DELETE dbo.BackgroundJobs WHERE JobId = @id");
            cleanup.Parameters.AddWithValue("@id", job.JobId);
            await Database.SqlExecutionService.ExecuteNonQueryAsync(Database.TenantId, cleanup, CancellationToken.None);
        }
    }

    [Fact]
    public async Task GivenDistributedMode_WhenAnotherShardUpdatesTheOwnerUnchanged_ThenTheStoredOwnerIsUsed()
    {
        var repository = CreateJobs(TenantMode.Distributed);
        var job = Job();
        await repository.CreateAsync(job);
        job.Status = "Running";

        await repository.UpdateAsync(job, 2);

        var updated = await CreateJobs(TenantMode.Isolated).GetAsync(job.JobId, 1);
        updated.ShouldNotBeNull();
        updated.Status.ShouldBe("Running");
        updated.Definition.TenantId.ShouldBe(1);
        await repository.DeleteAsync(job.JobId, 2);
        (await repository.GetAsync(job.JobId, 1)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenIsolatedMode_WhenAnotherTenantClaimsTheJob_ThenTheExistingOwnerRemainsAuthorized()
    {
        var repository = CreateJobs(TenantMode.Isolated);
        var job = Job();
        await repository.CreateAsync(job);
        var spoofed = Job(job.JobId);
        spoofed.Definition = Job(tenantId: 2).Definition;

        await Should.ThrowAsync<InvalidOperationException>(() => repository.UpdateAsync(spoofed, 2));
        await Should.ThrowAsync<InvalidOperationException>(() => repository.DeleteAsync(job.JobId, 2));

        (await repository.GetAsync(job.JobId, 2)).ShouldBeNull();
        (await repository.GetAsync(job.JobId, 1)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAJobRemovedAfterAuthorization_WhenMutating_ThenTheLostWriteIsReported(bool delete)
    {
        var job = Job();
        await CreateJobs(TenantMode.Isolated).CreateAsync(job);
        var sql = new ConcurrentDeleteSqlService(Database.SqlExecutionService, job.JobId);
        var repository = CreateJobs(TenantMode.Isolated, sql);

        await Should.ThrowAsync<InvalidOperationException>(() => delete
            ? repository.DeleteAsync(job.JobId, 1)
            : repository.UpdateAsync(job, 1));

        (await CreateJobs(TenantMode.Isolated).GetAsync(job.JobId, 1)).ShouldBeNull();
    }

    private SqlServerSourceEventStore CreateEventStore() => new(
        Database.SqlExecutionService, Database.TenantId, NullLogger<SqlServerSourceEventStore>.Instance);

    private SqlServerBackgroundJobRepository<ExportJobDefinition> CreateJobs(TenantMode mode, ISqlExecutionService? sql = null)
        => new(sql ?? Database.SqlExecutionService, Database.TenantId, new ModeOnlyTenantStore(mode),
            NullLogger<SqlServerBackgroundJobRepository<ExportJobDefinition>>.Instance);

    private static BackgroundJob<ExportJobDefinition> Job(string? id = null, int tenantId = 1) => new()
    {
        JobId = id ?? Guid.NewGuid().ToString(),
        JobType = 1,
        Status = "Queued",
        Definition = new ExportJobDefinition
        {
            TenantId = tenantId,
            ResourceTypes = ["Patient"],
            TypeFilters = new Dictionary<string, string>(),
            OutputFormat = "ndjson",
            OutputPath = "/exports/persistence-contract",
        },
    };

    private static NewSourceEvent Event(string stream, string package) =>
        new(stream, nameof(PackageDeactivated), new PackageDeactivated(package, "1", "test"));

    private static async Task<List<SourceEvent>> ReadStreamAsync(SqlServerSourceEventStore store, string stream)
    {
        var events = new List<SourceEvent>();
        await foreach (var item in store.ReadStreamAsync(stream, CancellationToken.None))
        {
            events.Add(item);
        }

        return events;
    }

    private sealed class ModeOnlyTenantStore(TenantMode mode) : ITenantConfigurationStore
    {
        public TenantMode Mode => mode;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => ValueTask.FromResult<TenantConfiguration?>(null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<TenantConfiguration>>([]);

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TenantConfiguration?>(null);
    }

    private sealed class ConcurrentDeleteSqlService(ISqlExecutionService inner, string jobId) : ISqlExecutionService
    {
        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);

        public async Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            // The competing deletion really commits between the repository's read and write.
            using var competingDelete = new SqlCommand("DELETE dbo.BackgroundJobs WHERE TenantId = 1 AND JobId = @id");
            competingDelete.Parameters.AddWithValue("@id", jobId);
            await inner.ExecuteNonQueryAsync(tenantId, competingDelete, cancellationToken);
            return await inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
        }

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

        public Task ExecuteInTransactionAsync(
            int tenantId, Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken)
            => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }
}
