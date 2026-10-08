using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Reindex;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Reindex;

public sealed class ReindexAvailabilityServiceTests
{
    [Fact]
    public async Task GivenMixedProviderServer_WhenCheckingAvailability_ThenReturnsUnsupportedForEveryTenant()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Tenant(0),
                Tenant(1),
                Tenant(2)
            ]);
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IFhirRepository, IReindexStore>());
        repositories.GetRepositoryAsync(2, Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IFhirRepository>());
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            repositories);

        var availability = await service.GetAvailabilityAsync(CancellationToken.None);

        availability.Status.ShouldBe(ReindexAvailabilityStatus.Unsupported);
        availability.UnsupportedTenantId.ShouldBe(2);
        await repositories.DidNotReceive().GetRepositoryAsync(
            0,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenDisabledReindex_WhenCheckingAvailability_ThenDoesNotInspectTenantProviders()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = false }),
            tenants,
            repositories);

        var availability = await service.GetAvailabilityAsync(CancellationToken.None);

        availability.ShouldBe(ReindexAvailability.Disabled);
        _ = await tenants.DidNotReceive().GetAllTenantsAsync(Arg.Any<CancellationToken>());
    }

    private static TenantConfiguration Tenant(int tenantId) =>
        new()
        {
            TenantId = tenantId,
            DisplayName = $"Tenant {tenantId}",
            FhirVersion = "4.0",
            IsSystemPartition = tenantId == 0
        };
}
