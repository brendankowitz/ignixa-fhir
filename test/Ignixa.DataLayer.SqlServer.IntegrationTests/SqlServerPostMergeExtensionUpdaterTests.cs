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

        // The single UPDATE statement affected a row, so it is not counted as a miss: success is logged
        // at Information, not Error.
        _logger.Errors.ShouldBeEmpty();
        _logger.Messages(LogLevel.Information).ShouldContain(
            message => message.Contains("Updated 1 TokenSearchParam extension records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenATokenSearchParamRowThatDoesNotMatchTheUpdateKey_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenAnErrorIsLoggedWithMissedAndTotalCounts()
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
    public async Task GivenTwoIdenticalTokenSearchParamRowsMatchingOneExtension_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenTheOverMatchIsNotLoggedAsAnError()
    {
        // TokenSearchParam has no unique key (see the class comment on SqlServerPostMergeExtensionUpdater),
        // so seeding two rows with an identical (ResourceTypeId, ResourceSurrogateId, SearchParamId,
        // SystemId, Code) is a legal duplicate, not a test artifact. The one extension's UPDATE therefore
        // affects 2 rows -- an over-match -- which must not be treated as a miss.
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, 4000, 1, NULL, 'dup-code')");
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, 4000, 1, NULL, 'dup-code')");

        var extension = new TokenSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 4000,
            SearchParamId: 1,
            SystemId: null,
            Code: "dup-code",
            IdentifierTypeSystemId: 42,
            IdentifierTypeCode: "MR");

        await _updater.UpdateTokenSearchParamExtensionsAsync([extension], CancellationToken.None);

        // The over-match affected rows (not zero), so it is not counted as a miss: success is logged at
        // Information, not Error.
        _logger.Errors.ShouldBeEmpty();
        _logger.Messages(LogLevel.Information).ShouldContain(
            message => message.Contains("Updated 1 TokenSearchParam extension records", StringComparison.Ordinal));

        var updatedCount = await _database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.TokenSearchParam WHERE ResourceSurrogateId = 4000 AND IdentifierTypeCode = 'MR'");
        updatedCount.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnOverMatchedExtensionAndAGenuineMiss_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenTheMissIsNotHiddenBehindTheOverMatch()
    {
        // This is the regression scenario the @@ROWCOUNT miss-counting fix exists for. Under the old
        // summed-affected-rows check, the over-matched extension's UPDATE affecting 2 rows and the missed
        // extension's UPDATE affecting 0 rows would sum to 2 -- exactly extensionList.Count -- so the old
        // check saw "2 affected == 2 expected" and logged success, silently hiding the genuine miss. The
        // current per-statement miss count instead sees one UPDATE that affected zero rows and reports
        // MissedCount=1 regardless of how many rows the other UPDATE in the same batch over-matched.
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, 5000, 1, NULL, 'dup-code')");
        await _database.ExecuteNonQueryAsync(
            "INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code) VALUES (1, 5000, 1, NULL, 'dup-code')");
        // No row is seeded for ResourceSurrogateId 5001: that extension's UPDATE will affect zero rows.

        var overMatchedExtension = new TokenSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 5000,
            SearchParamId: 1,
            SystemId: null,
            Code: "dup-code",
            IdentifierTypeSystemId: 42,
            IdentifierTypeCode: "MR");
        var missedExtension = new TokenSearchParamExtensionData(
            ResourceTypeId: 1,
            ResourceSurrogateId: 5001,
            SearchParamId: 1,
            SystemId: null,
            Code: "missing-code",
            IdentifierTypeSystemId: 42,
            IdentifierTypeCode: "MR");

        await _updater.UpdateTokenSearchParamExtensionsAsync(
            [overMatchedExtension, missedExtension], CancellationToken.None);

        _logger.Errors.ShouldContain(message =>
            message.Contains("TokenSearchParam", StringComparison.Ordinal) &&
            message.Contains($"tenant {_database.TenantId}", StringComparison.Ordinal) &&
            message.Contains("MissedCount=1", StringComparison.Ordinal) &&
            message.Contains("TotalCount=2", StringComparison.Ordinal));
        _logger.Messages(LogLevel.Information).ShouldNotContain(
            message => message.Contains("Updated 2 TokenSearchParam extension records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenExtensionsSpanningTwoBatches_WhenUpdateTokenSearchParamExtensionsAsyncCalled_ThenMissesAreAccumulatedAcrossBatches()
    {
        // BatchSize is 100, so 101 extensions span two batches (100 + 1). Seed a matching row for every
        // extension except one in each batch -- index 50 (first batch) and index 100 (the lone second
        // batch). @Missed is genuinely scoped to a single batch by design (it is declared inside each
        // batch's own SQL text, so it always starts at 0 per batch); what this test actually pins down is
        // the accumulation across batches in the C# foreach loop -- totalMissed += missedRows[0] must be
        // used, not totalMissed = missedRows[0], or the final log would report only 1 miss (whichever
        // batch happened to run last) instead of the true total of 2.
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

        // The single UPDATE statement affected a row, so it is not counted as a miss: success is logged
        // at Information, not Error.
        _logger.Errors.ShouldBeEmpty();
        _logger.Messages(LogLevel.Information).ShouldContain(
            message => message.Contains("Updated 1 UriSearchParam extension records", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GivenAUriSearchParamRowThatDoesNotMatchTheUpdateKey_WhenUpdateUriSearchParamExtensionsAsyncCalled_ThenAnErrorIsLoggedWithMissedAndTotalCounts()
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
