// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Admin;

public sealed class LoadPackageHandlerTests
{
    private readonly IImplementationGuideProvider _provider = Substitute.For<IImplementationGuideProvider>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IPackageActivationPipeline _activation = Substitute.For<IPackageActivationPipeline>();
    private readonly LoadPackageHandler _handler;

    public LoadPackageHandlerTests()
    {
        _handler = new LoadPackageHandler(
            _provider,
            _mediator,
            _activation,
            NullLogger<LoadPackageHandler>.Instance);
    }

    [Fact]
    public async Task GivenActivationRejectsPackage_WhenLoading_ThenActivationFailureIsReportedAndResultIsIncomplete()
    {
        // Arrange
        _provider.LoadPackageAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.0.0",
                ImportedResources = 2,
            });
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Failed([new ValidationIssue("conflict", "SearchParameter clash")]));

        // Act
        var result = await _handler.HandleAsync(
            new LoadPackageCommand("1", "example.ig", "1.0.0"),
            CancellationToken.None);

        // Assert
        result.Activation.ShouldNotBeNull().Success.ShouldBeFalse();
        result.Activation.Issues.ShouldHaveSingleItem().Code.ShouldBe("conflict");
        result.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenClosureWithBlockingMember_WhenLoadingWithDependencies_ThenMembersArePropagatedAndResultIsIncomplete()
    {
        // Arrange
        var failed = new PackageClosureMember(
            "broken",
            "2.0.0",
            PackageClosureMemberStatus.LoadFailed,
            "tarball missing");
        _provider.LoadPackageWithDependenciesAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.0.0",
                ClosureMembers = [new("example.ig", "1.0.0", PackageClosureMemberStatus.Imported), failed],
            });
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Succeeded());

        // Act
        var result = await _handler.HandleAsync(
            new LoadPackageCommand("1", "example.ig", "1.0.0", IncludeDependencies: true),
            CancellationToken.None);

        // Assert
        result.ClosureMembers.ShouldContain(failed);
        result.IsComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenOnlyInformationalMembersAndActivationSucceeds_WhenLoadingWithDependencies_ThenResultIsComplete()
    {
        // Arrange
        _provider.LoadPackageWithDependenciesAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.0.0",
                ClosureMembers =
                [
                    new("example.ig", "1.0.0", PackageClosureMemberStatus.Imported),
                    new("hl7.fhir.r4.core", "4.0.1", PackageClosureMemberStatus.ProvidedInProcess),
                ],
            });
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Succeeded(["Patient"]));

        // Act
        var result = await _handler.HandleAsync(
            new LoadPackageCommand("1", "example.ig", "1.0.0", IncludeDependencies: true),
            CancellationToken.None);

        // Assert
        result.IsComplete.ShouldBeTrue();
        result.Activation.ShouldNotBeNull().PendingReindex.ShouldBe(["Patient"]);
    }
}
