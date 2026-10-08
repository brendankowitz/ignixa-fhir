using System.Text.Json;
using Autofac;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Registrations;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public class BulkDeleteRegistrationTests
{
    [Fact]
    public async Task GivenTheRegisteredRoots_WhenABulkDeleteJobIsCreatedAndItsActivitiesRun_ThenEveryDependencyResolves()
    {
        using var versions = new FhirVersionContext(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetTenantConfigurationAsync(9, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 9, DisplayName = "Bulk delete", FhirVersion = "4.0" });
        var repository = Substitute.For<IFhirRepository>();
        repository.SupportsPhysicalDeletion.Returns(true);
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(9, Arg.Any<CancellationToken>()).Returns(repository);
        var search = Substitute.For<ISearchService>();
        search.SearchStreamAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(EmptyResults());
        var searches = Substitute.For<ISearchServiceFactory>();
        searches.GetSearchServiceAsync(9, Arg.Any<CancellationToken>()).Returns(search);
        var jobs = new InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>>.Instance);
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);
        var builder = new ContainerBuilder();
        builder.RegisterBackgroundJobHandlers();
        builder.RegisterDurableTaskActivities();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        builder.RegisterInstance(versions).As<IFhirVersionContext>().ExternallyOwned();
        builder.RegisterType<FhirRequestContextAccessor>().As<IFhirRequestContextAccessor>().SingleInstance();
        builder.RegisterInstance(NullFhirBaseUriProvider.Instance).As<IFhirBaseUriProvider>();
        builder.RegisterInstance(tenants).As<ITenantConfigurationStore>();
        builder.RegisterInstance(repositories).As<IFhirRepositoryFactory>();
        builder.RegisterInstance(searches).As<ISearchServiceFactory>();
        builder.RegisterInstance(Substitute.For<IMediator>()).As<IMediator>();
        builder.RegisterInstance(lifetime).As<IHostApplicationLifetime>();
        builder.RegisterType<QueryParameterParser>().As<IQueryParameterParser>();
        builder.RegisterType<SearchOptionsBuilderFactory>().As<ISearchOptionsBuilderFactory>().SingleInstance();
        builder.RegisterInstance(jobs).As<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        builder.RegisterInstance(Options.Create(new BulkDeleteOptions { BatchSize = 25 })).As<IOptions<BulkDeleteOptions>>();
        builder.RegisterInstance(new TaskHubClient(new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance)));
        using var container = builder.Build();

        var created = await container.Resolve<IRequestHandler<CreateBulkDeleteJobCommand, CreateBulkDeleteJobResult>>().HandleAsync(
            new CreateBulkDeleteJobCommand(9, "Patient", [new("name", "smith")], BulkDeleteMode.HardDelete, [], false, null),
            CancellationToken.None);
        var context = new TaskContext(new OrchestrationInstance { InstanceId = created.JobId });
        await container.Resolve<BulkDeleteBatchActivity>().RunAsync(context, JsonSerializer.Serialize(new[]
        {
            new BulkDeleteBatchInput(created.JobId, 9, "Patient", "name=smith", BulkDeleteMode.HardDelete, [], false, 25, null, []),
        }));
        await container.Resolve<CompleteBulkDeleteJobActivity>().RunAsync(context, JsonSerializer.Serialize(new[]
        {
            new CompleteBulkDeleteJobInput(created.JobId, 9, true, [], null),
        }));
        var status = await container.Resolve<IRequestHandler<GetBulkDeleteStatusQuery, GetBulkDeleteStatusResult>>()
            .HandleAsync(new GetBulkDeleteStatusQuery(9, created.JobId), CancellationToken.None);
        var cancel = await container.Resolve<IRequestHandler<CancelBulkDeleteCommand, CancelBulkDeleteOutcome>>()
            .HandleAsync(new CancelBulkDeleteCommand(9, created.JobId), CancellationToken.None);

        status.Status.ShouldBe("Completed");
        cancel.ShouldBe(CancelBulkDeleteOutcome.AlreadyTerminal);
        container.Resolve<BulkDeleteOrchestration>().ShouldNotBeNull();
        search.Received(1).SearchStreamAsync(Arg.Is<SearchOptions>(options =>
            options.ResourceType == "Patient" && options.MaxItemCount == 25), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    public void GivenAnOutOfRangeBatchSize_WhenBulkDeleteOptionsAreRead_ThenValidationFails(string batchSize)
    {
        using var provider = CreateBackgroundServices(batchSize);

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<BulkDeleteOptions>>().Value);

        failure.Message.ShouldContain("BulkDelete:BatchSize");
    }

    [Fact]
    public void GivenAConfiguredBatchSize_WhenBulkDeleteOptionsAreRead_ThenItIsBound()
    {
        using var provider = CreateBackgroundServices("10000");

        provider.GetRequiredService<IOptions<BulkDeleteOptions>>().Value.BatchSize.ShouldBe(10_000);
    }

    [Fact]
    public void GivenTheDurableTaskWorker_WhenItIsBuilt_ThenTheBulkDeleteOrchestrationAndActivitiesRegisterWithoutConflicts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bulk-delete-worker", Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DurableTask:Provider"] = "FileSystem",
                    ["FhirRepository:BaseDirectory"] = directory,
                }).Build())
                .AddDurableTask()
                .BuildServiceProvider();

            provider.GetRequiredService<TaskHubWorker>().ShouldNotBeNull();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ServiceProvider CreateBackgroundServices(string batchSize) =>
        new ServiceCollection()
            .AddIgnixaBackgroundServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BulkDelete:BatchSize"] = batchSize,
            }).Build())
            .BuildServiceProvider();

    private static async IAsyncEnumerable<SearchEntryResult> EmptyResults()
    {
        await Task.CompletedTask;
        yield break;
    }
}
