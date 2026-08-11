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
    Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database as a
    /// non-query (INSERT/UPDATE/DELETE/DDL) and returns the affected row count.
    /// <paramref name="command"/>.Connection is overwritten by this call and must not be relied
    /// upon by the caller afterward. Transient SQL errors (including command timeouts) are
    /// retried by default; because a timeout does not guarantee the server did not already commit
    /// the statement, <paramref name="command"/> must be safe to execute more than once
    /// (idempotent) unless <paramref name="disableRetries"/> is set.
    /// </summary>
    /// <param name="disableRetries">
    /// When <c>true</c>, disables the transient-fault retry pipeline for this call. Set this for
    /// commands whose side effects are not safe to execute more than once and that the caller
    /// hasn't made idempotent (e.g. via an idempotency key); a transient failure then propagates
    /// immediately instead of being retried.
    /// </param>
    Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken,
        bool disableRetries = false);
}
