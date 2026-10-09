using System.Text.Json.Nodes;
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
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexJobReconcilerTests
{
    [Fact]
    public async Task GivenTerminalJobStillOwnsReindexingParameter_WhenReconciled_ThenParameterReturnsToPending()
    {
        const string canonical = "http://example.org/SearchParameter/patient-orphan";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            OrchestrationInstanceId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Completed",
            Definition = Definition(target),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow,
            EndDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        using var jobLock = new TestJobLock();
        var reconciler = new ReindexJobReconciler(
            new TaskHubClient(Substitute.For<IOrchestrationServiceClient>()),
            repository,
            lifecycle,
            new ReindexJobUpdater(repository, jobLock, Substitute.For<IReindexCompletionHook>()),
            jobLock,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<ReindexJobReconciler>.Instance);

        await reconciler.ReconcileAsync(CancellationToken.None);

        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
        state.GetSearchParameter("Patient", "custom")!.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenCrashAfterDebouncedJobWasPersisted_WhenStartupReconciles_ThenQueuedJobIsReleasedForRecovery()
    {
        const string canonical = "http://example.org/SearchParameter/patient-custom";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "debounced",
            OrchestrationInstanceId = "debounced",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Queued",
            Definition = Definition(target),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        runtime.GetOrchestrationStateAsync("debounced", false).Returns([], []);
        using var jobLock = new TestJobLock();
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var updater = new ReindexJobUpdater(
            repository,
            jobLock,
            Substitute.For<IReindexCompletionHook>());
        var reconciler = new ReindexJobReconciler(
            new TaskHubClient(runtime),
            repository,
            lifecycle,
            updater,
            jobLock,
            Options.Create(new ReindexOptions { OrphanGrace = TimeSpan.FromMinutes(2) }),
            TimeProvider.System,
            NullLogger<ReindexJobReconciler>.Instance);

        await reconciler.ReconcileStartupAsync(CancellationToken.None);

        (await repository.GetAsync("debounced", 1, CancellationToken.None))!
            .Status.ShouldBe("Failed");
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    [Theory]
    [InlineData("Completed", true, SearchParameterStatus.Enabled)]
    [InlineData("Failed", false, SearchParameterStatus.Pending)]
    public async Task GivenPersistedStartIsNotAppliedLocally_WhenStartupReconciles_ThenLifecycleDecisionIsApplied(
        string decision,
        bool success,
        SearchParameterStatus expectedStatus)
    {
        const string canonical = "http://example.org/SearchParameter/patient-custom";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var events = new List<SourceEvent>
        {
            new(
                2,
                $"reindex:{canonical}",
                nameof(SearchParameterReindexStarted),
                new SearchParameterReindexStarted(
                    canonical,
                    "custom",
                    "Patient",
                    "job",
                    ["Patient"],
                    1),
                DateTimeOffset.UtcNow)
        };
        var eventStore = EventStore(events);
        var lifecycle = new ReindexLifecycleEventWriter(eventStore, state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            OrchestrationInstanceId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Completing",
            Definition = Definition(target),
            Progress = new JsonObject
            {
                ["terminalDecision"] = decision,
                ["terminalOutcomes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["canonical"] = canonical,
                        ["success"] = success,
                        ["resourcesIndexed"] = success ? 12 : 0,
                        ["errorMessage"] = success ? null : "failed"
                    }
                }
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        runtime.GetOrchestrationStateAsync("job", false).Returns([]);
        using var jobLock = new TestJobLock();
        var updater = new ReindexJobUpdater(
            repository,
            jobLock,
            Substitute.For<IReindexCompletionHook>());
        var reconciler = new ReindexJobReconciler(
            new TaskHubClient(runtime),
            repository,
            lifecycle,
            updater,
            jobLock,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<ReindexJobReconciler>.Instance);

        await reconciler.ReconcileAsync(CancellationToken.None);

        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task GivenPersistedCompletingDecision_WhenStartupReconciles_ThenLifecycleAndJobAreCompleted()
    {
        const string canonical = "http://example.org/SearchParameter/patient-custom";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            OrchestrationInstanceId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Completing",
            Definition = Definition(target),
            Progress = new JsonObject
            {
                ["terminalDecision"] = "Completed",
                ["terminalOutcomes"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["canonical"] = canonical,
                        ["success"] = true,
                        ["resourcesIndexed"] = 12
                    }
                }
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        runtime.GetOrchestrationStateAsync("job", false).Returns([]);
        using var jobLock = new TestJobLock();
        var completionHook = Substitute.For<IReindexCompletionHook>();
        var updater = new ReindexJobUpdater(repository, jobLock, completionHook);
        var reconciler = new ReindexJobReconciler(
            new TaskHubClient(runtime),
            repository,
            lifecycle,
            updater,
            jobLock,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<ReindexJobReconciler>.Instance);

        await reconciler.ReconcileAsync(CancellationToken.None);

        (await repository.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Completed");
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Enabled);
        await completionHook.Received(1).OnCompletedAsync(
            Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
            Arg.Any<CancellationToken>());
    }

    private static ReindexJobDefinition Definition(ReindexParameterDefinition target) => new()
    {
        TargetEventId = 1,
        TenantIds = [1],
        ResourceTypes = ["Patient"],
        SearchParameters =
        [
            new ReindexParameterDefinition(
                target.Canonical,
                target.Code,
                target.ResourceType,
                target.SearchParamId,
                target.ActivationEventId,
                target.AffectedResourceTypes)
        ],
        MaximumNumberOfResourcesPerQuery = 10_000,
        MaximumNumberOfResourcesPerWrite = 1_000,
        MaximumConcurrency = 4,
        QueryDelayIntervalInMilliseconds = 0,
        Trigger = "Manual"
    };

    private static ISourceEventStore EventStore()
    {
        return EventStore([]);
    }

    private static ISourceEventStore EventStore(List<SourceEvent> events)
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => ReadFrom(events, call.Arg<long>()));
        long nextEventId = events.Count == 0 ? 2 : events.Max(evt => evt.EventId) + 1;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var committed = call.Arg<IEnumerable<NewSourceEvent>>()
                    .Select(evt => new SourceEvent(
                        nextEventId++,
                        evt.StreamId,
                        evt.EventType,
                        evt.Data,
                        DateTimeOffset.UtcNow))
                    .ToArray();
                events.AddRange(committed);
                return committed;
            });
        return store;
    }

    private static async IAsyncEnumerable<SourceEvent> ReadFrom(
        IReadOnlyList<SourceEvent> events,
        long afterEventId)
    {
        foreach (var evt in events.Where(evt => evt.EventId > afterEventId).ToArray())
        {
            yield return evt;
        }

        await Task.CompletedTask;
    }

    private static SourceEvent Activation(string canonical) => new(
        1,
        "search",
        nameof(SearchParameterActivated),
        new SearchParameterActivated(
            canonical,
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
