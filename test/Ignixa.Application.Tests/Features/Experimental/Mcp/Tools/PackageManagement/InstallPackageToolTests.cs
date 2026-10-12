using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Experimental.Mcp.Tools.PackageManagement;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Tests.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp.Tools.PackageManagement;

public class InstallPackageToolTests
{
    private const string PackageId = "test.package";
    private const string Version = "1.0.0";

    [Fact]
    public async Task GivenStoredPackageIsRejectedByActivation_WhenInstalled_ThenTheToolReturnsAFailureNamingTheIssue()
    {
        using var state = new ConformanceState();
        var tool = CreateTool(state, TestPackages.SearchParameter(PackageId, TestPackages.Canonical("mixed"), baseTypes: ["Patient", "Practitioner"]));

        var result = await tool.InstallPackageAsync(PackageId, Version, tenantId: 1, CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Issues.ShouldHaveSingleItem().ShouldStartWith("error SP_MIXED_BASE_SHADOW:");
        result.Message.ShouldContain("did not activate");
        result.Message.ShouldContain("SP_MIXED_BASE_SHADOW");
        result.PendingReindex.ShouldBeEmpty();
        state.Packages.ShouldNotContainKey($"{PackageId}@{Version}");
    }

    [Fact]
    public async Task GivenActivatablePackage_WhenInstalled_ThenTheToolReportsSuccessWithItsPendingReindex()
    {
        using var state = new ConformanceState();
        var tool = CreateTool(state, TestPackages.Custom(PackageId));

        var result = await tool.InstallPackageAsync(PackageId, Version, tenantId: 1, CancellationToken.None);

        result.Success.ShouldBeTrue(string.Join("; ", result.Issues));
        result.PendingReindex.ShouldBe(["Patient"]);
        state.Packages.ShouldContainKey($"{PackageId}@{Version}");
    }

    private static InstallPackageTool CreateTool(ConformanceState state, PackageResource parameter)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(
                new TenantConfiguration { TenantId = 1, DisplayName = "One", FhirVersion = "4.0" }));
        var provider = Substitute.For<IImplementationGuideProvider>();
        provider.LoadPackageAsync("1", PackageId, Version, Arg.Any<CancellationToken>())
            .Returns(new PackageImportResult { PackageId = PackageId, PackageVersion = Version, ImportedResources = 1 });
        return new InstallPackageTool(
            Substitute.For<IFhirRequestContextAccessor>(),
            tenants,
            provider,
            Substitute.For<INpmPackageSearchService>(),
            TestPackageActivationPipeline.Create(TestPackages.Repository(parameter), new InMemoryEventLog(), state));
    }
}
