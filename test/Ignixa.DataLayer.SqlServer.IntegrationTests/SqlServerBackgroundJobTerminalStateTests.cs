using System.Text.Json.Nodes;
using Ignixa.DataLayer.SqlServer.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerBackgroundJobTerminalStateTests(SqlPersistenceContractFixture fixture)
    : IClassFixture<SqlPersistenceContractFixture>
{
    private const string ConflictType = "Ignixa.Domain.Exceptions.BackgroundJobUpdateConflictException";

    [Theory]
    [InlineData("Completed", "Running")]
    [InlineData("Completed", "Completed")]
    [InlineData("Completed", "Cancelled")]
    [InlineData("Failed", "Running")]
    [InlineData("Failed", "Completed")]
    [InlineData("Cancelled", "Running")]
    [InlineData("Cancelled", "Completed")]
    public async Task GivenATerminalJob_WhenAStaleSnapshotIsUpdated_ThenTheFirstTerminalMetadataRemains(
        string terminalStatus, string staleStatus)
    {
        var repository = CreateRepository();
        var job = NewJob();
        await repository.CreateAsync(job);
        var stale = (await repository.GetAsync(job.JobId, 1))!;
        var terminal = (await repository.GetAsync(job.JobId, 1))!;
        Complete(terminal, terminalStatus);
        await repository.UpdateAsync(terminal, 1);
        var expected = (await repository.GetAsync(job.JobId, 1))!;
        stale.Status = staleStatus;
        stale.Progress = JsonNode.Parse("""{"processedResources":999}""");
        stale.Result = JsonNode.Parse("""{"outputFiles":{"Patient":["wrong.ndjson"]}}""");
        stale.ErrorMessage = "late error";
        stale.CancelRequested = true;

        var conflict = await Should.ThrowAsync<Exception>(() => repository.UpdateAsync(stale, 1));

        conflict.GetType().FullName.ShouldBe(ConflictType);
        AssertUnchanged((await repository.GetAsync(job.JobId, 1))!, expected);
    }

    [Theory]
    [InlineData("Running")]
    [InlineData("Completed")]
    [InlineData("Cancelled")]
    public async Task GivenCompletionBetweenAuthorizationAndMutation_WhenUpdating_ThenTheAtomicPredicateRejectsTheStaleWrite(
        string staleStatus)
    {
        var repository = CreateRepository();
        var job = NewJob();
        await repository.CreateAsync(job);
        var stale = (await repository.GetAsync(job.JobId, 1))!;
        var terminal = (await repository.GetAsync(job.JobId, 1))!;
        Complete(terminal, "Completed");
        BackgroundJob<ExportJobDefinition>? expected = null;
        var racing = CreateRepository(new BeforeMutationExecutionService(
            fixture.Database.SqlExecutionService,
            async () =>
            {
                await repository.UpdateAsync(terminal, 1);
                expected = (await repository.GetAsync(job.JobId, 1))!;
            }));
        stale.Status = staleStatus;

        var conflict = await Should.ThrowAsync<Exception>(() => racing.UpdateAsync(stale, 1));

        conflict.GetType().FullName.ShouldBe(ConflictType);
        expected.ShouldNotBeNull();
        AssertUnchanged((await repository.GetAsync(job.JobId, 1))!, expected);
    }

    [Theory]
    [InlineData(TenantMode.Isolated, 1)]
    [InlineData(TenantMode.Distributed, 2)]
    public async Task GivenAnAuthorizedNonterminalJob_WhenUpdated_ThenItRemainsWritable(TenantMode mode, int requester)
    {
        var repository = CreateRepository(mode: mode);
        var job = NewJob();
        await repository.CreateAsync(job);
        job.Status = "Running";
        job.Progress = JsonNode.Parse("""{"processedResources":4}""");

        await repository.UpdateAsync(job, requester);

        var current = (await repository.GetAsync(job.JobId, 1))!;
        current.Status.ShouldBe("Running");
        current.Progress!["processedResources"]!.GetValue<int>().ShouldBe(4);
    }

    [Fact]
    public async Task GivenAMissingJob_WhenUpdated_ThenTheFailureIsNotATerminalConflict()
    {
        var error = await Should.ThrowAsync<InvalidOperationException>(() => CreateRepository().UpdateAsync(NewJob(), 1));
        error.GetType().FullName.ShouldNotBe(ConflictType);
    }

    private SqlServerBackgroundJobRepository<ExportJobDefinition> CreateRepository(
        ISqlExecutionService? sql = null, TenantMode mode = TenantMode.Isolated)
        => new(sql ?? fixture.Database.SqlExecutionService, fixture.Database.TenantId,
            new ModeOnlyTenantStore(mode), NullLogger<SqlServerBackgroundJobRepository<ExportJobDefinition>>.Instance);

    private static BackgroundJob<ExportJobDefinition> NewJob() => new()
    {
        JobId = Guid.NewGuid().ToString(), JobType = 1, Status = "Queued",
        Definition = new ExportJobDefinition
        {
            TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
            OutputFormat = "ndjson", OutputPath = "/exports/terminal-contract",
        },
    };

    private static void Complete(BackgroundJob<ExportJobDefinition> job, string status)
    {
        job.Status = status;
        job.EndDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        job.Progress = JsonNode.Parse("""{"processedResources":42,"nested":{"complete":true}}""");
        job.Result = JsonNode.Parse("""{"totalResources":42,"outputFiles":{"Patient":["partition/1/Patient.ndjson"]}}""");
        job.ErrorMessage = status == "Failed" ? "first failure" : null;
        job.Worker = "winning-worker";
    }

    private static void AssertUnchanged(BackgroundJob<ExportJobDefinition> actual, BackgroundJob<ExportJobDefinition> expected)
    {
        actual.Status.ShouldBe(expected.Status);
        actual.EndDate.ShouldBe(expected.EndDate);
        actual.HeartbeatDate.ShouldBe(expected.HeartbeatDate);
        actual.ErrorMessage.ShouldBe(expected.ErrorMessage);
        actual.Worker.ShouldBe(expected.Worker);
        actual.CancelRequested.ShouldBe(expected.CancelRequested);
        actual.Progress!.ToJsonString().ShouldBe(expected.Progress!.ToJsonString());
        actual.Result!.ToJsonString().ShouldBe(expected.Result!.ToJsonString());
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

    private sealed class BeforeMutationExecutionService(ISqlExecutionService inner, Func<Task> complete) : ISqlExecutionService
    {
        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);

        public async Task<int> ExecuteNonQueryAsync(
            int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            await complete();
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
