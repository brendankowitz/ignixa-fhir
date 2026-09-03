using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Executes SQL against a specific tenant's database, with retry and structured logging. Every
/// method takes a <c>tenantId</c> because one running instance serves N independent tenant
/// databases (see design doc §4/§6). There is no <c>isReadOnly</c> parameter -- read-replica
/// routing is explicitly deferred (see design doc §4/§7).
/// </summary>
public interface ISqlExecutionService
{
    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database and reads
    /// every result row via <paramref name="readRow"/>. Opens and disposes its own connection.
    /// <paramref name="command"/>.Connection is overwritten by this call and must not be relied
    /// upon by the caller afterward.
    /// </summary>
    /// <param name="idempotency">
    /// Whether <paramref name="command"/> is safe for the transient-fault pipeline to execute more than
    /// once. Reads are; an <c>INSERT ... OUTPUT INSERTED</c> -- which comes through this method rather than
    /// <see cref="ExecuteNonQueryAsync"/> precisely because it needs the generated identity back -- is not.
    /// </param>
    Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent);

    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database as a
    /// non-query (INSERT/UPDATE/DELETE/DDL) and returns the affected row count.
    /// <paramref name="command"/>.Connection is overwritten by this call and must not be relied
    /// upon by the caller afterward. Transient SQL errors (including command timeouts) are
    /// retried by default; because a timeout does not guarantee the server did not already commit
    /// the statement, <paramref name="command"/> must be safe to execute more than once unless
    /// <paramref name="idempotency"/> says otherwise.
    /// </summary>
    /// <param name="idempotency">
    /// Whether <paramref name="command"/> is safe for the transient-fault pipeline to execute more than
    /// once. <see cref="SqlCommandIdempotency.NonIdempotent"/> makes a transient failure propagate
    /// immediately instead of being retried.
    /// </param>
    Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent);

    /// <summary>
    /// Runs <paramref name="work"/> against one connection inside one SQL transaction, committing when it
    /// returns and rolling back when it throws. Without this, every multi-statement operation in this layer
    /// is a sequence of independently auto-committed statements on independent connections, and a failure
    /// part-way leaves the earlier ones applied.
    /// </summary>
    /// <param name="work">
    /// The unit of work. It receives an <see cref="ISqlTransactionContext"/> to run its commands through --
    /// commands run any other way do not join the transaction. <b>It may be invoked more than once</b>: a
    /// transient fault before the commit rolls the transaction back and restarts the whole unit, which is
    /// what makes retrying it safe. It must therefore build its own commands and not depend on in-memory
    /// state a previous attempt mutated.
    /// </param>
    /// <exception cref="SqlTransactionCommitException">
    /// The commit itself failed, so whether the work was applied is unknown. This is never retried -- see
    /// that type for why.
    /// </exception>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken);

    /// <summary>
    /// The no-result overload of
    /// <see cref="ExecuteInTransactionAsync{TResult}(int, Func{ISqlTransactionContext, CancellationToken, Task{TResult}}, CancellationToken)"/>,
    /// for units of work whose whole point is their side effects.
    /// </summary>
    Task ExecuteInTransactionAsync(
        int tenantId,
        Func<ISqlTransactionContext, CancellationToken, Task> work,
        CancellationToken cancellationToken);
}
