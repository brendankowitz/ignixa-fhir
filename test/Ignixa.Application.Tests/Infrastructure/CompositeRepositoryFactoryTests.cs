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

    [Theory]
    [MemberData(nameof(RecognizedStorageTypes))]
    public async Task GivenRecognizedStorageType_WhenFindingTenantWithoutReindexSupport_ThenReturnsExpectedCapability(
        string storageType,
        bool supportsReindex)
    {
        var factory = CreateFactory(TenantStore(Tenant(1, storageType)));

        var unsupportedTenantId = await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None);

        unsupportedTenantId.ShouldBe(supportsReindex ? null : 1);
    }

    [Fact]
    public async Task GivenMixedProviderServer_WhenFindingTenantWithoutReindexSupport_ThenReturnsItWithoutCreatingRepositories()
    {
        var fileSystemFactory = Substitute.For<IFhirRepositoryFactory>();
        var sqlServerFactory = Substitute.For<IFhirRepositoryFactory>();
        var sqlStores = Substitute.For<IReindexStoreFactory>();
        var factory = new CompositeRepositoryFactory(
            TenantStore(Tenant(0), Tenant(1, "SqlServer"), Tenant(3, "FileSystem"), Tenant(2, "FileSystem")),
            fileSystemFactory,
            sqlServerFactory,
            sqlStores);

        var unsupportedTenantId = await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None);

        unsupportedTenantId.ShouldBe(2);
        await fileSystemFactory.DidNotReceive().GetRepositoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await sqlServerFactory.DidNotReceive().GetRepositoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await sqlStores.DidNotReceive().GetReindexStoreAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenOnlySystemAndInactiveTenantsCannotReindex_WhenFindingTenantWithoutReindexSupport_ThenReturnsNull()
    {
        var inactive = Tenant(2, "FileSystem") with { IsActive = false };
        var factory = CreateFactory(TenantStore(Tenant(0), Tenant(1, "SqlServer"), inactive));

        (await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnknownStorageTypeAfterAnUnsupportedTenant_WhenFindingTenantWithoutReindexSupport_ThenThrowsConfigurationError()
    {
        var factory = CreateFactory(TenantStore(Tenant(1, "FileSystem"), Tenant(2, "TypoSqlServer")));

        var exception = await Should.ThrowAsync<NotSupportedException>(
            () => factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None));

        exception.Message.ShouldBe("Storage type 'TypoSqlServer' is not supported");
    }

    [Fact]
    public async Task GivenTenantProvidersChange_WhenFindingTenantWithoutReindexSupportAgain_ThenTheCurrentTenantsAreRead()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(
                [Tenant(1, "SqlServer")],
                [Tenant(1, "SqlServer"), Tenant(2, "FileSystem")]);
        var factory = CreateFactory(tenants);

        (await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None)).ShouldBeNull();
        (await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None)).ShouldBe(2);
    }

    [Fact]
    public async Task GivenTenantStoreFails_WhenFindingTenantWithoutReindexSupportAgain_ThenTheReadIsRetried()
    {
        var attempts = 0;
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1
                ? ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(
                    new InvalidOperationException("tenant configuration is unavailable"))
                : ValueTask.FromResult<IReadOnlyList<TenantConfiguration>>([Tenant(1, "SqlServer")]));
        var factory = CreateFactory(tenants);

        await Should.ThrowAsync<InvalidOperationException>(
            () => factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None));
        (await factory.FindTenantWithoutReindexSupportAsync(CancellationToken.None)).ShouldBeNull();
        attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("SqlEntityFramework")]
    public async Task GivenStorageTypeSupportingReindex_WhenGettingReindexStore_ThenSqlStoreIsReturned(
        string storageType)
    {
        var tenant = Tenant(1, storageType);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(tenant.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);
        var sqlStore = Substitute.For<IReindexStore>();
        var sqlStores = Substitute.For<IReindexStoreFactory>();
        sqlStores.GetReindexStoreAsync(tenant.TenantId, Arg.Any<CancellationToken>())
            .Returns(sqlStore);
        var factory = CreateFactory(tenants, sqlStores);

        (await factory.GetReindexStoreAsync(tenant.TenantId, CancellationToken.None))
            .ShouldBeSameAs(sqlStore);
    }

    [Fact]
    public async Task GivenFileSystemTenant_WhenGettingReindexStore_ThenNotSupportedIsThrown()
    {
        var tenant = Tenant(2, "FileSystem");
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(tenant.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);
        var sqlStores = Substitute.For<IReindexStoreFactory>();
        var factory = CreateFactory(tenants, sqlStores);

        await Should.ThrowAsync<NotSupportedException>(
            () => factory.GetReindexStoreAsync(tenant.TenantId, CancellationToken.None));
        await sqlStores.DidNotReceive().GetReindexStoreAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
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

    private static ITenantConfigurationStore TenantStore(params TenantConfiguration[] tenants)
    {
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(tenants);
        return store;
    }

    private static CompositeRepositoryFactory CreateFactory(
        ITenantConfigurationStore tenants,
        IReindexStoreFactory? sqlReindexStoreFactory = null) =>
        new(
            tenants,
            Substitute.For<IFhirRepositoryFactory>(),
            Substitute.For<IFhirRepositoryFactory>(),
            sqlReindexStoreFactory ?? Substitute.For<IReindexStoreFactory>());
}
