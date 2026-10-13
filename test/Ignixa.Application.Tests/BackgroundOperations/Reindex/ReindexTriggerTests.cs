using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class ReindexTriggerTests : IDisposable
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IOrchestrationServiceClient _runtime = Substitute.For<IOrchestrationServiceClient>();
    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();
    private ConformanceState _state = new();
    private readonly TestReindexJobLock _jobLock = new();

    public ReindexTriggerTests()
    {
        _state.ApplyAndTrack(Activation(eventId: 42));
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(new NoReindexWorkResult("No resources need reindexing."));
    }

    public void Dispose() => _state.Dispose();

    // A due job is not requested while reindex cannot run: an activation reports why, and ticks stay quiet.
    [Theory]
    [InlineData(false, true, false, "Automatic reindex is disabled; parameters remain Pending.")]
    [InlineData(true, false, false, "Reindex is unavailable because the feature is disabled; parameters remain Pending.")]
    [InlineData(true, true, true, "Reindex is unavailable for tenant 3; parameters remain Pending.")]
    public async Task GivenAutomaticReindexCannotRun_WhenActivationOrTickTriggers_ThenThePendingReasonIsReportedWithoutARequest(
        bool autoStart,
        bool enabled,
        bool withFileSystemTenant,
        string expectedMessage)
    {
        var trigger = CreateTrigger(
            autoStart: autoStart,
            enabled: enabled,
            tenants: withFileSystemTenant ? [Tenant(1, "SqlServer"), Tenant(3, "FileSystem")] : []);

        var result = await trigger.RequestReindexAsync("activation", CancellationToken.None);
        await trigger.ReconcileAsync(CancellationToken.None);

        result.ShouldBe(new ReindexTriggerResult(null, false, expectedMessage));
        await AssertNoJobRequestedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenACreatedOrActiveJob_WhenActivationTriggers_ThenItsIdIsReported(bool alreadyActive)
    {
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(alreadyActive ? new ActiveReindexJobResult("job-1") : new ReindexJobCreatedResult("job-1"));

        var result = await CreateTrigger().RequestReindexAsync("activation", CancellationToken.None);

        result.ShouldBe(new ReindexTriggerResult("job-1", alreadyActive, null));
        await AssertJobRequestedAsync(ReindexTriggerKind.Activation);
    }

    [Fact]
    public async Task GivenNoReindexWork_WhenActivationTriggers_ThenTheReasonIsReportedWithoutDeferral()
    {
        var result = await CreateTrigger().RequestReindexAsync("activation", CancellationToken.None);

        result.ShouldBe(new ReindexTriggerResult(null, false, "No resources need reindexing."));
    }

    // Operational failures defer to periodic reconciliation; programmer errors fail fast.
    [Theory]
    [InlineData(typeof(IOException), false)]
    [InlineData(typeof(TimeoutException), false)]
    [InlineData(typeof(InvalidOperationException), true)]
    [InlineData(typeof(ArgumentException), true)]
    [InlineData(typeof(NullReferenceException), true)]
    public async Task GivenAFailedJobRequest_WhenActivationTriggers_ThenOnlyAProgrammerErrorPropagates(Type failure, bool propagates)
    {
        FailJobRequests(failure);
        ReindexTriggerResult? result = null;

        var exception = await Record.ExceptionAsync(async () =>
            result = await CreateTrigger().RequestReindexAsync("activation", CancellationToken.None));

        if (propagates)
        {
            exception.ShouldBeOfType(failure);
        }
        else
        {
            exception.ShouldBeNull();
            result!.Deferred.ShouldBeTrue();
            result.JobId.ShouldBeNull();
            result.Message.ShouldContain("periodic reconciliation");
        }
    }

    [Fact]
    public async Task GivenCancellation_WhenActivationTriggers_ThenCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ => throw new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateTrigger().RequestReindexAsync("activation", cancellation.Token));
    }

    [Fact]
    public async Task GivenUnsupportedResult_WhenActivationTriggers_ThenItFailsFast()
    {
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(new UnsupportedReindexResult());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            CreateTrigger().RequestReindexAsync("activation", CancellationToken.None));

        exception.Message.ShouldContain(nameof(UnsupportedReindexResult));
    }

    // N1: neither a finished job's own Pending parameters nor an empty Pending set start a job.
    [Theory]
    [InlineData(null)]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task GivenNothingNewerThanTheLatestFinishedJob_WhenTickRuns_ThenNeitherLockNorRequestHappens(string? status)
    {
        if (status is null)
        {
            _state.Dispose();
            _state = new ConformanceState();
        }
        else
        {
            await _jobs.CreateAsync(Job(status, definition: CreateJobDefinition(targetEventId: 42)), CancellationToken.None);
        }

        await CreateTrigger().ReconcileAsync(CancellationToken.None);

        _jobLock.Acquisitions.ShouldBe(0);
        await AssertNoJobRequestedAsync();
    }

    // N1: an activation newer than the latest finished job's target is due once that job has finished.
    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    public async Task GivenActivationArrivesWhileJobRuns_WhenJobFinishes_ThenFollowUpIsRequestedOnTheNextTick(string finished)
    {
        await _jobs.CreateAsync(Job("Running", definition: CreateJobDefinition(targetEventId: 41)), CancellationToken.None);
        var trigger = CreateTrigger();

        await trigger.ReconcileAsync(CancellationToken.None);
        await AssertNoJobRequestedAsync();
        var running = (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
        running.Status = finished;
        await _jobs.UpdateAsync(running, 1, CancellationToken.None);
        await trigger.ReconcileAsync(CancellationToken.None);

        await AssertJobRequestedAsync(ReindexTriggerKind.Reconciliation);
    }

    [Fact]
    public async Task GivenAutoStartIsFalse_WhenTickFindsOrphanedJob_ThenItIsRecoveredButNoJobIsRequested()
    {
        await _jobs.CreateAsync(
            Job("Running", definition: CreateJobDefinition(targetEventId: 42), observed: Now - TimeSpan.FromMinutes(10)),
            CancellationToken.None);
        _runtime.GetOrchestrationStateAsync("job", false).Returns([]);

        await CreateTrigger(autoStart: false).ReconcileAsync(CancellationToken.None);

        (await _jobs.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Failed");
        await AssertNoJobRequestedAsync();
    }

    [Fact]
    public async Task GivenActivationLockIsHeld_WhenTickRuns_ThenPendingParametersAreReadOnlyAfterItIsReleased()
    {
        var held = await _state.AcquireActivationLockAsync(CancellationToken.None);
        var tick = CreateTrigger().ReconcileAsync(CancellationToken.None);
        await Task.Yield();

        tick.IsCompleted.ShouldBeFalse();
        await AssertNoJobRequestedAsync();
        held.Dispose();
        await tick;

        await AssertJobRequestedAsync(ReindexTriggerKind.Reconciliation);
    }

    [Theory]
    [InlineData(typeof(IOException), false)]
    [InlineData(typeof(InvalidOperationException), true)]
    public async Task GivenAFailedJobRequest_WhenTickRuns_ThenOnlyAProgrammerErrorPropagates(Type failure, bool propagates)
    {
        FailJobRequests(failure);

        var exception = await Record.ExceptionAsync(() => CreateTrigger().ReconcileAsync(CancellationToken.None));

        if (propagates)
        {
            exception.ShouldBeOfType(failure);
        }
        else
        {
            exception.ShouldBeNull();
        }
    }

    private ReindexTrigger CreateTrigger(bool autoStart = true, bool enabled = true, TenantConfiguration[]? tenants = null)
    {
        var options = Options.Create(new ReindexOptions
        {
            Enabled = enabled,
            AutoStart = autoStart,
            OrphanGrace = TimeSpan.FromMinutes(2)
        });
        return new ReindexTrigger(
            _mediator,
            _jobs,
            _state,
            new ReindexJobReconciler(
                new TaskHubClient(_runtime),
                _jobs,
                new ReindexLifecycleEventWriter(new TestSourceEventStore(), _state),
                _jobLock,
                options,
                new FixedTimeProvider(Now),
                NullLogger<ReindexJobReconciler>.Instance),
            CreateRepositoryFactory(tenants ?? []),
            options,
            NullLogger<ReindexTrigger>.Instance);
    }

    private void FailJobRequests(Type failure) =>
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ => throw (Exception)Activator.CreateInstance(failure, "Failure.")!);

    private Task AssertNoJobRequestedAsync() =>
        _mediator.DidNotReceive().SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>());

    private Task AssertJobRequestedAsync(ReindexTriggerKind trigger) =>
        _mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == trigger),
            CancellationToken.None);

    private sealed record UnsupportedReindexResult : CreateReindexJobResult;
}
