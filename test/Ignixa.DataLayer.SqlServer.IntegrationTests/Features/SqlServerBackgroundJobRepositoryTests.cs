using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features;

/// <summary>
/// Behavioural contract for the SQL-backed background job repository.
/// <para>
/// Unlike the other Phase F ports, these tests could not be written against the EF implementation first:
/// that implementation models <c>dbo.BackgroundJobs</c> without the <c>TenantId</c> column the deployed
/// table declares <c>NOT NULL</c> and includes in its primary key, so it cannot insert a row at all. The
/// assertions here therefore encode the rules that implementation *expressed* -- mode-dependent tenant
/// validation, <c>CreateDate DESC</c> ordering, and the deliberately different error shapes between Get,
/// Update and Delete -- against the schema that actually exists.
/// </para>
/// </summary>
public class SqlServerBackgroundJobRepositoryTests : IAsyncLifetime
{
    private const int OwnerTenantId = 7;
    private const int OtherTenantId = 9;

    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private SqlServerBackgroundJobRepository<ExportJobDefinition> CreateRepository(TenantMode mode = TenantMode.Isolated)
        => new(
            _database.SqlExecutionService,
            _database.TenantId,
            new ModeOnlyTenantStore(mode),
            NullLogger<SqlServerBackgroundJobRepository<ExportJobDefinition>>.Instance);

    // dbo.BackgroundJobs.JobId is NVARCHAR(36) -- exactly the width of a dashed GUID, with no room for a
    // readable prefix. Anything longer fails the insert outright rather than truncating silently.
    private static string NewJobId() => Guid.NewGuid().ToString();

    private static BackgroundJob<ExportJobDefinition> Job(
        string jobId,
        int tenantId,
        int jobType = 1,
        string status = "Queued",
        DateTimeOffset? createDate = null) => new()
        {
            JobId = jobId,
            JobType = jobType,
            Status = status,
            CreateDate = createDate ?? DateTimeOffset.UtcNow,
            Definition = new ExportJobDefinition
            {
                TenantId = tenantId,
                ResourceTypes = ["Patient"],
                TypeFilters = new Dictionary<string, string>(),
                OutputFormat = "ndjson",
                OutputPath = "/exports/test",
            },
        };

    [Fact]
    public async Task GivenACreatedJob_WhenFetched_ThenTheDefinitionRoundTrips()
    {
        var repository = CreateRepository();
        var jobId = NewJobId();

        await repository.CreateAsync(Job(jobId, OwnerTenantId), CancellationToken.None);

        var fetched = await repository.GetAsync(jobId, OwnerTenantId, CancellationToken.None);

        fetched.ShouldNotBeNull();
        fetched.JobId.ShouldBe(jobId);
        fetched.Status.ShouldBe("Queued");
        fetched.Definition.TenantId.ShouldBe(OwnerTenantId);
        fetched.Definition.ResourceTypes.ShouldBe(["Patient"]);
        fetched.Definition.OutputFormat.ShouldBe("ndjson");
    }

    [Fact]
    public async Task GivenTheTenantIdColumn_WhenAJobIsCreated_ThenItIsPersistedFromTheDefinition()
    {
        // The column is half the primary key and NOT NULL. This is the assertion the EF implementation
        // could never have satisfied, so it is pinned explicitly rather than left implied by a round-trip.
        var repository = CreateRepository();
        var jobId = NewJobId();

        await repository.CreateAsync(Job(jobId, OwnerTenantId), CancellationToken.None);

        var storedTenantId = await _database.ExecuteScalarAsync<int>(
            $"SELECT TenantId FROM dbo.BackgroundJobs WHERE JobId = '{jobId}'", CancellationToken.None);

        storedTenantId.ShouldBe(OwnerTenantId);
    }

    [Fact]
    public async Task GivenJobsOfSeveralTypes_WhenListedWithATypeFilter_ThenOnlyThatTypeIsReturnedNewestFirst()
    {
        var repository = CreateRepository();
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        var oldExport = NewJobId();
        var newExport = NewJobId();
        var importJob = NewJobId();

        await repository.CreateAsync(Job(oldExport, OwnerTenantId, jobType: 1, createDate: older), CancellationToken.None);
        await repository.CreateAsync(Job(newExport, OwnerTenantId, jobType: 1, createDate: newer), CancellationToken.None);
        await repository.CreateAsync(Job(importJob, OwnerTenantId, jobType: 2, createDate: newer), CancellationToken.None);

        var exports = await repository.ListAsync(jobType: 1, CancellationToken.None);

        exports.Select(j => j.JobId).ShouldBe([newExport, oldExport]);
    }

    [Fact]
    public async Task GivenJobsOwnedByDifferentTenants_WhenListed_ThenBothAreReturned()
    {
        // ListAsync takes no tenant and does not filter by one. Callers needing a scoped view filter on
        // Definition.TenantId themselves; changing that here would silently narrow every existing caller.
        var repository = CreateRepository();
        var mine = NewJobId();
        var theirs = NewJobId();

        await repository.CreateAsync(Job(mine, OwnerTenantId), CancellationToken.None);
        await repository.CreateAsync(Job(theirs, OtherTenantId), CancellationToken.None);

        var all = await repository.ListAsync(jobType: null, CancellationToken.None);

        all.Select(j => j.JobId).ShouldContain(mine);
        all.Select(j => j.JobId).ShouldContain(theirs);
    }

    [Fact]
    public async Task GivenAnExistingJob_WhenUpdated_ThenTheStatusChangesAndTheHeartbeatIsRefreshed()
    {
        var repository = CreateRepository();
        var jobId = NewJobId();
        var original = Job(jobId, OwnerTenantId);
        original.HeartbeatDate = DateTimeOffset.UtcNow.AddHours(-3);

        await repository.CreateAsync(original, CancellationToken.None);

        original.Status = "Running";
        original.Worker = "worker-1";
        await repository.UpdateAsync(original, OwnerTenantId, CancellationToken.None);

        var updated = await repository.GetAsync(jobId, OwnerTenantId, CancellationToken.None);

        updated.ShouldNotBeNull();
        updated.Status.ShouldBe("Running");
        updated.Worker.ShouldBe("worker-1");

        // Stamped by the repository, not carried from the model -- an update always refreshes liveness.
        updated.HeartbeatDate.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task GivenNoSuchJob_WhenUpdated_ThenItThrows()
    {
        var repository = CreateRepository();

        await Should.ThrowAsync<InvalidOperationException>(
            () => repository.UpdateAsync(Job(NewJobId(), OwnerTenantId), OwnerTenantId, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNoSuchJob_WhenDeleted_ThenItIsSilentlyIgnored()
    {
        // Deliberately asymmetric with Update above: deleting something already absent is not an error.
        var repository = CreateRepository();

        await Should.NotThrowAsync(
            () => repository.DeleteAsync(NewJobId(), OwnerTenantId, CancellationToken.None));
    }

    [Fact]
    public async Task GivenAnExistingJob_WhenDeleted_ThenItIsGone()
    {
        var repository = CreateRepository();
        var jobId = NewJobId();
        await repository.CreateAsync(Job(jobId, OwnerTenantId), CancellationToken.None);

        await repository.DeleteAsync(jobId, OwnerTenantId, CancellationToken.None);

        (await repository.GetAsync(jobId, OwnerTenantId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenIsolatedMode_WhenAnotherTenantFetchesTheJob_ThenItIsHiddenRatherThanRejected()
    {
        var repository = CreateRepository(TenantMode.Isolated);
        var jobId = NewJobId();
        await repository.CreateAsync(Job(jobId, OwnerTenantId), CancellationToken.None);

        var fetched = await repository.GetAsync(jobId, OtherTenantId, CancellationToken.None);

        // Null, not an exception: an unauthorised tenant must not be able to distinguish "exists but not
        // yours" from "does not exist".
        fetched.ShouldBeNull();
    }

    [Fact]
    public async Task GivenIsolatedMode_WhenAnotherTenantUpdatesOrDeletes_ThenItThrows()
    {
        var repository = CreateRepository(TenantMode.Isolated);
        var jobId = NewJobId();
        var job = Job(jobId, OwnerTenantId);
        await repository.CreateAsync(job, CancellationToken.None);

        await Should.ThrowAsync<InvalidOperationException>(
            () => repository.UpdateAsync(job, OtherTenantId, CancellationToken.None));
        await Should.ThrowAsync<InvalidOperationException>(
            () => repository.DeleteAsync(jobId, OtherTenantId, CancellationToken.None));
    }

    [Fact]
    public async Task GivenDistributedMode_WhenAnotherTenantFetchesTheJob_ThenItIsReturned()
    {
        // Distributed mode shards a single customer across databases, so every job in reach already belongs
        // to that customer and the ownership check is skipped entirely.
        var repository = CreateRepository(TenantMode.Distributed);
        var jobId = NewJobId();
        await repository.CreateAsync(Job(jobId, OwnerTenantId), CancellationToken.None);

        var fetched = await repository.GetAsync(jobId, OtherTenantId, CancellationToken.None);

        fetched.ShouldNotBeNull();
        fetched.JobId.ShouldBe(jobId);
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
}
