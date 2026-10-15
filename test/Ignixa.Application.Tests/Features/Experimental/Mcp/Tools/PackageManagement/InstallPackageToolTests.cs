using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Experimental.Mcp.Dtos;
using Ignixa.Application.Features.Experimental.Mcp.Tools.PackageManagement;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Tests.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp.Tools.PackageManagement;

/// <summary>Review 5 N2: the MCP install tool reports activation, not just storage, as its outcome.</summary>
public sealed class InstallPackageToolTests : IDisposable
{
    private const string PackageId = "test.package";
    private const string Key = $"{PackageId}@{TestPackages.PackageVersion}";
    private readonly ConformanceState _state = new();

    [Fact]
    public async Task GivenStoredPackageIsRejectedByActivation_WhenInstalled_ThenTheToolReturnsAFailureNamingTheIssue()
    {
        var result = await InstallAsync(TestPackages.SearchParameter(PackageId, TestPackages.Canonical("mixed"), baseTypes: ["Patient", "Practitioner"]));

        result.Success.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem().ShouldStartWith("error SP_MIXED_BASE_SHADOW:");
        result.Message.ShouldContain("did not activate");
        result.Message.ShouldContain("SP_MIXED_BASE_SHADOW");
        result.PendingReindex.ShouldBeEmpty();
        _state.Packages.ShouldNotContainKey(Key);
    }

    [Fact]
    public async Task GivenActivatablePackage_WhenInstalled_ThenTheToolReportsSuccessWithItsPendingReindex()
    {
        var result = await InstallAsync(TestPackages.Custom(PackageId));

        result.Success.ShouldBeTrue(string.Join("; ", result.Issues));
        result.PendingReindex.ShouldBe(["Patient"]);
        _state.Packages.ShouldContainKey(Key);
    }

    public void Dispose() => _state.Dispose();

    private Task<InstallPackageResultDto> InstallAsync(PackageResource parameter)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(new TenantConfiguration { TenantId = 1, DisplayName = "One", FhirVersion = "4.0" }));
        var tool = new InstallPackageTool(
            Substitute.For<IFhirRequestContextAccessor>(),
            tenants,
            TestPackages.Provider(PackageId),
            Substitute.For<INpmPackageSearchService>(),
            TestPackageActivationPipeline.Create(TestPackages.Repository(parameter), new InMemoryEventLog(), _state));
        return tool.InstallPackageAsync(PackageId, TestPackages.PackageVersion, tenantId: 1, CancellationToken.None);
    }
}
