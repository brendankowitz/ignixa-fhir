using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

/// <summary>Spec §8.6 Recovery: past OrphanGrace, a job with a missing or terminal orchestration takes its recorded outcome or fails.</summary>
public sealed class ReindexJobReconcilerTests : IDisposable
{
    private static readonly DateTimeOffset PastGrace = Now - TimeSpan.FromMinutes(10);

    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();
    private readonly IOrchestrationServiceClient _runtime = Substitute.For<IOrchestrationServiceClient>();
    private readonly TestReindexJobLock _jobLock = new();
    private readonly TestSourceEventStore _events = new();
    private readonly ConformanceState _state = new();
    private readonly ReindexLifecycleEventWriter _lifecycle;
    private readonly ReindexJobReconciler _reconciler;

    public ReindexJobReconcilerTests()
    {
        _state.ApplyAndTrack(Activation());
        _lifecycle = new ReindexLifecycleEventWriter(_events, _state);
        _reconciler = new ReindexJobReconciler(
            new TaskHubClient(_runtime),
            _jobs,
            _lifecycle,
            _jobLock,
            Options.Create(new ReindexOptions { OrphanGrace = TimeSpan.FromMinutes(2) }),
            new FixedTimeProvider(Now),
            NullLogger<ReindexJobReconciler>.Instance);
    }

    private SearchParameterStatus Status => _state.GetSearchParameter("Patient", "custom")!.Status;

    public void Dispose() => _state.Dispose();

    [Fact]
    public async Task GivenRunningJobWithinOrphanGrace_WhenReconciled_ThenLockIsNotTaken()
    {
        var job = await StartedJobAsync(Now - TimeSpan.FromMinutes(1), orchestration: null);

        await _reconciler.ReconcileAsync(job, CancellationToken.None);

        _jobLock.Acquisitions.ShouldBe(0);
        (await GetJobAsync()).Status.ShouldBe("Running");
        Status.ShouldBe(SearchParameterStatus.Reindexing);
    }

    [Fact]
    public async Task GivenOrchestrationStillRunning_WhenReconciled_ThenNothingChanges()
    {
        var job = await StartedJobAsync(PastGrace, OrchestrationStatus.Running);

        await _reconciler.ReconcileAsync(job, CancellationToken.None);

        (await GetJobAsync()).Status.ShouldBe("Running");
        Status.ShouldBe(SearchParameterStatus.Reindexing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRunningJobPastGraceWithDeadOrchestration_WhenReconciled_ThenJobFailsAndParametersReturnToPending(
        bool orchestrationIsMissing)
    {
        var job = await StartedJobAsync(PastGrace, orchestrationIsMissing ? null : OrchestrationStatus.Failed);

        await _reconciler.ReconcileAsync(job, CancellationToken.None);

        var failed = await GetJobAsync();
        failed.Status.ShouldBe("Failed");
        failed.EndDate.ShouldBe(Now);
        failed.ErrorMessage.ShouldContain(orchestrationIsMissing ? "missing" : "Failed");
        Status.ShouldBe(SearchParameterStatus.Pending);
        _state.GetSearchParameter("Patient", "custom")!.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenQueuedJobPastGraceWhoseOrchestrationWasNeverCreated_WhenReconciled_ThenRowIsDeleted()
    {
        var job = await CreateJobAsync("Queued", PastGrace);
        _runtime.GetOrchestrationStateAsync("job", false).Returns([]);

        await _reconciler.ReconcileAsync(job, CancellationToken.None);

        (await _jobs.GetAsync("job", 1, CancellationToken.None)).ShouldBeNull();
        Status.ShouldBe(SearchParameterStatus.Pending);
    }

    [Theory]
    [InlineData(true, "Completed", SearchParameterStatus.Enabled)]
    [InlineData(false, "Failed", SearchParameterStatus.Pending)]
    public async Task GivenLifecycleOutcomeAppendedBeforeTheFinalWriteWasLost_WhenReconciledTwice_ThenTheJobTakesThatOutcomeWithoutNewEvents(
        bool succeeded,
        string expectedJobStatus,
        SearchParameterStatus expectedParameterStatus)
    {
        var job = await StartedJobAsync(PastGrace, OrchestrationStatus.Failed);
        await _lifecycle.CompleteAsync(
            "job",
            [new ReindexTargetCompletion(Target(), succeeded, 12, succeeded ? null : "worker failed")],
            TimeSpan.Zero,
            CancellationToken.None);
        var appendedBefore = _events.Appended.Count;

        await _reconciler.ReconcileAsync(job, CancellationToken.None);
        await _reconciler.ReconcileAsync(job, CancellationToken.None);

        var reconciled = await GetJobAsync();
        reconciled.Status.ShouldBe(expectedJobStatus);
        reconciled.Result!["success"]!.GetValue<bool>().ShouldBe(succeeded);
        _events.Appended.Count.ShouldBe(appendedBefore);
        Status.ShouldBe(expectedParameterStatus);
    }

    /// <summary>A Running job that started <c>Patient.custom</c>; a null orchestration status means it is missing.</summary>
    private async Task<BackgroundJob<ReindexJobDefinition>> StartedJobAsync(
        DateTimeOffset lastObserved,
        OrchestrationStatus? orchestration)
    {
        await _lifecycle.StartAsync("job", [Target()], CancellationToken.None);
        _runtime.GetOrchestrationStateAsync("job", false).Returns(orchestration is { } status
            ? [new OrchestrationState { OrchestrationInstance = new() { InstanceId = "job" }, OrchestrationStatus = status }]
            : []);
        return await CreateJobAsync("Running", lastObserved);
    }

    private async Task<BackgroundJob<ReindexJobDefinition>> CreateJobAsync(string status, DateTimeOffset lastObserved)
    {
        await _jobs.CreateAsync(
            Job(status, definition: CreateJobDefinition([Target()], targetEventId: 1), observed: lastObserved),
            CancellationToken.None);
        return await GetJobAsync();
    }

    private async Task<BackgroundJob<ReindexJobDefinition>> GetJobAsync() =>
        (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
}
