using Ignixa.Abstractions;
using Ignixa.Api.Events;
using Ignixa.Application.Events.Package;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Api.Tests.Events;

public class FileSystemPackageSynchronizationTests
{
    [Fact]
    public async Task GivenFileSystemTenantAndSharedSqlContent_WhenPackageLoads_ThenCapabilitiesAreInvalidatedWithoutSqlCatalogAccess()
    {
        var contentStorage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = "unused" };
        TenantConfiguration[] tenants =
        [
            new() { TenantId = 0, DisplayName = "System", FhirVersion = "4.0", Storage = contentStorage },
            new() { TenantId = 1, DisplayName = "Packages", FhirVersion = "4.0", Storage = contentStorage },
            new() { TenantId = 2, DisplayName = "Files", FhirVersion = "4.0", Storage = new() { Type = "FileSystem" } },
        ];
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<TenantConfiguration?>(tenants.Single(t => t.TenantId == call.Arg<int>())));
        var sql = new SqlExecutionService(
            store,
            new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            NullLogger<SqlExecutionService>.Instance);
        using var registry = new SqlServerSearchIndexCacheRegistry(sql, NullLoggerFactory.Instance);
        var manager = Substitute.For<ISearchParameterDefinitionManager>();
        manager.AllSearchParameters.Returns([]);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSearchParameterDefinitionManager(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(manager);
        var invalidator = Substitute.For<ICapabilityCacheInvalidator>();
        var handler = new PackageLoadedSearchParameterSyncHandler(
            versions, registry, store, invalidator, NullLogger<PackageLoadedSearchParameterSyncHandler>.Instance);

        await handler.HandleAsync(new PackageLoadedEvent("filesystem.package", "1.0.0", 2, DateTimeOffset.UnixEpoch), CancellationToken.None);

        await invalidator.Received(1).InvalidateForTenantAsync(2, CancellationToken.None);
    }
}
