using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class StartReindexActivityTests : IAsyncLifetime, IDisposable
{
    private readonly ConformanceState _state = new();
    private readonly TestSourceEventStore _events = new();
    private readonly TestReindexJobLock _jobLock = new();
    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();
    private readonly ReindexLifecycleEventWriter _lifecycle;

    public StartReindexActivityTests()
    {
        _state.ApplyAndTrack(Activation());
        _lifecycle = new ReindexLifecycleEventWriter(_events, _state);
    }

    public Task InitializeAsync() => _jobs.CreateAsync(Job("Queued"), CancellationToken.None);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _state.Dispose();

    // §8.2: start appends SearchParameterReindexStarted per target and moves the job to Running.
    [Fact]
    public async Task GivenQueuedJob_WhenStartRuns_ThenTargetsAreStartedAndTheJobRuns()
    {
        var output = JsonSerializer.Deserialize<StartReindexOutput>(await StartAsync())!;

        output.ShouldContinue.ShouldBeTrue();
        _events.Appended.Count(evt => evt.Data is SearchParameterReindexStarted).ShouldBe(1);
        var started = (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
        started.Status.ShouldBe("Running");
        started.StartDate.ShouldNotBeNull();
    }

    // §8.5: a delayed start cannot reopen a finalized job or its parameters.
    [Theory]
    [InlineData("Completed", SearchParameterStatus.Enabled)]
    [InlineData("Failed", SearchParameterStatus.Pending)]
    [InlineData("Cancelled", SearchParameterStatus.Pending)]
    public async Task GivenJobFinishedBeforeStart_WhenDelayedStartResumes_ThenLifecycleAndTerminalJobAreUnchanged(
        string decision,
        SearchParameterStatus expectedStatus)
    {
        await _lifecycle.StartAsync("job", [Target()], CancellationToken.None);
        await _lifecycle.CompleteAsync(
            "job",
            [new ReindexTargetCompletion(Target(), decision == "Completed", 0, "terminated")],
            TimeSpan.Zero,
            CancellationToken.None);
        var finished = (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
        finished.Status = decision;
        await _jobs.UpdateAsync(finished, 1, CancellationToken.None);
        var before = JsonSerializer.Serialize(await _jobs.GetAsync("job", 1, CancellationToken.None));

        var output = JsonSerializer.Deserialize<StartReindexOutput>(await StartAsync())!;

        output.ShouldContinue.ShouldBeFalse();
        _state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(expectedStatus);
        JsonSerializer.Serialize(await _jobs.GetAsync("job", 1, CancellationToken.None)).ShouldBe(before);
        _events.Appended.Count(evt => evt.Data is SearchParameterReindexStarted).ShouldBe(1);
    }

    // §8.5: lifecycle start and cancellation serialize on the singleton job lock.
    [Fact]
    public async Task GivenStartIsAppending_WhenCancellationArrives_ThenCancellationWaitsForTheStartToCommit()
    {
        var appending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _events.BeforeAppend = async () =>
        {
            appending.SetResult();
            await releaseStart.Task;
        };
        var starting = StartAsync();
        await appending.Task;
        _events.BeforeAppend = null;
        var cancelling = new CancelReindexHandler(
                new TaskHubClient(Substitute.For<IOrchestrationServiceClient>()),
                _jobs,
                _lifecycle,
                _jobLock,
                TimeProvider.System)
            .HandleAsync(new CancelReindexCommand("job", "cancelled"), CancellationToken.None);
        string statusWhileStarting;
        try
        {
            statusWhileStarting = (await _jobs.GetAsync("job", 1, CancellationToken.None))!.Status;
        }
        finally
        {
            releaseStart.SetResult();
        }

        var startError = await Record.ExceptionAsync(() => starting);
        var cancelled = await cancelling;

        statusWhileStarting.ShouldBe("Queued");
        startError.ShouldBeNull();
        cancelled.ShouldBeOfType<ReindexCancelledResult>();
        var job = await _jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Cancelled");
        job.StartDate.ShouldNotBeNull();
        _state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    private Task<string> StartAsync() =>
        new StartReindexActivity(_lifecycle, _jobs, _jobLock, TimeProvider.System).RunAsync(
            ActivityContext(),
            JsonSerializer.Serialize(new[] { new StartReindexInput("job", 1, [Target()], [1]) }));
}
