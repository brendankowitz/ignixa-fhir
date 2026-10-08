using Ignixa.Abstractions;
using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Application.Events.Package;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Services;

public class ConformanceCacheRefresherTests
{
    [Fact]
    public async Task GivenRecoverableTenantConfigurationFailure_WhenRefreshed_ThenItThrowsTheDedicatedRefreshException()
    {
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(CancellationToken.None)
            .Returns(ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(
                new IOException("Configuration store unavailable.")));
        var refresher = new ConformanceCacheRefresher(null!, null!, tenantStore, null!);
        using var state = new ConformanceState();

        var exception = await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.BuildSnapshotAsync(state.CreateSnapshot(), 0, CancellationToken.None));

        exception.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task GivenCancellation_WhenRefreshed_ThenItPropagatesTheCancellation()
    {
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(CancellationToken.None)
            .Returns(ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(
                new OperationCanceledException("Independent cancellation.")));
        var refresher = new ConformanceCacheRefresher(null!, null!, tenantStore, null!);
        using var state = new ConformanceState();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            refresher.BuildSnapshotAsync(state.CreateSnapshot(), 0, CancellationToken.None));
    }

    [Fact]
    public async Task GivenPublishedSchemaProvider_WhenFutureSnapshotBuildsOffLock_ThenTheBuildDoesNotInvalidateCapabilityCaches()
    {
        var tenant = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "File tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(CancellationToken.None)
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>([tenant]));
        var definitions = Substitute.For<ISearchParameterDefinitionManager>();
        definitions.AllSearchParameters.Returns([]);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.CreateConformanceDefinitionsSnapshot(
                FhirVersion.R4,
                tenant.TenantId,
                Arg.Any<ConformanceStateSnapshot>(),
                29)
            .Returns(new ConformanceDefinitionsSnapshot(
                definitions,
                definitions,
                new DefinitionsHandle(
                    Substitute.For<ISearchIndexer>(),
                    Substitute.For<IFhirSchemaProvider>(),
                    29)));
        var capabilityCache = Substitute.For<ICapabilityCacheInvalidator>();
        var refresher = new ConformanceCacheRefresher(
            versions,
            null!,
            tenantStore,
            capabilityCache);
        using var state = new ConformanceState();

        var snapshot = await refresher.BuildSnapshotAsync(state.CreateSnapshot(), 29, CancellationToken.None);

        snapshot.Generation.ShouldBe(29);
        versions.Received(1).CreateConformanceDefinitionsSnapshot(
            FhirVersion.R4,
            tenant.TenantId,
            Arg.Any<ConformanceStateSnapshot>(),
            29);
        await capabilityCache.DidNotReceiveWithAnyArgs()
            .InvalidateForTenantAsync(default, default);
    }

    [Fact]
    public async Task GivenMultiTenantSnapshot_WhenPackageIsUnloaded_ThenItInvalidatesAllPublishedTenantCaches()
    {
        var tenants = new List<TenantConfiguration>
        {
            CreateFileSystemTenant(1),
            CreateFileSystemTenant(2),
        };
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetAllTenantsAsync(CancellationToken.None)
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(tenants));
        var definitions = Substitute.For<ISearchParameterDefinitionManager>();
        definitions.AllSearchParameters.Returns([]);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.CreateConformanceDefinitionsSnapshot(
                Arg.Any<FhirVersion>(),
                Arg.Any<int>(),
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>())
            .Returns(call => new ConformanceDefinitionsSnapshot(
                definitions,
                definitions,
                new DefinitionsHandle(
                    Substitute.For<ISearchIndexer>(),
                    Substitute.For<IFhirSchemaProvider>(),
                    call.ArgAt<long>(3))));
        var capabilityCache = Substitute.For<ICapabilityCacheInvalidator>();
        var refresher = new ConformanceCacheRefresher(
            versions,
            null!,
            tenantStore,
            capabilityCache);
        using var state = new ConformanceState();
        using var publisher = new ConformanceRefreshPublisher(
            state,
            refresher,
            NullLogger<ConformanceRefreshPublisher>.Instance);
        var handler = new PackageUnloadedNotificationHandler(
            publisher,
            NullLogger<PackageUnloadedNotificationHandler>.Instance);

        await handler.HandleAsync(
            new PackageUnloadedEvent("example.package", "1.0.0", 1, DateTimeOffset.UtcNow),
            CancellationToken.None);

        await capabilityCache.Received(1).InvalidateForTenantAsync(1, CancellationToken.None);
        await capabilityCache.Received(1).InvalidateForTenantAsync(2, CancellationToken.None);
    }

    private static TenantConfiguration CreateFileSystemTenant(int tenantId) => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = "4.0",
        Storage = new TenantStorageConfiguration { Type = "FileSystem" },
    };
}
