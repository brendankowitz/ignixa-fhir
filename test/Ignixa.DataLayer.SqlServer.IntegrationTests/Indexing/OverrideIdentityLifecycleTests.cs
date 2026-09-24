using Ignixa.Abstractions;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.EventStore;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using Shouldly;
using ConformanceState = Ignixa.Application.Features.Conformance.ConformanceState;
using SearchComparator = Ignixa.Specification.ValueSets.Normative.SearchComparator;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

public class OverrideIdentityLifecycleTests : IAsyncLifetime
{
    private const string OriginalUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideUrl = "http://example.org/SearchParameter/lifecycle-identifier";
    private const string OtherUrl = "http://example.org/SearchParameter/lifecycle-other";
    private const string Identifier = "override-lifecycle";

    private static readonly SearchParameterInfo OriginalParameter = new(
        "identifier", "identifier", SearchParamType.Token, new Uri(OriginalUrl));

    private static readonly SearchParameterInfo OverrideParameter = new(
        "identifier", "identifier", SearchParamType.Token, new Uri(OverrideUrl))
    {
        OverridesUrl = new Uri(OriginalUrl)
    };

    private static readonly StubSearchParameterDefinitionManager Definitions = new(
        new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal)
        {
            [OriginalUrl] = OriginalParameter,
            [OverrideUrl] = OverrideParameter
        });

    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateEmptyAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenPersistedPackageOverride_WhenRegistryRestartsOrEvicts_ThenColdQueriesAndWritesRetainIdentity(bool eager)
    {
        using (var seed = CreateCache())
        {
            await seed.SyncSearchParametersToDatabaseAsync([OriginalUrl], null, CancellationToken.None);
            await WritePatientAsync(seed, OriginalParameter, "before-restart");
            await seed.SyncSearchParametersToDatabaseAsync([OverrideUrl], Definitions, CancellationToken.None);
            await WritePatientAsync(seed, OverrideParameter, "before-eviction");
        }
        var store = new SqlServerSourceEventStore(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSourceEventStore>.Instance);
        await store.AppendAsync(
        [
            new NewSourceEvent("override-lifecycle", nameof(SearchParameterActivated),
                new SearchParameterActivated(OverrideUrl, "identifier", "Patient", "Patient.identifier",
                    SearchParamType.Token, "lifecycle.package@1.0", new OverrideInfo(OriginalUrl, 1),
                    1, null, null, null, null))
        ], CancellationToken.None);
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var baseManager = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        var definitions = new CompositeSearchParameterDefinitionManager(
            baseManager, state, "4.0", NullLogger<CompositeSearchParameterDefinitionManager>.Instance,
            new SearchParameterResolutionOptions { EagerLoadPackageSearchParameters = eager });
        if (eager)
        {
            await definitions.InitializeAsync();
        }

        using var registry = CreateRegistry(definitions);
        var cold = await registry.GetOrCreateAsync(_database.TenantId, CancellationToken.None);
        await AssertPatientsAsync(cold, "before-restart", "before-eviction");
        await WritePatientAsync(cold, OverrideParameter, "after-restart");
        await AssertPatientsAsync(cold, "before-restart", "before-eviction", "after-restart");

        registry.Invalidate(_database.TenantId).ShouldBeTrue();
        definitions.ClearCache();
        var evicted = await registry.GetOrCreateAsync(_database.TenantId, CancellationToken.None);
        await AssertPatientsAsync(evicted, "before-restart", "before-eviction", "after-restart");
        await WritePatientAsync(evicted, OverrideParameter, "after-eviction");
        await AssertPatientsAsync(evicted, "before-restart", "before-eviction", "after-restart", "after-eviction");
        definitions.AllSearchParameters.ShouldContain(p => p.Url == OverrideParameter.Url);
        definitions.GetSearchParameter("Patient", "identifier").Url.ShouldBe(OverrideParameter.Url);
    }

    private SqlServerSearchIndexCacheRegistry CreateRegistry(ISearchParameterDefinitionManager definitions)
    {
        // The same behavior test runs on the baseline, whose registry lacks the definition factory.
        var constructor = typeof(SqlServerSearchIndexCacheRegistry).GetConstructors()
            .SingleOrDefault(c => c.GetParameters().Length == 3);
        return constructor is null
            ? new SqlServerSearchIndexCacheRegistry(_database.SqlExecutionService, NullLoggerFactory.Instance)
            : (SqlServerSearchIndexCacheRegistry)constructor.Invoke(
                [_database.SqlExecutionService, NullLoggerFactory.Instance,
                    new Func<int, CancellationToken, Task<ISearchParameterDefinitionManager>>((_, _) => Task.FromResult(definitions))]);
    }

    [Fact]
    public async Task GivenInitializedTenantServices_WhenRegistryIsEvicted_ThenTheNextWriteUsesTheReplacementCache()
    {
        using var registry = CreateRegistry(Definitions);
        var factory = new SqlServerTenantServiceFactory(
            new TenantStore(_database.ConnectionString), NullLoggerFactory.Instance, new RecyclableMemoryStreamManager(),
            new SqlServerTenantInitializer(new ExistingSchemaDeployer(), registry, NullLogger<SqlServerTenantInitializer>.Instance),
            new ManagedIdentityConnectionStringValidator("Development", NullLogger<ManagedIdentityConnectionStringValidator>.Instance),
            _database.SqlExecutionService);
        await factory.GetRepositoryAsync(_database.TenantId);
        var oldCache = await registry.GetOrCreateAsync(_database.TenantId, CancellationToken.None);
        await oldCache.SyncSearchParametersToDatabaseAsync([OverrideUrl], Definitions, CancellationToken.None);
        registry.Invalidate(_database.TenantId).ShouldBeTrue();
        var repository = await factory.GetRepositoryAsync(_database.TenantId);
        var patient = new ResourceWrapper(
            "Patient", "tenant-after-eviction", "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"tenant-after-eviction"}"""),
            new ResourceRequest("PUT", "Patient/tenant-after-eviction"))
        {
            SearchIndices =
            [
                new SearchIndexEntry(OverrideParameter, new TokenSearchValue("http://new-system.example", Identifier, null))
            ]
        };

        await repository.CreateOrUpdateAsync(patient);

        var currentCache = await registry.GetOrCreateAsync(_database.TenantId, CancellationToken.None);
        await AssertPatientsAsync(currentCache, "tenant-after-eviction");
    }

    [Fact]
    public async Task GivenIndexedIdentifiers_WhenResyncedAndCacheRecreated_ThenOldAndNewResourcesShareTheOriginalIdentity()
    {
        using var cache = CreateCache();
        await cache.SyncSearchParametersToDatabaseAsync([OriginalUrl], null, CancellationToken.None);
        await WritePatientAsync(cache, OriginalParameter, "before-override");
        (await cache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBeNull();

        await cache.SyncSearchParametersToDatabaseAsync([OverrideUrl], Definitions, CancellationToken.None);
        await WritePatientAsync(cache, OverrideParameter, "after-override");
        await AssertPatientsAsync(cache, "before-override", "after-override");

        (await cache.SyncSearchParametersToDatabaseAsync([OverrideUrl], Definitions, CancellationToken.None)).ShouldBe(0);
        await WritePatientAsync(cache, OverrideParameter, "after-resync");
        await AssertPatientsAsync(cache, "before-override", "after-override", "after-resync");

        await cache.SyncSearchParametersToDatabaseAsync([OtherUrl, OverrideUrl], Definitions, CancellationToken.None);
        await WritePatientAsync(cache, OverrideParameter, "after-other-package");
        await AssertPatientsAsync(cache, "before-override", "after-override", "after-resync", "after-other-package");

        // Reconstruct from the same authoritative definitions, not an alias dictionary copied from cache.
        using var freshCache = CreateCache();
        (await freshCache.SyncSearchParametersToDatabaseAsync(
            [OriginalUrl, OverrideUrl, OtherUrl], Definitions, CancellationToken.None)).ShouldBe(0);
        await freshCache.PreloadSearchParamsAsync(null, CancellationToken.None);
        await WritePatientAsync(freshCache, OverrideParameter, "after-recreation");
        await AssertPatientsAsync(
            freshCache, "before-override", "after-override", "after-resync", "after-other-package", "after-recreation");

        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(DISTINCT SearchParamId) FROM dbo.TokenSearchParam WHERE Code = '{Identifier}'")).ShouldBe(1);
        (await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE SearchParamId = (SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{OverrideUrl}')")).ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAnOverrideBeforeItsTargetInTheBatch_WhenPreloadedAgain_ThenBothUseTheTargetId(bool rowsAlreadyExist)
    {
        using var cache = CreateCache();
        if (rowsAlreadyExist)
        {
            await cache.SyncSearchParametersToDatabaseAsync([OverrideUrl, OriginalUrl], null, CancellationToken.None);
        }

        await cache.SyncSearchParametersToDatabaseAsync([OverrideUrl, OriginalUrl], Definitions, CancellationToken.None);
        var targetId = await cache.GetSearchParamIdAsync(OriginalUrl, CancellationToken.None);
        cache.TryGetSearchParamIdFromCache(OverrideUrl).ShouldBe(targetId);
        await cache.PreloadSearchParamsAsync(null, CancellationToken.None);
        cache.TryGetSearchParamIdFromCache(OverrideUrl).ShouldBe(targetId);

        await WritePatientAsync(cache, OverrideParameter, "ordered-override");
        await AssertPatientsAsync(cache, "ordered-override");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenDefinitionsBeforeCatalogRows_WhenColdLookupOrPreload_ThenTheOverrideUsesTheTargetId(bool preload)
    {
        using var cache = CreateCache();
        await cache.SyncSearchParametersToDatabaseAsync([OtherUrl], Definitions, CancellationToken.None);
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) VALUES " +
            $"('{OverrideUrl}', 'Enabled', SYSDATETIMEOFFSET(), 0), ('{OriginalUrl}', 'Enabled', SYSDATETIMEOFFSET(), 0)");
        var targetId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{OriginalUrl}'");

        if (preload)
        {
            // The target need not be among the capped preload's rows.
            await cache.PreloadSearchParamsAsync(1, CancellationToken.None);
        }

        (await cache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBe(targetId);
    }

    [Fact]
    public async Task GivenAnOverrideWithAnUnseededTarget_WhenAnotherPackageIsSynced_ThenTheFirstRootIdentityIsPreserved()
    {
        using var cache = CreateCache();
        await cache.SyncSearchParametersToDatabaseAsync([OverrideUrl], Definitions, CancellationToken.None);
        var firstId = await cache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None);
        var baseOnlyDefinitions = new StubSearchParameterDefinitionManager(
            new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal) { [OriginalUrl] = OriginalParameter });
        await cache.SyncSearchParametersToDatabaseAsync([OriginalUrl], baseOnlyDefinitions, CancellationToken.None);

        var targetId = await cache.GetSearchParamIdAsync(OriginalUrl, CancellationToken.None);
        targetId.ShouldBe(firstId);
        (await cache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBe(targetId);
    }

    private SqlServerSearchIndexReferenceDataCache CreateCache() => new(
        _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

    private async Task WritePatientAsync(
        SqlServerSearchIndexReferenceDataCache cache, SearchParameterInfo parameter, string id)
    {
        var compressor = new GzipResourceCompressor(new RecyclableMemoryStreamManager());
        var updater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
        var repository = new SqlServerMergeRepository(
            _database.SqlExecutionService, _database.TenantId, compressor, cache, updater,
            NullLogger<SqlServerMergeRepository>.Instance);
        var resource = new ResourceWrapper(
            "Patient", id, "1", DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{id}}","identifier":[{"value":"{{Identifier}}"}]}"""),
            new ResourceRequest("PUT", $"Patient/{id}"))
        {
            SearchIndices = [new SearchIndexEntry(parameter, new TokenSearchValue(null, Identifier, null))]
        };
        var (transactionId, _) = await repository.BeginTransactionAsync(1, CancellationToken.None);
        await repository.MergeResourcesAsync(transactionId, true, [resource], [0], CancellationToken.None);
        await repository.CommitTransactionAsync(transactionId, cancellationToken: CancellationToken.None);
    }

    private async Task AssertPatientsAsync(SqlServerSearchIndexReferenceDataCache cache, params string[] expectedIds)
    {
        var baseDefinitions = new SearchParameterDefinitionManager(
            FhirVersion.R4.GetSchemaProvider(), NullLogger<SearchParameterDefinitionManager>.Instance);
        var service = new SqlServerCompiledSearchService(
            _database.SqlExecutionService, _database.TenantId, new SqlServerSymbolResolver(cache),
            new CompartmentDefinitionManager(FhirVersion.R4), baseDefinitions,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()), NullLogger.Instance);

        foreach (var parameter in new[] { OriginalParameter, OverrideParameter })
        {
            var options = new SearchOptions
            {
                ResourceType = "Patient",
                Expression = new SearchParameterExpression(
                    parameter,
                    new SearchParameterPredicateExpression(
                        parameter, SearchComparator.Eq, null, new TokenSearchValue(null, Identifier, null)))
            };
            List<string> actualIds = [];
            await foreach (var result in service.SearchStreamAsync(options, CancellationToken.None))
            {
                result.SearchMode.ShouldBe(SearchEntryMode.Match);
                actualIds.Add(result.ResourceId!);
            }

            actualIds.Order().ShouldBe(expectedIds.Order(), $"identifier search using {parameter.Url}");
        }
    }

    private sealed class ExistingSchemaDeployer : ISchemaDeployer
    {
        public Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TenantStore(string connectionString) : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new()
        {
            TenantId = TestTenantDatabase.TestTenantId, DisplayName = "Override tenant", FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString }
        };
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default) =>
            new(tenantId == _tenant.TenantId ? _tenant : null);
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default) =>
            new([_tenant]);
        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default) =>
            new((TenantConfiguration?)null);
    }
}
