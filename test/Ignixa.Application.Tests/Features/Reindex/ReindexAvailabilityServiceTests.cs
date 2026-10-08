using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Reindex;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Reindex;

public sealed class ReindexAvailabilityServiceTests
{
    [Fact]
    public async Task GivenMixedProviderServer_WhenCheckingAvailability_ThenReturnsUnsupportedWithoutCreatingRepositories()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Tenant(0),
                Tenant(1, "SqlServer"),
                Tenant(2, "FileSystem")
            ]);
        var fileSystemFactory = Substitute.For<IFhirRepositoryFactory>();
        var sqlServerFactory = Substitute.For<IFhirRepositoryFactory>();
        var capabilities = new CompositeRepositoryFactory(
            tenants,
            fileSystemFactory,
            sqlServerFactory);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            capabilities);

        var availability = await service.GetAvailabilityAsync(CancellationToken.None);

        availability.Status.ShouldBe(ReindexAvailabilityStatus.Unsupported);
        availability.UnsupportedTenantId.ShouldBe(2);
        await fileSystemFactory.DidNotReceive().GetRepositoryAsync(
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await sqlServerFactory.DidNotReceive().GetRepositoryAsync(
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenDisabledReindex_WhenCheckingAvailability_ThenDoesNotInspectTenantProviders()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        var capabilities = Substitute.For<IReindexProviderCapabilities>();
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = false }),
            tenants,
            capabilities);

        var availability = await service.GetAvailabilityAsync(CancellationToken.None);

        availability.ShouldBe(ReindexAvailability.Disabled);
        _ = await tenants.DidNotReceive().GetAllTenantsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenConcurrentFirstCalls_WhenCheckingAvailability_ThenRunsOneProbe()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        var probe = new TaskCompletionSource<IReadOnlyList<TenantConfiguration>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<TenantConfiguration>>(probe.Task));
        var capabilities = Substitute.For<IReindexProviderCapabilities>();
        capabilities.SupportsReindex(Arg.Any<TenantConfiguration>()).Returns(true);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            capabilities);

        var first = service.GetAvailabilityAsync(CancellationToken.None);
        var second = service.GetAvailabilityAsync(CancellationToken.None);

        _ = await tenants.Received(1).GetAllTenantsAsync(Arg.Any<CancellationToken>());
        probe.SetResult([Tenant(1)]);

        (await first).ShouldBe(ReindexAvailability.Available);
        (await second).ShouldBe(ReindexAvailability.Available);
    }

    [Fact]
    public async Task GivenProbeFailure_WhenCheckingAvailabilityAgain_ThenRetriesTheProbe()
    {
        var attempts = 0;
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempts++;
                return attempts == 1
                    ? ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(
                        new InvalidOperationException("tenant configuration is unavailable"))
                    : ValueTask.FromResult<IReadOnlyList<TenantConfiguration>>([Tenant(1)]);
            });
        var capabilities = Substitute.For<IReindexProviderCapabilities>();
        capabilities.SupportsReindex(Arg.Any<TenantConfiguration>()).Returns(true);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            capabilities);

        Func<Task> first = () => service.GetAvailabilityAsync(CancellationToken.None);

        await first.ShouldThrowAsync<InvalidOperationException>();
        (await service.GetAvailabilityAsync(CancellationToken.None)).ShouldBe(ReindexAvailability.Available);
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task GivenSuccessfulProbe_WhenCheckingAvailabilityAgain_ThenDoesNotRepeatTenantWork()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns([Tenant(1)]);
        var capabilities = Substitute.For<IReindexProviderCapabilities>();
        capabilities.SupportsReindex(Arg.Any<TenantConfiguration>()).Returns(true);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            capabilities);

        (await service.GetAvailabilityAsync(CancellationToken.None)).ShouldBe(ReindexAvailability.Available);
        tenants.ClearReceivedCalls();
        capabilities.ClearReceivedCalls();

        (await service.GetAvailabilityAsync(CancellationToken.None)).ShouldBe(ReindexAvailability.Available);

        _ = await tenants.DidNotReceive().GetAllTenantsAsync(Arg.Any<CancellationToken>());
        capabilities.DidNotReceive().SupportsReindex(Arg.Any<TenantConfiguration>());
    }

    private static TenantConfiguration Tenant(int tenantId, string storageType = "FileSystem") =>
        new()
        {
            TenantId = tenantId,
            DisplayName = $"Tenant {tenantId}",
            FhirVersion = "4.0",
            IsSystemPartition = tenantId == 0,
            Storage = new TenantStorageConfiguration { Type = storageType }
        };
}
