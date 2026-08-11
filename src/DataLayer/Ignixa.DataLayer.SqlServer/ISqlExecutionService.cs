using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Executes SQL against a specific tenant's database, with retry and structured logging. Mirrors
/// real fhir-server's ISqlRetryService shape (Microsoft.Health.Fhir.SqlServer.Features.Storage),
/// adapted for Ignixa's database-per-tenant multi-tenancy: fhir-server's version is bound to one
/// connection factory at startup (single-database-per-deployment); every method here takes a
/// tenantId, since one running instance serves N independent tenant databases (design doc §1/§6).
/// No isReadOnly parameter -- read-replica routing is explicitly deferred (design doc §4/§7).
/// </summary>
public interface ISqlExecutionService
{
    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database and reads
    /// every result row via <paramref name="readRow"/>. Opens and disposes its own connection.
    /// </summary>
    Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database as a
    /// non-query (INSERT/UPDATE/DELETE/DDL) and returns the affected row count.
    /// </summary>
    Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken);
}
