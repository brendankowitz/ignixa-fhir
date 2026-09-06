using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.Tests;

internal sealed class InspectingLastNSqlExecutionService(
    Action<int, SqlCommand, CancellationToken> inspect) : ISqlExecutionService
{
    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
    {
        inspect(tenantId, command, cancellationToken);
        return Task.FromResult<IReadOnlyList<TResult>>([]);
    }

    public Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        bool disableRetries = false)
        => throw new NotSupportedException("Lastn must not execute writes.");
}
