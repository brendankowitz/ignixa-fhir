using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

/// <summary>
/// The parts of <c>SyncSearchParametersToDatabaseAsync</c> that are decided before any SQL is issued: the
/// empty-input contract, and the <c>dbo.SearchParam.Uri</c> VARCHAR(128) guard. Everything else about the
/// method is defined by <c>dbo.UpsertSearchParams</c>' MERGE semantics and lives in the integration suite.
/// </summary>
public class SqlServerSearchIndexReferenceDataCacheSyncGuardTests
{
    [Fact]
    public async Task GivenNullUrls_WhenSyncSearchParametersToDatabase_ThenZeroIsReturnedWithoutTouchingTheDatabase()
    {
        // Arrange
        using var cache = CreateCache(out _);

        // Act
        var syncedCount = await cache.SyncSearchParametersToDatabaseAsync(
            null, searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenNoUrls_WhenSyncSearchParametersToDatabase_ThenZeroIsReturnedWithoutTouchingTheDatabase()
    {
        // Arrange
        using var cache = CreateCache(out _);

        // Act
        var syncedCount = await cache.SyncSearchParametersToDatabaseAsync(
            [], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenOnlyBlankUrls_WhenSyncSearchParametersToDatabase_ThenTheyAreRejectedWithAnErrorLog()
    {
        // Arrange: a blank URL cannot identify a dbo.SearchParam row, and inserting one would create a
        // garbage row that no lookup can ever match.
        using var cache = CreateCache(out var logger);

        // Act
        var syncedCount = await cache.SyncSearchParametersToDatabaseAsync(
            ["", "   "], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(0);
        logger.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenOnlyAUrlLongerThanTheUriColumn_WhenSyncSearchParametersToDatabase_ThenItIsRejectedWithAnErrorNamingTheUrl()
    {
        // Arrange: SQL Server rejects an over-length value outright (Msg 2628) rather than truncating it, and
        // dbo.UpsertSearchParams runs its MERGE under XACT_ABORT -- so one such URL in a batch would roll the
        // whole batch back. It is dropped instead, but the loss must be logged where it happens.
        using var cache = CreateCache(out var logger);
        var tooLongUrl = "http://example.org/SearchParameter/" + new string('x', 128);

        // Act
        var syncedCount = await cache.SyncSearchParametersToDatabaseAsync(
            [tooLongUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        syncedCount.ShouldBe(0);
        logger.Errors.ShouldHaveSingleItem().ShouldContain(tooLongUrl);
    }

    [Fact]
    public async Task GivenAUrlExactlyAtTheUriColumnLimit_WhenSyncSearchParametersToDatabase_ThenItIsNotRejected()
    {
        // Arrange: 128 characters fits VARCHAR(128) exactly -- an off-by-one in the guard would silently
        // stop syncing legitimate parameters. Reaching the database proves it passed the guard.
        using var cache = CreateCache(out var logger);
        var boundaryUrl = "http://example.org/" + new string('x', 128 - "http://example.org/".Length);
        boundaryUrl.Length.ShouldBe(128);

        // Act
        var act = async () => await cache.SyncSearchParametersToDatabaseAsync(
            [boundaryUrl], searchParameterDefinitionManager: null, CancellationToken.None);

        // Assert
        await Should.ThrowAsync<InvalidOperationException>(act);
        logger.Errors.ShouldBeEmpty();
    }

    private static SqlServerSearchIndexReferenceDataCache CreateCache(out RecordingLogger<SqlServerSearchIndexReferenceDataCache> logger)
    {
        logger = new RecordingLogger<SqlServerSearchIndexReferenceDataCache>();
        return new SqlServerSearchIndexReferenceDataCache(new UnusableSqlExecutionService(), tenantId: 1, logger);
    }
}
