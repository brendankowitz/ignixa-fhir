using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Application.BackgroundOperations.Terminology.EventHandlers;
using Ignixa.Application.Events.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class TerminologyImportCreationTests
{
    private const string SystemUrl = "https://example.test/review-cs";
    private const string Leaf = "https://example.test/leaf-vs";
    private const string Parent = "https://example.test/parent-vs";

    [Fact]
    public async Task GivenDependentValueSetsBeforeCodeSystem_WhenCreatingJob_ThenPersistedPlanCarriesExactDependencies()
    {
        var resources = Resources();
        var repository = Repository(resources);
        var client = Substitute.For<IOrchestrationServiceClient>();
        ExecutionStartedEvent started = null;
        client.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(call =>
            {
                started = (ExecutionStartedEvent)call.Arg<TaskMessage>().Event;
                return Task.CompletedTask;
            });
        using var services = Services(repository);
        var handler = ActivatorUtilities.CreateInstance<TerminologyImportTriggeredHandler>(
            services, new TaskHubClient(client), NullLogger<TerminologyImportTriggeredHandler>.Instance);

        await handler.HandleAsync(new TerminologyImportTriggeredEvent(1, "review.package", "1.0.0", [3, 2, 1]), CancellationToken.None);

        started.ShouldNotBeNull();
        var plan = JsonNode.Parse(started.Input)!["DependencyPlan"];
        plan.ShouldNotBeNull();
        var byId = plan.AsArray().ToDictionary(p => p!["PackageResourceId"]!.GetValue<long>());
        Dependencies(byId[1]).ShouldBeEmpty();
        Dependencies(byId[2]).ShouldBe([1L]);
        Dependencies(byId[3]).ShouldBe([2L]);
    }

    [Fact]
    public async Task GivenExternalReferenceAndPrecomputedExpansion_WhenCreatingJob_ThenNoUnsupportedDependencyIsInvented()
    {
        PackageResource[] resources =
        [
            Resource(1, "CodeSystem", SystemUrl, $$"""{"resourceType":"CodeSystem","url":"{{SystemUrl}}","version":"V1","content":"complete"}""", "V1"),
            Resource(2, "ValueSet", Leaf, $$$"""
                {"resourceType":"ValueSet","url":"{{{Leaf}}}","status":"active","compose":{"include":[{"system":"{{{SystemUrl}}}","version":"v1"}]}}
                """),
            Resource(3, "ValueSet", Parent, $$$"""
                {"resourceType":"ValueSet","url":"{{{Parent}}}","status":"active","expansion":{"contains":[]},"compose":{"include":[{"valueSet":["{{{Parent}}}"]}]}}
                """),
        ];
        var client = Substitute.For<IOrchestrationServiceClient>();
        ExecutionStartedEvent started = null;
        client.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>()).Returns(call =>
        {
            started = (ExecutionStartedEvent)call.Arg<TaskMessage>().Event;
            return Task.CompletedTask;
        });
        using var services = Services(Repository(resources));
        var handler = ActivatorUtilities.CreateInstance<TerminologyImportTriggeredHandler>(
            services, new TaskHubClient(client), NullLogger<TerminologyImportTriggeredHandler>.Instance);

        await handler.HandleAsync(new TerminologyImportTriggeredEvent(1, "review.package", "1.0.0", [1, 2, 3]), CancellationToken.None);

        var plan = JsonNode.Parse(started!.Input)!["DependencyPlan"];
        plan.ShouldNotBeNull();
        plan.AsArray().ShouldAllBe(p => Dependencies(p).Count == 0);
    }

    [Fact]
    public async Task GivenPlanningFailure_WhenCreatingJob_ThenNoSuccessIsReportedAndNoJobIsSubmitted()
    {
        var repository = Repository([]);
        repository.ListPackageResourcesAsync("review.package", "1.0.0", null, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<PackageResource>>>(_ => throw new IOException("metadata unavailable"));
        var client = Substitute.For<IOrchestrationServiceClient>();
        using var services = Services(repository);
        var handler = ActivatorUtilities.CreateInstance<TerminologyImportTriggeredHandler>(
            services, new TaskHubClient(client), NullLogger<TerminologyImportTriggeredHandler>.Instance);

        var error = await Should.ThrowAsync<IOException>(() => handler.HandleAsync(
            new TerminologyImportTriggeredEvent(1, "review.package", "1.0.0", [1]), CancellationToken.None));

        error.Message.ShouldBe("metadata unavailable");
        await client.DidNotReceiveWithAnyArgs().CreateTaskOrchestrationAsync(default, default);
    }

    [Fact]
    public async Task GivenJobSubmissionFailure_WhenCreatingJob_ThenFailureReachesTheRecoverableCallerBoundary()
    {
        var client = Substitute.For<IOrchestrationServiceClient>();
        client.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.FromException(new IOException("job store unavailable")));
        using var services = Services(Repository(Resources()));
        var handler = ActivatorUtilities.CreateInstance<TerminologyImportTriggeredHandler>(
            services, new TaskHubClient(client), NullLogger<TerminologyImportTriggeredHandler>.Instance);

        (await Should.ThrowAsync<IOException>(() => handler.HandleAsync(
            new TerminologyImportTriggeredEvent(1, "review.package", "1.0.0", [1, 2, 3]), CancellationToken.None)))
            .Message.ShouldBe("job store unavailable");
    }

    private static IReadOnlyList<long> Dependencies(JsonNode entry)
        => entry["DependsOn"]!.AsArray().Select(id => id!.GetValue<long>()).ToArray();

    private static ServiceProvider Services(IPackageResourceRepository repository)
        => new ServiceCollection().AddSingleton(repository).BuildServiceProvider();

    private static IPackageResourceRepository Repository(IReadOnlyList<PackageResource> resources)
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.ListPackageResourcesAsync("review.package", "1.0.0", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(resources));
        return repository;
    }

    private static PackageResource[] Resources() =>
    [
        Resource(3, "ValueSet", Parent, $$$"""
            {"resourceType":"ValueSet","url":"{{{Parent}}}","status":"active","compose":{"include":[{"valueSet":["{{{Leaf}}}"]}]}}
            """),
        Resource(2, "ValueSet", Leaf, $$$"""
            {"resourceType":"ValueSet","url":"{{{Leaf}}}","status":"active","compose":{"include":[{"system":"{{{SystemUrl}}}"}]}}
            """),
        Resource(1, "CodeSystem", SystemUrl, $$"""{"resourceType":"CodeSystem","url":"{{SystemUrl}}","content":"complete","concept":[{"code":"one"},{"code":"two"},{"code":"three"}]}"""),
    ];

    private static PackageResource Resource(long id, string type, string canonical, string json, string version = null) => new()
    {
        PackageResourceId = id, PackageId = "review.package", PackageVersion = "1.0.0",
        ResourceId = id.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceType = type,
        Canonical = canonical, Version = version, ResourceJson = json, FhirVersion = "4.0.1",
    };
}
