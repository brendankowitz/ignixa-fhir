using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

public class SqlFreshTenantDefinitionSeedTests : IAsyncLifetime
{
    private const string RootUrl = "http://example.org/SearchParameter/fresh-root";
    private const string OverrideUrl = "http://example.org/SearchParameter/fresh-override";
    private const string CustomUrl = "http://example.org/SearchParameter/fresh-custom";
    private const string CoreUrl = "http://example.org/SearchParameter/fresh-core";

    private TestTenantDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestTenantDatabase.CreateEmptyAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task GivenColdAuthoritativeDefinitions_WhenCoreDefinitionsAreSeeded_ThenAuthorityAndAllStorageRootsArePreserved()
    {
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance,
            searchParameterDefinitionManager: CreatePackageDefinitions());

        await cache.SeedSearchParametersToDatabaseAsync(CreateCoreDefinitions(), CancellationToken.None);

        await AssertAllMappingsAsync(cache);
        await cache.PreloadSearchParamsAsync(null, CancellationToken.None);
        await AssertAllMappingsAsync(cache);
    }

    [Fact]
    public async Task GivenSdkCacheWithoutAuthority_WhenSynchronizedThenCoreSeeded_ThenNewDefinitionsRepairMissesAndRemainAuthoritative()
    {
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await cache.SeedSearchParametersToDatabaseAsync(CreateCoreDefinitions(), CancellationToken.None);
        cache.SearchParameterMappings.ShouldContainKey(CoreUrl);
        (await cache.GetSearchParamIdAsync(OverrideUrl, CancellationToken.None)).ShouldBeNull();
        (await cache.GetSearchParamIdAsync(CustomUrl, CancellationToken.None)).ShouldBeNull();

        await cache.SyncSearchParametersToDatabaseAsync(
            [OverrideUrl, CustomUrl], CreatePackageDefinitions(), CancellationToken.None);
        await cache.SeedSearchParametersToDatabaseAsync(CreateCoreDefinitions(), CancellationToken.None);

        await AssertAllMappingsAsync(cache);
    }

    [Fact]
    public async Task GivenEmptySqlCatalog_WhenRegistryPublishesOrRecreatesCache_ThenAuthoritativeMappingsAlreadyExist()
    {
        using var registry = new SqlServerSearchIndexCacheRegistry(
            _database.SqlExecutionService, NullLoggerFactory.Instance,
            (_, _) => Task.FromResult<ISearchParameterDefinitionManager>(CreatePackageDefinitions()));

        var first = await registry.GetOrCreateAsync(_database.TenantId, CancellationToken.None);
        first.SearchParameterMappings.ShouldContainKey(CustomUrl);
        first.SearchParameterMappings.ShouldContainKey(RootUrl);
        first.SearchParameterMappings[OverrideUrl].ShouldBe(first.SearchParameterMappings[RootUrl]);
        registry.Invalidate(_database.TenantId).ShouldBeTrue();
        var recreated = await registry.GetOrCreateAsync(_database.TenantId, CancellationToken.None);
        ReferenceEquals(first, recreated).ShouldBeFalse();
        recreated.SearchParameterMappings.ShouldContainKey(CustomUrl);
        recreated.SearchParameterMappings[OverrideUrl].ShouldBe(recreated.SearchParameterMappings[RootUrl]);
    }

    private async Task AssertAllMappingsAsync(SqlServerSearchIndexReferenceDataCache cache)
    {
        cache.SearchParameterMappings.ShouldContainKey(RootUrl);
        cache.SearchParameterMappings.ShouldContainKey(OverrideUrl);
        cache.SearchParameterMappings.ShouldContainKey(CustomUrl);
        cache.SearchParameterMappings.ShouldContainKey(CoreUrl);
        var rootId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{RootUrl}'");
        var overrideId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{OverrideUrl}'");
        var customId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{CustomUrl}'");
        overrideId.ShouldNotBe(rootId);
        cache.SearchParameterMappings[OverrideUrl].ShouldBe(rootId);
        cache.SearchParameterMappings[RootUrl].ShouldBe(rootId);
        cache.SearchParameterMappings[CustomUrl].ShouldBe(customId);
    }

    private static StubSearchParameterDefinitionManager CreateCoreDefinitions() => new(
        new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal)
        {
            [CoreUrl] = new("core", "core", SearchParamType.Token, new Uri(CoreUrl))
        });

    private static StubSearchParameterDefinitionManager CreatePackageDefinitions() => new(
        new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal)
        {
            [OverrideUrl] = new("override", "override", SearchParamType.Token, new Uri(OverrideUrl))
            {
                OverridesUrl = new Uri(RootUrl)
            },
            [CustomUrl] = new("custom", "custom", SearchParamType.Token, new Uri(CustomUrl))
        });
}
