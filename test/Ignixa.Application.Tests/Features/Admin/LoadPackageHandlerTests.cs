using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Tests.Features.Conformance;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Models.OperationOutcomeIssue;

namespace Ignixa.Application.Tests.Features.Admin;

/// <summary>Review 5 N2: a package is stored before activation; a rejected activation fails the load, a durable one reports its issues.</summary>
public class LoadPackageHandlerTests
{
    private const string PackageId = "test.package";
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Fact]
    public async Task GivenStoredPackageIsRejectedByActivation_WhenLoaded_ThenTheLoadFailsWithEveryIssueCodeAndReason()
    {
        var mixed = TestPackages.SearchParameter(PackageId, TestPackages.Canonical("mixed"), baseTypes: ["Patient", "Practitioner"]);

        var exception = await Should.ThrowAsync<PackageActivationRejectedException>(() => LoadAsync(mixed));

        exception.StatusCode.ShouldBe(422);
        var issue = exception.OperationOutcome.Issue.ShouldHaveSingleItem();
        (issue.SeverityCode, issue.IssueTypeCode).ShouldBe((IssueSeverityCode.Error, IssueTypeCommon.BusinessRule));
        var coding = issue.Details!.Coding.ShouldHaveSingleItem();
        (coding.System, coding.Code).ShouldBe((PackageActivationRejectedException.IssueCodeSystem, "SP_MIXED_BASE_SHADOW"));
        issue.Diagnostics.ShouldContain("resolves to different storage roots");
        await _mediator.Received(1).PublishAsync(
            Arg.Is<PackageLoadedEvent>(loaded => loaded.PackageId == PackageId && loaded.RequiresConformanceRefresh), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenShadowingPackage_WhenLoaded_ThenTheResultCarriesTheHiddenCodesAsAWarning()
    {
        var result = await LoadAsync(TestPackages.Shadow(PackageId));

        var hidden = result.Issues.ShouldHaveSingleItem();
        (hidden.Code, hidden.Severity).ShouldBe((PackageActivationPipeline.TransitionPendingCode, ActivationIssueSeverity.Warning));
        hidden.Message.ShouldContain("Patient.identifier");
        result.PendingReindex.ShouldBeEmpty();
        await _mediator.Received(1).PublishAsync(Arg.Is<PackageLoadedEvent>(loaded => !loaded.RequiresConformanceRefresh), Arg.Any<CancellationToken>());
    }

    private Task<LoadPackageResult> LoadAsync(PackageResource parameter)
    {
        var handler = new LoadPackageHandler(
            TestPackages.Provider(PackageId),
            _mediator,
            TestPackageActivationPipeline.Create(TestPackages.Repository(parameter), new InMemoryEventLog(), new ConformanceState()),
            NullLogger<LoadPackageHandler>.Instance);
        return handler.HandleAsync(new LoadPackageCommand("1", PackageId, TestPackages.PackageVersion), CancellationToken.None);
    }
}
