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
    public async Task GivenNoPendingParameters_WhenAutomationRequestsReindex_ThenNothingToDoIsReturned()
    {
        var fixture = CreateFixture(withPendingParameter: false);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = "Activation" },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await fixture.Repository.ListAsync((int)BackgroundJobType.Reindex)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenRemoteCompletionAfterPollCatchUp_WhenReconciliationAcquiresJobLock_ThenNoRedundantJobIsCreated()
    {
        var fixture = CreateFixture();
        fixture.EventStore.ReadFromAsync(42, Arg.Any<CancellationToken>())
            .Returns(Events(
                new SourceEvent(
                    43,
                    "reindex:remote",
                    nameof(SearchParameterReindexStarted),
                    new SearchParameterReindexStarted(
                        fixture.Target.Canonical,
                        fixture.Target.Code,
                        fixture.Target.ResourceType,
                        "remote-job",
                        fixture.Target.AffectedResourceTypes,
                        fixture.Target.ActivationEventId),
                    DateTimeOffset.UtcNow),
                new SourceEvent(
                    44,
                    "reindex:remote",
                    nameof(SearchParameterReindexCompleted),
                    new SearchParameterReindexCompleted(
                        fixture.Target.Canonical,
                        fixture.Target.Code,
                        fixture.Target.ResourceType,
                        "remote-job",
                        1,
                        TimeSpan.FromSeconds(1),
                        fixture.Target.ActivationEventId),
                    DateTimeOffset.UtcNow)));

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = "Reconciliation" },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await fixture.Repository.ListAsync((int)BackgroundJobType.Reindex)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenJobEndsUnsuccessfullyBeforeReconciliationAcquiresLock_WhenReconciliationCreatesJob_ThenRestartIsSuppressed(
        string status)
    {
        var fixture = CreateFixture();
        fixture.JobLock.ExecuteAsync(
                Arg.Any<Func<CancellationToken, Task<CreateReindexJobResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await fixture.Repository.CreateAsync(FinishedJob("failed-during-race", status, fixture.Definition, fixture.Now), CancellationToken.None);
                return await call.Arg<Func<CancellationToken, Task<CreateReindexJobResult>>>()(
                    call.ArgAt<CancellationToken>(1));
            });

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = "Reconciliation" },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>().ErrorMessage.ShouldContain("failed-during-race");
        var jobs = await fixture.Repository.ListAsync((int)BackgroundJobType.Reindex);
        jobs.ShouldHaveSingleItem().JobId.ShouldBe("failed-during-race");
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenFinishedJobTargetedAnOlderActivation_WhenReconciliationCreatesJob_ThenJobStarts(string status)
    {
        var fixture = CreateFixture();
        var olderDefinition = new ReindexJobDefinition
        {
            TargetEventId = 41,
            TenantIds = [1],
            ResourceTypes = ["Patient"],
            SearchParameters = [],
            MaximumNumberOfResourcesPerQuery = 10_000,
            MaximumNumberOfResourcesPerWrite = 1_000,
            MaximumConcurrency = 4,
            QueryDelayIntervalInMilliseconds = 0,
            Trigger = "Manual"
        };
        await fixture.Repository.CreateAsync(FinishedJob("older", status, olderDefinition, fixture.Now), CancellationToken.None);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = "Reconciliation" },
            CancellationToken.None);

        var created = result.ShouldBeOfType<ReindexJobCreatedResult>();
        var job = (await fixture.Repository.GetAsync(created.JobId, 1, CancellationToken.None))!;
        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.Trigger.ShouldBe("Reconciliation");
        job.Definition.SearchParameters.Select(parameter => parameter.Code).ShouldBe(["custom"]);
    }

    [Fact]
    public async Task GivenFinishedJobCoversPendingParameters_WhenJobIsCreatedManually_ThenJobStarts()
    {
        var fixture = CreateFixture();
        await fixture.Repository.CreateAsync(FinishedJob("failed", "Failed", fixture.Definition, fixture.Now), CancellationToken.None);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        var created = result.ShouldBeOfType<ReindexJobCreatedResult>();
        (await fixture.Repository.GetAsync(created.JobId, 1, CancellationToken.None))!
            .Definition.Trigger.ShouldBe("Manual");
    }

    [Fact]
    public async Task GivenInvalidConcurrency_WhenJobIsCreated_ThenTypedValidationResultIsReturned()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { MaximumConcurrency = 17 },
            CancellationToken.None);

        result.ShouldBeOfType<InvalidReindexRequestResult>();
        (await fixture.Repository.ListAsync()).ShouldBeEmpty();
        _ = fixture.JobLock.DidNotReceiveWithAnyArgs()
            .ExecuteAsync<CreateReindexJobResult>(default!, default);
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    public async Task GivenActiveJob_WhenJobIsCreated_ThenActiveJobIdIsReturned(string status)
    {
        var fixture = CreateFixture();
        await fixture.Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "active",
            JobType = (int)BackgroundJobType.Reindex,
            Status = status,
            Definition = ReindexTestHelper.CreateJobDefinition(),
            CreateDate = fixture.Now,
            HeartbeatDate = fixture.Now
        }, CancellationToken.None);

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("active");
        (await fixture.Repository.ListAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenJobIsCreated_WhenOrchestrationStarts_ThenRowAndInputCarryTheResolvedPlan()
    {
        var fixture = CreateFixture();

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = "Activation" },
            CancellationToken.None);

        var created = result.ShouldBeOfType<ReindexJobCreatedResult>();
        var job = (await fixture.Repository.GetAsync(created.JobId, 1, CancellationToken.None))!;
        job.Status.ShouldBe("Queued");
        job.OrchestrationInstanceId.ShouldBe(created.JobId);
        job.CreateDate.ShouldBe(fixture.Now);
        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.TenantIds.ShouldBe([1]);
        job.Definition.ResourceTypes.ShouldBe(["Patient"]);
        job.Definition.Trigger.ShouldBe("Activation");
        await fixture.Runtime.Received(1).CreateTaskOrchestrationAsync(
            Arg.Is<TaskMessage>(message =>
                message.OrchestrationInstance.InstanceId == created.JobId &&
                message.Event is ExecutionStartedEvent),
            Arg.Any<OrchestrationStatus[]>());
    }

    [Fact]
    public async Task GivenOrchestrationStartFailure_WhenJobIsCreated_ThenRowIsDeletedAndNextAutomaticStartSucceeds()
    {
        var fixture = CreateFixture();
        var failStart = true;
        fixture.Runtime.CreateTaskOrchestrationAsync(
                Arg.Any<TaskMessage>(),
                Arg.Any<OrchestrationStatus[]>())
            .Returns(_ => failStart
                ? Task.FromException(new TimeoutException("runtime unavailable"))
                : Task.CompletedTask);

        await Should.ThrowAsync<TimeoutException>(() => fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None));

        (await fixture.Repository.ListAsync()).ShouldBeEmpty();

        failStart = false;
        var retried = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = "Reconciliation" },
            CancellationToken.None);

        retried.ShouldBeOfType<ReindexJobCreatedResult>();
    }

    private static BackgroundJob<ReindexJobDefinition> FinishedJob(
        string jobId,
        string status,
        ReindexJobDefinition definition,
        DateTimeOffset now) =>
        new()
        {
            JobId = jobId,
            OrchestrationInstanceId = jobId,
            JobType = (int)BackgroundJobType.Reindex,
            Status = status,
            Definition = definition,
            CreateDate = now,
            HeartbeatDate = now,
            EndDate = now
        };

    private static Fixture CreateFixture(bool withPendingParameter = true)
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
        if (withPendingParameter)
        {
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
        }
        var target = new ReindexParameterDefinition(
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
            SearchParameters = [target],
            MaximumNumberOfResourcesPerQuery = 10_000,
            MaximumNumberOfResourcesPerWrite = 1_000,
            MaximumConcurrency = 4,
            QueryDelayIntervalInMilliseconds = 0,
            Trigger = "Manual"
        };
        var jobLock = Substitute.For<IReindexJobLock>();
        jobLock.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<CreateReindexJobResult>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<CreateReindexJobResult>>>()(call.ArgAt<CancellationToken>(1)));
        var eventStore = EventStore();
        var now = new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

        return new Fixture(
            new CreateReindexJobHandler(
                new TaskHubClient(runtime),
                repository,
                jobLock,
                new ReindexTargetResolver(tenants, versions, state, eventStore),
                Options.Create(new ReindexOptions { BarrierDelay = TimeSpan.Zero }),
                new FixedTimeProvider(now)),
            repository,
            runtime,
            jobLock,
            state,
            target,
            definition,
            now,
            eventStore);
    }

    private static ISourceEventStore EventStore()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SourceEvent>());
        return store;
    }

    private sealed record Fixture(
        CreateReindexJobHandler Handler,
        IBackgroundJobRepository<ReindexJobDefinition> Repository,
        IOrchestrationServiceClient Runtime,
        IReindexJobLock JobLock,
        ConformanceState State,
        ReindexParameterDefinition Target,
        ReindexJobDefinition Definition,
        DateTimeOffset Now,
        ISourceEventStore EventStore);

    private static async IAsyncEnumerable<SourceEvent> Events(params SourceEvent[] events)
    {
        foreach (var sourceEvent in events)
        {
            yield return sourceEvent;
        }

        await Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
