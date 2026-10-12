using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Tests.Features.Conformance;
using Ignixa.Domain.Models;
using Ignixa.Conformance.Events.Abstractions;
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
            TestPackages.SearchParameter(PackageId, TestPackages.Canonical("mixed"), baseTypes: ["Patient", "Practitioner"]),
            new InMemoryEventLog());

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
        var log = new InMemoryEventLog();
        foreach (var index in Enumerable.Range(1, 5))
        {
            log.OtherWriters.Enqueue(ConformanceEvents.PackageActivated($"other-{index}"));
        }

        var handler = CreateHandler(Substitute.For<IMediator>(), TestPackages.Custom(PackageId), log);

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
        var handler = CreateHandler(mediator, TestPackages.Shadow(PackageId), new InMemoryEventLog());

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

    private static LoadPackageHandler CreateHandler(IMediator mediator, PackageResource parameter, ISourceEventStore eventStore)
    {
        var provider = Substitute.For<IImplementationGuideProvider>();
        provider.LoadPackageAsync("1", PackageId, Version, Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = PackageId, PackageVersion = Version, ImportedResources = 1 });
        return new LoadPackageHandler(
            provider,
            mediator,
            TestPackageActivationPipeline.Create(TestPackages.Repository(parameter), eventStore, new ConformanceState()),
            NullLogger<LoadPackageHandler>.Instance);
    }
}
