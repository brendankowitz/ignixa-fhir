using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Application.BackgroundOperations.Import;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ImportJobConcurrencyConfigurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GivenConfiguredConcurrency_WhenCreatingImport_ThenThePersistedWorkflowUsesIt(int concurrency)
    {
        var (handler, repository, runtime) = CreateHandler(concurrency);
        string? workflowInput = null;
        runtime.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(call =>
            {
                workflowInput = ((ExecutionStartedEvent)call.Arg<TaskMessage>().Event).Input;
                return Task.CompletedTask;
            });

        var result = await handler.HandleAsync(Command(), CancellationToken.None);

        var input = JsonNode.Parse(workflowInput!)!.AsObject();
        var configured = input.FirstOrDefault(property => property.Key.Equals("MaxConcurrentFiles", StringComparison.OrdinalIgnoreCase)).Value;
        configured.ShouldNotBeNull();
        configured!.GetValue<int>().ShouldBe(concurrency);
        (await repository.GetAsync(result.JobId, 1, CancellationToken.None)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GivenInvalidConcurrency_WhenCreatingImport_ThenNoJobIsQueued(int concurrency)
    {
        var (handler, repository, _) = CreateHandler(concurrency);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(Command(), CancellationToken.None));

        failure.Message.ShouldContain("MaxConcurrentFiles");
        (await repository.ListAsync()).ShouldBeEmpty();
    }

    private static (CreateImportJobHandler Handler, IBackgroundJobRepository<ImportJobDefinition> Repository, IOrchestrationServiceClient Runtime)
        CreateHandler(int concurrency)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ImportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ImportJobDefinition>>.Instance);
        var runtime = Substitute.For<IOrchestrationServiceClient>();
        using var services = new ServiceCollection()
            .AddSingleton<IBackgroundJobRepository<ImportJobDefinition>>(repository)
            .AddSingleton(new TaskHubClient(runtime))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Import:MaxConcurrentFiles"] = concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).Build())
            .BuildServiceProvider();
        return (ActivatorUtilities.CreateInstance<CreateImportJobHandler>(services), repository, runtime);
    }

    private static CreateImportJobCommand Command() => new()
    {
        TenantId = 1, InputFiles = [new InputFileInfo { Type = "Patient", Url = "input.ndjson" }], Mode = "IncrementalLoad"
    };
}
