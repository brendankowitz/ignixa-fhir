using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class MergeTransactionAllocationTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(80000)]
    [InlineData(int.MaxValue)]
    public async Task GivenAnUnsafeAllocationSize_WhenBeginningTransaction_ThenRejectsBeforeCallingSql(int resourceCount)
    {
        var sql = Substitute.For<ISqlExecutionService>();
        var allocations = 0;
        sql.ExecuteNonQueryAsync(1, Arg.Any<SqlCommand>(), Arg.Any<CancellationToken>(), Arg.Any<SqlCommandIdempotency>())
            .Returns(call =>
            {
                allocations++;
                var command = (SqlCommand)call[1];
                command.Parameters["@TransactionId"].Value = 123L;
                command.Parameters["@SequenceRangeFirstValue"].Value = 0;
                return 1;
            });
        using var cache = new SqlServerSearchIndexReferenceDataCache(sql, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var repository = new SqlServerMergeRepository(sql, 1,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()), cache,
            new SqlServerPostMergeExtensionUpdater(sql, 1, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => repository.BeginTransactionAsync(resourceCount));
        allocations.ShouldBe(0);
    }

    [Fact]
    public async Task GivenAnAllocation_WhenResponseIsLost_ThenTheCallSiteDisablesReplayAndSurfacesTheFailure()
    {
        var sql = Substitute.For<ISqlExecutionService>();
        SqlCommandIdempotency? observed = null;
        var responseLost = new IOException("Allocation committed but response was lost.");
        sql.ExecuteNonQueryAsync(1, Arg.Any<SqlCommand>(), Arg.Any<CancellationToken>(), Arg.Any<SqlCommandIdempotency>())
            .Returns(call =>
            {
                ((SqlCommand)call[1]).CommandText.ShouldContain("MergeResourcesBeginTransaction");
                observed = (SqlCommandIdempotency)call[3];
                return Task.FromException<int>(responseLost);
            });
        using var cache = new SqlServerSearchIndexReferenceDataCache(sql, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var repository = new SqlServerMergeRepository(sql, 1,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()), cache,
            new SqlServerPostMergeExtensionUpdater(sql, 1, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);

        var exception = await Should.ThrowAsync<IOException>(() => repository.BeginTransactionAsync(5));

        exception.ShouldBeSameAs(responseLost);
        observed.ShouldBe(SqlCommandIdempotency.NonIdempotent);
    }
}
