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

/// <summary>Spec §6.4: cancel is 202 (parameters return to Pending), 404 for an unknown job, or 409 for a terminal one.</summary>
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
    public async Task GivenNoSuchJobOrATerminalJob_WhenCancellationIsRequested_ThenItIsRejectedWithoutTerminatingAnOrchestration()
    {
        await _jobs.CreateAsync(Job("Completed", "done"), CancellationToken.None);

        var missing = await CancelAsync();
        var terminal = await CancelAsync("done");

        missing.ShouldBeOfType<ReindexJobNotFoundResult>();
        terminal.ShouldBeOfType<ReindexJobAlreadyTerminalResult>().Status.ShouldBe(ReindexJobStatus.Completed);
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

    private Task<CancelReindexResult> CancelAsync(string jobId = "job") =>
        new CancelReindexHandler(new TaskHubClient(_runtime), _jobs, _lifecycle, new TestReindexJobLock(), TimeProvider.System)
            .HandleAsync(new CancelReindexCommand(jobId, "operator request"), CancellationToken.None);
}
