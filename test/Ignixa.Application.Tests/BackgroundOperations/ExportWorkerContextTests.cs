using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Models;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ExportWorkerContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAnAmbientOtherTenant_WhenAWorkerRuns_ThenItsOwnContextIsUsedAndTheOriginalIsRestored(bool fail)
    {
        var previous = FhirRequestContextFactory.CreateBackgroundContext(2);
        IFhirRequestContext? current = previous;
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(_ => current);
        accessor.When(value => value.RequestContext = Arg.Any<IFhirRequestContext>())
            .Do(call => current = call.Arg<IFhirRequestContext>());
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Worker tenant", FhirVersion = "4.0" });
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(FhirVersion.R4.GetSchemaProvider());
        var searches = Substitute.For<ISearchServiceFactory>();
        var search = Substitute.For<ISearchService>();
        searches.GetSearchServiceAsync(1, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            current!.TenantId.ShouldBe(1);
            current.IsBackgroundTask.ShouldBeTrue();
            return search;
        });
        search.SearchStreamAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(Empty());
        var options = Substitute.For<ISearchOptionsBuilder>();
        options.Build(Arg.Any<string>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<ISchema?>(),
            Arg.Any<IList<ParameterTrace>?>()).Returns(new SearchOptions { ResourceType = "Patient" });
        var builders = Substitute.For<ISearchOptionsBuilderFactory>();
        builders.Create(FhirVersion.R4, 1).Returns(options);
        var writer = Substitute.For<IExportStreamWriter>();
        if (fail)
        {
            writer.FlushAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("Writer failed")));
        }
        var writers = Substitute.For<IExportStreamWriterFactory>();
        writers.CreateAsync(1, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(writer);
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        var worker = new ExportWorkerActivity(searches, writers, tenants, new QueryParameterParser(), builders,
            new ViewDefinitionLoader(repositories, NullLogger<ViewDefinitionLoader>.Instance),
            Substitute.For<IBlobStorageClient>(), versions, NullLoggerFactory.Instance,
            NullLogger<ExportWorkerActivity>.Instance, new ExportGroupResolver(repositories, NullFhirBaseUriProvider.Instance),
            accessor);
        var input = new ExportWorkerInput("context", 1, "Patient", 1, 100, "test.ndjson");
        async Task RunAsync() => await worker.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "context" }),
            JsonSerializer.Serialize(new[] { input }));

        if (fail)
        {
            var exception = await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(RunAsync);
            exception.Message.ShouldContain("Writer failed");
        }
        else
        {
            await RunAsync();
        }
        current.ShouldBeSameAs(previous);
    }

    private static async IAsyncEnumerable<SearchEntryResult> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
