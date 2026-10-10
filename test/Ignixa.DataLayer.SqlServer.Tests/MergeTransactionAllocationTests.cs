using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class MergeTransactionAllocationTests
{
    [Fact]
    public void GivenTheSqlRepository_WhenInspectingWriteOverloads_ThenNoZeroStampCompatibilityOverloadExists()
    {
        typeof(SqlServerFhirRepository).GetMethods()
            .Count(method => method.Name == nameof(SqlServerFhirRepository.GetNextTransactionIdAsync))
            .ShouldBe(1);
        typeof(SqlServerFhirRepository).GetMethods()
            .Count(method => method.Name == nameof(SqlServerFhirRepository.DeleteAsync))
            .ShouldBe(1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(80000)]
    [InlineData(int.MaxValue)]
    public async Task GivenAnUnsafeAllocationSize_WhenBeginningTransaction_ThenRejectsBeforeCallingSql(int resourceCount)
    {
        var sql = Substitute.For<ISqlExecutionService>();
        var allocations = 0;
        sql.ExecuteReaderAsync(
                1,
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, long>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SqlCommandIdempotency>())
            .Returns(call =>
            {
                allocations++;
                var command = (SqlCommand)call[1];
                command.Parameters["@TransactionId"].Value = 123L;
                command.Parameters["@SequenceRangeFirstValue"].Value = 0;
                return (IReadOnlyList<long>)[0];
            });
        using var cache = new SqlServerSearchIndexReferenceDataCache(sql, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var repository = new SqlServerMergeRepository(sql, 1,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()), cache,
            new SqlServerPostMergeExtensionUpdater(sql, 1, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => repository.BeginTransactionAsync(resourceCount, 0));
        allocations.ShouldBe(0);
    }

    [Fact]
    public async Task GivenAnAllocation_WhenResponseIsLost_ThenTheCallSiteDisablesReplayAndSurfacesTheFailure()
    {
        var sql = Substitute.For<ISqlExecutionService>();
        SqlCommandIdempotency? observed = null;
        var responseLost = new IOException("Allocation committed but response was lost.");
        sql.ExecuteReaderAsync(
                1,
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, long>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SqlCommandIdempotency>())
            .Returns(call =>
            {
                ((SqlCommand)call[1]).CommandText.ShouldContain("MergeResourcesBeginTransaction");
                observed = (SqlCommandIdempotency)call[4];
                return Task.FromException<IReadOnlyList<long>>(responseLost);
            });
        using var cache = new SqlServerSearchIndexReferenceDataCache(sql, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var repository = new SqlServerMergeRepository(sql, 1,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()), cache,
            new SqlServerPostMergeExtensionUpdater(sql, 1, NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);

        var exception = await Should.ThrowAsync<IOException>(() => repository.BeginTransactionAsync(5, 0));

        exception.ShouldBeSameAs(responseLost);
        observed.ShouldBe(SqlCommandIdempotency.NonIdempotent);
    }

    [Fact]
    public async Task GivenAStaleAllocationWhoseFailureCannotBeCompleted_WhenBeginningTransaction_ThenCompletionFailureEscapes()
    {
        var sql = Substitute.For<ISqlExecutionService>();
        var completionFailure = new IOException("Transaction completion failed.");
        sql.ExecuteReaderAsync(
                1,
                Arg.Any<SqlCommand>(),
                Arg.Any<Func<SqlDataReader, long>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SqlCommandIdempotency>())
            .Returns(call =>
            {
                var command = (SqlCommand)call[1];
                command.Parameters["@TransactionId"].Value = 123L;
                command.Parameters["@SequenceRangeFirstValue"].Value = 0;
                return (IReadOnlyList<long>)[29];
            });
        sql.ExecuteNonQueryAsync(
                1,
                Arg.Any<SqlCommand>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<SqlCommandIdempotency>())
            .Returns(Task.FromException<int>(completionFailure));
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql,
            1,
            NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        var repository = new SqlServerMergeRepository(
            sql,
            1,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()),
            cache,
            new SqlServerPostMergeExtensionUpdater(
                sql,
                1,
                NullLogger<SqlServerPostMergeExtensionUpdater>.Instance),
            NullLogger<SqlServerMergeRepository>.Instance);

        var exception = await Should.ThrowAsync<IOException>(() =>
            repository.BeginTransactionAsync(1, definitionsEventId: 11));

        exception.ShouldBeSameAs(completionFailure);
        exception.Data["Ignixa.StaleConformanceDefinitions"].ShouldBeOfType<StaleConformanceDefinitionsException>();
    }
}
