using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Parsing;
using Ignixa.Serialization.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public sealed class CreateBulkDeleteJobHandlerTests : IDisposable
{
    private const int TenantId = 1;
    private readonly FhirVersionContext _versions = new(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(),
        NullFhirBaseUriProvider.Instance);
    private readonly SearchOptionsBuilderFactory _builders;
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();
    private readonly InMemoryBackgroundJobRepository<BulkDeleteJobDefinition> _jobs;
    private readonly IFhirRepository _repository = Substitute.For<IFhirRepository>();
    private readonly IOrchestrationServiceClient _orchestrations = Substitute.For<IOrchestrationServiceClient>();
    private readonly List<TaskMessage> _started = [];

    public CreateBulkDeleteJobHandlerTests()
    {
        _builders = new SearchOptionsBuilderFactory(_versions, NullFhirBaseUriProvider.Instance);
        _tenants.Mode.Returns(TenantMode.Isolated);
        _tenants.GetTenantConfigurationAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = TenantId, DisplayName = "Bulk delete", FhirVersion = "4.0" });
        _jobs = new(_tenants, NullLogger<InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>>.Instance);
        _repository.SupportsPhysicalDeletion.Returns(true);
        _orchestrations.CreateTaskOrchestrationAsync(Arg.Do<TaskMessage>(_started.Add)).Returns(Task.CompletedTask);
        _orchestrations.CreateTaskOrchestrationAsync(Arg.Do<TaskMessage>(_started.Add), Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task GivenSystemLevelTypeFilterAndExclusion_WhenCreating_ThenTheSnapshotIsTheIntersectionMinusExclusions()
    {
        var result = await CreateHandler().HandleAsync(
            Command(null, [new("_type", "Patient,Observation"), new("_type", "Condition")], excluded: ["Observation"]),
            CancellationToken.None);

        var job = (await _jobs.GetAsync(result.JobId, TenantId, CancellationToken.None))!;
        job.Definition.ResourceTypes.ShouldBe(["Condition", "Patient"]);
        job.Definition.IsSystemLevel.ShouldBeTrue();
        job.Definition.ExcludedResourceTypes.ShouldBe(["Observation"]);
        job.Definition.SearchQuery.ShouldBeEmpty();
        job.JobType.ShouldBe((int)BackgroundJobType.BulkDelete);
        job.Status.ShouldBe("Queued");
        job.OrchestrationInstanceId.ShouldBe(result.JobId);
    }

    [Fact]
    public async Task GivenSystemLevelWithoutTypeFilter_WhenCreating_ThenEveryConcreteTypeExceptExclusionsIsSnapshottedInOrdinalOrder()
    {
        var result = await CreateHandler().HandleAsync(Command(null, [], excluded: ["Patient"]), CancellationToken.None);

        var types = (await _jobs.GetAsync(result.JobId, TenantId, CancellationToken.None))!.Definition.ResourceTypes;
        types.ShouldContain("Observation");
        types.ShouldContain("Binary");
        types.ShouldNotContain("Patient");
        types.ShouldNotContain("Resource");
        types.ShouldNotContain("DomainResource");
        types.ShouldBe(types.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task GivenAValidRequest_WhenCreating_ThenTheOrchestrationStartsWithTheSnapshotAndConfiguredBatchSize()
    {
        var result = await CreateHandler(batchSize: 7).HandleAsync(
            Command("Patient", [new("name", "smith")], BulkDeleteMode.HardDelete, removeReferences: true),
            CancellationToken.None);

        var started = _started.ShouldHaveSingleItem();
        started.OrchestrationInstance.InstanceId.ShouldBe(result.JobId);
        var execution = started.Event.ShouldBeOfType<ExecutionStartedEvent>();
        execution.Name.ShouldBe(NameVersionHelper.GetDefaultName(typeof(BulkDeleteOrchestration)));
        var input = JsonNode.Parse(execution.Input)!;
        input["JobId"]!.GetValue<string>().ShouldBe(result.JobId);
        input["TenantId"]!.GetValue<int>().ShouldBe(TenantId);
        input["ResourceTypes"]!.AsArray().Select(type => type!.GetValue<string>()).ShouldBe(["Patient"]);
        input["SearchQuery"]!.GetValue<string>().ShouldBe("name=smith");
        input["Mode"]!.GetValue<int>().ShouldBe((int)BulkDeleteMode.HardDelete);
        input["RemoveReferences"]!.GetValue<bool>().ShouldBeTrue();
        input["BatchSize"]!.GetValue<int>().ShouldBe(7);
    }

    [Fact]
    public async Task GivenValuesWithSearchSyntaxCharacters_WhenCreating_ThenTheSearchQueryRoundTripsThroughTheParser()
    {
        KeyValuePair<string, string>[] parameters =
        [
            new("identifier", "http://example.org/ids|a b,c:d"),
            new("_include:iterate", "Patient:organization"),
            new("_revinclude", "Observation:subject"),
            new("_lastUpdated", "lt2020-01-01T00:00:00+01:00"),
            new("_format", "json"),
            new("_pretty", "true"),
        ];

        var result = await CreateHandler().HandleAsync(Command("Patient", parameters), CancellationToken.None);

        var definition = (await _jobs.GetAsync(result.JobId, TenantId, CancellationToken.None))!.Definition;
        new QueryParameterParser().Parse(definition.SearchQuery).ShouldBe(
        [
            new QueryParameter("identifier", "http://example.org/ids|a b,c:d"),
            new QueryParameter("_include:iterate", "Patient:organization"),
            new QueryParameter("_revinclude", "Observation:subject"),
            new QueryParameter("_lastUpdated", "lt2020-01-01T00:00:00+01:00"),
        ]);
    }

    [Fact]
    public async Task GivenTheSystemPartition_WhenCreating_ThenTheRequestIsRejectedBeforeAnyJobIsCreated()
    {
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        var jobs = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        var handler = new CreateBulkDeleteJobHandler(new TaskHubClient(_orchestrations), jobs, _tenants, _versions,
            repositories, _builders, Options.Create(new BulkDeleteOptions()), NullLogger<CreateBulkDeleteJobHandler>.Instance);

        var failure = await Should.ThrowAsync<BadRequestException>(() => handler.HandleAsync(
            new CreateBulkDeleteJobCommand(0, null, [], BulkDeleteMode.HardDelete, [], false, null), CancellationToken.None));

        failure.Message.ShouldContain("system partition");
        await jobs.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await _tenants.DidNotReceiveWithAnyArgs().GetTenantConfigurationAsync(default, default);
        await repositories.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default, default);
        _started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("_count", "10")]
    [InlineData("_sort", "name")]
    [InlineData("_summary", "true")]
    [InlineData("_elements", "id")]
    [InlineData("_total", "accurate")]
    [InlineData("ct", "abc")]
    [InlineData("_contained", "true")]
    [InlineData("_containedType", "contained")]
    [InlineData("_includesCount", "5")]
    [InlineData("_includesContinuationToken", "abc")]
    public async Task GivenAResultShapingParameter_WhenCreating_ThenTheRequestIsRejected(string name, string value)
    {
        var failure = await Should.ThrowAsync<BadRequestException>(() =>
            CreateHandler().HandleAsync(Command("Patient", [new(name, value)]), CancellationToken.None));

        failure.Message.ShouldContain(name);
        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnUnknownSearchParameter_WhenCreating_ThenTheRequestIsRejectedInsteadOfWideningTheDelete()
    {
        var failure = await Should.ThrowAsync<BadRequestException>(() =>
            CreateHandler().HandleAsync(Command("Patient", [new("not-a-parameter", "x")]), CancellationToken.None));

        failure.Message.ShouldContain("not-a-parameter");
        failure.Message.ShouldContain("Patient");
    }

    [Fact]
    public async Task GivenAFilterInvalidForOneSystemLevelType_WhenCreating_ThenTheRequestIsRejectedNamingThatType()
    {
        var failure = await Should.ThrowAsync<BadRequestException>(() => CreateHandler().HandleAsync(
            Command(null, [new("_type", "Patient,Observation"), new("name", "smith")]), CancellationToken.None));

        failure.Message.ShouldContain("'Observation'");
    }

    [Fact]
    public async Task GivenAnUnsupportedModifier_WhenCreating_ThenTheRequestIsRejectedWith400()
    {
        var failure = await Should.ThrowAsync<SearchModifierNotSupportedException>(() =>
            CreateHandler().HandleAsync(Command("Patient", [new("_id:above", "x")]), CancellationToken.None));

        ((FhirException)failure).StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task GivenAnInvalidInclude_WhenCreating_ThenTheSearchExceptionSurfacesAs400()
    {
        var failure = await Should.ThrowAsync<FhirException>(() =>
            CreateHandler().HandleAsync(Command("Patient", [new("_include", "Patient:not-a-reference")]), CancellationToken.None));

        failure.StatusCode.ShouldBe(400);
    }

    public static TheoryData<string?, KeyValuePair<string, string>[], string[], BulkDeleteMode, bool, string> InvalidRequests => new()
    {
        { "Patient", [new("_type", "Patient")], [], BulkDeleteMode.SoftDelete, false, "_type" },
        { "Patient", [], ["Patient"], BulkDeleteMode.SoftDelete, false, "Patient" },
        { "NotAType", [], [], BulkDeleteMode.SoftDelete, false, "NotAType" },
        { "Resource", [], [], BulkDeleteMode.SoftDelete, false, "Resource" },
        { null, [new("_type", "NotAType")], [], BulkDeleteMode.SoftDelete, false, "NotAType" },
        { null, [new("_type", ",")], [], BulkDeleteMode.SoftDelete, false, "_type" },
        { null, [], ["NotAType"], BulkDeleteMode.SoftDelete, false, "NotAType" },
        { null, [new("_type", "Patient")], ["Patient"], BulkDeleteMode.SoftDelete, false, "No resource types remain" },
        { "Patient", [], [], BulkDeleteMode.SoftDelete, true, "_remove-references" },
        { "Patient", [], [], BulkDeleteMode.PurgeHistory, true, "_remove-references" },
        { "Patient", [new("_include:iterate&x", "Patient:organization")], [], BulkDeleteMode.SoftDelete, false, "_include:iterate&x" },
        { null, [new("_type", "")], [], BulkDeleteMode.SoftDelete, false, "'_type' have no value" },
        { "Patient", [new("name", "")], [], BulkDeleteMode.SoftDelete, false, "'name' have no value" },
        { "Patient", [new("identifier", "  ")], [], BulkDeleteMode.SoftDelete, false, "'identifier' have no value" },
        { "Patient", [new("name", "smith"), new("name", null!)], [], BulkDeleteMode.SoftDelete, false, "'name' have no value" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task GivenAnInvalidRequest_WhenCreating_ThenItIsRejectedWithoutCreatingAJob(
        string? resourceType,
        KeyValuePair<string, string>[] parameters,
        string[] excluded,
        BulkDeleteMode mode,
        bool removeReferences,
        string expectedMessage)
    {
        var failure = await Should.ThrowAsync<BadRequestException>(() => CreateHandler().HandleAsync(
            Command(resourceType, parameters, mode, excluded, removeReferences), CancellationToken.None));

        failure.Message.ShouldContain(expectedMessage);
        (await _jobs.ListAsync(cancellationToken: CancellationToken.None)).ShouldBeEmpty();
        _started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BulkDeleteMode.HardDelete)]
    [InlineData(BulkDeleteMode.PurgeHistory)]
    public async Task GivenStorageWithoutPhysicalDeletion_WhenCreatingAPhysicalDelete_ThenTheRequestIsRejected(BulkDeleteMode mode)
    {
        _repository.SupportsPhysicalDeletion.Returns(false);

        var failure = await Should.ThrowAsync<BadRequestException>(() =>
            CreateHandler().HandleAsync(Command("Patient", [], mode), CancellationToken.None));

        failure.Message.ShouldContain("not supported");
        _started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("_include", "Patient:organization")]
    [InlineData("_revinclude", "Observation:subject")]
    [InlineData("_include:iterate", "Patient:organization")]
    public async Task GivenStorageThatCannotEvaluateIncludes_WhenCreatingWithAnIncludeCascade_ThenTheRequestIsRejected(
        string name, string value)
    {
        _repository.SupportsPhysicalDeletion.Returns(false);

        var failure = await Should.ThrowAsync<BadRequestException>(() =>
            CreateHandler().HandleAsync(Command("Patient", [new(name, value)]), CancellationToken.None));

        failure.Message.ShouldContain(name);
        failure.Message.ShouldContain("not supported");
        _started.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenStorageWithoutPhysicalDeletion_WhenCreatingASoftDelete_ThenTheJobStarts()
    {
        _repository.SupportsPhysicalDeletion.Returns(false);

        await CreateHandler().HandleAsync(Command("Patient", []), CancellationToken.None);

        _started.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenTheOrchestrationCannotStart_WhenCreating_ThenTheJobIsFailedAndTheErrorPropagates()
    {
        var orchestrations = Substitute.For<IOrchestrationServiceClient>();
        orchestrations.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(Task.FromException(new IOException("task hub unavailable")));
        orchestrations.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>())
            .Returns(Task.FromException(new IOException("task hub unavailable")));

        await Should.ThrowAsync<IOException>(() =>
            CreateHandler(client: orchestrations).HandleAsync(Command("Patient", []), CancellationToken.None));

        var job = (await _jobs.ListAsync(cancellationToken: CancellationToken.None)).ShouldHaveSingleItem();
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldContain("task hub unavailable");
        job.Result!["issues"]!.AsArray().ShouldHaveSingleItem()!.GetValue<string>().ShouldContain("task hub unavailable");
    }

    private CreateBulkDeleteJobHandler CreateHandler(int batchSize = 500, IOrchestrationServiceClient? client = null)
    {
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(TenantId, Arg.Any<CancellationToken>()).Returns(_repository);
        return new CreateBulkDeleteJobHandler(new TaskHubClient(client ?? _orchestrations), _jobs, _tenants, _versions,
            repositories, _builders, Options.Create(new BulkDeleteOptions { BatchSize = batchSize }),
            NullLogger<CreateBulkDeleteJobHandler>.Instance);
    }

    private static CreateBulkDeleteJobCommand Command(
        string? resourceType,
        KeyValuePair<string, string>[] parameters,
        BulkDeleteMode mode = BulkDeleteMode.SoftDelete,
        string[]? excluded = null,
        bool removeReferences = false) =>
        new(TenantId, resourceType, parameters, mode, excluded ?? [], removeReferences, "https://fhir/$bulk-delete");

    public void Dispose()
    {
        _builders.Dispose();
        _versions.Dispose();
    }
}
