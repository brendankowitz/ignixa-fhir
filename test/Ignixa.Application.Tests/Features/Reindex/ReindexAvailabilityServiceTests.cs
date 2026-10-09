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
    public static TheoryData<string, bool> RecognizedStorageTypes => new()
    {
        { "SqlServer", true },
        { "SqlEntityFramework", true },
        { "FileSystem", false }
    };

    [Theory]
    [MemberData(nameof(RecognizedStorageTypes))]
    public void GivenRecognizedStorageType_WhenCheckingReindexSupport_ThenReturnsExpectedCapability(
        string storageType,
        bool expected)
    {
        var capabilities = CreateCompositeRepositoryFactory(
            Substitute.For<ITenantConfigurationStore>());

        capabilities.SupportsReindex(Tenant(1, storageType)).ShouldBe(expected);
    }

    [Fact]
    public void GivenUnknownStorageType_WhenCheckingReindexSupport_ThenThrowsConfigurationError()
    {
        var capabilities = CreateCompositeRepositoryFactory(
            Substitute.For<ITenantConfigurationStore>());

        var exception = Should.Throw<NotSupportedException>(
            () => capabilities.SupportsReindex(Tenant(1, "TypoSqlServer")));

        exception.Message.ShouldBe("Storage type 'TypoSqlServer' is not supported");
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("SqlEntityFramework")]
    public async Task GivenStorageTypeSupportingReindex_WhenCreatingRepository_ThenRepositoryImplementsReindexStore(
        string storageType)
    {
        var tenant = Tenant(1, storageType);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(tenant.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);
        var sqlRepository = Substitute.For<IFhirRepository, IReindexStore>();
        var sqlFactory = Substitute.For<IFhirRepositoryFactory>();
        sqlFactory.GetRepositoryAsync(tenant.TenantId, Arg.Any<CancellationToken>())
            .Returns(sqlRepository);
        var capabilities = CreateCompositeRepositoryFactory(
            tenants,
            sqlEfFactory: sqlFactory);

        capabilities.SupportsReindex(tenant).ShouldBeTrue();
        (await capabilities.GetRepositoryAsync(tenant.TenantId, CancellationToken.None))
            .ShouldBeAssignableTo<IReindexStore>();
    }

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
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = false }),
            tenants,
            CreateCompositeRepositoryFactory(tenants));

        var availability = await service.GetAvailabilityAsync(CancellationToken.None);

        availability.ShouldBe(ReindexAvailability.Disabled);
        _ = await tenants.DidNotReceive().GetAllTenantsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenConcurrentCalls_WhenCheckingAvailability_ThenEachCallProbesCurrentTenants()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        var probe = new TaskCompletionSource<IReadOnlyList<TenantConfiguration>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<IReadOnlyList<TenantConfiguration>>(probe.Task));
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            CreateCompositeRepositoryFactory(tenants));

        var first = service.GetAvailabilityAsync(CancellationToken.None);
        var second = service.GetAvailabilityAsync(CancellationToken.None);

        _ = await tenants.Received(2).GetAllTenantsAsync(Arg.Any<CancellationToken>());
        probe.SetResult([Tenant(1, "SqlServer")]);

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
                    : ValueTask.FromResult<IReadOnlyList<TenantConfiguration>>([Tenant(1, "SqlServer")]);
            });
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            CreateCompositeRepositoryFactory(tenants));

        Func<Task> first = () => service.GetAvailabilityAsync(CancellationToken.None);

        await first.ShouldThrowAsync<InvalidOperationException>();
        (await service.GetAvailabilityAsync(CancellationToken.None)).ShouldBe(ReindexAvailability.Available);
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task GivenUnknownStorageType_WhenCheckingAvailabilityAgain_ThenDoesNotCacheConfigurationError()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Tenant(1, "FileSystem"),
                Tenant(2, "TypoSqlServer")
            ]);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            CreateCompositeRepositoryFactory(tenants));

        Func<Task> first = () => service.GetAvailabilityAsync(CancellationToken.None);
        Func<Task> second = () => service.GetAvailabilityAsync(CancellationToken.None);

        (await first.ShouldThrowAsync<NotSupportedException>())
            .Message.ShouldBe("Storage type 'TypoSqlServer' is not supported");
        (await second.ShouldThrowAsync<NotSupportedException>())
            .Message.ShouldBe("Storage type 'TypoSqlServer' is not supported");
        _ = await tenants.Received(2).GetAllTenantsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenSuccessfulProbe_WhenCheckingAvailabilityAgain_ThenRepeatsTenantWork()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns([Tenant(1, "SqlServer")]);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            CreateCompositeRepositoryFactory(tenants));

        (await service.GetAvailabilityAsync(CancellationToken.None)).ShouldBe(ReindexAvailability.Available);
        tenants.ClearReceivedCalls();

        (await service.GetAvailabilityAsync(CancellationToken.None)).ShouldBe(ReindexAvailability.Available);

        _ = await tenants.Received(1).GetAllTenantsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTenantProvidersChange_WhenCheckingAvailabilityAgain_ThenAvailabilityIsRecomputed()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(
                [Tenant(1, "SqlServer")],
                [Tenant(1, "SqlServer"), Tenant(2, "FileSystem")]);
        var capabilities = CreateCompositeRepositoryFactory(tenants);
        var service = new ReindexAvailabilityService(
            Options.Create(new ReindexOptions { Enabled = true }),
            tenants,
            capabilities);

        (await service.GetAvailabilityAsync(CancellationToken.None))
            .ShouldBe(ReindexAvailability.Available);
        var changed = await service.GetAvailabilityAsync(CancellationToken.None);

        changed.Status.ShouldBe(ReindexAvailabilityStatus.Unsupported);
        changed.UnsupportedTenantId.ShouldBe(2);
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

    private static CompositeRepositoryFactory CreateCompositeRepositoryFactory(
        ITenantConfigurationStore tenants,
        IFhirRepositoryFactory? fileSystemFactory = null,
        IFhirRepositoryFactory? sqlEfFactory = null) =>
        new(
            tenants,
            fileSystemFactory ?? Substitute.For<IFhirRepositoryFactory>(),
            sqlEfFactory ?? Substitute.For<IFhirRepositoryFactory>());
}
