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
        var target = new ReindexTarget(canonical, "custom", "Patient", 17, 1, ["Patient"]);
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
            state,
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

    private static ReindexJobDefinition Definition(ReindexTarget target) => new()
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
