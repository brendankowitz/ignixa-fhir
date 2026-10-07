using DurableTask.Core;
using System.Text.Json;
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

public class CompleteReindexActivityTests
{
    [Fact]
    public async Task GivenOverrideUsesExistingPhysicalId_WhenReindexCompletes_ThenOverrideIsEnabled()
    {
        const string overrideCanonical = "http://example.org/SearchParameter/patient-override";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var jobs = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        await jobs.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var eventStore = EventStore();
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(overrideCanonical));
        var lifecycle = new ReindexLifecycleEventWriter(eventStore, state);
        var target = new ReindexTarget(
            overrideCanonical,
            "custom",
            "Patient",
            17,
            1,
            ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(
                17,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var updater = new ReindexJobUpdater(
            jobs,
            jobLock,
            Substitute.For<IReindexCompletionHook>());
        var activity = new CompleteReindexActivity(
            repositoryFactory,
            lifecycle,
            updater,
            new ReindexProgressReporter(updater),
            TimeProvider.System);

        await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[]
            {
                new CompleteReindexInput(
                "job",
                1,
                [target],
                [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 153, [], null)],
                [])
            }));

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Completed");
        job.Progress!["tenants"]![0]!["failedResources"]!.GetValue<long>().ShouldBe(153);
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Enabled);
    }

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
            new OverrideInfo("http://hl7.org/fhir/SearchParameter/Resource-id", 17),
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
