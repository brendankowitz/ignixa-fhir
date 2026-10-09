using System.Text.Json.Nodes;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Admin;

public class LoadPackageHandlerTests
{
    private const string PackageId = "test.package";
    private const string Version = "1.0.0";

    [Fact]
    public async Task GivenStoredPackageIsRejectedByActivation_WhenLoaded_ThenTheLoadFailsWithEveryIssueCodeAndReason()
    {
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(
            mediator,
            CreateParameter("http://example.org/SearchParameter/mixed", "identifier", ["Patient", "Practitioner"]),
            AppendingStore());

        var exception = await Should.ThrowAsync<PackageActivationRejectedException>(() =>
            handler.HandleAsync(new LoadPackageCommand("1", PackageId, Version), CancellationToken.None));

        exception.StatusCode.ShouldBe(422);
        var issue = exception.OperationOutcome.Issue.ShouldHaveSingleItem();
        issue.SeverityCode.ShouldBe(OperationOutcomeIssue.IssueSeverityCode.Error);
        issue.IssueTypeCode.ShouldBe(OperationOutcomeIssue.IssueTypeCommon.BusinessRule);
        var coding = issue.Details!.Coding.ShouldHaveSingleItem();
        coding.System.ShouldBe(PackageActivationRejectedException.IssueCodeSystem);
        coding.Code.ShouldBe("SP_MIXED_BASE_SHADOW");
        issue.Diagnostics.ShouldContain("resolves to different storage roots");
        await mediator.Received(1).PublishAsync(
            Arg.Is<PackageLoadedEvent>(loaded => loaded.PackageId == PackageId && loaded.RequiresConformanceRefresh),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenActivationLosesEveryAppendRace_WhenLoaded_ThenTheLoadFailsAsARetryableConflict()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SourceEvent>>(new SourceEventConcurrencyException(0, 1)));
        var handler = CreateHandler(
            Substitute.For<IMediator>(),
            CreateParameter("http://example.org/SearchParameter/custom", "custom", ["Patient"]),
            store);

        var exception = await Should.ThrowAsync<PackageActivationRejectedException>(() =>
            handler.HandleAsync(new LoadPackageCommand("1", PackageId, Version), CancellationToken.None));

        exception.StatusCode.ShouldBe(409);
        exception.ActivationIssues.ShouldHaveSingleItem().Code.ShouldBe("CONFORMANCE_CONFLICT");
        exception.OperationOutcome.Issue.ShouldHaveSingleItem().IssueTypeCode
            .ShouldBe(OperationOutcomeIssue.IssueTypeCommon.Conflict);
    }

    [Fact]
    public async Task GivenShadowingPackage_WhenLoaded_ThenTheResultCarriesTheHiddenCodesAsAWarning()
    {
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(
            mediator,
            CreateParameter(
                "http://example.org/SearchParameter/Patient-identifier",
                "identifier",
                ["Patient"],
                derivedFrom: "http://hl7.org/fhir/SearchParameter/Patient-identifier"),
            AppendingStore());

        var result = await handler.HandleAsync(new LoadPackageCommand("1", PackageId, Version), CancellationToken.None);

        var hidden = result.Issues.ShouldHaveSingleItem();
        hidden.Code.ShouldBe(PackageActivationPipeline.TransitionPendingCode);
        hidden.Severity.ShouldBe(ActivationIssueSeverity.Warning);
        hidden.Message.ShouldContain("Patient.identifier");
        result.PendingReindex.ShouldBeEmpty();
        await mediator.Received(1).PublishAsync(
            Arg.Is<PackageLoadedEvent>(loaded => !loaded.RequiresConformanceRefresh),
            Arg.Any<CancellationToken>());
    }

    private static LoadPackageHandler CreateHandler(
        IMediator mediator,
        PackageResource parameter,
        ISourceEventStore eventStore)
    {
        var provider = Substitute.For<IImplementationGuideProvider>();
        provider.LoadPackageAsync("1", PackageId, Version, Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = PackageId, PackageVersion = Version, ImportedResources = 1 });
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetResourcesForActivationAsync(PackageId, Version, Arg.Any<CancellationToken>())
            .Returns([parameter]);
        var state = new ConformanceState();
        return new LoadPackageHandler(
            provider,
            mediator,
            TestPackageActivationPipeline.Create(repository, eventStore, state),
            NullLogger<LoadPackageHandler>.Instance);
    }

    private static ISourceEventStore AppendingStore()
    {
        var store = Substitute.For<ISourceEventStore>();
        var nextEventId = 0L;
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<SourceEvent>>(call.Arg<IEnumerable<NewSourceEvent>>()
                .Select(sourceEvent => new SourceEvent(
                    ++nextEventId,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray()));
        return store;
    }

    private static PackageResource CreateParameter(
        string canonical,
        string code,
        string[] baseTypes,
        string? derivedFrom = null)
    {
        var resource = new JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = code,
            ["url"] = canonical,
            ["code"] = code,
            ["base"] = new JsonArray(baseTypes.Select(type => JsonValue.Create(type)).ToArray<JsonNode?>()),
            ["type"] = "token",
            ["expression"] = "Resource.identifier",
        };
        if (derivedFrom is not null)
        {
            resource["derivedFrom"] = derivedFrom;
        }

        return new PackageResource
        {
            PackageId = PackageId,
            PackageVersion = Version,
            ResourceType = "SearchParameter",
            ResourceId = code,
            Canonical = canonical,
            FhirVersion = "4.0.1",
            ResourceJson = resource.ToJsonString(),
        };
    }
}
