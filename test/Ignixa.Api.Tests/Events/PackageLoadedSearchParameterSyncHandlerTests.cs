using Ignixa.Api.Events;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Events;

/// <summary>
/// Pins the one behaviour that differs deliberately from the EF handler this replaces: a failed sync is
/// surfaced, not swallowed.
/// <para>
/// The EF version caught everything and logged, justified by "the parameters will be loaded lazily on first
/// search". There is no lazy load on the SqlServer write path — row generators read the cache dictionary
/// directly and skip a row for any parameter missing from it — so swallowing turns a transient sync failure
/// into resources that are permanently unfindable by that parameter, while the package reports loaded.
/// </para>
/// </summary>
public class PackageLoadedSearchParameterSyncHandlerTests
{
    [Fact]
    public async Task GivenTheSyncFails_WhenThePackageLoadedEventIsHandled_ThenTheFailureIsSurfacedRatherThanSwallowed()
    {
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Test",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = "unused" },
            }));

        // A hand-written stub rather than a substitute: ExecuteReaderAsync is generic, and configuring
        // NSubstitute for one instantiation silently leaves every other one returning empty — which is
        // exactly how the first version of this test passed while the handler swallowed nothing at all.
        using var registry = new SqlServerSearchIndexCacheRegistry(
            new ThrowingSqlExecutionService(), NullLoggerFactory.Instance);

        var handler = new PackageLoadedSearchParameterSyncHandler(
            Substitute.For<IFhirVersionContext>(),
            registry,
            tenantStore,
            Substitute.For<ICapabilityCacheInvalidator>(),
            NullLogger<PackageLoadedSearchParameterSyncHandler>.Instance);

        await Should.ThrowAsync<Exception>(() => handler.HandleAsync(
            new PackageLoadedEvent("hl7.fhir.us.core", "6.1.0", 1, DateTimeOffset.UnixEpoch), CancellationToken.None));
    }

    [Fact]
    public async Task GivenTheTenantDoesNotExist_WhenThePackageLoadedEventIsHandled_ThenItReturnsWithoutThrowing()
    {
        // An unknown tenant is not a sync failure -- there is nothing to sync and nothing to lose, so this
        // path stays a warning-and-return exactly as it was.
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        tenantStore.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>((TenantConfiguration?)null));

        using var registry = new SqlServerSearchIndexCacheRegistry(
            new ThrowingSqlExecutionService(), NullLoggerFactory.Instance);

        var capabilityInvalidator = Substitute.For<ICapabilityCacheInvalidator>();

        var handler = new PackageLoadedSearchParameterSyncHandler(
            Substitute.For<IFhirVersionContext>(),
            registry,
            tenantStore,
            capabilityInvalidator,
            NullLogger<PackageLoadedSearchParameterSyncHandler>.Instance);

        await Should.NotThrowAsync(() => handler.HandleAsync(
            new PackageLoadedEvent("hl7.fhir.us.core", "6.1.0", 99, DateTimeOffset.UnixEpoch), CancellationToken.None));

        await capabilityInvalidator.DidNotReceive()
            .InvalidateForTenantAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
