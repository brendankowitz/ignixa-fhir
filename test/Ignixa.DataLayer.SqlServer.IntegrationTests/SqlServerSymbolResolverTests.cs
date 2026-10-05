using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

// CA1001 (owns a disposable field but isn't itself IDisposable): mirrors
// SqlServerSearchIndexReferenceDataCacheTests.cs's own suppression rationale -- the cache's only
// disposable is a SemaphoreSlim, explicitly disposed in DisposeAsync below, and xunit already
// drives this type's lifecycle through IAsyncLifetime.
#pragma warning disable CA1001
public class SqlServerSymbolResolverTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private SqlServerSymbolResolver _resolver = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        _resolver = new SqlServerSymbolResolver(_cache);
    }

    public async Task DisposeAsync()
    {
        _cache.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task GivenASearchParameterWithAKnownUrl_WhenResolved_ThenReturnsItsSearchParamId()
    {
        // Arrange
        const string uri = "http://ignixa.dev/fhir/task7/SearchParameter/patient-name";
        await _database.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.SearchParam (Uri, Status, LastUpdated, IsPartiallySupported) " +
            $"VALUES ('{uri}', 'active', SYSDATETIMEOFFSET(), 0)");
        var expectedId = await _database.ExecuteScalarAsync<int>(
            $"SELECT SearchParamId FROM dbo.SearchParam WHERE Uri = '{uri}'");

        var parameter = new SearchParameterInfo(
            "name",
            "name",
            SearchParamType.String,
            new Uri(uri));

        // Act
        var searchParamId = await _resolver.GetSearchParamIdAsync(parameter, CancellationToken.None);

        // Assert
        searchParamId.ShouldBe((short)expectedId);
    }

    [Fact]
    public async Task GivenAKnownResourceType_WhenResolved_ThenReturnsItsResourceTypeId()
    {
        // Arrange -- TestTenantDatabase.CreateEmptyAsync already seeds one "Patient" row (see
        // TestTenantDatabase.cs's SeedResourceTypeAsync call), so this reads that existing row
        // rather than inserting a second one and violating dbo.ResourceType's PK on Name.
        var expectedId = await _database.ExecuteScalarAsync<int>(
            "SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Patient'");

        // Act
        var resourceTypeId = await _resolver.GetResourceTypeIdAsync("Patient", CancellationToken.None);

        // Assert
        resourceTypeId.ShouldBe((short)expectedId);
    }

    [Fact]
    public async Task GivenASystemInsertedByTheWritePath_WhenResolved_ThenReturnsItsRealIdWithoutInsertingAgain()
    {
        // Arrange
        var systemUri = $"http://example.org/resolver-known-system-{Guid.NewGuid():N}";
        var insertedId = await _cache.GetOrCreateSystemIdAsync(systemUri, CancellationToken.None);

        // Act
        var resolvedId = await _resolver.GetSystemIdAsync(systemUri, CancellationToken.None);

        // Assert
        resolvedId.ShouldBe(insertedId);
    }

    [Fact]
    public async Task GivenASystemNeverInserted_WhenResolved_ThenReturnsNullAndDoesNotInsertARow()
    {
        // Arrange
        var systemUri = $"http://example.org/resolver-unknown-system-{Guid.NewGuid():N}";

        // Act
        var resolvedId = await _resolver.GetSystemIdAsync(systemUri, CancellationToken.None);

        // Assert
        resolvedId.ShouldBeNull();
        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.System WHERE Value = '{systemUri}'");
        rowCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenAQuantityCodeInsertedByTheWritePath_WhenResolved_ThenReturnsItsRealIdWithoutInsertingAgain()
    {
        // Arrange
        var code = $"resolver-known-code-{Guid.NewGuid():N}";
        var insertedId = await _cache.GetOrCreateQuantityCodeIdAsync(code, CancellationToken.None);

        // Act
        var resolvedId = await _resolver.GetQuantityCodeIdAsync(code, CancellationToken.None);

        // Assert
        resolvedId.ShouldBe(insertedId);
    }

    [Fact]
    public async Task GivenAQuantityCodeNeverInserted_WhenResolved_ThenReturnsNullAndDoesNotInsertARow()
    {
        // Arrange
        var code = $"resolver-unknown-code-{Guid.NewGuid():N}";

        // Act
        var resolvedId = await _resolver.GetQuantityCodeIdAsync(code, CancellationToken.None);

        // Assert
        resolvedId.ShouldBeNull();
        var rowCount = await _database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.QuantityCode WHERE Value = '{code}'");
        rowCount.ShouldBe(0);
    }
}
