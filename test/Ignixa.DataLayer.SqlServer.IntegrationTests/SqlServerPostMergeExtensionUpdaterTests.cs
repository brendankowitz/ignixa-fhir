using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.RowGenerators;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlServerPostMergeExtensionUpdaterTests : IAsyncLifetime
{
    private TestTenantDatabase _database = null!;
    private RecordingLogger<SqlServerPostMergeExtensionUpdater> _logger = null!;
    private SqlServerPostMergeExtensionUpdater _updater = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateEmptyAsync();
        _logger = new RecordingLogger<SqlServerPostMergeExtensionUpdater>();
        _updater = new SqlServerPostMergeExtensionUpdater(_database.SqlExecutionService, _database.TenantId, _logger);
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

        // Rows affected (1) matched the input count (1): success is logged at Information, not Error.
        _logger.Errors.ShouldBeEmpty();
        _logger.Messages(LogLevel.Information).ShouldContain(
            message => message.Contains("Updated 1 TokenSearchParam extension records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenATokenSearchParamRowThatDoesNotMatchTheUpdateKey_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenAnErrorIsLoggedWithTheExpectedAndActualCounts()
    {
        // No matching dbo.TokenSearchParam row is seeded, so the batch UPDATE's WHERE clause matches
        // nothing and 0 rows are affected -- simulating a race/ordering bug where the merge's row isn't
        // there (or isn't there yet) when the post-merge extension update runs.
        var extension = new TokenSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 1000,
            SearchParamId: 1,
            SystemId: null,
            Code: "test-code",
            IdentifierTypeSystemId: 42,
            IdentifierTypeCode: "MR");

        await _updater.UpdateTokenSearchParamExtensionsAsync([extension], CancellationToken.None);

        _logger.Errors.ShouldContain(message =>
            message.Contains("TokenSearchParam", StringComparison.Ordinal) &&
            message.Contains($"tenant {_database.TenantId}", StringComparison.Ordinal) &&
            message.Contains("MissedCount=1", StringComparison.Ordinal) &&
            message.Contains("TotalCount=1", StringComparison.Ordinal));
        _logger.Messages(LogLevel.Information).ShouldNotContain(
            message => message.Contains("Updated 1 TokenSearchParam extension records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenExtensionsSpanningTwoBatches_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenMissesAreAccumulatedAcrossBatches()
    {
        // BatchSize is 100, so 101 extensions span two batches (100 + 1). Seed a matching row for every
        // extension except one in each batch -- index 50 (first batch) and index 100 (the lone second
        // batch). If @Missed were reset per batch instead of accumulated into totalMissed across the
        // foreach loop, the final log would report 1 miss (whichever batch happened to run last), not
        // the true total of 2, so this proves accumulation rather than just per-batch correctness.
        const int extensionCount = 101;
        const int missedIndexInFirstBatch = 50;
        const int missedIndexInSecondBatch = 100;

        var extensions = new List<TokenSearchParamExtensionData>();
        for (var i = 0; i < extensionCount; i++)
        {
            var resourceSurrogateId = 2000 + i;
            var isMissed = i is missedIndexInFirstBatch or missedIndexInSecondBatch;

            if (!isMissed)
            {
                await _database.ExecuteNonQueryAsync(
                    $"INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, {resourceSurrogateId}, 1, NULL, 'code-{i}')");
            }

            extensions.Add(new TokenSearchParamExtensionData(
                ResourceTypeId: 1,
                ResourceSurrogateId: resourceSurrogateId,
                SearchParamId: 1,
                SystemId: null,
                Code: $"code-{i}",
                IdentifierTypeSystemId: 42,
                IdentifierTypeCode: "MR"));
        }

        await _updater.UpdateTokenSearchParamExtensionsAsync(extensions, CancellationToken.None);

        _logger.Errors.ShouldContain(message =>
            message.Contains("TokenSearchParam", StringComparison.Ordinal) &&
            message.Contains($"tenant {_database.TenantId}", StringComparison.Ordinal) &&
            message.Contains("MissedCount=2", StringComparison.Ordinal) &&
            message.Contains($"TotalCount={extensionCount}", StringComparison.Ordinal));

        // Every non-missed row across both batches was actually updated, confirming the batching
        // itself still does its job end to end and the two seeded misses are the only misses.
        var updatedCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE IdentifierTypeCode = 'MR'");
        updatedCount.ShouldBe(extensionCount - 2);
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

        // Rows affected (1) matched the input count (1): success is logged at Information, not Error.
        _logger.Errors.ShouldBeEmpty();
        _logger.Messages(LogLevel.Information).ShouldContain(
            message => message.Contains("Updated 1 UriSearchParam extension records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAUriSearchParamRowThatDoesNotMatchTheUpdateKey_WhenUpdateUriSearchParamExtensionsAsyncCalled_ThenAnErrorIsLoggedWithTheExpectedAndActualCounts()
    {
        // No matching dbo.UriSearchParam row is seeded, so the batch UPDATE's WHERE clause matches
        // nothing and 0 rows are affected -- simulating a race/ordering bug where the merge's row isn't
        // there (or isn't there yet) when the post-merge extension update runs.
        var extension = new UriSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 1000,
            SearchParamId: 1,
            Uri: "http://example.com/uri",
            Version: "1.0",
            Fragment: "section1");

        await _updater.UpdateUriSearchParamExtensionsAsync([extension], CancellationToken.None);

        _logger.Errors.ShouldContain(message =>
            message.Contains("UriSearchParam", StringComparison.Ordinal) &&
            message.Contains($"tenant {_database.TenantId}", StringComparison.Ordinal) &&
            message.Contains("MissedCount=1", StringComparison.Ordinal) &&
            message.Contains("TotalCount=1", StringComparison.Ordinal));
        _logger.Messages(LogLevel.Information).ShouldNotContain(
            message => message.Contains("Updated 1 UriSearchParam extension records", StringComparison.Ordinal));
    }
}
