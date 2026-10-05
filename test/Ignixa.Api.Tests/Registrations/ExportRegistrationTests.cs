using System.Text;
using System.Text.Json;
using Autofac;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Api.Registrations;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public class ExportRegistrationTests
{
    [Fact]
    public async Task GivenTheRegisteredExportRoot_WhenResolvingAnR5GroupJobAndWorker_ThenTenantSchemaAndMembershipReachSearch()
    {
        using var versions = new FhirVersionContext(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetTenantConfigurationAsync(9, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 9, DisplayName = "R5 export", FhirVersion = "5.0" });
        var repository = Substitute.For<IFhirRepository>();
        repository.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
            .Returns(new SearchEntryResult("Group", "cohort", "1", DateTimeOffset.UnixEpoch, Encoding.UTF8.GetBytes("""
                {"resourceType":"Group","id":"cohort","type":"person","membership":"enumerated",
                 "member":[{"entity":{"reference":"Patient/member"}}]}
                """)));
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(9, Arg.Any<CancellationToken>()).Returns(repository);
        var search = Substitute.For<ISearchService>();
        search.SearchStreamAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(EmptyResults());
        var searches = Substitute.For<ISearchServiceFactory>();
        searches.GetSearchServiceAsync(9, Arg.Any<CancellationToken>()).Returns(search);
        var writers = Substitute.For<IExportStreamWriterFactory>();
        writers.CreateAsync(9, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Substitute.For<IExportStreamWriter>());
        var jobs = new InMemoryBackgroundJobRepository<ExportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        var runtime = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        var builder = new ContainerBuilder();
        builder.RegisterBackgroundJobHandlers();
        builder.RegisterDurableTaskActivities();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        builder.RegisterInstance(versions).As<IFhirVersionContext>().ExternallyOwned();
        builder.RegisterType<FhirRequestContextAccessor>().As<IFhirRequestContextAccessor>().SingleInstance();
        builder.RegisterInstance(NullFhirBaseUriProvider.Instance).As<IFhirBaseUriProvider>();
        builder.RegisterInstance(tenants).As<ITenantConfigurationStore>();
        builder.RegisterInstance(repositories).As<IFhirRepositoryFactory>();
        builder.RegisterInstance(searches).As<ISearchServiceFactory>();
        builder.RegisterInstance(writers).As<IExportStreamWriterFactory>();
        builder.RegisterInstance(Substitute.For<IBlobStorageClient>()).As<IBlobStorageClient>();
        builder.RegisterType<ViewDefinitionLoader>();
        builder.RegisterType<QueryParameterParser>().As<IQueryParameterParser>();
        builder.RegisterType<SearchOptionsBuilderFactory>().As<ISearchOptionsBuilderFactory>().SingleInstance();
        builder.RegisterInstance(jobs).As<IBackgroundJobRepository<ExportJobDefinition>>();
        builder.RegisterInstance(new TaskHubClient(runtime));
        using var container = builder.Build();

        var handler = container.Resolve<IRequestHandler<CreateExportJobCommand, CreateExportJobResult>>();
        var job = await handler.HandleAsync(new CreateExportJobCommand
        {
            TenantId = 9, ResourceTypes = [], GroupId = "cohort", TypeFilters = new Dictionary<string, string>()
        }, CancellationToken.None);
        var definition = (await jobs.GetAsync(job.JobId, 9, CancellationToken.None))!.Definition;
        definition.ResourceTypes.ShouldContain("AllergyIntolerance");
        definition.ResourceTypes.ShouldNotContain("Organization");
        var worker = container.Resolve<ExportWorkerActivity>();
        await worker.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = job.JobId }),
            JsonSerializer.Serialize(new[] { new ExportWorkerInput(job.JobId, 9, "Observation", 1, 100, "test.ndjson", GroupId: "cohort") }));

        search.Received(1).SearchStreamAsync(Arg.Is<SearchOptions>(options =>
            options.ResourceType == "Observation" && options.Expression.ToString().Contains("Patient 'member'", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<SearchEntryResult> EmptyResults()
    {
        await Task.CompletedTask;
        yield break;
    }
}
