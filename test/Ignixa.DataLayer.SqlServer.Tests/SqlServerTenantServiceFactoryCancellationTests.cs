using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

/// <summary>
/// The warm path through <c>SqlServerTenantServiceFactory.GetOrInitializeTenantAsync</c> did not observe the
/// caller's cancellation token.
/// <para>
/// The entry it awaits is a <see cref="Lazy{T}"/> over a <see cref="Task"/>, and
/// <see cref="Task.WaitAsync(CancellationToken)"/> returns the task unchanged when it is already complete --
/// the runtime checks <c>IsCompleted</c> before it checks the token. After a tenant's first request that
/// task is always complete, so every subsequent read and write for that tenant ignored a cancelled token.
/// These tests are warm on purpose: with a cold entry the initialization is still running, the wait is a
/// real wait, and the token would be observed with or without the fix -- so a cold test proves nothing.
/// </para>
/// </summary>
public sealed class SqlServerTenantServiceFactoryCancellationTests : IDisposable
{
    private const int TenantId = 1;

    // An empty row set is enough for every read tenant initialization performs: the reference-data cache's
    // two eager preloads, and the search-parameter catalog sync, which resolves nothing and logs that it
    // resolved nothing. No database, and nothing here depends on what initialization produced -- only on the
    // fact that it completed.
    private readonly FixedRowsSqlExecutionService<(short Id, string Name)> _sql = new();
    private readonly SqlServerSearchIndexCacheRegistry _cacheRegistry;

    public SqlServerTenantServiceFactoryCancellationTests()
        => _cacheRegistry = new SqlServerSearchIndexCacheRegistry(_sql, NullLoggerFactory.Instance);

    public void Dispose() => _cacheRegistry.Dispose();

    private sealed class SingleTenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new()
        {
            TenantId = TenantId,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration
            {
                Type = "SqlServer",
                ConnectionString = "Server=localhost;Database=Ignixa;Integrated Security=true;",
            },
        };

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == TenantId ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);
    }

    private SqlServerTenantServiceFactory CreateFactory()
    {
        var schemaDeployer = Substitute.For<ISchemaDeployer>();
        var initializer = new SqlServerTenantInitializer(
            schemaDeployer, _cacheRegistry, NullLogger<SqlServerTenantInitializer>.Instance);

        return new SqlServerTenantServiceFactory(
            new SingleTenantStore(),
            NullLoggerFactory.Instance,
            new RecyclableMemoryStreamManager(),
            initializer,
            // Development, not Production: this suite is about cancellation, and a Production validator would
            // reject the integrated-security-free test connection string before the path under test ran.
            new ManagedIdentityConnectionStringValidator(
                "Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            _sql);
    }

    private async Task<SqlServerTenantServiceFactory> CreateWarmFactoryAsync()
    {
        var factory = CreateFactory();
        await factory.GetRepositoryAsync(TenantId, CancellationToken.None);

        // The precondition every test here rests on. If initialization had failed it would have been evicted,
        // the entry would be cold again, and the assertions below would pass for the wrong reason.
        factory.InitializedTenantCount.ShouldBe(1);
        return factory;
    }

    [Fact]
    public async Task GivenAWarmTenantAndACancelledToken_WhenGettingARepository_ThenItThrowsRatherThanReturningTheCachedServices()
    {
        // Arrange
        var factory = await CreateWarmFactoryAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => factory.GetRepositoryAsync(TenantId, cts.Token));
    }

    [Fact]
    public async Task GivenAWarmTenantAndACancelledToken_WhenGettingASearchService_ThenItThrowsRatherThanReturningTheCachedServices()
    {
        // Arrange
        var factory = await CreateWarmFactoryAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            () => factory.GetSearchServiceAsync(TenantId, cts.Token));
    }

    /// <summary>
    /// The guard sits before the dictionary lookup, so a cancelled caller never reaches the eviction path.
    /// If it did, one load-shed request would throw away the initialization every other request for that
    /// tenant is sharing, and the next one would redeploy the schema.
    /// </summary>
    [Fact]
    public async Task GivenAWarmTenantAndACancelledToken_WhenTheRequestIsRejected_ThenTheSharedEntrySurvivesForEveryoneElse()
    {
        // Arrange
        var factory = await CreateWarmFactoryAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        await Should.ThrowAsync<OperationCanceledException>(
            () => factory.GetRepositoryAsync(TenantId, cts.Token));

        // Assert
        factory.InitializedTenantCount.ShouldBe(1);
        (await factory.GetRepositoryAsync(TenantId, CancellationToken.None)).ShouldNotBeNull();
    }

    /// <summary>
    /// The negative control for all three: without it they would still pass for a factory that had simply
    /// stopped serving warm tenants.
    /// </summary>
    [Fact]
    public async Task GivenAWarmTenantAndALiveToken_WhenGettingARepository_ThenTheCachedServicesAreStillReturned()
    {
        // Arrange
        var factory = await CreateWarmFactoryAsync();
        using var cts = new CancellationTokenSource();

        // Act
        var repository = await factory.GetRepositoryAsync(TenantId, cts.Token);

        // Assert
        repository.ShouldNotBeNull();
        factory.InitializedTenantCount.ShouldBe(1);
    }
}
