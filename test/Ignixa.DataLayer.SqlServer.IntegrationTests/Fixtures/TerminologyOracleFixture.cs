using Ignixa.DataLayer.SqlEntityFramework;
using Ignixa.DataLayer.SqlEntityFramework.Features.Terminology;
using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Stands up the **EF** terminology implementation against a real database, so its behaviour can be captured
/// as tests before Phase F ports it. This exists because the port has no other oracle: there is no
/// terminology test project and no terminology test class anywhere in the repository, and the EF
/// implementation stops existing when the project is deleted.
/// <para>
/// Two things make this harder than the other Phase F fixtures, and both are worth knowing because they are
/// the reason the coverage gap existed in the first place.
/// </para>
/// <para>
/// <b>Terminology lives in the system partition, not a tenant.</b>
/// <c>SqlTerminologyService</c> resolves every context through
/// <c>GetDbContextAsync(SystemConstants.SystemPartitionId)</c>, so the tenant store here must serve
/// partition 0 as well as the ordinary test tenant. <see cref="TestTenantDatabase"/>'s own store returns
/// null for anything but tenant 1, which alone makes the service unconstructible in that fixture.
/// </para>
/// <para>
/// <b>It depends on the concrete composition root.</b> <c>SqlTerminologyService</c> takes a
/// <see cref="SqlEntityFrameworkRepositoryFactory"/> rather than an abstraction, so exercising it means
/// building the whole factory: tenant store, logger factory, stream manager, multi-tenant cache, schema
/// deployer and execution service. The Phase F port removes that coupling — it needs only
/// <see cref="ISqlExecutionService"/> — which is precisely why the oracle has to be captured first.
/// </para>
/// </summary>
public sealed class TerminologyOracleFixture : IAsyncDisposable
{
    private readonly TestTenantDatabase _database;
    private readonly SqlEntityFrameworkRepositoryFactory _factory;

    // Each CreateTerminologyService call gets its own cache (see that method); the fixture owns their
    // lifetimes so callers do not have to.
    private readonly List<MemoryCache> _caches = [];

    // Reference-data caches created for the ported importer; disposed with the fixture.
    private readonly List<global::Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexReferenceDataCache> _searchCaches = [];

    private TerminologyOracleFixture(
        TestTenantDatabase database,
        SqlEntityFrameworkRepositoryFactory factory,
        ISqlExecutionService sqlExecutionService)
    {
        _database = database;
        _factory = factory;
        SqlExecutionService = sqlExecutionService;
    }

    public ISqlExecutionService SqlExecutionService { get; }

    public int SystemPartitionId => SystemConstants.SystemPartitionId;

    public static async Task<TerminologyOracleFixture> CreateAsync(CancellationToken cancellationToken = default)
    {
        var database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        var store = new SystemPartitionTenantStore(database.ConnectionString);

        var deployer = new SchemaDeployer(
            store,
            new FixtureHostEnvironment(),
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new SchemaVersionResolver(store, NullLogger<SchemaVersionResolver>.Instance),
            NullLogger<SchemaDeployer>.Instance);

        var sqlExecutionService = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // "Development" rather than the default "Production": the factory's
        // ValidateManagedIdentityAuthentication rejects any connection string carrying a password when the
        // environment is Production. The fixture uses integrated security so it would pass either way, but
        // pinning it here keeps the fixture working if the connection string ever gains credentials.
        var factory = new SqlEntityFrameworkRepositoryFactory(
            store,
            NullLoggerFactory.Instance,
            new RecyclableMemoryStreamManager(),
            new MultiTenantSearchIndexCache(NullLoggerFactory.Instance),
            deployer,
            sqlExecutionService,
            environment: "Development");

        return new TerminologyOracleFixture(database, factory, sqlExecutionService);
    }

    /// <summary>
    /// A terminology service with its own cache. <c>LookupCodeAsync</c> memoises on
    /// <c>system|version|code</c> and returns before touching the database on a hit, so a test that shares a
    /// cache across cases can assert against the cache rather than the query it meant to exercise. Each call
    /// here gets a fresh one.
    /// <para>
    /// <b>The seam Task 6 flips.</b> Every assertion in the oracle was written and run green against the EF
    /// implementation first; none were edited when this changed. Swap the two lines below to compare.
    /// </para>
    /// </summary>
    public ITerminologyService CreateTerminologyService()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        _caches.Add(cache);

        return new SqlServerTerminologyService(
            SqlExecutionService,
            SystemConstants.SystemPartitionId,
            cache,
            NullLogger<SqlServerTerminologyService>.Instance);
    }

    /// <summary>
    /// GetImportStatusAsync is public on both implementations but is not part of ITerminologyService, so it
    /// is routed through the fixture rather than making the seam return a concrete type.
    /// </summary>
    public async Task<Ignixa.Domain.Terminology.TerminologyImportStatus?> GetImportStatusAsync(
        string canonical, CancellationToken cancellationToken = default)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        _caches.Add(cache);

        var service = new SqlServerTerminologyService(
            SqlExecutionService,
            SystemConstants.SystemPartitionId,
            cache,
            NullLogger<SqlServerTerminologyService>.Instance);

        return await service.GetImportStatusAsync(canonical, cancellationToken);
    }

    /// <summary>The EF implementation, kept so a disagreement can be attributed to the port rather than the test.</summary>
    public ITerminologyService CreateEfTerminologyService()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        _caches.Add(cache);
        return new SqlTerminologyService(_factory, cache, NullLogger<SqlTerminologyService>.Instance);
    }

    /// <summary>
    /// The importer, plus the system repository it depends on, built the way
    /// <c>ImportTerminologyResourceActivity</c> builds them: by hand, from a partition-0 context. The caller
    /// owns the returned context's lifetime.
    /// </summary>
    public async Task<(SqlCodeSystemImporter Importer, FhirDbContext Context)> CreateImporterAsync(
        CancellationToken cancellationToken = default)
    {
        var context = await _factory.GetDbContextAsync(SystemConstants.SystemPartitionId, cancellationToken);

        var systemRepository = new SqlSystemRepository(
            context, NullLogger<SqlSystemRepository>.Instance, searchIndexCache: null);

        var importer = new SqlCodeSystemImporter(
            context, systemRepository, NullLogger<SqlCodeSystemImporter>.Instance);

        return (importer, context);
    }

    /// <summary>
    /// The ported CodeSystem importer, built the way the composition root will build it: over
    /// <see cref="ISqlExecutionService"/> alone, with no DbContext and no composition root. It resolves
    /// system ids through the ported <c>SqlServerSystemRepository</c>, so this exercises both.
    /// </summary>
    public SqlServerCodeSystemImporter CreateSqlServerImporter()
    {
        var searchIndexCache = new global::Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexReferenceDataCache(
            SqlExecutionService,
            SystemConstants.SystemPartitionId,
            NullLogger<global::Ignixa.DataLayer.SqlServer.Indexing.SqlServerSearchIndexReferenceDataCache>.Instance);

        _searchCaches.Add(searchIndexCache);

        var systemRepository = new SqlServerSystemRepository(
            searchIndexCache, NullLogger<SqlServerSystemRepository>.Instance);

        return new SqlServerCodeSystemImporter(
            SqlExecutionService,
            SystemConstants.SystemPartitionId,
            systemRepository,
            NullLogger<SqlServerCodeSystemImporter>.Instance);
    }

    public Task<T> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken = default)
        => _database.ExecuteScalarAsync<T>(sql, cancellationToken);

    public Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default)
        => _database.ExecuteNonQueryAsync(sql, cancellationToken);

    /// <summary>
    /// Persists a package resource and returns the model carrying its real identity.
    /// <para>
    /// <c>ImportCodeSystemAsync</c> looks the row up by <c>PackageResourceId</c> and throws if it is absent,
    /// so terminology import genuinely depends on the package row existing first — a CodeSystem arrives as
    /// package content, not on its own. Inserted directly rather than through
    /// <c>IPackageResourceRepository</c>: that interface's upsert returns void, and mixing the ported
    /// repository into an EF oracle fixture would blur which implementation a failure came from.
    /// </para>
    /// </summary>
    public async Task<PackageResource> SeedPackageResourceAsync(
        string resourceType, string canonical, string json, CancellationToken cancellationToken = default)
    {
        var resourceId = canonical.Split('/')[^1];
        var packageId = $"oracle.{Guid.NewGuid():N}";

        var packageResourceId = await _database.ExecuteScalarAsync<long>(
            "INSERT INTO dbo.PackageResource " +
            "(PackageId, PackageVersion, ResourceType, Canonical, ResourceId, ResourceJson, FhirVersion, IsActive) " +
            "OUTPUT INSERTED.PackageResourceId VALUES " +
            $"('{packageId}', '1.0.0', '{resourceType}', '{canonical}', '{resourceId}', " +
            $"'{json.Replace("'", "''", StringComparison.Ordinal)}', '4.0.1', 1)",
            cancellationToken);

        return new PackageResource
        {
            PackageResourceId = packageResourceId,
            PackageId = packageId,
            PackageVersion = "1.0.0",
            ResourceType = resourceType,
            Canonical = canonical,
            ResourceId = resourceId,
            ResourceJson = json,
            FhirVersion = "4.0.1",
            IsActive = true,
        };
    }

    /// <summary>
    /// A CodeSystem with a two-level hierarchy — one root with two children, plus a standalone sibling — so
    /// <c>SubsumesAsync</c> has a real parent walk rather than a single level, and every one of its four
    /// outcomes is reachable from one seed.
    /// </summary>
    public static string HierarchicalCodeSystemJson(string url) =>
        "{" +
        "\"resourceType\":\"CodeSystem\"," +
        $"\"url\":\"{url}\"," +
        "\"version\":\"1.0.0\"," +
        "\"status\":\"active\"," +
        "\"content\":\"complete\"," +
        "\"hierarchyMeaning\":\"is-a\"," +
        "\"caseSensitive\":true," +
        "\"concept\":[" +
        "{\"code\":\"vehicle\",\"display\":\"Vehicle\",\"concept\":[" +
        "{\"code\":\"car\",\"display\":\"Car\"}," +
        "{\"code\":\"truck\",\"display\":\"Truck\"}]}," +
        "{\"code\":\"building\",\"display\":\"Building\"}" +
        "]}";

    /// <summary>
    /// A flat CodeSystem of <paramref name="conceptCount"/> concepts, for straddling the importer's
    /// 1,000-concept bulk threshold. Flat rather than nested because the point is which insert path runs,
    /// not the hierarchy — and below the threshold the hierarchy would be discarded anyway.
    /// </summary>
    public static string FlatCodeSystemJson(string url, int conceptCount)
    {
        var concepts = string.Join(",", Enumerable.Range(0, conceptCount)
            .Select(i => $"{{\"code\":\"c{i}\",\"display\":\"Concept {i}\"}}"));

        return "{" +
            "\"resourceType\":\"CodeSystem\"," +
            $"\"url\":\"{url}\"," +
            "\"version\":\"1.0.0\"," +
            "\"status\":\"active\"," +
            "\"content\":\"complete\"," +
            "\"caseSensitive\":true," +
            $"\"concept\":[{concepts}]" +
            "}";
    }

    /// <summary>
    /// A ValueSet carrying a pre-computed expansion. <c>name</c> is mandatory — the importer throws
    /// <see cref="InvalidOperationException"/> without it — and the expansion's <c>contains</c> entries are
    /// what become <c>dbo.TermValueSetExpansion</c> rows.
    /// </summary>
    public static string ExpandedValueSetJson(string url, string codeSystemUrl, params string[] codes)
    {
        var contains = string.Join(",", codes.Select(c =>
            $"{{\"system\":\"{codeSystemUrl}\",\"code\":\"{c}\",\"display\":\"Display {c}\"}}"));

        return "{" +
            "\"resourceType\":\"ValueSet\"," +
            $"\"url\":\"{url}\"," +
            "\"name\":\"OracleValueSet\"," +
            "\"version\":\"1.0.0\"," +
            "\"status\":\"active\"," +
            $"\"expansion\":{{\"contains\":[{contains}]}}" +
            "}";
    }

    /// <summary>
    /// A ConceptMap with a single group mapping one code to another. The group's <c>source</c> and
    /// <c>target</c> system URLs are resolved through <c>ISystemRepository.GetOrCreateAsync</c> during
    /// import, so importing this also creates those system rows.
    /// </summary>
    public static string ConceptMapJson(string url, string sourceSystem, string targetSystem) =>
        "{" +
        "\"resourceType\":\"ConceptMap\"," +
        $"\"url\":\"{url}\"," +
        "\"name\":\"OracleConceptMap\"," +
        "\"version\":\"1.0.0\"," +
        "\"status\":\"active\"," +
        "\"group\":[{" +
        $"\"source\":\"{sourceSystem}\"," +
        $"\"target\":\"{targetSystem}\"," +
        "\"element\":[{" +
        "\"code\":\"car\",\"display\":\"Car\"," +
        "\"target\":[{\"code\":\"auto\",\"display\":\"Auto\",\"equivalence\":\"equivalent\"}]" +
        "}]}]}";

    public async ValueTask DisposeAsync()
    {
        foreach (var cache in _caches)
        {
            cache.Dispose();
        }

        foreach (var searchCache in _searchCaches)
        {
            searchCache.Dispose();
        }

        await _database.DisposeAsync();
    }

    /// <summary>
    /// Serves the ordinary test tenant and the system partition from the same database. Real deployments
    /// give partition 0 no connection string of its own and let it inherit
    /// (<c>InheritConnectionStringFromTenant</c>); the fixture shortcuts that by configuring both directly,
    /// since the inheritance path is the composition root's concern rather than terminology's.
    /// </summary>
    private sealed class SystemPartitionTenantStore(string connectionString) : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = Build(TestTenantDatabase.TestTenantId, connectionString, isSystemPartition: false);
        private readonly TenantConfiguration _systemPartition = Build(SystemConstants.SystemPartitionId, connectionString, isSystemPartition: true);

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == SystemConstants.SystemPartitionId ? _systemPartition
                : tenantId == TestTenantDatabase.TestTenantId ? _tenant
                : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant, _systemPartition });

        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
            => new((TenantConfiguration?)null);

        private static TenantConfiguration Build(int tenantId, string connectionString, bool isSystemPartition) => new()
        {
            TenantId = tenantId,
            DisplayName = isSystemPartition ? "System Partition" : "Test Tenant",
            FhirVersion = "4.0",
            IsSystemPartition = isSystemPartition,
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
        };
    }

    private sealed class FixtureHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "Ignixa.DataLayer.SqlServer.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
