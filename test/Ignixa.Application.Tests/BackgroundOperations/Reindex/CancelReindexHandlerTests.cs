using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class CancelReindexHandlerTests : IDisposable
{
    private readonly IOrchestrationServiceClient _runtime = Substitute.For<IOrchestrationServiceClient>();
    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();
    private readonly ConformanceState _state = new();
    private readonly ReindexLifecycleEventWriter _lifecycle;

    public CancelReindexHandlerTests()
    {
        _state.ApplyAndTrack(Activation());
        _lifecycle = new ReindexLifecycleEventWriter(new TestSourceEventStore(), _state);
    }

    public void Dispose() => _state.Dispose();

    [Fact]
    public async Task GivenTerminalJob_WhenCancellationIsRequested_ThenOrchestrationIsNotTerminated()
    {
        await _jobs.CreateAsync(Job("Completed"), CancellationToken.None);

        var result = await CancelAsync();

        result.ShouldBeOfType<ReindexJobAlreadyTerminalResult>().Status.ShouldBe(ReindexJobStatus.Completed);
        await _runtime.DidNotReceiveWithAnyArgs().ForceTerminateTaskOrchestrationAsync(default!, default!);
    }

    [Fact]
    public async Task GivenRunningJob_WhenCancelled_ThenParametersReturnToPendingAndJobIsCancelled()
    {
        await _lifecycle.StartAsync("job", [Target()], CancellationToken.None);
        await _jobs.CreateAsync(Job("Running", definition: CreateJobDefinition([Target()])), CancellationToken.None);

        var result = await CancelAsync();

        result.ShouldBeOfType<ReindexCancelledResult>().JobId.ShouldBe("job");
        var job = (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
        job.Status.ShouldBe("Cancelled");
        job.CancelRequested.ShouldBeTrue();
        ReindexProgress.FromJson(job.Progress)!.CancellationReason.ShouldBe("operator request");
        var parameter = _state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
        await _runtime.Received(1).ForceTerminateTaskOrchestrationAsync("job", "operator request");
    }

    private Task<CancelReindexResult> CancelAsync() =>
        new CancelReindexHandler(new TaskHubClient(_runtime), _jobs, _lifecycle, new TestReindexJobLock(), TimeProvider.System)
            .HandleAsync(new CancelReindexCommand("job", "operator request"), CancellationToken.None);
}
