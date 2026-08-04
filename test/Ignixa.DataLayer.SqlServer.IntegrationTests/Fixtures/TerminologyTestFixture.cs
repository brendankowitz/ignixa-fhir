using Ignixa.DataLayer.SqlServer;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Stands up the terminology stack against a real database: the importers, the terminology service and the
/// search-index cache registry, all over <see cref="ISqlExecutionService"/>.
/// <para>
/// <b>Terminology lives in the system partition, not a tenant.</b> Every terminology query resolves against
/// <see cref="SystemConstants.SystemPartitionId"/>, so the tenant store here serves partition 0 as well as
/// the ordinary test tenant. <see cref="TestTenantDatabase"/>'s own store returns null for anything but
/// tenant 1, which alone makes the service unconstructible in that fixture — which is why this one exists.
/// </para>
/// <para>
/// Both partitions point at the same physical database, whose schema <see cref="TestTenantDatabase"/> has
/// already deployed. Nothing here needs a composition root: the importers and the service take
/// <see cref="ISqlExecutionService"/> and a partition id, and that is the whole dependency graph.
/// </para>
/// </summary>
public sealed class TerminologyTestFixture : IAsyncDisposable
{
    private readonly TestTenantDatabase _database;

    // Each CreateTerminologyService call gets its own cache (see that method); the fixture owns their
    // lifetimes so callers do not have to.
    private readonly List<MemoryCache> _caches = [];

    // Reference-data caches created for the importer; disposed with the fixture.
    private readonly List<SqlServerSearchIndexReferenceDataCache> _searchCaches = [];

    private readonly SqlServerSearchIndexCacheRegistry _cacheRegistry;

    private TerminologyTestFixture(
        TestTenantDatabase database,
        ISqlExecutionService sqlExecutionService,
        SqlServerSearchIndexCacheRegistry cacheRegistry)
    {
        _database = database;
        SqlExecutionService = sqlExecutionService;
        _cacheRegistry = cacheRegistry;
    }

    /// <summary>
    /// The registry the write path resolves its per-tenant cache through, so a test can reach the same
    /// instance the write path uses. Obtaining a cache any other way defeats the point of the registry.
    /// </summary>
    public SqlServerSearchIndexCacheRegistry CacheRegistry => _cacheRegistry;

    public ISqlExecutionService SqlExecutionService { get; }

    public int SystemPartitionId => SystemConstants.SystemPartitionId;

    public static async Task<TerminologyTestFixture> CreateAsync(CancellationToken cancellationToken = default)
    {
        var database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();

        var store = new SystemPartitionTenantStore(database.ConnectionString);

        var sqlExecutionService = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        var cacheRegistry = new SqlServerSearchIndexCacheRegistry(
            sqlExecutionService, NullLoggerFactory.Instance);

        return new TerminologyTestFixture(database, sqlExecutionService, cacheRegistry);
    }

    /// <summary>
    /// A terminology service with its own cache. <c>LookupCodeAsync</c> memoises on
    /// <c>system|version|code</c> and returns before touching the database on a hit, so a test that shares a
    /// cache across cases can assert against the cache rather than the query it meant to exercise. Each call
    /// here gets a fresh one.
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
    /// The CodeSystem importer, built the way the composition root builds it: over
    /// <see cref="ISqlExecutionService"/> alone, with no DbContext and no composition root. It resolves
    /// system ids through <see cref="SqlServerSystemRepository"/>, so this exercises both.
    /// </summary>
    public SqlServerCodeSystemImporter CreateSqlServerImporter()
    {
        var searchIndexCache = new SqlServerSearchIndexReferenceDataCache(
            SqlExecutionService,
            SystemConstants.SystemPartitionId,
            NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

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
    /// <c>IPackageResourceRepository</c> so that a failure here is unambiguously a seeding failure rather
    /// than a defect in the repository under test.
    /// </para>
    /// </summary>
    public async Task<PackageResource> SeedPackageResourceAsync(
        string resourceType, string canonical, string json, CancellationToken cancellationToken = default)
    {
        var resourceId = canonical.Split('/')[^1];
        var packageId = $"terminology.{Guid.NewGuid():N}";

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
            "\"name\":\"TerminologyTestValueSet\"," +
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
        "\"name\":\"TerminologyTestConceptMap\"," +
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

        _cacheRegistry.Dispose();

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
}
