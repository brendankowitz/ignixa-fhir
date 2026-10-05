using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// The one connection and one transaction that
/// <see cref="ISqlExecutionService.ExecuteInTransactionAsync{TResult}"/> lends to its unit of work. Every
/// command run through this enlists in that transaction, so the whole sequence commits or rolls back
/// together.
/// <para>
/// Nothing here retries: the transaction as a whole is the retry unit, and the execution service restarts it
/// from the top after a rollback. Retrying one command inside a live transaction would leave the earlier
/// statements applied twice within it.
/// </para>
/// <para>
/// The instance is valid only for the duration of the callback it was passed to. Its connection and
/// transaction are disposed when the callback returns, so it must not be captured and used afterwards.
/// </para>
/// </summary>
public interface ISqlTransactionContext
{
    /// <summary>
    /// Executes <paramref name="command"/> inside the transaction and returns the affected row count.
    /// <paramref name="command"/>'s <c>Connection</c> and <c>Transaction</c> are overwritten by this call.
    /// </summary>
    Task<int> ExecuteNonQueryAsync(SqlCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Executes <paramref name="command"/> inside the transaction and reads every result row via
    /// <paramref name="readRow"/>. <paramref name="command"/>'s <c>Connection</c> and <c>Transaction</c> are
    /// overwritten by this call.
    /// </summary>
    Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken);
}
