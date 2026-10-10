using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class CancelReindexHandlerTests
{
    private const string Canonical = "http://example.org/SearchParameter/patient-custom";

    [Fact]
    public async Task GivenTerminalJob_WhenCancellationIsRequested_ThenOrchestrationIsNotTerminated()
    {
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, (int)BackgroundJobType.Reindex, Arg.Any<CancellationToken>())
            .Returns(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job",
                OrchestrationInstanceId = "job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = "Completed",
                Definition = ReindexTestHelper.CreateJobDefinition()
            });
        var handler = new CancelReindexHandler(
            new TaskHubClient(runtime),
            repository,
            new ReindexLifecycleEventWriter(Substitute.For<ISourceEventStore>(), new ConformanceState()),
            Substitute.For<IReindexJobLock>(),
            TimeProvider.System);

        var result = await handler.HandleAsync(
            new CancelReindexCommand("job", "operator request"),
            CancellationToken.None);

        result.ShouldBeOfType<ReindexJobAlreadyTerminalResult>().Status.ShouldBe("Completed");
        await runtime.DidNotReceive().ForceTerminateTaskOrchestrationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task GivenRunningJob_WhenCancelled_ThenParametersReturnToPendingAndJobIsCancelled()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation());
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(Canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            OrchestrationInstanceId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = Definition(target),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        using var jobLock = new TestJobLock();
        var handler = new CancelReindexHandler(
            new TaskHubClient(runtime),
            repository,
            lifecycle,
            jobLock,
            TimeProvider.System);

        var result = await handler.HandleAsync(
            new CancelReindexCommand("job", "operator request"),
            CancellationToken.None);

        result.ShouldBeOfType<ReindexCancelledResult>().JobId.ShouldBe("job");
        var job = (await repository.GetAsync("job", 1, CancellationToken.None))!;
        job.Status.ShouldBe("Cancelled");
        job.CancelRequested.ShouldBeTrue();
        job.Progress!["cancellationReason"]!.GetValue<string>().ShouldBe("operator request");
        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
        await runtime.Received(1).ForceTerminateTaskOrchestrationAsync("job", "operator request");
    }

    private static ReindexJobDefinition Definition(ReindexParameterDefinition target) => new()
    {
        TargetEventId = 1,
        TenantIds = [1],
        ResourceTypes = ["Patient"],
        SearchParameters = [target],
        MaximumNumberOfResourcesPerQuery = 10_000,
        MaximumNumberOfResourcesPerWrite = 1_000,
        MaximumConcurrency = 4,
        QueryDelayIntervalInMilliseconds = 0,
        Trigger = "Manual"
    };

    private static ISourceEventStore EventStore()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SourceEvent>());
        long nextEventId = 2;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select(evt => new SourceEvent(
                    nextEventId++,
                    evt.StreamId,
                    evt.EventType,
                    evt.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        return store;
    }

    private static SourceEvent Activation() => new(
        1,
        "search",
        nameof(SearchParameterActivated),
        new SearchParameterActivated(
            Canonical,
            "custom",
            "Patient",
            "Patient.id",
            SearchParamType.String,
            "example@1.0.0",
            null,
            17,
            null,
            null,
            null,
            null),
        DateTimeOffset.UtcNow);

    private sealed class TestJobLock : IReindexJobLock, IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await action(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose() => _semaphore.Dispose();
    }
}
