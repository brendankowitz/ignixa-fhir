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
    public async Task GivenFinalWriteFailsAfterLifecycleAppend_WhenActivityRetries_ThenCompletionEventsAreNotAppendedTwice()
    {
        const string canonical = "http://example.org/SearchParameter/patient-retry-write";
        var (jobs, tenants) = await CreateRunningJobAsync([Tenant(1)]);
        var proxy = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        proxy.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => jobs.GetAsync("job", 1, CancellationToken.None));
        var failWrite = true;
        proxy.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(call => failWrite
                ? Task.FromException(new TimeoutException("job store unavailable"))
                : jobs.UpdateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), 1, CancellationToken.None));
        var events = new List<SourceEvent>();
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(events), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(StoreFactoryWithCatalogId(17), lifecycle, proxy, jobLock, tenants);
        var input = SingleTenantInput(target);
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(context, input));
        (await jobs.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Running");
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Enabled);

        failWrite = false;
        await activity.RunAsync(context, input);

        (await jobs.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Completed");
        events.Count(evt => evt.Data is SearchParameterReindexCompleted).ShouldBe(1);
    }

    [Fact]
    public async Task GivenFinalWriteFailsAfterFailureWasAppended_WhenActivityRetries_ThenJobStillFails()
    {
        const string canonical = "http://example.org/SearchParameter/patient-retry-failure";
        var (jobs, tenants) = await CreateRunningJobAsync([Tenant(1)]);
        var proxy = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        proxy.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => jobs.GetAsync("job", 1, CancellationToken.None));
        var failWrite = true;
        proxy.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(call => failWrite
                ? Task.FromException(new TimeoutException("job store unavailable"))
                : jobs.UpdateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), 1, CancellationToken.None));
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(StoreFactoryWithCatalogId(17), lifecycle, proxy, jobLock, tenants);
        var input = JsonSerializer.Serialize(new[]
        {
            new CompleteReindexInput(
                "job",
                1,
                [target],
                [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 0, [], null)],
                [])
            {
                FailureMessage = "Reindex orchestration failed: worker crashed"
            }
        });
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(context, input));
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);

        failWrite = false;
        await activity.RunAsync(context, input);

        var job = (await jobs.GetAsync("job", 1, CancellationToken.None))!;
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("worker crashed");
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    [Fact]
    public async Task GivenLifecycleAppendFails_WhenActivityRetries_ThenTargetIsNotEnabled()
    {
        const string canonical = "http://example.org/SearchParameter/patient-retry-lifecycle";
        var (jobs, tenants) = await CreateRunningJobAsync([Tenant(1), Tenant(2)]);
        var failAppend = false;
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(() => failAppend), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            StoreFactoryWithCatalogId(17),
            lifecycle,
            jobs,
            jobLock,
            tenants);
        var input = SingleTenantInput(target);
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });

        failAppend = true;
        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(context, input));
        (await jobs.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Running");

        failAppend = false;
        await activity.RunAsync(context, input);

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("manual $reindex");
    }

    [Fact]
    public async Task GivenActiveTenantWasNotInJob_WhenCompletionRuns_ThenJobFailsWithoutEnablingTarget()
    {
        const string canonical = "http://example.org/SearchParameter/patient-new-tenant";
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.Mode.Returns(TenantMode.Isolated);
        tenantStore.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Tenant(1),
                Tenant(2)
            ]);
        var jobs = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenantStore,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var definition = ReindexTestHelper.CreateJobDefinition();
        await jobs.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = definition,
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IReindexStore>();
        repository.HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var storeFactory = Substitute.For<IReindexStoreFactory>();
        storeFactory.GetReindexStoreAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            storeFactory,
            lifecycle,
            jobs,
            jobLock,
            tenantStore);

        await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[]
            {
                new CompleteReindexInput(
                    "job",
                    1,
                    [target],
                    [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 0, [], null)],
                    [])
            }));

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("manual $reindex");
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("resourceType")]
    [InlineData("code")]
    [InlineData("activationEventId")]
    public async Task GivenPlannedTargetDiffersFromOwnedTargetInOneIdentityField_WhenCompletionRuns_ThenOwnedTargetReturnsToPending(
        string differingField)
    {
        const string canonical = "http://example.org/SearchParameter/patient-identity-mismatch";
        var (jobs, tenants) = await CreateRunningJobAsync([Tenant(1)]);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var owned = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [owned], CancellationToken.None);
        var planned = differingField switch
        {
            "canonical" => owned with { Canonical = canonical + "-other" },
            "resourceType" => owned with { ResourceType = "Observation" },
            "code" => owned with { Code = "other" },
            _ => owned with { ActivationEventId = owned.ActivationEventId + 1 }
        };
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            StoreFactoryWithCatalogId(17),
            lifecycle,
            jobs,
            jobLock,
            tenants);

        await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            SingleTenantInput(planned));

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("not planned by this job");
        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenActiveTenantWasAddedAndJobOwnsUnplannedTarget_WhenCompletionRuns_ThenJobAndTargetRetainUnplannedFailure()
    {
        const string canonical = "http://example.org/SearchParameter/patient-unplanned-new-tenant";
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.Mode.Returns(TenantMode.Isolated);
        tenantStore.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Tenant(1),
                Tenant(2)
            ]);
        var jobs = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenantStore,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        await jobs.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexTestHelper.CreateJobDefinition(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IReindexStore>();
        repository.HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var storeFactory = Substitute.For<IReindexStoreFactory>();
        storeFactory.GetReindexStoreAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            storeFactory,
            lifecycle,
            jobs,
            jobLock,
            tenantStore);

        await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[]
            {
                new CompleteReindexInput(
                    "job",
                    1,
                    [],
                    [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 0, [], null)],
                    [])
            }));

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("not planned by this job");
        job.ErrorMessage.ShouldContain("manual $reindex");
        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenJobOwnsTargetMissingFromInput_WhenCompletionRuns_ThenJobFailsAndOwnedTargetReturnsToPending()
    {
        const string canonical = "http://example.org/SearchParameter/patient-added-during-debounce";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns([Tenant(1)]);
        var jobs = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        await jobs.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexTestHelper.CreateJobDefinition(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IReindexStore>();
        repository.HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var storeFactory = Substitute.For<IReindexStoreFactory>();
        storeFactory.GetReindexStoreAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            storeFactory,
            lifecycle,
            jobs,
            jobLock,
            tenants);

        await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[]
            {
                new CompleteReindexInput(
                    "job",
                    1,
                    [],
                    [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 0, [], null)],
                    [])
            }));

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("not planned by this job");
        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenOverrideUsesExistingPhysicalId_WhenReindexCompletes_ThenOverrideIsEnabled()
    {
        const string overrideCanonical = "http://example.org/SearchParameter/patient-override";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns([Tenant(1)]);
        var jobs = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        await jobs.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexTestHelper.CreateJobDefinition(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var eventStore = EventStore();
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(overrideCanonical));
        var lifecycle = new ReindexLifecycleEventWriter(eventStore, state);
        var target = new ReindexParameterDefinition(
            overrideCanonical,
            "custom",
            "Patient",
            17,
            1,
            ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IReindexStore>();
        repository.HasSearchParameterAsync(
                17,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var storeFactory = Substitute.For<IReindexStoreFactory>();
        storeFactory.GetReindexStoreAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            storeFactory,
            lifecycle,
            jobs,
            jobLock,
            tenants);

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
        job.Progress!["tenants"]![0]!["tenantId"]!.GetValue<int>().ShouldBe(1);
        job.Progress!["tenants"]![0]!["failedResources"]!.GetValue<long>().ShouldBe(153);
        job.Result!["success"]!.GetValue<bool>().ShouldBeTrue();
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Enabled);
    }

    private static async Task<(InMemoryBackgroundJobRepository<ReindexJobDefinition> Jobs, ITenantConfigurationStore Tenants)>
        CreateRunningJobAsync(IReadOnlyList<TenantConfiguration> activeTenants)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(activeTenants);
        var jobs = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        await jobs.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "job",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexTestHelper.CreateJobDefinition(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        return (jobs, tenants);
    }

    private static IReindexStoreFactory StoreFactoryWithCatalogId(int searchParamId)
    {
        var repository = Substitute.For<IReindexStore>();
        repository.HasSearchParameterAsync(searchParamId, Arg.Any<CancellationToken>())
            .Returns(true);
        var storeFactory = Substitute.For<IReindexStoreFactory>();
        storeFactory.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        return storeFactory;
    }

    private static string SingleTenantInput(ReindexParameterDefinition target) =>
        JsonSerializer.Serialize(new[]
        {
            new CompleteReindexInput(
                "job",
                1,
                [target],
                [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 0, [], null)],
                [])
        });

    private static ISourceEventStore EventStore(Func<bool> failAppend)
    {
        var store = EventStore();
        long nextEventId = 100;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => failAppend()
                ? throw new InvalidOperationException("event store unavailable")
                : call.Arg<IEnumerable<NewSourceEvent>>()
                    .Select(evt => new SourceEvent(
                        nextEventId++,
                        evt.StreamId,
                        evt.EventType,
                        evt.Data,
                        DateTimeOffset.UtcNow))
                    .ToArray());
        return store;
    }

    private static ISourceEventStore EventStore() => EventStore([]);

    private static ISourceEventStore EventStore(List<SourceEvent> committed)
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SourceEvent>());
        long nextEventId = 2;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var events = call.Arg<IEnumerable<NewSourceEvent>>()
                    .Select(evt => new SourceEvent(
                        nextEventId++,
                        evt.StreamId,
                        evt.EventType,
                        evt.Data,
                        DateTimeOffset.UtcNow))
                    .ToArray();
                committed.AddRange(events);
                return events;
            });
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

    private static CompleteReindexActivity CreateActivity(
        IReindexStoreFactory storeFactory,
        ReindexLifecycleEventWriter lifecycle,
        IBackgroundJobRepository<ReindexJobDefinition> jobs,
        IReindexJobLock jobLock,
        ITenantConfigurationStore tenantStore) =>
        new(
            storeFactory,
            lifecycle,
            jobs,
            jobLock,
            tenantStore,
            TimeProvider.System,
            NullLogger<CompleteReindexActivity>.Instance);

    private static TenantConfiguration Tenant(int tenantId) => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = "4.0",
        IsActive = true
    };

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
