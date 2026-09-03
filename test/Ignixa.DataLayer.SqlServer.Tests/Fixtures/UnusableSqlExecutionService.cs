using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.Tests.Fixtures;

/// <summary>
/// An <see cref="ISqlExecutionService"/> that throws on every call, turning "this code path must not touch
/// the database" into an assertion rather than a comment. There is no usable fake for this interface in the
/// repo -- every other test against it uses a real scratch database -- so this is deliberately not one: it
/// exists only for short-circuit paths that must return before any SQL is issued.
/// </summary>
public sealed class UnusableSqlExecutionService : ISqlExecutionService
{
    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => throw Unexpected(command);

    public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        => throw Unexpected(command);

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException(
            "The code under test was expected to short-circuit before issuing SQL, but opened a transaction.");

    public Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException(
            "The code under test was expected to short-circuit before issuing SQL, but opened a transaction.");

    private static InvalidOperationException Unexpected(SqlCommand command)
        => new($"The code under test was expected to short-circuit before issuing SQL, but ran: {command.CommandText}");
}
