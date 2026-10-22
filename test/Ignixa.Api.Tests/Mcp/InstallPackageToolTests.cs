// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Experimental.Mcp.Tools.PackageManagement;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Mcp;

public sealed class InstallPackageToolTests : McpTestBase
{
    private readonly IImplementationGuideProvider _provider = Substitute.For<IImplementationGuideProvider>();
    private readonly IPackageActivationPipeline _activation = Substitute.For<IPackageActivationPipeline>();
    private readonly InstallPackageTool _tool;

    public InstallPackageToolTests()
    {
        SetupTenant(1, "One");
        _provider.LoadPackageAsync("1", "example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.0.0",
                ImportedResources = 2,
            });
        _tool = new InstallPackageTool(
            HttpContextAccessor,
            TenantConfigurationStore,
            _provider,
            Substitute.For<INpmPackageSearchService>(),
            _activation,
            NullLogger<InstallPackageTool>.Instance);
    }

    [Fact]
    public async Task GivenActivationRejectsPackage_WhenInstalling_ThenResultIsUnsuccessfulAndListsIssues()
    {
        // Arrange
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Failed([new ValidationIssue("conflict", "SearchParameter clash")]));

        // Act
        var result = await _tool.InstallPackageAsync("example.ig", "1.0.0", 1, CancellationToken.None);

        // Assert
        result.Success.ShouldBeFalse();
        result.ActivationIssues.ShouldBe(["conflict: SearchParameter clash"]);
        result.Message.ShouldContain("activation failed");
    }

    [Fact]
    public async Task GivenActivationSucceedsWithPendingReindex_WhenInstalling_ThenResultIsSuccessfulAndReportsReindex()
    {
        // Arrange
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Succeeded(["Patient"]));

        // Act
        var result = await _tool.InstallPackageAsync("example.ig", "1.0.0", 1, CancellationToken.None);

        // Assert
        result.Success.ShouldBeTrue();
        result.PendingReindex.ShouldBe(["Patient"]);
    }

    [Fact]
    public async Task GivenActivationReportsConcurrencyException_WhenInstalling_ThenResultDoesNotExposeExceptionDetails()
    {
        // Arrange
        _activation.ActivateAsync("example.ig", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(ActivationResult.Failed(
            [
                new ValidationIssue(
                    "CONFORMANCE_CONFLICT",
                    "SqlException at C:\\packages\\tenant.db: token=do-not-return"),
            ]));

        // Act
        var result = await _tool.InstallPackageAsync("example.ig", "1.0.0", 1, CancellationToken.None);

        // Assert
        result.ActivationIssues.ShouldBe(["CONFORMANCE_CONFLICT: A concurrent conformance update prevented activation."]);
        result.Message.ShouldNotContain("token=do-not-return");
        result.Message.ShouldNotContain("C:\\packages");
    }

    [Fact]
    public async Task GivenActivationIsCancelled_WhenInstalling_ThenCancellationPropagates()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        _activation.ActivateAsync("example.ig", "1.0.0", cancellation.Token)
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            _tool.InstallPackageAsync("example.ig", "1.0.0", 1, cancellation.Token));
    }
}
