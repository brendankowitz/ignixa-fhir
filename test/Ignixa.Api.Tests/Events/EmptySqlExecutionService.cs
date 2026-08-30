using Ignixa.DataLayer.SqlServer;
using Microsoft.Data.SqlClient;

namespace Ignixa.Api.Tests.Events;

/// <summary>
/// Answers every query with no rows, so the reference-data cache builds and preloads without a database.
/// The counterpart to <see cref="ThrowingSqlExecutionService"/>: this one lets the happy path run to
/// completion, that one proves a failure is not swallowed.
/// </summary>
internal sealed class EmptySqlExecutionService : ISqlExecutionService
{
    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TResult>>([]);

    public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken, bool disableRetries = false)
        => Task.FromResult(0);
}
