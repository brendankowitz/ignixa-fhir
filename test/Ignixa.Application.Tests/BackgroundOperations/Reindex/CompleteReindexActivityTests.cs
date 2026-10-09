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
    public async Task GivenFailedDecisionPersistsBeforeCompletionHookFails_WhenRetryConditionsRecover_ThenJobCompletesAsFailed()
    {
        const string canonical = "http://example.org/SearchParameter/patient-retry-decision";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        IReadOnlyList<TenantConfiguration> activeTenants = [Tenant(1), Tenant(2)];
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => activeTenants);
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
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        var completionHook = Substitute.For<IReindexCompletionHook>();
        var failCompletion = true;
        completionHook.OnCompletedAsync(
                Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => failCompletion
                ? Task.FromException(new InvalidOperationException("completion hook unavailable"))
                : Task.CompletedTask);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            repositoryFactory,
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, completionHook),
            tenants);
        var input = JsonSerializer.Serialize(new[]
        {
            new CompleteReindexInput(
                "job",
                1,
                [target],
                [new ReindexTenantOutput(1, true, 1, 1, 1, 1, 0, 0, [], null)],
                [])
        });
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(context, input));

        var persisted = await jobs.GetAsync("job", 1, CancellationToken.None);
        persisted!.Status.ShouldBe("Completing");
        persisted.Progress!["terminalDecision"]!.GetValue<string>().ShouldBe("Failed");

        activeTenants = [Tenant(1)];
        failCompletion = false;

        await activity.RunAsync(context, input);

        var completed = await jobs.GetAsync("job", 1, CancellationToken.None);
        completed!.Status.ShouldBe("Failed");
    }

    [Fact]
    public async Task GivenFailedDecisionPersistsBeforeLifecycleAppendFails_WhenActivityRetries_ThenTargetIsNotEnabled()
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
            RepositoryFactoryWithCatalogId(17),
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, Substitute.For<IReindexCompletionHook>()),
            tenants);
        var input = SingleTenantInput(target);
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });

        failAppend = true;
        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(context, input));
        (await jobs.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Completing");

        failAppend = false;
        await activity.RunAsync(context, input);

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("manual $reindex");
    }

    [Fact]
    public async Task GivenLifecycleCommittedBeforeHookFails_WhenActivityRetries_ThenPersistedFailureReasonIsKept()
    {
        const string canonical = "http://example.org/SearchParameter/patient-retry-reason";
        var (jobs, tenants) = await CreateRunningJobAsync([Tenant(1), Tenant(2)]);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var completionHook = Substitute.For<IReindexCompletionHook>();
        var failHook = true;
        completionHook.OnCompletedAsync(
                Arg.Any<BackgroundJob<ReindexJobDefinition>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => failHook
                ? Task.FromException(new InvalidOperationException("completion hook unavailable"))
                : Task.CompletedTask);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            RepositoryFactoryWithCatalogId(17),
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, completionHook),
            tenants);
        var input = SingleTenantInput(target);
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => activity.RunAsync(context, input));
        failHook = false;
        await activity.RunAsync(context, input);

        var job = await jobs.GetAsync("job", 1, CancellationToken.None);
        job!.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("manual $reindex");
        job.Progress!["terminalOutcomes"]!.AsArray().Count.ShouldBe(1);
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
        var definition = ReindexJobDefinition.CreateForTest();
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
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            repositoryFactory,
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, Substitute.For<IReindexCompletionHook>()),
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
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            repositoryFactory,
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, Substitute.For<IReindexCompletionHook>()),
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
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            repositoryFactory,
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, Substitute.For<IReindexCompletionHook>()),
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
    public async Task GivenJobOwnsTargetNotFullyCoveredByInput_WhenCompletionRuns_ThenJobFailsAndOwnedTargetReturnsToPending()
    {
        const string canonical = "http://example.org/SearchParameter/patient-not-fully-covered";
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
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await lifecycle.StartAsync("job", [target], CancellationToken.None);
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        using var jobLock = new TestJobLock();
        var activity = CreateActivity(
            repositoryFactory,
            lifecycle,
            new ReindexJobUpdater(jobs, jobLock, Substitute.For<IReindexCompletionHook>()),
            tenants);
        var notFullyCovered = target with { ScheduledResourceTypes = [] };

        await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[]
            {
                new CompleteReindexInput(
                    "job",
                    1,
                    [notFullyCovered],
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
            Definition = ReindexJobDefinition.CreateForTest(),
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
        var activity = CreateActivity(
            repositoryFactory,
            lifecycle,
            updater,
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
        job.Progress!["terminalOutcomes"]![0]!["canonical"]!.GetValue<string>()
            .ShouldBe(overrideCanonical);
        job.Progress!["terminalOutcomes"]![0]!["resourceType"]!.GetValue<string>()
            .ShouldBe("Patient");
        job.Progress!["terminalOutcomes"]![0]!["code"]!.GetValue<string>().ShouldBe("custom");
        job.Progress!["terminalOutcomes"]![0]!["success"]!.GetValue<bool>().ShouldBeTrue();
        job.Progress!["terminalOutcomes"]![0]!["resourcesIndexed"]!.GetValue<long>().ShouldBe(1);
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
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        return (jobs, tenants);
    }

    private static IFhirRepositoryFactory RepositoryFactoryWithCatalogId(int searchParamId)
    {
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)repository).HasSearchParameterAsync(searchParamId, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        return repositoryFactory;
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

    private static CompleteReindexActivity CreateActivity(
        IFhirRepositoryFactory repositoryFactory,
        ReindexLifecycleEventWriter lifecycle,
        ReindexJobUpdater updater,
        ITenantConfigurationStore tenantStore)
    {
        var constructor = typeof(CompleteReindexActivity).GetConstructors().Single();
        var arguments = constructor.GetParameters().Length == 5
            ? new object[] { repositoryFactory, lifecycle, updater, tenantStore, TimeProvider.System }
            : [repositoryFactory, lifecycle, updater, TimeProvider.System];
        return (CompleteReindexActivity)constructor.Invoke(arguments);
    }

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
