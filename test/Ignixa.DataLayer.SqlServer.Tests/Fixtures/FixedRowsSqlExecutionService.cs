using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer.Tests.Fixtures;

/// <summary>
/// Answers every <see cref="ExecuteReaderAsync{TResult}"/> call with a fixed row set, regardless of the SQL
/// text, and counts how many times it was invoked. Used to warm a cache's positive entries without a real
/// database, so a cancellation test can prove the warm path returns before reaching this fixture at all --
/// asserting <see cref="CallCount"/> stays at the priming call's count is what actually distinguishes "the
/// guard fired" from "the guard is missing and the query happened to also throw."
/// </summary>
public sealed class FixedRowsSqlExecutionService<TRow>(params TRow[] rows) : ISqlExecutionService
{
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
    {
        CallCount++;

        if (typeof(TResult) != typeof(TRow))
        {
            throw new InvalidOperationException(
                $"This fixture was constructed for {typeof(TRow)} rows but was asked for {typeof(TResult)}.");
        }

        return Task.FromResult<IReadOnlyList<TResult>>(rows.Cast<TResult>().ToList());
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
