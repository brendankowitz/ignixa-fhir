using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
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

        var result = await fixture.Handler.HandleAsync(
            new CreateReindexJobCommand(),
            CancellationToken.None);

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("active");
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
        var jobLock = Substitute.For<IReindexJobLock>();
        jobLock.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<CreateReindexJobResult>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<CreateReindexJobResult>>>()(call.ArgAt<CancellationToken>(1)));
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IFhirRepository, IReindexStore>());

        return new Fixture(
            new CreateReindexJobHandler(
                new TaskHubClient(runtime),
                repository,
                tenants,
                versions,
                state,
                repositories,
                jobLock,
                Options.Create(new ReindexOptions { BarrierDelay = TimeSpan.Zero })),
            repository,
            runtime);
    }

    private sealed record Fixture(
        CreateReindexJobHandler Handler,
        IBackgroundJobRepository<ReindexJobDefinition> Repository,
        IOrchestrationServiceClient Runtime);
}
