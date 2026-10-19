using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;

/// <summary>
/// Passes every call through to a real <see cref="ISqlExecutionService"/> and records, per compiled search
/// statement, how many include rows (<c>IsMatch = 0</c>) SQL Server returned. Each such row is a resource
/// the search service then fetches and decompresses, so this is the cost an include cap must bound.
/// </summary>
internal sealed class IncludeRowCountingSqlExecutionService(ISqlExecutionService inner) : ISqlExecutionService
{
    private readonly List<int> _includeRowsPerStatement = [];

    /// <summary>Include rows read by each include-bearing statement since the last <see cref="Reset"/>.</summary>
    public IReadOnlyList<int> IncludeRowsPerStatement => _includeRowsPerStatement;

    public void Reset() => _includeRowsPerStatement.Clear();

    public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
    {
        if (!command.CommandText.Contains("AS IsMatch", StringComparison.Ordinal))
        {
            return await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
        }

        var includeRows = 0;
        var rows = await inner.ExecuteReaderAsync(
            tenantId,
            command,
            reader =>
            {
                if (!reader.GetBoolean(reader.GetOrdinal("IsMatch")))
                {
                    includeRows++;
                }

                return readRow(reader);
            },
            cancellationToken,
            idempotency);

        _includeRowsPerStatement.Add(includeRows);
        return rows;
    }

    public Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
        => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);

    public Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken)
        => inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
}
