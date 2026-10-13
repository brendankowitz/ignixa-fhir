using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Infrastructure;

public sealed class CompositeRepositoryFactoryTests
{
    public static TheoryData<string, bool> RecognizedStorageTypes => new()
    {
        { "SqlServer", true },
        { "SqlEntityFramework", true },
        { "FileSystem", false }
    };

    // The system partition and inactive tenants never block reindex; otherwise the lowest unsupported id is reported.
    public static TheoryData<TenantConfiguration[], int?> TenantSets => new()
    {
        { [Tenant(1, "SqlServer")], null },
        { [Tenant(1, "SqlEntityFramework")], null },
        { [Tenant(1, "FileSystem")], 1 },
        { [Tenant(0), Tenant(1, "SqlServer"), Tenant(3, "FileSystem"), Tenant(2, "FileSystem")], 2 },
        { [Tenant(0), Tenant(1, "SqlServer"), Tenant(2, "FileSystem") with { IsActive = false }], null },
    };

    [Theory]
    [MemberData(nameof(TenantSets))]
    public async Task GivenTenantStorage_WhenFindingTenantWithoutReindexSupport_ThenItReturnsTheFirstUnsupportedTenantWithoutCreatingRepositories(
        TenantConfiguration[] tenants,
        int? expectedTenantId)
    {
        var fileSystemFactory = Substitute.For<IFhirRepositoryFactory>();
        var sqlServerFactory = Substitute.For<IFhirRepositoryFactory>();
        var sqlStores = Substitute.For<IReindexStoreFactory>();
        var factory = new CompositeRepositoryFactory(TenantStore(tenants), fileSystemFactory, sqlServerFactory, sqlStores);

        (await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None)).ShouldBe(expectedTenantId);

        await fileSystemFactory.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default, default);
        await sqlServerFactory.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default, default);
        await sqlStores.DidNotReceiveWithAnyArgs().GetReindexStoreAsync(default, default);
    }

    [Fact]
    public async Task GivenUnknownStorageTypeAfterAnUnsupportedTenant_WhenFindingTenantWithoutReindexSupport_ThenThrowsConfigurationError()
    {
        var factory = CreateFactory(TenantStore(Tenant(1, "FileSystem"), Tenant(2, "TypoSqlServer")));

        var exception = await Should.ThrowAsync<NotSupportedException>(() => factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None));

        exception.Message.ShouldBe("Storage type 'TypoSqlServer' is not supported");
    }

    [Theory]
    [MemberData(nameof(RecognizedStorageTypes))]
    public async Task GivenRecognizedStorageType_WhenGettingReindexStore_ThenOnlySqlTenantsGetTheSqlStore(
        string storageType,
        bool supportsReindex)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(Tenant(1, storageType));
        var sqlStore = Substitute.For<IReindexStore>();
        var sqlStores = Substitute.For<IReindexStoreFactory>();
        sqlStores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(sqlStore);
        var factory = CreateFactory(tenants, sqlStores);

        if (supportsReindex)
        {
            (await factory.GetReindexStoreAsync(1, CancellationToken.None)).ShouldBeSameAs(sqlStore);
        }
        else
        {
            await Should.ThrowAsync<NotSupportedException>(() => factory.GetReindexStoreAsync(1, CancellationToken.None));
        }
    }

    private static TenantConfiguration Tenant(int tenantId, string storageType = "FileSystem") => new()
    {
        TenantId = tenantId,
        DisplayName = $"Tenant {tenantId}",
        FhirVersion = "4.0",
        IsSystemPartition = tenantId == 0,
        Storage = new TenantStorageConfiguration { Type = storageType }
    };

    private static ITenantConfigurationStore TenantStore(params TenantConfiguration[] tenants) => TestConformanceRefresher.Tenants(tenants);

    private static CompositeRepositoryFactory CreateFactory(ITenantConfigurationStore tenants, IReindexStoreFactory? sqlReindexStoreFactory = null) =>
        new(tenants, Substitute.For<IFhirRepositoryFactory>(), Substitute.For<IFhirRepositoryFactory>(), sqlReindexStoreFactory ?? Substitute.For<IReindexStoreFactory>());
}
