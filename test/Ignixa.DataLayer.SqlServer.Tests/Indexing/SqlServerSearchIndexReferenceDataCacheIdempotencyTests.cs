using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.Tests.Fixtures;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.SqlServer.Tests.Indexing;

/// <summary>
/// <c>GetOrCreateSystemIdAsync</c> and <c>GetOrCreateQuantityCodeIdAsync</c> both run an unguarded
/// <c>INSERT ... OUTPUT INSERTED</c> through <c>ExecuteReaderAsync</c> -- needed only because they must get
/// the generated identity back. A <c>-2</c> command timeout does not prove the server did not already commit
/// that insert, so retrying it risks a duplicate-key failure on a write that actually succeeded. This pins
/// the call site, not the mechanism -- that a <see cref="SqlCommandIdempotency.NonIdempotent"/> command
/// really does bypass the retry pipeline is already covered by <c>SqlExecutionServiceConnectionTests</c>.
/// Nothing else would notice this argument being dropped from either insert.
/// </summary>
public class SqlServerSearchIndexReferenceDataCacheIdempotencyTests
{
    [Fact]
    public async Task GivenASystemMissingFromTheCatalog_WhenItIsCreatedOnDemand_ThenOnlyTheInsertDeclaresItselfNonIdempotent()
    {
        var sql = new RecordingSqlExecutionService(insertedId: 7);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

        await cache.GetOrCreateSystemIdAsync("http://loinc.org", CancellationToken.None);

        var byKind = sql.ReaderCalls.ToLookup(call => call.CommandText.Contains("INSERT INTO dbo.System", StringComparison.Ordinal));
        byKind[true].ShouldHaveSingleItem().Idempotency.ShouldBe(SqlCommandIdempotency.NonIdempotent);
        byKind[false].ShouldNotBeEmpty();
        byKind[false].ShouldAllBe(call => call.Idempotency == SqlCommandIdempotency.Idempotent);
    }

    [Fact]
    public async Task GivenAQuantityCodeMissingFromTheCatalog_WhenItIsCreatedOnDemand_ThenOnlyTheInsertDeclaresItselfNonIdempotent()
    {
        var sql = new RecordingSqlExecutionService(insertedId: 9);
        using var cache = new SqlServerSearchIndexReferenceDataCache(
            sql, tenantId: 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);

        await cache.GetOrCreateQuantityCodeIdAsync("mg", CancellationToken.None);

        var byKind = sql.ReaderCalls.ToLookup(call => call.CommandText.Contains("INSERT INTO dbo.QuantityCode", StringComparison.Ordinal));
        byKind[true].ShouldHaveSingleItem().Idempotency.ShouldBe(SqlCommandIdempotency.NonIdempotent);
        byKind[false].ShouldNotBeEmpty();
        byKind[false].ShouldAllBe(call => call.Idempotency == SqlCommandIdempotency.Idempotent);
    }

    /// <summary>
    /// Answers the SELECT-then-INSERT shape <c>GetOrCreateSystemIdAsync</c>/<c>GetOrCreateQuantityCodeIdAsync</c>
    /// share -- the existence-check SELECT always comes back empty (the "missing from the catalog" case that
    /// reaches the insert), and the INSERT always yields <paramref name="insertedId"/> -- while recording
    /// every command's declared idempotency.
    /// </summary>
    private sealed class RecordingSqlExecutionService(int insertedId) : ISqlExecutionService
    {
        private readonly List<(string CommandText, SqlCommandIdempotency Idempotency)> _readerCalls = [];

        public IReadOnlyList<(string CommandText, SqlCommandIdempotency Idempotency)> ReaderCalls => _readerCalls;

        public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId,
            SqlCommand command,
            Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            ArgumentNullException.ThrowIfNull(command);
            _readerCalls.Add((command.CommandText, idempotency));

            var isSelect = command.CommandText.StartsWith("SELECT", StringComparison.Ordinal);
            IReadOnlyList<TResult> rows = isSelect
                ? []
                : [(TResult)(object)insertedId];

            return Task.FromResult(rows);
        }

        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
            => throw new NotSupportedException("This fixture only supports ExecuteReaderAsync.");

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("This fixture only supports ExecuteReaderAsync.");

        public Task ExecuteInTransactionAsync(
            int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("This fixture only supports ExecuteReaderAsync.");
    }
}
