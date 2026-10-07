using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class CreateReindexJobHandlerTests
{
    [Fact]
    public async Task GivenInvalidConcurrency_WhenJobIsCreated_ThenTypedValidationResultIsReturned()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { MaximumConcurrency = 17 },
            CancellationToken.None);

        result.ShouldBeOfType<InvalidReindexRequestResult>();
        (await fixture.Repository.ListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenActiveJob_WhenJobIsCreated_ThenActiveJobIdIsReturned()
    {
        var fixture = CreateFixture();
        await fixture.Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "active",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        fixture.Runtime.GetOrchestrationStateAsync("active", false)
            .Returns([
                new OrchestrationState
                {
                    OrchestrationInstance = new OrchestrationInstance { InstanceId = "active" },
                    OrchestrationStatus = OrchestrationStatus.Running
                }
            ]);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("active");
    }

    [Fact]
    public async Task GivenFreshQueuedJobWithMissingOrchestration_WhenJobIsCreated_ThenExistingJobRemainsActive()
    {
        var fixture = CreateFixture();
        await fixture.Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "fresh",
            OrchestrationInstanceId = "fresh",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Queued",
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = fixture.Now,
            HeartbeatDate = fixture.Now
        }, CancellationToken.None);
        fixture.Runtime.GetOrchestrationStateAsync("fresh", false)
            .Returns([]);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("fresh");
        var jobs = await fixture.Repository.ListAsync();
        jobs.Single().Status.ShouldBe("Queued");
    }

    [Fact]
    public async Task GivenOldRunningJobWithMissingOrchestration_WhenJobIsCreated_ThenLifecycleIsFailedAndNewJobStarts()
    {
        var fixture = CreateFixture();
        await fixture.Lifecycle.StartAsync("orphan", [fixture.Target], CancellationToken.None);
        await fixture.Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "orphan",
            OrchestrationInstanceId = "orphan",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = fixture.Definition,
            CreateDate = fixture.Now - TimeSpan.FromMinutes(10),
            HeartbeatDate = fixture.Now - TimeSpan.FromMinutes(10)
        }, CancellationToken.None);
        fixture.Runtime.GetOrchestrationStateAsync("orphan", false)
            .Returns([], []);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ReindexJobCreatedResult>();
        var jobs = await fixture.Repository.ListAsync();
        jobs.Single(job => job.JobId == "orphan").Status.ShouldBe("Failed");
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Pending);
        jobs.Count(job => job.Status == "Queued").ShouldBe(1);
    }

    [Fact]
    public async Task GivenOldQueuedJobAppearsOnRequery_WhenJobIsCreated_ThenExistingJobRemainsActive()
    {
        var fixture = CreateFixture();
        await fixture.Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "registering",
            OrchestrationInstanceId = "registering",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Queued",
            Definition = fixture.Definition,
            CreateDate = fixture.Now - TimeSpan.FromMinutes(10),
            HeartbeatDate = fixture.Now - TimeSpan.FromMinutes(10)
        }, CancellationToken.None);
        fixture.Runtime.GetOrchestrationStateAsync("registering", false)
            .Returns(
                [],
                [
                    new OrchestrationState
                    {
                        OrchestrationInstance = new OrchestrationInstance
                        {
                            InstanceId = "registering"
                        },
                        OrchestrationStatus = OrchestrationStatus.Pending
                    }
                ]);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("registering");
        (await fixture.Repository.ListAsync()).Single().Status.ShouldBe("Queued");
    }

    [Fact]
    public async Task GivenCompletingJob_WhenJobIsCreated_ThenPersistedDecisionIsReconciledAndNewJobStarts()
    {
        var fixture = CreateFixture();
        await fixture.Lifecycle.StartAsync("completing", [fixture.Target], CancellationToken.None);
        await fixture.Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "completing",
            OrchestrationInstanceId = "completing",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Completing",
            Definition = fixture.Definition,
            Progress = new System.Text.Json.Nodes.JsonObject
            {
                ["terminalDecision"] = "Failed",
                ["terminalOutcomes"] = new System.Text.Json.Nodes.JsonArray
                {
                    new System.Text.Json.Nodes.JsonObject
                    {
                        ["canonical"] = fixture.Target.Canonical,
                        ["success"] = false,
                        ["resourcesIndexed"] = 0,
                        ["errorMessage"] = "worker failed"
                    }
                }
            },
            CreateDate = fixture.Now - TimeSpan.FromMinutes(10),
            HeartbeatDate = fixture.Now
        }, CancellationToken.None);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ReindexJobCreatedResult>();
        var jobs = await fixture.Repository.ListAsync();
        jobs.Single(job => job.JobId == "completing").Status.ShouldBe("Failed");
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Pending);
        jobs.Count(job => job.Status == "Queued").ShouldBe(1);
    }

    [Fact]
    public async Task GivenOrchestrationStartFailure_WhenJobIsCreated_ThenQueuedJobIsFailed()
    {
        var fixture = CreateFixture();
        fixture.Runtime.CreateTaskOrchestrationAsync(
                Arg.Any<TaskMessage>(),
                Arg.Any<OrchestrationStatus[]>())
            .Returns<Task>(_ => throw new InvalidOperationException("runtime unavailable"));

        await Should.ThrowAsync<InvalidOperationException>(() => fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None));

        var job = (await fixture.Repository.ListAsync()).Single();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("runtime unavailable");
    }

    private static Fixture CreateFixture()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TenantConfiguration
                {
                    TenantId = 1,
                    DisplayName = "Tenant",
                    FhirVersion = "4.0"
                }
            });
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        runtime.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.CompletedTask);
        var versions = Substitute.For<IFhirVersionContext>();
        var schema = Substitute.For<IFhirSchemaProvider>();
        schema.ResourceTypeNames.Returns(new HashSet<string>(StringComparer.Ordinal) { "Patient" });
        var patient = Substitute.For<IType>();
        patient.Info.Returns(new TypeInfo("Patient", isResource: true));
        schema.GetTypeDefinition("Patient").Returns(patient);
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(schema);
        var state = new ConformanceState();
        state.ApplyAndTrack(new SourceEvent(
            42,
            "search",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/patient-custom",
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
            DateTimeOffset.UtcNow));
        var target = new ReindexTarget(
            "http://example.org/SearchParameter/patient-custom",
            "custom",
            "Patient",
            17,
            42,
            ["Patient"]);
        var definition = new ReindexJobDefinition
        {
            TargetEventId = 42,
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
        var jobLock = Substitute.For<IReindexJobLock>();
        jobLock.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<CreateReindexJobResult>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<CreateReindexJobResult>>>()(call.ArgAt<CancellationToken>(1)));
        jobLock.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<bool>>>()(call.ArgAt<CancellationToken>(1)));
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IFhirRepository, IReindexStore>());
        var eventStore = EventStore();
        var lifecycle = new ReindexLifecycleEventWriter(eventStore, state);
        var updater = new ReindexJobUpdater(
            repository,
            jobLock,
            Substitute.For<IReindexCompletionHook>());
        var now = new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var reconciler = new ReindexJobReconciler(
            new TaskHubClient(runtime),
            repository,
            lifecycle,
            updater,
            state,
            jobLock,
            Options.Create(new ReindexOptions
            {
                BarrierDelay = TimeSpan.Zero,
                OrphanGrace = TimeSpan.FromMinutes(2)
            }),
            timeProvider,
            NullLogger<ReindexJobReconciler>.Instance);

        return new Fixture(
            new CreateReindexJobHandler(
                new TaskHubClient(runtime),
                repository,
                tenants,
                versions,
                state,
                repositories,
                jobLock,
                reconciler,
                Options.Create(new ReindexOptions { BarrierDelay = TimeSpan.Zero })),
            repository,
            runtime,
            lifecycle,
            state,
            target,
            definition,
            now);
    }

    private static ISourceEventStore EventStore()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SourceEvent>());
        long nextEventId = 43;
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

    private sealed record Fixture(
        CreateReindexJobHandler Handler,
        IBackgroundJobRepository<ReindexJobDefinition> Repository,
        IOrchestrationServiceClient Runtime,
        ReindexLifecycleEventWriter Lifecycle,
        ConformanceState State,
        ReindexTarget Target,
        ReindexJobDefinition Definition,
        DateTimeOffset Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
