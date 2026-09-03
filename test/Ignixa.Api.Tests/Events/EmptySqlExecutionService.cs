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
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => Task.FromResult<IReadOnlyList<TResult>>([]);

    public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => Task.FromResult(0);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("No test using this fixture runs a transaction.");

    public Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("No test using this fixture runs a transaction.");
}
