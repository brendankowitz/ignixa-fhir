using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Indexing;

/// <summary>
/// Covers <c>SqlServerSearchIndexReferenceDataCache.SyncSearchParametersToDatabaseAsync</c>, the only repair
/// surface for a search-parameter cache entry poisoned with the not-found sentinel. Requires a real database:
/// the method's behaviour is defined by <c>dbo.UpsertSearchParams</c>' MERGE (which OUTPUTs ids for INSERTED
/// rows only) and by <c>dbo.SearchParam.Uri</c>'s VARCHAR(128) case-sensitive collation, and this repo has no
/// fake <c>ISqlExecutionService</c> for any of it.
/// </summary>
// CA1001: see SqlServerSearchParameterSyncIndexRowTests -- same rationale, cache disposed in DisposeAsync.
#pragma warning disable CA1001
public class SqlServerSearchIndexReferenceDataCacheSyncTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenAProbeThatCachedTheNotFoundSentinel_WhenSyncSearchParametersToDatabase_ThenTheRealIdReplacesIt()
    {
        // Arrange: probing before the sync caches -1, which TryAdd would then refuse to overwrite for the
        // process lifetime -- every resource would index with this parameter's rows silently dropped.
        const string url = "http://example.org/SearchParameter/us-core-race";
        (await _cache.GetSearchParamIdAsync(url, CancellationToken.None)).ShouldBeNull();

        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            $"VALUES ('{url}', 'Enabled', SYSDATETIMEOFFSET(), 0)");
        var expectedId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{url}'");

        // Act
        await _cache.SyncSearchParametersToDatabaseAsync([url], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        _cache.TryGetSearchParamIdFromCache(url).ShouldBe(
            expectedId,
            "a synced parameter must replace the cached not-found sentinel");
        (await _cache.GetSearchParamIdAsync(url, CancellationToken.None)).ShouldBe(expectedId);
    }

    [Fact]
    public async Task GivenSearchParametersWithNoRows_WhenSyncSearchParametersToDatabase_ThenTheRowsAreInsertedEnabledAndFullySupported()
    {
        // Arrange
        const string firstUrl = "http://example.org/SearchParameter/first";
        const string secondUrl = "http://example.org/SearchParameter/second";

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [firstUrl, secondUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(2);
        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.SearchParam WHERE Status = 'Enabled' AND IsPartiallySupported = 0 AND Uri IN ('{firstUrl}', '{secondUrl}')");
        rowCount.ShouldBe(2);
        _cache.TryGetSearchParamIdFromCache(firstUrl).ShouldNotBeNull();
        _cache.TryGetSearchParamIdFromCache(secondUrl).ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenTheSameUrlTwiceInOneBatch_WhenSyncSearchParametersToDatabase_ThenOneRowIsInsertedWithoutAPrimaryKeyViolation()
    {
        // Arrange: dbo.SearchParam's clustered PK is Uri, so a duplicate reaching the TVP would make
        // dbo.UpsertSearchParams' MERGE attempt the same key twice.
        const string url = "http://example.org/SearchParameter/duplicated";

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [url, url], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(1);
        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.SearchParam WHERE Uri = '{url}'");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenAParameterThatOverridesAnExistingOne_WhenSyncSearchParametersToDatabase_ThenItIsCachedUnderTheOverriddenId()
    {
        // Arrange: an overriding parameter gets its own dbo.SearchParam row, but must index under the
        // overridden parameter's id -- that is what SearchParameterIdLookupHelper's fallback expects.
        const string overriddenUrl = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
        const string overridingUrl = "http://example.org/SearchParameter/custom-identifier";
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            $"VALUES ('{overriddenUrl}', 'Enabled', SYSDATETIMEOFFSET(), 0)");
        var overriddenId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{overriddenUrl}'");

        var overridingParameter = new SearchParameterInfo(
            "custom-identifier", "custom-identifier", SearchParamType.Token, new Uri(overridingUrl))
        {
            OverridesUrl = new Uri(overriddenUrl),
        };
        var manager = new StubSearchParameterDefinitionManager(
            new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal) { [overridingUrl] = overridingParameter });

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync([overridingUrl], manager, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(1);
        _cache.TryGetSearchParamIdFromCache(overridingUrl).ShouldBe(
            overriddenId,
            "an overriding parameter must be cached under the overridden parameter's id, not its own");
        var ownId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{overridingUrl}'");
        ownId.ShouldNotBe(overriddenId, "the overriding parameter still gets its own row");
    }

    [Fact]
    public async Task GivenAnOverrideTargetWithNoRow_WhenSyncSearchParametersToDatabase_ThenTheParameterIsCachedUnderItsOwnId()
    {
        // Arrange: the override target is unknown to the database, so there is no id to alias to.
        const string overridingUrl = "http://example.org/SearchParameter/orphan-override";
        var overridingParameter = new SearchParameterInfo(
            "orphan-override", "orphan-override", SearchParamType.Token, new Uri(overridingUrl))
        {
            OverridesUrl = new Uri("http://example.org/SearchParameter/never-created"),
        };
        var manager = new StubSearchParameterDefinitionManager(
            new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal) { [overridingUrl] = overridingParameter });

        // Act
        await _cache.SyncSearchParametersToDatabaseAsync([overridingUrl], manager, CancellationToken.None);

        // Assert
        var ownId = await _database.ExecuteScalarAsync<short>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{overridingUrl}'");
        _cache.TryGetSearchParamIdFromCache(overridingUrl).ShouldBe(ownId);
    }

    [Fact]
    public async Task GivenUrlsDifferingOnlyByCase_WhenSyncSearchParametersToDatabase_ThenBothAreStoredSeparately()
    {
        // Arrange: dbo.SearchParam.Uri is COLLATE Latin1_General_100_CS_AS, so the database treats these as
        // two distinct keys. Ordinal matching on the C# side reproduces that exactly; an ordinal-ignore-case
        // cache would collapse them and hand one parameter the other's id.
        const string lowerUrl = "http://example.org/SearchParameter/case-test";
        const string upperUrl = "http://example.org/SearchParameter/Case-Test";

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [lowerUrl, upperUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(2);
        _cache.TryGetSearchParamIdFromCache(lowerUrl).ShouldNotBe(_cache.TryGetSearchParamIdFromCache(upperUrl));
    }

    [Fact]
    public async Task GivenAUrlLongerThanTheUriColumn_WhenSyncSearchParametersToDatabase_ThenItIsSkippedWithoutFailingTheRestOfTheBatch()
    {
        // Arrange: dbo.SearchParam.Uri is VARCHAR(128) and SQL Server rejects a longer value outright
        // (Msg 2628). dbo.UpsertSearchParams wraps its MERGE in one XACT_ABORT transaction, so letting the
        // over-length URL through would roll back the whole batch and lose every other parameter in it too.
        var tooLongUrl = "http://example.org/SearchParameter/" + new string('x', 128);
        const string storableUrl = "http://example.org/SearchParameter/storable";

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [tooLongUrl, storableUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(1, "the storable URL must still sync");
        _cache.TryGetSearchParamIdFromCache(storableUrl).ShouldNotBeNull();
        _cache.TryGetSearchParamIdFromCache(tooLongUrl).ShouldBeNull();
        var rowCount = await _database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.SearchParam");
        rowCount.ShouldBe(1);
    }

    [Fact]
    public async Task GivenEveryUrlAlreadyPresent_WhenSyncSearchParametersToDatabase_ThenNothingIsInsertedAndZeroIsReturned()
    {
        // Arrange
        const string url = "http://example.org/SearchParameter/already-there";
        await _cache.SyncSearchParametersToDatabaseAsync([url], searchParameterDefinitionManager: null, CancellationToken.None);

        // Act
        var syncedCount = await _cache.SyncSearchParametersToDatabaseAsync(
            [url], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(0, "synced count reports rows created, and this row already existed");
        var rowCount = await _database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.SearchParam WHERE Uri = '{url}'");
        rowCount.ShouldBe(1);
    }
}
