using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerPostMergeExtensionUpdaterTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private SqlServerPostMergeExtensionUpdater _updater = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _updater = new SqlServerPostMergeExtensionUpdater(
            _database.SqlExecutionService, _database.TenantId, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance);
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenAnEmptyExtensionList_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenNoOpsWithoutError()
    {
        await Should.NotThrowAsync(() =>
            _updater.UpdateTokenSearchParamExtensionsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task GivenAPreExistingTokenSearchParamRow_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenTheExtensionColumnsAreSet()
    {
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, 1000, 1, NULL, 'test-code')");

        var extension = new TokenSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 1000,
            SearchParamId: 1,
            SystemId: null,
            Code: "test-code",
            IdentifierTypeSystemId: 42,
            IdentifierTypeCode: "MR");

        await _updater.UpdateTokenSearchParamExtensionsAsync([extension], CancellationToken.None);

        var identifierTypeCode = await _database.ExecuteScalarAsync<string>(
            "SELECT IdentifierTypeCode FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = 1000");
        identifierTypeCode.ShouldBe("MR");
    }

    [Fact]
    public async Task GivenAnEmptyExtensionList_WhenUpdateUriSearchParamExtensionsAsyncCalled_ThenNoOpsWithoutError()
    {
        await Should.NotThrowAsync(() =>
            _updater.UpdateUriSearchParamExtensionsAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task GivenAPreExistingUriSearchParamRow_WhenUpdateUriSearchParamExtensionsAsyncCalled_ThenTheExtensionColumnsAreSet()
    {
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri) VALUES (1, 1000, 1, 'http://example.com/uri')");

        var extension = new UriSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 1000,
            SearchParamId: 1,
            Uri: "http://example.com/uri",
            Version: "1.0",
            Fragment: "section1");

        await _updater.UpdateUriSearchParamExtensionsAsync([extension], CancellationToken.None);

        var version = await _database.ExecuteScalarAsync<string>(
            "SELECT Version FROM dbo.UriSearchParam WHERE ResourceSurrogateId = 1000");
        version.ShouldBe("1.0");

        var fragment = await _database.ExecuteScalarAsync<string>(
            "SELECT Fragment FROM dbo.UriSearchParam WHERE ResourceSurrogateId = 1000");
        fragment.ShouldBe("section1");
    }
}
