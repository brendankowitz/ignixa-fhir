using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
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

public class StartReindexActivityTests
{
    [Fact]
    public async Task GivenQueuedDefinitionWasExpandedDuringDebounce_WhenStartRuns_ThenLatestTargetsAreUsed()
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var second = new ReindexParameterDefinition(
            "http://example.org/SearchParameter/patient-second",
            "second",
            "Patient",
            18,
            2,
            ["Patient"]);
        fixture.State.ApplyAndTrack(new SourceEvent(
            2,
            "search",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                second.Canonical,
                second.Code,
                second.ResourceType,
                "Patient.name",
                SearchParamType.String,
                "example@2.0.0",
                null,
                second.SearchParamId,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow));
        var job = (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!;
        job.Definition = new ReindexJobDefinition
        {
            TargetEventId = 2,
            TenantIds = [1],
            ResourceTypes = ["Patient"],
            SearchParameters = [Definition(fixture.Target), Definition(second)],
            MaximumNumberOfResourcesPerQuery = 10_000,
            MaximumNumberOfResourcesPerWrite = 1_000,
            MaximumConcurrency = 4,
            QueryDelayIntervalInMilliseconds = 0,
            Trigger = "Activation",
            ConsumedGeneration = 2
        };
        await fixture.Repository.UpdateAsync(job, 1, CancellationToken.None);

        var output = JsonSerializer.Deserialize<StartReindexOutput>(await fixture.StartAsync())!;

        output.TargetEventId.ShouldBe(2);
        output.Targets!.Select(target => target.Code)
            .ShouldBe(["custom", "second"], ignoreOrder: true);
        fixture.Events.OfType<SourceEvent>()
            .Count(evt => evt.Data is SearchParameterReindexStarted).ShouldBe(2);
    }

    [Theory]
    [InlineData("Completed", SearchParameterStatus.Enabled)]
    [InlineData("Failed", SearchParameterStatus.Pending)]
    [InlineData("Cancelled", SearchParameterStatus.Pending)]
    public async Task GivenCompletionWins_WhenDelayedStartResumes_ThenLifecycleAndTerminalJobAreUnchanged(
        string decision,
        SearchParameterStatus expectedStatus)
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        var effectsApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completing = fixture.Updater.TryCompleteAsync(
            "job",
            decision,
            async (_, cancellationToken) =>
            {
                await fixture.Lifecycle.CompleteAsync(
                    "job",
                    [new ReindexTargetCompletion(
                        fixture.Target, decision == "Completed", 0, TimeSpan.Zero, "terminated")],
                    cancellationToken);
                effectsApplied.SetResult();
                await releaseCompletion.Task;
            },
            job => job.Status = decision,
            CancellationToken.None);
        await effectsApplied.Task;

        Task<string> starting;
        try
        {
            starting = fixture.StartAsync();
        }
        finally
        {
            releaseCompletion.SetResult();
        }

        (await completing).ShouldBeTrue();
        var output = JsonSerializer.Deserialize<StartReindexOutput>(await starting)!;

        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(expectedStatus);
        output.ShouldContinue.ShouldBeFalse();
        (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe(decision);
        fixture.Events.Count(evt => evt.Data is SearchParameterReindexStarted).ShouldBe(1);
    }

    [Fact]
    public async Task GivenPersistedCompletingDecision_WhenStartIsRetried_ThenLifecycleAndDecisionAreUnchanged()
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Updater.TryCompleteAsync(
            "job",
            "Cancelled",
            async (_, cancellationToken) =>
            {
                await fixture.Lifecycle.CompleteAsync(
                    "job",
                    [new ReindexTargetCompletion(fixture.Target, false, 0, TimeSpan.Zero, "cancelled")],
                    cancellationToken);
                throw new InvalidOperationException("completion interrupted");
            },
            job =>
            {
                job.Status = "Cancelled";
                job.Progress = new JsonObject { ["cancellationReason"] = "cancelled" };
            },
            CancellationToken.None));
        var before = JsonSerializer.Serialize(
            await fixture.Repository.GetAsync("job", 1, CancellationToken.None));

        await fixture.StartAsync();

        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
        JsonSerializer.Serialize(await fixture.Repository.GetAsync("job", 1, CancellationToken.None))
            .ShouldBe(before);
        fixture.Events.Count(evt => evt.Data is SearchParameterReindexStarted).ShouldBe(1);
    }

    [Fact]
    public async Task GivenStartIsAppending_WhenCompletionArrives_ThenDecisionWaitsForInitialProgress()
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        var appending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.BeforeAppend = async () =>
        {
            appending.SetResult();
            await releaseStart.Task;
        };
        var starting = fixture.StartAsync();
        await appending.Task;
        fixture.BeforeAppend = null;
        string statusWhileStarting;
        var completing = fixture.Updater.TryCompleteAsync(
            "job",
            "Cancelled",
            (_, cancellationToken) => fixture.Lifecycle.CompleteAsync(
                "job",
                [new ReindexTargetCompletion(fixture.Target, false, 0, TimeSpan.Zero, "cancelled")],
                cancellationToken),
            job => job.Status = "Cancelled",
            CancellationToken.None);
        try
        {
            statusWhileStarting = (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!.Status;
        }
        finally
        {
            releaseStart.SetResult();
        }
        var startError = await Record.ExceptionAsync(() => starting);
        (await completing).ShouldBeTrue();

        statusWhileStarting.ShouldBe("Queued");
        startError.ShouldBeNull();
        var job = await fixture.Repository.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Cancelled");
        job.StartDate.ShouldNotBeNull();
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    private static ReindexParameterDefinition Definition(ReindexParameterDefinition target) =>
        new(
            target.Canonical,
            target.Code,
            target.ResourceType,
            target.SearchParamId,
            target.ActivationEventId,
            target.AffectedResourceTypes);

    private sealed class Fixture : IDisposable
    {
        private readonly TestJobLock _jobLock = new();

        public Fixture()
        {
            var tenants = Substitute.For<ITenantConfigurationStore>();
            tenants.Mode.Returns(TenantMode.Isolated);
            Repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
                tenants,
                NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
            State.ApplyAndTrack(new SourceEvent(
                1,
                "search",
                nameof(SearchParameterActivated),
                new SearchParameterActivated(
                    Target.Canonical, "custom", "Patient", "Patient.id", SearchParamType.String,
                    "example@1.0.0", null, 17, null, null, null, null),
                DateTimeOffset.UtcNow));
            var eventStore = Substitute.For<ISourceEventStore>();
            eventStore.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(AsyncEnumerable.Empty<SourceEvent>());
            eventStore.AppendAsync(
                    Arg.Any<IEnumerable<NewSourceEvent>>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    if (BeforeAppend is { } beforeAppend)
                    {
                        await beforeAppend();
                    }

                    var committed = call.Arg<IEnumerable<NewSourceEvent>>()
                        .Select((evt, index) => new SourceEvent(
                            Events.Count + index + 2, evt.StreamId, evt.EventType, evt.Data, DateTimeOffset.UtcNow))
                        .ToArray();
                    Events.AddRange(committed);
                    return (IReadOnlyList<SourceEvent>)committed;
                });
            Lifecycle = new ReindexLifecycleEventWriter(eventStore, State);
            Updater = new ReindexJobUpdater(Repository, _jobLock, new NullReindexCompletionHook());
        }

        public InMemoryBackgroundJobRepository<ReindexJobDefinition> Repository { get; }
        public ConformanceState State { get; } = new();
        public List<SourceEvent> Events { get; } = [];
        public Func<Task>? BeforeAppend { get; set; }
        public ReindexParameterDefinition Target { get; } =
            new("http://example.org/SearchParameter/patient-custom", "custom", "Patient", 17, 1, ["Patient"]);
        public ReindexLifecycleEventWriter Lifecycle { get; }
        public ReindexJobUpdater Updater { get; }

        public Task InitializeAsync() =>
            Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = "Queued",
                Definition = ReindexTestHelper.CreateJobDefinition(),
                CreateDate = DateTimeOffset.UtcNow,
                HeartbeatDate = DateTimeOffset.UtcNow
            }, CancellationToken.None);

        public Task<string> StartAsync() =>
            new StartReindexActivity(Lifecycle, Updater).RunAsync(
                new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
                JsonSerializer.Serialize(new[] { new StartReindexInput("job", 1, [Target], [1]) }));

        public void Dispose() => _jobLock.Dispose();
    }

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
